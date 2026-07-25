using System;
using System.Collections.Generic;
using Xunit;
using Rhino.Geometry;
using Frahan.Core.Discontinuity;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>SetClusterer.Cluster</c> (joint-set clustering: mean-shift on
    /// the sphere with a Watson axial kernel, greedy merge of converged modes
    /// within MergeDeg, nearest-pole assignment, min-size drop). The HARD
    /// invariants map to the code contract proved as <c>mergeKeep_separated</c>
    /// in <c>frahan_proofs/FrahanProofs/Clustering.lean</c> (a greedy keep-if-far
    /// pass leaves the retained set pairwise separated):
    ///   T1 PARTITION    — returned FacetIndices pairwise disjoint, all in [0,n);
    ///   T2 MIN-SIZE     — every returned set has &gt;= MinSetFacets facets;
    ///   T3 SEPARATION   — returned poles pairwise &gt;= MergeDeg apart
    ///                     (the mergeKeep_separated invariant);
    ///   T5 POINT-SHARE  — each PointShare in [0,1]; sum over sets &lt;= 1.
    /// Every invariant is checked with an INDEPENDENT double-vector oracle here,
    /// never the kernel's own OrientationMath / state.
    ///
    /// NOTE: planted-cluster RECOVERY (getting exactly K sets back) is NOT
    /// gated here. The harness established it is bandwidth-limited — sets closer
    /// than ~1.8*BandwidthDeg fuse in mean-shift and MergeDeg cannot re-split
    /// them — which is a documented characteristic, not a bug. Gating it would
    /// produce false CI failures. Only the hard invariants gate.
    ///
    /// Origin harness + report: <c>outputs/2026-07-24/clustering_verification/</c>
    /// (all hard invariants PASS; recovery under-segmentation is the documented
    /// bandwidth floor).
    /// </summary>
    public class ClusteringTests
    {
        const double R2D = 180.0 / Math.PI;
        const double D2R = Math.PI / 180.0;

        [Fact]
        public void HardInvariants_Partition_MinSize_MergeSeparation_PointShare()
        {
            const double TOL = 1e-6;
            var rnd = new Random(20260724);

            int A = 1000;
            long part_bad = 0, size_bad = 0, ps_range_bad = 0, ps_sum_bad = 0;
            long instances = 0, totalSets = 0, totalPairs = 0;
            long pairs_lt_merge = 0;
            double worstSepRatio = double.MaxValue; string worstSepMsg = "(none)";
            double worstPsSum = 0;

            for (int t = 0; t < A; t++)
            {
                int K = 1 + rnd.Next(7);                  // 1..7 planted blobs
                double bw = 8 + rnd.NextDouble() * 12;    // 8..20
                double merge = 5 + rnd.NextDouble() * 7;  // 5..12
                int minSet = 2 + rnd.Next(4);             // 2..5
                var opt = new SetOptions
                {
                    BandwidthDeg = bw, MergeDeg = merge, MinSetFacets = minSet,
                    MaxStarts = 200 + rnd.Next(1001)
                };
                double bwHalf = bw / 2;

                var facets = new List<Facet>();
                for (int k = 0; k < K; k++)
                {
                    double[] g = NormFromDipDipDir(5 + rnd.NextDouble() * 80, rnd.NextDouble() * 360);
                    double sigma = 1 + rnd.NextDouble() * 9;
                    int size = 1 + rnd.Next(40);
                    for (int j = 0; j < size; j++)
                    {
                        double[] nrm = Perturb(g, ScatterAng(rnd, sigma, bwHalf * 0.95), rnd);
                        int pc = rnd.NextDouble() < 0.06 ? 0 : 1 + rnd.Next(200);
                        facets.Add(MakeFacet(nrm, pc, rnd));
                    }
                }
                for (int i = facets.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (facets[i], facets[j]) = (facets[j], facets[i]); }
                int n = facets.Count;
                if (n == 0) continue;

                var sets = SetClusterer.Cluster(facets, opt);
                instances++; totalSets += sets.Count;

                // T1 -- partition validity
                var seen = new HashSet<int>();
                bool partOk = true;
                foreach (var st in sets)
                    foreach (var fi in st.FacetIndices)
                    {
                        if (fi < 0 || fi >= n) partOk = false;
                        if (!seen.Add(fi)) partOk = false;   // duplicate across/within sets
                    }
                if (!partOk) part_bad++;

                // T2 -- min size
                foreach (var st in sets)
                    if (st.FacetIndices.Length < opt.MinSetFacets) { size_bad++; break; }

                // T3 -- pairwise separation of returned poles (mergeKeep_separated)
                for (int a2 = 0; a2 < sets.Count; a2++)
                    for (int b2 = a2 + 1; b2 < sets.Count; b2++)
                    {
                        totalPairs++;
                        double sep = AxialDeg(sets[a2].Pole, sets[b2].Pole);
                        if (sep < merge) pairs_lt_merge++;
                        double ratio = sep / merge;
                        if (ratio < worstSepRatio)
                        {
                            worstSepRatio = ratio;
                            worstSepMsg = $"sep={sep:F3} deg, MergeDeg={merge:F2} (ratio {ratio:F3}), bw={bw:F2}, sets={sets.Count}, n={n}";
                        }
                    }

                // T5 -- point share
                double psSum = 0;
                foreach (var st in sets)
                {
                    if (st.PointShare < -TOL || st.PointShare > 1 + TOL) ps_range_bad++;
                    psSum += st.PointShare;
                }
                if (psSum > worstPsSum) worstPsSum = psSum;
                if (psSum > 1 + 1e-4) ps_sum_bad++;
            }

            Assert.True(part_bad == 0, $"T1 PARTITION violated on {part_bad}/{instances} instances (indices not disjoint / out of range)");
            Assert.True(size_bad == 0, $"T2 MIN-SIZE violated on {size_bad}/{instances} instances");
            Assert.True(pairs_lt_merge == 0, $"T3 SEPARATION (mergeKeep_separated) violated: {pairs_lt_merge}/{totalPairs} pole pairs closer than MergeDeg; closest {worstSepMsg}");
            Assert.True(ps_range_bad == 0, $"T5 PointShare out of [0,1] on {ps_range_bad} sets");
            Assert.True(ps_sum_bad == 0, $"T5 PointShare sum > 1 on {ps_sum_bad} instances (worst sum {worstPsSum:F6})");
        }

        // ---- independent double-vector oracle helpers -------------------------
        static double[] NormFromDipDipDir(double dipDeg, double ddDeg)
        {
            double dip = dipDeg * D2R, dd = ddDeg * D2R, s = Math.Sin(dip);
            return Unit(new[] { s * Math.Sin(dd), s * Math.Cos(dd), -Math.Cos(dip) });
        }
        static double[] Unit(double[] v)
        {
            double L = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (L < 1e-300) return new[] { 0.0, 0.0, 1.0 };
            return new[] { v[0] / L, v[1] / L, v[2] / L };
        }
        static double[] Cross(double[] a, double[] b) => new[]
        { a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0] };
        static double Dot(double[] a, double[] b) => a[0]*b[0]+a[1]*b[1]+a[2]*b[2];

        // acute angle between two orientations treated as AXES (n == -n), degrees.
        static double AxialDeg(double[] a, double[] b)
        {
            double[] ua = Unit(a), ub = Unit(b);
            double d = Math.Abs(Dot(ua, ub));
            if (d > 1.0) d = 1.0;
            return Math.Acos(d) * R2D;
        }
        static double AxialDeg(Vector3d a, Vector3d b)
            => AxialDeg(new[] { a.X, a.Y, a.Z }, new[] { b.X, b.Y, b.Z });

        // perturb unit normal g by exactly `angRad` toward a random tangent dir.
        static double[] Perturb(double[] g, double angRad, Random r)
        {
            double[] helper = Math.Abs(g[0]) < 0.9 ? new[] { 1.0, 0, 0 } : new[] { 0.0, 1, 0 };
            double[] t1 = Unit(Cross(g, helper));
            double[] t2 = Cross(g, t1); // unit: g,t1 orthonormal
            double phi = 2 * Math.PI * r.NextDouble();
            double ca = Math.Cos(angRad), sa = Math.Sin(angRad);
            double c = Math.Cos(phi), s = Math.Sin(phi);
            return Unit(new[]
            {
                ca*g[0] + sa*(c*t1[0] + s*t2[0]),
                ca*g[1] + sa*(c*t1[1] + s*t2[1]),
                ca*g[2] + sa*(c*t1[2] + s*t2[2]),
            });
        }
        // half-normal scatter angle, capped below capDeg.
        static double ScatterAng(Random r, double sigmaDeg, double capDeg)
        {
            double u1 = Math.Max(1e-12, r.NextDouble());
            double g = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * r.NextDouble());
            double a = Math.Abs(g) * sigmaDeg;
            if (a > capDeg) a = capDeg;
            return a * D2R;
        }
        static Facet MakeFacet(double[] normal, int pointCount, Random r)
        {
            var idx = new int[Math.Max(0, pointCount)];
            return new Facet
            {
                Normal = new Vector3d(normal[0], normal[1], normal[2]),
                Centroid = new Point3d((r.NextDouble()-0.5)*10, (r.NextDouble()-0.5)*10, (r.NextDouble()-0.5)*10),
                PointIndices = idx,
            };
        }
    }
}
