using Godot;

namespace HexEditor.scripts.shared;

// Serializes ChunkData object into a binary .dat file for concise storage
    
// Format Version 1
// Header -- 8 Bytes
// -- 0-3 HEXC Identifier
// -- 4 Version Number (1)
// -- 5 Width (16)
// -- 6 Height (16)
// -- 7 Flags (currently unused)
// Tile Data -- (Width * Height * 6 * 4) Bytes
// Tiles organized by dq(outer), dr(inner), 6 floats for vertex heights
    
// Naming convention:
// chunk_q_r.dat
// chunk_nq_nr.dat for negative values (- protected in windows)


public static class ChunkDataWriter
{
    // HEXC identifier
    private static readonly byte[] Magic = { (byte)'H', (byte)'E', (byte)'X', (byte)'C' }; // HEXC
    
    // header size
    private const int HeaderSize = 8;
    
    // Write ChunkData to a binary file
    // Uses Godot.FileAccess to reach res://

    public static void WriteChunk(ChunkData data, string filePath)
    {
        // Open file to work on
        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            GD.PushError($"ChunkDataWriter: Failed to open {filePath} for writing. Error: {FileAccess.GetOpenError()}");
            return;
        }
        
        // Write Header

        file.StoreBuffer(Magic);
        file.Store8(data.Version);
        file.Store8(data.Width);
        file.Store8(data.Height);
        file.Store8(0); // flags
        
        // Write Tile Vertex Heights
        for (int i = 0; i < data.VertexHeights.Length; i++)
        {
            file.StoreFloat(data.VertexHeights[i]);
        }
        
        GD.Print($"ChunkDataWriter: Wrote chunk ({data.ChunkX},{data.ChunkY}) " +
                 $"to {filePath} ({data.VertexHeights.Length} vertices)");

    }
    
    // Make filename for binary file

    public static string GetChunkFileName(int chunkX, int chunkY)
    {
        string xStr = chunkX < 0 ? $"n{-chunkX}" : chunkX.ToString();
        string yStr = chunkY < 0 ? $"n{-chunkY}" : chunkY.ToString();
        return $"chunk_{xStr}_{yStr}.dat";
    }
}