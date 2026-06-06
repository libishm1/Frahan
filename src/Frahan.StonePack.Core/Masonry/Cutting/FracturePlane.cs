#nullable disable
using System;

namespace Frahan.Masonry.Cutting;

// =============================================================================
// FracturePlane — oriented infinite plane representing a fracture / joint
// surface in a quarry or masonry block. Cuts a Slab into two convex pieces.
//
// Convention: the plane is { p : dot(p - PointOnPlane, Normal) == 0 }. Points
// with positive signed distance are on the "above" side; negative are on the
// "below" side. The Cut method emits both pieces by default.
//
// This is the SlabCutter input that corresponds to Goodman & Shi (1985)
// "Block Theory and its Application to Rock Engineering" joint planes:
// each fracture is an infinite, oriented half-space boundary. Finite-extent
// fractures (which produce non-convex output) are a separate Phase-2 effort.
// =============================================================================

/// <summary>
/// An oriented infinite plane that participates in slab cutting. Stored as
/// a point + unit normal; the plane equation is
/// <c>dot(p - point, normal) = 0</c>.
/// </summary>
public sealed class FracturePlane
{
    public FracturePlane(
        double pointX, double pointY, double pointZ,
        double normalX, double normalY, double normalZ)
    {
        double n2 = normalX * normalX + normalY * normalY + normalZ * normalZ;
        if (n2 < 1e-24)
            throw new ArgumentException(
                "normal must be non-zero", nameof(normalX));

        double inv = 1.0 / Math.Sqrt(n2);
        PointX = pointX; PointY = pointY; PointZ = pointZ;
        NormalX = normalX * inv; NormalY = normalY * inv; NormalZ = normalZ * inv;
    }

    public double PointX { get; }
    public double PointY { get; }
    public double PointZ { get; }
    public double NormalX { get; }
    public double NormalY { get; }
    public double NormalZ { get; }

    /// <summary>
    /// Signed distance of (x, y, z) to the plane. Positive on the side
    /// indicated by Normal, negative on the other side, zero on the plane.
    /// </summary>
    public double SignedDistance(double x, double y, double z)
    {
        return (x - PointX) * NormalX + (y - PointY) * NormalY + (z - PointZ) * NormalZ;
    }

    public override string ToString() =>
        $"FracturePlane(p=({PointX:0.##},{PointY:0.##},{PointZ:0.##}), " +
        $"n=({NormalX:0.##},{NormalY:0.##},{NormalZ:0.##}))";
}
