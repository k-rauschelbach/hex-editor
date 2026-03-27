using Godot;
using System;
using System.Collections.Generic;

namespace HexEditor.scripts.shared;

public static class ChunkMeshBuilder
{
    private static readonly Basis TileBasis = Basis.Identity.Rotated(Vector3.Up, Mathf.Pi / 6f);

    public static ArrayMesh BuildChunkMesh(ChunkData data, int chunkWidth, int chunkHeight, float tileSize)
    {
        var surfaceTools = new Dictionary<TileSurfaceType, SurfaceTool>();

        float[] vertexBuffer = new float[ChunkData.VerticesPerTile];
        float[] localHeightBuffer = new float[ChunkData.VerticesPerTile];
        int qStart = data.ChunkX * chunkWidth;
        int rStart = data.ChunkY * chunkHeight;

        for (int dq = 0; dq < chunkWidth; dq++)
        {
            for (int dr = 0; dr < chunkHeight; dr++)
            {
                data.GetVertexHeightsNonAlloc(dq, dr, vertexBuffer);

                float avgHeight = 0f;
                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                {
                    avgHeight += vertexBuffer[v];
                }
                avgHeight /= ChunkData.VerticesPerTile;

                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                {
                    localHeightBuffer[v] = vertexBuffer[v] - avgHeight;
                }

                HexAxial axial = new HexAxial(qStart + dq, rStart + dr);
                Vector3 worldPos = HexAxialMath.AxialToWorld(axial, tileSize, avgHeight);
                Transform3D transform = new Transform3D(TileBasis, worldPos);

                TileSurfaceType surfaceType = data.GetSurfaceType(dq, dr);
                if (!surfaceTools.TryGetValue(surfaceType, out SurfaceTool st))
                {
                    st = new SurfaceTool();
                    st.Begin(Mesh.PrimitiveType.Triangles);
                    st.SetMaterial(HexMeshGenerator.GetSurfaceMaterial(surfaceType));
                    surfaceTools[surfaceType] = st;
                }

                HexMeshGenerator.AppendTileGeometry(st, localHeightBuffer, tileSize, transform);
            }
        }

        ArrayMesh mesh = null;
        foreach (TileSurfaceType surfaceType in Enum.GetValues<TileSurfaceType>())
        {
            if (!surfaceTools.TryGetValue(surfaceType, out SurfaceTool st))
                continue;

            mesh = st.Commit(mesh);
        }

        return mesh;
    }

    public static ArrayMesh BuildWalkabilityOverlayMesh(
        ChunkData data,
        int chunkWidth,
        int chunkHeight,
        float tileSize,
        Func<int, int, bool> isTileWalkable,
        Material overlayMaterial)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(overlayMaterial);

        bool hasGeometry = false;
        float[] vertexBuffer = new float[ChunkData.VerticesPerTile];
        float[] localHeightBuffer = new float[ChunkData.VerticesPerTile];
        int qStart = data.ChunkX * chunkWidth;
        int rStart = data.ChunkY * chunkHeight;

        for (int dq = 0; dq < chunkWidth; dq++)
        {
            for (int dr = 0; dr < chunkHeight; dr++)
            {
                if (isTileWalkable(dq, dr))
                    continue;

                data.GetVertexHeightsNonAlloc(dq, dr, vertexBuffer);

                float avgHeight = 0f;
                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                {
                    avgHeight += vertexBuffer[v];
                }
                avgHeight /= ChunkData.VerticesPerTile;

                for (int v = 0; v < ChunkData.VerticesPerTile; v++)
                {
                    localHeightBuffer[v] = vertexBuffer[v] - avgHeight;
                }

                HexAxial axial = new HexAxial(qStart + dq, rStart + dr);
                Vector3 worldPos = HexAxialMath.AxialToWorld(axial, tileSize, avgHeight);
                Transform3D transform = new Transform3D(TileBasis, worldPos);

                HexMeshGenerator.AppendTileTopFace(st, localHeightBuffer, tileSize, transform);
                hasGeometry = true;
            }
        }

        return hasGeometry ? st.Commit() : null;
    }
}
