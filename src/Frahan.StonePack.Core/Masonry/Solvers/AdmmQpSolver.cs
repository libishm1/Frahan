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
//
// MEASURED LIMIT (2026-06-11): on penalty-RBE masonry systems cold-start
// convergence degrades steeply past ~50 contact interfaces (54-iface
// exact-joint wall 5.4 s, 95-iface 24 s, 147-iface 86 s; the original KB-10
// repro returned SolverError at 8000 iterations). MITIGATED same day (KB-10):
// MasonryStabilityChecker now runs an LS-first KKT certificate + POCS cone
// polish that decodes wall verdicts WITHOUT the ADMM (54/95/147-iface walls
// in 0.07/0.4/1.1 s), and falls back to this solver — warm-started via the
// appended Solve(problem, warmStartX) overload — when the certificate
// declines. Per-element verification (examples/27 card 10) remains a valid
// pattern for mixed assemblies.
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

    public ConvexQpResult Solve(ConvexQpProblem problem) => Solve(problem, null);

    /// <summary>
    /// Warm-started overload (KB-10, 2026-06-11). <paramref name="warmStartX"/>
    /// is an initial primal point in the ORIGINAL (unscaled) variable space;
    /// it is mapped into the internally equilibrated space and seeds x and
    /// z = clamp(Ax, l, u) with y = 0 (OSQP-style warm start). Pass null (the
    /// default via the 1-arg overload) for the unchanged cold start.
    /// </summary>
    public ConvexQpResult Solve(ConvexQpProblem problem, double[] warmStartX)
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

        // ---- Hessian: O(n) diagonal fast path when applicable. Sparse-built
        // problems (2026-07-02) carry the diagonal directly, no dense Hessian. ----
        double[] pDiag = problem.Hessian != null
            ? ExtractDiagonal(problem.Hessian, n)
            : (problem.HessianDiagonal != null ? (double[])problem.HessianDiagonal.Clone() : null);
        double[] pDiag0 = pDiag != null ? (double[])pDiag.Clone() : null;

        // ---- P1.1b: full Ruiz equilibration (rows AND columns, 3 alternating
        // passes). Column scales fold into the objective (P <- DPD, q <- Dq) and
        // the solution is unscaled on exit (x = D x'). The masonry QPs mix
        // newton-scale forces, metre-scale moment arms, and the 1e3 penalty
        // weight — without column scaling the iteration count explodes. ----
        var dCol = new double[n];
        for (int c = 0; c < n; c++) dCol[c] = 1.0;
        {
            var eTmp = new double[m];
            var cTmp = new double[n];
            for (int pass = 0; pass < 3; pass++)
            {
                a.RowInfNorms(eTmp);
                for (int r = 0; r < m; r++)
                    eTmp[r] = eTmp[r] > 1e-300 ? 1.0 / Math.Sqrt(eTmp[r]) : 1.0;
                a.ScaleRows(eTmp);
                for (int r = 0; r < m; r++) { lo[r] *= eTmp[r]; hi[r] *= eTmp[r]; }
                a.ColInfNorms(cTmp);
                for (int c = 0; c < n; c++)
                {
                    double dc = cTmp[c] > 1e-300 ? 1.0 / Math.Sqrt(cTmp[c]) : 1.0;
                    cTmp[c] = dc; dCol[c] *= dc;
                }
                a.ScaleCols(cTmp);
            }
        }
        for (int c = 0; c < n; c++) q[c] *= dCol[c];
        double[,] pUse = null;
        if (pDiag != null)
        {
            for (int c = 0; c < n; c++) pDiag[c] *= dCol[c] * dCol[c];
        }
        else if (problem.Hessian != null)
        {
            pUse = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int c = 0; c < n; c++) pUse[i, c] = problem.Hessian[i, c] * dCol[i] * dCol[c];
        }

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

        // ---- Warm start (KB-10): map the caller's point into the column-
        // equilibrated space (xOut = dCol * x  =>  x = warm / dCol) and seed
        // z at the clamped constraint image. y stays 0. ----
        if (warmStartX != null && warmStartX.Length == n)
        {
            for (int c = 0; c < n; c++) x[c] = warmStartX[c] / dCol[c];
            a.Mul(x, ax);
            for (int r = 0; r < m; r++) z[r] = Clamp(ax[r], lo[r], hi[r]);
        }

        // Per-row rho (OSQP sec. 5.2): equality rows (l == u) get 1e3 x the base
        // rho — they must hold exactly, and stiffening them slashes iterations.
        double rho = 0.1;
        var rhoRow = new double[m];
        var isEq = new bool[m];
        for (int r = 0; r < m; r++) isEq[r] = lo[r] == hi[r] && !double.IsInfinity(lo[r]);
        UpdateRhoRows(rhoRow, isEq, rho);
        // ---- x-update linear solve: dense Cholesky for legacy problems; for
        // SPARSE-built problems (diagonal P, no dense blocks) a matrix-free
        // Jacobi-preconditioned CG on (P + sigma I + A' rho A) — the dense
        // factor is O(n^2) memory / O(n^3) time and was the 20-min/OOM wall. ----
        bool useCg = problem.Hessian == null && pDiag != null;
        double[,] chol = null;
        double[] kktDiag = null;
        double[] cgTmpM = null, cgR = null, cgZ = null, cgP = null, cgAp = null;
        if (useCg)
        {
            kktDiag = BuildKktDiag(pDiag, a, rhoRow, _sigma, n);
            cgTmpM = new double[m]; cgR = new double[n]; cgZ = new double[n]; cgP = new double[n]; cgAp = new double[n];
        }
        else
        {
            chol = Factor(pUse, pDiag, a, n, rhoRow, _sigma);
            if (chol == null)
                return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
                    "Cholesky factorisation failed (P + sigma*I + rho*A'A not SPD).");
        }

        double lastRPri = double.NaN, lastRDua = double.NaN, lastEpsPri = double.NaN, lastEpsDua = double.NaN;
        int refactors = 0;
        for (int it = 1; it <= _maxIterations; it++)
        {
            // rhs = sigma*x - q + A'(rho*z - y)
            for (int c = 0; c < n; c++) rhs[c] = _sigma * x[c] - q[c];
            a.TransposeMulAccumulate(z, y, rhoRow, rhs);
            if (useCg)
                CgSolveKkt(pDiag, a, rhoRow, _sigma, kktDiag, rhs, xTilde, cgTmpM, cgR, cgZ, cgP, cgAp); // warm-started from last xTilde
            else
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
            MulHessian(pUse, pDiag, x, px, n);
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
                var xOut = new double[n];
                for (int c = 0; c < n; c++) xOut[c] = dCol[c] * x[c];
                var q0 = new double[n];
                if (problem.LinearObjective != null)
                    for (int c = 0; c < n; c++) q0[c] = problem.LinearObjective[c];
                double obj = Objective(problem.Hessian, pDiag0, q0, xOut, n);
                return new ConvexQpResult(ConvexQpStatus.Optimal, xOut, obj,
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
                    if (useCg)
                    {
                        kktDiag = BuildKktDiag(pDiag, a, rhoRow, _sigma, n); // no factor: just refresh the preconditioner
                    }
                    else
                    {
                        chol = Factor(pUse, pDiag, a, n, rhoRow, _sigma);
                        if (chol == null)
                            return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
                                "Cholesky refactorisation failed after rho update.");
                    }
                }
            }
        }

        return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
            $"ADMM did not converge in {_maxIterations} iterations " +
            $"(rho={rho:0.###e0}, r_pri={lastRPri:0.###e0}/eps {lastEpsPri:0.###e0}, " +
            $"r_dua={lastRDua:0.###e0}/eps {lastEpsDua:0.###e0}).");
    }

    // =========================================================================
    // Matrix-free CG for the sparse x-update (2026-07-02): solves
    //   (P + sigma I + A' diag(rho) A) xTilde = rhs
    // with a Jacobi preconditioner; warm-started from the previous xTilde, so
    // late ADMM iterations converge in a handful of CG steps.
    // =========================================================================
    private static double[] BuildKktDiag(double[] pDiag, Csr a, double[] rhoRow, double sigma, int n)
    {
        var d = new double[n];
        for (int c = 0; c < n; c++) d[c] = (pDiag != null ? pDiag[c] : 0.0) + sigma;
        a.AddColSqWeighted(rhoRow, d);
        for (int c = 0; c < n; c++) if (d[c] < 1e-12) d[c] = 1.0;
        return d;
    }

    private static void KktMul(double[] pDiag, Csr a, double[] rhoRow, double sigma,
                               double[] x, double[] tmpM, double[] y)
    {
        a.Mul(x, tmpM);
        for (int r = 0; r < tmpM.Length; r++) tmpM[r] *= rhoRow[r];
        a.TransposeMul(tmpM, y);
        for (int c = 0; c < x.Length; c++) y[c] += ((pDiag != null ? pDiag[c] : 0.0) + sigma) * x[c];
    }

    private static void CgSolveKkt(double[] pDiag, Csr a, double[] rhoRow, double sigma,
                                   double[] jacobi, double[] b, double[] x,
                                   double[] tmpM, double[] r, double[] z, double[] p, double[] ap)
    {
        int n = x.Length;
        KktMul(pDiag, a, rhoRow, sigma, x, tmpM, ap);
        double bn = 0;
        for (int c = 0; c < n; c++) { r[c] = b[c] - ap[c]; bn += b[c] * b[c]; }
        bn = Math.Sqrt(bn) + 1e-300;
        double rz = 0;
        for (int c = 0; c < n; c++) { z[c] = r[c] / jacobi[c]; p[c] = z[c]; rz += r[c] * z[c]; }
        for (int it = 0; it < 250; it++)
        {
            KktMul(pDiag, a, rhoRow, sigma, p, tmpM, ap);
            double pap = 0;
            for (int c = 0; c < n; c++) pap += p[c] * ap[c];
            if (Math.Abs(pap) < 1e-300) break;
            double alpha = rz / pap;
            double rn = 0;
            for (int c = 0; c < n; c++)
            {
                x[c] += alpha * p[c];
                r[c] -= alpha * ap[c];
                rn += r[c] * r[c];
            }
            if (Math.Sqrt(rn) <= 1e-10 * bn + 1e-14) break;
            double rz2 = 0;
            for (int c = 0; c < n; c++) { z[c] = r[c] / jacobi[c]; rz2 += r[c] * z[c]; }
            double beta = rz2 / (rz + 1e-300);
            for (int c = 0; c < n; c++) p[c] = z[c] + beta * p[c];
            rz = rz2;
        }
    }

    // =========================================================================
    // CSR assembly (equality + inequality + bound-identity rows), row-equilibrated.
    // =========================================================================
    private static Csr BuildCsr(ConvexQpProblem problem, int n, out double[] lo, out double[] hi)
    {
        if (problem.EqualitySparse != null || problem.InequalitySparse != null)
            return BuildCsrFromCoo(problem, n, out lo, out hi);
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
            for (int c = 0; c < n; c++)
            {
                double v = problem.EqualityMatrix[i, c];
                if (v != 0) { colIdx[k] = c; vals[k] = v; k++; }
            }
            lo[i] = problem.EqualityRhs[i]; hi[i] = lo[i];
        }
        for (int i = 0; i < mineq; i++)
        {
            int r = meq + i;
            rowPtr[r] = k;
            for (int c = 0; c < n; c++)
            {
                double v = problem.InequalityMatrix[i, c];
                if (v != 0) { colIdx[k] = c; vals[k] = v; k++; }
            }
            lo[r] = double.NegativeInfinity; hi[r] = problem.InequalityRhs[i];
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

    /// <summary>
    /// CSR straight from the COO blocks of a SPARSE-built problem (2026-07-02):
    /// counting sort by row, no dense intermediate. Duplicate COO entries are
    /// legal (the matvec kernels accumulate).
    /// </summary>
    private static Csr BuildCsrFromCoo(ConvexQpProblem problem, int n, out double[] lo, out double[] hi)
    {
        var eq = problem.EqualitySparse;
        var ineq = problem.InequalitySparse;
        int meq = eq != null ? eq.RowCount : 0;
        int mineq = ineq != null ? ineq.RowCount : 0;
        int m = meq + mineq + n;
        lo = new double[m]; hi = new double[m];
        int nnzEq = eq != null ? eq.NonZeroCount : 0;
        int nnzIn = ineq != null ? ineq.NonZeroCount : 0;
        int nnz = nnzEq + nnzIn + n;

        var rowPtr = new int[m + 1];
        if (eq != null) { var ri = eq.RowIndices; for (int k2 = 0; k2 < nnzEq; k2++) rowPtr[ri[k2] + 1]++; }
        if (ineq != null) { var ri = ineq.RowIndices; for (int k2 = 0; k2 < nnzIn; k2++) rowPtr[meq + ri[k2] + 1]++; }
        for (int i = 0; i < n; i++) rowPtr[meq + mineq + i + 1]++;
        for (int r = 0; r < m; r++) rowPtr[r + 1] += rowPtr[r];

        var colIdx = new int[nnz];
        var vals = new double[nnz];
        var cursor = new int[m];
        for (int r = 0; r < m; r++) cursor[r] = rowPtr[r];
        if (eq != null)
        {
            var ri = eq.RowIndices; var ci = eq.ColIndices; var vv = eq.Values;
            for (int k2 = 0; k2 < nnzEq; k2++) { int r = ri[k2]; int p = cursor[r]++; colIdx[p] = ci[k2]; vals[p] = vv[k2]; }
        }
        if (ineq != null)
        {
            var ri = ineq.RowIndices; var ci = ineq.ColIndices; var vv = ineq.Values;
            for (int k2 = 0; k2 < nnzIn; k2++) { int r = meq + ri[k2]; int p = cursor[r]++; colIdx[p] = ci[k2]; vals[p] = vv[k2]; }
        }
        for (int i = 0; i < n; i++) { int r = meq + mineq + i; int p = cursor[r]++; colIdx[p] = i; vals[p] = 1.0; }

        for (int i = 0; i < meq; i++) { lo[i] = problem.EqualityRhs[i]; hi[i] = lo[i]; }
        for (int i = 0; i < mineq; i++) { lo[meq + i] = double.NegativeInfinity; hi[meq + i] = problem.InequalityRhs[i]; }
        for (int i = 0; i < n; i++)
        {
            int r = meq + mineq + i;
            lo[r] = problem.LowerBounds != null ? problem.LowerBounds[i] : double.NegativeInfinity;
            hi[r] = problem.UpperBounds != null ? problem.UpperBounds[i] : double.PositiveInfinity;
        }
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

        /// <summary>acc[c] += sum_r w[r] * A[r,c]^2 (Jacobi diag of Aᵀ diag(w) A).</summary>
        public void AddColSqWeighted(double[] w, double[] acc)
        {
            for (int r = 0; r < RowCount; r++)
            {
                double wr = w[r];
                if (wr == 0) continue;
                int end = _rowPtr[r + 1];
                for (int k = _rowPtr[r]; k < end; k++) acc[_colIdx[k]] += _vals[k] * _vals[k] * wr;
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

        /// <summary>Per-row infinity norms.</summary>
        public void RowInfNorms(double[] result)
        {
            for (int r = 0; r < RowCount; r++)
            {
                double mx = 0;
                int end = _rowPtr[r + 1];
                for (int k = _rowPtr[r]; k < end; k++)
                { double v = Math.Abs(_vals[k]); if (v > mx) mx = v; }
                result[r] = mx;
            }
        }

        /// <summary>Per-column infinity norms.</summary>
        public void ColInfNorms(double[] result)
        {
            Array.Clear(result, 0, result.Length);
            int nnz = _vals.Length;
            for (int k = 0; k < nnz; k++)
            { double v = Math.Abs(_vals[k]); int c = _colIdx[k]; if (v > result[c]) result[c] = v; }
        }

        /// <summary>vals[row k] *= e[r] for every row.</summary>
        public void ScaleRows(double[] e)
        {
            for (int r = 0; r < RowCount; r++)
            {
                int end = _rowPtr[r + 1];
                for (int k = _rowPtr[r]; k < end; k++) _vals[k] *= e[r];
            }
        }

        /// <summary>vals[k] *= d[col(k)] for every entry.</summary>
        public void ScaleCols(double[] d)
        {
            int nnz = _vals.Length;
            for (int k = 0; k < nnz; k++) _vals[k] *= d[_colIdx[k]];
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
