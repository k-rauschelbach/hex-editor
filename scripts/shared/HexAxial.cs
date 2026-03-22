using System;

namespace HexEditor.scripts.shared;

public readonly struct HexAxial
{
    public int Q { get; } // column
    public int R { get; } // row
    
    public HexAxial(int q, int r)
    {
        Q = q;
        R = r;
    }
    
    public override string ToString() => $"({Q},{R})";
    
    // for dictionary keys
    public bool Equals(HexAxial other) => Q == other.Q && R == other.R;

    public override bool Equals(object obj) => obj is HexAxial other && Equals(other);
    
    public override int GetHashCode() => HashCode.Combine(Q, R);

    public static bool operator ==(HexAxial a, HexAxial b) => a.Equals(b);
    public static bool operator !=(HexAxial a, HexAxial b) => !a.Equals(b);

}