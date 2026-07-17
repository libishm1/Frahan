#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// KrigingField3D / KrigingField2D -- implicit (level-set) kriging v3.
//
// Faithful C# port of pyfrahan/krige3d.py (Task G13; research/G13_v3_result.md).
// The representation flip (G11 finding 1): instead of kriging a height d(x,y),
// krige a 3D scalar field F(x,y,z) whose zero level-set IS the fracture surface.
// F=0 is dip-agnostic -- a vertical wall is as cheap as a horizontal bed, and a
// multi-valued surface (overhang, stacked reflectors) is representable because
// the zero-set is not a graph over (x,y). Potential-field / RBF-implicit method
// (Lajaunie 1997; Carr 2001; Calcagno 2008; GemPy) reused in a kriging frame.
//
// Data model (per pick i with family normal n_i):
//   F = 0  at the pick               (on-surface interface constraint)
//   F = +h at pick + h*n_i           (manufactured signed off-surface point)
//   F = -h at pick - h*n_i
//   h = h_frac * s, s = median nearest-neighbour pick spacing.
// Pure F=0 data is degenerate (F==0 fits it); the +-h off-surface points supply
// the REQUIRED polarity (Carr 2001; G11's recommended route ii). Per-pick sigma
// enters as point_nugget (variance sigma_i^2) on the F=0 Gram diagonal (G11
// finding 3; Chiles & Delfiner filtering-the-nugget). Simple (mean-0) kriging of
// F; Gaussian covariance default (matern15/25/exponential optional) with a 3D
// anisotropic lag; Cholesky nugget-bump ladder. predict_grid_3d returns
// (F, sigma_F); sigma_F is the latent kriging sigma sqrt(sill - k^T K^-1 k), the
// same definition v1 (Kriging.cs) / KrigingV2 use. Delta-method position sigma
// sigma_pos = sigma_F/|grad F| (floor-guarded) is coloured onto the extracted
// isosurface vertices (see MarchingCubes.cs).
//
// PORT PROVENANCE / RECONCILE NOTE: the covariance family (_corr), Cholesky
// factor + nugget-bump ladder, and cho-solve idioms here are reproduced from the
// Python krige.py (which itself ports Kriging.cs v1) and are duplicated with the
// sister branch's KrigingV2.cs (feat/krige-v2-honest-sigma, commit 52a739a). This
// branch (port/csharp-implicit-kriging) is off main and does NOT contain
// KrigingV2.cs, so Krige3dMath below is self-contained. At MERGE TIME the shared
// primitives (Corr, TryCholesky, ForwardSub, CholSolveL, Span) should be unified
// with KrigingV2.cs into one internal helper (they are byte-equivalent math).
//
// net48 / Rhino-free. Validated by the CSV parity harness against krige3d.py
// (outputs/2026-07-15/krishnagiri_survey/scripts/csharp_kriging_parity).
// =============================================================================

/// <summary>Shared numeric primitives for the implicit kriging fields. Reproduced
/// from krige.py (_corr / _try_cholesky / _cho_solve_L / _span); duplicated with
/// KrigingV2.cs on the sister branch -- unify at merge.</summary>
internal static class Krige3dMath
{
    internal static readonly double Sqrt3 = Math.Sqrt(3.0);
    internal static readonly double Sqrt5 = Math.Sqrt(5.0);

    /// <summary>Unit-range correlation rho(r), r = anisotropic-scaled lag (>= 0).
    /// Mirrors krige._corr.</summary>
    internal static double Corr(string family, double r)
    {
        switch (family)
        {
            case "gaussian":                     // squared-exponential (v1 family)
                return Math.Exp(-(r * r));
            case "exponential":                  // Matern nu = 1/2
                return Math.Exp(-r);
            case "matern15":                     // Matern nu = 3/2
            {
                double a = Sqrt3 * r;
                return (1.0 + a) * Math.Exp(-a);
            }
            case "matern25":                     // Matern nu = 5/2
            {
                double a = Sqrt5 * r;
                return (1.0 + a + a * a / 3.0) * Math.Exp(-a);
            }
            default:
                throw new ArgumentException("unknown covariance family '" + family + "'");
        }
    }

    /// <summary>Lower Cholesky factor of SPD A, or null if not SPD
    /// (mirrors numpy.linalg.cholesky / krige._try_cholesky).</summary>
    internal static double[][] TryCholesky(double[][] A)
    {
        int n = A.Length;
        var L = new double[n][];
        for (int i = 0; i < n; i++) L[i] = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double s = A[i][j];
                for (int k = 0; k < j; k++) s -= L[i][k] * L[j][k];
                if (i == j)
                {
                    if (s <= 0) return null;
                    L[i][j] = Math.Sqrt(s);
                }
                else
                {
                    L[i][j] = s / L[j][j];
                }
            }
        }
        return L;
    }

    /// <summary>Solve L x = b for lower-triangular L (forward substitution).</summary>
    internal static double[] ForwardSub(double[][] L, double[] b)
    {
        int n = L.Length;
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= L[i][k] * x[k];
            x[i] = s / L[i][i];
        }
        return x;
    }

    /// <summary>Solve K x = b via K = L L^T (forward then back). Mirrors
    /// krige._cho_solve_L (numpy solve(L,.) then solve(L.T,.)).</summary>
    internal static double[] CholSolveL(double[][] L, double[] b)
    {
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

    internal static double Span(double[] a)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < a.Length; i++) { double v = a[i]; if (v < lo) lo = v; if (v > hi) hi = v; }
        return hi - lo;
    }

    /// <summary>numpy.median of a copy of a (mean of the two central order
    /// statistics for even length).</summary>
    internal static double Median(double[] a)
    {
        int n = a.Length;
        if (n == 0) return double.NaN;
        var c = (double[])a.Clone();
        Array.Sort(c);
        if ((n & 1) == 1) return c[n / 2];
        return 0.5 * (c[n / 2 - 1] + c[n / 2]);
    }

    /// <summary>Sample variance (ddof=1): sum((v-mean)^2)/(N-1). Mirrors
    /// numpy.var(., ddof=1) (mean computed first).</summary>
    internal static double VarDdof1(double[] a)
    {
        int n = a.Length;
        double m = 0.0;
        for (int i = 0; i < n; i++) m += a[i];
        m /= n;
        double s = 0.0;
        for (int i = 0; i < n; i++) { double d = a[i] - m; s += d * d; }
        return s / Math.Max(1, n - 1);
    }

    /// <summary>Median nearest-neighbour distance of P [N,d]. Brute force,
    /// strictly positive. Mirrors krige3d._median_nn_spacing.</summary>
    internal static double MedianNnSpacing(double[][] P)
    {
        int n = P.Length;
        if (n < 2) return 1.0;
        int d = P[0].Length;
        var best = new double[n];
        for (int i = 0; i < n; i++)
        {
            double bd2 = double.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                double s = 0.0;
                for (int c = 0; c < d; c++) { double diff = P[i][c] - P[j][c]; s += diff * diff; }
                if (s < bd2) bd2 = s;
            }
            best[i] = Math.Sqrt(bd2);
        }
        double med = Median(best);
        return med > 1e-12 ? med : 1.0;
    }
}

/// <summary>Simple (mean-0) kriging of a 3D scalar field F(x,y,z) whose zero
/// level-set is the fracture surface. Faithful port of krige3d.KrigingField3D.</summary>
public sealed class KrigingField3D
{
    private readonly string _family;
    private readonly double[][] _pts;        // [3N,3] : P0 block, +h block, -h block
    private readonly double[] _vals;         // [3N]  mean-0 field values
    private readonly double[] _nug;          // [3N]  per-observation nugget (variance)
    private readonly double[][] _L;          // Cholesky factor of the final Gram
    private readonly double[] _alpha;        // K^-1 vals
    private readonly double _sill, _r, _rz;
    private readonly int _n;

    public double Spacing { get; }
    public double H { get; }
    public double HFrac { get; }
    public double GradFloorFrac { get; }
    public double Sill => _sill;
    public double Range => _r;
    public double RangeZ => _rz;
    public double[][] Points => _pts;

    /// <param name="normals">Either a single (3,) family normal (broadcast) or a
    /// per-pick (N,3) array (jagged). Off-surface points are placed +-h along these.</param>
    /// <param name="pointSigma">per-pick sigma (same units as z); enters as
    /// point_nugget (variance sigma_i^2) on the F=0 diagonal. null -> use offsurf nugget.</param>
    public KrigingField3D(double[] x, double[] y, double[] z, double[][] normals,
        string family = "gaussian", double hFrac = 1.5,
        double spacing = double.NaN, double[] pointSigma = null,
        double range = double.NaN, double rangeZ = double.NaN,
        double sill = double.NaN, double offsurfNugFrac = 1e-4,
        double gradFloorFrac = 0.2, bool fit = true)
    {
        if (x == null || y == null || z == null) throw new ArgumentNullException();
        int n = x.Length;
        if (n < 3 || y.Length != n || z.Length != n)
            throw new ArgumentException("need >= 3 equal-length (x,y,z) on-surface picks");

        var p0 = new double[n][];
        for (int i = 0; i < n; i++) p0[i] = new[] { x[i], y[i], z[i] };

        // normalise polarity normals (broadcast (3,) if given as a single row)
        var nrm = NormaliseNormals(normals, n, 3);

        _family = family;
        double s = !double.IsNaN(spacing) ? spacing : Krige3dMath.MedianNnSpacing(p0);
        Spacing = s;
        H = hFrac * s;
        HFrac = hFrac;
        GradFloorFrac = gradFloorFrac;

        // manufacture the signed off-surface polarity points (Carr 2001)
        _n = n;
        _pts = new double[3 * n][];
        _vals = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            _pts[i] = new[] { p0[i][0], p0[i][1], p0[i][2] };                    // F = 0
            _pts[n + i] = new[] { p0[i][0] + H * nrm[i][0], p0[i][1] + H * nrm[i][1], p0[i][2] + H * nrm[i][2] };
            _pts[2 * n + i] = new[] { p0[i][0] - H * nrm[i][0], p0[i][1] - H * nrm[i][1], p0[i][2] - H * nrm[i][2] };
            _vals[i] = 0.0;
            _vals[n + i] = H;
            _vals[2 * n + i] = -H;
        }

        _sill = (!double.IsNaN(sill) && sill > 0) ? sill
              : Math.Max(1e-12, Krige3dMath.VarDdof1(_vals));

        // per-observation nugget vector (variance units)
        double off = offsurfNugFrac * _sill;
        _nug = new double[3 * n];
        if (pointSigma != null)
        {
            if (pointSigma.Length != n) throw new ArgumentException("pointSigma length must match n");
            for (int i = 0; i < n; i++) _nug[i] = Math.Max(pointSigma[i] * pointSigma[i], 1e-12);
        }
        else
        {
            for (int i = 0; i < n; i++) _nug[i] = off;
        }
        for (int i = n; i < 3 * n; i++) _nug[i] = off;

        // covariance ranges
        double extent = Math.Max(Krige3dMath.Span(Col(_pts, 0)),
                        Math.Max(Krige3dMath.Span(Col(_pts, 1)), Krige3dMath.Span(Col(_pts, 2))));
        if (!(extent > 0)) extent = 1.0;
        if (!double.IsNaN(range)) _r = range;
        else if (fit) _r = FitRange(extent);
        else _r = 0.25 * extent;
        _rz = !double.IsNaN(rangeZ) ? rangeZ : _r;

        _L = Factor(_r, _rz, _nug);
        _alpha = Krige3dMath.CholSolveL(_L, _vals);
    }

    // ---- covariance / factorisation ----
    private static double Lag(double dx, double dy, double dz, double r, double rz)
    {
        double u = dx / r, v = dy / r, w = dz / rz;
        return Math.Sqrt(u * u + v * v + w * w);
    }

    private double[][] Build(double r, double rz)
    {
        int m = _pts.Length;
        var K = new double[m][];
        for (int i = 0; i < m; i++)
        {
            K[i] = new double[m];
            for (int j = 0; j < m; j++)
            {
                double dx = _pts[i][0] - _pts[j][0];
                double dy = _pts[i][1] - _pts[j][1];
                double dz = _pts[i][2] - _pts[j][2];
                K[i][j] = _sill * Krige3dMath.Corr(_family, Lag(dx, dy, dz, r, rz));
            }
        }
        return K;
    }

    private double[][] Factor(double r, double rz, double[] nug)
    {
        var K = Build(r, rz);
        int m = K.Length;
        for (int i = 0; i < m; i++) K[i][i] += nug[i];
        var L = Krige3dMath.TryCholesky(K);
        double bump = 1e-9 * _sill;
        while (L == null && bump < 1e3 * _sill + 1e-6)
        {
            L = Krige3dMath.TryCholesky(AddDiag(K, bump));
            bump *= 10;
        }
        if (L == null) L = Krige3dMath.TryCholesky(AddDiag(K, 1e-3 * _sill + 1e-9));
        return L;
    }

    private static double[][] AddDiag(double[][] K, double d)
    {
        int m = K.Length;
        var A = new double[m][];
        for (int i = 0; i < m; i++)
        {
            A[i] = (double[])K[i].Clone();
            A[i][i] += d;
        }
        return A;
    }

    private double FitRange(double extent)
    {
        double bestNll = double.PositiveInfinity, bestR = 0.25 * extent;
        double[] facs = { 0.05, 0.1, 0.15, 0.25, 0.4, 0.6, 0.9 };
        foreach (double f in facs)
        {
            double r = f * extent;
            var K = Build(r, r);
            int m = K.Length;
            for (int i = 0; i < m; i++) K[i][i] += _nug[i];
            var L = Krige3dMath.TryCholesky(K);
            if (L == null) continue;
            var w = Krige3dMath.ForwardSub(L, _vals);
            double quad = 0.0; for (int i = 0; i < m; i++) quad += w[i] * w[i];
            double logdet = 0.0; for (int i = 0; i < m; i++) logdet += Math.Log(L[i][i]);
            double nll = 0.5 * quad + logdet;
            if (nll < bestNll) { bestNll = nll; bestR = r; }
        }
        return bestR;
    }

    // ---- prediction ----
    /// <summary>Vectorised (F, sigma_F) over flattened query arrays. sigma_F is the
    /// latent kriging sigma sqrt(sill - k^T K^-1 k). Mirrors predict_grid_3d.</summary>
    public (double[] F, double[] SigmaF) PredictGrid3d(double[] xs, double[] ys, double[] zs)
    {
        int m = xs.Length;
        var F = new double[m];
        var S = new double[m];
        int np = _pts.Length;
        var k = new double[np];
        for (int q = 0; q < m; q++)
        {
            double fq = 0.0;
            for (int j = 0; j < np; j++)
            {
                double dx = xs[q] - _pts[j][0];
                double dy = ys[q] - _pts[j][1];
                double dz = zs[q] - _pts[j][2];
                double kv = _sill * Krige3dMath.Corr(_family, Lag(dx, dy, dz, _r, _rz));
                k[j] = kv;
                fq += kv * _alpha[j];
            }
            F[q] = fq;
            var w = Krige3dMath.ForwardSub(_L, k);
            double ww = 0.0; for (int j = 0; j < np; j++) ww += w[j] * w[j];
            double var = _sill - ww;
            S[q] = Math.Sqrt(Math.Max(0.0, var));
        }
        return (F, S);
    }

    /// <summary>Evaluate F + sigma_F on the (xs1d x ys1d x zs1d) lattice
    /// (indexing 'ij': index (i,j,k) -> (xs1d[i], ys1d[j], zs1d[k])). Returns the
    /// two [nx,ny,nz] volumes, flattened C-order (i slowest, k fastest).</summary>
    public (double[] F, double[] SigmaF, int Nx, int Ny, int Nz) PredictLattice3d(
        double[] xs1d, double[] ys1d, double[] zs1d)
    {
        int nx = xs1d.Length, ny = ys1d.Length, nz = zs1d.Length;
        int m = nx * ny * nz;
        var qx = new double[m];
        var qy = new double[m];
        var qz = new double[m];
        int t = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                for (int kk = 0; kk < nz; kk++)
                {
                    qx[t] = xs1d[i]; qy[t] = ys1d[j]; qz[t] = zs1d[kk]; t++;
                }
        var (F, S) = PredictGrid3d(qx, qy, qz);
        return (F, S, nx, ny, nz);
    }

    // ---- helpers ----
    internal static double[][] NormaliseNormals(double[][] normals, int n, int dim)
    {
        double[][] rows;
        if (normals.Length == 1 && n != 1)
        {
            rows = new double[n][];
            for (int i = 0; i < n; i++) rows[i] = normals[0];
        }
        else
        {
            rows = normals;
        }
        if (rows.Length != n) throw new ArgumentException("normals must be (dim,) or (N,dim)");
        var outn = new double[n][];
        for (int i = 0; i < n; i++)
        {
            if (rows[i].Length != dim) throw new ArgumentException("normal row wrong length");
            double nn = 0.0;
            for (int c = 0; c < dim; c++) nn += rows[i][c] * rows[i][c];
            nn = Math.Max(Math.Sqrt(nn), 1e-12);
            outn[i] = new double[dim];
            for (int c = 0; c < dim; c++) outn[i][c] = rows[i][c] / nn;
        }
        return outn;
    }

    private static double[] Col(double[][] p, int c)
    {
        var a = new double[p.Length];
        for (int i = 0; i < p.Length; i++) a[i] = p[i][c];
        return a;
    }
}

/// <summary>Simple (mean-0) kriging of a 2D scalar field F(x,z) whose zero
/// level-set is the fracture SECTION. Faithful port of krige3d.KrigingField2D.</summary>
public sealed class KrigingField2D
{
    private readonly string _family;
    private readonly double[][] _pts;        // [3N,2]
    private readonly double[] _vals;
    private readonly double[] _nug;
    private readonly double[][] _L;
    private readonly double[] _alpha;
    private readonly double _sill, _r;
    private readonly int _n;

    public double Spacing { get; }
    public double H { get; }
    public double GradFloorFrac { get; }
    public double Sill => _sill;
    public double Range => _r;
    public double[][] Points => _pts;

    public KrigingField2D(double[] x, double[] z, double[][] normals,
        string family = "gaussian", double hFrac = 1.5,
        double spacing = double.NaN, double[] pointSigma = null,
        double range = double.NaN, double sill = double.NaN,
        double offsurfNugFrac = 1e-4, double gradFloorFrac = 0.2, bool fit = true)
    {
        if (x == null || z == null) throw new ArgumentNullException();
        int n = x.Length;
        if (n < 3 || z.Length != n)
            throw new ArgumentException("need >= 3 equal-length (x,z) picks");

        var p0 = new double[n][];
        for (int i = 0; i < n; i++) p0[i] = new[] { x[i], z[i] };
        var nrm = KrigingField3D.NormaliseNormals(normals, n, 2);

        _family = family;
        double s = !double.IsNaN(spacing) ? spacing : Krige3dMath.MedianNnSpacing(p0);
        Spacing = s;
        H = hFrac * s;
        GradFloorFrac = gradFloorFrac;

        _n = n;
        _pts = new double[3 * n][];
        _vals = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            _pts[i] = new[] { p0[i][0], p0[i][1] };
            _pts[n + i] = new[] { p0[i][0] + H * nrm[i][0], p0[i][1] + H * nrm[i][1] };
            _pts[2 * n + i] = new[] { p0[i][0] - H * nrm[i][0], p0[i][1] - H * nrm[i][1] };
            _vals[i] = 0.0;
            _vals[n + i] = H;
            _vals[2 * n + i] = -H;
        }

        _sill = (!double.IsNaN(sill) && sill > 0) ? sill
              : Math.Max(1e-12, Krige3dMath.VarDdof1(_vals));

        double off = offsurfNugFrac * _sill;
        _nug = new double[3 * n];
        if (pointSigma != null)
        {
            if (pointSigma.Length != n) throw new ArgumentException("pointSigma length must match n");
            for (int i = 0; i < n; i++) _nug[i] = Math.Max(pointSigma[i] * pointSigma[i], 1e-12);
        }
        else
        {
            for (int i = 0; i < n; i++) _nug[i] = off;
        }
        for (int i = n; i < 3 * n; i++) _nug[i] = off;

        double extent = Math.Max(Krige3dMath.Span(Col(_pts, 0)), Krige3dMath.Span(Col(_pts, 1)));
        if (!(extent > 0)) extent = 1.0;
        if (!double.IsNaN(range)) _r = range;
        else if (fit) _r = FitRange(extent);
        else _r = 0.25 * extent;

        _L = Factor(_r, _nug);
        _alpha = Krige3dMath.CholSolveL(_L, _vals);
    }

    private static double Lag(double dx, double dz, double r)
    {
        double u = dx / r, w = dz / r;
        return Math.Sqrt(u * u + w * w);
    }

    private double[][] Build(double r)
    {
        int m = _pts.Length;
        var K = new double[m][];
        for (int i = 0; i < m; i++)
        {
            K[i] = new double[m];
            for (int j = 0; j < m; j++)
            {
                double dx = _pts[i][0] - _pts[j][0];
                double dz = _pts[i][1] - _pts[j][1];
                K[i][j] = _sill * Krige3dMath.Corr(_family, Lag(dx, dz, r));
            }
        }
        return K;
    }

    private double[][] Factor(double r, double[] nug)
    {
        var K = Build(r);
        int m = K.Length;
        for (int i = 0; i < m; i++) K[i][i] += nug[i];
        var L = Krige3dMath.TryCholesky(K);
        double bump = 1e-9 * _sill;
        while (L == null && bump < 1e3 * _sill + 1e-6)
        {
            L = Krige3dMath.TryCholesky(AddDiag(K, bump));
            bump *= 10;
        }
        if (L == null) L = Krige3dMath.TryCholesky(AddDiag(K, 1e-3 * _sill + 1e-9));
        return L;
    }

    private static double[][] AddDiag(double[][] K, double d)
    {
        int m = K.Length;
        var A = new double[m][];
        for (int i = 0; i < m; i++) { A[i] = (double[])K[i].Clone(); A[i][i] += d; }
        return A;
    }

    private double FitRange(double extent)
    {
        double bestNll = double.PositiveInfinity, bestR = 0.25 * extent;
        double[] facs = { 0.05, 0.1, 0.15, 0.25, 0.4, 0.6, 0.9 };
        foreach (double f in facs)
        {
            double r = f * extent;
            var K = Build(r);
            int m = K.Length;
            for (int i = 0; i < m; i++) K[i][i] += _nug[i];
            var L = Krige3dMath.TryCholesky(K);
            if (L == null) continue;
            var w = Krige3dMath.ForwardSub(L, _vals);
            double quad = 0.0; for (int i = 0; i < m; i++) quad += w[i] * w[i];
            double logdet = 0.0; for (int i = 0; i < m; i++) logdet += Math.Log(L[i][i]);
            double nll = 0.5 * quad + logdet;
            if (nll < bestNll) { bestNll = nll; bestR = r; }
        }
        return bestR;
    }

    /// <summary>Vectorised (F, sigma_F). Mirrors predict_grid_2d.</summary>
    public (double[] F, double[] SigmaF) PredictGrid2d(double[] xs, double[] zs)
    {
        int m = xs.Length;
        var F = new double[m];
        var S = new double[m];
        int np = _pts.Length;
        var k = new double[np];
        for (int q = 0; q < m; q++)
        {
            double fq = 0.0;
            for (int j = 0; j < np; j++)
            {
                double dx = xs[q] - _pts[j][0];
                double dz = zs[q] - _pts[j][1];
                double kv = _sill * Krige3dMath.Corr(_family, Lag(dx, dz, _r));
                k[j] = kv;
                fq += kv * _alpha[j];
            }
            F[q] = fq;
            var w = Krige3dMath.ForwardSub(_L, k);
            double ww = 0.0; for (int j = 0; j < np; j++) ww += w[j] * w[j];
            S[q] = Math.Sqrt(Math.Max(0.0, _sill - ww));
        }
        return (F, S);
    }

    /// <summary>Evaluate F, sigma_F over the (xs1d x zs1d) lattice (indexing 'ij':
    /// index (i,k) -> (xs1d[i], zs1d[k])). Flattened C-order (i slow, k fast).</summary>
    public (double[] F, double[] SigmaF, int Nx, int Nz) PredictLattice2d(double[] xs1d, double[] zs1d)
    {
        int nx = xs1d.Length, nz = zs1d.Length;
        int m = nx * nz;
        var qx = new double[m];
        var qz = new double[m];
        int t = 0;
        for (int i = 0; i < nx; i++)
            for (int kk = 0; kk < nz; kk++) { qx[t] = xs1d[i]; qz[t] = zs1d[kk]; t++; }
        var (F, S) = PredictGrid2d(qx, qz);
        return (F, S, nx, nz);
    }

    private static double[] Col(double[][] p, int c)
    {
        var a = new double[p.Length];
        for (int i = 0; i < p.Length; i++) a[i] = p[i][c];
        return a;
    }
}

/// <summary>Phantom-free polarity assignment for a STACK of sub-parallel
/// reflectors (LEAD surprise of G13). Faithful port of krige3d.depth_layers /
/// layered_polarity_normals.</summary>
public static class PolarityNormals
{
    /// <summary>Label picks by reflector LAYER: order along base_normal, start a new
    /// layer wherever the along-normal coordinate jumps by more than layer_gap.
    /// The number of distinct labels = honest count of stacked reflector structures.</summary>
    public static int[] DepthLayers(double[][] coords, double[] baseNormal, double layerGap)
    {
        int n = coords.Length;
        int d = baseNormal.Length;
        double bn2 = 0.0; for (int c = 0; c < d; c++) bn2 += baseNormal[c] * baseNormal[c];
        double nn = Math.Max(Math.Sqrt(bn2), 1e-12);
        var bn = new double[d];
        for (int c = 0; c < d; c++) bn[c] = baseNormal[c] / nn;

        var t = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0.0; for (int c = 0; c < d; c++) s += coords[i][c] * bn[c];
            t[i] = s;
        }
        // argsort(t) -- stable, to match numpy default quicksort tie behaviour on
        // distinct values (ties are not expected on a continuous coordinate).
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => t[a].CompareTo(t[b]));

        var label = new int[n];
        int cur = 0;
        for (int q = 0; q < n - 1; q++)
        {
            int a = order[q], b = order[q + 1];
            if ((t[b] - t[a]) > layerGap) cur += 1;
            label[b] = cur;
        }
        return label;
    }

    /// <summary>Per-pick polarity normals for a stack of reflectors: depth-order,
    /// split into layers, ALTERNATE the off-surface sign per layer. Returns (N,d)
    /// signed unit normals. Mirrors layered_polarity_normals.</summary>
    public static double[][] LayeredPolarityNormals(double[][] coords, double[] baseNormal, double layerGap)
    {
        int d = baseNormal.Length;
        double bn2 = 0.0; for (int c = 0; c < d; c++) bn2 += baseNormal[c] * baseNormal[c];
        double nn = Math.Max(Math.Sqrt(bn2), 1e-12);
        var bn = new double[d];
        for (int c = 0; c < d; c++) bn[c] = baseNormal[c] / nn;

        var layer = DepthLayers(coords, bn, layerGap);
        int n = coords.Length;
        var outn = new double[n][];
        for (int i = 0; i < n; i++)
        {
            double sign = (layer[i] % 2 == 0) ? 1.0 : -1.0;
            outn[i] = new double[d];
            for (int c = 0; c < d; c++) outn[i][c] = bn[c] * sign;
        }
        return outn;
    }
}
