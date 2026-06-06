using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Order-preserving (monotone, NON-CROSSING) correspondence between two
    /// ordered boundary point sequences A=[a_0..a_{n-1}] and B=[b_0..b_{m-1}].
    /// Pairs are produced so that matched indices are non-decreasing in BOTH
    /// sequences: if (i,j) and (i',j') are both matched with i &lt; i', then
    /// j &lt;= j'. That monotonicity is exactly what prevents the tangled
    /// (crossing) correspondences free nearest-neighbour ICP can produce on
    /// wiggly / noisy rims.
    ///
    /// Algorithm: a DTW-style dynamic program over the n x m cost grid where
    /// cell(i,j) = squared distance between a_i and b_j. The DP minimises the
    /// total matched distance subject to monotonicity, allowing bounded
    /// horizontal / vertical skips so unequal-length or partially-overlapping
    /// rims still match without forcing a one-to-one pairing. O(n*m) time,
    /// O(n*m) memory, fully deterministic (no randomness, fixed iteration
    /// order, stable tie-breaks toward the diagonal).
    ///
    /// Conceptual basis. The "no two matched chords cross" objective is the
    /// minimum-weight NON-CROSSING matching idea of Marcotte and Suri,
    /// "Fast matching algorithms for points on a polygon", SIAM J. Comput.
    /// 20(3), 1991, and the user's reference implementation at
    /// https://github.com/libishm1/polygon-perfect-matching . That algorithm
    /// is O(N log N) but matches points AMONG THEMSELVES on a single CONVEX
    /// polygon boundary. Our problem is different: match points BETWEEN two
    /// arbitrary (non-convex, possibly cyclic) rims. So a verbatim port does
    /// not apply. The monotone DP used here is the general primitive that
    /// keeps the non-crossing guarantee on two independent ordered sequences;
    /// the cyclic closed-rim case is reduced to the linear case by the
    /// existing <see cref="PhaseCorrelator"/> (fixing the offset) plus an
    /// explicit reversed-orientation trial (see <see cref="MatchClosed"/>).
    ///
    /// This type is RhinoCommon-light: it only uses Point3d as a value
    /// container and contains no Rhino-runtime calls, so it is unit-testable
    /// without a live Rhino process.
    /// </summary>
    public static class OrderedBoundaryMatcher
    {
        /// <summary>
        /// One matched index pair. <see cref="A"/> indexes the first
        /// sequence, <see cref="B"/> the second. The pair list returned by
        /// the matcher is sorted ascending by A, and B is non-decreasing
        /// across the list (the non-crossing guarantee).
        /// </summary>
        public readonly struct Pair
        {
            public readonly int A;
            public readonly int B;
            public Pair(int a, int b) { A = a; B = b; }
        }

        /// <summary>
        /// Monotone correspondence between two OPEN ordered sequences.
        /// Returns matched (a-index, b-index) pairs that never cross. The
        /// <paramref name="maxGap"/> bounds how far the two running indices
        /// may diverge (in index units); pass a non-positive value to leave
        /// the band unbounded. Bounding the band is a strictly safety-
        /// preserving robustness guard: it stops a single very long rim from
        /// pairing one of its points against a far-away point on a short rim.
        /// </summary>
        public static List<Pair> MatchOpen(
            IReadOnlyList<Point3d> a,
            IReadOnlyList<Point3d> b,
            int maxGap = 0)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int n = a.Count;
            int m = b.Count;
            var result = new List<Pair>(Math.Min(n, m));
            if (n == 0 || m == 0) return result;
            if (n == 1) { result.Add(new Pair(0, NearestIndex(a[0], b))); return result; }
            if (m == 1) { result.Add(new Pair(NearestIndex(b[0], a), 0)); return result; }

            // Forward DP. cost[i,j] = best cumulative cost to align a[0..i]
            // with b[0..j] ending with a[i] paired to b[j]. Three monotone
            // moves into (i,j): diagonal (i-1,j-1) = advance both, up
            // (i-1,j) = a advances while b waits (gap in b), left (i,j-1) =
            // b advances while a waits (gap in a). Diagonal is preferred on
            // ties so the path hugs the main correspondence and stays
            // deterministic.
            const double Inf = double.PositiveInfinity;
            var cost = new double[n, m];
            // back: 0 = diagonal, 1 = up (from i-1,j), 2 = left (from i,j-1).
            var back = new byte[n, m];

            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                    cost[i, j] = Inf;

            cost[0, 0] = Sq(Dist(a[0], b[0]));
            back[0, 0] = 0;

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    if (i == 0 && j == 0) continue;
                    if (maxGap > 0 && Math.Abs(i - j) > maxGap) continue;

                    double best = Inf;
                    byte from = 0;

                    if (i > 0 && j > 0 && cost[i - 1, j - 1] < Inf)
                    {
                        best = cost[i - 1, j - 1];
                        from = 0;
                    }
                    if (i > 0 && cost[i - 1, j] < best)
                    {
                        best = cost[i - 1, j];
                        from = 1;
                    }
                    if (j > 0 && cost[i, j - 1] < best)
                    {
                        best = cost[i, j - 1];
                        from = 2;
                    }

                    if (best >= Inf) continue;
                    double d = Sq(Dist(a[i], b[j]));
                    cost[i, j] = best + d;
                    back[i, j] = from;
                }
            }

            if (cost[n - 1, m - 1] >= Inf)
            {
                // Band too tight to reach the far corner (only possible with
                // maxGap > 0). Fall back to the unbounded DP so we always
                // return a valid monotone correspondence.
                return maxGap > 0 ? MatchOpen(a, b, 0) : result;
            }

            // Backtrack from (n-1, m-1). Only diagonal moves emit a pair;
            // up/left moves are gaps (one side skipped) and are NOT emitted,
            // which keeps the correspondence non-crossing AND one-to-one
            // among the emitted pairs.
            int ci = n - 1, cj = m - 1;
            while (ci >= 0 && cj >= 0)
            {
                byte from = back[ci, cj];
                if (ci == 0 && cj == 0)
                {
                    result.Add(new Pair(0, 0));
                    break;
                }
                if (from == 0)
                {
                    result.Add(new Pair(ci, cj));
                    ci--; cj--;
                }
                else if (from == 1)
                {
                    ci--;
                }
                else
                {
                    cj--;
                }
            }

            result.Reverse();
            return result;
        }

        /// <summary>
        /// Monotone correspondence between two CLOSED rims. Closed rims have
        /// no canonical start point and may traverse the shared physical
        /// curve in opposite senses, so this method:
        ///   1. tries a set of cyclic offsets of B (seeded by
        ///      <paramref name="phaseOffset"/> from <see cref="PhaseCorrelator"/>
        ///      when supplied, plus a small bracket around it),
        ///   2. tries both the forward and reversed orientation of B,
        ///   3. runs <see cref="MatchOpen"/> on each linearised candidate,
        ///   4. keeps the candidate with the lowest mean matched distance.
        /// Index pairs are returned in terms of the ORIGINAL a / b indices
        /// (the cyclic offset and reversal are undone before returning).
        /// Deterministic: candidate offsets are enumerated in fixed order and
        /// ties break toward the smaller offset then forward orientation.
        /// </summary>
        public static List<Pair> MatchClosed(
            IReadOnlyList<Point3d> a,
            IReadOnlyList<Point3d> b,
            int phaseOffset = 0,
            int offsetBracket = 2,
            int maxGap = 0)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int n = a.Count;
            int m = b.Count;
            if (n == 0 || m == 0) return new List<Pair>();
            if (m == 1 || n == 1) return MatchOpen(a, b, maxGap);

            if (offsetBracket < 0) offsetBracket = 0;

            List<Pair>? bestPairs = null;
            double bestMean = double.PositiveInfinity;

            // Candidate offsets: phaseOffset +/- bracket, normalised into
            // [0, m). Enumerated in a fixed deterministic order.
            var offsets = new List<int>();
            for (int d = -offsetBracket; d <= offsetBracket; d++)
            {
                int off = Mod(phaseOffset + d, m);
                if (!offsets.Contains(off)) offsets.Add(off);
            }

            foreach (bool reversed in new[] { false, true })
            {
                foreach (int off in offsets)
                {
                    var rotated = RotateAndMaybeReverse(b, off, reversed);
                    var pairs = MatchOpen(a, rotated, maxGap);
                    if (pairs.Count == 0) continue;

                    double mean = MeanMatchedDistance(a, rotated, pairs);
                    // Strict '<' keeps the first (lowest offset, forward)
                    // candidate on ties -> deterministic.
                    if (mean < bestMean)
                    {
                        bestMean = mean;
                        // Map rotated/reversed b-indices back to original.
                        var mapped = new List<Pair>(pairs.Count);
                        foreach (var p in pairs)
                            mapped.Add(new Pair(p.A, OriginalBIndex(p.B, off, reversed, m)));
                        bestPairs = mapped;
                    }
                }
            }

            return bestPairs ?? MatchOpen(a, b, maxGap);
        }

        /// <summary>
        /// Mean Euclidean distance over a matched pair list. Used to compare
        /// candidate orientations / offsets and to score a correspondence.
        /// Returns +Infinity for an empty pair list. NaN distances are
        /// skipped (defensive guard against degenerate input); if every pair
        /// is NaN the result is +Infinity so that candidate loses.
        /// </summary>
        public static double MeanMatchedDistance(
            IReadOnlyList<Point3d> a,
            IReadOnlyList<Point3d> b,
            IReadOnlyList<Pair> pairs)
        {
            if (pairs == null || pairs.Count == 0) return double.PositiveInfinity;
            double sum = 0.0;
            int counted = 0;
            for (int k = 0; k < pairs.Count; k++)
            {
                var p = pairs[k];
                if (p.A < 0 || p.A >= a.Count || p.B < 0 || p.B >= b.Count) continue;
                double d = Dist(a[p.A], b[p.B]);
                if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                sum += d;
                counted++;
            }
            return counted == 0 ? double.PositiveInfinity : sum / counted;
        }

        // ---- helpers -------------------------------------------------------

        private static IReadOnlyList<Point3d> RotateAndMaybeReverse(
            IReadOnlyList<Point3d> b, int offset, bool reversed)
        {
            int m = b.Count;
            var outp = new Point3d[m];
            for (int k = 0; k < m; k++)
            {
                int src = reversed
                    ? Mod(offset - k, m)
                    : Mod(offset + k, m);
                outp[k] = b[src];
            }
            return outp;
        }

        private static int OriginalBIndex(int rotatedIndex, int offset, bool reversed, int m)
        {
            return reversed
                ? Mod(offset - rotatedIndex, m)
                : Mod(offset + rotatedIndex, m);
        }

        private static int NearestIndex(Point3d p, IReadOnlyList<Point3d> pts)
        {
            int best = 0;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = Sq(Dist(p, pts[i]));
                if (double.IsNaN(d)) continue;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static double Dist(Point3d p, Point3d q)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double Sq(double v) => v * v;

        private static int Mod(int x, int m)
        {
            int r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}
