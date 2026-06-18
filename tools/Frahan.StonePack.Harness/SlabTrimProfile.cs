#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Frahan.StonePack.Harness
{
    /// <summary>
    /// Headless test of the greedy convex-hull SLAB TRIM core (Rhino-free). An irregular slab "blob"
    /// (closed polygon) is trimmed to a convex blank by straight tangential half-plane cuts, each chosen
    /// greedily to remove the most waste per unit cut length. Dumps the blob, hull, cuts, trimmed polygon
    /// and metrics for a matplotlib render. usage: --slabtrim &lt;outdir&gt; [maxCuts] [kerf]
    /// </summary>
    internal static class SlabTrimProfile
    {
        public static int Run(string[] args)
        {
            string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "slabtrim");
            int maxCuts = args.Length > 2 ? int.Parse(args[2]) : 8;
            double kerf = args.Length > 3 ? double.Parse(args[3]) : 0.01;
            Directory.CreateDirectory(outDir);

            double eps = args.Length > 4 ? double.Parse(args[4]) : 0.06;
            var blob = MakeBlob();
            var r = GreedyConvexTrim(blob, maxCuts, kerf, 1.0);          // mode: convex blank
            var k = KerfFollow(blob, eps);                              // mode: concave kerf-follow
            var hull = ConvexHull(blob);

            void Dump(string f, IEnumerable<double[]> pts) => File.WriteAllLines(Path.Combine(outDir, f), pts.Select(p => $"{p[0]:F4},{p[1]:F4}"));
            void DumpCuts(string f, IEnumerable<(double[], double[])> cs) => File.WriteAllLines(Path.Combine(outDir, f), cs.Select(c => $"{c.Item1[0]:F4},{c.Item1[1]:F4},{c.Item2[0]:F4},{c.Item2[1]:F4}"));
            Dump("blob.csv", blob); Dump("hull.csv", hull);
            Dump("convex_trimmed.csv", r.Trimmed); DumpCuts("convex_cuts.csv", r.Cuts);
            Dump("kerf_trimmed.csv", k.Simplified); DumpCuts("kerf_cuts.csv", k.Cuts);

            Console.WriteLine($"blob area={Area(blob):F3} verts={blob.Count}  hull verts={hull.Count}");
            Console.WriteLine($"CONVEX blank : cuts={r.CutCount} recovered={r.RecoveredPct:F1}% cutLength={r.TotalCutLength:F2} convex={r.IsConvex}");
            Console.WriteLine($"KERF-follow  : cuts={k.CutCount} recovered={k.RecoveredPct:F1}% cutLength={k.TotalCutLength:F2} (eps={eps})");
            Console.WriteLine($"DUMP -> {outDir}");
            return 0;
        }

        // ---- a granite-slab "blob": rough rectangle with protruding lobes + a notch ----
        private static List<double[]> MakeBlob()
        {
            var pts = new List<double[]>();
            // parametric wavy rectangle 3.0 x 2.0 with 3 bumps + 1 notch, CCW
            int n = 80;
            for (int i = 0; i < n; i++)
            {
                double t = 2 * Math.PI * i / n;
                double rx = 1.5, ry = 1.0;
                double x = rx * Math.Cos(t), y = ry * Math.Sin(t);
                // protruding lobes (positive bump) + one notch (negative) on the boundary
                double bump = 0.32 * Math.Max(0, Math.Cos(3 * t - 0.6))          // 3 lobes
                            - 0.30 * Math.Max(0, Math.Cos(t - 2.4) - 0.85) * 8;  // 1 sharp notch
                double nx = Math.Cos(t), ny = Math.Sin(t);
                pts.Add(new[] { x + bump * nx, y + bump * ny });
            }
            return pts;
        }

        // ---- greedy convex trim by half-plane cuts at reflex vertices ----
        private static (List<double[]> Trimmed, List<(double[], double[])> Cuts, double RecoveredPct,
                        double TotalCutLength, int CutCount, bool IsConvex) GreedyConvexTrim(
            List<double[]> poly, int maxCuts, double kerf, double tolDeg)
        {
            var P = EnsureCcw(poly);
            double area0 = Area(P);
            var cuts = new List<(double[], double[])>();
            double tol = tolDeg * Math.PI / 180.0;

            for (int it = 0; it < maxCuts; it++)
            {
                int worst = -1; double worstReflex = -tol;
                for (int i = 0; i < P.Count; i++)
                {
                    var a = P[(i - 1 + P.Count) % P.Count]; var b = P[i]; var c = P[(i + 1) % P.Count];
                    double cr = Cross(Sub(b, a), Sub(c, b));      // < 0 = reflex (CCW)
                    double mag = Math.Atan2(-cr, Dot(Sub(b, a), Sub(c, b)));  // turn angle, + when reflex
                    if (-cr > 1e-9 && mag > worstReflex) { worstReflex = mag; worst = i; }
                }
                if (worst < 0) break;   // convex

                // cut line = supporting line of the edge entering the reflex vertex, extended; keep the side
                // with the polygon centroid (removes the reflex wedge).
                var v = P[worst]; var pv = P[(worst - 1 + P.Count) % P.Count];
                var dir = Norm(Sub(v, pv));
                var nrm = new[] { -dir[1], dir[0] };            // line normal
                var ctr = Centroid(P);
                double sgn = Dot(nrm, Sub(ctr, pv)) >= 0 ? 1 : -1;
                // keep where sgn*nrm.(x-pv) >= 0
                var seg = LinePolygonSegment(pv, dir, P);
                if (seg != null) cuts.Add(seg.Value);
                P = HalfPlaneClip(P, pv, new[] { sgn * nrm[0], sgn * nrm[1] });
                if (P.Count < 3) break;
            }

            double recovered = Area(P) / area0 * 100.0;
            double cutLen = cuts.Sum(c => Len(Sub(c.Item2, c.Item1)));
            bool convex = IsConvex(P);
            return (P, cuts, recovered, cutLen, cuts.Count, convex);
        }

        // ---- concave KERF-FOLLOW: fewest straight kerfs that stay within eps of the boundary
        // (Imai-Iri min-# polygonal approximation via a fewest-edges shortcut path). Keeps the concave
        // shape, recovers nearly all the area, at the cost of more cuts than the convex trim. ----
        private static (List<double[]> Simplified, List<(double[], double[])> Cuts, double RecoveredPct,
                        double TotalCutLength, int CutCount) KerfFollow(List<double[]> poly, double eps)
        {
            var P = EnsureCcw(poly); int n = P.Count;
            var minSeg = Enumerable.Repeat(int.MaxValue, n).ToArray();
            var pred = Enumerable.Repeat(-1, n).ToArray();
            minSeg[0] = 0;
            for (int j = 1; j < n; j++)
                for (int i = 0; i < j; i++)
                    if (minSeg[i] != int.MaxValue && minSeg[i] + 1 < minSeg[j] && Admissible(P, i, j, eps))
                    { minSeg[j] = minSeg[i] + 1; pred[j] = i; }
            var idx = new List<int>(); int c = n - 1; while (c >= 0) { idx.Add(c); c = pred[c]; } idx.Reverse();
            var simp = idx.Select(i => P[i]).ToList();
            var cuts = new List<(double[], double[])>();
            for (int i = 0; i < simp.Count; i++) cuts.Add((simp[i], simp[(i + 1) % simp.Count]));
            double rec = Math.Abs(Area(simp)) / Math.Abs(Area(P)) * 100.0;
            double len = cuts.Sum(cc => Len(Sub(cc.Item2, cc.Item1)));
            return (simp, cuts, rec, len, cuts.Count);
        }
        // chord P[i]->P[j] admissible if every boundary point between is within eps of the chord
        private static bool Admissible(List<double[]> P, int i, int j, double eps)
        {
            var a = P[i]; var b = P[j]; var d = Sub(b, a); double dl = Len(d); if (dl < 1e-9) return true;
            for (int k = i + 1; k < j; k++) { double dist = Math.Abs(Cross(Sub(P[k], a), d)) / dl; if (dist > eps) return false; }
            return true;
        }

        // Sutherland-Hodgman clip of a polygon by the half-plane { x : n.(x-p) >= 0 }
        private static List<double[]> HalfPlaneClip(List<double[]> poly, double[] p, double[] n)
        {
            var outp = new List<double[]>();
            double Side(double[] x) => n[0] * (x[0] - p[0]) + n[1] * (x[1] - p[1]);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double sa = Side(a), sb = Side(b);
                if (sa >= 0) outp.Add(a);
                if ((sa >= 0) != (sb >= 0))
                {
                    double t = sa / (sa - sb);
                    outp.Add(new[] { a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]) });
                }
            }
            return outp;
        }

        // where the infinite line (p, dir) crosses the polygon boundary -> the cut segment
        private static (double[], double[])? LinePolygonSegment(double[] p, double[] dir, List<double[]> poly)
        {
            var hits = new List<double[]>(); var nrm = new[] { -dir[1], dir[0] };
            double Side(double[] x) => nrm[0] * (x[0] - p[0]) + nrm[1] * (x[1] - p[1]);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double sa = Side(a), sb = Side(b);
                if ((sa >= 0) != (sb >= 0))
                {
                    double t = sa / (sa - sb);
                    hits.Add(new[] { a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]) });
                }
            }
            if (hits.Count < 2) return null;
            // farthest pair along dir
            hits = hits.OrderBy(h => Dot(Sub(h, p), dir)).ToList();
            return (hits[0], hits[hits.Count - 1]);
        }

        private static List<double[]> ConvexHull(List<double[]> pts)
        {
            var s = pts.Distinct(new PtCmp()).OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
            if (s.Count < 3) return s;
            var h = new List<double[]>();
            foreach (var p in s) { while (h.Count >= 2 && Cross(Sub(h[h.Count - 1], h[h.Count - 2]), Sub(p, h[h.Count - 2])) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            int lower = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var p = s[i]; while (h.Count >= lower && Cross(Sub(h[h.Count - 1], h[h.Count - 2]), Sub(p, h[h.Count - 2])) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            h.RemoveAt(h.Count - 1); return h;
        }
        private sealed class PtCmp : IEqualityComparer<double[]>
        { public bool Equals(double[]? a, double[]? b) => a != null && b != null && Math.Abs(a[0] - b[0]) < 1e-9 && Math.Abs(a[1] - b[1]) < 1e-9; public int GetHashCode(double[] p) => p[0].GetHashCode() ^ p[1].GetHashCode(); }

        private static bool IsConvex(List<double[]> P)
        {
            for (int i = 0; i < P.Count; i++)
            { var a = P[(i - 1 + P.Count) % P.Count]; var b = P[i]; var c = P[(i + 1) % P.Count]; if (Cross(Sub(b, a), Sub(c, b)) < -1e-7) return false; }
            return true;
        }
        private static List<double[]> EnsureCcw(List<double[]> p) => Area(p) < 0 ? Enumerable.Reverse(p).ToList() : new List<double[]>(p);
        private static double Area(List<double[]> p)
        { double s = 0; for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a[0] * b[1] - b[0] * a[1]; } return s / 2.0; }
        private static double[] Centroid(List<double[]> p) => new[] { p.Average(q => q[0]), p.Average(q => q[1]) };
        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1] };
        private static double Cross(double[] a, double[] b) => a[0] * b[1] - a[1] * b[0];
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1];
        private static double Len(double[] a) => Math.Sqrt(a[0] * a[0] + a[1] * a[1]);
        private static double[] Norm(double[] a) { double l = Len(a); return l > 1e-12 ? new[] { a[0] / l, a[1] / l } : a; }
    }
}
