#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// BlockCuttingRobustnessTests — Phase 5 of the robustness pass.
// Block-size distribution analyzer + fragment merger.
// =============================================================================

static class BlockCuttingRobustnessTests
{
    // ─── BlockSizeDistribution ──────────────────────────────────────────

    public static void BlockSize_UniformPieces_LowCV()
    {
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1, 1, 1),
            Slab.Box(0, 0, 0, 1, 1, 1),
            Slab.Box(0, 0, 0, 1, 1, 1),
            Slab.Box(0, 0, 0, 1, 1, 1),
        };
        var rep = BlockSizeDistribution.Analyse(pieces);
        Assert(rep.Count == 4, $"count {rep.Count}");
        Assert(Math.Abs(rep.Mean - 1.0) < 1e-9, $"mean {rep.Mean}");
        Assert(rep.CoefficientOfVariation < 1e-9,
            $"uniform set CV expected ~0, got {rep.CoefficientOfVariation}");
        Assert(rep.OutlierIndices.Count == 0, $"outliers {rep.OutlierIndices.Count}");
    }

    public static void BlockSize_OneHugeOutlier_FlaggedByTukey()
    {
        // Three small + one huge — the huge one falls outside the Tukey fence.
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1, 1, 1),     // 1.0
            Slab.Box(0, 0, 0, 1, 1, 1),     // 1.0
            Slab.Box(0, 0, 0, 1, 1, 1),     // 1.0
            Slab.Box(0, 0, 0, 10, 10, 10),  // 1000.0 — outlier
        };
        var rep = BlockSizeDistribution.Analyse(pieces);
        Assert(rep.OutlierIndices.Count >= 1,
            $"expected at least 1 outlier, got {rep.OutlierIndices.Count}");
        Assert(rep.OutlierIndices.Contains(3), "huge piece must be flagged");
        Assert(rep.CoefficientOfVariation > 1.0,
            $"expected high CV, got {rep.CoefficientOfVariation}");
    }

    public static void BlockSize_Histogram_BinsCoverData()
    {
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1, 1, 1),
            Slab.Box(0, 0, 0, 2, 1, 1),
            Slab.Box(0, 0, 0, 3, 1, 1),
            Slab.Box(0, 0, 0, 4, 1, 1),
        };
        var rep = BlockSizeDistribution.Analyse(pieces, binCount: 4);
        int sum = 0;
        for (int i = 0; i < rep.BinCounts.Count; i++) sum += rep.BinCounts[i];
        Assert(sum == 4, $"hist must cover all 4, got {sum}");
        Assert(rep.BinCounts.Count == 4, $"bin count {rep.BinCounts.Count}");
    }

    public static void BlockSize_EmptyInput_DegenerateButSafe()
    {
        var rep = BlockSizeDistribution.Analyse(Array.Empty<Slab>());
        Assert(rep.Count == 0, $"count {rep.Count}");
        Assert(rep.TotalVolume == 0.0, $"total {rep.TotalVolume}");
    }

    // ─── FragmentMerger ─────────────────────────────────────────────────

    public static void Merger_SmallShardMergesIntoLargeNeighbour()
    {
        // Two pieces: a big one and a tiny adjacent shard.
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1.0, 1.0, 1.0),   // 1.0
            Slab.Box(0, 0, 0, 0.001, 1, 1),     // 0.001 — sliver
        };
        var adj = new[] { (0, 1) };
        var res = FragmentMerger.Merge(pieces, adj, thresholdFraction: 0.1);
        Assert(res.HostOf[0] == 0, $"piece 0 should be its own host, got {res.HostOf[0]}");
        Assert(res.HostOf[1] == 0, $"piece 1 should merge into piece 0, got {res.HostOf[1]}");
        Assert(res.MergedCount == 1, $"merged {res.MergedCount}");
        Assert(Math.Abs(res.MergedVolume[0] - 1.001) < 1e-9,
            $"host volume {res.MergedVolume[0]}");
    }

    public static void Merger_IsolatedSliver_KeepsItself()
    {
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1, 1, 1),
            Slab.Box(0, 0, 0, 0.001, 1, 1),
        };
        // No adjacency edges → isolated.
        var res = FragmentMerger.Merge(pieces, Array.Empty<(int, int)>(),
            thresholdFraction: 0.1);
        Assert(res.HostOf[0] == 0, "p0 host self");
        Assert(res.HostOf[1] == 1, "isolated sliver remains its own host");
        Assert(res.MergedCount == 0, $"merged {res.MergedCount}");
    }

    public static void Merger_ChainOfSlivers_AllAccreteToLargest()
    {
        // [big, sliver, sliver, sliver] in a chain. All slivers should
        // eventually be hosted by the big one.
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 10, 1, 1),       // 10
            Slab.Box(0, 0, 0, 0.01, 1, 1),     // 0.01
            Slab.Box(0, 0, 0, 0.01, 1, 1),     // 0.01
            Slab.Box(0, 0, 0, 0.01, 1, 1),     // 0.01
        };
        var adj = new[] { (0, 1), (1, 2), (2, 3) };
        var res = FragmentMerger.Merge(pieces, adj, thresholdFraction: 0.5);
        Assert(res.HostOf[0] == 0, "big stays self");
        Assert(res.HostOf[1] == 0, "sliver 1 → big");
        Assert(res.HostOf[2] == 0, "sliver 2 → big");
        Assert(res.HostOf[3] == 0, "sliver 3 → big (via inherited adjacency)");
        Assert(res.MergedCount == 3, $"merged {res.MergedCount}");
    }

    public static void Merger_NullAdjacency_Throws()
    {
        bool threw = false;
        try
        {
            _ = FragmentMerger.Merge(
                new[] { Slab.Box(0, 0, 0, 1, 1, 1) }, null);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null adjacency must throw");
    }

    // ─── GH metadata ────────────────────────────────────────────────────

    public static void Gh_BlockSizeDistributionComponent_Metadata()
    {
        var c = new BlockSizeDistributionComponent();
        Assert(c.ComponentGuid == new Guid("EF012345-6789-ABCD-EF01-23456789ABCD"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.Params.Input.Count == 2, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 13, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_FragmentMergerComponent_Metadata()
    {
        var c = new FragmentMergerComponent();
        Assert(c.ComponentGuid == new Guid("F0123456-789A-BCDE-F012-3456789ABCDE"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.Params.Input.Count == 4, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
