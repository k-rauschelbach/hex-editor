namespace HexEditor.scripts;

using Godot;
using System.Collections.Generic;
using HexEditor.scripts.shared;

// Groups vertices that share the same geometric position across adjacent tiles.
//
// After accounting for the mesh generator's Pi/6 angle offset plus the HexTile scene's
// 30-degree Y rotation, each vertex's effective world-space angle is Pi/3 * i:
//   v0 = 0° (East), v1 = 60° (NE), v2 = 120° (NW),
//   v3 = 180° (West), v4 = 240° (SW), v5 = 300° (SE)
//
// Each hex corner is shared by exactly 3 tiles. Verified algebraically by computing
// world positions: for tile (Q,R), vertex v is at:
//   tileCenter + (cos(Pi/3*v) * tileSize, sin(Pi/3*v) * tileSize)
//
// Correct sharing table:
//   v0 (E):  shared with SE(+1,0) v4,  NE(+1,-1) v2
//   v1 (NE): shared with SE(+1,0) v3,  S(0,+1) v5
//   v2 (NW): shared with S(0,+1) v4,   SW(-1,+1) v0
//   v3 (W):  shared with SW(-1,+1) v5,  NW(-1,0) v1
//   v4 (SW): shared with NW(-1,0) v0,  N(0,-1) v2
//   v5 (SE): shared with N(0,-1) v1,   NE(+1,-1) v3
//
// Direction codes (matching HexAxialMath.Directions):
//   0: (+1,-1) NE    1: (0,-1) N     2: (-1,0) NW
//   3: (-1,+1) SW    4: (0,+1) S     5: (+1,0) SE

public class VertexMap
{
    // VertexLocation identifies one vertex height value in ChunkData 
    public struct VertexLocation
    {
        public Vector2I ChunkCoord; // Chunk Identifier
        public int Dq; // local Q position of tile
        public int Dr; // local R position of tile
        public int VertexIndex; // 0-5 vertex identifier
        
        // Equals/GetHashCode so VertexLocation can be a dictionary key
        public override bool Equals(object obj) =>
            obj is VertexLocation other &&
            ChunkCoord == other.ChunkCoord &&
            Dq == other.Dq &&
            Dr == other.Dr &&
            VertexIndex == other.VertexIndex;

        public override int GetHashCode() =>
            System.HashCode.Combine(ChunkCoord, Dq, Dr, VertexIndex);
    }
    
    // --- Sharing table --- //
    // Format: [VertexIndex] = (neighborDir1, VertexIndex1, neighborDir2, VertexIndex2)
    // Neighbor direction codes (HexAxialMath.Directions)
    // 0=NE, 1=N, 2=NW, 3=SW, 4=S, 5=SE
    private static readonly (int dir1, int v1, int dir2, int v2)[] SharingTable =
    {
        (5, 4, 0, 2), // v0
        (5, 3, 4, 5), // v1
        (4, 4, 3, 0), // v2
        (3, 5, 2, 1), // v3
        (2, 0, 1, 2), // v4
        (1, 1, 0, 3) // v5
    };
    
    // Hex neighbor offsets
    private static readonly (int dq, int dr)[] DirOffsets =
    {
        (1, -1), // 0 NE
        (0, -1), // 1 N
        (-1, 0), // 2 NW
        (-1, 1), // 3 SW
        (0, 1), // 4 S
        (1, 0) // 5 SE
    };
    
    // --- Data --- //
    
    // Map of each vertex location to its group ID
    private readonly Dictionary<VertexLocation, int> _locationToGroup = new();
    // Each group is a list of all VertexLocations that have the same geometric location
    private readonly List<List<VertexLocation>> _groups = new();
    // Reference to all Chunks data for read/write of heights
    private readonly Dictionary<Vector2I, ChunkData> _chunks = new();
    
    // Chunk dimensions
    private readonly int _chunkWidth;
    private readonly int _chunkHeight;
    
    public int GroupCount => _groups.Count;

    public VertexMap(Dictionary<Vector2I, ChunkData> chunks, int chunkWidth, int chunkHeight)
    {
        // Shared chunk dictionary with EditorChunkManager
        _chunks = chunks;
        _chunkWidth = chunkWidth;
        _chunkHeight = chunkHeight;
    }
    
    // Build the entire vertex map for all loaded chunks
    // Called after adding or removing a chunk
    public void Rebuild()
    {
        _locationToGroup.Clear();
        _groups.Clear();

        foreach (var (chunkCoord, chunkData) in _chunks)
        {
            int qStart = chunkData.ChunkX * _chunkWidth;
            int rStart = chunkData.ChunkY * _chunkHeight;

            for (int dq = 0; dq < _chunkWidth; dq++)
            {
                for (int dr = 0; dr < _chunkHeight; dr++)
                {
                    // global axial coordinates
                    int globalQ = qStart + dq;
                    int globalR = rStart + dr;

                    for (int v = 0; v < 6; v++)
                    {
                        var loc = new VertexLocation
                        {
                            ChunkCoord = chunkCoord,
                            Dq = dq,
                            Dr = dr,
                            VertexIndex = v
                        };
                        
                        // Check if vertex is already assigned to a group
                        if (_locationToGroup.ContainsKey(loc))
                            continue;
                        
                        // Create a new group for this vertex
                        var groupId = _groups.Count;
                        var group = new List<VertexLocation> { loc };
                        _locationToGroup[loc] = groupId;
                        
                        // Check neighbors that might share this vertex
                        var sharing = SharingTable[v];
                        TryAddNeighborToGroup(globalQ, globalR, sharing.dir1, sharing.v1, group, groupId);
                        TryAddNeighborToGroup(globalQ, globalR, sharing.dir2, sharing.v2, group, groupId);
                        
                        _groups.Add(group);
                    }
                }
            }
        }
    }
    
    // Check if a neighbor exists in our loaded chunks
    // Add valid neighbors to applicable vertex groups

    private void TryAddNeighborToGroup(int globalQ, int globalR, int dir, int neighborVertexIndex,
        List<VertexLocation> group, int groupId)
    {
        // Calculate the neighbor tile's global coordinates
        int nQ = globalQ + DirOffsets[dir].dq;
        int nR = globalR + DirOffsets[dir].dr;
        
        // Convert global coordinates to local coordinates + offset
        // Floor div to handle negative coordinate values
        int chunkX = FloorDiv(nQ, _chunkWidth);
        int chunkY = FloorDiv(nR, _chunkHeight);
        int localDq = nQ - chunkX * _chunkWidth;
        int localDr = nR - chunkY * _chunkHeight;

        Vector2I neighborChunkCoord = new Vector2I(chunkX, chunkY);
        
        // Only add if the chunk is actually loaded in the editor
        if (!_chunks.ContainsKey(neighborChunkCoord))
            return;

        var neighborLoc = new VertexLocation
        {
            ChunkCoord = neighborChunkCoord,
            Dq = localDq,
            Dr = localDr,
            VertexIndex = neighborVertexIndex
        };
        
        // Don't add if the neighbor is already in the group
        if (_locationToGroup.ContainsKey(neighborLoc))
            return;
        
        group.Add(neighborLoc);
        _locationToGroup[neighborLoc] = groupId;
    }
    
    // Integer floor division for handling negative coordinate values
    private static int FloorDiv(int a, int b)
    {
        return (a >= 0) ? a / b : (a - b + 1) / b;
    }
    
    // --- API ---
    
    // Get Group ID for a given vertex location
    public int GetGroupId(VertexLocation loc) =>
        _locationToGroup.TryGetValue(loc, out int id) ? id : -1;
    
    // Get all vertex locations in a group
    public List<VertexLocation> GetGroup(int groupId) => _groups[groupId];
    
    // Get the current height of a vertex group
    public float GetGroupHeight(int groupId)
    {
        var loc = _groups[groupId][0];
        var data = _chunks[loc.ChunkCoord];
        int index = (loc.Dq * data.Height + loc.Dr) * ChunkData.VerticesPerTile + loc.VertexIndex;
        return data.VertexHeights[index];
    }
    
    // Set the height of all vertices in a group
    public HashSet<(Vector2I chunk, int dq, int dr)> SetGroupHeight(int groupId, float height)
    {
        var affectedTiles = new HashSet<(Vector2I, int, int)>();

        foreach (var loc in _groups[groupId])
        {
            var data = _chunks[loc.ChunkCoord];
            int index = (loc.Dq * data.Height + loc.Dr) * ChunkData.VerticesPerTile + loc.VertexIndex;
            data.VertexHeights[index] = height;
            affectedTiles.Add((loc.ChunkCoord, loc.Dq, loc.Dr));
        }
        return affectedTiles;
    }
    
    // Iterate all groups
    public int GetGroupCount() => _groups.Count;


}