using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Discrete Frechet distance between two ordered point sequences (polylines).
    /// The "dog-walking" metric: the minimum leash length as one traversal walks
    /// A=[a_0..a_{n-1}] and another walks B=[b_0..b_{m-1}], both moving forward
    /// only (monotone, no backtracking). It is the MAXIMUM gap encountered along
    /// the best monotone coupling, minimised over all such couplings.
    ///
    /// Why it complements the existing gates. A mean/RMS ICP residual is an
    /// average, so a single local gap (a notch mismatch) washes out; Frechet is a
    /// max, so it BOUNDS the worst gap along the joint -- the physically
    /// meaningful tolerance for a cut ("mating edges within X everywhere"). And
    /// unlike closest-point residual / Hausdorff, Frechet respects the sequential
    /// ORDER and DIRECTION of the two curves (the monotone constraint), so a
    /// reversed / folded / scrambled alignment that looks close as a point set is
    /// rejected. Discrete-Frechet(A,B) >= Hausdorff(A,B) always.
    ///
    /// Intended use: a final verification gate on a matched rim pair, after
    /// alignment, before a cut is emitted (the R1 recommendation of
    /// wiki/research/edge_matching_theory_vs_implementation.md).
    ///
    /// Algorithm: the coupling-measure dynamic program of Eiter and Mannila,
    /// "Computing discrete Frechet distance", TR CD-TR 94/64, TU Wien, 1994.
    ///   ca(0,0)   = d(a_0, b_0)
    ///   ca(i,0)   = max( ca(i-1,0), d(a_i, b_0) )
    ///   ca(0,j)   = max( ca(0,j-1), d(a_0, b_j) )
    ///   ca(i,j)   = max( d(a_i, b_j), min( ca(i-1,j), ca(i-1,j-1), ca(i,j-1) ) )
    /// result = ca(n-1, m-1). O(n*m) time; this implementation rolls two rows for
    /// O(min(n,m)) memory. Fully deterministic. RhinoCommon-light (Point3d value
    /// container only), so unit-testable without a live Rhino process.
    ///
    /// Note on sampling: discrete Frechet compares the given samples, so both
    /// polylines should be sampled at a comparable density (the Frahan rims are
    /// already arc-length resampled by BoundarySegmenter). Denser sampling of a
    /// smooth curve converges toward the continuous Frechet distance.
    /// </summary>
    public static class FrechetDistance
    {
        /// <summary>
        /// Discrete Frechet distance between two ordered point sequences. Throws
        /// on null; requires at least one point in each sequence.
        /// </summary>
        public static double Discrete(IReadOnlyList<Point3d> a, IReadOnlyList<Point3d> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            int n = a.Count, m = b.Count;
            if (n == 0 || m == 0)
                throw new ArgumentException("both sequences need at least one point");

            // Roll two rows over the smaller dimension to bound memory.
            if (m > n)
                return Discrete(b, a); // symmetric; keep the inner row = min dimension

            var prev = new double[m]; // ca(i-1, .)
            var cur = new double[m];  // ca(i,   .)
            for (int i = 0; i < n; i++)
            {
                Point3d ai = a[i];
                for (int j = 0; j < m; j++)
                {
                    double d = ai.DistanceTo(b[j]);
                    double best;
                    if (i == 0 && j == 0) best = d;
                    else if (i == 0) best = Math.Max(cur[j - 1], d);   // ca(0,j)
                    else if (j == 0) best = Math.Max(prev[0], d);      // ca(i,0)
                    else best = Math.Max(d, Math.Min(prev[j], Math.Min(prev[j - 1], cur[j - 1])));
                    cur[j] = best;
                }
                var tmp = prev; prev = cur; cur = tmp; // cur (row i) becomes prev
            }
            return prev[m - 1]; // after the final swap, prev holds row n-1
        }
    }
}
