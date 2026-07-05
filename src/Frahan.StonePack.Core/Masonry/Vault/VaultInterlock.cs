#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // VaultInterlock — quantify how interlocked a tessellation is by finding the
    // longest CONTINUOUS JOINT (a near-straight connected run of joint edges = a
    // potential sliding plane). A masonry assembly resists sliding when no long
    // straight joint runs uninterrupted across many units. This is a universal
    // check on BOTH regimes:
    //   * Voronoi rubble -> blue-noise seeds give short, random joints (naturally
    //     staggered) -> low metric.
    //   * Regular quad courses -> the head joints line up across courses unless a
    //     running-bond stagger is imposed -> high metric (caught here).
    // Operates on the tessellation mesh: its topology edges ARE the joints; a run
    // is a chain of edges that stay collinear within an angle tolerance BOTH step-
    // to-step AND relative to the run's seed direction (so a gently-curving arc is
    // not mistaken for one straight joint). Seeds are walked longest-edge-first, so
    // the result is deterministic; LongestRun is a greedy LOWER BOUND on the true
    // longest joint (a genuinely straight sliding plane is always captured in full).
    // =========================================================================
    public sealed class InterlockResult
    {
        public double LongestRun;            // length of the longest near-straight joint run (m)
        public double SpanFraction;          // LongestRun / bounding-box diagonal
        public Polyline LongestJoint = new Polyline();
        public List<Polyline> FlaggedRuns = new List<Polyline>();  // runs over the flag fraction
        public int JointEdges;
        public string Verdict = "";          // interlocked / moderate / sliding-plane risk
    }

    public static class VaultInterlock
    {
        /// <summary>
        /// angleTolDeg: max turn between consecutive joint edges to count as one straight run.
        /// flagFraction: collect every run longer than this fraction of the bbox diagonal.
        /// </summary>
        public static InterlockResult Analyze(Mesh tess, double angleTolDeg = 15.0, double flagFraction = 0.5)
        {
            var res = new InterlockResult();
            if (tess == null || tess.Vertices.Count == 0) { res.Verdict = "empty"; return res; }

            var top = tess.TopologyEdges;
            var tvs = tess.TopologyVertices;
            int ne = top.Count, nv = tvs.Count;
            res.JointEdges = ne;
            if (ne == 0) { res.Verdict = "no edges"; return res; }

            var P = new Point3d[nv];
            for (int i = 0; i < nv; i++) { var p = tvs[i]; P[i] = new Point3d(p.X, p.Y, p.Z); }

            // edge endpoints + per-vertex incidence
            var ev = new int[ne][];
            var vinc = new List<int>[nv];
            for (int i = 0; i < nv; i++) vinc[i] = new List<int>(4);
            for (int e = 0; e < ne; e++)
            {
                var ip = top.GetTopologyVertices(e);
                ev[e] = new[] { ip.I, ip.J };
                vinc[ip.I].Add(e); vinc[ip.J].Add(e);
            }

            double cosTol = Math.Cos(angleTolDeg * Math.PI / 180.0);
            var bbox = tess.GetBoundingBox(false);
            double span = bbox.Diagonal.Length; if (span < 1e-9) span = 1.0;

            // canonical seed order: longest edges first -> deterministic (independent of
            // mesh storage order) and the long joints get captured before short forks.
            var elen = new double[ne];
            for (int e = 0; e < ne; e++) elen[e] = P[ev[e][0]].DistanceTo(P[ev[e][1]]);
            var order = new int[ne];
            for (int i = 0; i < ne; i++) order[i] = i;
            Array.Sort(order, (x, y) => elen[y].CompareTo(elen[x]));

            bool[] used = new bool[ne];
            double longest = 0;
            foreach (int e0 in order)
            {
                if (used[e0]) continue;
                int a = ev[e0][0], b = ev[e0][1];
                Vector3d d0 = Dir(P[a], P[b]);
                used[e0] = true;
                if (d0.IsZero) continue;   // degenerate seed: marked used, contributes nothing

                // grow forward from b (incoming dir a->b) and backward from a (incoming dir b->a)
                var fwd = new List<int>();   // vertices after b
                Grow(b, d0, P, vinc, ev, used, cosTol, fwd);
                var bwd = new List<int>();   // vertices before a
                Grow(a, Dir(P[b], P[a]), P, vinc, ev, used, cosTol, bwd);

                // assemble the chain: reverse(bwd) + a + b + fwd
                var chain = new List<int>(bwd.Count + fwd.Count + 2);
                for (int i = bwd.Count - 1; i >= 0; i--) chain.Add(bwd[i]);
                chain.Add(a); chain.Add(b);
                chain.AddRange(fwd);

                var pl = new Polyline(chain.Count);
                double len = 0;
                for (int i = 0; i < chain.Count; i++)
                {
                    pl.Add(P[chain[i]]);
                    if (i > 0) len += P[chain[i]].DistanceTo(P[chain[i - 1]]);
                }

                if (len > longest) { longest = len; res.LongestJoint = pl; }
                if (len / span >= flagFraction) res.FlaggedRuns.Add(pl);
            }

            res.LongestRun = longest;
            res.SpanFraction = longest / span;
            res.Verdict = res.SpanFraction >= 0.66 ? "sliding-plane risk"
                        : res.SpanFraction >= 0.33 ? "moderate"
                        : "interlocked";
            return res;
        }

        static Vector3d Dir(Point3d a, Point3d b)
        {
            var v = b - a; double L = v.Length;
            return L < 1e-12 ? Vector3d.Zero : v / L;   // degenerate edge: non-collinear with all
        }

        // From vertex v with incoming direction d (the run's SEED direction at the first call),
        // repeatedly take the most step-collinear unused edge that ALSO stays within the angle
        // tolerance of the seed -> bounds the run's total bend, not just the per-step turn, so a
        // gently-curving arc is not counted as one straight joint. Thresholds are inclusive at
        // the tolerance (>= cosTol). Appends visited far-vertices to chain; marks edges used.
        static void Grow(int v, Vector3d d, Point3d[] P, List<int>[] vinc, int[][] ev,
            bool[] used, double cosTol, List<int> chain)
        {
            Vector3d d0 = d;   // seed direction: cap cumulative bend against this
            int guard = 0;
            while (guard++ < 100000)
            {
                int best = -1, bestW = -1; double bestDot = double.NegativeInfinity;
                Vector3d bestDir = d;
                var inc = vinc[v];
                for (int i = 0; i < inc.Count; i++)
                {
                    int e = inc[i];
                    if (used[e]) continue;
                    int w = ev[e][0] == v ? ev[e][1] : ev[e][0];
                    var od = Dir(P[v], P[w]);
                    if (od.IsZero) continue;
                    double dotStep = d.X * od.X + d.Y * od.Y + d.Z * od.Z;     // vs previous edge
                    double dotSeed = d0.X * od.X + d0.Y * od.Y + d0.Z * od.Z;  // vs seed direction
                    if (dotStep >= cosTol && dotSeed >= cosTol && dotStep > bestDot)
                    { bestDot = dotStep; best = e; bestW = w; bestDir = od; }
                }
                if (best < 0) break;
                used[best] = true;
                chain.Add(bestW);
                v = bestW; d = bestDir;
            }
        }
    }
}
