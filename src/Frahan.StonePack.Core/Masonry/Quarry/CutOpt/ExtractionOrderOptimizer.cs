#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// ExtractionOrderOptimizer -- v1 greedy. Spec section 6.
//
// score(b) = w_yield * yieldFraction
//          - w_risk  * fractureRisk
//          - w_access * accessCostNormalised
//
// Blocks with yieldFraction < MinYieldFraction are diverted to Skipped (spec
// section 8). Stable sort: ties broken by BenchBlock.Id ordinal.
// =============================================================================

public sealed class ExtractionOrderOptions
{
    public ExtractionOrderOptions(
        double yieldWeight = 1.0,
        double riskWeight = 1.0,
        double accessWeight = 0.0,
        double minYieldFraction = 0.10,
        double accessCostNormaliser = 1.0)
    {
        if (yieldWeight < 0) throw new ArgumentOutOfRangeException(nameof(yieldWeight));
        if (riskWeight < 0) throw new ArgumentOutOfRangeException(nameof(riskWeight));
        if (accessWeight < 0) throw new ArgumentOutOfRangeException(nameof(accessWeight));
        if (minYieldFraction < 0 || minYieldFraction > 1) throw new ArgumentOutOfRangeException(nameof(minYieldFraction), "0..1");
        if (accessCostNormaliser <= 0) throw new ArgumentOutOfRangeException(nameof(accessCostNormaliser), "> 0");

        YieldWeight = yieldWeight;
        RiskWeight = riskWeight;
        AccessWeight = accessWeight;
        MinYieldFraction = minYieldFraction;
        AccessCostNormaliser = accessCostNormaliser;
    }

    public double YieldWeight { get; }
    public double RiskWeight { get; }
    public double AccessWeight { get; }
    public double MinYieldFraction { get; }
    public double AccessCostNormaliser { get; }
}

public static class ExtractionOrderOptimizer
{
    public static ExtractionPlan Plan(
        QuarryInventory inventory,
        IReadOnlyList<BlockYieldEstimate> estimates,
        ExtractionOrderOptions options = null)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        if (estimates == null) throw new ArgumentNullException(nameof(estimates));
        if (estimates.Count != inventory.Count)
            throw new ArgumentException(
                $"estimates.Count={estimates.Count} but inventory.Count={inventory.Count}");
        options = options ?? new ExtractionOrderOptions();

        var byId = new Dictionary<string, BlockYieldEstimate>(estimates.Count, StringComparer.Ordinal);
        for (int i = 0; i < estimates.Count; i++)
        {
            if (!byId.ContainsKey(estimates[i].BlockId))
                byId.Add(estimates[i].BlockId, estimates[i]);
        }

        var accepted = new List<(BenchBlock block, BlockYieldEstimate est, double score)>();
        var skipped = new List<(BenchBlock block, BlockYieldEstimate est, double score)>();
        foreach (var b in inventory.Blocks)
        {
            if (!byId.TryGetValue(b.Id, out var e))
                throw new InvalidOperationException($"no estimate for block '{b.Id}'");

            double accessTerm = options.AccessWeight * (b.AccessCost / options.AccessCostNormaliser);
            double score = options.YieldWeight * e.YieldFraction
                         - options.RiskWeight * e.FractureRisk
                         - accessTerm;

            if (e.YieldFraction < options.MinYieldFraction)
            {
                skipped.Add((b, e, score));
            }
            else
            {
                accepted.Add((b, e, score));
            }
        }

        accepted.Sort((x, y) =>
        {
            int c = y.score.CompareTo(x.score);
            return c != 0 ? c : string.CompareOrdinal(x.block.Id, y.block.Id);
        });
        skipped.Sort((x, y) => string.CompareOrdinal(x.block.Id, y.block.Id));

        var acceptedEntries = new List<ExtractionPlanEntry>(accepted.Count);
        for (int i = 0; i < accepted.Count; i++)
        {
            var a = accepted[i];
            acceptedEntries.Add(new ExtractionPlanEntry(i, a.block, a.est, a.score));
        }
        var skippedEntries = new List<ExtractionPlanEntry>(skipped.Count);
        for (int i = 0; i < skipped.Count; i++)
        {
            var s = skipped[i];
            skippedEntries.Add(new ExtractionPlanEntry(i, s.block, s.est, s.score));
        }

        return new ExtractionPlan(inventory.BenchId, acceptedEntries, skippedEntries);
    }
}
