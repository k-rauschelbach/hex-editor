namespace HexEditor.scripts;

using Godot;
using HexEditor.scripts.shared;
using System;
using System.Collections.Generic;

public class ChunkArrowManager
{
    private readonly Node3D _arrowsRoot;
    private readonly EditorChunkManager _chunkManager;
    private readonly VertexMap _vertexMap;
    private readonly EditorCamera _camera;
    private readonly int _chunkWidth;
    private readonly int _chunkHeight;
    private readonly float _tileSize;
    private ChunkArrow _hoveredArrow;

    // EditorMain sets this to rebuild handles/deselect after a chunk is added
    public Action OnChunksChanged { get; set; }

    public ChunkArrowManager(Node3D arrowsRoot, EditorChunkManager chunkManager,
        VertexMap vertexMap, EditorCamera camera, int chunkWidth, int chunkHeight, float tileSize)
    {
        _arrowsRoot = arrowsRoot;
        _chunkManager = chunkManager;
        _vertexMap = vertexMap;
        _camera = camera;
        _chunkWidth = chunkWidth;
        _chunkHeight = chunkHeight;
        _tileSize = tileSize;
    }

    public void SpawnArrows()
    {
        // clear any existing arrows
        foreach (Node child in _arrowsRoot.GetChildren()) child.Free();

        // clear hovered arrow as it doesn't exist anymore
        _hoveredArrow = null;

        var directions = new (EditorChunkManager.ChunkDirection dir, Vector2I offset)[]
        {
            (EditorChunkManager.ChunkDirection.North, new Vector2I(0, -1)),
            (EditorChunkManager.ChunkDirection.South, new Vector2I(0, 1)),
            (EditorChunkManager.ChunkDirection.East, new Vector2I(1, 0)),
            (EditorChunkManager.ChunkDirection.West, new Vector2I(-1, 0))
        };

        foreach (Vector2I coord in _chunkManager.AllChunks.Keys)
        {
            foreach (var (dir, offset) in directions)
            {
                Vector2I neighborCoord = coord + offset;

                // if neighbor chunk exists, skip
                if (_chunkManager.GetChunkData(neighborCoord) != null) continue;

                // Get world location for arrow
                Vector3 worldPos = GetChunkEdgeCenter(coord, dir);

                // Chevron points toward -Z, rotate around Y to orient correctly
                float yRotation = dir switch
                {
                    EditorChunkManager.ChunkDirection.North => 0,
                    EditorChunkManager.ChunkDirection.South => Mathf.Pi,
                    EditorChunkManager.ChunkDirection.East => -Mathf.Pi / 2,
                    EditorChunkManager.ChunkDirection.West => Mathf.Pi / 2,
                    _ => 0f
                };

                var arrow = new ChunkArrow();
                arrow.Initialize(coord, dir, worldPos, yRotation);
                _arrowsRoot.AddChild(arrow);
            }
        }
    }

    // Add chunk after clicking on an arrow
    public void AddChunkFromArrow(ChunkArrow arrow)
    {
        _chunkManager.AddAdjacentChunk(arrow.ChunkCoord, arrow.Direction);

        // Get new chunk's coordinate
        Vector2I offset = DirectionToOffset(arrow.Direction);
        Vector2I newCoord = arrow.ChunkCoord + offset;

        // Rebuild Vertex Map, sync edges, respawn arrows
        _vertexMap.Rebuild();
        _chunkManager.SyncNewChunkEdges(newCoord, _vertexMap);
        SpawnArrows();

        OnChunksChanged?.Invoke();
    }

    // Add a chunk in a given direction from the first loaded chunk that has an empty neighbor
    public void AddChunkInDirection(EditorChunkManager.ChunkDirection direction)
    {
        Vector2I offset = DirectionToOffset(direction);

        foreach (var coord in _chunkManager.AllChunks.Keys)
        {
            Vector2I neighborCoord = coord + offset;
            if (_chunkManager.GetChunkData(neighborCoord) == null)
            {
                _chunkManager.AddAdjacentChunk(coord, direction);

                // rebuild vertex map
                _vertexMap.Rebuild();
                // Sync edges with new chunk
                _chunkManager.SyncNewChunkEdges(neighborCoord, _vertexMap);

                OnChunksChanged?.Invoke();
                return;
            }
        }
        GD.Print("No empty space found to add chunk.");
    }

    public void UpdateHover(PhysicsDirectSpaceState3D spaceState)
    {
        Camera3D camera = _camera.Camera;
        Vector2 mousePos = camera.GetViewport().GetMousePosition();
        Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = camera.ProjectRayNormal(mousePos);
        Vector3 rayEnd = rayOrigin + rayDir * 1000f;
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
        query.CollisionMask = 4; // Layer 4 = chunk arrows only

        var result = spaceState.IntersectRay(query);

        ChunkArrow hitArrow = null;
        if (result.Count > 0)
        {
            Node3D collider = (Node3D)result["collider"];
            hitArrow = collider.GetParent<ChunkArrow>();
        }

        // Un-highlight the old arrow if we moved away from it
        if (_hoveredArrow != null && _hoveredArrow != hitArrow)
        {
            _hoveredArrow.SetHighlighted(false);
        }

        // Highlight the new arrow
        if (hitArrow != null)
        {
            hitArrow.SetHighlighted(true);
        }

        _hoveredArrow = hitArrow;
    }

    // Check if a clicked collider is an arrow, and if so handle it
    // Returns true if an arrow was clicked
    public bool TryHandleArrowClick(PhysicsDirectSpaceState3D spaceState, Vector3 rayOrigin, Vector3 rayEnd)
    {
        var arrowQuery = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
        arrowQuery.CollisionMask = 4;
        var arrowResult = spaceState.IntersectRay(arrowQuery);
        if (arrowResult.Count > 0)
        {
            Node3D collider = (Node3D)arrowResult["collider"];
            ChunkArrow arrow = collider.GetParent<ChunkArrow>();
            if (arrow != null)
            {
                AddChunkFromArrow(arrow);
                return true;
            }
        }
        return false;
    }

    private Vector3 GetChunkEdgeCenter(Vector2I chunkCoord, EditorChunkManager.ChunkDirection dir)
    {
        int cx = chunkCoord.X;
        int cy = chunkCoord.Y;

        int midQ, midR;

        switch (dir)
        {
            case EditorChunkManager.ChunkDirection.North:
                midQ = cx * _chunkWidth + _chunkWidth / 2;
                midR = cy * _chunkHeight;
                break;
            case EditorChunkManager.ChunkDirection.South:
                midQ = cx * _chunkWidth + _chunkWidth / 2;
                midR = cy * _chunkHeight + _chunkHeight - 1;
                break;
            case EditorChunkManager.ChunkDirection.East:
                midQ = cx * _chunkWidth + _chunkWidth - 1;
                midR = cy * _chunkHeight + _chunkHeight / 2;
                break;
            case EditorChunkManager.ChunkDirection.West:
                midQ = cx * _chunkWidth;
                midR = cy * _chunkHeight + _chunkHeight / 2;
                break;
            default:
                midQ = cx * _chunkWidth + _chunkWidth / 2;
                midR = cy * _chunkHeight + _chunkHeight / 2;
                break;
        }

        // Convert to world coords
        Vector3 edgeCenter = HexAxialMath.AxialToWorld(new HexAxial(midQ, midR), _tileSize);

        // Nudge outward
        float nudge = _tileSize * 2f;

        Vector3 nudgeOffset = dir switch
        {
            EditorChunkManager.ChunkDirection.North => new Vector3(0f, 0f, -nudge),
            EditorChunkManager.ChunkDirection.South => new Vector3(0f, 0f, nudge),
            EditorChunkManager.ChunkDirection.East => new Vector3(nudge, 0f, 0f),
            EditorChunkManager.ChunkDirection.West => new Vector3(-nudge, 0f, 0f),
            _ => Vector3.Zero
        };

        return edgeCenter + nudgeOffset;
    }

    private static Vector2I DirectionToOffset(EditorChunkManager.ChunkDirection direction)
    {
        return direction switch
        {
            EditorChunkManager.ChunkDirection.North => new Vector2I(0, -1),
            EditorChunkManager.ChunkDirection.South => new Vector2I(0, 1),
            EditorChunkManager.ChunkDirection.East => new Vector2I(1, 0),
            EditorChunkManager.ChunkDirection.West => new Vector2I(-1, 0),
            _ => Vector2I.Zero
        };
    }
}
