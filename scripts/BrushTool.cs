namespace HexEditor.scripts;

using Godot;
using HexEditor.scripts.shared;
using System.Collections.Generic;
using System;
using HexEditor.scripts;

// --- Brush Tool --- //
// Area-based sculpting tool. Selects all vertex handles within a configurable radius/shape and applies
// height changes with Gaussian falloff.

public class BrushTool
{
    // --- Configuration --- //
    
    // Brush radius in world units
    private float _radius = 2.0f;
    private const float MinRadius = 0.5f;
    private const float MaxRadius = 20.0f;
    private const float RadiusStep = 0.25f;
    
    // Gaussian falloff factor
    private float _strength = 0.5f;

    public enum Shape
    {
        Circle,
        Square
    }

    private Shape _shape = Shape.Circle;
    
    // --- Runtime State --- //
    
    // World XZ position of brush center
    private Vector3 _centerWorld = Vector3.Zero;
    private bool _cursorValid = false;
    
    // 3D mesh for cursor on ground plane
    private MeshInstance3D _cursorMesh;
    // Material for cursor mesh
    private StandardMaterial3D _cursorMaterial;
    
    // Vertex data for current drag stroke
    private readonly Dictionary<int, float> _weights = new();
    private readonly Dictionary<int, float> _dragStartHeights = new();
    
    // Is a brush stroke in progress
    private bool _isDragging = false;
    private float _dragStartMouseY;
    
    // Height change per pixel of mouse movement
    private const float DragSensitivity = -0.02f;
    
    // Subsystem References
    private readonly VertexMap _vertexMap;
    private readonly EditorChunkManager _chunkManager;
    private readonly Node3D _handlesRoot;
    private readonly EditorCamera _camera;
    
    // UI reference for sync to spinbox
    private SpinBox _radiusSpinBox;
    
    public bool IsDragging => _isDragging;
    public System.Func<int, float, float> ClampHeight { get; set; }

    public BrushTool(VertexMap vertexMap, EditorChunkManager chunkManager, Node3D handlesRoot, EditorCamera camera)
    {
        _vertexMap = vertexMap;
        _chunkManager = chunkManager;
        _handlesRoot = handlesRoot;
        _camera = camera;
    }
    
    // Create the visual brush cursor
    // Semi-transparent disc/square
    // Called by EditorMain._Ready()
    public void CreateCursor(Node3D parentNode)
    {
        _cursorMaterial = new StandardMaterial3D();
        _cursorMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _cursorMaterial.AlbedoColor = new Color(1f, 1f, 1f, 0.25f); // white, 25% alpha
        _cursorMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _cursorMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled; // No occlusion culling

        _cursorMesh = new MeshInstance3D();
        _cursorMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; // No shadow on terrain
        _cursorMesh.Visible = false;
        parentNode.AddChild(_cursorMesh);

        RebuildCursorMesh();
    }
    
    // Regenerate the cursor mesh geometry to match current radius and shape
    // Called when radius or shape changes
    private void RebuildCursorMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        if (_shape == Shape.Circle)
        {
            // disc of 32 triangle segments
            int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float angle0 = 2f * Mathf.Pi * i / segments;
                float angle1 = 2f * Mathf.Pi * (i + 1) / segments;

                st.AddVertex(Vector3.Zero);
                st.AddVertex(new Vector3(Mathf.Cos(angle0) * _radius, 0f, Mathf.Sin(angle0) * _radius));
                st.AddVertex(new Vector3(Mathf.Cos(angle1) * _radius, 0f, Mathf.Sin(angle1) * _radius));

            }
        }
        else
        {
            // Flat square
            float r = _radius;
            st.AddVertex(new Vector3(-r, 0f, -r));
            st.AddVertex(new Vector3(r, 0f, -r));
            st.AddVertex(new Vector3(r, 0f, r));
            
            st.AddVertex(new Vector3(-r, 0f, -r));
            st.AddVertex(new Vector3(r, 0f, r));
            st.AddVertex(new Vector3(-r, 0f, r));
        }

        st.GenerateNormals();
        _cursorMesh.Mesh = st.Commit();
        _cursorMesh.MaterialOverride = _cursorMaterial;
    }
    
    // Update the cursor world position with raycast from camera
    // Mathematical raycasting in lieu of physics raycasting
    public void UpdateCursorPosition()
    {
        // Get current mouse position in viewport coordinates
        Vector2 mousePos = _camera.Camera.GetViewport().GetMousePosition();
        
        // Raycast from camera through mouse position
        Vector3 rayOrigin = _camera.Camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = _camera.Camera.ProjectRayNormal(mousePos);
        Vector3 rayEnd = rayOrigin + rayDir * 1000f;
        
        var spaceState = _camera.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
        query.CollisionMask = 1;
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            Vector3 hitPoint = (Vector3)result["position"];
            _centerWorld = hitPoint;
            _cursorValid = true;
            _cursorMesh.Visible = true;
            // Slightly offset Y to prevent clipping into terrain
            _cursorMesh.GlobalPosition = new Vector3(hitPoint.X, hitPoint.Y + 0.05f, hitPoint.Z);
            return;
        }
        
        // Fallback to Y=0 intersection if over empty space
        if (Mathf.Abs(rayDir.Y) < 0.0001f || (-rayOrigin.Y / rayDir.Y) < 0f)
        {
            _cursorValid = false;
            _cursorMesh.Visible = false;
            return;
        }

        float t = -rayOrigin.Y / rayDir.Y;
        Vector3 groundHit = rayOrigin + t * rayDir;
        _centerWorld = groundHit;
        _cursorValid = true;
        _cursorMesh.Visible = true;
        _cursorMesh.GlobalPosition = new Vector3(groundHit.X, groundHit.Y + 0.05f, groundHit.Z);



    }
    
    // Show/hide the brush cursor
    public void SetCursorVisible(bool visible)
    {
        _cursorMesh.Visible = visible && _cursorValid;
    }
    
    // Shift + Scroll to adjust radius
    public bool HandleScrollResize(InputEventMouseButton scrollEvent)
    {
        if (!scrollEvent.ShiftPressed || !scrollEvent.Pressed) return false;
        if (scrollEvent.ButtonIndex == MouseButton.WheelUp)
            _radius = Mathf.Min(MaxRadius, _radius + RadiusStep);
        else if (scrollEvent.ButtonIndex == MouseButton.WheelDown)
            _radius = Mathf.Max(MinRadius, _radius - RadiusStep);
        else return false;
        
        RebuildCursorMesh();
        if (_radiusSpinBox != null) _radiusSpinBox.Value = _radius;
        return true; // Consume event
    }
    
    // Click handler
    // Find all vertex handles within the brush radius, compute gaussian falloff, start a drag
    public bool HandleClickDown(float mouseY, HashSet<int> selectedGroups)
    {
        if (!_cursorValid) return false;
        
        // Clear any previous stroke data
        _weights.Clear();
        _dragStartHeights.Clear();
        
        // Gaussian sigma
        float sigma = _radius * _strength;
        
        // check if vertex handles are within the brush shape
        foreach (Node child in _handlesRoot.GetChildren())
        {
            if (child is not VertexHandle handle) continue;

            Vector3 handlePos = handle.Position;
            float dx = handlePos.X - _centerWorld.X;
            float dz = handlePos.Z - _centerWorld.Z;
            
            // Distance depends on brush shape
            float distance = _shape == Shape.Circle ? Mathf.Sqrt(dx * dx + dz * dz) : Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));

            if (distance > _radius) continue; // outside brush
            
            // Gaussian weight
            // exponential decay with radius and strength
            float weight = Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));
            
            _weights[handle.GroupId] = weight;
            _dragStartHeights[handle.GroupId] = _vertexMap.GetGroupHeight(handle.GroupId);
        }

        if (_weights.Count == 0) return false;

        _isDragging = true;
        _dragStartMouseY = mouseY;
        return true;
    }
    
    // Drag handler
    // Apply weighted height adjustments to selected vertex handles
    public HashSet<(Vector2I chunk, int dq, int dr)> HandleDragMotion(float currentMouseY)
    {
        float deltaPixels = currentMouseY - _dragStartMouseY;
        HashSet<(Vector2I chunk, int dq, int dr)> dirtyTiles = new();

        foreach (var (groupId, weight) in _weights)
        {
            // adjust each vertex by (height + (drag delta * weight)
            float startHeight = _dragStartHeights[groupId];
            float newHeight = startHeight + deltaPixels * DragSensitivity * weight;
            if (ClampHeight != null) newHeight = ClampHeight(groupId, newHeight);

            var affected = _vertexMap.SetGroupHeight(groupId, newHeight);
            dirtyTiles.UnionWith(affected);
        }

        return dirtyTiles;
    }
    
    // Release handler
    // Clean up after drag
    public void HandleClickUp()
    {
        _isDragging = false;
        _weights.Clear();
        _dragStartHeights.Clear();
    }
    
    // --- UI --- //
    
    // Create the UI brush controls
    // radius, strength, shape

    public VBoxContainer CreateUI()
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("seperation", 8);

        // Brush Radius
        var radiusLabel = new Label();
        radiusLabel.Text = "Brush Radius:";
        container.AddChild(radiusLabel);

        _radiusSpinBox = new SpinBox();
        _radiusSpinBox.MinValue = MinRadius;
        _radiusSpinBox.MaxValue = MaxRadius;
        _radiusSpinBox.Step = RadiusStep;
        _radiusSpinBox.Value = _radius;
        _radiusSpinBox.Editable = false; // disabled until in brush mode
        _radiusSpinBox.ValueChanged += (double val) =>
        {
            _radius = (float)val;
            RebuildCursorMesh();
        };
        container.AddChild(_radiusSpinBox);

        // Falloff strength
        var strengthLabel = new Label();
        strengthLabel.Text = "Strength:";
        container.AddChild(strengthLabel);

        // Slider and value side by side
        var strengthRow = new HBoxContainer();

        var strengthSlider = new HSlider();
        strengthSlider.MinValue = 0.1;
        strengthSlider.MaxValue = 1.0;
        strengthSlider.Step = 0.05;
        strengthSlider.Value = _strength;
        strengthSlider.Editable = false;
        strengthSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        strengthRow.AddChild(strengthSlider);

        var strengthValueLabel = new Label();
        strengthValueLabel.Text = _strength.ToString("F2");
        strengthValueLabel.CustomMinimumSize = new Vector2(35, 0);
        strengthRow.AddChild(strengthValueLabel);

        strengthSlider.ValueChanged += (double val) =>
        {
            _strength = (float)val;
            strengthValueLabel.Text = val.ToString("F2");
        };
        container.AddChild(strengthRow);
        
        // Brush Shape
        var shapeLabel = new Label();
        shapeLabel.Text = "Brush Shape:";
        container.AddChild(shapeLabel);
        
        var shapeDropdown = new OptionButton();
        shapeDropdown.AddItem("Circle", 0);
        shapeDropdown.AddItem("Square", 1);
        shapeDropdown.Selected = 0;
        shapeDropdown.Disabled = true;
        shapeDropdown.ItemSelected += (long index) =>
        {
            _shape = (Shape)index;
            RebuildCursorMesh();
        };
        container.AddChild(shapeDropdown);
            
        // Store references for enable/disable toggling
        _uiControls = (strengthSlider, shapeDropdown);

        return container;
    }
    
    private (HSlider strengthSlider, OptionButton shapeDropdown) _uiControls;
    
    // Enable/Disable Brush controls in UI
    public void SetControlsEnabled(bool enabled)
    {
        _radiusSpinBox.Editable = enabled;
        _uiControls.strengthSlider.Editable = enabled;
        _uiControls.shapeDropdown.Disabled = !enabled;
    }


}