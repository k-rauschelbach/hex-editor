namespace HexEditor.scripts;

// Creates a small clickable sphere at each VertexGroup geographical location
// Clicking on a VertexGroup will highlight the corresponding Vertices
// Separate collision layer from tile

using Godot;

public partial class VertexHandle : Node3D
{
    // Vertex group identifier
    public int GroupId { get; set; }
    
    // Visual State
    private MeshInstance3D _meshInstance;
    private static StandardMaterial3D _normalMaterial;
    private static StandardMaterial3D _selectedMaterial;
    private bool _isSelected;
    
    public bool IsSelected => _isSelected;
    
    // Create a handle
    // Called by EditorMain
    public void Initialize(int groupId, Vector3 worldPosition)
    {
        GroupId = groupId;
        Position = worldPosition;
        
        // Visual sphere
        _meshInstance = new MeshInstance3D();
        var sphere = new SphereMesh();
        sphere.Radius = 0.06f;
        sphere.Height = 0.12f;
        sphere.RadialSegments = 8;
        sphere.Rings = 8;
        _meshInstance.Mesh = sphere;
        
        // Create shared materials for selected and unselected states
        if (_normalMaterial == null)
        {
            _normalMaterial = new StandardMaterial3D();
            _normalMaterial.AlbedoColor = new Color(0.0f, 0.8f, 0.8f); // Cyan for unselected
            _normalMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded; // Remove lighting effects
        }

        if (_selectedMaterial == null)
        {
            _selectedMaterial = new StandardMaterial3D();
            _selectedMaterial.AlbedoColor = new Color(1.0f, 1.0f, 0.0f); // yellow for selected
            _selectedMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded; // Remove lighting effects
        }
        
        _meshInstance.MaterialOverride = _normalMaterial;
        AddChild(_meshInstance);
        
        // Create a collision body for sphere on Layer 2
        StaticBody3D body = new StaticBody3D();
        body.CollisionLayer = 2; // Layer 2 for handles
        body.CollisionMask = 0;
        
        CollisionShape3D colShape = new CollisionShape3D();
        var sphereShape = new SphereShape3D();
        sphereShape.Radius = 0.1f; // slightly bigger than unselected spheres
        colShape.Shape = sphereShape;
        body.AddChild(colShape);
        AddChild(body);
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (_meshInstance != null)
        {
            _meshInstance.MaterialOverride = selected ? _selectedMaterial : _normalMaterial;
        }
    }

}