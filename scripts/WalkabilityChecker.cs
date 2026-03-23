using System.Runtime.InteropServices.Marshalling;

namespace HexEditor.scripts;

// Static utility methods for determining walkability
// Two independant methods:
// 1. Vertex Deviation - prevents individual tiles from being too warped. How far each of a tile's vertices can stray from its own average height
// 2. Step Height - prevents tiles average height from being too different from its neighboring tiles heights

public static class WalkabilityChecker
{
    // Small tolerance for floating-point precision in walkability checks.
    // The clamping math in ComputeAllowedHeightRange divides by 5 and 6,
    // which can produce values that round-trip slightly outside the allowed range.
    private const float Epsilon = 1e-4f;

    // Compute the maximum deviation of any vertex from the tile's average height
    public static float ComputeMaxDeviation(float[] vertexHeights)
    {
        // Calculate the average height of the tile's vertices
        float sum = 0f;
        for (int i = 0; i < 6; i++ )
            sum += vertexHeights[i];
        float avg = sum / 6f;
        
        // Find the vertex that deviates most from the average
        float maxDev = 0f;
        for (int i = 0; i < 6; i++)
        {
            float dev = System.Math.Abs(vertexHeights[i] - avg);
            if (dev > maxDev)
                maxDev = dev;
        }

        return maxDev;
    }
    
    // Computes the average height of a tile from its 6 vertex heights
    public static float ComputeTileAverage(float[] vertexHeights)
    {
        float sum = 0f;
        for (int i = 0; i < 6; i++)
            sum += vertexHeights[i];
        return sum / 6f;
    }
    
    // Check if a tile is walkable given the average height of its neighboring tiles
    public static bool IsTileWalkable(float[] vertexHeights, float[] neighborAvgs,
        float maxStepHeight)
    {
        float tileAvg = ComputeTileAverage(vertexHeights);
        for (int i = 0; i < neighborAvgs.Length; i++)
        {
            if (System.Math.Abs(tileAvg - neighborAvgs[i]) > maxStepHeight)
                return false;
        }

        return true;
    }
    
    // Compute the maximum height a vertex can be moved to while keeping the tile within the walkability bounds
    // Returns (minH, maxH) as the minimum and maximum heights the vertex can be moved to
    public static (float minH, float maxH) ComputeAllowedHeightRange(float[] vertexHeights, int vertexIndex, float maxOffset)
    {
        // Sum of the other 5 vertices
        float sumOthers = 0f;
        for (int i = 0; i < 6; i++)
        {
            if (i != vertexIndex)
                sumOthers += vertexHeights[i];
        }
        
        // Self-constraint only. How far the vertex can be moved from its own average height
        float min = (sumOthers - 6f * maxOffset) / 5f;
        float max = (sumOthers + 6f * maxOffset) / 5f;

        return (min, max);
    }

    public static (float minH, float maxH) ComputeStepHeightRange(float sumOthers, float[] neighborAvgs,
        float maxStepHeight)
    {
        float globalMin = float.MinValue;
        float globalMax = float.MaxValue;

        for (int i = 0; i < neighborAvgs.Length; i++)
        {
            float nAvg = neighborAvgs[i];
            float lo = 6f * (nAvg - maxStepHeight) - sumOthers;
            float hi = 6f * (nAvg + maxStepHeight) - sumOthers;

            if (lo > globalMin) globalMin = lo;
            if (hi < globalMax) globalMax = hi;
        }
        
        return (globalMin, globalMax);
    }
    
    
}