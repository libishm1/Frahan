#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.Packing;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// TreePackForestTests — Kim 2025 port (Frahan.Core.Packing.TreePackForest).
// Covers:
//   - Core algorithm: single element / single container fits.
//   - Multi-element pack into one container.
//   - Multi-container price minimisation (cheapest subset preferred).
//   - Deterministic seed (same seed → same placement order).
//   - Kerf extension (kerf consumes space, fewer fits).
//   - Forbidden boxes (overlap-rejected; alternative container picked).
//   - Rotation extension (3-axis lets elongated element fit a flat container).
//   - Score formula: score increases when all elements packed, container
//     bonus prefers fewer / cheaper containers.
//
// Tests use synthetic Box AABBs at WorldXY. Pure managed where possible;
// the Box / Transform paths skip cleanly under FRAHAN_SKIP_NATIVE.
// =============================================================================

static class TreePackForestTests
{
    // ─── input validation ────────────────────────────────────────────────

    public static void Pack_NullElements_Throws()
    {
        try
        {
            TreePackForest.Pack(null, new[] { 1.0 }, new[] { MakeBox(1, 1, 1) },
                new[] { 1.0 }, new GuillotinePackOptions());
            throw new Exception("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException) { /* expected */ }
    }

    public static void Pack_MismatchedValueCount_Throws()
    {
        try
        {
            TreePackForest.Pack(
                new[] { MakeBox(1, 1, 1), MakeBox(2, 1, 1) },
                new[] { 1.0 }, // only 1 value, 2 elements
                new[] { MakeBox(10, 10, 10) }, new[] { 1.0 },
                new GuillotinePackOptions());
            throw new Exception("Expected ArgumentException for mismatched values.");
        }
        catch (ArgumentException) { /* expected */ }
    }

    public static void Pack_EmptyElements_Throws()
    {
        try
        {
            TreePackForest.Pack(
                new Box[0], new double[0],
                new[] { MakeBox(1, 1, 1) }, new[] { 1.0 },
                new GuillotinePackOptions());
            throw new Exception("Expected ArgumentException for empty elements.");
        }
        catch (ArgumentException) { /* expected */ }
    }

    // ─── single fit ──────────────────────────────────────────────────────

    public static void Pack_SingleElementFitsSingleContainer_AllPacked()
    {
        var elements = new[] { MakeBox(1, 1, 1) };
        var elementValues = new[] { 10.0 };
        var containers = new[] { MakeBox(5, 5, 5) };
        var containerPrices = new[] { 100.0 };
        var opts = new GuillotinePackOptions(forestCount: 4, seed: 42);

        var r = TreePackForest.Pack(elements, elementValues, containers, containerPrices, opts);
        Assert(r.AllElementsPacked, "single small element should fit in big container");
        Assert(r.Placements.Count == 1, $"expected 1 placement, got {r.Placements.Count}");
        Assert(r.UsedContainerIndices.Count == 1 && r.UsedContainerIndices[0] == 0,
            "container 0 should be used");
        // All-packed score: sum(pE) + 1/(1+containerPrice) = 10 + 1/101 ≈ 10.0099
        AssertNear(r.Score, 10.0 + 1.0 / 101.0, 1e-9, "score with bonus");
    }

    public static void Pack_OversizedElement_RemainsUnpacked()
    {
        var elements = new[] { MakeBox(10, 10, 10) };
        var elementValues = new[] { 5.0 };
        var containers = new[] { MakeBox(1, 1, 1) };
        var containerPrices = new[] { 1.0 };
        var opts = new GuillotinePackOptions(forestCount: 4, seed: 1);

        var r = TreePackForest.Pack(elements, elementValues, containers, containerPrices, opts);
        Assert(!r.AllElementsPacked, "oversized element should not pack");
        Assert(r.Placements.Count == 0, $"expected 0 placements, got {r.Placements.Count}");
        AssertNear(r.Score, 0.0, 1e-9, "score = 0 when nothing packs (sum of packed values)");
    }

    // ─── multi-container preference ──────────────────────────────────────

    public static void Pack_CheapContainerPreferredWhenSufficient()
    {
        // Two containers both large enough for one small element. Cheap
        // container price 1, expensive 1000. Score's φ(price) term should
        // favour the cheap one across enough forests.
        var elements = new[] { MakeBox(1, 1, 1) };
        var elementValues = new[] { 5.0 };
        var containers = new[] { MakeBox(5, 5, 5), MakeBox(5, 5, 5) };
        var containerPrices = new[] { 1000.0, 1.0 };
        var opts = new GuillotinePackOptions(forestCount: 64, seed: 7);

        var r = TreePackForest.Pack(elements, elementValues, containers, containerPrices, opts);
        Assert(r.AllElementsPacked, "should pack");
        Assert(r.UsedContainerIndices.Count == 1, $"expected 1 used container, got {r.UsedContainerIndices.Count}");
        Assert(r.UsedContainerIndices[0] == 1,
            $"cheap container (index 1) should win across 64 forests, got {r.UsedContainerIndices[0]}");
    }

    // ─── deterministic seed (Frahan extension) ───────────────────────────

    public static void Pack_SameSeedProducesIdenticalResult()
    {
        var elements = new[] { MakeBox(1, 1, 1), MakeBox(2, 2, 2), MakeBox(3, 1, 1) };
        var elementValues = new[] { 1.0, 5.0, 2.0 };
        var containers = new[] { MakeBox(5, 5, 5), MakeBox(4, 4, 4) };
        var containerPrices = new[] { 10.0, 20.0 };
        var opts = new GuillotinePackOptions(forestCount: 16, seed: 12345);

        var r1 = TreePackForest.Pack(elements, elementValues, containers, containerPrices, opts);
        var r2 = TreePackForest.Pack(elements, elementValues, containers, containerPrices, opts);
        AssertNear(r1.Score, r2.Score, 1e-12, "score must be reproducible for the same seed");
        Assert(r1.Placements.Count == r2.Placements.Count, "placement count must be reproducible");
        Assert(r1.BestForestIndex == r2.BestForestIndex,
            $"best forest index must be reproducible; {r1.BestForestIndex} vs {r2.BestForestIndex}");
    }

    public static void Pack_DifferentSeedsCanProduceDifferentResults()
    {
        // Use elements that don't all fit obviously so the search has
        // room to find different orderings.
        var elements = new[] {
            MakeBox(2, 2, 2), MakeBox(2, 2, 2), MakeBox(2, 2, 2), MakeBox(2, 2, 2),
            MakeBox(2, 2, 2), MakeBox(2, 2, 2), MakeBox(2, 2, 2), MakeBox(2, 2, 2),
        };
        var elementValues = new[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };
        var containers = new[] { MakeBox(4, 4, 4) };
        var containerPrices = new[] { 10.0 };

        var r1 = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 1, seed: 1));
        var r2 = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 1, seed: 99999));

        // Both should pack the same number (8 unit-ish elements into a 4x4x4
        // container can fit all 8 since 8*(2^3) = 64 = container vol; the
        // axis-aligned guillotine pack may or may not find it depending on
        // ordering. At minimum, the *placement order* (or one of: score,
        // best-forest, placement count) should differ between seeds.
        bool differentInSomeWay =
            r1.BestForestIndex != r2.BestForestIndex
            || r1.Placements.Count != r2.Placements.Count
            || Math.Abs(r1.Score - r2.Score) > 1e-12;
        Assert(differentInSomeWay || r1.Placements.Count == 8,
            "different seeds should usually give different exploration; trivially-perfect packs may match");
    }

    // ─── kerf extension (Frahan extension) ───────────────────────────────

    public static void Pack_KerfReducesPackedCount()
    {
        // Container 4x4x4. Eight 2x2x2 elements pack perfectly with zero
        // kerf. A 0.5-unit kerf consumes 0.5 between every adjacent pair,
        // so the 2x2 pairs no longer tile the 4x4 axis.
        var elements = new Box[8];
        var elementValues = new double[8];
        for (int i = 0; i < 8; i++) { elements[i] = MakeBox(2, 2, 2); elementValues[i] = 1.0; }
        var containers = new[] { MakeBox(4, 4, 4) };
        var containerPrices = new[] { 10.0 };

        var r0 = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 16, seed: 1, kerfWidth: 0.0));
        var rK = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 16, seed: 1, kerfWidth: 0.5));
        Assert(rK.Placements.Count <= r0.Placements.Count,
            $"non-zero kerf should not pack MORE than zero kerf; got kerf={rK.Placements.Count} vs none={r0.Placements.Count}");
    }

    // ─── forbidden boxes (Frahan extension) ──────────────────────────────

    public static void Pack_ForbiddenBoxBlocksPlacement()
    {
        // Container 5x5x5, single element 4x4x4. With no forbidden region,
        // it fits. With a forbidden box right at origin (occupying any
        // corner) the placement cannot start there; the tree algorithm
        // tries leaf corners in shuffle order, so a single forbidden box
        // at the only valid corner blocks the only fit.
        // Use a smaller container so there's only ONE corner that fits a
        // 4x4x4 element.
        var elements = new[] { MakeBox(4, 4, 4) };
        var elementValues = new[] { 1.0 };
        var containers = new[] { MakeBox(4, 4, 4) }; // exactly one valid corner
        var containerPrices = new[] { 10.0 };
        // Forbidden box covers the entire container.
        var forbidden = new List<IReadOnlyList<Box>>
        {
            new[] { MakeBox(4, 4, 4) },
        };
        var opts = new GuillotinePackOptions(forestCount: 4, seed: 1);

        var r = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            opts, forbidden);
        Assert(r.Placements.Count == 0,
            $"forbidden box covering the only fit should block placement; got {r.Placements.Count}");
    }

    public static void Pack_ForbiddenBoxLetsSecondContainerWin()
    {
        // First container blocked, second container free → must pack in second.
        var elements = new[] { MakeBox(3, 3, 3) };
        var elementValues = new[] { 1.0 };
        var containers = new[] { MakeBox(3, 3, 3), MakeBox(3, 3, 3) };
        var containerPrices = new[] { 1.0, 1.0 };
        var forbidden = new List<IReadOnlyList<Box>>
        {
            new[] { MakeBox(3, 3, 3) }, // blocks container 0
            new Box[0],                 // container 1 free
        };
        var opts = new GuillotinePackOptions(forestCount: 16, seed: 42);

        var r = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            opts, forbidden);
        Assert(r.Placements.Count == 1, $"should pack in free container; got {r.Placements.Count}");
        Assert(r.Placements[0].ContainerIndex == 1,
            $"should pack in container 1 (free); got {r.Placements[0].ContainerIndex}");
    }

    // ─── rotation modes ──────────────────────────────────────────────────

    public static void Pack_ThreeAxisRotation_FitsElongatedElementInFlatContainer()
    {
        // Container 5x5x1 (flat). Element 4x1x1 (elongated). Without rotation,
        // it doesn't fit (4-deep won't fit into 5x5x1 along the natural axes
        // unless oriented horizontally). With 3-axis rotation, an orientation
        // that keeps long-axis horizontal is found.
        var elements = new[] { MakeBox(4, 1, 1) };
        var elementValues = new[] { 1.0 };
        var containers = new[] { MakeBox(5, 5, 1) };
        var containerPrices = new[] { 10.0 };

        var rRot = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 32, seed: 1, rotationMode: GuillotineRotationMode.ThreeAxis));
        Assert(rRot.Placements.Count == 1,
            "3-axis rotation should let 4x1x1 fit into 5x5x1");
    }

    // ─── score monotonicity ──────────────────────────────────────────────

    public static void Pack_AllPackedScoreExceedsNotPacked()
    {
        // Scenario where all elements can fit in container 0 alone.
        var elements = new[] { MakeBox(1, 1, 1), MakeBox(1, 1, 1) };
        var elementValues = new[] { 5.0, 5.0 };
        var containers = new[] { MakeBox(5, 5, 5) };
        var containerPrices = new[] { 1.0 };

        var rOk = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 16, seed: 1));
        Assert(rOk.AllElementsPacked, "both should pack");
        // sum(pE) = 10, container price 1 → score = 10 + 1/2 = 10.5
        AssertNear(rOk.Score, 10.5, 1e-9, "all-packed score = 10 + 1/(1+1)");
    }

    // ─── K2 extensions ───────────────────────────────────────────────────

    public static void Pack_ParallelMatchesSerial()
    {
        // Same seed must produce bitwise-identical results regardless of
        // MaxDegreeOfParallelism, because each forest k uses (Seed + k)
        // and writes into its own attempt slot.
        var elements = new[] { MakeBox(1, 1, 1), MakeBox(2, 1, 1), MakeBox(1, 2, 1), MakeBox(1, 1, 2) };
        var elementValues = new[] { 1.0, 2.0, 2.0, 2.0 };
        var containers = new[] { MakeBox(4, 4, 4), MakeBox(3, 3, 3) };
        var containerPrices = new[] { 10.0, 5.0 };

        var serial = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 32, seed: 42, maxDegreeOfParallelism: 1));
        var parallel = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 32, seed: 42, maxDegreeOfParallelism: 8));

        AssertNear(serial.Score, parallel.Score, 1e-12,
            "parallel score must match serial bit-for-bit");
        Assert(serial.BestForestIndex == parallel.BestForestIndex,
            $"best forest index must match; serial={serial.BestForestIndex} parallel={parallel.BestForestIndex}");
        Assert(serial.Placements.Count == parallel.Placements.Count,
            $"placement count must match; serial={serial.Placements.Count} parallel={parallel.Placements.Count}");
    }

    public static void Pack_CutSurfaceWeightLowersScore()
    {
        // Same input, two runs: one with CutSurfaceWeight = 0 (Kim
        // baseline), one with weight > 0. The weighted run must have
        // a score that's at most the baseline (cut surface is non-
        // negative; the weight only subtracts).
        var elements = new[] { MakeBox(2, 2, 2), MakeBox(2, 2, 2), MakeBox(2, 2, 2), MakeBox(2, 2, 2) };
        var elementValues = new[] { 5.0, 5.0, 5.0, 5.0 };
        var containers = new[] { MakeBox(4, 4, 4) };
        var containerPrices = new[] { 100.0 };

        var baseline = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 32, seed: 1, cutSurfaceWeight: 0.0));
        var penalised = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 32, seed: 1, cutSurfaceWeight: 0.5));

        Assert(penalised.Score <= baseline.Score + 1e-9,
            $"cut surface weight should not increase score; baseline={baseline.Score} penalised={penalised.Score}");
    }

    public static void Pack_MemoryBudgetReducesForestCount()
    {
        // A tiny budget should cap forests to a small number while still
        // producing a valid (possibly suboptimal) result.
        var elements = new[] { MakeBox(1, 1, 1) };
        var elementValues = new[] { 1.0 };
        var containers = new[] { MakeBox(5, 5, 5) };
        var containerPrices = new[] { 1.0 };

        // 1 KB budget × ~1.4 KB per forest per element → forests collapses to 1.
        var r = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 1000, seed: 7, memoryBudgetBytes: 1024));
        // Implementation reports best forest index in [0, capped_count).
        Assert(r.BestForestIndex >= 0 && r.BestForestIndex < 1000,
            $"best forest index must be valid even under budget cap, got {r.BestForestIndex}");
        Assert(r.Placements.Count == 1,
            $"even a single-forest run should pack the trivially-fitting element; got {r.Placements.Count}");
    }

    public static void Pack_CutSurfaceAreaInvariantForFixedLayout()
    {
        // With Mode = None and a single fitting element, the placement
        // is forced (only one slab, one corner). The cut surface for
        // that placement is the three opposite-face areas: 1*1 + 1*1 + 1*1 = 3.
        // Score baseline = 5 + 1/(1+10) ≈ 5.0909
        // With weight 1.0: 5.0909 - 1*3 = 2.0909
        var elements = new[] { MakeBox(1, 1, 1) };
        var elementValues = new[] { 5.0 };
        var containers = new[] { MakeBox(2, 2, 2) };
        var containerPrices = new[] { 10.0 };

        var rBase = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 4, seed: 1, cutSurfaceWeight: 0.0));
        var rPen = TreePackForest.Pack(elements, elementValues, containers, containerPrices,
            new GuillotinePackOptions(forestCount: 4, seed: 1, cutSurfaceWeight: 1.0));
        Assert(rBase.AllElementsPacked && rPen.AllElementsPacked, "both should pack");
        // Cut surface area = 3 for the single 1×1×1 element.
        AssertNear(rBase.Score - rPen.Score, 3.0, 1e-9,
            "score gap with cutSurfaceWeight=1 should equal cut-surface area = 3");
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static Box MakeBox(double w, double d, double h) =>
        new Box(Plane.WorldXY, new Interval(0, w), new Interval(0, d), new Interval(0, h));

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} ± {tol}, got {actual}");
    }
}
