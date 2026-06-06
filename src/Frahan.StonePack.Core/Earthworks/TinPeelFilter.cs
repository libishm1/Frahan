#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.Earthworks
{
    // =========================================================================
    // TinPeelFilter -- raw Delaunay / scan-reconstruction TIN -> true terrain.
    //
    // Delaunay (and Poisson / alpha scan reconstruction) fills the whole convex
    // hull, so concave shorelines, sparse boundaries and data gaps get long thin
    // "cap" triangles and near-vertical gap webs that are NOT real terrain. This
    // peels them, by iteratively removing BORDER triangles that satisfy any
    // removal predicate, then dropping connected components below a min size.
    // (Ports the Fade2D land-survey peel logic; see outputs/2026-06-04/
    // cutfill_excavation/geom_at_meshing_codedive.md sec 2, card A2.)
    //
    // Removal predicate (PeelDeciderAggressive) -- remove a border triangle if ANY:
    //   1. Long edge:  max edge length^2 > (k*m)^2,  m = median 2D edge length,
    //      k = 3 (aggressive) / 10 (careful).   (scale-relative; GeometryNumerics T2)
    //   2. Near-vertical facet:  angle(normal, z_hat) > 85 deg  (2.5D gap artifact).
    //   3. Cap/sliver:  interior angle opposite the border edge > 140 deg.
    // Non-destructive: returns the KEPT triangle index list; the input is untouched.
    //
    // Thresholds are RELATIVE to the local median edge so the same filter works at
    // any survey scale -- the scale-relative-epsilon principle (GeometryNumerics T2).
    // Rhino-free, headless-testable. Method-class: border-peel on a 2.5D TIN +
    // connected-component size filter.
    // =========================================================================
    public sealed class TinPeelOptions
    {
        /// <summary>Long-edge multiple of the median edge (3 = aggressive, 10 = careful).</summary>
        public double LongEdgeK = 3.0;
        /// <summary>Remove border facets steeper than this (deg from horizontal-normal).</summary>
        public double MaxFacetTiltDeg = 85.0;
        /// <summary>Remove border facets whose angle opposite the border edge exceeds this (deg).</summary>
        public double MaxCapAngleDeg = 140.0;
        /// <summary>Drop connected components with fewer triangles than this after peeling.</summary>
        public int MinComponentTriangles = 50;
        /// <summary>Enable the long-edge criterion (1).</summary>
        public bool UseLongEdge = true;
        /// <summary>Enable the near-vertical criterion (2).</summary>
        public bool UseVerticality = true;
        /// <summary>Enable the cap-angle criterion (3).</summary>
        public bool UseCapAngle = true;
    }

    public sealed class TinPeelResult
    {
        public TinPeelResult(List<int> keptTriangles, int removedByPeel, int removedBySize,
            double medianEdge, int components)
        {
            KeptTriangles = keptTriangles;
            RemovedByPeel = removedByPeel;
            RemovedBySize = removedBySize;
            MedianEdgeLength = medianEdge;
            ComponentsKept = components;
        }
        /// <summary>Retained triangles as flat (i0,i1,i2) vertex-index triples.</summary>
        public List<int> KeptTriangles { get; }
        public int RemovedByPeel { get; }
        public int RemovedBySize { get; }
        public double MedianEdgeLength { get; }
        public int ComponentsKept { get; }
        public int KeptCount => KeptTriangles.Count / 3;
        public override string ToString() =>
            $"TinPeel(kept={KeptCount} tris, peeled={RemovedByPeel}, sizeDropped={RemovedBySize}, " +
            $"m={MedianEdgeLength:0.###}, comps={ComponentsKept})";
    }

    public static class TinPeelFilter
    {
        /// <summary>
        /// Peel cap / vertical / sliver border triangles and drop tiny components.
        /// </summary>
        /// <param name="xyz">flat vertex coords [x0,y0,z0, x1,y1,z1, ...].</param>
        /// <param name="triangles">flat triangle vertex indices [a0,b0,c0, a1,b1,c1, ...].</param>
        /// <param name="opt">options (null = defaults).</param>
        public static TinPeelResult Filter(IReadOnlyList<double> xyz, IReadOnlyList<int> triangles,
            TinPeelOptions opt = null)
        {
            if (xyz == null) throw new ArgumentNullException(nameof(xyz));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (triangles.Count % 3 != 0) throw new ArgumentException("triangles length must be a multiple of 3");
            opt = opt ?? new TinPeelOptions();
            int nt = triangles.Count / 3;
            int nv = xyz.Count / 3;

            // median 2D edge length over all triangle edges
            double m = MedianEdgeLength2D(xyz, triangles);
            double longSq = (opt.LongEdgeK * m) * (opt.LongEdgeK * m);

            var alive = new bool[nt];
            for (int t = 0; t < nt; t++) alive[t] = true;

            // edge -> incident triangles, to find borders (an edge on exactly one alive tri = border)
            var edgeCount = BuildEdgeCounts(triangles, nt, alive);

            int peeled = 0;
            // iterative border peel until stable
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < nt + 5)
            {
                changed = false;
                for (int t = 0; t < nt; t++)
                {
                    if (!alive[t]) continue;
                    if (!IsBorder(triangles, t, edgeCount)) continue;
                    if (ShouldPeel(xyz, triangles, t, longSq, opt))
                    {
                        alive[t] = false; peeled++; changed = true;
                    }
                }
                if (changed) edgeCount = BuildEdgeCounts(triangles, nt, alive);
            }

            // connected components on the alive triangles (adjacency via shared edge)
            var comps = ConnectedComponents(triangles, nt, alive);
            int sizeDropped = 0, compsKept = 0;
            var kept = new List<int>(nt * 3);
            foreach (var comp in comps)
            {
                if (comp.Count < opt.MinComponentTriangles)
                {
                    sizeDropped += comp.Count;
                    continue;
                }
                compsKept++;
                foreach (int t in comp)
                {
                    kept.Add(triangles[3 * t]); kept.Add(triangles[3 * t + 1]); kept.Add(triangles[3 * t + 2]);
                }
            }
            return new TinPeelResult(kept, peeled, sizeDropped, m, compsKept);
        }

        // --- predicate (3 criteria) ---
        private static bool ShouldPeel(IReadOnlyList<double> xyz, IReadOnlyList<int> tri, int t,
            double longSq, TinPeelOptions opt)
        {
            int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
            // (1) long edge
            if (opt.UseLongEdge)
            {
                double e0 = Dist2_2D(xyz, a, b), e1 = Dist2_2D(xyz, b, c), e2 = Dist2_2D(xyz, c, a);
                double mx = Math.Max(e0, Math.Max(e1, e2));
                if (mx > longSq) return true;
            }
            // (2) near-vertical facet
            if (opt.UseVerticality)
            {
                double tilt = FacetTiltDeg(xyz, a, b, c);
                if (tilt > opt.MaxFacetTiltDeg) return true;
            }
            // (3) cap angle: max interior angle > threshold (the angle opposite the longest/border edge)
            if (opt.UseCapAngle)
            {
                double maxAng = MaxInteriorAngleDeg(xyz, a, b, c);
                if (maxAng > opt.MaxCapAngleDeg) return true;
            }
            return false;
        }

        // --- geometry helpers (2D in x,y for edges/area; 3D normal for tilt) ---
        private static double Dist2_2D(IReadOnlyList<double> p, int i, int j)
        {
            double dx = p[3 * i] - p[3 * j], dy = p[3 * i + 1] - p[3 * j + 1];
            return dx * dx + dy * dy;
        }

        private static double FacetTiltDeg(IReadOnlyList<double> p, int a, int b, int c)
        {
            double ax = p[3 * a], ay = p[3 * a + 1], az = p[3 * a + 2];
            double bx = p[3 * b], by = p[3 * b + 1], bz = p[3 * b + 2];
            double cx = p[3 * c], cy = p[3 * c + 1], cz = p[3 * c + 2];
            double ux = bx - ax, uy = by - ay, uz = bz - az;
            double vx = cx - ax, vy = cy - ay, vz = cz - az;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double nn = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nn < 1e-30) return 90.0;       // degenerate -> treat as vertical artifact
            double cosToVert = Math.Abs(nz) / nn;        // |n . zhat| / ||n||
            cosToVert = Math.Max(-1.0, Math.Min(1.0, cosToVert));
            return Math.Acos(cosToVert) * (180.0 / Math.PI);
        }

        private static double MaxInteriorAngleDeg(IReadOnlyList<double> p, int a, int b, int c)
        {
            double angA = AngleAt(p, a, b, c);
            double angB = AngleAt(p, b, a, c);
            double angC = 180.0 - angA - angB;
            return Math.Max(angA, Math.Max(angB, angC));
        }

        // interior angle at vertex 'v' of triangle (v, j, k), using 2D (x,y)
        private static double AngleAt(IReadOnlyList<double> p, int v, int j, int k)
        {
            double v1x = p[3 * j] - p[3 * v], v1y = p[3 * j + 1] - p[3 * v + 1];
            double v2x = p[3 * k] - p[3 * v], v2y = p[3 * k + 1] - p[3 * v + 1];
            double n1 = Math.Sqrt(v1x * v1x + v1y * v1y), n2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            if (n1 < 1e-30 || n2 < 1e-30) return 0.0;
            double cos = (v1x * v2x + v1y * v2y) / (n1 * n2);
            cos = Math.Max(-1.0, Math.Min(1.0, cos));
            return Math.Acos(cos) * (180.0 / Math.PI);
        }

        private static double MedianEdgeLength2D(IReadOnlyList<double> xyz, IReadOnlyList<int> tri)
        {
            int nt = tri.Count / 3;
            var lens = new List<double>(nt * 3);
            for (int t = 0; t < nt; t++)
            {
                int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                lens.Add(Math.Sqrt(Dist2_2D(xyz, a, b)));
                lens.Add(Math.Sqrt(Dist2_2D(xyz, b, c)));
                lens.Add(Math.Sqrt(Dist2_2D(xyz, c, a)));
            }
            if (lens.Count == 0) return 1.0;
            lens.Sort();
            int n = lens.Count;
            return n % 2 == 1 ? lens[n / 2] : 0.5 * (lens[n / 2 - 1] + lens[n / 2]);
        }

        // --- topology: edge counts (alive only), border test, components ---
        private static Dictionary<long, int> BuildEdgeCounts(IReadOnlyList<int> tri, int nt, bool[] alive)
        {
            var d = new Dictionary<long, int>(nt * 3);
            for (int t = 0; t < nt; t++)
            {
                if (!alive[t]) continue;
                int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                Bump(d, a, b); Bump(d, b, c); Bump(d, c, a);
            }
            return d;
        }

        private static void Bump(Dictionary<long, int> d, int i, int j)
        {
            long key = EdgeKey(i, j);
            d.TryGetValue(key, out int v); d[key] = v + 1;
        }

        private static long EdgeKey(int i, int j)
        {
            int lo = Math.Min(i, j), hi = Math.Max(i, j);
            return ((long)lo << 32) | (uint)hi;
        }

        private static bool IsBorder(IReadOnlyList<int> tri, int t, Dictionary<long, int> edgeCount)
        {
            int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
            return EdgeShared(edgeCount, a, b) < 2 || EdgeShared(edgeCount, b, c) < 2 || EdgeShared(edgeCount, c, a) < 2;
        }

        private static int EdgeShared(Dictionary<long, int> d, int i, int j)
            => d.TryGetValue(EdgeKey(i, j), out int v) ? v : 0;

        private static List<List<int>> ConnectedComponents(IReadOnlyList<int> tri, int nt, bool[] alive)
        {
            // edge -> list of alive triangles sharing it
            var edgeToTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < nt; t++)
            {
                if (!alive[t]) continue;
                int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                AddEdgeTri(edgeToTris, a, b, t); AddEdgeTri(edgeToTris, b, c, t); AddEdgeTri(edgeToTris, c, a, t);
            }
            var visited = new bool[nt];
            var comps = new List<List<int>>();
            var stack = new Stack<int>();
            for (int s = 0; s < nt; s++)
            {
                if (!alive[s] || visited[s]) continue;
                var comp = new List<int>();
                stack.Push(s); visited[s] = true;
                while (stack.Count > 0)
                {
                    int t = stack.Pop(); comp.Add(t);
                    int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                    PushNbr(edgeToTris, a, b, t, visited, stack);
                    PushNbr(edgeToTris, b, c, t, visited, stack);
                    PushNbr(edgeToTris, c, a, t, visited, stack);
                }
                comps.Add(comp);
            }
            return comps;
        }

        private static void AddEdgeTri(Dictionary<long, List<int>> d, int i, int j, int t)
        {
            long k = EdgeKey(i, j);
            if (!d.TryGetValue(k, out var lst)) { lst = new List<int>(2); d[k] = lst; }
            lst.Add(t);
        }

        private static void PushNbr(Dictionary<long, List<int>> d, int i, int j, int self,
            bool[] visited, Stack<int> stack)
        {
            if (!d.TryGetValue(EdgeKey(i, j), out var lst)) return;
            foreach (int t in lst)
                if (t != self && !visited[t]) { visited[t] = true; stack.Push(t); }
        }
    }
}
