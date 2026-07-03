#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// FractureIntensity -- the Dershowitz & Herda (1992) P_ij intensity / density
// family for a jointed rock mass. The reviewer-grade way to report "how
// fractured" a rock is: P32 (fracture AREA per unit rock VOLUME, units 1/m) is
// the scale-independent measure a DFN is conditioned on, unlike raw counts
// (P10) or trace densities (P21), which depend on the sampling geometry.
//
//   P10 = number of fractures per unit length of scanline        [1/m]
//   P20 = number of trace intersections per unit window area     [1/m^2]
//   P21 = trace length per unit window area                      [1/m]
//   P30 = number of fractures per unit rock volume               [1/m^3]
//   P32 = fracture area per unit rock volume                     [1/m]   <-- target
//
// Two routes to P32:
//   (a) Persistent parallel sets of normal spacing s: each set contributes
//       P32 = 1/s (unit area per s of thickness). Total P32 = sum 1/s_j. This
//       equals the Palmstrom volumetric joint count Jv for persistent joints --
//       cross-checks BlockSizeMath.Jv.
//   (b) Scanline route (Terzaghi-linked): for a single set intersected by a
//       scanline of direction t, P10 = P32 * |cos(angle(t, pole))|. Inverting,
//       P32 = P10 / |cos(angle(t, pole))|. So a measured scanline count converts
//       to volumetric intensity via the same geometry factor Terzaghi uses.
//   (c) Direct DFN route: P32 = (sum of finite fracture areas inside V) / V.
//
// References:
//   - Dershowitz, W. S. & Herda, H. H. (1992). Interpretation of fracture
//     spacing and intensity. 33rd US Rock Mech. Symp.
//   - Wang, X. (2005). Stereological interpretation of rock fracture traces...
//     (P10/P21 -> P32 conversion factors).
//   - Elmo, Rogers, Dershowitz -- DFN P32 conditioning practice.
//
// Pure managed arithmetic (Rhino value types only), headless-unit-testable.
// =============================================================================

/// <summary>Dershowitz P_ij fracture-intensity descriptors for a rock mass.</summary>
public sealed class IntensityResult
{
    /// <summary>Per-set volumetric intensity P32 = 1/spacing (1/m). Null if no spacings given.</summary>
    public double[] P32PerSet;
    /// <summary>Total volumetric fracture intensity P32 (fracture area / rock volume, 1/m).</summary>
    public double P32;
    /// <summary>Volumetric fracture count P30 (1/m^3). NaN if not derivable.</summary>
    public double P30 = double.NaN;
    /// <summary>Linear intensity P10 along the supplied scanline (1/m). NaN if no scanline.</summary>
    public double P10 = double.NaN;
    /// <summary>Areal trace intensity P21 (1/m). NaN if no window/traces.</summary>
    public double P21 = double.NaN;
    /// <summary>Spacing unit label echoed for display.</summary>
    public string SpacingUnits = "";
    public List<string> Notes = new List<string>();
    public string Report = "";
}

public static class FractureIntensity
{
    private const double D2R = Math.PI / 180.0, R2D = 180.0 / Math.PI;
    private const double Eps = 1e-9;

    /// <summary>Per-set P32 = 1/(spacing*unitScale) and their sum. Zero/negative spacings are skipped.</summary>
    public static double[] P32FromSpacings(IReadOnlyList<double> spacings, double unitScale, out double total)
    {
        total = 0;
        if (spacings == null) return new double[0];
        if (unitScale <= 0) unitScale = 1.0;
        var p = new double[spacings.Count];
        for (int i = 0; i < spacings.Count; i++)
        {
            double s = spacings[i] * unitScale;
            p[i] = s > Eps ? 1.0 / s : 0.0;
            total += p[i];
        }
        return p;
    }

    /// <summary>
    /// Expected scanline linear intensity P10 for persistent sets: sum over sets of
    /// P32_set * |cos(angle between scanline and set pole)|. This is the forward of
    /// the Terzaghi correction (a scanline sub-parallel to a set under-counts it).
    /// </summary>
    public static double P10AlongScanline(Vector3d scanlineDir, IReadOnlyList<Vector3d> poles, IReadOnlyList<double> p32PerSet)
    {
        if (poles == null || p32PerSet == null) return double.NaN;
        var t = scanlineDir; if (t.SquareLength < 1e-18) return double.NaN; t.Unitize();
        int n = Math.Min(poles.Count, p32PerSet.Count);
        double p10 = 0;
        for (int i = 0; i < n; i++)
        {
            var p = poles[i]; if (p.SquareLength < Eps) continue; p.Unitize();
            double c = Math.Abs(t.X * p.X + t.Y * p.Y + t.Z * p.Z);
            p10 += p32PerSet[i] * c;
        }
        return p10;
    }

    /// <summary>Invert a single-set scanline count to volumetric intensity: P32 = P10 / |cos(angle(t, pole))|, capped.</summary>
    public static double P32FromP10(double p10, Vector3d scanlineDir, Vector3d pole, double minAngleDeg = 15.0)
    {
        var t = scanlineDir; var p = pole;
        if (t.SquareLength < 1e-18 || p.SquareLength < 1e-18) return double.NaN;
        t.Unitize(); p.Unitize();
        double c = Math.Abs(t.X * p.X + t.Y * p.Y + t.Z * p.Z);
        double cMin = Math.Cos((90.0 - Math.Max(1e-6, minAngleDeg)) * D2R); // floor so a grazing set doesn't explode
        if (c < cMin) c = cMin;
        return c > Eps ? p10 / c : double.NaN;
    }

    /// <summary>Direct DFN route: P32 = (sum of finite fracture areas) / volume.</summary>
    public static double P32FromAreas(IReadOnlyList<double> areas, double volume)
    {
        if (areas == null || volume <= Eps) return double.NaN;
        double a = 0; foreach (var x in areas) if (x > 0) a += x;
        return a / volume;
    }

    /// <summary>Volumetric fracture count P30 = count / volume.</summary>
    public static double P30FromCount(int count, double volume)
        => volume > Eps ? count / volume : double.NaN;

    /// <summary>Areal trace intensity P21 = (sum of trace lengths) / window area.</summary>
    public static double P21FromTraces(IReadOnlyList<double> traceLengths, double windowArea)
    {
        if (traceLengths == null || windowArea <= Eps) return double.NaN;
        double l = 0; foreach (var x in traceLengths) if (x > 0) l += x;
        return l / windowArea;
    }

    /// <summary>
    /// Assemble an intensity report from per-set orientations + spacings, with an
    /// optional scanline (for expected P10) and an optional finite-fracture DFN
    /// (areas + volume, for a directly measured P32/P30 cross-check).
    /// </summary>
    public static IntensityResult Compute(
        IReadOnlyList<double> dipDeg,
        IReadOnlyList<double> dipDirDeg,
        IReadOnlyList<double> spacings,
        double unitScale = 1.0,
        bool hasScanline = false,
        Vector3d scanlineDir = default,
        IReadOnlyList<double> dfnAreas = null,
        double dfnVolume = 0.0,
        int dfnCount = 0,
        string spacingUnits = "")
    {
        var res = new IntensityResult { SpacingUnits = spacingUnits };
        if (unitScale <= 0) unitScale = 1.0;

        double total;
        res.P32PerSet = P32FromSpacings(spacings, unitScale, out total);
        res.P32 = total;

        // poles for the scanline route
        var poles = new List<Vector3d>();
        if (dipDeg != null && dipDirDeg != null)
        {
            int n = Math.Min(dipDeg.Count, dipDirDeg.Count);
            for (int i = 0; i < n; i++) poles.Add(OrientationMath.NormalFromDipDipDir(dipDeg[i], dipDirDeg[i]));
        }

        if (hasScanline && scanlineDir.SquareLength > 1e-18 && poles.Count == res.P32PerSet.Length)
            res.P10 = P10AlongScanline(scanlineDir, poles, res.P32PerSet);

        // direct DFN cross-check
        if (dfnAreas != null && dfnVolume > Eps)
        {
            double measuredP32 = P32FromAreas(dfnAreas, dfnVolume);
            res.Notes.Add($"DFN-measured P32 = {measuredP32:F3} 1/m over V = {dfnVolume:G3} m^3 (spacing-route P32 = {res.P32:F3}).");
            if (dfnCount > 0) res.P30 = P30FromCount(dfnCount, dfnVolume);
        }

        res.Report = BuildReport(res, unitScale, hasScanline, spacingUnits);
        return res;
    }

    private static string BuildReport(IntensityResult r, double unitScale, bool hasScanline, string units)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Fracture intensity (Dershowitz-Herda 1992 P_ij).");
        string u = string.IsNullOrEmpty(units) ? "cloud" : units;
        sb.AppendLine($"  spacing units: {u}  x unitScale {unitScale:G3} -> metres");
        if (r.P32PerSet != null && r.P32PerSet.Length > 0)
        {
            sb.AppendLine("  Per-set P32 (=1/spacing, persistent, 1/m):");
            for (int i = 0; i < r.P32PerSet.Length; i++)
                sb.AppendLine($"    S{i + 1}: {r.P32PerSet[i]:F3}");
        }
        sb.AppendLine($"  Total P32 = {r.P32:F3} 1/m  (fracture area / rock volume; = Jv for persistent joints)");
        if (!double.IsNaN(r.P10))
            sb.AppendLine($"  Expected scanline P10 = {r.P10:F3} 1/m  (orientation-weighted; a scanline || a set under-counts it)");
        if (!double.IsNaN(r.P30))
            sb.AppendLine($"  DFN P30 = {r.P30:F3} 1/m^3");
        foreach (var note in r.Notes) sb.AppendLine("  ! " + note);
        sb.AppendLine("  P32 is scale-independent; P10/P21 are sampling-geometry dependent -- report P32 for DFN conditioning.");
        return sb.ToString().TrimEnd();
    }
}
