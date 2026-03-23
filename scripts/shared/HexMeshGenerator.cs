using Godot;

namespace HexEditor.scripts.shared;

// Creates hex tile with per-vertex height data

// Layout
// 0 = east 0 degrees
// 1 = northeast 60 degrees
// 2 = northwest 120 degrees
// 3 = west 180 degrees
// 4 = southwest 240 degrees
// 5 = southeast 300 degrees


public class HexMeshGenerator
{
    // hex tile depth
    private const float SideDepth = 1.0f;
    
    // default mesh material for tiles
    private static StandardMaterial3D _sharedMaterial;
    
    // Vertex Offset lookup table
    private static float _cachedTileSize;
    private static Vector2[] _cachedOffsets; // [6] - (x, z) pairs

    private static void EnsureOffsets(float tileSize)
    {
        // Recompute offsets if tile size has changed
        if (_cachedOffsets != null && _cachedTileSize == tileSize)
            return;

        _cachedTileSize = tileSize;
        _cachedOffsets = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Pi / 3f * i + Mathf.Pi / 6f;
            _cachedOffsets[i] = new Vector2(Mathf.Cos(angle) * tileSize, Mathf.Sin(angle) * tileSize);
        }
    }
    
    // Generate the hex tile from vertex heights
    // Return a struct containing Mesh and CollisionShape

    public static HexMeshResult GenerateTileMesh(float[] vertexHeights, float tileSize)
    {
        if (_sharedMaterial == null)
        {
            _sharedMaterial = new StandardMaterial3D();
            _sharedMaterial.AlbedoColor = new Color(0.458f, 0.458f, 0.458f);
        }
        
        EnsureOffsets(tileSize);

        // Calculate the global position of hex vertices

        // Get the vertex average height
        float centerHeight = 0f;
        for (int i = 0; i < 6; i++) centerHeight += vertexHeights[i];
        centerHeight /= 6f;

        Vector3 centerTop = new Vector3(0, centerHeight, 0);

        // Calculate the 6 corner positions
        Vector3[] topVerts = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            topVerts[i] = new Vector3(_cachedOffsets[i].X, vertexHeights[i], _cachedOffsets[i].Y);
        }

        // Build the top face

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        st.SetMaterial(_sharedMaterial);

        // Compute flat normals for the 6 top-face triangles
        Vector3[] triNormals = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            triNormals[i] = CalculateTriangleNormal(centerTop, topVerts[next], topVerts[i]);
        }

        // Smooth normal for center = average of all 6 triangle normals
        Vector3 centerNormal = Vector3.Zero;
        for (int i = 0; i < 6; i++) centerNormal += triNormals[i];
        centerNormal = centerNormal.Normalized();

        // Smooth normal for each corner = average of the two triangles sharing it
        Vector3[] cornerNormals = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            int prev = (i - 1 + 6) % 6;
            cornerNormals[i] = (triNormals[i] + triNormals[prev]).Normalized();
        }

        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;

            st.SetNormal(centerNormal);
            st.AddVertex(centerTop);

            st.SetNormal(cornerNormals[i]);
            st.AddVertex(topVerts[i]);

            st.SetNormal(cornerNormals[next]);
            st.AddVertex(topVerts[next]);
        }

        // Side walls

        // Find lowest vertex
        float minHeight = float.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            if (vertexHeights[i] < minHeight)
                minHeight = vertexHeights[i];
        }

        float bottomY = minHeight - SideDepth;

        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;

            Vector3 topA = topVerts[i];
            Vector3 topB = topVerts[next];
            Vector3 botA = new Vector3(topA.X, bottomY, topA.Z);
            Vector3 botB = new Vector3(topB.X, bottomY, topB.Z);

            // Side wall normals
            Vector3 sideNormal = CalculateTriangleNormal(topA, topB, botA);

            // Triangle 1 of side
            st.SetNormal(sideNormal);
            st.AddVertex(topA);

            st.SetNormal(sideNormal);
            st.AddVertex(botA);

            st.SetNormal(sideNormal);
            st.AddVertex(topB);

            // Triangle 2 of side
            st.SetNormal(sideNormal);
            st.AddVertex(topB);

            st.SetNormal(sideNormal);
            st.AddVertex(botA);

            st.SetNormal(sideNormal);
            st.AddVertex(botB);

        }

        ArrayMesh mesh = st.Commit();

        // Collision Shape

        var collisionPoints = new Vector3[14];
        for (int i = 0; i < 6; i++)
        {
            collisionPoints[i] = topVerts[i];
            collisionPoints[i + 6] = new Vector3(topVerts[i].X, bottomY, topVerts[i].Z);
        }

        collisionPoints[12] = centerTop;
        collisionPoints[13] = new Vector3(0, bottomY, 0);

        var shape = new ConvexPolygonShape3D();
        shape.Points = collisionPoints;

        return new HexMeshResult
        {
            Mesh = mesh,
            CollisionShape = shape
        };
    }
    
    // Calculate normal vector for triangles
    private static Vector3 CalculateTriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        return edge1.Cross(edge2).Normalized();
    }
    
    // HexMeshResult struct
    public struct HexMeshResult
    {
        public ArrayMesh Mesh;
        public ConvexPolygonShape3D CollisionShape;
    }
    
}