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
    private Node3D _arrowsRoot;
    
    private ChunkArrow _hoveredArrow;
    
    // Subsystem References
    private EditorChunkManager _chunkManager;
    private VertexMap _vertexMap;
    
    // Selection State
    private readonly HashSet<int> _selectedGroups = new();
    
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
    
    // --- Drag State --- //
    //
    // Check for active dragging a vertex
    private bool _isDragging = false;
    // The group being dragged
    private int _dragGroupId = -1;
    // The vertex height when dragging started
    private float _dragStartHeight;
    // The mouse Y position when the drag started
    private float _dragStartMouseY;
    // Height change sensitivity from mouse movement during drag
    private const float DragSensitivity = -0.02f;
    
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
        _arrowsRoot = GetNode<Node3D>("ArrowsRoot");
        
        // Instantiate Chunk Manager
        _chunkManager = new EditorChunkManager(_chunksRoot, _chunkWidth, _chunkHeight, _tileSize);
        
        // Create initial flat chunk at 0, 0 to start
        _chunkManager.CreateFlatChunk(0, 0);
        
        // Build the vertex map and spawn handles
        _vertexMap = new VertexMap(_chunkManager.ChunkDataDictionary, _chunkWidth, _chunkHeight);
        _vertexMap.Rebuild();
        SpawnVertexHandles();
        
        // Spawn chunk arrows
        SpawnChunkArrows();
        
        Vector3 chunkCenter = HexAxialMath.AxialToWorld(new HexAxial(8, 8), _tileSize);
        _camera.SetFocusPoint(chunkCenter);
        
        // Create the brush tool
        _brushTool = new BrushTool(_vertexMap, _chunkManager, _handlesRoot, _camera);
        _brushTool.CreateCursor(this); // create cursor mesh as child of main
        
        // Setup UI
        SetupUI();
        

    }

    public override void _Process(double delta)
    {
        // Update brush cursor position when brush mode is active
        if (_currentMode == EditMode.Brush)
        {
            _brushTool.UpdateCursorPosition();
        }

        UpdateArrowHover();
    }

    private void UpdateArrowHover()
    {
        Camera3D camera = _camera.Camera;
        Vector2 mousePos = camera.GetViewport().GetMousePosition();
        Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = camera.ProjectRayNormal(mousePos);
        Vector3 rayEnd = rayOrigin + rayDir * 1000f;
        var spaceState = GetWorld3D().DirectSpaceState;
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
    
    // Spawn in VertexHandles for each vertex group
    private void SpawnVertexHandles()
    {
        // clear any existing handles
        foreach (Node child in _handlesRoot.GetChildren())
            child.QueueFree();

        for (int groupId = 0; groupId < _vertexMap.GetGroupCount(); groupId++)
        {
            // get world position for this vertex
            Vector3 worldPos = GetVertexWorldPosition(groupId);

            var handle = new VertexHandle();
            handle.Initialize(groupId, worldPos);
            _handlesRoot.AddChild(handle);
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
        if (@event is InputEventMouseMotion mouseMotion && (_isDragging || _brushTool.IsDragging))
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
                return;
            }
        }
        
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
                        StartDrag(handle.GroupId, mouseBtn.Position.Y);
                    }
                    else
                    {
                        // Select handle first
                        SelectHandle(handle, shiftHeld);
                        // Start drag immediately
                        StartDrag(handle.GroupId, mouseBtn.Position.Y);
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

    private void StartDrag(int groupId, float mouseY)
    {
        _isDragging = true;
        _dragGroupId = groupId;
        _dragStartHeight = _vertexMap.GetGroupHeight(groupId);
        _dragStartMouseY = mouseY;
    }

    private void HandleLeftClickUp()
    {
        if (_currentMode == EditMode.Brush && _brushTool.IsDragging)
        {
            _brushTool.HandleClickUp();
            DeselectAll();
        }

        if (_isDragging)
        {
            _isDragging = false;
            _dragGroupId = -1;
        }
    }

    private void HandleDragMotion(InputEventMouseMotion mouseMotion)
    {
        HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles;

        if (_currentMode == EditMode.Brush && _brushTool.IsDragging)
        {
            // Brush mode: BrushTool computes weighted heights for all affected vertices
            dirtyTiles = _brushTool.HandleDragMotion(mouseMotion.Position.Y);
        }
        else
        {
            // Single vertex mode: existing behavior (all selected move equally)
            float currentMouseY = mouseMotion.Position.Y;
            float deltaPixels = currentMouseY - _dragStartMouseY;
            float newHeight = _dragStartHeight + deltaPixels * DragSensitivity;

            dirtyTiles = new();
            foreach (int groupId in _selectedGroups)
            {
                var affected = _vertexMap.SetGroupHeight(groupId, newHeight);
                dirtyTiles.UnionWith(affected);
            }

            _updatingSpinBox = true;
            _heightSpinBox.Value = newHeight;
            _updatingSpinBox = false;
        }

        // Regenerate affected tiles and update handle positions (shared by both modes)
        foreach (var (chunk, dq, dr) in dirtyTiles)
            _chunkManager.RegenerateTile(chunk, dq, dr);

        UpdateHandlePositions();
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
        
        // apply new hight to all selected vertex groups
        HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles = new();

        foreach (int groupId in _selectedGroups)
        {
            var affected = _vertexMap.SetGroupHeight(groupId, height);
            dirtyTiles.UnionWith(affected);
        }
        
        // Regenerate meshes for affected tiles
        foreach (var (chunk, dq, dr) in dirtyTiles)
            _chunkManager.RegenerateTile(chunk, dq, dr);
        
        // Update vertex handle positions
        UpdateHandlePositions();
    }
    
    // reposition vertex handles when tile height changes
    private void UpdateHandlePositions()
    {
        foreach (Node child in _handlesRoot.GetChildren())
        {
            if (child is VertexHandle handle)
            {
                handle.Position = GetVertexWorldPosition(handle.GroupId);
            }
        }
    }
    
    // --- Chunk Management --- //
    //

    private void SpawnChunkArrows()
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
    private void AddChunkFromArrow(ChunkArrow arrow)
    {
        _chunkManager.AddAdjacentChunk(arrow.ChunkCoord, arrow.Direction);
        
        // Get new chunks coordinate
        Vector2I offset = arrow.Direction switch
        {
            EditorChunkManager.ChunkDirection.North => new Vector2I(0, -1),
            EditorChunkManager.ChunkDirection.South => new Vector2I(0, 1),
            EditorChunkManager.ChunkDirection.East => new Vector2I(1, 0),
            EditorChunkManager.ChunkDirection.West => new Vector2I(-1, 0),
            _ => Vector2I.Zero
        };
        Vector2I newCoord = arrow.ChunkCoord + offset;
        
        // Rebuild Vertex Map, sync edges, respawn handles and arrows
        _vertexMap.Rebuild();
        _chunkManager.SyncNewChunkEdges(newCoord, _vertexMap);
        SpawnVertexHandles();
        SpawnChunkArrows();
        
        DeselectAll();
    }
    
    // Compute world position for arrows
    // Find center tile of edge, convert to world coord, then nudge outward some
    private Vector3 GetChunkEdgeCenter(Vector2I chunkCoord, EditorChunkManager.ChunkDirection dir)
    {
        int cx = chunkCoord.X;
        int cy = chunkCoord.Y;

        int midQ, midR;

        switch (dir)
        {
            case EditorChunkManager.ChunkDirection.North:
                midQ = cx * _chunkWidth + _chunkWidth / 2;
                midR = cy * _chunkHeight; // dr = 0;
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
    
    // Find a chunk that borders an empty space in the given direction and add a new flat chunk there
    private void AddChunkInDirection(EditorChunkManager.ChunkDirection direction)
    {
        // find a loaded chunk whose neighbor in this direction doesn't exist
        // uses first one found
        Vector2I offset = direction switch
        {
            EditorChunkManager.ChunkDirection.North => new Vector2I(0, -1),
            EditorChunkManager.ChunkDirection.South => new Vector2I(0, 1),
            EditorChunkManager.ChunkDirection.East => new Vector2I(1, 0),
            EditorChunkManager.ChunkDirection.West => new Vector2I(-1, 0),
            _ => Vector2I.Zero
        };

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
                
                // Respawn handles and deselect all
                SpawnVertexHandles();
                DeselectAll();

                _chunkCountLabel.Text = $"Loaded: {_chunkManager.AllChunks.Count} Chunk(s)";
                return;
            }
        }
        GD.Print("No empty space found to add chunk.");
    }
    
    // --- UI Setup --- //
    
    // minimal, replace later
    private void SetupUI()
    {
        var ui = GetNode<Control>("CanvasLayer/EditorUI");

        // Make the parent control span the full viewport so child anchors
        // work relative to screen size (not the tiny 40x40 default from the scene).
        ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.SetOffsetsPreset(Control.LayoutPreset.FullRect);
        // Ignore mouse on this full-screen control so clicks pass through to the 3D viewport
        ui.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Create panel on the right side
        var panel = new PanelContainer();
        // position panel on right edge of frame
        panel.AnchorLeft = 1.0f;
        panel.AnchorRight = 1.0f;
        panel.AnchorTop = 0.0f;
        panel.AnchorBottom = 1.0f;
        panel.OffsetLeft = -220;
        panel.OffsetRight = 0;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        
        // Title
        var title = new Label();
        title.Text = "Terrain Editor";
        vbox.AddChild(title);
        
        vbox.AddChild(new HSeparator());
        
        // Edit mode selector
        var modeLabel = new Label();
        modeLabel.Text = "Edit Mode:";
        vbox.AddChild(modeLabel);
        
        var modeDropdown = new OptionButton();
        modeDropdown.AddItem("Single Vertex", 0);
        modeDropdown.AddItem("Brush", 1);
        modeDropdown.Selected = 0;
        modeDropdown.ItemSelected += (long index) =>
        {
            _currentMode = (EditMode)index;
            DeselectAll();
            _brushTool.SetCursorVisible(_currentMode == EditMode.Brush);
            _brushTool.SetControlsEnabled(_currentMode == EditMode.Brush);
        };
        vbox.AddChild(modeDropdown);
        
        // Brush Controls
        var brushUI = _brushTool.CreateUI();
        vbox.AddChild(brushUI);
        vbox.AddChild(new HSeparator());
        
        // Height Control
        var heightLabel = new Label();
        heightLabel.Text = "Vertex Height:";
        vbox.AddChild(heightLabel);

        _heightSpinBox = new SpinBox();
        _heightSpinBox.MinValue = -50;
        _heightSpinBox.MaxValue = 50;
        _heightSpinBox.Step = 0.1;
        _heightSpinBox.Editable = false; // disabled until a vertex is selected
        _heightSpinBox.ValueChanged += OnHeightSpinBoxChanged;
        vbox.AddChild(_heightSpinBox);
        
        // Deselect button
        var deselectButton = new Button();
        deselectButton.Text = "Deselect All";
        deselectButton.Pressed += () => DeselectAll();
        vbox.AddChild(deselectButton);
        
        panel.AddChild(vbox);
        ui.AddChild(panel);
        
        // --- File Managemnet --- //
        vbox.AddChild(new HSeparator());

        var fileLabel = new Label();
        fileLabel.Text = "File";
        vbox.AddChild(fileLabel);
        
        // Save directory input
        var dirContainer = new HBoxContainer();
        var dirLabel = new Label();
        dirLabel.Text = "Dir:";
        dirContainer.AddChild(dirLabel);

        var dirInput = new LineEdit();
        dirInput.Text = "res://worlddata/";
        dirInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        dirContainer.AddChild(dirInput);
        vbox.AddChild(dirContainer);
        
        // Save button
        var saveBtn = new Button();
        saveBtn.Text = "Save All";
        saveBtn.Pressed += () => _chunkManager.SaveAllChunks(dirInput.Text);
        vbox.AddChild(saveBtn);
        
        // Load Button
        var loadBtn = new Button();
        loadBtn.Text = "Load";
        loadBtn.Pressed += () =>
        {
            if (_chunkManager.LoadChunksFromDirectory(dirInput.Text))
            {
                // Rebuild vertex map and handles after loading
                _vertexMap.Rebuild();
                SpawnVertexHandles();
                SpawnChunkArrows();
                DeselectAll();
            }
        };
        vbox.AddChild(loadBtn);
        
        _chunkCountLabel = new Label();
        _chunkCountLabel.Text = "Loaded: 1 Chunk(s)";
        vbox.AddChild(_chunkCountLabel);
    }
}
