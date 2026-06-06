#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Sequencing;

namespace Frahan.Tests;

// =============================================================================
// SequencingTests — unit tests for Frahan.Masonry.Sequencing (the C# port
// of Kim 2024 polygonal-masonry depth search). Mirrors the Python
// reference at
// Template-General/outputs/2026-05-20/polygonal_masonry_sequence/tests/test_all.py.
// Pure-managed; no Rhino runtime needed.
// =============================================================================

static class SequencingTests
{
    // ─── Geom2D ─────────────────────────────────────────────────────────────

    public static void Geom_Orient_BasicCases()
    {
        Assert(Geom2D.Orient((0, 0), (1, 0), (1, 1)) == 1, "CCW must return +1");
        Assert(Geom2D.Orient((0, 0), (1, 0), (1, -1)) == -1, "CW must return -1");
        Assert(Geom2D.Orient((0, 0), (1, 0), (2, 0)) == 0, "collinear must return 0");
    }

    public static void Geom_SignedArea_CcwIsPositive()
    {
        var ccw = new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1), (0, 1) };
        var cw = new List<(double X, double Y)> { (0, 1), (1, 1), (1, 0), (0, 0) };
        AssertAlmostEqual(Geom2D.SignedArea(ccw), 1.0, 1e-9, "CCW unit square area");
        AssertAlmostEqual(Geom2D.SignedArea(cw), -1.0, 1e-9, "CW unit square signed area");
    }

    public static void Geom_PointInRing_InsideAndOutside()
    {
        var ring = new List<(double X, double Y)> { (0, 0), (2, 0), (2, 2), (0, 2) };
        Assert(Geom2D.PointInRing((1, 1), ring), "centre must be inside");
        Assert(!Geom2D.PointInRing((3, 1), ring), "exterior point must be outside");
    }

    public static void Geom_ChainIsMonotone_AcceptsXAndVerticalY()
    {
        var xMono = new List<(double X, double Y)> { (0, 1), (1, 2), (2, 1) };
        var notMono = new List<(double X, double Y)> { (0, 1), (1, 2), (0.5, 0.5) };
        var vertical = new List<(double X, double Y)> { (3, 0), (3, 1), (3, 2) };
        Assert(Geom2D.ChainIsMonotone(xMono), "x-monotone chain must be accepted");
        Assert(!Geom2D.ChainIsMonotone(notMono), "non-monotone chain must be rejected");
        Assert(Geom2D.ChainIsMonotone(vertical), "vertical chain must be accepted");
    }

    public static void Geom_RingCentroid_OfUnitSquareIsCentre()
    {
        var ring = new List<(double X, double Y)> { (0, 0), (2, 0), (2, 2), (0, 2) };
        var c = Geom2D.RingCentroid(ring);
        AssertAlmostEqual(c.X, 1.0, 1e-9, "centroid X");
        AssertAlmostEqual(c.Y, 1.0, 1e-9, "centroid Y");
    }

    // ─── PSLG ───────────────────────────────────────────────────────────────

    public static void Pslg_UnitSquare_HasOneBoundedFace()
    {
        var segs = new List<((double X, double Y) A, (double X, double Y) B)>
        {
            ((0, 0), (1, 0)),
            ((1, 0), (1, 1)),
            ((1, 1), (0, 1)),
            ((0, 1), (0, 0)),
        };
        var pslg = Pslg.FromSegments(segs);
        var bounded = pslg.BoundedFaces();
        Assert(bounded.Count == 1, $"expected 1 bounded face, got {bounded.Count}");
        AssertAlmostEqual(Math.Abs(bounded[0].SignedArea), 1.0, 1e-9, "area");
        Assert(pslg.Faces.Count == 2, $"expected 2 total faces, got {pslg.Faces.Count}");
    }

    public static void Pslg_TwoSquaresSharingEdge_TJunctionSplit()
    {
        // 2x1 rectangle plus a middle vertical line whose endpoints are
        // not pre-split on the perimeter. The PSLG must T-split the
        // perimeter to recover two unit-area regions.
        var segs = new List<((double X, double Y) A, (double X, double Y) B)>
        {
            ((0, 0), (2, 0)),
            ((2, 0), (2, 1)),
            ((2, 1), (0, 1)),
            ((0, 1), (0, 0)),
            ((1, 0), (1, 1)),
        };
        var pslg = Pslg.FromSegments(segs);
        var bounded = pslg.BoundedFaces();
        Assert(bounded.Count == 2, $"expected 2 bounded faces, got {bounded.Count}");
        foreach (var f in bounded)
        {
            AssertAlmostEqual(Math.Abs(f.SignedArea), 1.0, 1e-9, "each face area");
        }
    }

    // ─── Reversed Kahn's ────────────────────────────────────────────────────

    public static void Kahn_LinearChain_AssignsDescendingDepth()
    {
        var graph = new Dictionary<int, List<int>>
        {
            { 0, new List<int> { 1 } },
            { 1, new List<int> { 2 } },
            { 2, new List<int>() },
        };
        var d = Wall.ReversedKahnDepths(graph);
        Assert(d[0] == 2 && d[1] == 1 && d[2] == 0, $"depths {d[0]},{d[1]},{d[2]}");
    }

    public static void Kahn_TransitiveEdgeInvariance()
    {
        var g1 = new Dictionary<int, List<int>>
        {
            { 0, new List<int> { 1 } },
            { 1, new List<int> { 2 } },
            { 2, new List<int>() },
        };
        var g2 = new Dictionary<int, List<int>>
        {
            { 0, new List<int> { 1, 2 } },
            { 1, new List<int> { 2 } },
            { 2, new List<int>() },
        };
        var d1 = Wall.ReversedKahnDepths(g1);
        var d2 = Wall.ReversedKahnDepths(g2);
        Assert(d1[0] == d2[0] && d1[1] == d2[1] && d1[2] == d2[2],
            "transitive edge must not change depths");
    }

    public static void Kahn_DiamondGraph_MaxDepthAtSource()
    {
        // 0 -> 1, 0 -> 2, 1 -> 3, 2 -> 3
        var graph = new Dictionary<int, List<int>>
        {
            { 0, new List<int> { 1, 2 } },
            { 1, new List<int> { 3 } },
            { 2, new List<int> { 3 } },
            { 3, new List<int>() },
        };
        var d = Wall.ReversedKahnDepths(graph);
        Assert(d[3] == 0, $"sink depth {d[3]}");
        Assert(d[1] == 1, $"d[1] {d[1]}");
        Assert(d[2] == 1, $"d[2] {d[2]}");
        Assert(d[0] == 2, $"source depth {d[0]}");
    }

    // ─── Wall (full pipeline) ───────────────────────────────────────────────

    public static void Wall_TwoChainThreeBandWall_HasOneStone()
    {
        // Two horizontal chains span the bbox. Three bands: bottom
        // infinite, one stone, top infinite.
        var chains = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double X, double Y)> { (0, 1), (4, 1) },
            new List<(double X, double Y)> { (0, 3), (4, 3) },
        };
        var wall = Wall.FromChains(chains, (0, 0, 4, 4));
        var plan = wall.InstallSequence();
        Assert(wall.Regions.Count == 4,
            $"expected 4 regions (3 bounded + 1 outer), got {wall.Regions.Count}");
        Assert(wall.ActualFiniteRegionCount() == 1,
            $"expected 1 stone, got {wall.ActualFiniteRegionCount()}");
        Assert(wall.ExpectedRegionCount() == 3,
            $"eq. (9) expected 3 (m+1=3), got {wall.ExpectedRegionCount()}");
        foreach (var d in plan.Depth.Values) Assert(d >= 0, "depth must be non-negative");
    }

    public static void Wall_ChainsWithVerticalConnectors_IsAcyclic()
    {
        var chains = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double X, double Y)> { (0, 1.5), (4, 1.2), (8, 1.8), (10, 1.5) },
            new List<(double X, double Y)> { (0, 3.0), (5, 2.7), (10, 3.0) },
            new List<(double X, double Y)> { (0, 4.5), (3.5, 4.9), (7, 4.4), (10, 4.5) },
        };
        var wall = Wall.FromChains(chains, (0, 0, 10, 6));
        var plan = wall.InstallSequence();
        // Forward Kahn must process every node iff DAG is acyclic.
        var (graph, _) = wall.BuildDag();
        var indeg = new Dictionary<int, int>();
        foreach (var v in graph.Keys) indeg[v] = 0;
        foreach (var kvp in graph)
        {
            foreach (var w in kvp.Value)
            {
                if (!indeg.ContainsKey(w)) indeg[w] = 0;
                indeg[w]++;
            }
        }
        var queue = new Stack<int>();
        foreach (var kvp in indeg) if (kvp.Value == 0) queue.Push(kvp.Key);
        int processed = 0;
        while (queue.Count > 0)
        {
            int v = queue.Pop();
            processed++;
            if (!graph.TryGetValue(v, out var succs)) continue;
            foreach (var w in succs)
            {
                indeg[w]--;
                if (indeg[w] == 0) queue.Push(w);
            }
        }
        Assert(processed == indeg.Count,
            $"DAG must be acyclic, processed {processed} of {indeg.Count}");
    }

    public static void Wall_RemoveRegions_ExcludesFromPlan()
    {
        var chains = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double X, double Y)> { (0, 1.5), (4, 1.2), (8, 1.5) },
            new List<(double X, double Y)> { (0, 3.0), (4, 3.0), (8, 3.0) },
            new List<(double X, double Y)> { (0, 4.5), (4, 4.8), (8, 4.5) },
        };
        var wall = Wall.FromChains(chains, (0, 0, 8, 6));
        var planBefore = wall.InstallSequence();
        var (bot, top) = wall.ClassifyTopBottom();
        int? target = null;
        foreach (var r in wall.Regions)
        {
            if (!r.IsFinite) continue;
            if (r.Id == bot || r.Id == top) continue;
            target = r.Id;
            break;
        }
        Assert(target.HasValue, "expected at least one non-bottom non-top finite region");
        wall.RemoveRegions(new[] { target.Value });
        var planAfter = wall.InstallSequence();
        Assert(!planAfter.Order.ContainsKey(target.Value),
            "removed region must not appear in install order");
        Assert(planAfter.Order.Count < planBefore.Order.Count,
            "removing a region must shrink the install plan");
    }

    public static void Wall_RuleEight_CycleDetection()
    {
        // Construct a degenerate wall that should still be acyclic; the
        // primary point is that BuildDag does not throw on valid input.
        var chains = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double X, double Y)> { (0, 2), (10, 2) },
        };
        var wall = Wall.FromChains(chains, (0, 0, 10, 4));
        var plan = wall.InstallSequence();
        Assert(plan.Order.Count >= 1, "must produce at least one ordered region");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertAlmostEqual(double actual, double expected,
                                            double tol, string what)
    {
        if (Math.Abs(actual - expected) > tol)
        {
            throw new InvalidOperationException(
                $"{what}: expected {expected}, got {actual}, tol {tol}");
        }
    }
}
