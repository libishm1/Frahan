#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// ParetoPoint -- one (psi, dx, dy) candidate scored on four objectives:
//
//   - Recovery:   non-intersected block count       maximise
//   - Revenue:    sum of per-block RMV              maximise
//   - KerfTime:   sum of per-block saw-cut time     minimise
//   - BCSdbBV:    cutting surface area / block value  minimise   (Jalalian 2023)
//
// Phase 6 of the synthesis roadmap. I11 = BCSdbBV is the fourth axis.
// Reference: `D:\code_ws\wiki\papers\equations_and_diagrams\10_consensus_update_and_forward_plan.md`
// section 3 and `08_synthesis_and_optimum_algorithm.md` section 6.1 row I11.
// =============================================================================

public readonly struct ParetoPoint : IEquatable<ParetoPoint>
{
    public ParetoPoint(
        int recoveryCount,
        double revenue,
        double kerfTime,
        double bcsdbBv,
        double psiRad,
        double dx,
        double dy)
    {
        RecoveryCount = recoveryCount;
        Revenue = revenue;
        KerfTime = kerfTime;
        BcsdbBv = bcsdbBv;
        PsiRad = psiRad;
        Dx = dx;
        Dy = dy;
    }

    public int RecoveryCount { get; }
    public double Revenue { get; }
    public double KerfTime { get; }
    public double BcsdbBv { get; }
    public double PsiRad { get; }
    public double Dx { get; }
    public double Dy { get; }

    public double PsiDeg => PsiRad * 180.0 / Math.PI;

    /// <summary>
    /// True iff this point dominates <paramref name="other"/> in the Pareto sense:
    /// strictly better on at least one axis and no worse on any axis. Recovery
    /// and Revenue are maximised; KerfTime and BcsdbBv are minimised.
    /// </summary>
    public bool Dominates(in ParetoPoint other)
    {
        bool strictlyBetter = false;

        if (RecoveryCount < other.RecoveryCount) return false;
        if (RecoveryCount > other.RecoveryCount) strictlyBetter = true;

        if (Revenue < other.Revenue) return false;
        if (Revenue > other.Revenue) strictlyBetter = true;

        if (KerfTime > other.KerfTime) return false;
        if (KerfTime < other.KerfTime) strictlyBetter = true;

        if (BcsdbBv > other.BcsdbBv) return false;
        if (BcsdbBv < other.BcsdbBv) strictlyBetter = true;

        return strictlyBetter;
    }

    public bool Equals(ParetoPoint other) =>
        RecoveryCount == other.RecoveryCount
        && Revenue == other.Revenue
        && KerfTime == other.KerfTime
        && BcsdbBv == other.BcsdbBv
        && PsiRad == other.PsiRad
        && Dx == other.Dx
        && Dy == other.Dy;

    public override bool Equals(object obj) => obj is ParetoPoint p && Equals(p);

    public override int GetHashCode() =>
        // hand-rolled stable hash; HashCode.Combine is not available on net48
        unchecked(
            ((RecoveryCount * 397) ^ Revenue.GetHashCode()) * 397
            ^ KerfTime.GetHashCode());

    public override string ToString() =>
        $"ParetoPoint(R={RecoveryCount}, Pi={Revenue:0.000}, " +
        $"tau={KerfTime:0.000}, BCSdbBV={BcsdbBv:0.000}, " +
        $"psi={PsiDeg:0.0} deg, dx={Dx:0.00}, dy={Dy:0.00})";
}
