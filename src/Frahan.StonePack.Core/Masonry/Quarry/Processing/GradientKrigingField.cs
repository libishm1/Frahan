#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// GradientKrigingField -- Lajaunie 1997 potential-field / gradient cokriging
// (G11 route i), the PRINCIPLED successor to the signed-off-surface polarity
// trick that KrigingField3D ships (route ii, Carr 2001).
//
// Faithful C# port of pyfrahan/krige3d_grad.py (Task G17;
// research/G17_gradient_cokriging.md). Kriges a scalar potential F over R^d
// (d = 2 or 3) whose zero level-set is the fracture, from two data blocks,
// NEITHER of which invents an off-surface value:
//
//   (a) INCREMENT / interface constraints. Picks on the same fracture share one
//       unknown isovalue; the increment F(x_i) - F(x_ref) = 0 (per interface,
//       to that interface's reference pick) removes the unknown potential level
//       EXACTLY by differencing -- the honest replacement for route ii's
//       manufactured +-h shells. Per-pick position variance sigma_i^2 enters as
//       a nugget on the increment (measurement-error kriging).
//   (b) GRADIENT constraints. At each pick the unit normal constrains the field
//       gradient: grad F(x_i) = g0 * n_i (polarity + orientation), cokriged
//       through the cross-covariances between F and its partial derivatives.
//       g0 is a free gauge; g0 = 1 makes F behave like a signed distance near
//       the picks (|grad F| ~ 1).
//
// Cross-covariances (Chiles & Delfiner 2012 sec. 2; Lajaunie 1997), with
// C(h) = sill*phi(rho), rho = sqrt(sum_k (h_k/r_k)^2) (per-axis ranges r_k):
//
//   Cov(F(x),       F(x'))         =  C(h)
//   Cov(F(x),       dF(x')/dx'_b)  =  C_b(h)  = sill*(phi'(rho)/rho)*(h_b/r_b^2)
//   Cov(dF/dx_a,    dF(x')/dx'_b)  = -C_ab(h)
//   C_ab(h) = sill*[ w2(rho)*(h_a/r_a^2)(h_b/r_b^2) + (phi'(rho)/rho)*delta_ab/r_b^2 ],
//   w2(rho) = ( phi''(rho) - phi'(rho)/rho ) / rho^2.
//
// At h = 0: C_b(0) = 0 and C_ab(0) = sill*phi''(0)*delta_ab/r_b^2 (the
// phi'(rho)/rho -> phi''(0) limit). Requires phi''(0) finite (mean-square
// differentiable field): gaussian / matern15 / matern25 OK; exponential
// (nu = 1/2) is REJECTED, exactly as in the Python.
//
// Field scale gauge: sill = rel_sill * Rgeo^2 / |phi''(0)| so the prior
// derivative variance matches the unit gradient data (g0 = 1); the increment
// value nugget for a pick with position sigma_i is (g0*sigma_i)^2 = sigma_i^2.
// Only the RATIO nugget/sill matters; {F=0} and sigma_pos are gauge-invariant.
//
// Prediction: simple cokriging of the increment field F(x) = E[T(x)-T(x_ref)]
// ({F=0} is interface 0); sigma_F = sqrt(Var(T(x)-T(x_ref)) - c^T K^-1 c) with
// prior Var = 2*sill*(1 - phi(rho(x - x_ref))). GradF returns the ANALYTIC
// field gradient for the delta method sigma_pos = sigma_F/|grad F| (floor-
// guarded; no finite-difference lattice floor). Isosurface extraction reuses
// the shared clean-room MarchingCubes (same extractor as route ii).
//
// PORT DEVIATIONS (documented in research/PORT_csharp_gradient_kriging.md):
//  * The Python default range selector fit_method='cv' (K-fold CV) is NOT
//    ported: its fold construction uses np.random.default_rng(0).permutation,
//    which is not reproducible bit-exactly without porting numpy's PCG64 +
//    bounded-integer shuffle. fit defaults to false here (fixed ranges; the
//    no-fit Python default rr = 0.3*extent is reproduced); fit=true with
//    fitMethod="mll" runs the deterministic marginal-likelihood grid ported
//    from _fit_range; fitMethod="cv" throws NotSupportedException.
//  * Python None sentinels -> double.NaN / null, as in KrigingField3D.
//  * predict()'s np.linalg.solve(L, .) -> Krige3dMath.ForwardSub/CholSolveL
//    (the P2/A2 precedent; parity ~1e-13 or better).
//  * level_curves_2d (contourpy display path) is not ported; the 2D field via
//    Predict on (x,z) rows is, and MarchingSquares serves extraction.
//
// REUSE: Krige3dMath (Corr / TryCholesky / ForwardSub / CholSolveL / Span) and
// KrigingField3D.NormaliseNormals; MarchingCubes for extraction; the same
// delta-method sigma_pos as the route-ii GH path. No primitive is duplicated.
// The Krige3dMath reconcile-at-merge note in KrigingField3D.cs carries forward
// unchanged (unify with KrigingV2.cs when the branches merge).
//
// net48 / Rhino-free. Validated by the JSON parity harness against
// krige3d_grad.py (outputs/2026-07-15/krishnagiri_survey/scripts/
// csharp_gradient_parity), including the in-harness kernel-derivative
// finite-difference gate (< 1e-6, all three families).
// =============================================================================

/// <summary>1-D correlation derivatives and the spatial cross-covariances for
/// gradient cokriging. Mirrors krige3d_grad.phi/dphi/d2phi/cov_C/cov_grad/
/// cov_hess. phi delegates to Krige3dMath.Corr (the shared family).</summary>
internal static class GradKrigeKernels
{
    internal const double Tiny = 1e-12;   // krige3d_grad._TINY

    /// <summary>phi''(0) per family; finite = mean-square differentiable field.
    /// Mirrors krige3d_grad._PHI2_0. Throws for exponential / unknown.</summary>
    internal static double Phi2Zero(string family)
    {
        switch (family)
        {
            case "gaussian": return -2.0;
            case "matern15": return -3.0;
            case "matern25": return -5.0 / 3.0;
            default:
                throw new ArgumentException(
                    "family must be gaussian|matern15|matern25 (gradient cokriging "
                    + "needs a differentiable field); got '" + family + "'");
        }
    }

    /// <summary>Unit-range correlation phi(rho). Same values as Krige3dMath.Corr
    /// (which also serves the C(F,F) block); kept here so d/drho stay next to it.</summary>
    internal static double Phi(string family, double rho) => Krige3dMath.Corr(family, rho);

    /// <summary>phi'(rho): first derivative of the correlation w.r.t. the scaled lag.</summary>
    internal static double DPhi(string family, double rho)
    {
        switch (family)
        {
            case "gaussian":
                return -2.0 * rho * Math.Exp(-(rho * rho));
            case "matern15":
            {
                double a = Krige3dMath.Sqrt3 * rho;
                return -3.0 * rho * Math.Exp(-a);
            }
            case "matern25":
            {
                double a = Krige3dMath.Sqrt5 * rho;
                return -(5.0 / 3.0) * rho * (1.0 + a) * Math.Exp(-a);
            }
            case "exponential":
                throw new ArgumentException(
                    "exponential (nu=1/2) is not mean-square differentiable; "
                    + "gradient cokriging needs gaussian/matern15/matern25");
            default:
                throw new ArgumentException("unknown covariance family '" + family + "'");
        }
    }

    /// <summary>phi''(rho): second derivative of the correlation w.r.t. the scaled lag.</summary>
    internal static double D2Phi(string family, double rho)
    {
        switch (family)
        {
            case "gaussian":
                return (4.0 * rho * rho - 2.0) * Math.Exp(-(rho * rho));
            case "matern15":
            {
                double a = Krige3dMath.Sqrt3 * rho;
                return 3.0 * (a - 1.0) * Math.Exp(-a);      // 3(sqrt3 rho - 1) e^{-a}
            }
            case "matern25":
            {
                double a = Krige3dMath.Sqrt5 * rho;
                return -(5.0 / 3.0) * (1.0 + a - a * a) * Math.Exp(-a);
            }
            case "exponential":
                throw new ArgumentException(
                    "exponential (nu=1/2) has no second derivative at 0; "
                    + "gradient cokriging needs gaussian/matern15/matern25");
            default:
                throw new ArgumentException("unknown covariance family '" + family + "'");
        }
    }

    /// <summary>Anisotropy-scaled lag rho = sqrt(sum_k (h_k/r_k)^2). Mirrors _rho_of.</summary>
    internal static double RhoOf(double[] h, double[] rr)
    {
        double s = 0.0;
        for (int k = 0; k < h.Length; k++)
        {
            double u = h[k] / rr[k];
            s += u * u;
        }
        return Math.Sqrt(s);
    }

    /// <summary>C(h) = sill*phi(rho). Mirrors cov_C.</summary>
    internal static double CovC(string family, double[] h, double[] rr, double sill)
        => sill * Phi(family, RhoOf(h, rr));

    /// <summary>C_b(h) = sill*(phi'(rho)/rho)*(h_b/r_b^2) into outg (length d).
    /// phi'(rho)/rho -> phi''(0) as rho->0; at h=0, h_b=0 => C_b=0. Mirrors cov_grad.</summary>
    internal static void CovGrad(string family, double[] h, double[] rr, double sill, double[] outg)
    {
        double rho = RhoOf(h, rr);
        double ratio = rho < Tiny ? Phi2Zero(family) : DPhi(family, rho) / rho;
        double sr = sill * ratio;
        for (int b = 0; b < h.Length; b++)
            outg[b] = sr * (h[b] / (rr[b] * rr[b]));
    }

    /// <summary>C_ab(h) into outh (d x d):
    ///   C_ab = sill*[ w2(rho)*(h_a/r_a^2)(h_b/r_b^2) + (phi'/rho)*delta_ab/r_b^2 ],
    ///   w2(rho) = (phi''(rho) - phi'(rho)/rho)/rho^2.
    /// At h=0 the outer term vanishes and (phi'/rho)->phi''(0), giving the diagonal
    /// C_ab(0) = sill*phi''(0)*delta_ab/r_b^2. Mirrors cov_hess.</summary>
    internal static void CovHess(string family, double[] h, double[] rr, double sill, double[][] outh)
    {
        int d = h.Length;
        double rho = RhoOf(h, rr);
        double ratio, w2;
        if (rho < Tiny)
        {
            ratio = Phi2Zero(family);
            w2 = 0.0;
        }
        else
        {
            double dp = DPhi(family, rho);
            ratio = dp / rho;
            w2 = (D2Phi(family, rho) - dp / rho) / (rho * rho);
        }
        for (int a = 0; a < d; a++)
        {
            double hsa = h[a] / (rr[a] * rr[a]);
            for (int b = 0; b < d; b++)
            {
                double hsb = h[b] / (rr[b] * rr[b]);
                double diag = a == b ? ratio * (1.0 / (rr[b] * rr[b])) : 0.0;
                outh[a][b] = sill * (w2 * (hsa * hsb) + diag);
            }
        }
    }
}

/// <summary>Lajaunie 1997 potential-field / gradient cokriging of a scalar F over
/// R^d (d = 2 or 3) whose zero level-set is the fracture. Faithful port of
/// krige3d_grad.GradientKrigingField (see the file-header notes for formulation,
/// gauge, and the documented deviations).</summary>
public sealed class GradientKrigingField
{
    private readonly string _family;
    private readonly int _d, _n;
    private readonly double[][] _pts;        // [N,d] on-surface picks
    private readonly double[][] _normals;    // [N,d] unit gradient directions
    private readonly int[] _labels;          // [N] interface label per pick
    private readonly List<int> _uniq;        // stable first-appearance label order
    private readonly Dictionary<int, int> _ref;   // label -> reference pick index
    private readonly int _globalRef;         // interface 0's reference ({F=0})
    private readonly int[] _incA, _incB;     // increment pairs (pick, ref)
    private readonly int _nInc;
    private readonly int[] _gIdx;            // gradient-constraint pick indices
    private readonly int _nG;
    private readonly double[] _psig;         // per-pick position sigma, or null
    private readonly double _incNugFrac;
    private readonly double _gradVar;        // g0^2
    private readonly double[] _gsig;         // per-constraint grad sigma (subsampled), or null
    private readonly double _gradNugFrac;
    private readonly double[] _rr;           // per-axis ranges
    private readonly double _rgeo, _sill;
    private readonly double[][] _L;          // Cholesky factor of K + nugget diag
    private readonly double[] _w;            // dual weights K^-1 d
    private readonly double[] _cRefInc;      // C(xr-Pa_i) - C(xr-Pb_i), precomputed
    private readonly double[] _cRefGrad;     // C_b(Pg_k - xr), flattened k*d+b

    public double G0 { get; }
    public double GradFloorFrac { get; }
    public string Family => _family;
    public int Dim => _d;
    public double Sill => _sill;
    public double GeoMeanRange => _rgeo;
    public double[] Ranges => (double[])_rr.Clone();
    public int IncrementCount => _nInc;
    public int GradientCount => _nG;
    public double[][] Points => _pts;

    /// <param name="coords">(N,d) on-surface pick coordinates, d = 2 or 3.</param>
    /// <param name="normals">Gradient directions: a single (d,) row (broadcast) or
    /// (N,d) per-pick normals (G12 local_plane_normals). Unit-normalised; the
    /// constraint is grad F(x_i) = g0*n_i.</param>
    /// <param name="interfaceLabels">(N,) int label grouping picks into interfaces
    /// (same-surface picks share an isovalue). null = one interface.</param>
    /// <param name="family">gaussian (default) | matern15 | matern25. exponential
    /// is rejected (not mean-square differentiable).</param>
    /// <param name="range">Scalar isotropic range (NaN = not given).</param>
    /// <param name="ranges">(d,) per-axis anisotropic ranges (overrides range).</param>
    /// <param name="relSill">sill = relSill * Rgeo^2 / |phi''(0)| (unit-gradient gauge).</param>
    /// <param name="sill">Explicit sill override (NaN = use the gauge).</param>
    /// <param name="pointSigma">(N,) per-pick position sigma; increment nugget
    /// (g0*sigma_i)^2 + (g0*sigma_ref)^2.</param>
    /// <param name="incNugFrac">Increment nugget as a sill fraction when pointSigma is null.</param>
    /// <param name="gradNugFrac">Gradient nugget as a fraction of the gradient variance g0^2.</param>
    /// <param name="gradSigma">(N,) per-pick orientation-noise std on the gradient
    /// data; overrides gradNugFrac (nugget = gradSigma^2).</param>
    /// <param name="gradSubsample">Keep every k-th pick's gradient constraint (>= 1).</param>
    /// <param name="refIndex">Reference pick index per interface label; default =
    /// the pick nearest the interface centroid.</param>
    /// <param name="fit">Select the isotropic range when range/ranges are not given.
    /// DEVIATION: defaults to false (Python defaults to true with the un-ported CV);
    /// false reproduces the Python no-fit default rr = 0.3*extent.</param>
    /// <param name="fitMethod">"mll" (ported, deterministic marginal-likelihood grid)
    /// or "cv" (NOT PORTED -> NotSupportedException; see the deviation register).</param>
    /// <param name="g0">Gradient-magnitude gauge (default 1; F ~ signed distance).</param>
    /// <param name="gradFloorFrac">|grad F| floor for the delta method as a fraction
    /// of the median vertex |grad F| (G11 4.3 L1). Default 0.2.</param>
    public GradientKrigingField(double[][] coords, double[][] normals,
        int[] interfaceLabels = null, string family = "gaussian",
        double range = double.NaN, double[] ranges = null,
        double relSill = 1.0, double sill = double.NaN,
        double[] pointSigma = null, double incNugFrac = 1e-6,
        double gradNugFrac = 1e-4, double[] gradSigma = null,
        int gradSubsample = 1, IDictionary<int, int> refIndex = null,
        bool fit = false, string fitMethod = "mll",
        double g0 = 1.0, double gradFloorFrac = 0.2)
    {
        if (coords == null || normals == null) throw new ArgumentNullException();
        int n = coords.Length;
        int d = n > 0 ? coords[0].Length : 0;
        if (n < 3 || (d != 2 && d != 3))
            throw new ArgumentException("coords must be (N,d) with N>=3 and d in {2,3}");
        for (int i = 0; i < n; i++)
            if (coords[i] == null || coords[i].Length != d)
                throw new ArgumentException("coords rows must all have length " + d);
        _n = n;
        _d = d;
        _pts = new double[n][];
        for (int i = 0; i < n; i++) _pts[i] = (double[])coords[i].Clone();

        GradKrigeKernels.Phi2Zero(family);   // validates the family (throws otherwise)
        _family = family;
        G0 = g0;
        GradFloorFrac = gradFloorFrac;

        // unit gradient directions ((d,) broadcast handled by the shared helper)
        _normals = KrigingField3D.NormaliseNormals(normals, n, d);

        // ---- interface labels + per-interface reference pick ----
        if (interfaceLabels == null)
        {
            _labels = new int[n];
        }
        else
        {
            if (interfaceLabels.Length != n)
                throw new ArgumentException("interface length must match n");
            _labels = (int[])interfaceLabels.Clone();
        }
        _uniq = new List<int>();
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
            if (seen.Add(_labels[i])) _uniq.Add(_labels[i]);
        _ref = new Dictionary<int, int>();
        foreach (int g in _uniq)
        {
            var mem = Members(g);
            int r;
            if (refIndex != null && refIndex.TryGetValue(g, out int ri))
            {
                r = ri;
            }
            else
            {
                var c = new double[d];
                foreach (int i in mem)
                    for (int k = 0; k < d; k++) c[k] += _pts[i][k];
                for (int k = 0; k < d; k++) c[k] /= mem.Count;
                double best = double.PositiveInfinity;
                r = mem[0];
                foreach (int i in mem)
                {
                    double s2 = 0.0;
                    for (int k = 0; k < d; k++)
                    {
                        double diff = _pts[i][k] - c[k];
                        s2 += diff * diff;
                    }
                    if (s2 < best) { best = s2; r = i; }   // first argmin (numpy rule)
                }
            }
            _ref[g] = r;
        }
        _globalRef = _ref[_uniq[0]];          // {F=0} = interface 0

        // ---- increment pairs (a = pick, b = ref) within each interface ----
        var aIdx = new List<int>();
        var bIdx = new List<int>();
        foreach (int g in _uniq)
        {
            int r = _ref[g];
            foreach (int i in Members(g))
                if (i != r) { aIdx.Add(i); bIdx.Add(r); }
        }
        _incA = aIdx.ToArray();
        _incB = bIdx.ToArray();
        _nInc = _incA.Length;

        // ---- gradient locations (optionally subsampled) ----
        int ss = Math.Max(1, gradSubsample);
        var gi = new List<int>();
        for (int i = 0; i < n; i += ss) gi.Add(i);
        if (gi.Count == 0) gi.Add(0);
        _gIdx = gi.ToArray();
        _nG = _gIdx.Length;

        // ---- nuggets ----
        if (pointSigma != null)
        {
            if (pointSigma.Length != n) throw new ArgumentException("pointSigma length must match n");
            _psig = new double[n];
            for (int i = 0; i < n; i++) _psig[i] = Math.Max(pointSigma[i], 0.0);
        }
        _incNugFrac = incNugFrac;
        _gradVar = g0 * g0;    // gradient variance under the gauge
        if (gradSigma != null)
        {
            if (gradSigma.Length != n) throw new ArgumentException("gradSigma length must match n");
            _gsig = new double[_nG];
            for (int k = 0; k < _nG; k++) _gsig[k] = gradSigma[_gIdx[k]];
        }
        _gradNugFrac = gradNugFrac;

        // ---- ranges + sill gauge ----
        double extent = 0.0;
        for (int k = 0; k < d; k++)
            extent = Math.Max(extent, Krige3dMath.Span(Col(_pts, k)));
        if (extent == 0.0) extent = 1.0;      // Python `max(...) or 1.0`
        double[] rr;
        if (ranges != null)
        {
            if (ranges.Length != d) throw new ArgumentException("ranges must be length " + d);
            rr = (double[])ranges.Clone();
        }
        else if (!double.IsNaN(range))
        {
            rr = Full(d, range);
        }
        else if (fit)
        {
            if (fitMethod == "mll")
                rr = Full(d, FitRangeMll(extent));
            else if (fitMethod == "cv")
                throw new NotSupportedException(
                    "fit_method='cv' is NOT ported: the Python fold construction uses "
                    + "np.random.default_rng(0).permutation (numpy PCG64), which is not "
                    + "reproducible bit-exactly here. Pass fixed range/ranges, or "
                    + "fitMethod=\"mll\". See research/PORT_csharp_gradient_kriging.md.");
            else
                throw new ArgumentException("fitMethod must be 'cv' or 'mll'");
        }
        else
        {
            rr = Full(d, 0.3 * extent);
        }
        _rr = rr;
        double logSum = 0.0;
        for (int k = 0; k < d; k++) logSum += Math.Log(Math.Max(rr[k], GradKrigeKernels.Tiny));
        _rgeo = Math.Exp(logSum / d);          // geometric-mean range
        _sill = (!double.IsNaN(sill) && sill > 0) ? sill
              : relSill * _rgeo * _rgeo / Math.Abs(GradKrigeKernels.Phi2Zero(family));

        // ---- assemble + factor the cokriging system, solve the dual weights ----
        var K = BuildK(_rr, _sill);
        var nug = NuggetVector();
        int m = K.Length;
        for (int i = 0; i < m; i++) K[i][i] += nug[i];
        _L = Factor(K);
        _w = Krige3dMath.CholSolveL(_L, DataVector());

        // ---- reference-pick cross terms (query-independent; used by Cross) ----
        _cRefInc = new double[_nInc];
        var xr = _pts[_globalRef];
        var h1 = new double[d];
        var h2 = new double[d];
        for (int i = 0; i < _nInc; i++)
        {
            Sub(xr, _pts[_incA[i]], h1);
            Sub(xr, _pts[_incB[i]], h2);
            _cRefInc[i] = GradKrigeKernels.CovC(_family, h1, _rr, _sill)
                        - GradKrigeKernels.CovC(_family, h2, _rr, _sill);
        }
        _cRefGrad = new double[_nG * d];
        var gbuf = new double[d];
        for (int k = 0; k < _nG; k++)
        {
            Sub(_pts[_gIdx[k]], xr, h1);       // Pg_k - x_ref
            GradKrigeKernels.CovGrad(_family, h1, _rr, _sill, gbuf);
            for (int b = 0; b < d; b++) _cRefGrad[k * d + b] = gbuf[b];
        }
    }

    // ------------------------------------------------------------------ assembly
    private List<int> Members(int label)
    {
        var mem = new List<int>();
        for (int i = 0; i < _n; i++) if (_labels[i] == label) mem.Add(i);
        return mem;
    }

    /// <summary>Increment nugget vector (variance units): (g0*sigma_a)^2 +
    /// (g0*sigma_ref)^2 per increment, else 2*incNugFrac*sill. Mirrors _incnug.</summary>
    private double[] IncNug()
    {
        var nug = new double[_nInc];
        if (_psig != null)
        {
            for (int q = 0; q < _nInc; q++)
            {
                double va = G0 * _psig[_incA[q]];
                double vb = G0 * _psig[_incB[q]];
                nug[q] = va * va + vb * vb;
            }
        }
        else
        {
            double v = 2.0 * _incNugFrac * _sill;
            for (int q = 0; q < _nInc; q++) nug[q] = v;
        }
        for (int q = 0; q < _nInc; q++) nug[q] = Math.Max(nug[q], GradKrigeKernels.Tiny);
        return nug;
    }

    /// <summary>Gradient nugget per constraint. Mirrors _gradnug.</summary>
    private double[] GradNug()
    {
        var nug = new double[_nG];
        if (_gsig != null)
        {
            for (int k = 0; k < _nG; k++)
                nug[k] = Math.Max(_gsig[k] * _gsig[k], GradKrigeKernels.Tiny);
        }
        else
        {
            double v = _gradNugFrac * _gradVar + GradKrigeKernels.Tiny;
            for (int k = 0; k < _nG; k++) nug[k] = v;
        }
        return nug;
    }

    /// <summary>concat(IncNug, repeat-each(GradNug, d)) -- the K diagonal add.</summary>
    private double[] NuggetVector()
    {
        var inc = IncNug();
        var gr = GradNug();
        var nug = new double[_nInc + _nG * _d];
        Array.Copy(inc, nug, _nInc);
        for (int k = 0; k < _nG; k++)
            for (int b = 0; b < _d; b++) nug[_nInc + k * _d + b] = gr[k];
        return nug;
    }

    /// <summary>Full cokriging Gram [inc-inc, inc-grad; grad-inc, grad-grad]
    /// (no nugget). Mirrors _build_K; block layout identical (grad column
    /// index = nInc + k*d + b, i.e. constraint-major, component-minor).</summary>
    private double[][] BuildK(double[] rr, double sill)
    {
        int d = _d, ni = _nInc, ng = _nG;
        int m = ni + ng * d;
        var K = new double[m][];
        for (int i = 0; i < m; i++) K[i] = new double[m];
        var h = new double[d];
        var gA = new double[d];
        var gB = new double[d];

        // inc-inc: C(a-a') - C(a-b') - C(b-a') + C(b-b')
        for (int i = 0; i < ni; i++)
        {
            double[] pa = _pts[_incA[i]], pb = _pts[_incB[i]];
            for (int j = 0; j < ni; j++)
            {
                double[] qa = _pts[_incA[j]], qb = _pts[_incB[j]];
                Sub(pa, qa, h); double t1 = GradKrigeKernels.CovC(_family, h, rr, sill);
                Sub(pa, qb, h); double t2 = GradKrigeKernels.CovC(_family, h, rr, sill);
                Sub(pb, qa, h); double t3 = GradKrigeKernels.CovC(_family, h, rr, sill);
                Sub(pb, qb, h); double t4 = GradKrigeKernels.CovC(_family, h, rr, sill);
                K[i][j] = ((t1 - t2) - t3) + t4;
            }
        }

        // inc-grad: C_b(Pg_k - a_i) - C_b(Pg_k - b_i); symmetric transpose block
        for (int i = 0; i < ni; i++)
        {
            double[] pa = _pts[_incA[i]], pb = _pts[_incB[i]];
            for (int k = 0; k < ng; k++)
            {
                double[] pg = _pts[_gIdx[k]];
                Sub(pg, pa, h); GradKrigeKernels.CovGrad(_family, h, rr, sill, gA);
                Sub(pg, pb, h); GradKrigeKernels.CovGrad(_family, h, rr, sill, gB);
                for (int b = 0; b < d; b++)
                {
                    double v = gA[b] - gB[b];
                    K[i][ni + k * d + b] = v;
                    K[ni + k * d + b][i] = v;
                }
            }
        }

        // grad-grad: -C_ab(Pg_k - Pg_j) at row (j,a), col (k,b)
        var hess = new double[d][];
        for (int a = 0; a < d; a++) hess[a] = new double[d];
        for (int j = 0; j < ng; j++)
        {
            double[] pj = _pts[_gIdx[j]];
            for (int k = 0; k < ng; k++)
            {
                double[] pk = _pts[_gIdx[k]];
                Sub(pk, pj, h);                       // Pg_k - Pg_j
                GradKrigeKernels.CovHess(_family, h, rr, sill, hess);
                for (int a = 0; a < d; a++)
                    for (int b = 0; b < d; b++)
                        K[ni + j * d + a][ni + k * d + b] = -hess[a][b];
            }
        }
        return K;
    }

    /// <summary>Cholesky with the nugget-bump ladder (K already carries the
    /// nugget diagonal). Mirrors _factor.</summary>
    private double[][] Factor(double[][] K)
    {
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

    /// <summary>d = [ increments (all 0) ; gradient components g0*n_k ]. Mirrors
    /// _data_vector.</summary>
    private double[] DataVector()
    {
        var dv = new double[_nInc + _nG * _d];
        for (int k = 0; k < _nG; k++)
        {
            double[] nk = _normals[_gIdx[k]];
            for (int b = 0; b < _d; b++) dv[_nInc + k * _d + b] = G0 * nk[b];
        }
        return dv;
    }

    /// <summary>Deterministic marginal-likelihood range grid, ported from
    /// _fit_range (kept for the ablation; UNINFORMATIVE for pure
    /// increment(=0)+gradient data -- prefer fixed ranges; the Python 'cv'
    /// default is not ported, see the deviation register).</summary>
    private double FitRangeMll(double extent)
    {
        double bestNll = double.PositiveInfinity, bestR = 0.3 * extent;
        double[] facs = { 0.1, 0.18, 0.28, 0.4, 0.6, 0.85 };
        foreach (double f in facs)
        {
            double r = f * extent;
            var rr = Full(_d, r);
            double sill = r * r / Math.Abs(GradKrigeKernels.Phi2Zero(_family));
            // temporary nuggets (sill-relative + point sigma) for the likelihood
            var incn = new double[_nInc];
            if (_psig != null)
            {
                for (int q = 0; q < _nInc; q++)
                {
                    double va = G0 * _psig[_incA[q]];
                    double vb = G0 * _psig[_incB[q]];
                    incn[q] = Math.Max(va * va + vb * vb, GradKrigeKernels.Tiny);
                }
            }
            else
            {
                double v = 2.0 * _incNugFrac * sill;
                for (int q = 0; q < _nInc; q++) incn[q] = v;
            }
            double gvar = G0 * G0;
            double gn = _gradNugFrac * gvar + GradKrigeKernels.Tiny;
            var K = BuildK(rr, sill);
            for (int q = 0; q < _nInc; q++) K[q][q] += incn[q];
            for (int k = 0; k < _nG * _d; k++) K[_nInc + k][_nInc + k] += gn;
            var L = Krige3dMath.TryCholesky(K);
            if (L == null) continue;
            var dv = DataVector();
            var wsol = Krige3dMath.ForwardSub(L, dv);
            double quad = 0.0;
            for (int i = 0; i < wsol.Length; i++) quad += wsol[i] * wsol[i];
            double logdet = 0.0;
            for (int i = 0; i < L.Length; i++) logdet += Math.Log(L[i][i]);
            double nll = 0.5 * quad + logdet;
            if (nll < bestNll) { bestNll = nll; bestR = r; }
        }
        return bestR;
    }

    // ---------------------------------------------------------------- prediction
    /// <summary>Cross-covariance row c(x) between the increment field
    /// F(x) = T(x) - T(x_ref) and all data, into c (length nInc + nG*d).
    /// Mirrors _cross (reference terms precomputed; numerically identical).</summary>
    private void Cross(double[] q, double[] c)
    {
        int d = _d;
        var h = new double[d];
        var g = new double[d];
        for (int i = 0; i < _nInc; i++)
        {
            Sub(q, _pts[_incA[i]], h);
            double ca = GradKrigeKernels.CovC(_family, h, _rr, _sill);
            Sub(q, _pts[_incB[i]], h);
            double cb = GradKrigeKernels.CovC(_family, h, _rr, _sill);
            c[i] = (ca - cb) - _cRefInc[i];
        }
        for (int k = 0; k < _nG; k++)
        {
            Sub(_pts[_gIdx[k]], q, h);            // Pg_k - x
            GradKrigeKernels.CovGrad(_family, h, _rr, _sill, g);
            for (int b = 0; b < d; b++)
                c[_nInc + k * d + b] = g[b] - _cRefGrad[k * d + b];
        }
    }

    /// <summary>(F, sigma_F) over query rows (m,d). F(x) = E[T(x) - T(x_ref)];
    /// {F=0} is interface 0. sigma_F = sqrt(Var(T(x)-T(x_ref)) - c^T K^-1 c),
    /// prior Var = 2*sill*(1 - phi(rho(x - x_ref))). withSigma=false returns
    /// (F, zeros) and skips the per-query triangular solve. Mirrors predict.</summary>
    public (double[] F, double[] SigmaF) Predict(double[][] coords, bool withSigma = true)
    {
        int m = coords.Length;
        int nd = _nInc + _nG * _d;
        var F = new double[m];
        var S = new double[m];
        var c = new double[nd];
        var h = new double[_d];
        var xr = _pts[_globalRef];
        for (int q = 0; q < m; q++)
        {
            Cross(coords[q], c);
            double f = 0.0;
            for (int j = 0; j < nd; j++) f += c[j] * _w[j];
            F[q] = f;
            if (withSigma)
            {
                Sub(coords[q], xr, h);
                double rho = GradKrigeKernels.RhoOf(h, _rr);
                double prior = 2.0 * _sill * (1.0 - GradKrigeKernels.Phi(_family, rho));
                var wv = Krige3dMath.ForwardSub(_L, c);
                double ww = 0.0;
                for (int j = 0; j < nd; j++) ww += wv[j] * wv[j];
                S[q] = Math.Sqrt(Math.Max(0.0, prior - ww));
            }
        }
        return (F, S);
    }

    /// <summary>KrigingField3D.PredictGrid3d-parity wrapper: (F, sigma_F) over
    /// flattened coordinate arrays (3D fields only). Mirrors predict_grid_3d.</summary>
    public (double[] F, double[] SigmaF) PredictGrid3d(double[] xs, double[] ys, double[] zs,
        bool withSigma = true)
    {
        if (_d != 3) throw new InvalidOperationException("PredictGrid3d needs a 3D field");
        int m = xs.Length;
        var Q = new double[m][];
        for (int i = 0; i < m; i++) Q[i] = new[] { xs[i], ys[i], zs[i] };
        return Predict(Q, withSigma);
    }

    /// <summary>Evaluate F + sigma_F on the (xs1d x ys1d x zs1d) lattice
    /// (indexing 'ij', C-order flat: i slowest, k fastest) -- the same
    /// convention as KrigingField3D.PredictLattice3d, for MarchingCubes.</summary>
    public (double[] F, double[] SigmaF, int Nx, int Ny, int Nz) PredictLattice3d(
        double[] xs1d, double[] ys1d, double[] zs1d, bool withSigma = true)
    {
        if (_d != 3) throw new InvalidOperationException("PredictLattice3d needs a 3D field");
        int nx = xs1d.Length, ny = ys1d.Length, nz = zs1d.Length;
        int m = nx * ny * nz;
        var Q = new double[m][];
        int t = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                    Q[t++] = new[] { xs1d[i], ys1d[j], zs1d[k] };
        var (F, S) = Predict(Q, withSigma);
        return (F, S, nx, ny, nz);
    }

    /// <summary>Analytic gradient of the predicted field, grad E[F(x)] =
    /// sum_j w_j * d/dx c_j(x). Returns (m,d). Used for the delta-method
    /// |grad F| (exact; no finite-difference lattice floor). Mirrors grad_F.</summary>
    public double[][] GradF(double[][] coords)
    {
        int m = coords.Length, d = _d;
        var G = new double[m][];
        var h = new double[d];
        var gA = new double[d];
        var gB = new double[d];
        var hess = new double[d][];
        for (int a = 0; a < d; a++) hess[a] = new double[d];
        for (int q = 0; q < m; q++)
        {
            var g = new double[d];
            double[] Qc = coords[q];
            // d/dx of the inc cross: C_grad(x - a_i) - C_grad(x - b_i), weighted
            for (int i = 0; i < _nInc; i++)
            {
                Sub(Qc, _pts[_incA[i]], h);
                GradKrigeKernels.CovGrad(_family, h, _rr, _sill, gA);
                Sub(Qc, _pts[_incB[i]], h);
                GradKrigeKernels.CovGrad(_family, h, _rr, _sill, gB);
                double wi = _w[i];
                for (int a = 0; a < d; a++) g[a] += (gA[a] - gB[a]) * wi;
            }
            // d/dx of the grad cross: d/dx_alpha C_beta(Pg_k - x) = -C_{beta,alpha}
            for (int k = 0; k < _nG; k++)
            {
                Sub(_pts[_gIdx[k]], Qc, h);        // Pg_k - x
                GradKrigeKernels.CovHess(_family, h, _rr, _sill, hess);
                for (int b = 0; b < d; b++)
                {
                    double wkb = _w[_nInc + k * d + b];
                    for (int a = 0; a < d; a++) g[a] -= hess[b][a] * wkb;
                }
            }
            G[q] = g;
        }
        return G;
    }

    // -------------------------------------------------------------- extraction
    /// <summary>Predicted F-level of each interface's reference pick (interface
    /// 0 -> 0 up to kriging residual). Mirrors interface_levels.</summary>
    public Dictionary<int, double> InterfaceLevels()
    {
        int ng = _uniq.Count;
        var refs = new double[ng][];
        for (int i = 0; i < ng; i++) refs[i] = _pts[_ref[_uniq[i]]];
        var (lv, _) = Predict(refs, withSigma: false);
        var outd = new Dictionary<int, double>();
        for (int i = 0; i < ng; i++) outd[_uniq[i]] = lv[i];
        return outd;
    }

    // ------------------------------------------------------------------ helpers
    private static void Sub(double[] a, double[] b, double[] outh)
    {
        for (int k = 0; k < outh.Length; k++) outh[k] = a[k] - b[k];
    }

    private static double[] Full(int d, double v)
    {
        var a = new double[d];
        for (int k = 0; k < d; k++) a[k] = v;
        return a;
    }

    private static double[] Col(double[][] p, int c)
    {
        var a = new double[p.Length];
        for (int i = 0; i < p.Length; i++) a[i] = p[i][c];
        return a;
    }
}
