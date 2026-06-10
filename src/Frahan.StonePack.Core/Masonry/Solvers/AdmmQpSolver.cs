#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// AdmmQpSolver — robust pure-managed convex-QP solver (OSQP-style ADMM),
// evolution P1 of EVOLUTION_PLAN_MASONRY.md (2026-06-10).
//
// Motivation: the Dykstra alternating-projection ManagedQpSolver diverges on
// real masonry RBE systems (mixed force/moment row scales push the projection
// residuals to ~1e60 — the "Dykstra convergence tail" flagged by the V3 SLM
// review). This solver replaces it for the stability pipeline.
//
// Method (Stellato et al. 2020, "OSQP: an operator splitting solver for
// quadratic programs", Math. Prog. Comp. 12:637-672 — simplified dense form):
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
// =============================================================================

/// <summary>
/// OSQP-style ADMM solver for the masonry RBE/penalty QPs. Pure managed,
/// dense (suitable for the &lt;= few-thousand-variable assemblies a wall or
/// vault produces). Registered name: "AdmmQpSolver".
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

        // ---- Stack the constraint blocks into one (A, l, u). ----
        int meq = problem.EqualityMatrix != null ? problem.EqualityMatrix.GetLength(0) : 0;
        int mineq = problem.InequalityMatrix != null ? problem.InequalityMatrix.GetLength(0) : 0;
        int m = meq + mineq + n; // + identity block for the box bounds
        var a = new double[m, n];
        var lo = new double[m];
        var hi = new double[m];
        for (int i = 0; i < meq; i++)
        {
            for (int c = 0; c < n; c++) a[i, c] = problem.EqualityMatrix[i, c];
            lo[i] = problem.EqualityRhs[i]; hi[i] = problem.EqualityRhs[i];
        }
        for (int i = 0; i < mineq; i++)
        {
            int r = meq + i;
            for (int c = 0; c < n; c++) a[r, c] = problem.InequalityMatrix[i, c];
            lo[r] = double.NegativeInfinity; hi[r] = problem.InequalityRhs[i];
        }
        for (int i = 0; i < n; i++)
        {
            int r = meq + mineq + i;
            a[r, i] = 1.0;
            lo[r] = problem.LowerBounds != null ? problem.LowerBounds[i] : double.NegativeInfinity;
            hi[r] = problem.UpperBounds != null ? problem.UpperBounds[i] : double.PositiveInfinity;
        }

        // ---- Ruiz-lite row equilibration (unit inf-norm rows). ----
        for (int i = 0; i < m; i++)
        {
            double rmax = 0;
            for (int c = 0; c < n; c++) { double v = Math.Abs(a[i, c]); if (v > rmax) rmax = v; }
            if (rmax <= 1e-300) continue;
            double e = 1.0 / rmax;
            for (int c = 0; c < n; c++) a[i, c] *= e;
            if (!double.IsNegativeInfinity(lo[i])) lo[i] *= e;
            if (!double.IsPositiveInfinity(hi[i])) hi[i] *= e;
        }

        var q = new double[n];
        if (problem.LinearObjective != null)
            for (int i = 0; i < n; i++) q[i] = problem.LinearObjective[i];

        double lastRPri = double.NaN, lastRDua = double.NaN, lastEpsPri = double.NaN, lastEpsDua = double.NaN;

        // ---- ADMM state. ----
        var x = new double[n];
        var z = new double[m];
        var y = new double[m];
        var ax = new double[m];
        var axTilde = new double[m];
        var rhs = new double[n];
        var xTilde = new double[n];

        double rho = 0.1;
        double[,] chol = Factor(problem.Hessian, a, n, m, rho, _sigma);
        if (chol == null)
            return new ConvexQpResult(ConvexQpStatus.SolverError, null, 0,
                "Cholesky factorisation failed (P + sigma*I + rho*A'A not SPD).");

        int refactors = 0;
        for (int it = 1; it <= _maxIterations; it++)
        {
            // rhs = sigma*x - q + A'(rho*z - y)
            for (int c = 0; c < n; c++) rhs[c] = _sigma * x[c] - q[c];
            for (int r = 0; r < m; r++)
            {
                double w = rho * z[r] - y[r];
                if (w == 0) continue;
                for (int c = 0; c < n; c++) rhs[c] += a[r, c] * w;
            }
            CholSolve(chol, rhs, xTilde, n);

            MatVec(a, xTilde, axTilde, m, n);
            for (int c = 0; c < n; c++) x[c] = _alpha * xTilde[c] + (1 - _alpha) * x[c];
            for (int r = 0; r < m; r++)
            {
                double zRelaxed = _alpha * axTilde[r] + (1 - _alpha) * z[r];
                double zNew = Clamp(zRelaxed + y[r] / rho, lo[r], hi[r]);
                y[r] += rho * (zRelaxed - zNew);
                z[r] = zNew;
            }

            if (it % 10 != 0 && it != _maxIterations) continue;

            // ---- residuals ----
            MatVec(a, x, ax, m, n);
            double rPri = 0, axN = 0, zN = 0;
            for (int r = 0; r < m; r++)
            {
                double v = Math.Abs(ax[r] - z[r]); if (v > rPri) rPri = v;
                double v2 = Math.Abs(ax[r]); if (v2 > axN) axN = v2;
                double v3 = Math.Abs(z[r]); if (v3 > zN) zN = v3;
            }
            double rDua = 0, pxN = 0, atyN = 0, qN = 0;
            for (int c = 0; c < n; c++)
            {
                double px = 0;
                if (problem.Hessian != null)
                    for (int c2 = 0; c2 < n; c2++) px += problem.Hessian[c, c2] * x[c2];
                double aty = 0;
                for (int r = 0; r < m; r++) aty += a[r, c] * y[r];
                double v = Math.Abs(px + q[c] + aty); if (v > rDua) rDua = v;
                if (Math.Abs(px) > pxN) pxN = Math.Abs(px);
                if (Math.Abs(aty) > atyN) atyN = Math.Abs(aty);
                if (Math.Abs(q[c]) > qN) qN = Math.Abs(q[c]);
            }
            double epsPri = _epsAbs + _epsRel * Math.Max(axN, zN);
            double epsDua = _epsAbs + _epsRel * Math.Max(Math.Max(pxN, atyN), qN);
            lastRPri = rPri; lastRDua = rDua; lastEpsPri = epsPri; lastEpsDua = epsDua;

            if (rPri <= epsPri && rDua <= epsDua)
            {
                double obj = Objective(problem, q, x, n);
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
                    chol = Factor(problem.Hessian, a, n, m, rho, _sigma);
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

    // ---- M = P + sigma*I + rho*A'A, lower-triangular Cholesky (null on failure). ----
    private static double[,] Factor(double[,] p, double[,] a, int n, int m, double rho, double sigma)
    {
        var mtx = new double[n, n];
        if (p != null)
            for (int i = 0; i < n; i++)
                for (int c = 0; c < n; c++) mtx[i, c] = p[i, c];
        for (int i = 0; i < n; i++) mtx[i, i] += sigma;
        for (int r = 0; r < m; r++)
        {
            for (int i = 0; i < n; i++)
            {
                double ari = a[r, i];
                if (ari == 0) continue;
                double w = rho * ari;
                for (int c = i; c < n; c++) mtx[i, c] += w * a[r, c];
            }
        }
        // mirror upper -> lower then factor in place (we filled upper for c >= i)
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
        // forward: L w = b
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= l[i, k] * x[k];
            x[i] = s / l[i, i];
        }
        // backward: L' x = w
        for (int i = n - 1; i >= 0; i--)
        {
            double s = x[i];
            for (int k = i + 1; k < n; k++) s -= l[k, i] * x[k];
            x[i] = s / l[i, i];
        }
    }

    private static void MatVec(double[,] a, double[] x, double[] result, int m, int n)
    {
        for (int r = 0; r < m; r++)
        {
            double s = 0;
            for (int c = 0; c < n; c++) s += a[r, c] * x[c];
            result[r] = s;
        }
    }

    private static double Objective(ConvexQpProblem problem, double[] q, double[] x, int n)
    {
        double obj = 0;
        if (problem.Hessian != null)
        {
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int c = 0; c < n; c++) s += problem.Hessian[i, c] * x[c];
                obj += 0.5 * x[i] * s;
            }
        }
        for (int i = 0; i < n; i++) obj += q[i] * x[i];
        return obj;
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
}
