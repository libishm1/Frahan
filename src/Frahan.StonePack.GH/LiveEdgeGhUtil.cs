using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.GH;

// Shared helpers for the Live Edge flooring components (Classify/Match/Trim/Stagger).
internal static class LiveEdgeGhUtil
{
    // Convert a (closed) curve to a dense list of loop points, deduped at the seam.
    public static List<Point3d> CurveToLoop(Curve crv, int n = 200)
    {
        if (crv == null) return null;
        Point3d[] pts;
        crv.DivideByCount(n, true, out pts);
        if (pts == null || pts.Length < 8)
        {
            Polyline pl;
            if (crv.TryGetPolyline(out pl)) return new List<Point3d>(pl);
            return null;
        }
        var list = new List<Point3d>(pts);
        if (list.Count > 1 && list[0].DistanceTo(list[list.Count - 1]) < 1e-6) list.RemoveAt(list.Count - 1);
        return list;
    }
}
