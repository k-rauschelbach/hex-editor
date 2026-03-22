namespace HexEditor.scripts.shared;

using Godot;
using System.Collections.Generic;
using HexEditor.scripts.shared;

// Data authority for terrain data
public class EditorChunkManager
{
    // --- Storage --- //
    
    // Map of chunk grid coordinates to its ChunkData
    private readonly Dictionary<Vector2I, ChunkData> _chunks = new();
    // Map of chunk grid coordinates to parent Node3D of tiles
    private readonly Dictionary<Vector2I, Node3D> _chunkNodes = new();
    
    // --- Configuration --- //

    private readonly int _chunkWidth;
    private readonly int _chunkHeight;
    private readonly float _tileSize;
    
    // Scene tree parent for adding child nodes
    private readonly Node3D _chunksRoot;
    
    // Expose dictionary to share access with VertexMap
    public Dictionary<Vector2I, ChunkData> ChunkDataDictionary => _chunks;

    public EditorChunkManager(Node3D chunksRoot, int chunkWidth, int chunkHeight, float tileSize)
    {
        _chunksRoot = chunksRoot;
        _chunkWidth = chunkWidth;
        _chunkHeight = chunkHeight;
        _tileSize = tileSize;
    }
    
    // --- Chunk Access --- //

    public ChunkData GetChunkData(Vector2I coord) => _chunks.TryGetValue(coord, out var data) ? data : null;

    public IReadOnlyDictionary<Vector2I, ChunkData> AllChunks => _chunks;
    
    // Create initial flat chunk
    public void CreateFlatChunk(int cx, int cy)
    {
        Vector2I coord = new Vector2I(cx, cy);
        
        // Check if chunk already exists
        if (_chunks.ContainsKey(coord))
        {
            GD.PushWarning($"Chunk at {coord} already exists.");
            return;
        }
        
        // Create the ChunkData Object
        int totalVertices = _chunkWidth * _chunkHeight * ChunkData.VerticesPerTile;
        ChunkData data = new ChunkData
        {
            ChunkX = cx,
            ChunkY = cy,
            Version = 1,
            Width = (byte)_chunkWidth,
            Height = (byte)_chunkHeight,
            VertexHeights = new float[totalVertices]
        };
        _chunks[coord] = data;
        
        // Create Nodes
        // Use 'n' prefix for negative coords (same convention as ChunkDataWriter)
        // to avoid '-' in node names which can cause Godot path-parsing issues
        Node3D chunkNode = new Node3D();
        string xStr = cx < 0 ? $"n{-cx}" : cx.ToString();
        string yStr = cy < 0 ? $"n{-cy}" : cy.ToString();
        chunkNode.Name = $"Chunk_{xStr}_{yStr}";
        _chunksRoot.AddChild(chunkNode);
        _chunkNodes[coord] = chunkNode;
        
        //Generate tile meshes
        GenerateAllTiles(coord);
    }

    // Generate all tile meshes for a chunk
    private void GenerateAllTiles(Vector2I coord)
    {
        ChunkData data = _chunks[coord];
        Node3D chunkNode = _chunkNodes[coord];
        
        // clear any existing children
        foreach (Node child in chunkNode.GetChildren())
        {
            child.Free();
        }
        
        // Buffer for vertices
        float[] vertexBuffer = new float[ChunkData.VerticesPerTile];
        float[] localHeightBuffer = new float[ChunkData.VerticesPerTile];
        
        // convert first tile coordinates from global axial to local offsets
        int qStart = data.ChunkX * _chunkWidth;
        int rStart = data.ChunkY * _chunkHeight;

        for (int dq = 0; dq < _chunkWidth; dq++)
        {
            for (int dr = 0; dr < _chunkHeight; dr++)
            {
                // get global axial coordinates
                HexAxial axial = new HexAxial(qStart + dq, rStart + dr);
                
                // get vertex data from ChunkData
                data.GetVertexHeightsNonAlloc(dq, dr, vertexBuffer);
                
                // Calculate average height for this tile
                float avgHeight = 0f;
                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                    avgHeight += vertexBuffer[v];
                avgHeight /= ChunkData.VerticesPerTile;
                
                // Convert to local heights
                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                    localHeightBuffer[v] = vertexBuffer[v] - avgHeight;
                
                // Convert axial to world position
                Vector3 worldPos = HexAxialMath.AxialToWorld(axial, _tileSize, avgHeight);
                
                // Generate Mesh
                var meshResult = HexMeshGenerator.GenerateTileMesh(localHeightBuffer, _tileSize);
                
                // Create tile node
                Node3D tileNode = new Node3D();
                tileNode.Name = $"Tile_{dq}_{dr}";
                
                // Rotate tile to be flat-top
                tileNode.RotationDegrees = new Vector3(0, 30, 0);
                tileNode.Position = worldPos;
                
                // Create MeshInstance3D
                MeshInstance3D meshInst = new MeshInstance3D();
                meshInst.Mesh = meshResult.Mesh;
                tileNode.AddChild(meshInst);
                
                // Create StaticBody3D and CollisionShape3D
                StaticBody3D body = new StaticBody3D();
                CollisionShape3D collisionShape = new CollisionShape3D();
                collisionShape.Shape = meshResult.CollisionShape;
                body.AddChild(collisionShape);
                tileNode.AddChild(body);
                
                // Store axial coord as metadata
                tileNode.SetMeta("dq", dq);
                tileNode.SetMeta("dr", dr);
                tileNode.SetMeta("chunk_x", data.ChunkX);
                tileNode.SetMeta("chunk_y", data.ChunkY);
                
                chunkNode.AddChild(tileNode);

            }
        }
    }
    
    // Regenerate mesh for single tile
    public void RegenerateTile(Vector2I chunkCoord, int dq, int dr)
    {
        // Check if chunk exists
        if (!_chunks.TryGetValue(chunkCoord, out ChunkData data)) return;
        if (!_chunkNodes.TryGetValue(chunkCoord, out Node3D chunkNode)) return;
        
        // Find existing tile nodes
        string tileName = $"Tile_{dq}_{dr}";
        Node3D tileNode = chunkNode.GetNode<Node3D>(tileName);
        if (tileNode == null) return;
        
        // read current vertex heights
        float[] vertexBuffer = new float[ChunkData.VerticesPerTile];
        float[] localHeightBuffer = new float[ChunkData.VerticesPerTile];
        data.GetVertexHeightsNonAlloc(dq, dr, vertexBuffer);
        
        // Recalculate average height
        float avgHeight = 0f;
        for (int v = 0; v < ChunkData.VerticesPerTile; v++)
            avgHeight += vertexBuffer[v];
        avgHeight /= ChunkData.VerticesPerTile;
        
        // Recalculate local heights
        for (int v = 0; v < ChunkData.VerticesPerTile; v++)
        {
            localHeightBuffer[v] = vertexBuffer[v] - avgHeight;
        }
        
        // Update tile position
        HexAxial axial = new HexAxial(data.ChunkX * _chunkWidth + dq, data.ChunkY * _chunkHeight + dr);
        tileNode.Position = HexAxialMath.AxialToWorld(axial, _tileSize, avgHeight);
        
        // Regenerate mesh
        var meshResult = HexMeshGenerator.GenerateTileMesh(localHeightBuffer, _tileSize);
        
        // Update MeshInstance3D
        MeshInstance3D meshInst = tileNode.GetChild<MeshInstance3D>(0);
        meshInst.Mesh = meshResult.Mesh;
        
        // Update CollisionShape3D
        var body = tileNode.GetChild<StaticBody3D>(1);
        // Make sure body exists
        if (body == null)
        {
            foreach (var child in tileNode.GetChildren())
            {
                if (child is StaticBody3D sb)
                {
                    body = sb;
                    break;
                }
            }
        }

        if (body != null)
        {
            var colShape = body.GetChild<CollisionShape3D>(0);
            if (colShape != null)
                colShape.Shape = meshResult.CollisionShape;
        }
    }
    
    // --- Save / Load --- //

    // Save chunks to world data directory
    public void SaveAllChunks(string directory)
    {
        // ensure directory path ends with '/'
        if (!directory.EndsWith("/")) directory += "/";
        
        // Ensure directory exists
        DirAccess.MakeDirRecursiveAbsolute(directory);

        foreach (var (coord, data) in _chunks)
        {
            string fileName = ChunkDataWriter.GetChunkFileName(coord.X, coord.Y);
            string filePath = directory + fileName;
            ChunkDataWriter.WriteChunk(data, filePath);
        }
        
        GD.Print($"{_chunks.Count} chunks saved to {directory}.");
    }
    
    // Load Chunks from directory
    // Loads ALL chunk data files in a given directory
    public bool LoadChunksFromDirectory(string directory)
    {
        // ensure directory path ends with '/'
        if (!directory.EndsWith("/")) directory += "/";
        
        // Clear any existing data and nodes
        _chunks.Clear();
        foreach (var (_, node) in _chunkNodes)
            node.QueueFree();
        _chunkNodes.Clear();
        
        // Scan Directory for chunk files
        var loader = new ChunkDataLoader(directory);

        var dir = DirAccess.Open(directory);
        if (dir == null)
        {
            GD.PushError($"Cannot open directory: {directory}");
            return false;
        }

        int loadedCount = 0;
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            // check that filename matches ChunkWriter convention (chunk_(n)#_(n)#.dat"
            if (fileName.StartsWith("chunk_") && fileName.EndsWith(".dat"))
            {
                // Parse file contents
                Vector2I? coord = ParseChunkFileName(fileName);
                if (coord.HasValue)
                {
                    ChunkData data = loader.LoadChunkData(coord.Value);
                    if (data != null)
                    {
                        _chunks[coord.Value] = data;
                        
                        // create visual node (n-prefix for negative coords)
                        Node3D chunkNode = new Node3D();
                        string lxStr = coord.Value.X < 0 ? $"n{-coord.Value.X}" : coord.Value.X.ToString();
                        string lyStr = coord.Value.Y < 0 ? $"n{-coord.Value.Y}" : coord.Value.Y.ToString();
                        chunkNode.Name = $"Chunk_{lxStr}_{lyStr}";
                        _chunksRoot.AddChild(chunkNode);
                        _chunkNodes[coord.Value] = chunkNode;
                        
                        GenerateAllTiles(coord.Value);
                        loadedCount++;
                    }
                }
            }

            fileName = dir.GetNext();
        }

        dir.ListDirEnd();

        GD.Print($"Loaded {loadedCount} chunks from {directory}.");
        return loadedCount > 0;
    }
    
    // parse "chunk_(n)#_(n)#.dat" for chunk coord
    private static Vector2I? ParseChunkFileName(string fileName)
    {
        // remove 'chunk' from name
        string stripped = fileName.Replace("chunk_", "").Replace(".dat", "");
        
        // split '_'
        string[] parts = stripped.Split('_');
        if (parts.Length != 2) return null;

        if (!TryParseCoord(parts[0], out int x) || !TryParseCoord(parts[1], out int y))
            return null;
        
        return new Vector2I(x, y);
    }
    
    // Parse string name result into integers
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
    
    //Direction offsets for chunk neighbors
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
    
    // Add a new adjacent chunk to one already loaded in editor
    // chunk is loaded flat, shared edge vertices match existing groups
    public void AddAdjacentChunk(Vector2I referenceCoord, ChunkDirection direction)
    {
        Vector2I offset = ChunkDirOffsets[direction];
        Vector2I newCoord = referenceCoord + offset;

        if (_chunks.ContainsKey(newCoord))
        {
            GD.PushWarning($"Chunk at {newCoord} already exists.");
            return;
        }
        
        // Create flat chunk
        CreateFlatChunk(newCoord.X, newCoord.Y);
    }
    
    // Sync new edges after VertexMap rebuilds
    public void SyncNewChunkEdges(Vector2I newChunkCoord, VertexMap vertexMap)
    {
        ChunkData newData = _chunks[newChunkCoord];
        
        // Iterate all vertices
        for (int dq = 0; dq < _chunkWidth; dq++)
        {
            for (int dr = 0; dr < _chunkHeight; dr++)
            {
                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                {
                    var loc = new VertexMap.VertexLocation()
                    {
                        ChunkCoord = newChunkCoord,
                        Dq = dq,
                        Dr = dr,
                        VertexIndex = v
                    };

                    int groupId = vertexMap.GetGroupId(loc);
                    if (groupId < 0) continue;
                    
                    // Check if any other vertex in this group belongs to a different chunk
                    var group = vertexMap.GetGroup(groupId);
                    foreach (var otherLoc in group)
                    {
                        if (otherLoc.ChunkCoord != newChunkCoord)
                        {
                            // Copy the existing chunk's height to the other chunk vertex
                            var otherData = _chunks[otherLoc.ChunkCoord];
                            int otherIndex = (otherLoc.Dq * otherData.Height + otherLoc.Dr) * ChunkData.VerticesPerTile + otherLoc.VertexIndex;
                            float existingHeight = otherData.VertexHeights[otherIndex];
                            int ourIndex = (dq * newData.Height + dr) * ChunkData.VerticesPerTile + v;
                            newData.VertexHeights[ourIndex] = existingHeight;
                            break;
                        }
                    }
                }
            }
        }
        
        GenerateAllTiles(newChunkCoord);
    }

}
