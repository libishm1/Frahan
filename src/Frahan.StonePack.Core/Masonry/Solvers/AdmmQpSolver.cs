#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// AdmmQpSolver — robust pure-managed convex-QP solver (OSQP-style ADMM),
// evolution P1 of EVOLUTION_PLAN_MASONRY.md (2026-06-10); P1.1 sparse rewrite.
//
// Motivation: the Dykstra alternating-projection ManagedQpSolver diverges on
// real masonry RBE systems (mixed force/moment row scales push the projection
// residuals to ~1e60 — the "Dykstra convergence tail" flagged by the V3 SLM
// review). This solver replaces it for the stability pipeline.
//
// Method (Stellato et al. 2020, "OSQP: an operator splitting solver for
// quadratic programs", Math. Prog. Comp. 12:637-672 — simplified form):
//
//   min ½ xᵀPx + qᵀx   s.t.  l <= A x <= u
//
// with all constraint blocks stacked into one A:
//   * equality rows      l = u = beq
//   * inequality rows    l = -inf, u = bineq
//   * box-bound rows     identity, l = lb, u = ub
//
// Iteration k:
//   x̃   = (P + σI + ρAᵀA)⁻¹ (σx − q + Aᵀ(ρz − y))         (cached Cholesky)
//   x⁺  = α x̃ + (1−α) x
//   z⁺  = Π_[l,u]( α A x̃ + (1−α) z + y/ρ )
//   y⁺  = y + ρ( α A x̃ + (1−α) z − z⁺ )
// Converged when ‖Ax−z‖∞ <= eps_pri and ‖Px+q+Aᵀy‖∞ <= eps_dua (abs+rel).
// Adaptive ρ (refactorises when the primal/dual residual ratio drifts) and
// Ruiz-lite row equilibration keep the mixed-scale masonry rows tame.
//
// P1.1 (sparse rewrite, 2026-06-10): A is stored CSR, so every matvec /
// transpose-matvec is O(nnz) instead of O(m·n) — the masonry constraint
// blocks are >99% sparse (each equilibrium row touches one block's contacts;
// each friction row touches one vertex's 3 columns; bound rows are identity).
// Diagonal Hessians (all masonry formulations) get an O(n) Px fast path.
// The 40-stone wall check drops from minutes (dense) to seconds.
// =============================================================================

/// <summary>
/// OSQP-style ADMM solver for the masonry RBE/penalty QPs. Pure managed,
/// CSR-sparse constraint storage (suitable for wall/vault-scale assemblies).
/// Registered name: "AdmmQpSolver".
/// </summary>
public sealed class AdmmQpSolver : IConvexQpSolver
{
    private readonly double _sigma;
    private readonly double _alpha;
    private readonly double _epsAbs;
    private readonly double _epsRel;
    private readonly int _maxIterations;

    public AdmmQpSolver(
        double epsAbs = 1e-6, double epsRel = 1e-5,
        int maxIterations = 8000, double sigma = 1e-6, double alpha = 1.6)
    {
        if (epsAbs <= 0) throw new ArgumentOutOfRangeException(nameof(epsAbs));
        if (epsRel < 0) throw new ArgumentOutOfRangeException(nameof(epsRel));
        if (maxIterations < 10) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (sigma <= 0) throw new ArgumentOutOfRangeException(nameof(sigma));
        if (!(alpha > 0 && alpha < 2)) throw new ArgumentOutOfRangeException(nameof(alpha));
        _epsAbs = epsAbs; _epsRel = epsRel; _maxIterations = maxIterations;
        _sigma = sigma; _alpha = alpha;
    }

    public string Name => "AdmmQpSolver";

    public ConvexQpResult Solve(ConvexQpProblem problem)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        int n = problem.VariableCount;

        // ---- Stack (equality / inequality / bounds) into one CSR (A, l, u),
        //      with Ruiz-lite row equilibration applied during assembly. ----
        var a = BuildCsr(problem, n, out double[] lo, out double[] hi);
        int m = a.RowCount;

        var q = new double[n];
        if (problem.LinearObjective != null)
            for (int i = 0; i < n; i++) q[i] = problem.LinearObjective[i];

        // ---- Hessian: O(n) diagonal fast path when applicable. ----
        double[] pDiag = ExtractDiagonal(problem.Hessian, n);

        // ---- ADMM state. ----
        var x = new double[n];
        var z = new double[m];
        var y = new double[m];
        var ax = new double[m];
        var axTilde = new double[m];
        var rhs = new double[n];
        var xTilde = new double[n];
        var px = new double[n];
        var aty = new double[n];

        // Per-row rho (OSQP sec. 5.2): equality rows (l == u) get 1e3 x the base
        // rho — they must hold exactly, and stiffening them slashes iterations.
        double rho = 0.1;
        var rhoRow = new double[m];
        var isEq = new bool[m];
        for (int r = 0; r < m; r++) isEq[r] = lo[r] == hi[r] && !double.IsInfinity(lo[r]);
        UpdateRhoRows(rhoRow, isEq, rho);
        double[,] chol = Factor(problem.Hessian, pDiag, a, n, rhoRow, _sigma);
        if (chol == null)
            return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
                "Cholesky factorisation failed (P + sigma*I + rho*A'A not SPD).");

        double lastRPri = double.NaN, lastRDua = double.NaN, lastEpsPri = double.NaN, lastEpsDua = double.NaN;
        int refactors = 0;
        for (int it = 1; it <= _maxIterations; it++)
        {
            // rhs = sigma*x - q + A'(rho*z - y)
            for (int c = 0; c < n; c++) rhs[c] = _sigma * x[c] - q[c];
            a.TransposeMulAccumulate(z, y, rhoRow, rhs);
            CholSolve(chol, rhs, xTilde, n);

            a.Mul(xTilde, axTilde);
            for (int c = 0; c < n; c++) x[c] = _alpha * xTilde[c] + (1 - _alpha) * x[c];
            for (int r = 0; r < m; r++)
            {
                double zRelaxed = _alpha * axTilde[r] + (1 - _alpha) * z[r];
                double zNew = Clamp(zRelaxed + y[r] / rhoRow[r], lo[r], hi[r]);
                y[r] += rhoRow[r] * (zRelaxed - zNew);
                z[r] = zNew;
            }

            if (it % 10 != 0 && it != _maxIterations) continue;

            // ---- residuals (all sparse / O(n)) ----
            a.Mul(x, ax);
            double rPri = 0, axN = 0, zN = 0;
            for (int r = 0; r < m; r++)
            {
                double v = Math.Abs(ax[r] - z[r]); if (v > rPri) rPri = v;
                double v2 = Math.Abs(ax[r]); if (v2 > axN) axN = v2;
                double v3 = Math.Abs(z[r]); if (v3 > zN) zN = v3;
            }
            MulHessian(problem.Hessian, pDiag, x, px, n);
            a.TransposeMul(y, aty);
            double rDua = 0, pxN = 0, atyN = 0, qN = 0;
            for (int c = 0; c < n; c++)
            {
                double v = Math.Abs(px[c] + q[c] + aty[c]); if (v > rDua) rDua = v;
                if (Math.Abs(px[c]) > pxN) pxN = Math.Abs(px[c]);
                if (Math.Abs(aty[c]) > atyN) atyN = Math.Abs(aty[c]);
                if (Math.Abs(q[c]) > qN) qN = Math.Abs(q[c]);
            }
            double epsPri = _epsAbs + _epsRel * Math.Max(axN, zN);
            double epsDua = _epsAbs + _epsRel * Math.Max(Math.Max(pxN, atyN), qN);
            lastRPri = rPri; lastRDua = rDua; lastEpsPri = epsPri; lastEpsDua = epsDua;

            if (rPri <= epsPri && rDua <= epsDua)
            {
                double obj = Objective(problem.Hessian, pDiag, q, x, n);
                return new ConvexQpResult(ConvexQpStatus.Optimal, (double[])x.Clone(), obj,
                    $"ADMM converged in {it} iterations (rho={rho:0.###}, refactors={refactors}, " +
                    $"r_pri={rPri:0.###e0}, r_dua={rDua:0.###e0}).");
            }

            // ---- adaptive rho (bounded refactorisations) ----
            if (it % 100 == 0 && refactors < 10)
            {
                double ratio = (rPri / Math.Max(epsPri, 1e-300)) /
                               Math.Max(rDua / Math.Max(epsDua, 1e-300), 1e-300);
                double newRho = rho;
                if (ratio > 10) newRho = rho * 5;
                else if (ratio < 0.1) newRho = rho / 5;
                newRho = Math.Max(1e-6, Math.Min(1e6, newRho));
                if (Math.Abs(newRho - rho) / rho > 1e-9)
                {
                    rho = newRho; refactors++;
                    UpdateRhoRows(rhoRow, isEq, rho);
                    chol = Factor(problem.Hessian, pDiag, a, n, rhoRow, _sigma);
                    if (chol == null)
                        return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
                            "Cholesky refactorisation failed after rho update.");
                }
            }
        }

        return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
            $"ADMM did not converge in {_maxIterations} iterations " +
            $"(rho={rho:0.###e0}, r_pri={lastRPri:0.###e0}/eps {lastEpsPri:0.###e0}, " +
            $"r_dua={lastRDua:0.###e0}/eps {lastEpsDua:0.###e0}).");
    }

    // =========================================================================
    // CSR assembly (equality + inequality + bound-identity rows), row-equilibrated.
    // =========================================================================
    private static Csr BuildCsr(ConvexQpProblem problem, int n, out double[] lo, out double[] hi)
    {
        int meq = problem.EqualityMatrix != null ? problem.EqualityMatrix.GetLength(0) : 0;
        int mineq = problem.InequalityMatrix != null ? problem.InequalityMatrix.GetLength(0) : 0;
        int m = meq + mineq + n;
        lo = new double[m]; hi = new double[m];

        // count nnz (dense-source blocks: count non-zeros)
        int nnz = n; // identity block
        for (int i = 0; i < meq; i++)
            for (int c = 0; c < n; c++) if (problem.EqualityMatrix[i, c] != 0) nnz++;
        for (int i = 0; i < mineq; i++)
            for (int c = 0; c < n; c++) if (problem.InequalityMatrix[i, c] != 0) nnz++;

        var rowPtr = new int[m + 1];
        var colIdx = new int[nnz];
        var vals = new double[nnz];
        int k = 0;
        for (int i = 0; i < meq; i++)
        {
            rowPtr[i] = k;
            double rmax = 0;
            for (int c = 0; c < n; c++) { double v = Math.Abs(problem.EqualityMatrix[i, c]); if (v > rmax) rmax = v; }
            double e = rmax > 1e-300 ? 1.0 / rmax : 1.0;
            for (int c = 0; c < n; c++)
            {
                double v = problem.EqualityMatrix[i, c];
                if (v != 0) { colIdx[k] = c; vals[k] = v * e; k++; }
            }
            lo[i] = problem.EqualityRhs[i] * e; hi[i] = lo[i];
        }
        for (int i = 0; i < mineq; i++)
        {
            int r = meq + i;
            rowPtr[r] = k;
            double rmax = 0;
            for (int c = 0; c < n; c++) { double v = Math.Abs(problem.InequalityMatrix[i, c]); if (v > rmax) rmax = v; }
            double e = rmax > 1e-300 ? 1.0 / rmax : 1.0;
            for (int c = 0; c < n; c++)
            {
                double v = problem.InequalityMatrix[i, c];
                if (v != 0) { colIdx[k] = c; vals[k] = v * e; k++; }
            }
            lo[r] = double.NegativeInfinity; hi[r] = problem.InequalityRhs[i] * e;
        }
        for (int i = 0; i < n; i++)
        {
            int r = meq + mineq + i;
            rowPtr[r] = k;
            colIdx[k] = i; vals[k] = 1.0; k++;
            lo[r] = problem.LowerBounds != null ? problem.LowerBounds[i] : double.NegativeInfinity;
            hi[r] = problem.UpperBounds != null ? problem.UpperBounds[i] : double.PositiveInfinity;
        }
        rowPtr[m] = k;
        return new Csr(m, n, rowPtr, colIdx, vals);
    }

    /// <summary>Minimal CSR matrix with the three kernels ADMM needs.</summary>
    private sealed class Csr
    {
        public readonly int RowCount;
        public readonly int ColCount;
        private readonly int[] _rowPtr;
        private readonly int[] _colIdx;
        private readonly double[] _vals;

        public Csr(int rows, int cols, int[] rowPtr, int[] colIdx, double[] vals)
        { RowCount = rows; ColCount = cols; _rowPtr = rowPtr; _colIdx = colIdx; _vals = vals; }

        public int NonZeroCount => _vals.Length;

        /// <summary>result = A x</summary>
        public void Mul(double[] x, double[] result)
        {
            for (int r = 0; r < RowCount; r++)
            {
                double s = 0;
                int end = _rowPtr[r + 1];
                for (int k = _rowPtr[r]; k < end; k++) s += _vals[k] * x[_colIdx[k]];
                result[r] = s;
            }
        }

        /// <summary>result = Aᵀ y (overwrites result)</summary>
        public void TransposeMul(double[] y, double[] result)
        {
            Array.Clear(result, 0, result.Length);
            for (int r = 0; r < RowCount; r++)
            {
                double w = y[r];
                if (w == 0) continue;
                int end = _rowPtr[r + 1];
                for (int k = _rowPtr[r]; k < end; k++) result[_colIdx[k]] += _vals[k] * w;
            }
        }

        /// <summary>acc += Aᵀ (rho_r*z − y), fused for the ADMM rhs.</summary>
        public void TransposeMulAccumulate(double[] z, double[] y, double[] rhoRow, double[] acc)
        {
            for (int r = 0; r < RowCount; r++)
            {
                double w = rhoRow[r] * z[r] - y[r];
                if (w == 0) continue;
                int end = _rowPtr[r + 1];
                for (int k = _rowPtr[r]; k < end; k++) acc[_colIdx[k]] += _vals[k] * w;
            }
        }

        /// <summary>M += Aᵀ diag(rho) A (dense accumulator, upper triangle then mirrored by caller).</summary>
        public void AddAtAUpper(double[,] mtx, double[] rhoRow)
        {
            for (int r = 0; r < RowCount; r++)
            {
                int start = _rowPtr[r], end = _rowPtr[r + 1];
                for (int k1 = start; k1 < end; k1++)
                {
                    int ci = _colIdx[k1];
                    double w = rhoRow[r] * _vals[k1];
                    for (int k2 = start; k2 < end; k2++)
                    {
                        int cj = _colIdx[k2];
                        if (cj >= ci) mtx[ci, cj] += w * _vals[k2];
                    }
                }
            }
        }
    }

    // ---- Hessian helpers (diagonal fast path). ----
    private static double[] ExtractDiagonal(double[,] p, int n)
    {
        if (p == null) return new double[n];
        for (int i = 0; i < n; i++)
            for (int c = 0; c < n; c++)
                if (i != c && p[i, c] != 0) return null; // not diagonal
        var d = new double[n];
        for (int i = 0; i < n; i++) d[i] = p[i, i];
        return d;
    }

    private static void MulHessian(double[,] p, double[] pDiag, double[] x, double[] result, int n)
    {
        if (pDiag != null)
        {
            for (int i = 0; i < n; i++) result[i] = pDiag[i] * x[i];
            return;
        }
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int c = 0; c < n; c++) s += p[i, c] * x[c];
            result[i] = s;
        }
    }

    private static double Objective(double[,] p, double[] pDiag, double[] q, double[] x, int n)
    {
        double obj = 0;
        if (pDiag != null)
        {
            for (int i = 0; i < n; i++) obj += 0.5 * pDiag[i] * x[i] * x[i];
        }
        else if (p != null)
        {
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int c = 0; c < n; c++) s += p[i, c] * x[c];
                obj += 0.5 * x[i] * s;
            }
        }
        for (int i = 0; i < n; i++) obj += q[i] * x[i];
        return obj;
    }

    private static void UpdateRhoRows(double[] rhoRow, bool[] isEq, double rho)
    {
        for (int r = 0; r < rhoRow.Length; r++) rhoRow[r] = isEq[r] ? rho * 1e3 : rho;
    }

    // ---- M = P + sigma*I + A' diag(rho) A, lower-triangular Cholesky (null on failure). ----
    private static double[,] Factor(double[,] p, double[] pDiag, Csr a, int n, double[] rhoRow, double sigma)
    {
        var mtx = new double[n, n];
        if (pDiag != null)
        {
            for (int i = 0; i < n; i++) mtx[i, i] = pDiag[i];
        }
        else if (p != null)
        {
            for (int i = 0; i < n; i++)
                for (int c = 0; c < n; c++) mtx[i, c] = p[i, c];
        }
        for (int i = 0; i < n; i++) mtx[i, i] += sigma;
        a.AddAtAUpper(mtx, rhoRow);
        for (int i = 0; i < n; i++)
            for (int c = 0; c < i; c++) mtx[i, c] = mtx[c, i];

        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c <= i; c++)
            {
                double s = mtx[i, c];
                for (int k = 0; k < c; k++) s -= l[i, k] * l[c, k];
                if (i == c)
                {
                    if (s <= 0) return null;
                    l[i, i] = Math.Sqrt(s);
                }
                else l[i, c] = s / l[c, c];
            }
        }
        return l;
    }

    private static void CholSolve(double[,] l, double[] b, double[] x, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= l[i, k] * x[k];
            x[i] = s / l[i, i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double s = x[i];
            for (int k = i + 1; k < n; k++) s -= l[k, i] * x[k];
            x[i] = s / l[i, i];
        }
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
}
