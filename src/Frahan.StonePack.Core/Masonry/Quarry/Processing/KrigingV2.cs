#nullable disable
using System;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// KrigingV2 -- the HONEST-uncertainty sibling of Kriging.cs.
//
// Kriging.cs (v1) ships the LATENT predictive variance sill - k*^T K^-1 k*: the
// uncertainty about the noise-free field only. Scored against a 3D-printed STL
// truth (ETH gypsum fracture surface), v1's 95%-band coverage is 0.178 -- ~5x
// overconfident, because the truth carries sub-resolution roughness the latent
// band ignores. KrigingV2 reports the TOTAL prediction variance latent + nugget,
// where the nugget is PICK-CALIBRATED (one depth-sample squared) so it absorbs
// that target roughness. Truth-test coverage rises to 0.959 (near the nominal
// 0.95). See outputs/2026-07-15/krishnagiri_survey/research/{P1_subsample_result,
// P2_csharp_port}.md and pyfrahan/README.md truth-test section.
//
// This is a FAITHFUL PORT of the gaussian-family, isotropic, trend='none',
// scalar-fixed-nugget configuration of pyfrahan/krige.py class KrigingV2 -- the
// 'tauz_pickcal' ablation lever:
//     KrigingV2(x, y, z, family='gaussian', nugget=<one-sample^2>,
//               trend='none', anisotropic=False)   # predict(..., total=True)
// The Python class also carries anisotropy, Matern families, a KED planar trend,
// set-aligned dispatch, and heteroscedastic tau_z / point-nugget models. Those
// levers are NOT ported here (out of scope for the shipped honest band); the code
// below reproduces ONLY the isotropic gaussian scalar-nugget spine, bit-faithful
// to the Python for that path. Deviations are enumerated in P2_csharp_port.md.
//
// Model (identical spine to v1): simple kriging on mean-centred data with a
// Gaussian covariance C(h) = sill * exp(-(h/range)^2), nugget on the diagonal.
//   mean(x*) = mean + k*^T alpha,     alpha = K^-1 (z - mean)
//   var_latent(x*) = sill - w^T w,    w = L^-1 k*   (K = L L^T, Cholesky)
//   var_total(x*)  = var_latent(x*) + nugget          <-- the honest band
// Hyperparameter fit: the SAME NLL grid the Python KrigingV2._fit_hyper walks for
// the isotropic case -- 7 candidate ranges, fixed nugget, azimuth 0, argmin of
// NLML = 0.5 z^T K^-1 z + 0.5 log|K|. Rhino-free, pure double-precision.
//
// PARITY: cross-checked against the Python KrigingV2 via a CSV harness
// (outputs/2026-07-15/krishnagiri_survey/scripts/p2_harness). Fixed-hyper path
// and NLL-fit path both agree to < 1e-8 on mean, latent variance, and total
// variance over a 15x15 grid (n=60 synthetic conditioning set). Numbers in
// P2_csharp_port.md.
// =============================================================================

public sealed class KrigingV2
{
    private readonly double[] _x, _y;        // conditioning coords (identity transform; set-align not ported)
    private readonly double[] _resid;        // z - mean (trend='none')
    private readonly double[] _alpha;        // K^-1 resid
    private readonly double[] _nug;          // per-point nugget vector (variance units)
    private readonly double[][] _L;          // lower Cholesky factor of the final K
    private readonly double _mean, _sill, _rmaj, _rmin, _az;
    private readonly int _n;

    public double RangeMajor => _rmaj;
    public double RangeMinor => _rmin;
    public double Sill => _sill;

    /// <summary>Mean of the per-point nugget vector (the scalar nugget for this port).</summary>
    public double NuggetMean
    {
        get { double s = 0; for (int i = 0; i < _nug.Length; i++) s += _nug[i]; return s / _nug.Length; }
    }

    /// <summary>
    /// Port of pyfrahan KrigingV2 (gaussian / isotropic / trend='none' / scalar nugget).
    /// </summary>
    /// <param name="x">conditioning x.</param>
    /// <param name="y">conditioning y.</param>
    /// <param name="z">conditioning z (depth).</param>
    /// <param name="nugget">scalar fixed nugget (variance). &lt; 0 = fit/derive (matches Python nugget=None).</param>
    /// <param name="rangeMajor">fixed major range. &lt;= 0 with rangeMinor &lt;= 0 = fit by NLL.</param>
    /// <param name="rangeMinor">fixed minor range. Isotropic port: pass == rangeMajor.</param>
    /// <param name="fit">fit the range by marginal likelihood when ranges are not fixed (Python fit=True).</param>
    /// <param name="sill">fixed sill (variance). &lt;= 0 = data variance (Python sill=None).</param>
    public KrigingV2(double[] x, double[] y, double[] z,
        double nugget = -1, double rangeMajor = -1, double rangeMinor = -1,
        bool fit = true, double sill = -1)
    {
        if (x == null || y == null || z == null) throw new ArgumentNullException();
        _n = x.Length;
        if (_n < 3 || y.Length != _n || z.Length != _n)
            throw new ArgumentException("need >= 3 equal-length (x,y,z) samples");
        _x = (double[])x.Clone();
        _y = (double[])y.Clone();

        // --- trend='none': mean-centre (Python: self._mean = z.mean(); resid = z - mean) ---
        _mean = Mean(z);
        _resid = new double[_n];
        for (int i = 0; i < _n; i++) _resid[i] = z[i] - _mean;

        // sill = fixed, else var(resid, ddof=1) floored at 1e-12 (Python max(1e-12, np.var(resid, ddof=1)))
        _sill = sill > 0 ? sill : Math.Max(1e-12, Variance(_resid));

        // per-point nugget vector: scalar fixed if provided, else null -> fit path fills it
        double[] nug = nugget >= 0 ? Full(_n, nugget) : null;

        _az = 0.0;                            // isotropic port: azimuth is always 0

        double extent = Math.Max(Span(_x), Span(_y));
        if (!(extent > 0)) extent = 1.0;

        if (rangeMajor > 0 && rangeMinor > 0)
        {
            // fixed ranges (Python: range_major is not None and range_minor is not None)
            _rmaj = rangeMajor; _rmin = rangeMinor;
            if (nug == null) nug = Full(_n, 0.01 * _sill);
        }
        else if (fit)
        {
            (_rmaj, _rmin, nug) = FitHyper(extent, nug);
        }
        else
        {
            _rmaj = _rmin = 0.15 * extent;
            if (nug == null) nug = Full(_n, 0.01 * _sill);
        }
        _nug = nug;

        // factor the final system and solve alpha = K^-1 resid
        _L = Factor(_rmaj, _rmin, _az, _nug);
        _alpha = CholSolve(_L, _resid);
    }

    // ---- covariance -----------------------------------------------------------
    // gaussian family only (Python _corr('gaussian', r) = exp(-(r*r))).
    private static double CorrGaussian(double r) => Math.Exp(-(r * r));

    // Anisotropic scaled lag (Python _aniso_lag): rotate by -az, divide by ranges.
    // az==0, rmaj==rmin in this port => r = sqrt(dx^2 + dy^2) / range.
    private static double AnisoLag(double dx, double dy, double rmaj, double rmin, double az)
    {
        double c = Math.Cos(az), s = Math.Sin(az);
        double u = (dx * c + dy * s) / rmaj;
        double v = (-dx * s + dy * c) / rmin;
        return Math.Sqrt(u * u + v * v);
    }

    // K_signal[i][j] = sill * corr(lag) (Python _build_signal, no nugget on the diagonal here).
    private double[][] BuildSignal(double rmaj, double rmin, double az)
    {
        var K = new double[_n][];
        for (int i = 0; i < _n; i++)
        {
            K[i] = new double[_n];
            for (int j = 0; j < _n; j++)
            {
                double r = AnisoLag(_x[i] - _x[j], _y[i] - _y[j], rmaj, rmin, az);
                K[i][j] = _sill * CorrGaussian(r);
            }
        }
        return K;
    }

    // Factor K = signal + diag(nug) with the Python _factor bump ladder.
    private double[][] Factor(double rmaj, double rmin, double az, double[] nug)
    {
        double[][] K = BuildSignal(rmaj, rmin, az);
        for (int i = 0; i < _n; i++) K[i][i] += nug[i];

        double[][] L;
        if (TryCholesky(K, out L)) return L;

        // while L is None and bump < 1e3*sill+1e-6: L = chol(K + eye*bump); bump *= 10
        double bump = 1e-9 * _sill;
        while (bump < 1e3 * _sill + 1e-6)
        {
            if (TryCholesky(AddDiag(K, bump), out L)) return L;
            bump *= 10;
        }
        // final fallback: chol(K + eye*(1e-3*sill + 1e-9))
        TryCholesky(AddDiag(K, 1e-3 * _sill + 1e-9), out L);
        return L;
    }

    // ---- hyperparameter fit (isotropic gaussian; Python _fit_hyper) -----------
    // NLML = 0.5 z^T K^-1 z + 0.5 log|K|; grid over 7 ranges, fixed nugget (or 4
    // candidate nuggets when nug==null), azimuth 0, ratio 1 (isotropic).
    private (double rmaj, double rmin, double[] nug) FitHyper(double extent, double[] nug)
    {
        double[] rFacs = { 0.03, 0.06, 0.1, 0.15, 0.25, 0.4, 0.6 };
        var ranges = new double[rFacs.Length];
        for (int i = 0; i < rFacs.Length; i++) ranges[i] = rFacs[i] * extent;

        double[][] nugOpts;
        if (nug != null) nugOpts = new[] { nug };
        else
        {
            double[] nFacs = { 0.003, 0.01, 0.03, 0.08 };
            nugOpts = new double[nFacs.Length][];
            for (int i = 0; i < nFacs.Length; i++) nugOpts[i] = Full(_n, nFacs[i] * _sill);
        }

        // best init: (inf, ranges[3], ranges[3], 0.0, nugOpts[0])   -- ratios=(1.0,), azs=(0.0,)
        double bestNll = double.PositiveInfinity;
        double bestRmaj = ranges[3], bestRmin = ranges[3];
        double[] bestNug = nugOpts[0];
        foreach (double rmaj in ranges)
        {
            double rmin = rmaj;                 // ratio == 1 (isotropic)
            foreach (double[] cand in nugOpts)
            {
                double nll = Nll(rmaj, rmin, 0.0, cand);
                if (nll < bestNll) { bestNll = nll; bestRmaj = rmaj; bestRmin = rmin; bestNug = cand; }
            }
        }
        return (bestRmaj, bestRmin, bestNug);
    }

    // NLML for a (range, nugget) candidate (Python _nll). +inf if K not SPD.
    private double Nll(double rmaj, double rmin, double az, double[] nug)
    {
        double[][] K = BuildSignal(rmaj, rmin, az);
        for (int i = 0; i < _n; i++) K[i][i] += nug[i];
        if (!TryCholesky(K, out var L)) return double.PositiveInfinity;
        var w = ForwardSub(L, _resid);                 // L^-1 resid
        double quad = 0; for (int i = 0; i < _n; i++) quad += w[i] * w[i];
        double logdet = 0; for (int i = 0; i < _n; i++) logdet += Math.Log(L[i][i]);   // 0.5 log|K|
        return 0.5 * quad + logdet;
    }

    // ---- prediction -----------------------------------------------------------
    /// <summary>LATENT (mean, variance) at one query -- the shipped v1 / Kriging.cs definition.</summary>
    public (double Mean, double Variance) Predict(double qx, double qy) => PredictOne(qx, qy, false);

    /// <summary>HONEST TOTAL (mean, variance) = latent + nugget -- the truth-validated band.</summary>
    public (double Mean, double Variance) PredictTotal(double qx, double qy) => PredictOne(qx, qy, true);

    /// <summary>Total posterior standard deviation (m) at a query = the honest sigma.</summary>
    public double SigmaTotal(double qx, double qy) => Math.Sqrt(PredictTotal(qx, qy).Variance);

    private (double Mean, double Variance) PredictOne(double qx, double qy, bool total)
    {
        var k = new double[_n];
        double m = _mean;
        for (int i = 0; i < _n; i++)
        {
            double r = AnisoLag(qx - _x[i], qy - _y[i], _rmaj, _rmin, _az);
            k[i] = _sill * CorrGaussian(r);
            m += k[i] * _alpha[i];
        }
        var w = ForwardSub(_L, k);          // w = L^-1 k
        double kKk = 0; for (int i = 0; i < _n; i++) kKk += w[i] * w[i];
        double var = _sill - kKk;           // latent predictive variance
        if (var < 0.0) var = 0.0;
        if (total) var += NuggetAt();       // + observation nugget (scalar => mean of _nug)
        return (m, var);
    }

    // Python _nugget_at for the scalar-nugget, hetero=False, point_nugget=None path
    // returns mean(self._nug); for a constant vector that is the scalar nugget.
    private double NuggetAt() => NuggetMean;

    /// <summary>Vectorised prediction over a query set (same math as Predict/PredictTotal).</summary>
    public (double[] Mean, double[] Variance) PredictGrid(double[] xs, double[] ys, bool total = false)
    {
        if (xs == null || ys == null) throw new ArgumentNullException();
        if (xs.Length != ys.Length) throw new ArgumentException("xs and ys must be equal length");
        var means = new double[xs.Length];
        var varis = new double[xs.Length];
        for (int q = 0; q < xs.Length; q++)
        {
            var (m, v) = PredictOne(xs[q], ys[q], total);
            means[q] = m; varis[q] = v;
        }
        return (means, varis);
    }

    // ---- dense linear algebra (SPD) -- identical to Kriging.cs -----------------
    private static bool TryCholesky(double[][] A, out double[][] L)
    {
        int n = A.Length;
        L = new double[n][];
        for (int i = 0; i < n; i++) L[i] = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double s = A[i][j];
                for (int k = 0; k < j; k++) s -= L[i][k] * L[j][k];
                if (i == j)
                {
                    if (s <= 0) return false;
                    L[i][j] = Math.Sqrt(s);
                }
                else L[i][j] = s / L[j][j];
            }
        }
        return true;
    }

    private static double[] ForwardSub(double[][] L, double[] b)
    {
        int n = L.Length; var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= L[i][k] * x[k];
            x[i] = s / L[i][i];
        }
        return x;
    }

    private static double[] CholSolve(double[][] L, double[] b)
    {
        // solve L L^T x = b: forward L y = b, back L^T x = y
        int n = L.Length;
        var y = ForwardSub(L, b);
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int k = i + 1; k < n; k++) s -= L[k][i] * x[k];
            x[i] = s / L[i][i];
        }
        return x;
    }

    // ---- small helpers --------------------------------------------------------
    private static double[][] AddDiag(double[][] A, double d)
    {
        int n = A.Length;
        var B = new double[n][];
        for (int i = 0; i < n; i++)
        {
            B[i] = (double[])A[i].Clone();
            B[i][i] += d;
        }
        return B;
    }

    private static double[] Full(int n, double v)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }

    private static double Span(double[] a)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var v in a) { if (v < lo) lo = v; if (v > hi) hi = v; }
        return hi - lo;
    }

    private static double Mean(double[] a) { double s = 0; foreach (var v in a) s += v; return s / a.Length; }

    private static double Variance(double[] a)
    {
        double m = Mean(a), s = 0; foreach (var v in a) s += (v - m) * (v - m);
        return s / Math.Max(1, a.Length - 1);
    }
}
