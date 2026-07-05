#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// TerzaghiCorrection -- orientation sampling-bias correction for discontinuity
// surveys. A scanline (or a rock-face window) preferentially intersects
// discontinuities whose planes are near-perpendicular to the sampling element;
// discontinuities sub-parallel to it are systematically under-sampled. Left
// uncorrected, joint-set proportions and mean orientations are biased -- the
// single most common defect a rock-mechanics reviewer will flag in a raw
// pole plot.
//
// Terzaghi weight for one discontinuity:  w = 1 / sin(delta)
//   delta = the acute angle between the discontinuity PLANE and the sampling
//           element (a scanline direction, or a sampling-face normal).
//   Scanline: delta = 90 - angle(scanline, pole)   (grazing when plane || line).
//   Window  : delta = angle(pole, face normal)      (grazing when plane || face).
// As delta -> 0 the weight diverges, so it is capped by a minimum bias angle
// (the "blind zone" half-angle, default 15 deg -> w_max ~= 3.86); features
// inside the blind zone cannot be reliably corrected and are flagged.
//
// References:
//   - Terzaghi, R. D. (1965). Sources of error in joint surveys. Geotechnique
//     15(3), 287-304.  (the original 1/sin(delta) weighting)
//   - Priest, S. D. (1993). Discontinuity Analysis for Rock Engineering. Ch.5.
//   - Wathelet et al.; Park & West -- capped-weight practice (min angle cap).
//
// Pure managed arithmetic (Rhino value types only), headless-unit-testable.
// =============================================================================

/// <summary>Result of a Terzaghi orientation-bias correction over a discontinuity population.</summary>
public sealed class TerzaghiResult
{
    /// <summary>Per-discontinuity Terzaghi weight (>= 1), capped at <see cref="MaxWeight"/>.</summary>
    public double[] Weights;
    /// <summary>Per-discontinuity bias angle delta (deg): angle between the plane and the sampler.</summary>
    public double[] BiasAngleDeg;
    /// <summary>Blind-zone half-angle (deg): below this delta the weight is capped.</summary>
    public double BlindZoneDeg;
    /// <summary>Maximum weight applied = 1 / sin(BlindZoneDeg).</summary>
    public double MaxWeight;
    /// <summary>How many discontinuities fell inside the blind zone (weight capped).</summary>
    public int Clamped;

    // ---- per-set summary (populated only when set ids are supplied) ----
    /// <summary>Distinct set ids (ascending, excludes unassigned -1). Null if no set ids given.</summary>
    public int[] SetIds;
    /// <summary>Raw (uncorrected) count per set.</summary>
    public int[] RawCount;
    /// <summary>Bias-corrected count per set (sum of weights).</summary>
    public double[] CorrectedCount;
    /// <summary>Raw proportion per set (0..1).</summary>
    public double[] RawFraction;
    /// <summary>Bias-corrected proportion per set (0..1).</summary>
    public double[] CorrectedFraction;

    public string Report = "";
}

public static class TerzaghiCorrection
{
    private const double D2R = Math.PI / 180.0, R2D = 180.0 / Math.PI;

    /// <summary>Acute angle (deg) between the plane (pole <paramref name="pole"/>) and a scanline direction.</summary>
    public static double ScanlineBiasAngleDeg(Vector3d pole, Vector3d scanlineDir)
        => 90.0 - AcuteDeg(pole, scanlineDir);

    /// <summary>Acute angle (deg) between the plane (pole <paramref name="pole"/>) and a sampling face (normal <paramref name="faceNormal"/>).</summary>
    public static double WindowBiasAngleDeg(Vector3d pole, Vector3d faceNormal)
        => AcuteDeg(pole, faceNormal);

    /// <summary>Terzaghi weight 1/sin(delta) for a bias angle, capped at the minimum bias angle.</summary>
    public static double Weight(double biasAngleDeg, double minBiasAngleDeg)
    {
        double d = Math.Max(Math.Abs(biasAngleDeg), Math.Max(1e-6, minBiasAngleDeg));
        double s = Math.Sin(d * D2R);
        return s < 1e-9 ? 1e9 : 1.0 / s;
    }

    /// <summary>
    /// Correct a discontinuity population for orientation sampling bias.
    /// <paramref name="sampling"/> is the scanline direction (window=false) or the
    /// sampling-face normal (window=true). Poles need not be unit vectors.
    /// If <paramref name="setIds"/> is supplied (same length, -1 = unassigned),
    /// raw vs corrected set proportions are also computed.
    /// </summary>
    public static TerzaghiResult Correct(
        IReadOnlyList<Vector3d> poles,
        Vector3d sampling,
        bool windowMode,
        double minBiasAngleDeg = 15.0,
        IReadOnlyList<int> setIds = null)
    {
        if (poles == null) throw new ArgumentNullException(nameof(poles));
        var s = sampling;
        if (s.SquareLength < 1e-18) s = Vector3d.ZAxis;
        s.Unitize();
        if (minBiasAngleDeg <= 0 || minBiasAngleDeg >= 90) minBiasAngleDeg = 15.0;

        int n = poles.Count;
        var res = new TerzaghiResult
        {
            Weights = new double[n],
            BiasAngleDeg = new double[n],
            BlindZoneDeg = minBiasAngleDeg,
            MaxWeight = 1.0 / Math.Sin(minBiasAngleDeg * D2R)
        };

        for (int i = 0; i < n; i++)
        {
            double delta = windowMode ? WindowBiasAngleDeg(poles[i], s)
                                      : ScanlineBiasAngleDeg(poles[i], s);
            res.BiasAngleDeg[i] = delta;
            res.Weights[i] = Weight(delta, minBiasAngleDeg);
            if (Math.Abs(delta) <= minBiasAngleDeg + 1e-9) res.Clamped++;
        }

        if (setIds != null && setIds.Count == n)
        {
            var ids = new List<int>();
            foreach (var id in setIds) if (id >= 0 && !ids.Contains(id)) ids.Add(id);
            ids.Sort();
            int m = ids.Count;
            res.SetIds = ids.ToArray();
            res.RawCount = new int[m];
            res.CorrectedCount = new double[m];
            res.RawFraction = new double[m];
            res.CorrectedFraction = new double[m];
            double totRaw = 0, totCor = 0;
            for (int i = 0; i < n; i++)
            {
                int k = ids.IndexOf(setIds[i]);
                if (k < 0) continue;
                res.RawCount[k]++; totRaw++;
                res.CorrectedCount[k] += res.Weights[i]; totCor += res.Weights[i];
            }
            for (int k = 0; k < m; k++)
            {
                res.RawFraction[k] = totRaw > 0 ? res.RawCount[k] / totRaw : 0;
                res.CorrectedFraction[k] = totCor > 0 ? res.CorrectedCount[k] / totCor : 0;
            }
        }

        res.Report = BuildReport(res, windowMode, minBiasAngleDeg, n);
        return res;
    }

    /// <summary>Bias-corrected mean pole of a discontinuity population (weighted axial mean, lower hemisphere).</summary>
    public static Vector3d CorrectedMeanPole(IReadOnlyList<Vector3d> poles, IReadOnlyList<double> weights)
    {
        if (poles == null || weights == null || poles.Count == 0) return Vector3d.Zero;
        // seed with the largest-weight pole for a consistent axial sign
        int seed = 0; for (int i = 1; i < weights.Count && i < poles.Count; i++) if (weights[i] > weights[seed]) seed = i;
        var s0 = OrientationMath.LowerHemisphere(poles[seed]);
        double sx = 0, sy = 0, sz = 0;
        int n = Math.Min(poles.Count, weights.Count);
        for (int i = 0; i < n; i++)
        {
            var p = OrientationMath.LowerHemisphere(poles[i]);
            double sgn = (p.X * s0.X + p.Y * s0.Y + p.Z * s0.Z) >= 0 ? 1.0 : -1.0;
            sx += weights[i] * sgn * p.X; sy += weights[i] * sgn * p.Y; sz += weights[i] * sgn * p.Z;
        }
        var mean = new Vector3d(sx, sy, sz);
        if (mean.SquareLength < 1e-18) return Vector3d.Zero;
        return OrientationMath.LowerHemisphere(mean);
    }

    private static double AcuteDeg(Vector3d a, Vector3d b)
    {
        var x = a; var y = b; x.Unitize(); y.Unitize();
        double d = Math.Abs(x.X * y.X + x.Y * y.Y + x.Z * y.Z);
        return Math.Acos(Math.Min(1.0, d)) * R2D;
    }

    private static string BuildReport(TerzaghiResult r, bool windowMode, double minAngle, int n)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Terzaghi orientation-bias correction (Terzaghi 1965).");
        sb.AppendLine($"  Sampler: {(windowMode ? "window / rock-face (bias vs face normal)" : "scanline (bias vs line direction)")}");
        sb.AppendLine($"  {n} discontinuities; blind zone = {minAngle:F1} deg -> max weight {r.MaxWeight:F2}");
        sb.AppendLine($"  {r.Clamped} inside the blind zone (weight capped; orientation under-sampled -- treat as lower-confidence).");
        if (r.SetIds != null)
        {
            sb.AppendLine("  Set proportions (raw -> bias-corrected):");
            for (int k = 0; k < r.SetIds.Length; k++)
                sb.AppendLine($"    S{r.SetIds[k]}: {r.RawCount[k]} ({r.RawFraction[k] * 100:F1}%) -> {r.CorrectedCount[k]:F1} ({r.CorrectedFraction[k] * 100:F1}%)");
        }
        else sb.AppendLine("  (no set ids given -> per-discontinuity weights only)");
        return sb.ToString().TrimEnd();
    }
}
