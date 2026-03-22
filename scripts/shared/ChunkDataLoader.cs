using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace HexEditor.scripts.shared;

// Reads binary chunk data files from disk
// Uses cache to store most recently loaded chunks
// cache is voided to LRU when full

// Threads chunk load operations to background. HexWorld creates nodes on the godot thread later

public class ChunkDataLoader
{
    // cache entry
    private struct CacheEntry
    {
        public Vector2I Coord;
        public ChunkData Data;
    }
    
    // Linked list for maintaining access order for recently loaded chunks
    private readonly LinkedList<CacheEntry> _cacheOrder = new();
    
    // Map chunk coordinates to linked list
    private readonly Dictionary<Vector2I, LinkedListNode<CacheEntry>> _cacheMap = new();
    
    // ------ Configuration ------
    
    // Binary files directory path
    private readonly string _worldDataPath;
    
    // Maximum number of chunks to keep in cache
    // Chunk data is ~6Kb each (Version 1)
    private readonly int _cacheCapacity;
    
    // Expected Magic header (Version 1 = HEXC)
    private static readonly byte[] ExpectedMagic = { (byte)'H', (byte)'E', (byte)'X', (byte)'C' };
    
    // ------ Constructor ------
    
    // Initialization
    public ChunkDataLoader(string worldDataPath, int cacheCapacity = 64)
    {
        _worldDataPath = worldDataPath.EndsWith("/") ? worldDataPath : worldDataPath + "/";
        _cacheCapacity = cacheCapacity;
    }
    
    // ------ API ------
    
    // Sync load chunk data from disk or cache

    public ChunkData LoadChunkData(Vector2I chunkCoord)
    {
        // Check cache first
        if (_cacheMap.TryGetValue(chunkCoord, out var node))
        {
            _cacheOrder.Remove(node);
            _cacheOrder.AddFirst(node);
            return node.Value.Data;
        }
        
        // Not in cache

        string filePath = GetChunkFilePath(chunkCoord);

        if (!FileAccess.FileExists(filePath))
        {
            return null;
        }

        ChunkData data = ReadChunkFile(filePath, chunkCoord);

        if (data != null)
        {
            AddToCache(chunkCoord, data);
        }

        return data;
    }
    
    // Async load chunk data from background thread
    public Task<ChunkData> LoadChunkDataAsync(Vector2I chunkCoord)
    {
        // Check Cache
        if (_cacheMap.TryGetValue(chunkCoord, out var node))
        {
            _cacheOrder.Remove(node);
            _cacheOrder.AddFirst(node);
            return Task.FromResult(node.Value.Data);
        }

        string filePath = GetChunkFilePath(chunkCoord);

        return Task.Run(() =>
        {
            if (!FileAccess.FileExists(filePath))
            {
                return null;
            }

            return ReadChunkFile(filePath, chunkCoord);
        });
    }
    
    // Check if chunk data exists for given coordinates
    // Checks for world boundaries without reading all data

    public bool ChunkExists(Vector2I chunkCoord)
    {
        return FileAccess.FileExists(GetChunkFilePath(chunkCoord));
    }
    
    // Add chunk data to cache from async thread
    public void CacheChunkData(Vector2I chunkCoord, ChunkData data)
    {
        if (data != null && !_cacheMap.ContainsKey(chunkCoord))
        {
            AddToCache(chunkCoord, data);
        }
    }
    
    // ------ Private Functions ------
    
    // Get file path for chunk data given coordinates
    private string GetChunkFilePath(Vector2I chunkCoord)
    {
        string fileName = ChunkDataWriter.GetChunkFileName(chunkCoord.X, chunkCoord.Y);
        return _worldDataPath + fileName;
    }
    
    // Read chunk file and create a ChunkData object
    private ChunkData ReadChunkFile(string filePath, Vector2I chunkCoord)
    {
        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            GD.PushError($"ChunkDataLoader: Failed to open {filePath}." +
                         $"Error: {FileAccess.GetOpenError()}");
        }
        
        // validate header
        byte[] magic = file.GetBuffer(4);
        if (magic.Length < 4 ||
            magic[0] != ExpectedMagic[0] || magic[1] != ExpectedMagic[1] ||
            magic[2] != ExpectedMagic[2] || magic[3] != ExpectedMagic[3])
        {
            GD.PushError($"ChunkDataLoader: Invalid header in {filePath}." + 
                         $"Expected {ExpectedMagic}");
            return null;
        }

        byte version = file.Get8();
        byte width = file.Get8();
        byte height = file.Get8();
        byte flags = file.Get8(); // Currently unused (Version 1)
        
        // Read vertex heights
        int totalFloats = width * height * ChunkData.VerticesPerTile;
        float[] vertexHeights = new float[totalFloats];
        
        // Read floats using GetFloat
        for (int i = 0; i < totalFloats; i++)
        {
            vertexHeights[i] = file.GetFloat();
        }
        
        // Assemble Data to ChunkData object

        return new ChunkData
        {
            ChunkX = chunkCoord.X,
            ChunkY = chunkCoord.Y,
            Version = version,
            Width = width,
            Height = height,
            VertexHeights = vertexHeights
        };
    }
    
    // Add ChunkData to cache
    // evict oldest if full

    private void AddToCache(Vector2I coord, ChunkData data)
    {
        // check if currently in cache
        if (_cacheMap.TryGetValue(coord, out var existingNode))
        {
            _cacheOrder.Remove(existingNode);
            _cacheMap.Remove(coord);
        }
        
        // remove oldest entry if full
        if (_cacheOrder.Count >= _cacheCapacity)
        {
            var lastNode = _cacheOrder.Last;
            _cacheMap.Remove(lastNode.Value.Coord);
            _cacheOrder.RemoveLast();
        }
        
        // add new entry
        var entry = new CacheEntry { Coord = coord, Data = data };
        var newNode = _cacheOrder.AddFirst(entry);
        _cacheMap[coord] = newNode;
    }
}