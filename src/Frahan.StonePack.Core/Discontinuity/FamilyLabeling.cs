#nullable disable
using System;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// FamilyLabeling -- the joint-family taxonomy + kriging dispatch (spec G12 S4).
// sheet (dip <= 30), inclined (30-60), and the two vertical families (dip > 60),
// split transverse/longitudinal by the acute angle of the family strike to the
// dyke trend (< 45 deg -> longitudinal, strike parallel to the dyke). Each label
// carries the downstream kriging route (Y's per-family dispatch / G11 implicit).
//
// Port of pyfrahan.cluster.label_family + _DISPATCH. Pure managed, Rhino-free.
// =============================================================================

public enum FamilyLabel
{
    Sheet,
    Inclined,
    VerticalTransverse,
    VerticalLongitudinal
}

public static class FamilyLabeling
{
    /// <summary>Snake-case label string matching the Python labels (for parity / reports).</summary>
    public static string ToKey(FamilyLabel lab)
    {
        switch (lab)
        {
            case FamilyLabel.Sheet: return "sheet";
            case FamilyLabel.Inclined: return "inclined";
            case FamilyLabel.VerticalTransverse: return "vertical-transverse";
            case FamilyLabel.VerticalLongitudinal: return "vertical-longitudinal";
            default: throw new ArgumentOutOfRangeException(nameof(lab));
        }
    }

    /// <summary>Kriging route for a label, matching pyfrahan.cluster._DISPATCH.</summary>
    public static string Dispatch(FamilyLabel lab)
    {
        switch (lab)
        {
            case FamilyLabel.Sheet: return "regression_kriging_d(x,y) [KEEP]";
            case FamilyLabel.Inclined: return "set-aligned rotation kriging";
            case FamilyLabel.VerticalTransverse: return "implicit / potential-field (G11)";
            case FamilyLabel.VerticalLongitudinal: return "implicit / potential-field (G11)";
            default: throw new ArgumentOutOfRangeException(nameof(lab));
        }
    }

    /// <summary>
    /// Taxonomy label for a family given its mean-pole dip / dip-direction (deg)
    /// and the dyke trend (deg). Port of label_family.
    /// </summary>
    public static FamilyLabel Label(double dipDeg, double dipDirDeg, double dykeTrendDeg = 0.0)
    {
        if (dipDeg <= 30.0) return FamilyLabel.Sheet;
        if (dipDeg <= 60.0) return FamilyLabel.Inclined;
        double strike = Mod(dipDirDeg - 90.0, 360.0);
        double d = Math.Abs(Mod((strike - dykeTrendDeg) + 90.0, 180.0) - 90.0);  // acute 0..90
        return d < 45.0 ? FamilyLabel.VerticalLongitudinal : FamilyLabel.VerticalTransverse;
    }

    // Python-style floored modulo (result has the sign of the divisor, i.e. >= 0
    // for a positive n), so the strike/dyke split matches label_family exactly.
    private static double Mod(double a, double n) => ((a % n) + n) % n;
}
