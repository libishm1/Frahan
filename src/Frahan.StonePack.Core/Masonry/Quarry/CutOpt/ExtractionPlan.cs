#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// ExtractionPlan -- ordered list of (BenchBlock, BlockYieldEstimate) pairs
// plus the set of skipped (low-yield) blocks. Output of ExtractionOrderOptimizer.
// =============================================================================

public sealed class ExtractionPlanEntry
{
    public ExtractionPlanEntry(int order, BenchBlock block, BlockYieldEstimate estimate, double score)
    {
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        Order = order;
        Block = block ?? throw new ArgumentNullException(nameof(block));
        Estimate = estimate ?? throw new ArgumentNullException(nameof(estimate));
        Score = score;
    }

    public int Order { get; }
    public BenchBlock Block { get; }
    public BlockYieldEstimate Estimate { get; }
    public double Score { get; }

    public override string ToString() =>
        $"#{Order} {Block.Id}: score={Score:0.000}, R={Estimate.RecoveryPercent:0.0}%, risk={Estimate.FractureRisk:0.00}";
}

public sealed class ExtractionPlan
{
    public ExtractionPlan(
        string benchId,
        IReadOnlyList<ExtractionPlanEntry> accepted,
        IReadOnlyList<ExtractionPlanEntry> skipped)
    {
        if (string.IsNullOrWhiteSpace(benchId)) throw new ArgumentException("benchId required", nameof(benchId));
        BenchId = benchId;
        Accepted = accepted ?? throw new ArgumentNullException(nameof(accepted));
        Skipped = skipped ?? throw new ArgumentNullException(nameof(skipped));
    }

    public string BenchId { get; }
    public IReadOnlyList<ExtractionPlanEntry> Accepted { get; }
    public IReadOnlyList<ExtractionPlanEntry> Skipped { get; }

    public double TotalRecoverableVolume => Accepted.Sum(e => e.Estimate.RecoverableVolume);
    public double TotalWasteVolume => Accepted.Sum(e => e.Estimate.WasteVolume);
    public double TotalEstimatedCuttingTimeMin => Accepted.Sum(e => e.Estimate.EstimatedCuttingTimeMin);

    public override string ToString() =>
        $"ExtractionPlan({BenchId}, accepted={Accepted.Count}, skipped={Skipped.Count}, V_rec={TotalRecoverableVolume:0.###} m3)";
}
