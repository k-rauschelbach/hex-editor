using System;
using System.Collections.Generic;
using Godot;

namespace HexEditor.scripts.shared;

public static class HexAxialMath
{
    // set the base direction of rotation for the tile neighbors. East is 0, then counterclockwise
    private static readonly HexAxial[] Directions =
    {
        new HexAxial(1, -1), // 0 Northeast
        new HexAxial(0, -1), // 1 North
        new HexAxial(-1, 0), // 2 Northwest
        new HexAxial(-1, 1), // 3 Southwest
        new HexAxial(0, 1), // 4 South
        new HexAxial(1, 0) // 5 Southeast
    };
    
    // method for returning the neighbor of a given tile in a given direction, using the above Directions
    public static HexAxial Neighbor(HexAxial origin, int directionIndex)
    {
        // wrap directionIndex around to the range [0, 5]
        var d = Directions[Mathf.PosMod(directionIndex, 6)];
        return new HexAxial(origin.Q + d.Q, origin.R + d.R);
    }

    // method for returning all neighbors of a given tile
    public static IEnumerable<HexAxial> GetNeighbors(HexAxial origin)
    {
        for (int i = 0; i < Directions.Length; i++)
        {
            var dir = Directions[i];
            yield return new HexAxial(origin.Q + dir.Q, origin.R + dir.R);
        }
    }
    
    /* Axial -> World Conversion
     Flat-Top hexagonal grid coordinates to Cartesian coordinates.
     XZ is the 2D Plane
     Y is up for height
     
     tileSize is hex radius, centerpoint to any vertex
     
    */

    // convert a HexAxial coordinate to a world position in XYZ. Y is set at 0 by default
    public static Vector3 AxialToWorld(HexAxial coord, float tileSize, float y = 0f)
    {
        float x = tileSize * (1.5f * coord.Q);
        float z = tileSize * (Mathf.Sqrt(3f) * (coord.R + coord.Q * 0.5f));
        
        return new Vector3(x, y, z);
    }
    
    // convert a World Position from XYZ to the nearest HexAxial Position as an HexAxial
    public static HexAxial WorldToAxial(Vector3 worldPos, float tileSize)
    {
        float x = worldPos.X;
        float z = worldPos.Z;
    
        float q = (2f / 3f * x) / tileSize;
        float r = (-1f / 3f * x + Mathf.Sqrt(3f) / 3f * z) / tileSize;

        return AxialRound(q, r);
    }
    
    // return a distance measurement in hex steps between two HexAxial coordinates
    public static int Distance(HexAxial a, HexAxial b)
    {
        int ax = a.Q;
        int az = a.R;
        int ay = -ax - az;

        int bx = b.Q;
        int bz = b.R;
        int by = -bx - bz;

        return (Math.Abs(ax - bx) + Math.Abs(ay - by) + Math.Abs(az - bz)) / 2;
    }
    
    // World to Axial can give fractional q,r values, so round them to get the nearest hex
    public static HexAxial AxialRound(float q, float r)
    {
        // cube coordination
        float x = q;
        float z = r;
        float y = -x - z;
        
        // round each component to nearest integer
        int rx = Mathf.RoundToInt(x);
        int ry = Mathf.RoundToInt(y);
        int rz = Mathf.RoundToInt(z);
        
        // find out which component shifted the most from rounding
        float xDiff = Mathf.Abs(rx - x);
        float yDiff = Mathf.Abs(ry - y);
        float zDiff = Mathf.Abs(rz - z);
        
        // adjust largest shift to make sure x+y+z = 0
        if (xDiff > yDiff && xDiff > zDiff)
        {
            rx = -ry - rz;
        }
        else if (yDiff > zDiff)
        {
            ry = -rx - rz;
        }
        else
        {
            rz = -rx - ry;
        }
        
        return new HexAxial(rx, rz);
        
    }
}