using System;
using System.Collections.Generic;

namespace Frahan.Core;

/// <summary>
/// Composite report aggregating the outputs of one packing plan: the per-
/// run packing metrics, the residual void list, and (optionally) the
/// fragment-edge match scores. Pure-data DTO.
///
/// Spec 5 section 5; spec 7 section 5 (proposed "Frahan Packing Report"
/// and "Frahan Masonry Report" GH components).
/// </summary>
public sealed class PackingPlanReport
{
    public PackingPlanReport(
        PackingMetricsReport packingMetrics,
        IReadOnlyList<ResidualVoid> residualVoids,
        double totalResidualVoidArea,
        IReadOnlyList<double> bestEdgeMatchScores,
        double averageBestEdgeMatchScore)
    {
        PackingMetrics = packingMetrics ?? throw new ArgumentNullException(nameof(packingMetrics));
        ResidualVoids = residualVoids ?? Array.Empty<ResidualVoid>();
        TotalResidualVoidArea = totalResidualVoidArea;
        BestEdgeMatchScores = bestEdgeMatchScores ?? Array.Empty<double>();
        AverageBestEdgeMatchScore = averageBestEdgeMatchScore;
    }

    public PackingMetricsReport PackingMetrics { get; }
    public IReadOnlyList<ResidualVoid> ResidualVoids { get; }
    public double TotalResidualVoidArea { get; }
    public IReadOnlyList<double> BestEdgeMatchScores { get; }
    public double AverageBestEdgeMatchScore { get; }

    public override string ToString() =>
        $"PackingPlanReport(packed={PackingMetrics.PlacementCount}, " +
        $"fill={PackingMetrics.FillRatio:P1}, voids={ResidualVoids.Count} " +
        $"({TotalResidualVoidArea:0.##} area), edges={BestEdgeMatchScores.Count} " +
        $"(avg score {AverageBestEdgeMatchScore:0.###}))";
}

/// <summary>
/// Pure-managed builder for <see cref="PackingPlanReport"/>. Accepts already-
/// computed pieces (PackingMetricsReport, residual voids, edge match scores)
/// and produces the composite. The pieces are decoupled so callers can build
/// any subset and pass <c>null</c> / empty for the rest.
/// </summary>
public static class PackingPlanReportBuilder
{
    public static PackingPlanReport Build(
        PackingMetricsReport packingMetrics,
        IReadOnlyList<ResidualVoid> residualVoids,
        IReadOnlyList<IReadOnlyList<double>> perFragmentEdgeMatchScores)
    {
        if (packingMetrics == null) throw new ArgumentNullException(nameof(packingMetrics));

        double totalVoidArea = 0.0;
        if (residualVoids != null)
            for (int i = 0; i < residualVoids.Count; i++)
                if (residualVoids[i] != null)
                    totalVoidArea += residualVoids[i].ApproximateArea;

        // Flatten per-fragment-per-edge top scores into a single list.
        var bestScores = new List<double>();
        if (perFragmentEdgeMatchScores != null)
        {
            for (int fi = 0; fi < perFragmentEdgeMatchScores.Count; fi++)
            {
                var perEdge = perFragmentEdgeMatchScores[fi];
                if (perEdge == null) continue;
                for (int ei = 0; ei < perEdge.Count; ei++)
                    bestScores.Add(perEdge[ei]);
            }
        }

        double avgScore = 0.0;
        if (bestScores.Count > 0)
        {
            double sum = 0.0;
            for (int i = 0; i < bestScores.Count; i++) sum += bestScores[i];
            avgScore = sum / bestScores.Count;
        }

        return new PackingPlanReport(
            packingMetrics: packingMetrics,
            residualVoids: residualVoids ?? Array.Empty<ResidualVoid>(),
            totalResidualVoidArea: totalVoidArea,
            bestEdgeMatchScores: bestScores,
            averageBestEdgeMatchScore: avgScore);
    }
}
