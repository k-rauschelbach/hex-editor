using System;

namespace HexEditor.scripts.shared;

// Holds deserialized terrain data for a single chunk

// Defines vertices using east-ccw format
// 0 = east 0
// 1 = northeast 60
// 2 = northwest 120
// 3 = west 180
// 4 = southwest 240
// 5 = southeast 300

public class ChunkData
{
    public const byte CurrentVersion = 2;

    // Chunk grid X Coordinate. Maps to Q in hexaxial
    // First tile in chunk has global Q = ChunkX * Width
    public int ChunkX;

    // Chunk grid Y coordinate. Maps to R in hexaxial
    // First tile in chunk has global R = ChunkY * Height
    public int ChunkY;

    // Version number of data format
    public byte Version;

    // Number of tiles across chunk top edge
    public byte Width;

    // Number of tiles across chunk side edge
    public byte Height;

    // ------ Tile Data ------

    // Flat array of all vertex heights for tiles in the chunk
    // LAYOUT
    // tile at location (0, 0) has indices [0..5]
    // tile at location (1, 0) has indices [Height*6..Height*6+5]

    public float[] VertexHeights;
    public const int VerticesPerTile = 6;

    // Per-tile surface identifier. Keep this as a compact byte array in save data;
    // actual materials/meshes should be looked up at runtime from this ID.
    public byte[] TileSurfaceIds;

    public int GetTileIndex(int dq, int dr)
    {
        return dq * Height + dr;
    }

    public int GetVertexStartIndex(int dq, int dr)
    {
        return GetTileIndex(dq, dr) * VerticesPerTile;
    }

    public void EnsureTileSurfaceIds(byte defaultSurface = (byte)TileSurfaceType.Grass)
    {
        int tileCount = Width * Height;

        if (TileSurfaceIds != null && TileSurfaceIds.Length == tileCount)
        {
            return;
        }

        TileSurfaceIds = new byte[tileCount];
        if (defaultSurface == 0)
        {
            return;
        }

        for (int i = 0; i < TileSurfaceIds.Length; i++)
        {
            TileSurfaceIds[i] = defaultSurface;
        }
    }

    public TileSurfaceType GetSurfaceType(int dq, int dr)
    {
        EnsureTileSurfaceIds();
        return (TileSurfaceType)TileSurfaceIds[GetTileIndex(dq, dr)];
    }

    public void SetSurfaceType(int dq, int dr, TileSurfaceType surfaceType)
    {
        EnsureTileSurfaceIds();
        TileSurfaceIds[GetTileIndex(dq, dr)] = (byte)surfaceType;
    }

    // Return the vertex heights for a tile given a local position (dq, dr)
    // Local position is relative to the specific chunk, not global
    public float[] getVertexHeights(int dq, int dr)
    {
        // Calculate where the tile data starts in the VertexHeights array based on position
        int startIndex = GetVertexStartIndex(dq, dr);

        float[] heights = new float[VerticesPerTile];
        Array.Copy(VertexHeights, startIndex, heights, 0, VerticesPerTile);

        return heights;
    }

    // Return the average height of all vertices for a given tile
    public float GetAverageHeight(int dq, int dr)
    {
        int startIndex = GetVertexStartIndex(dq, dr);
        float sum = 0f;
        for (int i = 0; i < VerticesPerTile; i++)
        {
            sum += VertexHeights[startIndex + i];
        }
        
        return sum / VerticesPerTile;
    }
    
    // Write vertex heights in an existing buffer to avoid allocations during generation
    public void GetVertexHeightsNonAlloc(int dq, int dr, float[] buffer)
    {
        int startIndex = GetVertexStartIndex(dq, dr);
        Array.Copy(VertexHeights, startIndex, buffer, 0, VerticesPerTile);
    }

}
