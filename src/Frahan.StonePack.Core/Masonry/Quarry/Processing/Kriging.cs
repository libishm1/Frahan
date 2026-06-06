#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// Kriging -- ordinary kriging / Gaussian-process regression of a scattered 2-D
// field z = f(x,y) (here: fracture DEPTH over the survey footprint).
//
// This is the MANAGED replacement for the Python sklearn GaussianProcessRegressor
// used in the prototype: it both INTERPOLATES the fracture surface AND yields the
// posterior STANDARD DEVIATION at every query point -- which is sigma_interp, the
// "fracture reconstruction" rung of the tolerance ladder (FractureUncertainty).
// Kriging is just linear algebra, so it ships in C# with no native shim and no
// Python runtime (a Python worker would force numpy/scipy/sklearn onto every install).
//
// Model: simple kriging on mean-centred data with a Gaussian covariance
//   C(h) = sill * exp(-(h/range)^2),  nugget added to the diagonal (the GPR noise /
//   denoising term). Predict:
//     mean(x*) = mean + k*^T alpha,        alpha = K^-1 (z - mean)
//     var(x*)  = (sill + nugget) - w^T w,  w = L^-1 k*   (K = L L^T, Cholesky)
// Hyperparameters default from the data (range ~ 3x median nearest-neighbour
// spacing; sill = var(z); nugget = noiseFrac * sill) but can be supplied.
//
// n is small here (<= ~530 picks/fracture) so the dense O(n^3) Cholesky is instant.
// Rhino-free. Validated against the sklearn prototype (fracture_uncertainty.py).
// =============================================================================

public sealed class Kriging
{
    private readonly double[] _x, _y, _alpha;
    private readonly double[][] _L;          // lower Cholesky factor of K
    private readonly double _mean, _sill, _range2, _nugget, _nuggetUsed;
    private readonly int _n;

    public double Range { get; }
    public double Sill => _sill;
    public double Nugget => _nugget;

    public Kriging(double[] x, double[] y, double[] z,
        double range = -1, double sill = -1, double nugget = -1)
    {
        if (x == null || y == null || z == null) throw new ArgumentNullException();
        _n = x.Length;
        if (_n < 3 || y.Length != _n || z.Length != _n)
            throw new ArgumentException("need >= 3 equal-length (x,y,z) samples");
        _x = (double[])x.Clone(); _y = (double[])y.Clone();

        _mean = Mean(z);
        _sill = sill > 0 ? sill : Math.Max(1e-9, Variance(z));
        var zc = new double[_n];
        for (int i = 0; i < _n; i++) zc[i] = z[i] - _mean;

        if (range > 0)
        {
            Range = range;
            _nugget = nugget >= 0 ? nugget : 0.01 * _sill;
        }
        else
        {
            // fit (range, nugget) by maximising the GP log marginal likelihood -- captures the
            // cross-LINE correlation (a pure nearest-neighbour heuristic is dominated by the dense
            // along-line spacing and badly under-estimates the range, over-stating sigma in gaps).
            (Range, _nugget) = FitMarginalLikelihood(zc, sill, nugget);
        }
        _range2 = Range * Range;

        // Cholesky-factor K (bump the nugget if not SPD: near-duplicate points)
        double bump = _nugget; _nuggetUsed = _nugget;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (TryCholesky(BuildK(_range2, bump), out _L)) { _nuggetUsed = bump; break; }
            bump = Math.Max(bump * 10, 1e-6 * _sill);
            if (attempt == 5) { TryCholesky(BuildK(_range2, bump), out _L); _nuggetUsed = bump; }
        }
        _alpha = CholSolve(_L, zc);
    }

    private double[][] BuildK(double range2, double nugget)
    {
        var K = new double[_n][];
        for (int i = 0; i < _n; i++)
        {
            K[i] = new double[_n];
            for (int j = 0; j < _n; j++)
            {
                double dx = _x[i] - _x[j], dy = _y[i] - _y[j];
                K[i][j] = _sill * Math.Exp(-(dx * dx + dy * dy) / range2);
            }
            K[i][i] += nugget;
        }
        return K;
    }

    // Fit (range, nugget) by maximising the GP log marginal likelihood over a small grid.
    // NLML = 0.5*z^T K^-1 z + 0.5*log|K| (+ const). |K| via the Cholesky diagonal.
    private (double range, double nugget) FitMarginalLikelihood(double[] zc, double sillArg, double nuggetArg)
    {
        double extent = Math.Max(Span(_x), Span(_y));
        if (!(extent > 0)) extent = 1.0;
        var ranges = new[] { 0.03, 0.06, 0.1, 0.15, 0.25, 0.4, 0.6 }.Select(f => f * extent).ToArray();
        var nuggets = nuggetArg >= 0 ? new[] { nuggetArg }
                                     : new[] { 0.003, 0.01, 0.03, 0.08 }.Select(f => f * _sill).ToArray();
        double bestNlml = double.MaxValue, bestR = ranges[0], bestN = nuggets[0];
        foreach (var r in ranges)
            foreach (var nug in nuggets)
            {
                if (!TryCholesky(BuildK(r * r, nug), out var L)) continue;
                var w = ForwardSub(L, zc);                 // L^-1 zc
                double quad = 0; for (int i = 0; i < _n; i++) quad += w[i] * w[i];
                double logdet = 0; for (int i = 0; i < _n; i++) logdet += Math.Log(L[i][i]);  // 0.5 log|K|
                double nlml = 0.5 * quad + logdet;
                if (nlml < bestNlml) { bestNlml = nlml; bestR = r; bestN = nug; }
            }
        return (bestR, bestN);
    }

    private static double Span(double[] a)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var v in a) { if (v < lo) lo = v; if (v > hi) hi = v; }
        return hi - lo;
    }

    /// <summary>Predict (mean, variance) at one query location.</summary>
    public (double Mean, double Variance) Predict(double qx, double qy)
    {
        var k = new double[_n];
        double m = _mean;
        for (int i = 0; i < _n; i++)
        {
            double dx = _x[i] - qx, dy = _y[i] - qy;
            k[i] = _sill * Math.Exp(-(dx * dx + dy * dy) / _range2);
            m += k[i] * _alpha[i];
        }
        var w = ForwardSub(_L, k);          // w = L^-1 k
        double kKk = 0; for (int i = 0; i < _n; i++) kKk += w[i] * w[i];
        double var = _sill - kKk;           // latent predictive variance C(0)=sill
        return (m, Math.Max(0.0, var));
    }

    /// <summary>Posterior standard deviation (m) at a query location = sigma_interp.</summary>
    public double Sigma(double qx, double qy) => Math.Sqrt(Predict(qx, qy).Variance);

    // ---- dense linear algebra (SPD) ----
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

    private static double Mean(double[] a) { double s = 0; foreach (var v in a) s += v; return s / a.Length; }
    private static double Variance(double[] a)
    {
        double m = Mean(a), s = 0; foreach (var v in a) s += (v - m) * (v - m);
        return s / Math.Max(1, a.Length - 1);
    }
}
