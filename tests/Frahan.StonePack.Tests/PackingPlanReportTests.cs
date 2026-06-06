#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.PackingPlanReport + Builder. Pure managed.

static class PackingPlanReportTests
{
    public static void Build_NullMetrics_Throws()
    {
        bool threw = false;
        try { PackingPlanReportBuilder.Build(null, null, null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null PackingMetrics should throw");
    }

    public static void Build_AllNullExceptMetrics_Empty()
    {
        var m = MakeMetrics(2, 1);
        var r = PackingPlanReportBuilder.Build(m, null, null);
        Assert(r.PackingMetrics == m, "metrics passed through");
        Assert(r.ResidualVoids.Count == 0, "null voids -> empty list");
        Assert(r.TotalResidualVoidArea == 0.0, "no voids -> 0 area");
        Assert(r.BestEdgeMatchScores.Count == 0, "null edge scores -> empty list");
        Assert(r.AverageBestEdgeMatchScore == 0.0, "no scores -> 0 avg");
    }

    public static void Build_SumsResidualVoidAreas()
    {
        var voids = new[]
        {
            new ResidualVoid(0, 0, 10, 10, 100.0, 100),
            new ResidualVoid(20, 20, 25, 25, 25.0, 25),
        };
        var r = PackingPlanReportBuilder.Build(MakeMetrics(2, 1), voids, null);
        Assert(r.ResidualVoids.Count == 2, "two voids preserved");
        Assert(Math.Abs(r.TotalResidualVoidArea - 125.0) < 1e-9,
            $"void area sum should be 125, got {r.TotalResidualVoidArea}");
    }

    public static void Build_FlattensPerFragmentEdgeScoresAndAverages()
    {
        var perFragment = new IReadOnlyList<double>[]
        {
            new[] { 1.0, 0.8, 0.6 },     // fragment 0 has 3 edges
            new[] { 0.9, 0.7 },           // fragment 1 has 2 edges
            null!,                         // null branch is tolerated
        };
        var r = PackingPlanReportBuilder.Build(MakeMetrics(2, 1), null, perFragment);
        Assert(r.BestEdgeMatchScores.Count == 5, $"5 edge scores expected, got {r.BestEdgeMatchScores.Count}");
        // Average: (1.0 + 0.8 + 0.6 + 0.9 + 0.7) / 5 = 0.8
        Assert(Math.Abs(r.AverageBestEdgeMatchScore - 0.8) < 1e-9,
            $"avg should be 0.8, got {r.AverageBestEdgeMatchScore}");
    }

    public static void Build_TolerantToNullVoidEntries()
    {
        var voids = new ResidualVoid[]
        {
            new ResidualVoid(0, 0, 1, 1, 1.0, 1),
            null!,
            new ResidualVoid(2, 2, 3, 3, 1.0, 1),
        };
        var r = PackingPlanReportBuilder.Build(MakeMetrics(0, 0), voids, null);
        Assert(r.ResidualVoids.Count == 3, "null entries are still counted in length");
        Assert(Math.Abs(r.TotalResidualVoidArea - 2.0) < 1e-9, "null-area entries ignored in sum");
    }

    public static void ToString_IsHumanReadable()
    {
        var r = PackingPlanReportBuilder.Build(MakeMetrics(3, 1), null, null);
        var s = r.ToString();
        Assert(s.Contains("packed=3"), $"ToString should mention placements; got: {s}");
        Assert(s.Contains("voids="), $"ToString should mention voids; got: {s}");
    }

    private static PackingMetricsReport MakeMetrics(int placed, int failed) =>
        new PackingMetricsReport(placed, failed,
            failureRatio: failed / Math.Max(1.0, placed + failed),
            packedVolume: placed * 10.0,
            containerVolume: 1000.0,
            fillRatio: placed * 10.0 / 1000.0,
            averagePlacementScore: 0.5,
            maxItemHeight: 5.0,
            maxItemVolume: 10.0,
            minItemVolume: 10.0,
            averageItemVolume: 10.0,
            failureReasonCounts: new Dictionary<string, int>());

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
