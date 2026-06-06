#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Pure-managed tests: OrderedBoundaryMatcher uses only Point3d as a value
// container and Math.*, no RhinoCommon native (rhcommon_c) calls, so these
// run on hosts without a live Rhino process.
static class EdgeMatchingOrderedMatcherTests
{
    // (a) The correspondence is monotone (non-crossing): matched indices are
    // strictly increasing in BOTH sequences. This is the core guarantee.
    public static void MatchOpen_ProducesMonotoneCorrespondence()
    {
        var a = new List<Point3d>();
        var b = new List<Point3d>();
        // Two parallel rims, slightly offset, equal length.
        for (int i = 0; i < 12; i++)
        {
            double x = i;
            a.Add(new Point3d(x, 0.0, 0.0));
            b.Add(new Point3d(x + 0.1, 1.0, 0.0));
        }

        var pairs = OrderedBoundaryMatcher.MatchOpen(a, b);
        Assert(pairs.Count > 0, "expected at least one matched pair");

        int prevA = -1, prevB = -1;
        foreach (var p in pairs)
        {
            Assert(p.A > prevA, $"A indices must be strictly increasing, saw {p.A} after {prevA}");
            Assert(p.B >= prevB, $"B indices must be non-decreasing, saw {p.B} after {prevB}");
            prevA = p.A;
            prevB = p.B;
        }
    }

    // (b) On a synthetic wiggly rim where free nearest-neighbour CROSSES, the
    // monotone matcher does NOT cross. Both correspondences are measured in
    // the SAME index frame (MatchOpen, no orientation flip), so the crossing
    // count is a direct apples-to-apples comparison.
    //
    // Construction: A and B are two rims sampled left-to-right. B carries a
    // local "swap" wiggle: a later B-point sits laterally next to an earlier
    // A-point, so free NN pairs them out of order (a crossing). The monotone
    // DP refuses to cross and keeps the index order, which is the whole point
    // of the non-crossing primitive.
    public static void MatchOpen_BeatsNearestNeighbour_OnWigglyRim()
    {
        int n = 20;
        var a = new List<Point3d>();
        var b = new List<Point3d>();
        for (int i = 0; i < n; i++)
        {
            double x = i;
            a.Add(new Point3d(x, 0.0, 0.0));
            // Base B rim is 1.0 above A, same left-to-right order.
            b.Add(new Point3d(x, 1.0, 0.0));
        }
        // Inject local "near-swaps": nudge adjacent B-points past each other in
        // X so that, locally, B[i+1] is closer to A[i] than B[i] is. Free NN
        // then crosses on those pairs; the monotone DP does not.
        for (int i = 2; i < n - 2; i += 4)
        {
            var bi = b[i];
            var bj = b[i + 1];
            // Swap their X coordinates -> B[i] now sits to the right of B[i+1].
            b[i] = new Point3d(bj.X, bi.Y, 0.0);
            b[i + 1] = new Point3d(bi.X, bj.Y, 0.0);
        }

        // Free nearest-neighbour correspondence the OLD ICP path uses.
        var nn = new List<OrderedBoundaryMatcher.Pair>(n);
        for (int i = 0; i < n; i++)
            nn.Add(new OrderedBoundaryMatcher.Pair(i, NearestIndex(a[i], b)));
        int nnCrossings = CrossingCount(nn);

        // Ordered (monotone) correspondence.
        var ordered = OrderedBoundaryMatcher.MatchOpen(a, b);
        Assert(ordered.Count > 0, "expected ordered pairs");
        int orderedCrossings = CrossingCount(ordered);

        Assert(nnCrossings > 0,
            $"test premise: free NN should cross on the swapped wiggly rim, got {nnCrossings}");
        Assert(orderedCrossings == 0,
            $"ordered correspondence must be non-crossing, got {orderedCrossings}");
        Assert(orderedCrossings < nnCrossings,
            $"ordered ({orderedCrossings}) must beat NN ({nnCrossings}) on crossings");
    }

    // (b2) Closed-rim path: complementary rims traverse the shared curve in
    // OPPOSITE senses. MatchClosed must pick the reversed orientation and
    // return a correspondence that is non-crossing IN THE ORIENTATION IT
    // MATCHED. We verify that by checking the pairing is monotone once B's
    // reversal is undone (i.e. B-index decreases as A-index increases, with
    // no order violations within that decreasing run).
    public static void MatchClosed_PicksReversedOrientation_OnComplementaryRim()
    {
        int n = 16;
        var a = new List<Point3d>();
        for (int i = 0; i < n; i++)
            a.Add(new Point3d(i, Math.Sin(i * 0.5) * 0.3, 0.0));
        // B = same physical curve, opposite traversal sense, 0.05 offset in +Y.
        var b = new List<Point3d>(n);
        for (int i = 0; i < n; i++)
        {
            var src = a[n - 1 - i];
            b.Add(new Point3d(src.X, src.Y + 0.05, 0.0));
        }

        var ordered = OrderedBoundaryMatcher.MatchClosed(a, b, phaseOffset: 0, offsetBracket: 2);
        Assert(ordered.Count > 0, "expected ordered pairs on complementary rim");

        // The matched mean distance should be tiny (~0.05): proof the matcher
        // found the correct opposite-sense alignment, not a tangled one.
        double mean = OrderedBoundaryMatcher.MeanMatchedDistance(a, b, ordered);
        Assert(mean < 0.2,
            $"complementary rim should align tightly (~0.05), got mean {mean}");

        // In the matcher's chosen (reversed) frame the pairing is monotone:
        // A increases while B decreases, monotonically. Verify B is strictly
        // monotone (here: non-increasing) across the pair list sorted by A.
        bool nonIncreasing = true, nonDecreasing = true;
        for (int k = 1; k < ordered.Count; k++)
        {
            if (ordered[k].B > ordered[k - 1].B) nonIncreasing = false;
            if (ordered[k].B < ordered[k - 1].B) nonDecreasing = false;
        }
        Assert(nonIncreasing || nonDecreasing,
            "complementary-rim correspondence must be monotone (non-crossing) in B");
    }

    // Robustness: empty / single-point inputs do not throw and return a
    // sensible (possibly empty) correspondence.
    public static void MatchOpen_EmptyAndSinglePoint_NoThrow()
    {
        var empty = new List<Point3d>();
        var one = new List<Point3d> { new Point3d(0, 0, 0) };
        var many = new List<Point3d>
        {
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(2, 0, 0),
        };

        var r1 = OrderedBoundaryMatcher.MatchOpen(empty, many);
        Assert(r1.Count == 0, "empty A should produce no pairs");

        var r2 = OrderedBoundaryMatcher.MatchOpen(one, many);
        Assert(r2.Count == 1 && r2[0].A == 0, "single-A should produce one pair");

        var r3 = OrderedBoundaryMatcher.MatchOpen(many, one);
        Assert(r3.Count == 1 && r3[0].B == 0, "single-B should produce one pair");
    }

    // Determinism: same input -> identical pairing across runs.
    public static void MatchClosed_IsDeterministic()
    {
        var a = new List<Point3d>();
        var b = new List<Point3d>();
        var rng = new Random(7);
        for (int i = 0; i < 16; i++)
        {
            double t = i;
            a.Add(new Point3d(t, Math.Cos(t * 0.4) * 0.3, 0));
            b.Add(new Point3d(t + 0.02, Math.Cos(t * 0.4) * 0.3 + 0.05, 0));
        }

        var r1 = OrderedBoundaryMatcher.MatchClosed(a, b, 0, 2);
        var r2 = OrderedBoundaryMatcher.MatchClosed(a, b, 0, 2);
        Assert(r1.Count == r2.Count, "deterministic: same pair count");
        for (int i = 0; i < r1.Count; i++)
            Assert(r1[i].A == r2[i].A && r1[i].B == r2[i].B,
                $"deterministic: pair {i} differs between runs");
    }

    // ---- helpers -----------------------------------------------------------

    // Count crossing pairs: (i,j) and (i',j') cross when the A-order and the
    // B-order disagree, i.e. (Ai - Ai') and (Bj - Bj') have opposite signs.
    private static int CrossingCount(IList<OrderedBoundaryMatcher.Pair> pairs)
    {
        int crossings = 0;
        for (int i = 0; i < pairs.Count; i++)
            for (int k = i + 1; k < pairs.Count; k++)
            {
                int da = pairs[i].A - pairs[k].A;
                int db = pairs[i].B - pairs[k].B;
                if ((long)da * db < 0) crossings++;
            }
        return crossings;
    }

    private static int NearestIndex(Point3d p, IList<Point3d> pts)
    {
        int best = 0;
        double bestD = double.PositiveInfinity;
        for (int i = 0; i < pts.Count; i++)
        {
            double dx = p.X - pts[i].X, dy = p.Y - pts[i].Y, dz = p.Z - pts[i].Z;
            double d = dx * dx + dy * dy + dz * dz;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
