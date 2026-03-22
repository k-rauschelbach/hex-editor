using HexEditor.scripts.shared;

namespace HexEditor.scripts;

using Godot;

// Arrow shape for generating a new flat chunk on the chosen edge

public partial class ChunkArrow : Node3D
{
    // which chunk this arrow borders
    public Vector2I ChunkCoord { get; private set; }

    // Which direction the new chunk would be added in
    public EditorChunkManager.ChunkDirection Direction { get; private set; }

    private MeshInstance3D _meshInstance;
    private StaticBody3D _body;

    private StandardMaterial3D _material;

    private bool _isHighlighted = false;
    
    private const float Wing = 1.2f;   
    private const float Tip = 0.8f;     
    private const float Tail = 0.4f;    
    private const float Notch = 0.4f;
    
        public void Initialize(Vector2I chunkCoord, EditorChunkManager.ChunkDirection direction,
                           Vector3 worldPos, float yRotation)
    {
        ChunkCoord = chunkCoord;
        Direction = direction;
        Position = worldPos;
        Rotation = new Vector3(0f, yRotation, 0f);

        // --- Material --- //
        // Per-instance material so hover highlight only affects this arrow.
        // Unshaded so it's always visible regardless of lighting angle.
        // CullMode Disabled so it's visible from below (when camera orbits under).
        _material = new StandardMaterial3D();
        _material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _material.AlbedoColor = new Color(0.2f, 0.8f, 0.2f, 0.5f); // Semi-transparent green
        _material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        // --- Mesh --- //
        // Build the chevron shape using SurfaceTool. The shape is defined in
        // local space pointing toward -Z, then rotated by yRotation to face
        // the correct direction.
        _meshInstance = new MeshInstance3D();
        _meshInstance.Mesh = BuildChevronMesh();
        _meshInstance.MaterialOverride = _material;
        _meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_meshInstance);

        // --- Collision --- //
        // StaticBody3D on layer 4 so EditorMain can raycast for arrows
        // separately from terrain (layer 1) and vertex handles (layer 2).
        // CollisionMask = 0 because the arrow doesn't need to detect collisions
        // with anything — it only needs to BE detected by raycasts.
        _body = new StaticBody3D();
        _body.CollisionLayer = 4;  // Layer 3 in Godot's 1-indexed UI = bit 4
        _body.CollisionMask = 0;

        // Box collision shape sized to cover the chevron.
        // Slightly oversized for easier clicking.
        var colShape = new CollisionShape3D();
        var box = new BoxShape3D();
        box.Size = new Vector3(Wing * 2f, 0.3f, Tip + Tail);
        colShape.Shape = box;
        // Center the box on the chevron (which spans from -Tip to +Tail in Z)
        colShape.Position = new Vector3(0f, 0f, (Tail - Tip) / 2f);
        _body.AddChild(colShape);
        AddChild(_body);
    }
    
    // Builds the chevron Array Mesh
    private ArrayMesh BuildChevronMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Define the 5 vertices of the chevron (all at Y=0, flat on ground)
        Vector3 tip       = new Vector3(0f,      0f, -Tip);   // Front point
        Vector3 leftWing  = new Vector3(-Wing,   0f,  0f);    // Left outer corner
        Vector3 rightWing = new Vector3(Wing,    0f,  0f);    // Right outer corner
        Vector3 leftTail  = new Vector3(-Notch,  0f,  Tail);  // Left inner notch
        Vector3 rightTail = new Vector3(Notch,   0f,  Tail);  // Right inner notch

        // Triangle 1: tip → leftWing → leftTail (left half of chevron)
        st.AddVertex(tip);
        st.AddVertex(leftWing);
        st.AddVertex(leftTail);

        // Triangle 2: tip → leftTail → rightTail (center connecting strip)
        st.AddVertex(tip);
        st.AddVertex(leftTail);
        st.AddVertex(rightTail);

        // Triangle 3: tip → rightTail → rightWing (right half of chevron)
        st.AddVertex(tip);
        st.AddVertex(rightTail);
        st.AddVertex(rightWing);

        st.GenerateNormals();
        return st.Commit();
    }

    // Toggles the hover highlight
    public void SetHighlighted(bool highlighted)
    {
        if (_isHighlighted == highlighted) return;
        _isHighlighted = highlighted;

        if (highlighted) 
            _material.AlbedoColor = new Color(0.3f, 1.0f, 0.3f, 0.75f);
        else 
            _material.AlbedoColor = new Color(0.3f, 1.0f, 0.3f, 0.5f);
    }

}