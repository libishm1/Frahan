#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// OrientationMath -- geological dip / dip-direction / strike from a plane normal.
// Convention: x = East, y = North, z = Up. The geological pole is the
// down-dipping normal, so the normal is first folded to the lower hemisphere.
//   dip     = acos(|nz|)            (0 horizontal .. 90 vertical)
//   dipdir  = atan2(nx, ny) mod 360 (clockwise from North, the azimuth dipped toward)
//   strike  = (dipdir - 90) mod 360 (right-hand rule)
// Verified: n=(0,0.5,-0.866) -> dip 30, dipdir 0 (N); n=(0.5,0,-0.866) -> dipdir 90 (E).
// =============================================================================

public static class OrientationMath
{
    private const double R2D = 180.0 / Math.PI;

    // Fold a normal to the lower hemisphere (nz <= 0), with a deterministic
    // tie-break for vertical planes so antipodal poles map consistently.
    public static Vector3d LowerHemisphere(Vector3d n)
    {
        n.Unitize();
        if (n.Z > 1e-12) return -n;
        if (Math.Abs(n.Z) <= 1e-12)
        {
            // vertical plane: break the equator tie by x then y
            if (n.X < -1e-12 || (Math.Abs(n.X) <= 1e-12 && n.Y < 0)) return -n;
        }
        return n;
    }

    // Returns (dipDeg in [0,90], dipDirDeg in [0,360)).
    public static (double dip, double dipDir) DipDipDir(Vector3d normal)
    {
        var n = LowerHemisphere(normal);
        double dip = Math.Acos(Math.Min(1.0, Math.Abs(n.Z))) * R2D;
        double dipdir;
        if (Math.Abs(n.X) < 1e-12 && Math.Abs(n.Y) < 1e-12) dipdir = 0.0; // horizontal: undefined -> 0
        else dipdir = (Math.Atan2(n.X, n.Y) * R2D + 360.0) % 360.0;
        return (dip, dipdir);
    }

    public static double Strike(double dipDirDeg) => (dipDirDeg - 90.0 + 360.0) % 360.0;

    // Acute angle (deg) between two orientations treated as AXES (n and -n equal).
    public static double AxialAngleDeg(Vector3d a, Vector3d b)
    {
        a.Unitize(); b.Unitize();
        double d = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z);
        return Math.Acos(Math.Min(1.0, d)) * R2D;
    }
}
