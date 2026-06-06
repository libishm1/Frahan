#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// CascadeResult / CascadeTier -- output of RecoveryCascade.Run.
//
// One CascadeTier per scale records the marketable blocks recovered at that
// scale (count, volume, RMV value, cutting-surface area for BCSdbBV, kerf
// volume). CascadeResult also tracks the cracked blocks that were routed to a
// finer scale and the residual waste (cracked blocks below the smallest
// marketable size). The cascade-total recovered volume / value is the headline:
// material the single-scale packer would have thrown away that is recovered by
// re-cutting around the fracture at a finer scale.
// =============================================================================

/// <summary>Recovery at one scale of the cascade. Mutable accumulator.</summary>
public sealed class CascadeTier
{
    public CascadeTier(int scale, string label) { Scale = scale; Label = label; }

    public int Scale { get; }
    public string Label { get; }
    public int RecoveredCount { get; set; }
    public double RecoveredVolumeM3 { get; set; }
    public double RecoveredValue { get; set; }
    /// <summary>BCSdbBV (Jalalian I11) numerator: sawn surface area of recovered blocks (m^2).</summary>
    public double CutSurfaceAreaM2 { get; set; }
    /// <summary>Kerf volume consumed making this tier's blocks (m^3).</summary>
    public double KerfVolumeM3 { get; set; }
}

/// <summary>Full multi-scale cascade result. Immutable snapshot.</summary>
public sealed class CascadeResult
{
    public CascadeResult(
        IReadOnlyList<CascadeTier> tiers,
        int crackedRoutedCount,
        int residualCount,
        double residualVolumeM3,
        double testedVolumeM3)
    {
        Tiers = tiers ?? throw new ArgumentNullException(nameof(tiers));
        CrackedRoutedCount = crackedRoutedCount;
        ResidualCount = residualCount;
        ResidualVolumeM3 = residualVolumeM3;
        TestedVolumeM3 = testedVolumeM3;
    }

    public IReadOnlyList<CascadeTier> Tiers { get; }
    /// <summary>Cracked blocks fed to a finer scale (sum over the recursion).</summary>
    public int CrackedRoutedCount { get; }
    /// <summary>Cracked blocks scrapped (below the finest marketable size).</summary>
    public int ResidualCount { get; }
    public double ResidualVolumeM3 { get; }
    public double TestedVolumeM3 { get; }

    public int TotalRecoveredCount
    { get { int n = 0; foreach (var t in Tiers) n += t.RecoveredCount; return n; } }

    public double TotalRecoveredVolumeM3
    { get { double v = 0; foreach (var t in Tiers) v += t.RecoveredVolumeM3; return v; } }

    public double TotalRecoveredValue
    { get { double v = 0; foreach (var t in Tiers) v += t.RecoveredValue; return v; } }

    public double TotalCutSurfaceAreaM2
    { get { double a = 0; foreach (var t in Tiers) a += t.CutSurfaceAreaM2; return a; } }

    public double TotalKerfVolumeM3
    { get { double k = 0; foreach (var t in Tiers) k += t.KerfVolumeM3; return k; } }

    /// <summary>Cascade-total recovered volume / tested volume (0..1).</summary>
    public double RecoveryFraction => TestedVolumeM3 > 1e-12 ? TotalRecoveredVolumeM3 / TestedVolumeM3 : 0.0;

    /// <summary>BCSdbBV / Jalalian I11: cutting-surface area per unit recovered value.</summary>
    public double Bcsdbbv => TotalRecoveredValue > 1e-12 ? TotalCutSurfaceAreaM2 / TotalRecoveredValue : 0.0;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RecoveryCascade: tested {TestedVolumeM3:0.##} m^3, recovered {TotalRecoveredVolumeM3:0.##} m^3 " +
                      $"({100.0 * RecoveryFraction:0.#}%), residual {ResidualVolumeM3:0.##} m^3, cracked-routed {CrackedRoutedCount}");
        foreach (var t in Tiers)
            sb.AppendLine($"  tier {t.Scale} ({t.Label}): {t.RecoveredCount} blk, {t.RecoveredVolumeM3:0.##} m^3, " +
                          $"value {t.RecoveredValue:0.##}, cut-surf {t.CutSurfaceAreaM2:0.#} m^2, kerf {t.KerfVolumeM3:0.##} m^3");
        sb.Append($"  BCSdbBV (I11) {Bcsdbbv:0.###} m^2/value, total kerf {TotalKerfVolumeM3:0.##} m^3");
        return sb.ToString();
    }
}
