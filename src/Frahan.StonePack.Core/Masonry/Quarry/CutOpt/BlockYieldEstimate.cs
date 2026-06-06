#nullable disable
using System;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// BlockYieldEstimate -- per-block result of running BlockCutOpt as a sub-
// routine on one BenchBlock.
//
// Carries the BlockCutOpt recovery%, the count of non-intersected dimension
// blocks, the fracture risk in [0, 1], and a coarse cutting-time estimate
// derived from the AABB footprint and the Shao 2022 feed-speed default.
// =============================================================================

public sealed class BlockYieldEstimate
{
    public BlockYieldEstimate(
        string blockId,
        int nonIntersectedCount,
        double recoveryPercent,
        double fractureRisk,
        double estimatedCuttingTimeMin,
        double recoverableVolume,
        double wasteVolume,
        double bestPsiDeg)
    {
        if (string.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("blockId required", nameof(blockId));
        if (nonIntersectedCount < 0) throw new ArgumentOutOfRangeException(nameof(nonIntersectedCount));
        if (recoveryPercent < 0) throw new ArgumentOutOfRangeException(nameof(recoveryPercent));
        if (fractureRisk < 0 || fractureRisk > 1) throw new ArgumentOutOfRangeException(nameof(fractureRisk), "0..1");

        BlockId = blockId;
        NonIntersectedCount = nonIntersectedCount;
        RecoveryPercent = recoveryPercent;
        FractureRisk = fractureRisk;
        EstimatedCuttingTimeMin = estimatedCuttingTimeMin;
        RecoverableVolume = recoverableVolume;
        WasteVolume = wasteVolume;
        BestPsiDeg = bestPsiDeg;
    }

    public string BlockId { get; }
    public int NonIntersectedCount { get; }
    public double RecoveryPercent { get; }
    public double FractureRisk { get; }
    public double EstimatedCuttingTimeMin { get; }
    public double RecoverableVolume { get; }
    public double WasteVolume { get; }
    public double BestPsiDeg { get; }

    public double YieldFraction => RecoveryPercent / 100.0;

    public override string ToString() =>
        $"Yield({BlockId}: R={RecoveryPercent:0.0}%, N_ni={NonIntersectedCount}, " +
        $"risk={FractureRisk:0.00}, t={EstimatedCuttingTimeMin:0.0} min)";
}
