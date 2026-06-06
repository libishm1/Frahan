#nullable disable
using System;
using Frahan.Surface;

namespace Frahan.Tests;

// Unit tests for Frahan.Surface.ChartFlatnessReport.Classify.
// Pure managed; no Rhino runtime required.

static class ChartFlatnessReportTests
{
    public static void Classify_NullList_Throws()
    {
        bool threw = false;
        try { ChartFlatnessReport.Classify(null, 1.5); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null list should throw");
    }

    public static void Classify_NonPositiveThreshold_Throws()
    {
        bool threw = false;
        try { ChartFlatnessReport.Classify(new[] { 1.0 }, 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "threshold <= 0 should throw");
    }

    public static void Classify_EmptyList_HasZeroFaces()
    {
        var report = ChartFlatnessReport.Classify(Array.Empty<double>(), 1.5);
        Assert(report.TotalFaceCount == 0, "empty list -> 0 faces");
        Assert(report.AboveThresholdCount == 0, "empty list -> 0 above");
        Assert(report.WorstFaceIndex == -1, "empty list -> WorstFaceIndex = -1");
    }

    public static void Classify_AllInsideThreshold_NoneFlagged()
    {
        var report = ChartFlatnessReport.Classify(new[] { 1.0, 1.1, 0.95 }, 1.5);
        Assert(report.TotalFaceCount == 3, "expected 3 faces");
        Assert(report.AboveThresholdCount == 0, "no face should exceed threshold");
        Assert(report.AboveThresholdRatio == 0.0, "ratio should be 0");
    }

    public static void Classify_OneAboveThreshold_FlaggedCorrectly()
    {
        // Threshold 1.5 means "max(r, 1/r) <= 1.5". 2.0 -> normalised 2.0 -> above.
        var report = ChartFlatnessReport.Classify(new[] { 1.0, 2.0, 0.9 }, 1.5);
        Assert(report.AboveThresholdCount == 1, $"expected 1 above, got {report.AboveThresholdCount}");
        Assert(report.PerFaceFlags[1].IsAboveThreshold, "face 1 (ratio 2.0) should be above");
        Assert(!report.PerFaceFlags[0].IsAboveThreshold, "face 0 (ratio 1.0) should not be above");
        Assert(!report.PerFaceFlags[2].IsAboveThreshold, "face 2 (ratio 0.9) should not be above");
    }

    public static void Classify_LowRatio_TreatedAsDistortedToo()
    {
        // r = 0.4 -> normalised = 1/0.4 = 2.5 -> above 1.5.
        var report = ChartFlatnessReport.Classify(new[] { 0.4, 1.0 }, 1.5);
        Assert(report.AboveThresholdCount == 1, "0.4 should be flagged (1/0.4 = 2.5 > 1.5)");
        Assert(report.PerFaceFlags[0].IsAboveThreshold, "face 0 (ratio 0.4) should be above");
    }

    public static void Classify_WorstFace_IsHighestNormalisedDistortion()
    {
        var report = ChartFlatnessReport.Classify(new[] { 1.0, 1.5, 2.5, 1.2 }, 2.0);
        Assert(report.WorstFaceIndex == 2, $"face 2 (ratio 2.5) is worst, got index {report.WorstFaceIndex}");
        Assert(Math.Abs(report.WorstAreaRatio - 2.5) < 1e-9,
            $"worst area ratio should be 2.5, got {report.WorstAreaRatio}");
    }

    public static void Classify_ZeroRatio_TreatedAsInfinitelyDistorted()
    {
        var report = ChartFlatnessReport.Classify(new[] { 0.0, 1.0 }, 100.0);
        Assert(report.AboveThresholdCount == 1, "ratio = 0 should be above any finite threshold");
        Assert(report.PerFaceFlags[0].IsAboveThreshold, "ratio = 0 face should be above");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
