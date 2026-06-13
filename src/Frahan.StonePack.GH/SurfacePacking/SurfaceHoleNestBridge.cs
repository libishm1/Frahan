#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Packing.TwoD;
using Rhino.Geometry;

namespace Frahan.GH.Surface
{
    /// <summary>
    /// Shared bridge from Rhino curves to the Core ContactNfpHoleNester
    /// (Frahan.Packing.TwoD) used by BOTH surface-packing components, so the
    /// hole-aware nester integration lives in exactly one place. Mirrors
    /// HoleNestComponent.CurveToLoop: uniform-by-length sampling for smooth
    /// curves (curvature-adaptive sampling makes degenerate tiny edges that
    /// slow the Minkowski NFPs), proxy-deviation measurement (feeds the
    /// deviation-compensated spacing so full-resolution outputs never overlap),
    /// CCW orientation (the Core nester expects CCW loops), and a
    /// WorldXY-parallel-plane guard (a tilted curve would nest a foreshortened
    /// projection). Explicit polylines are taken as-is with zero deviation.
    /// </summary>
    internal static class SurfaceHoleNestBridge
    {
        public const int MaxVerts = 200;          // hard cap on explicit-polyline vertices
        public const int DefaultSampleVerts = 24; // parts COST lane (collision proxy only; output is exact)
        public const int SheetSampleVerts = 192;  // sheet/holes ACCURACY lane (cheap: only PART verts drive NFP cost)

        public struct LoopResult
        {
            public List<(double X, double Y)> Loop;  // CCW; null when Reject != null
            public double PlaneZ;                     // mid-plane elevation of the curve
            public double MaxDev;                     // sampled proxy deviation (0 for explicit polylines)
            public string Reject;                     // non-null reason when Loop == null
        }

        /// <summary>Closed planar Rhino curve -> CCW loop in (x,y), with deviation + plane Z.</summary>
        public static LoopResult CurveToLoop(Curve curve, int sampleVerts)
        {
            var r = new LoopResult();
            if (sampleVerts <= 0) sampleVerts = DefaultSampleVerts;
            if (curve == null) { r.Reject = "null curve"; return r; }
            if (!curve.IsClosed) { r.Reject = "open curve"; return r; }

            IList<Point3d> pts = null;
            bool measureDeviation = false;
            if (curve.TryGetPolyline(out var pl))
            {
                pts = pl;
            }
            else
            {
                // uniform-by-length sampling (see class summary)
                measureDeviation = true;
                var seg = curve.GetLength() / sampleVerts;
                var div = seg > Rhino.RhinoMath.ZeroTolerance ? curve.DivideEquidistant(seg) : null;
                if (div != null && div.Length >= 3)
                {
                    pts = div;
                }
                else
                {
                    var divPar = curve.DivideByCount(sampleVerts, false);
                    if (divPar == null || divPar.Length < 3) { r.Reject = "degenerate"; return r; }
                    var tmp = new List<Point3d>(divPar.Length);
                    foreach (var t in divPar) tmp.Add(curve.PointAt(t));
                    pts = tmp;
                }
            }

            var n = pts.Count;
            if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < 1e-9) n--;
            if (n < 3) { r.Reject = "degenerate"; return r; }

            if (measureDeviation)
            {
                for (var i = 0; i < n; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % n];
                    var mid = new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
                    if (curve.ClosestPoint(mid, out var tcp))
                    {
                        var d = curve.PointAt(tcp).DistanceTo(mid);
                        if (d > r.MaxDev) r.MaxDev = d;
                    }
                }
            }

            double zMin = double.MaxValue, zMax = double.MinValue;
            Point3d pMin = pts[0], pMax = pts[0];
            for (var i = 0; i < n; i++)
            {
                var p = pts[i];
                if (p.Z < zMin) zMin = p.Z;
                if (p.Z > zMax) zMax = p.Z;
                pMin.X = Math.Min(pMin.X, p.X); pMin.Y = Math.Min(pMin.Y, p.Y);
                pMax.X = Math.Max(pMax.X, p.X); pMax.Y = Math.Max(pMax.Y, p.Y);
            }
            double span = Math.Max(pMax.X - pMin.X, pMax.Y - pMin.Y);
            if (zMax - zMin > 1e-6 * (1.0 + span)) { r.Reject = "not WorldXY-parallel"; return r; }
            r.PlaneZ = 0.5 * (zMin + zMax);

            var loop = new List<(double X, double Y)>(Math.Min(n, MaxVerts));
            if (n > MaxVerts)
            {
                var step = (double)n / MaxVerts;
                for (var i = 0; i < MaxVerts; i++)
                {
                    var idx = Math.Min(n - 1, (int)(i * step));
                    loop.Add((pts[idx].X, pts[idx].Y));
                }
            }
            else
            {
                for (var i = 0; i < n; i++) loop.Add((pts[i].X, pts[i].Y));
            }

            var area = SignedArea(loop);
            if (Math.Abs(area) < 1e-12) { r.Reject = "zero area"; return r; }
            if (area < 0) loop.Reverse();   // Core nester expects CCW loops
            r.Loop = loop;
            return r;
        }

        public static double SignedArea(IReadOnlyList<(double X, double Y)> loop)
        {
            double a = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                var j = (i + 1) % loop.Count;
                a += loop[i].X * loop[j].Y - loop[j].X * loop[i].Y;
            }
            return 0.5 * a;
        }

        /// <summary>
        /// Packed-2D rigid transform for a placement: rotate about the world-Z
        /// origin, then translate (the Core convention). dz lifts the part from
        /// its own input plane onto the flat chart plane (Z) so barycentric
        /// mapping locates it in the flat mesh.
        /// </summary>
        public static Transform PackTransform(HoleNestPlacement pl, double dz)
        {
            return Transform.Translation(pl.Tx, pl.Ty, dz) *
                   Transform.Rotation(pl.AngleRad, Vector3d.ZAxis, Point3d.Origin);
        }
    }
}
