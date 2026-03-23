using Godot;
using HexEditor.scripts.shared;
using HexEditor.scripts;
using System.Collections.Generic;
using System;

public partial class EditorMain : Node3D
{
    // ------ Configuration ------ //

    // Default sizes
    private int _chunkWidth;
    private int _chunkHeight;
    private float _tileSize;

    // Node References
    private Node3D _chunksRoot;
    private Node3D _handlesRoot;
    private EditorCamera _camera;

    private readonly Dictionary<int, VertexHandle> _handlesByGroupId = new();
    private bool _isLoading = false;

    // Subsystem References
    private EditorChunkManager _chunkManager;
    private VertexMap _vertexMap;
    private ChunkArrowManager _chunkArrowManager;
    private DragHandler _dragHandler;

    // Selection State
    private readonly HashSet<int> _selectedGroups = new();
    private Vector2 _lastHoverMousePos;

    // Brush mode enums
    public enum EditMode
    {
        SingleVertex,
        Brush
    }

    private EditMode _currentMode = EditMode.SingleVertex;
    private BrushTool _brushTool;

    // UI references
    private SpinBox _heightSpinBox;
    // Flag to prevent feedback loops
    private bool _updatingSpinBox = false;
    // Toggle for walkability clamping
    private bool _slopeConstraintEnabled = true;

    // Tracks tiles dirtied during a brush drag for collision rebuild on release
    private readonly HashSet<(Vector2I chunk, int dq, int dr)> _brushDirtyTiles = new();

    // --- Chunk Management --- //
    private Label _chunkCountLabel;


    public override void _Ready()
    {
        // Read Project settings for sizes
        _chunkWidth = (int)ProjectSettings.GetSetting("global/ChunkWidthDefault");
        _chunkHeight = (int)ProjectSettings.GetSetting("global/ChunkHeightDefault");
        _tileSize = (float)ProjectSettings.GetSetting("global/TileSizeDefault");

        // Get Node References
        _chunksRoot = GetNode<Node3D>("ChunksRoot");
        _handlesRoot = GetNode<Node3D>("HandlesRoot");
        _camera = GetNode<EditorCamera>("CameraPivot");
        var arrowsRoot = GetNode<Node3D>("ArrowsRoot");

        // Instantiate Chunk Manager
        _chunkManager = new EditorChunkManager(_chunksRoot, _chunkWidth, _chunkHeight, _tileSize);

        // Create initial flat chunk at 0, 0 to start
        _chunkManager.CreateFlatChunk(0, 0);

        // Build the vertex map and spawn handles
        _vertexMap = new VertexMap(_chunkManager.ChunkDataDictionary, _chunkWidth, _chunkHeight);
        _vertexMap.Rebuild();
        SpawnVertexHandles();

        // Create chunk arrow manager
        _chunkArrowManager = new ChunkArrowManager(arrowsRoot, _chunkManager, _vertexMap, _camera,
            _chunkWidth, _chunkHeight, _tileSize);
        _chunkArrowManager.OnChunksChanged = () =>
        {
            SpawnVertexHandles();
            DeselectAll();
            _chunkCountLabel.Text = $"Loaded: {_chunkManager.AllChunks.Count} Chunk(s)";
        };
        _chunkArrowManager.SpawnArrows();

        Vector3 chunkCenter = HexAxialMath.AxialToWorld(new HexAxial(8, 8), _tileSize);
        _camera.SetFocusPoint(chunkCenter);

        // Create the brush tool
        _brushTool = new BrushTool(_vertexMap, _chunkManager, _handlesRoot, _camera);
        _brushTool.ClampHeight = ClampHeightForGroup;
        _brushTool.CreateCursor(this); // create cursor mesh as child of main

        // Create the drag handler
        _dragHandler = new DragHandler(_vertexMap, _chunkManager);
        _dragHandler.ClampHeight = ClampHeightForGroup;

        // Setup UI
        var ui = GetNode<Control>("CanvasLayer/EditorUI");
        var refs = EditorUiBuilder.Build(
            ui,
            _brushTool,
            onModeChanged: (mode) =>
            {
                _currentMode = mode;
                DeselectAll();
                _brushTool.SetCursorVisible(_currentMode == EditMode.Brush);
                _brushTool.SetControlsEnabled(_currentMode == EditMode.Brush);
            },
            onDeselectAll: () => DeselectAll(),
            onHeightChanged: OnHeightSpinBoxChanged,
            onSlopeConstraintToggled: (on) => _slopeConstraintEnabled = on,
            onOverlayToggled: (on) =>
            {
                _chunkManager.ShowWalkabilityOverlay = on;
                _chunkManager.UpdateAllWalkabilityTints();
            },
            onMaxDeviationChanged: (val) =>
            {
                _chunkManager.MaxDeviation = val;
                _chunkManager.UpdateAllWalkabilityTints();
            },
            onMaxStepHeightChanged: (val) =>
            {
                _chunkManager.MaxStepHeight = val;
                _chunkManager.UpdateAllWalkabilityTints();
            },
            onSave: (dir) => _chunkManager.SaveAllChunks(dir),
            onLoad: (dir) => LoadChunksAsync(dir));

        _heightSpinBox = refs.HeightSpinBox;
        _chunkCountLabel = refs.ChunkCountLabel;
    }

    public override void _Process(double delta)
    {
        // Update brush cursor position when brush mode is active
        if (_currentMode == EditMode.Brush)
        {
            _brushTool.UpdateCursorPosition();
        }

        Vector2 currentMousePos = GetViewport().GetMousePosition();
        if (currentMousePos != _lastHoverMousePos)
        {
            _lastHoverMousePos = currentMousePos;
            _chunkArrowManager.UpdateHover(GetWorld3D().DirectSpaceState);
        }
    }

    // Spawn in VertexHandles for each vertex group
    private void SpawnVertexHandles()
    {
        // clear any existing handles
        foreach (Node child in _handlesRoot.GetChildren())
            child.QueueFree();

        // Clear the lookup dictionary
        _handlesByGroupId.Clear();

        for (int groupId = 0; groupId < _vertexMap.GetGroupCount(); groupId++)
        {
            // get world position for this vertex
            Vector3 worldPos = GetVertexWorldPosition(groupId);
            var handle = new VertexHandle();
            handle.Initialize(groupId, worldPos);
            _handlesRoot.AddChild(handle);

            _handlesByGroupId[groupId] = handle;
        }
    }

    // Calculate world position of a vertex group
    private Vector3 GetVertexWorldPosition(int groupId)
    {
        var group = _vertexMap.GetGroup(groupId);
        var loc = group[0];

        var data = _chunkManager.GetChunkData(loc.ChunkCoord);
        int globalQ = data.ChunkX * _chunkWidth + loc.Dq;
        int globalR = data.ChunkY * _chunkHeight + loc.Dr;

        // set tile center at 0 Y, set XZ from HexAxial position
        Vector3 tileCenter = HexAxialMath.AxialToWorld(new HexAxial(globalQ, globalR), _tileSize);

        // Vertex offset from tile center
        float angle = Mathf.Pi / 3f * loc.VertexIndex;
        float offsetX = Mathf.Cos(angle) * _tileSize;
        float offsetZ = Mathf.Sin(angle) * _tileSize;

        // get the absolute vertex height from ChunkData
        float height = _vertexMap.GetGroupHeight(groupId);

        return new Vector3(tileCenter.X + offsetX, height, tileCenter.Z + offsetZ);
    }

    // --- Input Handling --- //

    public override void _UnhandledInput(InputEvent @event)
    {

        // Block editing input while chunks are loading
        if (_isLoading) return;

        if (@event is InputEventMouseButton scrollEvent && _currentMode == EditMode.Brush)
        {
            if (_brushTool.HandleScrollResize(scrollEvent))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        // Left Click to select/deselect vertex handles
        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                if (mouseBtn.Pressed)
                {
                    HandleLeftClickDown(mouseBtn);
                }
                else
                {
                    HandleLeftClickUp();
                }
            }
        }

        // Mouse motion during drag
        if (@event is InputEventMouseMotion mouseMotion && (_dragHandler.IsDragging || _brushTool.IsDragging))
        {
            HandleDragMotion(mouseMotion);
        }
    }

    private void HandleLeftClickDown(InputEventMouseButton mouseBtn)
    {
        Camera3D camera = _camera.Camera;
        Vector3 rayOrigin = camera.ProjectRayOrigin(mouseBtn.Position);
        Vector3 rayDir = camera.ProjectRayNormal(mouseBtn.Position);
        Vector3 rayEnd = rayOrigin + rayDir * 1000f;
        var spaceState = GetWorld3D().DirectSpaceState;

        // Check if arrow was clicked
        if (_chunkArrowManager.TryHandleArrowClick(spaceState, rayOrigin, rayEnd))
            return;

        if (_currentMode == EditMode.SingleVertex)
        {
            // Check if handle hits first
            var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
            query.CollisionMask = 2;
            var result = spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                Node3D collider = (Node3D)result["collider"];
                VertexHandle handle = collider.GetParent<VertexHandle>();
                if (handle != null)
                {
                    bool shiftHeld = mouseBtn.ShiftPressed;

                    // If clicking on an already-selected handle, start dragging
                    if (_selectedGroups.Contains(handle.GroupId))
                    {
                        _dragHandler.StartDrag(handle.GroupId, mouseBtn.Position.Y);
                    }
                    else
                    {
                        // Select handle first
                        SelectHandle(handle, shiftHeld);
                        // Start drag immediately
                        _dragHandler.StartDrag(handle.GroupId, mouseBtn.Position.Y);
                    }
                }
            }
            else
            {
                if (!mouseBtn.ShiftPressed)
                {
                    DeselectAll();
                }
            }
        }
        else if (_currentMode == EditMode.Brush)
        {
            // Deselect all handles
            DeselectAll();
            _brushTool.HandleClickDown(mouseBtn.Position.Y, _selectedGroups);
        }
    }

    private void HandleLeftClickUp()
    {
        if (_currentMode == EditMode.Brush && _brushTool.IsDragging)
        {
            _brushTool.HandleClickUp();

            // Rebuild collision for all tiles affected during the brush stroke
            foreach (var (chunk, dq, dr) in _brushDirtyTiles)
                _chunkManager.RegenerateTile(chunk, dq, dr, updateCollision: true);
            _brushDirtyTiles.Clear();

            DeselectAll();
        }

        if (_dragHandler.IsDragging)
        {
            _dragHandler.EndDrag();
        }
    }

    private void HandleDragMotion(InputEventMouseMotion mouseMotion)
    {
        HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles;

        if (_currentMode == EditMode.Brush && _brushTool.IsDragging)
        {
            // Brush mode: BrushTool computes weighted heights for all affected vertices
            dirtyTiles = _brushTool.HandleDragMotion(mouseMotion.Position.Y);

            // Regenerate affected tile meshes (no collision during drag for performance)
            foreach (var (chunk, dq, dr) in dirtyTiles)
                _chunkManager.RegenerateTile(chunk, dq, dr, updateCollision: false);

            _brushDirtyTiles.UnionWith(dirtyTiles);
        }
        else
        {
            // Single vertex mode: DragHandler manages state and tile regeneration
            var result = _dragHandler.ProcessMotion(mouseMotion.Position.Y, _selectedGroups);
            dirtyTiles = result.dirtyTiles;

            _updatingSpinBox = true;
            _heightSpinBox.Value = result.newHeight;
            _updatingSpinBox = false;
        }

        UpdateHandlePositions(dirtyTiles);
    }

    private void SelectHandle(VertexHandle handle, bool addToSelection)
    {
        if (!addToSelection)
        {
            // Clear all selections
            DeselectAll();
        }

        if (_selectedGroups.Contains(handle.GroupId))
        {
            // Already selected, deselect it
            _selectedGroups.Remove(handle.GroupId);
            handle.SetSelected(false);
        }
        else
        {
            _selectedGroups.Add(handle.GroupId);
            handle.SetSelected(true);
        }
        // Update spinbox
        UpdateSpinBoxFromSelection();
    }

    private void DeselectAll()
    {
        _selectedGroups.Clear();
        // update all handles
        foreach (Node child in _handlesRoot.GetChildren())
        {
            if (child is VertexHandle handle)
                handle.SetSelected(false);
        }

        UpdateSpinBoxFromSelection();
    }

    private void UpdateSpinBoxFromSelection()
    {
        _updatingSpinBox = true; // prevent feedback loops

        if (_selectedGroups.Count == 0)
        {
            _heightSpinBox.Value = 0;
            _heightSpinBox.Editable = false;
        }
        else
        {
            // Show the height of the first selected group, even if multiple are selected
            _heightSpinBox.Editable = true;
            foreach (int groupId in _selectedGroups)
            {
                _heightSpinBox.Value = _vertexMap.GetGroupHeight(groupId);
                break;
            }
        }

        _updatingSpinBox = false;
    }

    // Called when user edits value in height spinbox
    private void OnHeightSpinBoxChanged(double newValue)
    {
        // prevent feedback loops
        if (_updatingSpinBox) return;

        float height = (float)newValue;

        // apply new height to all selected vertex groups
        HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles = new();

        foreach (int groupId in _selectedGroups)
        {
            float clampedHeight = ClampHeightForGroup(groupId, height);
            var affected = _vertexMap.SetGroupHeight(groupId, clampedHeight);
            dirtyTiles.UnionWith(affected);
        }

        // Regenerate meshes for affected tiles
        foreach (var (chunk, dq, dr) in dirtyTiles)
            _chunkManager.RegenerateTile(chunk, dq, dr);

        // Update vertex handle positions
        UpdateHandlePositions(dirtyTiles);
    }

    // Reposition vertex handles affected by dirty tiles
    private void UpdateHandlePositions(HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles)
    {
        // Collect groupIDs from dirty tiles
        HashSet<int> dirtyGroups = new();
        foreach (var (chunk, dq, dr) in dirtyTiles)
            _vertexMap.GetGroupIdsForTile(chunk, dq, dr, dirtyGroups);

        // reposition handles in dirty tiles
        foreach (int groupId in dirtyGroups)
        {
            if (_handlesByGroupId.TryGetValue(groupId, out VertexHandle handle))
                handle.Position = GetVertexWorldPosition(groupId);
        }
    }

    // reposition vertex handles when tile height changes
    private void UpdateHandlePositions()
    {
        foreach (Node child in _handlesRoot.GetChildren())
        {
            if (child is VertexHandle handle)
                handle.Position = GetVertexWorldPosition(handle.GroupId);
        }
    }

    // --- Chunk Loading --- //

    // ASync load a chunk from disk
    private async void LoadChunksAsync(string directory)
    {
        _isLoading = true;

        // Parse all chunkdata files in directory
        var coords = _chunkManager.ParseChunksFromDirectory(directory);

        if (coords.Count == 0)
        {
            _isLoading = false;
            return;
        }

        // Generate scene nodes at one chunk per frame
        for (int i = 0; i < coords.Count; i++)
        {
            _chunkManager.GenerateChunkScene(coords[i]);

            // Progress label
            _chunkCountLabel.Text = $"Loading: {i + 1}/{coords.Count} Chunk(s)";

            // Let engine render frame before next chunk
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // Rebuild subsystems once all chunks are loaded
        _vertexMap.Rebuild();
        SpawnVertexHandles();
        _chunkArrowManager.SpawnArrows();
        DeselectAll();
        _chunkCountLabel.Text = $"Loaded: {coords.Count} Chunk(s)";

        _isLoading = false;
    }

    // --- Walkability --- //
    /// <summary>
    ///  Clamp a proposed vertex height so that all tiles containing this vertex stay within the max deviation limits
    /// </summary>
    private float ClampHeightForGroup(int groupId, float proposedHeight)
    {
        if (!_slopeConstraintEnabled) return proposedHeight;

        float maxDev = _chunkManager.MaxDeviation;

        float globalMin = float.MinValue;
        float globalMax = float.MaxValue;

        // Get locations for all vertices in group
        var locations = _vertexMap.GetGroupLocations(groupId);

        foreach (var loc in locations)
        {
            // Get current tiles 6 heights
            if (!_chunkManager.TryGetChunkData(loc.ChunkCoord, out ChunkData data)) continue;

            float[] heights = new float[6];
            data.GetVertexHeightsNonAlloc(loc.Dq, loc.Dr, heights);

            // Compute allowed range for this vertex
            var (minH, maxH) = WalkabilityChecker.ComputeAllowedHeightRange(heights, loc.VertexIndex, maxDev);

            // Convert to global range
            if (minH > globalMin) globalMin = minH;
            if (maxH < globalMax) globalMax = maxH;
        }


        if (globalMin > globalMax)
        {
            float gap = globalMin - globalMax;
            if (gap < 0.01f)
                // Tiny float rounding — snap to the midpoint of the collapsed range
                return (globalMin + globalMax) * 0.5f;
            // Genuinely conflicting constraints (e.g. mid-brush-stroke with multiple
            // vertices moving). Hold at current height — don't allow movement past
            // constraints, but don't force a value from a broken range either.
            return _vertexMap.GetGroupHeight(groupId);
        }

        return Mathf.Clamp(proposedHeight, globalMin, globalMax);
    }
}
