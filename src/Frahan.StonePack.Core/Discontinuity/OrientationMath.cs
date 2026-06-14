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

    private const double D2R = Math.PI / 180.0;

    // Inverse of DipDipDir: build the lower-hemisphere unit pole for a measured
    // (dip, dipDir). Used by CSV / GeoJSON ingest where orientations are recorded
    // as dip/dip-direction rather than a normal vector.
    //
    // DipDipDir reads the LOWER-hemisphere normal m (mz <= 0) as
    //   dip = acos(|mz|),  dipDir = atan2(mx, my) mod 360.
    // Inverting that requires mz = -cos(dip) (down), and the horizontal part
    // aligned with the dip azimuth: (mx, my) = sin(dip) * (sin dipDir, cos dipDir).
    // NB: this is NOT (.., .., +cos dip) folded — folding a +z normal negates the
    // horizontal part too and yields a dipDir off by 180 deg. The z sign is the
    // only thing that flips. LowerHemisphere() below is a safe normaliser /
    // equator tie-break for the vertical (dip = 90) case.
    public static Vector3d NormalFromDipDipDir(double dipDeg, double dipDirDeg)
    {
        double dip = dipDeg * D2R, dd = dipDirDeg * D2R;
        double s = Math.Sin(dip);
        var n = new Vector3d(s * Math.Sin(dd), s * Math.Cos(dd), -Math.Cos(dip));
        return LowerHemisphere(n);
    }

    // Acute angle (deg) between two orientations treated as AXES (n and -n equal).
    public static double AxialAngleDeg(Vector3d a, Vector3d b)
    {
        a.Unitize(); b.Unitize();
        double d = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z);
        return Math.Acos(Math.Min(1.0, d)) * R2D;
    }
}
