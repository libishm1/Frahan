#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptResult -- output of one BlockCutOpt run.
//
// Recovery uses the kerf-aware form from Elkarmoty thesis Eq. 7-1:
//
//     R = N_ni * V_B / (V_tested - V_kerf) * 100
//
// where V_kerf is the volume occupied by saw kerf inside the cutting grid
// (approximated here by the missing volume between adjacent cells).
//
// Phase 1 returns the global optimum (single Pareto-degenerate point).
// =============================================================================

public sealed class BlockCutOptResult
{
    /// <summary>Phase 1 compat: theta and phi default to 0 (psi-only result).</summary>
    public BlockCutOptResult(
        int nonIntersectedCount,
        double recoveryPercent,
        double bestPsiRad,
        double bestDx,
        double bestDy,
        long totalEvaluations,
        TimeSpan elapsed)
        : this(nonIntersectedCount, recoveryPercent, bestPsiRad, 0.0, 0.0, bestDx, bestDy, totalEvaluations, elapsed)
    { }

    /// <summary>I1: full 3D rotation result with theta, phi.</summary>
    public BlockCutOptResult(
        int nonIntersectedCount,
        double recoveryPercent,
        double bestPsiRad,
        double bestThetaRad,
        double bestPhiRad,
        double bestDx,
        double bestDy,
        long totalEvaluations,
        TimeSpan elapsed)
    {
        if (nonIntersectedCount < 0) throw new ArgumentOutOfRangeException(nameof(nonIntersectedCount));
        if (recoveryPercent < 0) throw new ArgumentOutOfRangeException(nameof(recoveryPercent));

        NonIntersectedCount = nonIntersectedCount;
        RecoveryPercent = recoveryPercent;
        BestPsiRad = bestPsiRad;
        BestThetaRad = bestThetaRad;
        BestPhiRad = bestPhiRad;
        BestDx = bestDx;
        BestDy = bestDy;
        TotalEvaluations = totalEvaluations;
        Elapsed = elapsed;
    }

    public int NonIntersectedCount { get; }
    public double RecoveryPercent { get; }
    public double BestPsiRad { get; }
    public double BestThetaRad { get; }
    public double BestPhiRad { get; }
    public double BestDx { get; }
    public double BestDy { get; }
    public long TotalEvaluations { get; }
    public TimeSpan Elapsed { get; }

    public double BestPsiDeg => BestPsiRad * 180.0 / Math.PI;
    public double BestThetaDeg => BestThetaRad * 180.0 / Math.PI;
    public double BestPhiDeg => BestPhiRad * 180.0 / Math.PI;

    public override string ToString() =>
        $"BlockCutOptResult(N_ni={NonIntersectedCount}, R={RecoveryPercent:0.00}%, " +
        $"psi={BestPsiDeg:0.0}, theta={BestThetaDeg:0.0}, phi={BestPhiDeg:0.0} deg, " +
        $"dx={BestDx:0.00}, dy={BestDy:0.00}, " +
        $"evals={TotalEvaluations}, elapsed={Elapsed.TotalMilliseconds:0} ms)";
}
