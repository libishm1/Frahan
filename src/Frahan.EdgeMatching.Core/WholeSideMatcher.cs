#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>Result of scoring two whole sides for complementary fit.</summary>
    internal struct SideFit
    {
        /// <summary>Length-normalized L1 cost (lower = better). MaxValue when rejected.</summary>
        public double Cost;

        /// <summary>True when the reversed orientation of B won (the complementary-seam case).</summary>
        public bool Flip;

        /// <summary>True when the pair is incompatible (a flat side, or chord-length mismatch).</summary>
        public bool Rejected;
    }

    /// <summary>
    /// Whole-side compatibility scorer. Generalizes ryan-puzzle-solver's
    /// <c>error_between_polylines</c>: the minimum, over the two seam orientations, of
    /// the index-aligned L1 distance between the two canonical-frame side polylines,
    /// normalized by side length. Flat (border) sides and length-mismatched pairs are
    /// rejected outright so they never enter the assembler's frontier. True seams score
    /// ~0.2-1.0; spurious pairs &gt; 1.2 -- the separation the fragment hash never had.
    /// </summary>
    internal static class WholeSideMatcher
    {
        /// <summary>Reject if |1 - lenA/lenB| exceeds this (incompatible side lengths).</summary>
        public const double ChordDiscrepancy = 0.15;

        public static SideFit Score(WholeSide a, WholeSide b)
        {
            var r = new SideFit { Cost = double.MaxValue, Flip = false, Rejected = true };
            if (a == null || b == null) return r;
            if (a.IsFlat || b.IsFlat) return r;
            if (a.ChordLength < 1e-9 || b.ChordLength < 1e-9) return r;
            if (Math.Abs(1.0 - a.ChordLength / b.ChordLength) > ChordDiscrepancy) return r;

            double maxLen = Math.Max(a.ChordLength, b.ChordLength);
            if (maxLen < 1e-9) return r;

            double e0 = L1(a.Canonical, b.Canonical);            // same traversal
            double e1 = L1(a.Canonical, b.CanonicalFlipped);     // reversed (complementary)

            double best; bool flip;
            if (e1 <= e0) { best = e1; flip = true; } else { best = e0; flip = false; }
            if (best >= double.MaxValue) return r;

            r.Cost = best / maxLen;
            r.Flip = flip;
            r.Rejected = false;
            return r;
        }

        private static double L1(Point2d[] x, Point2d[] y)
        {
            if (x == null || y == null) return double.MaxValue;
            int k = Math.Min(x.Length, y.Length);
            if (k == 0) return double.MaxValue;
            double e = 0.0;
            for (int i = 0; i < k; i++)
                e += Math.Abs(x[i].X - y[i].X) + Math.Abs(x[i].Y - y[i].Y);
            return e;
        }
    }
}
