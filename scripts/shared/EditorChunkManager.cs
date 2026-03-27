namespace HexEditor.scripts.shared;

using Godot;
using System.Collections.Generic;

// Data authority for terrain data plus chunk-level baked editor visuals.
public class EditorChunkManager
{
    private sealed class ChunkVisual
    {
        public Node3D Root;
        public MeshInstance3D TerrainMesh;
        public MeshInstance3D OverlayMesh;
        public StaticBody3D Body;
        public CollisionShape3D CollisionShape;
    }

    // --- Storage --- //

    private readonly Dictionary<Vector2I, ChunkData> _chunks = new();
    private readonly Dictionary<Vector2I, ChunkVisual> _chunkVisuals = new();

    // --- Configuration --- //

    private readonly int _chunkWidth;
    private readonly int _chunkHeight;
    private readonly float _tileSize;
    private readonly Node3D _chunksRoot;

    public Dictionary<Vector2I, ChunkData> ChunkDataDictionary => _chunks;

    public EditorChunkManager(Node3D chunksRoot, int chunkWidth, int chunkHeight, float tileSize)
    {
        _chunksRoot = chunksRoot;
        _chunkWidth = chunkWidth;
        _chunkHeight = chunkHeight;
        _tileSize = tileSize;
    }

    // --- Walkability overlay --- //

    private static StandardMaterial3D _unwalkableMaterial;
    public float MaxVertexOffset { get; set; } = 0.5f;
    public float MaxStepHeight { get; set; } = 1.0f;
    public bool ShowWalkabilityOverlay { get; set; } = true;

    // --- Chunk Access --- //

    public ChunkData GetChunkData(Vector2I coord) => _chunks.TryGetValue(coord, out var data) ? data : null;

    public IReadOnlyDictionary<Vector2I, ChunkData> AllChunks => _chunks;

    public void CreateFlatChunk(int cx, int cy)
    {
        Vector2I coord = new Vector2I(cx, cy);
        if (_chunks.ContainsKey(coord))
        {
            GD.PushWarning($"Chunk at {coord} already exists.");
            return;
        }

        int totalVertices = _chunkWidth * _chunkHeight * ChunkData.VerticesPerTile;
        ChunkData data = new ChunkData
        {
            ChunkX = cx,
            ChunkY = cy,
            Version = ChunkData.CurrentVersion,
            Width = (byte)_chunkWidth,
            Height = (byte)_chunkHeight,
            VertexHeights = new float[totalVertices]
        };
        data.EnsureTileSurfaceIds();
        _chunks[coord] = data;

        CreateChunkVisual(coord);
        RebuildChunk(coord, updateCollision: true);
    }

    public void RebuildChunk(Vector2I coord, bool updateCollision = true)
    {
        if (!_chunks.TryGetValue(coord, out ChunkData data))
            return;
        if (!_chunkVisuals.TryGetValue(coord, out ChunkVisual visual))
            return;

        ArrayMesh terrainMesh = ChunkMeshBuilder.BuildChunkMesh(data, _chunkWidth, _chunkHeight, _tileSize);
        visual.TerrainMesh.Mesh = terrainMesh;

        if (updateCollision)
        {
            visual.CollisionShape.Shape = terrainMesh?.CreateTrimeshShape();
        }

        RebuildWalkabilityOverlay(coord);
    }

    public void RebuildChunks(IEnumerable<Vector2I> chunkCoords, bool updateCollision = true)
    {
        var rebuilt = new HashSet<Vector2I>();
        foreach (Vector2I coord in chunkCoords)
        {
            if (!rebuilt.Add(coord))
                continue;

            RebuildChunk(coord, updateCollision);
        }
    }

    public HashSet<Vector2I> GetAffectedChunkCoords(IEnumerable<(Vector2I chunk, int dq, int dr)> dirtyTiles)
    {
        HashSet<Vector2I> affected = new();

        foreach (var (chunkCoord, dq, dr) in dirtyTiles)
        {
            if (_chunks.ContainsKey(chunkCoord))
            {
                affected.Add(chunkCoord);
            }

            AddLoadedNeighborChunksForTile(chunkCoord, dq, dr, affected);
        }

        return affected;
    }

    public void SetTileSurface(Vector2I chunkCoord, int dq, int dr, TileSurfaceType surfaceType)
    {
        if (!_chunks.TryGetValue(chunkCoord, out ChunkData data))
            return;

        if (data.GetSurfaceType(dq, dr) == surfaceType)
            return;

        data.SetSurfaceType(dq, dr, surfaceType);
        RebuildChunk(chunkCoord, updateCollision: true);
    }

    // --- Save / Load --- //

    public void SaveAllChunks(string directory)
    {
        if (!directory.EndsWith("/"))
            directory += "/";

        DirAccess.MakeDirRecursiveAbsolute(directory);

        foreach (var (coord, data) in _chunks)
        {
            string fileName = ChunkDataWriter.GetChunkFileName(coord.X, coord.Y);
            string filePath = directory + fileName;
            ChunkDataWriter.WriteChunk(data, filePath);
        }

        GD.Print($"{_chunks.Count} chunks saved to {directory}.");
    }

    public List<Vector2I> ParseChunksFromDirectory(string directory)
    {
        if (!directory.EndsWith("/"))
            directory += "/";

        _chunks.Clear();
        foreach (var (_, visual) in _chunkVisuals)
            visual.Root.Free();
        _chunkVisuals.Clear();

        var parsedCoords = new List<Vector2I>();

        var dir = DirAccess.Open(directory);
        if (dir == null)
        {
            GD.PushError($"Cannot open directory: {directory}");
            return parsedCoords;
        }

        var loader = new ChunkDataLoader(directory);

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (fileName.StartsWith("chunk_") && fileName.EndsWith(".dat"))
            {
                Vector2I? coord = ParseChunkFileName(fileName);
                if (coord.HasValue)
                {
                    ChunkData data = loader.LoadChunkData(coord.Value);
                    if (data != null)
                    {
                        _chunks[coord.Value] = data;
                        parsedCoords.Add(coord.Value);
                    }
                }
            }

            fileName = dir.GetNext();
        }
        dir.ListDirEnd();

        GD.Print($"Parsed {parsedCoords.Count} chunks from {directory}.");
        return parsedCoords;
    }

    public void GenerateChunkScene(Vector2I coord)
    {
        if (!_chunks.ContainsKey(coord))
            return;

        CreateChunkVisual(coord);
        RebuildChunk(coord, updateCollision: true);
    }

    private static Vector2I? ParseChunkFileName(string fileName)
    {
        string stripped = fileName.Replace("chunk_", "").Replace(".dat", "");
        string[] parts = stripped.Split('_');
        if (parts.Length != 2)
            return null;

        if (!TryParseCoord(parts[0], out int x) || !TryParseCoord(parts[1], out int y))
            return null;

        return new Vector2I(x, y);
    }

    private static bool TryParseCoord(string s, out int value)
    {
        value = 0;
        if (s.StartsWith("n"))
        {
            if (int.TryParse(s.Substring(1), out int v))
            {
                value = -v;
                return true;
            }

            return false;
        }

        return int.TryParse(s, out value);
    }

    // --- Adjacent Chunk Management --- //

    public enum ChunkDirection
    {
        North,
        South,
        East,
        West
    }

    private static readonly Dictionary<ChunkDirection, Vector2I> ChunkDirOffsets = new()
    {
        { ChunkDirection.North, new Vector2I(0, -1) },
        { ChunkDirection.South, new Vector2I(0, 1) },
        { ChunkDirection.East, new Vector2I(1, 0) },
        { ChunkDirection.West, new Vector2I(-1, 0) }
    };

    public void AddAdjacentChunk(Vector2I referenceCoord, ChunkDirection direction)
    {
        Vector2I offset = ChunkDirOffsets[direction];
        Vector2I newCoord = referenceCoord + offset;

        if (_chunks.ContainsKey(newCoord))
        {
            GD.PushWarning($"Chunk at {newCoord} already exists.");
            return;
        }

        CreateFlatChunk(newCoord.X, newCoord.Y);
    }

    public void SyncNewChunkEdges(Vector2I newChunkCoord, VertexMap vertexMap)
    {
        ChunkData newData = _chunks[newChunkCoord];

        for (int dq = 0; dq < _chunkWidth; dq++)
        {
            for (int dr = 0; dr < _chunkHeight; dr++)
            {
                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                {
                    var loc = new VertexMap.VertexLocation
                    {
                        ChunkCoord = newChunkCoord,
                        Dq = dq,
                        Dr = dr,
                        VertexIndex = v
                    };

                    int groupId = vertexMap.GetGroupId(loc);
                    if (groupId < 0)
                        continue;

                    var group = vertexMap.GetGroup(groupId);
                    foreach (var otherLoc in group)
                    {
                        if (otherLoc.ChunkCoord == newChunkCoord)
                            continue;

                        var otherData = _chunks[otherLoc.ChunkCoord];
                        int otherIndex = otherData.GetVertexStartIndex(otherLoc.Dq, otherLoc.Dr) + otherLoc.VertexIndex;
                        float existingHeight = otherData.VertexHeights[otherIndex];
                        int ourIndex = newData.GetVertexStartIndex(dq, dr) + v;
                        newData.VertexHeights[ourIndex] = existingHeight;
                        break;
                    }
                }
            }
        }

        HashSet<Vector2I> affected = GetChunkWithLoadedNeighbors(newChunkCoord);
        RebuildChunks(affected, updateCollision: true);
    }

    // --- Walkability --- //

    private static StandardMaterial3D GetUnwalkableMaterial()
    {
        if (_unwalkableMaterial == null)
        {
            _unwalkableMaterial = new StandardMaterial3D();
            _unwalkableMaterial.AlbedoColor = new Color(1.0f, 0.2f, 0.2f, 0.45f);
            _unwalkableMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            _unwalkableMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            _unwalkableMaterial.NoDepthTest = true;
            _unwalkableMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        return _unwalkableMaterial;
    }

    public float[] GetNeighborAverages(Vector2I chunkCoord, int dq, int dr)
    {
        var avgs = new List<float>(6);
        int globalQ = chunkCoord.X * _chunkWidth + dq;
        int globalR = chunkCoord.Y * _chunkHeight + dr;

        for (int dir = 0; dir < 6; dir++)
        {
            var offset = HexAxialMath.Directions[dir];
            int nGlobalQ = globalQ + offset.Q;
            int nGlobalR = globalR + offset.R;

            int nChunkX = Mathf.FloorToInt((float)nGlobalQ / _chunkWidth);
            int nChunkY = Mathf.FloorToInt((float)nGlobalR / _chunkHeight);
            int nDq = ((nGlobalQ % _chunkWidth) + _chunkWidth) % _chunkWidth;
            int nDr = ((nGlobalR % _chunkHeight) + _chunkHeight) % _chunkHeight;

            Vector2I nChunkCoord = new Vector2I(nChunkX, nChunkY);
            if (_chunks.TryGetValue(nChunkCoord, out ChunkData neighborData))
            {
                float[] nHeights = new float[ChunkData.VerticesPerTile];
                neighborData.GetVertexHeightsNonAlloc(nDq, nDr, nHeights);
                avgs.Add(WalkabilityChecker.ComputeTileAverage(nHeights));
            }
        }

        return avgs.ToArray();
    }

    public void UpdateWalkabilityTint(Vector2I chunkCoord, int dq, int dr)
    {
        RebuildWalkabilityOverlays(GetAffectedChunkCoords(new[] { (chunkCoord, dq, dr) }));
    }

    public void UpdateAllWalkabilityTints()
    {
        RebuildWalkabilityOverlays(_chunks.Keys);
    }

    public void UpdateWalkabilityTintWithNeighbors(Vector2I chunkCoord, int dq, int dr)
    {
        RebuildWalkabilityOverlays(GetAffectedChunkCoords(new[] { (chunkCoord, dq, dr) }));
    }

    public bool TryGetChunkData(Vector2I chunkCoord, out ChunkData data) => _chunks.TryGetValue(chunkCoord, out data);

    private void CreateChunkVisual(Vector2I coord)
    {
        if (_chunkVisuals.TryGetValue(coord, out ChunkVisual existing))
        {
            existing.Root.Free();
            _chunkVisuals.Remove(coord);
        }

        Node3D root = new Node3D();
        string xStr = coord.X < 0 ? $"n{-coord.X}" : coord.X.ToString();
        string yStr = coord.Y < 0 ? $"n{-coord.Y}" : coord.Y.ToString();
        root.Name = $"Chunk_{xStr}_{yStr}";

        var terrainMesh = new MeshInstance3D();
        terrainMesh.Name = "TerrainMesh";
        root.AddChild(terrainMesh);

        var overlayMesh = new MeshInstance3D();
        overlayMesh.Name = "WalkabilityOverlay";
        overlayMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        overlayMesh.Visible = ShowWalkabilityOverlay;
        root.AddChild(overlayMesh);

        var body = new StaticBody3D();
        body.Name = "TerrainBody";
        body.CollisionLayer = 1;
        body.CollisionMask = 0;

        var collisionShape = new CollisionShape3D();
        collisionShape.Name = "TerrainCollision";
        body.AddChild(collisionShape);
        root.AddChild(body);

        _chunksRoot.AddChild(root);

        _chunkVisuals[coord] = new ChunkVisual
        {
            Root = root,
            TerrainMesh = terrainMesh,
            OverlayMesh = overlayMesh,
            Body = body,
            CollisionShape = collisionShape
        };
    }

    private void RebuildWalkabilityOverlay(Vector2I coord)
    {
        if (!_chunks.TryGetValue(coord, out ChunkData data))
            return;
        if (!_chunkVisuals.TryGetValue(coord, out ChunkVisual visual))
            return;

        ArrayMesh overlayMesh = ChunkMeshBuilder.BuildWalkabilityOverlayMesh(
            data,
            _chunkWidth,
            _chunkHeight,
            _tileSize,
            (dq, dr) => IsTileWalkable(coord, dq, dr),
            GetUnwalkableMaterial());

        visual.OverlayMesh.Mesh = overlayMesh;
        visual.OverlayMesh.Visible = ShowWalkabilityOverlay && overlayMesh != null;
    }

    private void RebuildWalkabilityOverlays(IEnumerable<Vector2I> chunkCoords)
    {
        HashSet<Vector2I> visited = new();
        foreach (Vector2I coord in chunkCoords)
        {
            if (!visited.Add(coord))
                continue;

            RebuildWalkabilityOverlay(coord);
        }
    }

    private bool IsTileWalkable(Vector2I chunkCoord, int dq, int dr)
    {
        if (!_chunks.TryGetValue(chunkCoord, out ChunkData data))
            return true;

        float[] heights = new float[ChunkData.VerticesPerTile];
        data.GetVertexHeightsNonAlloc(dq, dr, heights);

        float[] neighborAvgs = GetNeighborAverages(chunkCoord, dq, dr);
        return WalkabilityChecker.IsTileWalkable(heights, neighborAvgs, MaxStepHeight);
    }

    private void AddLoadedNeighborChunksForTile(Vector2I chunkCoord, int dq, int dr, HashSet<Vector2I> affected)
    {
        int globalQ = chunkCoord.X * _chunkWidth + dq;
        int globalR = chunkCoord.Y * _chunkHeight + dr;

        for (int dir = 0; dir < 6; dir++)
        {
            var offset = HexAxialMath.Directions[dir];
            int nGlobalQ = globalQ + offset.Q;
            int nGlobalR = globalR + offset.R;

            int nChunkX = Mathf.FloorToInt((float)nGlobalQ / _chunkWidth);
            int nChunkY = Mathf.FloorToInt((float)nGlobalR / _chunkHeight);
            Vector2I nChunkCoord = new Vector2I(nChunkX, nChunkY);

            if (_chunks.ContainsKey(nChunkCoord))
            {
                affected.Add(nChunkCoord);
            }
        }
    }

    private HashSet<Vector2I> GetChunkWithLoadedNeighbors(Vector2I chunkCoord)
    {
        HashSet<Vector2I> affected = new();
        if (_chunks.ContainsKey(chunkCoord))
        {
            affected.Add(chunkCoord);
        }

        int qStart = chunkCoord.X * _chunkWidth;
        int rStart = chunkCoord.Y * _chunkHeight;

        for (int dq = 0; dq < _chunkWidth; dq++)
        {
            AddLoadedNeighborChunksForTile(chunkCoord, dq, 0, affected);
            AddLoadedNeighborChunksForTile(chunkCoord, dq, _chunkHeight - 1, affected);
        }

        for (int dr = 0; dr < _chunkHeight; dr++)
        {
            AddLoadedNeighborChunksForTile(chunkCoord, 0, dr, affected);
            AddLoadedNeighborChunksForTile(chunkCoord, _chunkWidth - 1, dr, affected);
        }

        return affected;
    }
}
