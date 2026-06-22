#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Decomposes a planar panel's closed boundary into 4 whole sides between
    /// detected corners. Corners = the contour points nearest the MINIMUM-AREA
    /// bounding rectangle's corners (rotating calipers on the convex hull), which is
    /// robust to wavy seams (a wave peak never sits at a rect corner) and works on
    /// rotated pieces (the box is oriented, not axis-aligned). Consumes
    /// <see cref="Panel.SourceContour"/> directly; no hash index or ICP.
    /// </summary>
    internal static class WholeSideExtractor
    {
        /// <summary>Uniform arc-length samples around the boundary.</summary>
        public const int ArcSamples = 200;

        /// <summary>Points per side in the canonical frame (ryan SIDE_RESAMPLE_VERTEX_COUNT analogue).</summary>
        public const int SideResample = 40;

        /// <summary>Flat-side gate: maxPerpDeviation/chord below this = border edge, excluded from matching.</summary>
        public const double FlatThreshold = 0.04;

        public static List<WholeSide> Extract(Panel panel)
        {
            var result = new List<WholeSide>();
            if (panel == null) return result;
            var crv = panel.SourceContour;
            if (crv == null) return result;

            // 2D coords are the world X/Y of the (coplanar) contour. NOT a projection to
            // Panel.LocalFrame: PlanarityTester's PCA axes have arbitrary sign, which would
            // mirror some pieces' canonical seams and break complementary matching. The
            // solver assumes parts lie in the world XY plane (orient tilted input first).
            var ts = crv.DivideByCount(ArcSamples, true);
            if (ts == null || ts.Length < 4) return result;
            int m = Math.Min(ArcSamples, ts.Length);

            var p3 = new Point3d[m];
            var p2 = new Point2d[m];
            for (int i = 0; i < m; i++)
            {
                var p = crv.PointAt(ts[i]);
                p3[i] = p;
                p2[i] = new Point2d(p.X, p.Y);
            }

            // Normalize traversal to CCW so the complementary-seam mate is consistent.
            if (SignedArea(p2) < 0.0)
            {
                Array.Reverse(p3);
                Array.Reverse(p2);
            }

            int[] corners = FindCornerIndices(p2);
            if (corners.Length < 3) return result;

            for (int s = 0; s < corners.Length; s++)
            {
                int a = corners[s];
                int b = corners[(s + 1) % corners.Length];
                var side2 = new List<Point2d>();
                int idx = a;
                while (true)
                {
                    side2.Add(p2[idx]);
                    if (idx == b) break;
                    idx = (idx + 1) % m;
                }
                double chord = p2[a].DistanceTo(p2[b]);
                result.Add(new WholeSide
                {
                    PanelId = panel.Id,
                    SideIndex = s,
                    StartCorner = p3[a],
                    EndCorner = p3[b],
                    ChordLength = chord,
                    IsFlat = chord > 1e-9 && Flatness(side2) < FlatThreshold,
                    Canonical = Canonical(side2, false),
                    CanonicalFlipped = Canonical(side2, true),
                });
            }
            return result;
        }

        // 4 contour-sample indices nearest the min-area bounding-rect corners.
        internal static int[] FindCornerIndices(Point2d[] pts)
        {
            var hull = ConvexHull(pts);
            if (hull.Count < 3) return new int[0];

            double bestArea = double.MaxValue;
            Point2d[] rect = null;
            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                double axx = b.X - a.X, axy = b.Y - a.Y;
                double alen = Math.Sqrt(axx * axx + axy * axy);
                if (alen < 1e-9) continue;
                axx /= alen; axy /= alen;
                double ayx = -axy, ayy = axx;
                double mnu = double.MaxValue, mxu = double.MinValue, mnv = double.MaxValue, mxv = double.MinValue;
                foreach (var h in hull)
                {
                    double dx = h.X - a.X, dy = h.Y - a.Y;
                    double u = dx * axx + dy * axy, v = dx * ayx + dy * ayy;
                    if (u < mnu) mnu = u; if (u > mxu) mxu = u;
                    if (v < mnv) mnv = v; if (v > mxv) mxv = v;
                }
                double area = (mxu - mnu) * (mxv - mnv);
                // strict-less with epsilon: deterministic pick on near-equal orientations.
                if (area < bestArea - 1e-9)
                {
                    bestArea = area;
                    rect = new[]
                    {
                        new Point2d(a.X + axx * mnu + ayx * mnv, a.Y + axy * mnu + ayy * mnv),
                        new Point2d(a.X + axx * mxu + ayx * mnv, a.Y + axy * mxu + ayy * mnv),
                        new Point2d(a.X + axx * mxu + ayx * mxv, a.Y + axy * mxu + ayy * mxv),
                        new Point2d(a.X + axx * mnu + ayx * mxv, a.Y + axy * mnu + ayy * mxv),
                    };
                }
            }
            if (rect == null) return new int[0];

            var cs = new List<int>();
            foreach (var rc in rect)
            {
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < pts.Length; i++)
                {
                    double d = pts[i].DistanceTo(rc);
                    if (d < bd) { bd = d; best = i; }
                }
                cs.Add(best);
            }
            return cs.Distinct().OrderBy(x => x).ToArray();
        }

        // Andrew monotone-chain convex hull on the 2D samples (CCW).
        private static List<Point2d> ConvexHull(Point2d[] pts)
        {
            var p = pts
                .Select(a => new Point2d(Math.Round(a.X, 5), Math.Round(a.Y, 5)))
                .Distinct()
                .OrderBy(a => a.X).ThenBy(a => a.Y)
                .ToList();
            if (p.Count < 3) return p;
            Func<Point2d, Point2d, Point2d, double> cross = (o, a, b) =>
                (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var lo = new List<Point2d>();
            foreach (var pt in p)
            {
                while (lo.Count >= 2 && cross(lo[lo.Count - 2], lo[lo.Count - 1], pt) <= 0) lo.RemoveAt(lo.Count - 1);
                lo.Add(pt);
            }
            var up = new List<Point2d>();
            for (int i = p.Count - 1; i >= 0; i--)
            {
                var pt = p[i];
                while (up.Count >= 2 && cross(up[up.Count - 2], up[up.Count - 1], pt) <= 0) up.RemoveAt(up.Count - 1);
                up.Add(pt);
            }
            lo.RemoveAt(lo.Count - 1); up.RemoveAt(up.Count - 1); lo.AddRange(up);
            return lo;
        }

        private static double SignedArea(Point2d[] p)
        {
            double s = 0.0;
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Length];
                s += a.X * b.Y - b.X * a.Y;
            }
            return 0.5 * s;
        }

        // max perpendicular deviation from the chord, normalized by chord length.
        private static double Flatness(List<Point2d> side)
        {
            var a = side[0]; var b = side[side.Count - 1];
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len = Math.Sqrt(abx * abx + aby * aby);
            if (len < 1e-9) return 0.0;
            abx /= len; aby /= len;
            double mx = 0.0;
            foreach (var pp in side)
            {
                double dx = pp.X - a.X, dy = pp.Y - a.Y;
                double perp = Math.Abs(dx * aby - dy * abx);
                if (perp > mx) mx = perp;
            }
            return mx / len;
        }

        // Resample the side to K points, then pose into the endpoint-chord frame
        // (start at origin, end on +x). flip = traverse the side reversed first.
        private static Point2d[] Canonical(List<Point2d> side, bool flip)
        {
            var src = flip ? Enumerable.Reverse(side).ToList() : side;
            var poly = new Polyline(src.Select(q => new Point3d(q.X, q.Y, 0.0))).ToPolylineCurve();
            var ts = poly.DivideByCount(SideResample - 1, true);
            int k = (ts == null) ? 0 : Math.Min(SideResample, ts.Length);
            var raw = new Point2d[k];
            for (int i = 0; i < k; i++) { var p = poly.PointAt(ts[i]); raw[i] = new Point2d(p.X, p.Y); }
            if (k == 0) return new Point2d[0];

            var o = raw[0];
            double dirx = raw[k - 1].X - o.X, diry = raw[k - 1].Y - o.Y;
            double ang = -Math.Atan2(diry, dirx);
            double cs = Math.Cos(ang), sn = Math.Sin(ang);
            var outp = new Point2d[k];
            for (int i = 0; i < k; i++)
            {
                double dx = raw[i].X - o.X, dy = raw[i].Y - o.Y;
                outp[i] = new Point2d(dx * cs - dy * sn, dx * sn + dy * cs);
            }
            return outp;
        }
    }
}
