#nullable disable
using System;

namespace Frahan.Masonry.DataModel;

/// <summary>
/// One vertex of a block-block contact polygon, expressed in world
/// coordinates. The CRA / RBE force vector is indexed per contact vertex
/// (4 components per vertex in penalty form: f_n_pos, f_n_neg, f_t1, f_t2).
/// </summary>
public sealed class ContactVertex : IEquatable<ContactVertex>
{
    public ContactVertex(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public bool Equals(ContactVertex other) =>
        other != null && X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object obj) => obj is ContactVertex v && Equals(v);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = X.GetHashCode();
            h = (h * 397) ^ Y.GetHashCode();
            h = (h * 397) ^ Z.GetHashCode();
            return h;
        }
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
