namespace HexEditor.scripts;

using Godot;
using HexEditor.scripts.shared;
using System;
using System.Collections.Generic;

public class DragHandler
{
    private bool _isDragging;
    private int _dragGroupId = -1;
    private float _dragStartHeight;
    private float _dragStartMouseY;
    private const float DragSensitivity = -0.02f;
    private readonly HashSet<(Vector2I chunk, int dq, int dr)> _dirtyTiles = new();

    private readonly VertexMap _vertexMap;
    private readonly EditorChunkManager _chunkManager;

    public bool IsDragging => _isDragging;

    // Height clamping delegate (provided by EditorMain)
    public Func<int, float, float> ClampHeight { get; set; }

    public DragHandler(VertexMap vertexMap, EditorChunkManager chunkManager)
    {
        _vertexMap = vertexMap;
        _chunkManager = chunkManager;
    }

    public void StartDrag(int groupId, float mouseY)
    {
        _isDragging = true;
        _dragGroupId = groupId;
        _dragStartHeight = _vertexMap.GetGroupHeight(groupId);
        _dragStartMouseY = mouseY;
    }

    // Process mouse motion during a single-vertex drag.
    // Returns dirty tiles and the new height value (for spinbox sync).
    public (HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles, float newHeight) ProcessMotion(
        float currentMouseY, HashSet<int> selectedGroups)
    {
        float deltaPixels = currentMouseY - _dragStartMouseY;
        float newHeight = _dragStartHeight + deltaPixels * DragSensitivity;

        HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles = new();
        foreach (int groupId in selectedGroups)
        {
            float clampedHeight = ClampHeight != null ? ClampHeight(groupId, newHeight) : newHeight;
            var affected = _vertexMap.SetGroupHeight(groupId, clampedHeight);
            dirtyTiles.UnionWith(affected);
        }

        // Regenerate affected tiles (no collision during drag for performance)
        foreach (var (chunk, dq, dr) in dirtyTiles)
            _chunkManager.RegenerateTile(chunk, dq, dr, updateCollision: false);

        // Track affected tiles for collision rebuild on release
        _dirtyTiles.UnionWith(dirtyTiles);

        return (dirtyTiles, newHeight);
    }

    // End the drag and rebuild collision for all affected tiles
    public void EndDrag()
    {
        _isDragging = false;
        _dragGroupId = -1;

        // Rebuild collision shapes for tiles affected during the drag
        foreach (var (chunk, dq, dr) in _dirtyTiles)
            _chunkManager.RegenerateTile(chunk, dq, dr, updateCollision: true);

        _dirtyTiles.Clear();
    }
}
