using System;
using System.Collections.Generic;

namespace Frahan.Core;

/// <summary>
/// One row of summary metrics for a 3D packing run. Pure-data DTO returned by
/// <see cref="PackingMetrics"/>.
/// </summary>
public sealed class PackingMetricsReport
{
    public PackingMetricsReport(
        int placementCount,
        int failureCount,
        double failureRatio,
        double packedVolume,
        double containerVolume,
        double fillRatio,
        double averagePlacementScore,
        double maxItemHeight,
        double maxItemVolume,
        double minItemVolume,
        double averageItemVolume,
        IReadOnlyDictionary<string, int> failureReasonCounts)
    {
        PlacementCount = placementCount;
        FailureCount = failureCount;
        FailureRatio = failureRatio;
        PackedVolume = packedVolume;
        ContainerVolume = containerVolume;
        FillRatio = fillRatio;
        AveragePlacementScore = averagePlacementScore;
        MaxItemHeight = maxItemHeight;
        MaxItemVolume = maxItemVolume;
        MinItemVolume = minItemVolume;
        AverageItemVolume = averageItemVolume;
        FailureReasonCounts = failureReasonCounts ?? new Dictionary<string, int>();
    }

    public int PlacementCount { get; }
    public int FailureCount { get; }
    public double FailureRatio { get; }
    public double PackedVolume { get; }
    public double ContainerVolume { get; }
    public double FillRatio { get; }
    public double AveragePlacementScore { get; }
    public double MaxItemHeight { get; }
    public double MaxItemVolume { get; }
    public double MinItemVolume { get; }
    public double AverageItemVolume { get; }
    public IReadOnlyDictionary<string, int> FailureReasonCounts { get; }

    public override string ToString() =>
        $"PackingMetrics(placed={PlacementCount}, failed={FailureCount} " +
        $"({FailureRatio:P1}), fill={FillRatio:P1}, packed={PackedVolume:0.##}, " +
        $"container={ContainerVolume:0.##}, avgScore={AveragePlacementScore:0.###})";
}

/// <summary>
/// Pure-managed metric helpers for <see cref="PackResult"/>. Spec 5 section 5
/// + spec 7 section 5 list these as the canonical "packing report" surface
/// the GH "Frahan Packing Report" component (proposed) should expose.
///
/// All methods are static, side-effect-free, and operate on existing DTOs;
/// no allocation in the hot path beyond the returned report dictionary.
/// </summary>
public static class PackingMetrics
{
    /// <summary>
    /// Compute a complete metrics report for a 3D PackResult.
    /// </summary>
    public static PackingMetricsReport Compute(PackResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        int placementCount = result.Placements.Count;
        int failureCount = result.Failures.Count;
        int total = placementCount + failureCount;
        double failureRatio = total <= 0 ? 0.0 : (double)failureCount / total;

        double containerVolume = result.Container?.Volume ?? 0.0;
        double packedVolume = result.PackedVolume;
        double fillRatio = result.FillRatio;

        double avgScore = 0.0;
        if (placementCount > 0)
        {
            double sum = 0.0;
            for (int i = 0; i < placementCount; i++)
                sum += result.Placements[i].Score;
            avgScore = sum / placementCount;
        }

        double maxHeight = 0.0;
        double maxItemVol = 0.0;
        double minItemVol = placementCount > 0 ? double.MaxValue : 0.0;
        double sumItemVol = 0.0;
        for (int i = 0; i < placementCount; i++)
        {
            var p = result.Placements[i];
            double topZ = p.Box.Min.Z + p.Box.Size.Height;
            if (topZ > maxHeight) maxHeight = topZ;

            double v = p.Item.Volume;
            if (v > maxItemVol) maxItemVol = v;
            if (v < minItemVol) minItemVol = v;
            sumItemVol += v;
        }
        if (placementCount == 0) minItemVol = 0.0;
        double avgItemVol = placementCount == 0 ? 0.0 : sumItemVol / placementCount;

        var reasons = new Dictionary<string, int>();
        for (int i = 0; i < failureCount; i++)
        {
            var key = result.Failures[i].Reason ?? "(no reason)";
            reasons.TryGetValue(key, out int prior);
            reasons[key] = prior + 1;
        }

        return new PackingMetricsReport(
            placementCount,
            failureCount,
            failureRatio,
            packedVolume,
            containerVolume,
            fillRatio,
            avgScore,
            maxHeight,
            maxItemVol,
            minItemVol,
            avgItemVol,
            reasons);
    }
}
