#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// StereonetProjection -- lower-hemisphere projection of discontinuity poles onto
// a stereonet of radius R. Pure managed (Rhino value types), headless-testable.
//
//   Equal-area (Schmidt / Lambert):  r = sqrt(2) * sin(theta/2)
//   Equal-angle (Wulff):             r = tan(theta/2)
//   theta = acos(|nz|)   (0 at the centre = horizontal joint / vertical pole-up,
//                         R at the primitive circle = vertical joint),
//   phi   = atan2(nx, ny)            (azimuth, clockwise from North = +Y),
//   x = R * r * sin(phi),  y = R * r * cos(phi).
// Both r-laws map theta in [0,90deg] to [0,1], so a pole always lands inside the
// primitive circle of radius R. Output is in the net's own 2D plane (z = 0);
// the GH layer places it on a base Plane.
// =============================================================================

public static class StereonetProjection
{
    /// <summary>Project a pole to net-plane (x, y) at radius R. Equal-area unless wulff=true.</summary>
    public static Point2d Project(Vector3d pole, double radius = 1.0, bool wulff = false)
    {
        var n = OrientationMath.LowerHemisphere(pole); // nz <= 0
        double theta = Math.Acos(Math.Min(1.0, Math.Abs(n.Z))); // 0..pi/2
        double r = wulff ? Math.Tan(theta / 2.0) : Math.Sqrt(2.0) * Math.Sin(theta / 2.0);
        double phi = Math.Atan2(n.X, n.Y);
        return new Point2d(radius * r * Math.Sin(phi), radius * r * Math.Cos(phi));
    }

    /// <summary>Place a projected pole onto a base plane (origin + X/Y axes scaled by the net).</summary>
    public static Point3d ProjectOnPlane(Vector3d pole, Plane net, double radius = 1.0, bool wulff = false)
    {
        var uv = Project(pole, radius, wulff);
        return net.PointAt(uv.X, uv.Y);
    }

    /// <summary>
    /// Great circle (cyclographic trace) of the PLANE whose pole is <paramref name="pole"/>,
    /// sampled as <paramref name="segments"/>+1 net-plane points. The plane's dip vectors are
    /// the unit vectors perpendicular to the pole; each is projected like a pole would be.
    /// </summary>
    public static Point3d[] GreatCircleOnPlane(Vector3d pole, Plane net, double radius = 1.0,
        bool wulff = false, int segments = 72)
    {
        var p = OrientationMath.LowerHemisphere(pole);
        p.Unitize();
        // two unit axes spanning the plane (perpendicular to the pole)
        Vector3d u = Math.Abs(p.Z) < 0.9 ? Vector3d.CrossProduct(p, Vector3d.ZAxis)
                                          : Vector3d.CrossProduct(p, Vector3d.XAxis);
        u.Unitize();
        Vector3d v = Vector3d.CrossProduct(p, u); v.Unitize();

        var pts = new Point3d[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments;
            // a direction lying IN the plane; treat it as a "pole" to project to a net point
            var dir = Math.Cos(a) * u + Math.Sin(a) * v;
            pts[i] = ProjectOnPlane(dir, net, radius, wulff);
        }
        return pts;
    }
}
