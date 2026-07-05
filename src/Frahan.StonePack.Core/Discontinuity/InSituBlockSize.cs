#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// InSituBlockSize -- Monte-Carlo in-situ block-size distribution (IBSD) for a
// jointed rock mass. Where BlockSizeMath gives ONE deterministic block volume
// from the mean set orientations + spacings, this samples the natural scatter
// (Fisher orientation + spacing PDF) over many realizations and reports the
// DISTRIBUTION of block volumes and shapes -- the honest characterization output
// a rock-mechanics / dimension-stone reader expects, and the geology->fabrication
// bridge (how sawable-into-rectangular-blocks the natural fabric is).
//
// Block volume of a parallelepiped cut by 3 sets (unit poles n1,n2,n3, normal
// spacings s1,s2,s3):
//     Vb = s1*s2*s3 / q ,   q = |n1 . (n2 x n3)|   (scalar triple product)
// q in (0,1] is the NON-ORTHOGONALITY coefficient: q = 1 for mutually orthogonal
// sets (a right prism), q -> 0 as any two sets become parallel (block ill-defined).
// This is the exact form of Palmstrom's Vb = s1 s2 s3 / (sin g12 sin g23 sin g31)
// with the inter-set angle products folded into the determinant.
//
// Sampling per realization:
//   - orientation: Fisher (von Mises-Fisher) distribution about each set's mean
//     pole with concentration kappa (from an input angular scatter, kappa =
//     (81/scatterDeg)^2, Fisher's circular s.d. approximation).
//   - spacing: negative-exponential s = -mean*ln(U) (Priest 1993 default) or a
//     clamped normal (CV 0.3).
//
// References:
//   - Palmstrom, A. (2005). Measurements of and correlations between block size
//     and rock quality designation (RQD). Vb = s1 s2 s3 / q.
//   - Kalenchuk, Diederichs & McKinnon (2006). Characterizing block geometry in
//     jointed rockmasses (IBSD via Monte-Carlo).
//   - Elmo, Rogers, Stead, Eberhardt (2014). DFN-based block-size / IBSD.
//   - Priest, S.D. (1993). Discontinuity Analysis (negative-exponential spacing).
//   - Fisher, Lewis & Embleton (1987). Statistical analysis of spherical data
//     (Fisher sampling by inverse CDF of the colatitude).
//
// Pure managed arithmetic (Rhino value types + System.Random), deterministic for
// a fixed seed, headless-unit-testable.
// =============================================================================

/// <summary>In-situ block-size distribution result over many Monte-Carlo realizations.</summary>
public sealed class IbsdResult
{
    /// <summary>Per-realization block volume (m^3), the empirical distribution (valid realizations only).</summary>
    public double[] Volumes;
    /// <summary>Per-realization non-orthogonality coefficient q = |det(n1,n2,n3)| in (0,1].</summary>
    public double[] Q;
    public int Realizations, Valid;
    public double MeanVol, StdVol, P10Vol, P50Vol, P90Vol;
    /// <summary>Equivalent block diameter of the median block, Vb50^(1/3) (m).</summary>
    public double DeqMedian;
    public double MeanQ;
    /// <summary>Fraction of blocks that are effectively right prisms (q &gt;= 0.95) -- the sawable-to-rectangular signal.</summary>
    public double RightPrismFraction;
    /// <summary>Shape-class counts: [blocky, columnar, tabular, columnar+tabular].</summary>
    public int[] ShapeCounts = new int[4];
    public static readonly string[] ShapeLabels = { "blocky/equidimensional", "columnar (bar)", "tabular (slab)", "columnar+tabular" };
    public List<string> Notes = new List<string>();
    public string Report = "";
}

public static class InSituBlockSize
{
    private const double D2R = Math.PI / 180.0;
    private const double Eps = 1e-9;

    /// <summary>
    /// Monte-Carlo IBSD from per-set mean dip / dip-direction / spacing.
    /// <paramref name="scatterDeg"/> is the Fisher angular scatter (one value for all
    /// sets, or one per set). <paramref name="exponential"/> selects the spacing law
    /// (true = negative-exponential, default; false = clamped normal CV 0.3).
    /// Needs at least 3 sets; with more than 3, the 3 smallest-spacing sets bound
    /// each realization's block.
    /// </summary>
    public static IbsdResult Simulate(
        IReadOnlyList<double> setDip,
        IReadOnlyList<double> setDipDir,
        IReadOnlyList<double> meanSpacing,
        IReadOnlyList<double> scatterDeg,
        bool exponential = true,
        int realizations = 1000,
        int seed = 1,
        double unitScale = 1.0)
    {
        var res = new IbsdResult { Realizations = Math.Max(1, realizations) };
        if (setDip == null || setDipDir == null || meanSpacing == null)
        { res.Report = "No sets."; return res; }
        int nSets = Math.Min(setDip.Count, Math.Min(setDipDir.Count, meanSpacing.Count));
        if (nSets < 3)
        {
            res.Report = $"IBSD needs >= 3 joint sets (got {nSets}). A block volume is undefined with fewer.";
            res.Notes.Add("With 1 set the mass is tabular slabs; with 2, columns -- no bounded block.");
            return res;
        }
        if (unitScale <= 0) unitScale = 1.0;

        var meanPole = new Vector3d[nSets];
        var kappa = new double[nSets];
        var sMean = new double[nSets];
        for (int i = 0; i < nSets; i++)
        {
            meanPole[i] = OrientationMath.NormalFromDipDipDir(setDip[i], setDipDir[i]);
            double sc = (scatterDeg != null && scatterDeg.Count > 0)
                ? (scatterDeg.Count == nSets ? scatterDeg[i] : scatterDeg[0]) : 10.0;
            if (sc < 0.5) sc = 0.5;
            kappa[i] = (81.0 / sc) * (81.0 / sc);   // Fisher circular s.d. ~ 81/sqrt(kappa) deg
            sMean[i] = Math.Max(Eps, meanSpacing[i] * unitScale);
        }

        var rng = new Random(seed);
        var vols = new List<double>(res.Realizations);
        var qs = new List<double>(res.Realizations);
        double sumQ = 0;

        var idx = new int[nSets];
        var sSamp = new double[nSets];
        var nSamp = new Vector3d[nSets];

        for (int r = 0; r < res.Realizations; r++)
        {
            for (int i = 0; i < nSets; i++)
            {
                nSamp[i] = FisherSample(meanPole[i], kappa[i], rng);
                sSamp[i] = SampleSpacing(sMean[i], exponential, rng);
                idx[i] = i;
            }
            // choose the 3 smallest-spacing sets (they bound the block)
            int a = 0, b = 1, c = 2;
            if (nSets > 3) PickThreeSmallest(sSamp, nSets, out a, out b, out c);

            double q = Math.Abs(Det(nSamp[a], nSamp[b], nSamp[c]));
            if (q < 0.02) continue;                 // ~1 deg from coplanar -> ill-conditioned, skip
            double vb = sSamp[a] * sSamp[b] * sSamp[c] / q;
            if (!(vb > 0) || double.IsInfinity(vb)) continue;

            vols.Add(vb); qs.Add(q); sumQ += q;
            if (q >= 0.95) res.RightPrismFraction += 1.0;
            res.ShapeCounts[ShapeClass(sSamp[a], sSamp[b], sSamp[c])]++;
        }

        res.Valid = vols.Count;
        if (res.Valid == 0)
        {
            res.Report = "All realizations ill-conditioned (sets too near-parallel).";
            return res;
        }
        res.Volumes = vols.ToArray();
        res.Q = qs.ToArray();
        res.MeanQ = sumQ / res.Valid;
        res.RightPrismFraction /= res.Valid;

        var sorted = (double[])res.Volumes.Clone();
        Array.Sort(sorted);
        res.P10Vol = Percentile(sorted, 0.10);
        res.P50Vol = Percentile(sorted, 0.50);
        res.P90Vol = Percentile(sorted, 0.90);
        res.DeqMedian = Math.Pow(res.P50Vol, 1.0 / 3.0);
        double mean = 0; foreach (var v in res.Volumes) mean += v; mean /= res.Valid;
        double var2 = 0; foreach (var v in res.Volumes) var2 += (v - mean) * (v - mean);
        res.MeanVol = mean; res.StdVol = Math.Sqrt(var2 / res.Valid);

        res.Report = BuildReport(res, nSets, exponential);
        return res;
    }

    // ---- Fisher (vMF) sampling about a mean pole ---------------------------
    private static Vector3d FisherSample(Vector3d mu, double kappa, Random r)
    {
        var m = mu; m.Unitize();
        if (kappa > 700) return m;                  // effectively a point mass
        double u = r.NextDouble();
        double w = 1.0 + Math.Log(u + (1.0 - u) * Math.Exp(-2.0 * kappa)) / kappa; // cos(colatitude)
        if (w > 1) w = 1; if (w < -1) w = -1;
        double s = Math.Sqrt(Math.Max(0.0, 1.0 - w * w));
        double phi = 2.0 * Math.PI * r.NextDouble();
        Vector3d a = Math.Abs(m.Z) < 0.9 ? Vector3d.CrossProduct(m, Vector3d.ZAxis)
                                         : Vector3d.CrossProduct(m, Vector3d.XAxis);
        a.Unitize();
        Vector3d b = Vector3d.CrossProduct(m, a); b.Unitize();
        var v = w * m + s * (Math.Cos(phi) * a + Math.Sin(phi) * b);
        v.Unitize();
        return v;
    }

    private static double SampleSpacing(double mean, bool exponential, Random r)
    {
        if (exponential)
        {
            double u = Math.Max(1e-9, r.NextDouble());
            return -mean * Math.Log(u);
        }
        // clamped normal, CV 0.3 (Box-Muller)
        double u1 = Math.Max(1e-9, r.NextDouble()), u2 = r.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        double sp = mean * (1.0 + 0.3 * z);
        return Math.Max(mean * 0.05, sp);
    }

    private static double Det(Vector3d n1, Vector3d n2, Vector3d n3)
        => n1.X * (n2.Y * n3.Z - n2.Z * n3.Y)
         - n1.Y * (n2.X * n3.Z - n2.Z * n3.X)
         + n1.Z * (n2.X * n3.Y - n2.Y * n3.X);

    private static void PickThreeSmallest(double[] s, int n, out int a, out int b, out int c)
    {
        a = b = c = -1;
        for (int i = 0; i < n; i++)
        {
            if (a < 0 || s[i] < s[a]) { c = b; b = a; a = i; }
            else if (b < 0 || s[i] < s[b]) { c = b; b = i; }
            else if (c < 0 || s[i] < s[c]) { c = i; }
        }
    }

    // shape from the 3 spacings: elongation = smax/smid, flatness = smid/smin
    private static int ShapeClass(double s1, double s2, double s3)
    {
        double lo = Math.Min(s1, Math.Min(s2, s3));
        double hi = Math.Max(s1, Math.Max(s2, s3));
        double mid = s1 + s2 + s3 - lo - hi;
        double elong = mid > Eps ? hi / mid : 1.0;
        double flat = lo > Eps ? mid / lo : 1.0;
        bool e = elong >= 2.0, f = flat >= 2.0;
        if (e && f) return 3;
        if (e) return 1;
        if (f) return 2;
        return 0;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return double.NaN;
        double x = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(x); int hi = (int)Math.Ceiling(x);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (x - lo) * (sorted[hi] - sorted[lo]);
    }

    private static string BuildReport(IbsdResult r, int nSets, bool exp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("In-situ block size distribution (Monte-Carlo IBSD; Palmstrom/Kalenchuk).");
        sb.AppendLine($"  {nSets} sets, {r.Valid}/{r.Realizations} valid realizations; spacing law: {(exp ? "negative-exponential" : "normal CV0.3")}.");
        sb.AppendLine($"  Block volume Vb (m^3):  P10 {r.P10Vol:G3}   P50 {r.P50Vol:G3}   P90 {r.P90Vol:G3}   mean {r.MeanVol:G3}");
        sb.AppendLine($"  Median equivalent diameter Deq = {r.DeqMedian:G3} m");
        sb.AppendLine($"  Mean non-orthogonality q = {r.MeanQ:F2}  (1 = right prism, ->0 = ill-conditioned)");
        sb.AppendLine("  Block shape mix:");
        int tot = r.Valid;
        for (int i = 0; i < 4; i++)
            sb.AppendLine($"    {IbsdResult.ShapeLabels[i]}: {100.0 * r.ShapeCounts[i] / tot:F0}%");
        sb.AppendLine($"  Right-prism fraction (q>=0.95) = {100.0 * r.RightPrismFraction:F0}%  <- sawable-to-rectangular signal");
        if (r.RightPrismFraction < 0.2)
            sb.AppendLine("  ! Low right-prism fraction: natural fabric yields few rectangular blocks; expect high off-cut waste or oblique cutting.");
        foreach (var note in r.Notes) sb.AppendLine("  ! " + note);
        return sb.ToString().TrimEnd();
    }
}
