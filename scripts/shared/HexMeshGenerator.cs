using Godot;
using System.Collections.Generic;

namespace HexEditor.scripts.shared;

// Creates hex tile geometry with per-vertex height data.
// The same geometry routines are used both for single-tile previews and baked chunk meshes.
public class HexMeshGenerator
{
    private const float SideDepth = 1.0f;

    private static readonly Dictionary<TileSurfaceType, StandardMaterial3D> SurfaceMaterials = new();

    private static float _cachedTileSize;
    private static Vector2[] _cachedOffsets; // [6] - (x, z) pairs

    private static void EnsureOffsets(float tileSize)
    {
        if (_cachedOffsets != null && _cachedTileSize == tileSize)
            return;

        _cachedTileSize = tileSize;
        _cachedOffsets = new Vector2[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            float angle = Mathf.Pi / 3f * i + Mathf.Pi / 6f;
            _cachedOffsets[i] = new Vector2(Mathf.Cos(angle) * tileSize, Mathf.Sin(angle) * tileSize);
        }
    }

    public static StandardMaterial3D GetSurfaceMaterial(TileSurfaceType surfaceType)
    {
        if (SurfaceMaterials.TryGetValue(surfaceType, out var material))
            return material;

        material = new StandardMaterial3D
        {
            AlbedoColor = GetSurfaceColor(surfaceType),
            Roughness = 1.0f
        };

        SurfaceMaterials[surfaceType] = material;
        return material;
    }

    public static HexMeshResult GenerateTileMesh(float[] vertexHeights, float tileSize, TileSurfaceType surfaceType)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(GetSurfaceMaterial(surfaceType));

        AppendTileGeometry(st, vertexHeights, tileSize, Transform3D.Identity);

        ArrayMesh mesh = st.Commit();
        ConvexPolygonShape3D shape = BuildTileCollisionShape(vertexHeights, tileSize, Transform3D.Identity);

        return new HexMeshResult
        {
            Mesh = mesh,
            CollisionShape = shape
        };
    }

    public static void AppendTileGeometry(SurfaceTool st, float[] vertexHeights, float tileSize, Transform3D transform)
    {
        EnsureOffsets(tileSize);

        float centerHeight = 0f;
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
            centerHeight += vertexHeights[i];
        centerHeight /= ChunkData.VerticesPerTile;

        Vector3 centerTop = new Vector3(0f, centerHeight, 0f);

        Vector3[] topVerts = new Vector3[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            topVerts[i] = new Vector3(_cachedOffsets[i].X, vertexHeights[i], _cachedOffsets[i].Y);
        }

        Vector3[] triNormals = new Vector3[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int next = (i + 1) % ChunkData.VerticesPerTile;
            triNormals[i] = CalculateTriangleNormal(centerTop, topVerts[next], topVerts[i]);
        }

        Vector3 centerNormal = Vector3.Zero;
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
            centerNormal += triNormals[i];
        centerNormal = centerNormal.Normalized();

        Vector3[] cornerNormals = new Vector3[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int prev = (i - 1 + ChunkData.VerticesPerTile) % ChunkData.VerticesPerTile;
            cornerNormals[i] = (triNormals[i] + triNormals[prev]).Normalized();
        }

        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int next = (i + 1) % ChunkData.VerticesPerTile;

            AddVertex(st, transform, centerTop, centerNormal);
            AddVertex(st, transform, topVerts[i], cornerNormals[i]);
            AddVertex(st, transform, topVerts[next], cornerNormals[next]);
        }

        float minHeight = float.MaxValue;
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            if (vertexHeights[i] < minHeight)
                minHeight = vertexHeights[i];
        }

        float bottomY = minHeight - SideDepth;

        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int next = (i + 1) % ChunkData.VerticesPerTile;

            Vector3 topA = topVerts[i];
            Vector3 topB = topVerts[next];
            Vector3 botA = new Vector3(topA.X, bottomY, topA.Z);
            Vector3 botB = new Vector3(topB.X, bottomY, topB.Z);

            Vector3 sideNormal = CalculateTriangleNormal(topA, topB, botA);

            AddVertex(st, transform, topA, sideNormal);
            AddVertex(st, transform, botA, sideNormal);
            AddVertex(st, transform, topB, sideNormal);

            AddVertex(st, transform, topB, sideNormal);
            AddVertex(st, transform, botA, sideNormal);
            AddVertex(st, transform, botB, sideNormal);
        }
    }

    public static void AppendTileTopFace(
        SurfaceTool st,
        float[] vertexHeights,
        float tileSize,
        Transform3D transform,
        float yOffset = 0.02f)
    {
        EnsureOffsets(tileSize);

        float centerHeight = 0f;
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
            centerHeight += vertexHeights[i];
        centerHeight /= ChunkData.VerticesPerTile;

        Vector3 centerTop = new Vector3(0f, centerHeight + yOffset, 0f);
        Vector3[] topVerts = new Vector3[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            topVerts[i] = new Vector3(_cachedOffsets[i].X, vertexHeights[i] + yOffset, _cachedOffsets[i].Y);
        }

        Vector3[] triNormals = new Vector3[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int next = (i + 1) % ChunkData.VerticesPerTile;
            triNormals[i] = CalculateTriangleNormal(centerTop, topVerts[next], topVerts[i]);
        }

        Vector3 centerNormal = Vector3.Zero;
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
            centerNormal += triNormals[i];
        centerNormal = centerNormal.Normalized();

        Vector3[] cornerNormals = new Vector3[ChunkData.VerticesPerTile];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int prev = (i - 1 + ChunkData.VerticesPerTile) % ChunkData.VerticesPerTile;
            cornerNormals[i] = (triNormals[i] + triNormals[prev]).Normalized();
        }

        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            int next = (i + 1) % ChunkData.VerticesPerTile;

            AddVertex(st, transform, centerTop, centerNormal);
            AddVertex(st, transform, topVerts[i], cornerNormals[i]);
            AddVertex(st, transform, topVerts[next], cornerNormals[next]);
        }
    }

    public static ConvexPolygonShape3D BuildTileCollisionShape(float[] vertexHeights, float tileSize, Transform3D transform)
    {
        EnsureOffsets(tileSize);

        float centerHeight = 0f;
        float minHeight = float.MaxValue;
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            centerHeight += vertexHeights[i];
            if (vertexHeights[i] < minHeight)
                minHeight = vertexHeights[i];
        }
        centerHeight /= ChunkData.VerticesPerTile;

        float bottomY = minHeight - SideDepth;

        var collisionPoints = new Vector3[14];
        for (int i = 0; i < ChunkData.VerticesPerTile; i++)
        {
            Vector3 top = new Vector3(_cachedOffsets[i].X, vertexHeights[i], _cachedOffsets[i].Y);
            Vector3 bottom = new Vector3(_cachedOffsets[i].X, bottomY, _cachedOffsets[i].Y);
            collisionPoints[i] = TransformPoint(transform, top);
            collisionPoints[i + ChunkData.VerticesPerTile] = TransformPoint(transform, bottom);
        }

        collisionPoints[12] = TransformPoint(transform, new Vector3(0f, centerHeight, 0f));
        collisionPoints[13] = TransformPoint(transform, new Vector3(0f, bottomY, 0f));

        var shape = new ConvexPolygonShape3D();
        shape.Points = collisionPoints;
        return shape;
    }

    private static void AddVertex(SurfaceTool st, Transform3D transform, Vector3 vertex, Vector3 normal)
    {
        st.SetNormal(TransformNormal(transform, normal));
        st.AddVertex(TransformPoint(transform, vertex));
    }

    private static Vector3 TransformPoint(Transform3D transform, Vector3 point)
    {
        return transform.Origin + transform.Basis * point;
    }

    private static Vector3 TransformNormal(Transform3D transform, Vector3 normal)
    {
        return (transform.Basis * normal).Normalized();
    }

    private static Color GetSurfaceColor(TileSurfaceType surfaceType)
    {
        return surfaceType switch
        {
            TileSurfaceType.Grass => new Color(0.33f, 0.58f, 0.27f),
            TileSurfaceType.Dirt => new Color(0.49f, 0.34f, 0.21f),
            TileSurfaceType.Sand => new Color(0.80f, 0.72f, 0.50f),
            TileSurfaceType.Rock => new Color(0.46f, 0.46f, 0.46f),
            _ => new Color(0.58f, 0.12f, 0.58f)
        };
    }

    private static Vector3 CalculateTriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        return edge1.Cross(edge2).Normalized();
    }

    public struct HexMeshResult
    {
        public ArrayMesh Mesh;
        public ConvexPolygonShape3D CollisionShape;
    }
}
