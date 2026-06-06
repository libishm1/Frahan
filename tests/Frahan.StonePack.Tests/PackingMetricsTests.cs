#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.PackingMetrics + PackingMetricsReport.
// Pure managed; no Rhino runtime required.

static class PackingMetricsTests
{
    public static void Compute_Null_Throws()
    {
        bool threw = false;
        try { PackingMetrics.Compute(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null result should throw");
    }

    public static void Compute_EmptyResult_ReturnsZeroMetrics()
    {
        var container = new PackContainer(10, 10, 10);
        var result = new PackResult(
            placements: Array.Empty<PackPlacement>(),
            failures: Array.Empty<PackFailure>(),
            heightmap: new Heightmap(container, 1.0),
            container: container);

        var m = PackingMetrics.Compute(result);

        Assert(m.PlacementCount == 0, "empty result -> 0 placements");
        Assert(m.FailureCount == 0, "empty result -> 0 failures");
        Assert(m.FailureRatio == 0.0, "empty result -> 0 failure ratio");
        Assert(m.PackedVolume == 0.0, "empty result -> 0 packed");
        Assert(m.ContainerVolume == 1000.0, $"container vol should be 1000, got {m.ContainerVolume}");
        Assert(m.FillRatio == 0.0, "empty result -> 0 fill");
        Assert(m.AveragePlacementScore == 0.0, "no placements -> avg score 0");
        Assert(m.MaxItemHeight == 0.0, "no placements -> 0 max height");
        Assert(m.MaxItemVolume == 0.0, "no placements -> 0 max item vol");
        Assert(m.MinItemVolume == 0.0, "no placements -> 0 min item vol");
        Assert(m.AverageItemVolume == 0.0, "no placements -> 0 avg item vol");
        Assert(m.FailureReasonCounts.Count == 0, "no failure reasons");
    }

    public static void Compute_MixedResult_ComputesAllFields()
    {
        var container = new PackContainer(10, 10, 10); // 1000 volume

        var item1 = new PackItem("a", new Size3(2, 2, 2)); // vol 8
        var item2 = new PackItem("b", new Size3(3, 3, 3)); // vol 27
        var fail1Item = new PackItem("c", new Size3(20, 20, 20)); // too big

        var p1 = new PackPlacement(item1, new Box3(new Vec3(0, 0, 0), item1.Size), 0.0, 0.5, 0);
        var p2 = new PackPlacement(item2, new Box3(new Vec3(0, 0, 5), item2.Size), 90.0, 0.7, 1);

        var f1 = new PackFailure(fail1Item, "too_big");
        var f2 = new PackFailure(fail1Item, "too_big");
        var f3 = new PackFailure(item1, "no_feasible_candidate");

        var result = new PackResult(
            placements: new[] { p1, p2 },
            failures: new[] { f1, f2, f3 },
            heightmap: new Heightmap(container, 1.0),
            container: container);

        var m = PackingMetrics.Compute(result);

        Assert(m.PlacementCount == 2, "expected 2 placements");
        Assert(m.FailureCount == 3, "expected 3 failures");
        Assert(Math.Abs(m.FailureRatio - 3.0 / 5.0) < 1e-9, $"failure ratio should be 0.6, got {m.FailureRatio}");
        Assert(Math.Abs(m.PackedVolume - 35.0) < 1e-9, $"packed should be 8+27=35, got {m.PackedVolume}");
        Assert(m.ContainerVolume == 1000.0, "container vol should be 1000");
        Assert(Math.Abs(m.FillRatio - 0.035) < 1e-9, $"fill should be 0.035, got {m.FillRatio}");
        Assert(Math.Abs(m.AveragePlacementScore - 0.6) < 1e-9, $"avg score (0.5+0.7)/2=0.6, got {m.AveragePlacementScore}");
        Assert(Math.Abs(m.MaxItemHeight - 8.0) < 1e-9, $"max height should be 5+3=8, got {m.MaxItemHeight}");
        Assert(Math.Abs(m.MaxItemVolume - 27.0) < 1e-9, $"max item vol 27, got {m.MaxItemVolume}");
        Assert(Math.Abs(m.MinItemVolume - 8.0) < 1e-9, $"min item vol 8, got {m.MinItemVolume}");
        Assert(Math.Abs(m.AverageItemVolume - 17.5) < 1e-9, $"avg item vol 17.5, got {m.AverageItemVolume}");

        Assert(m.FailureReasonCounts.Count == 2, "expected 2 distinct failure reasons");
        Assert(m.FailureReasonCounts["too_big"] == 2, "expected 2 'too_big' failures");
        Assert(m.FailureReasonCounts["no_feasible_candidate"] == 1, "expected 1 'no_feasible_candidate' failure");
    }

    public static void Report_ToString_IsHumanReadable()
    {
        var report = new PackingMetricsReport(2, 1, 1.0 / 3.0, 35, 1000, 0.035, 0.6, 8, 27, 8, 17.5,
            new Dictionary<string, int> { ["x"] = 1 });
        string s = report.ToString();
        Assert(s.Contains("placed=2"), $"expected 'placed=2' in {s}");
        Assert(s.Contains("failed=1"), $"expected 'failed=1' in {s}");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
