#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// ManagedQpSolver — pure-managed convex QP solver via Dykstra's algorithm of
// alternating projections.
//
// Specialised for the regime the masonry RBE problem produces:
//   - strictly-convex Hessian H = c * I  (c > 0)
//   - linear equality constraints  Aeq x = beq
//   - linear inequality constraints  Aineq x <= bineq  (optional)
//   - simple lower / upper bounds on each variable
//
// For these inputs the QP is equivalent to "find the H-norm-closest point in
// the intersection of (i) the affine set {Aeq x = beq}, (ii) the half-space
// system {Aineq x <= bineq}, and (iii) the box [lb, ub]." Dykstra's algorithm
// converges to that point for any closed convex sets.
//
// For non-diagonal or non-uniform-diagonal Hessians, the solver returns
// ConvexQpStatus.NotImplemented. The bound-and-equality case (no Aineq) is
// the most common input in early masonry tests; we hit that fast path first.
//
// Algorithm reference:
//   Dykstra, R. L. (1983). "An algorithm for restricted least squares
//   regression." J. Amer. Statist. Assoc. 78 (384), 837-842.
//   Boyle, J. P.; Dykstra, R. L. (1986). Lecture Notes in Statistics 37, 28-47.
//
// Algorithm-source acknowledgement: Kao et al. 2022 (compas_cra) prefer
// IPOPT with full nonlinear coupling; this solver targets the simpler RBE
// QP only and is a managed alternative when shipping IPOPT is undesirable.
// =============================================================================

public sealed class ManagedQpSolver : IConvexQpSolver
{
    private const double DefaultTolerance = 1e-8;
    private const int DefaultMaxIterations = 500;
    private const double EqualityRegularization = 1e-12;
    private const double IdentityHessianTol = 1e-9;

    public ManagedQpSolver(double tolerance = DefaultTolerance, int maxIterations = DefaultMaxIterations)
    {
        if (tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be > 0");
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "maxIterations must be > 0");
        Tolerance = tolerance;
        MaxIterations = maxIterations;
    }

    public string Name => "ManagedDykstra";
    public double Tolerance { get; }
    public int MaxIterations { get; }

    public ConvexQpResult Solve(ConvexQpProblem problem)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));

        // ---- Hessian shape check: must be a positive diagonal. ----
        // The new (Phase B) check accepts any positive diagonal, not just
        // c*I, so RbeQpFormulation.Build with tangentialScale != 1 is also
        // supported. The legacy c*I form still works (every element equal).
        var diag = new double[problem.VariableCount];
        if (!TryExtractDiagonal(problem.Hessian, diag, out double scaleHint))
        {
            return new ConvexQpResult(
                status: ConvexQpStatus.NotImplemented,
                x: null,
                objectiveValue: 0.0,
                solverMessage: "ManagedQpSolver only supports diagonal positive-definite Hessians.");
        }

        // ---- Linear cost: must be zero (RBE objective is purely quadratic). ----
        for (int i = 0; i < problem.LinearObjective.Length; i++)
        {
            if (Math.Abs(problem.LinearObjective[i]) > IdentityHessianTol)
            {
                return new ConvexQpResult(
                    status: ConvexQpStatus.NotImplemented,
                    x: null,
                    objectiveValue: 0.0,
                    solverMessage: "ManagedQpSolver only supports zero linear objective (RBE-style problems).");
            }
        }

        // ---- Closed-form fast path: equality + bounds, no inequality. ----
        // For diagonal positive H and zero linear cost, the equality-only QP
        // has the closed-form solution x = H^-1 Aeq^T (Aeq H^-1 Aeq^T)^-1 beq.
        // If x satisfies the bounds, we return immediately. This bypasses
        // Dykstra's known convergence trouble on the masonry RBE 6-DOF
        // family (HANDOFF_TO_CLAUDE.md P1 / Stage B follow-up).
        if (problem.InequalityRowCount == 0)
        {
            var fastResult = TrySolveClosedForm(diag, problem.EqualityMatrix,
                                                  problem.EqualityRhs,
                                                  problem.LowerBounds,
                                                  problem.UpperBounds);
            if (fastResult != null) return fastResult;
            // else fall through to Dykstra.
        }

        // ---- Dykstra path requires uniform diagonal (legacy c*I family). ----
        if (!IsUniform(diag))
        {
            return new ConvexQpResult(
                status: ConvexQpStatus.NotImplemented,
                x: null,
                objectiveValue: 0.0,
                solverMessage: "Dykstra path needs uniform diagonal H = c*I; non-uniform diagonal " +
                               "Hessians fall through closed-form. Enable bounds-feasibility for the " +
                               "fast path or use IIpoptSolver for full RBE Hessians.");
        }

        int n = problem.VariableCount;
        double hessianScale = scaleHint;
        var x = new double[n];                // unconstrained min of (c/2)||x||^2 is x = 0

        // ---- Pre-factor Cholesky of (Aeq Aeq^T) for the equality projection. ----
        double[,] Aeq = problem.EqualityMatrix;
        double[] beq = problem.EqualityRhs;
        int meq = problem.EqualityRowCount;

        double[,] Keq = null;        // Cholesky factor in lower triangle
        bool hasEquality = meq > 0;
        if (hasEquality)
        {
            Keq = DenseLinAlg.AAt(Aeq);
            if (!DenseLinAlg.CholeskyInPlace(Keq, regularization: EqualityRegularization))
            {
                return new ConvexQpResult(
                    status: ConvexQpStatus.SolverError,
                    x: null,
                    objectiveValue: 0.0,
                    solverMessage: "Equality matrix Aeq*Aeq^T is not numerically positive definite.");
            }
        }

        double[,] Aineq = problem.InequalityMatrix;
        double[] bineq = problem.InequalityRhs;
        int mineq = problem.InequalityRowCount;
        bool hasInequality = mineq > 0;

        double[] lb = problem.LowerBounds;
        double[] ub = problem.UpperBounds;
        bool hasBounds = NeedsBoundProjection(lb, ub);

        // ---- Dykstra accumulator vectors (one per projected set). ----
        var p_eq = hasEquality ? new double[n] : null;
        var p_ineq = hasInequality ? new double[n] : null;
        var p_bnd = hasBounds ? new double[n] : null;

        var prev = new double[n];
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            Array.Copy(x, prev, n);

            if (hasEquality)
            {
                // y = x + p_eq
                AddInPlace(x, p_eq);
                ProjectOntoEquality(x, Aeq, beq, Keq);
                // p_eq <- (x_before_proj) - (x_after_proj). Equivalent to:
                //   p_eq = p_eq + prev_y - x_proj  where prev_y was x+p_eq
                // We folded prev_y into x already; recompute:
                //   p_eq_new = (prev + p_eq_old) - x   ⇔   p_eq += prev - x
                AddSubInPlace(p_eq, prev, x);
            }

            if (hasInequality)
            {
                Array.Copy(x, prev, n);
                AddInPlace(x, p_ineq);
                ProjectOntoInequalities(x, Aineq, bineq);
                AddSubInPlace(p_ineq, prev, x);
            }

            if (hasBounds)
            {
                Array.Copy(x, prev, n);
                AddInPlace(x, p_bnd);
                ProjectOntoBounds(x, lb, ub);
                AddSubInPlace(p_bnd, prev, x);
            }

            // ---- Convergence: residual on equality + inequality. ----
            double resEq = hasEquality ? EqualityResidual(Aeq, beq, x) : 0.0;
            double resIneq = hasInequality ? InequalityViolation(Aineq, bineq, x) : 0.0;
            double resBnd = hasBounds ? BoundViolation(x, lb, ub) : 0.0;
            if (resEq <= Tolerance && resIneq <= Tolerance && resBnd <= Tolerance)
            {
                double obj = 0.5 * hessianScale * Dot(x, x);
                return new ConvexQpResult(
                    status: ConvexQpStatus.Optimal,
                    x: x,
                    objectiveValue: obj,
                    solverMessage: $"Converged in {iter + 1} iterations (residual eq={resEq:0.##e+00}, ineq={resIneq:0.##e+00}, bnd={resBnd:0.##e+00}).");
            }
        }

        // ---- Did not converge within MaxIterations. ----
        double finalEq = hasEquality ? EqualityResidual(Aeq, beq, x) : 0.0;
        double finalIneq = hasInequality ? InequalityViolation(Aineq, bineq, x) : 0.0;
        double finalBnd = hasBounds ? BoundViolation(x, lb, ub) : 0.0;
        return new ConvexQpResult(
            status: ConvexQpStatus.SolverError,
            x: null,
            objectiveValue: 0.0,
            solverMessage:
                $"Failed to converge in {MaxIterations} iterations. Final residuals: " +
                $"eq={finalEq:0.##e+00}, ineq={finalIneq:0.##e+00}, bnd={finalBnd:0.##e+00}.");
    }

    // ---- Hessian shape detection ------------------------------------------

    private static bool TryExtractDiagonal(double[,] H, double[] diag, out double scaleHint)
    {
        int n = H.GetLength(0);
        scaleHint = 1.0;
        if (n == 0) return true;
        if (diag.Length != n)
            throw new ArgumentException($"diag length {diag.Length} != H size {n}", nameof(diag));

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    if (H[i, i] <= 0.0) return false;
                    diag[i] = H[i, i];
                }
                else if (Math.Abs(H[i, j]) > IdentityHessianTol)
                {
                    return false;
                }
            }
        }
        scaleHint = diag[0];
        return true;
    }

    private static bool IsUniform(double[] diag)
    {
        if (diag == null || diag.Length == 0) return true;
        double first = diag[0];
        for (int i = 1; i < diag.Length; i++)
        {
            if (Math.Abs(diag[i] - first) > IdentityHessianTol) return false;
        }
        return true;
    }

    // ---- Closed-form pseudoinverse fast path (Stage B) --------------------

    /// <summary>
    /// For diagonal positive H and zero linear cost, solves
    /// min ½ x^T H x  s.t.  Aeq x = beq  via the closed form
    /// x = H^-1 Aeq^T (Aeq H^-1 Aeq^T)^-1 beq.
    /// Returns Optimal if x satisfies all bounds; null if bounds are violated
    /// (caller falls through to iterative path) or if Cholesky fails.
    /// </summary>
    private static ConvexQpResult TrySolveClosedForm(
        double[] diag,
        double[,] Aeq, double[] beq,
        double[] lb, double[] ub)
    {
        if (diag == null) throw new ArgumentNullException(nameof(diag));
        int n = diag.Length;

        // No equality constraints: x = 0 is optimal (zero linear cost,
        // positive H). Just check bounds.
        if (Aeq == null || beq == null || beq.Length == 0)
        {
            var xz = new double[n];
            if (!BoundsSatisfied(xz, lb, ub)) return null;
            return new ConvexQpResult(
                status: ConvexQpStatus.Optimal,
                x: xz,
                objectiveValue: 0.0,
                solverMessage: "Closed-form: zero solution (no equality constraints).");
        }

        int meq = Aeq.GetLength(0);
        // K = Aeq * diag(1/diag) * Aeq^T   (meq x meq, symmetric PD).
        var K = new double[meq, meq];
        for (int i = 0; i < meq; i++)
        {
            for (int j = i; j < meq; j++)
            {
                double s = 0.0;
                for (int k = 0; k < n; k++)
                {
                    s += Aeq[i, k] * Aeq[j, k] / diag[k];
                }
                K[i, j] = s;
                if (i != j) K[j, i] = s;
            }
        }
        if (!DenseLinAlg.CholeskyInPlace(K, regularization: EqualityRegularization))
        {
            return null;  // not numerically PD; Dykstra may still help.
        }

        // Solve K λ = beq.
        var lamY = new double[meq];
        var lam  = new double[meq];
        DenseLinAlg.ForwardSubstitution(K, beq, lamY);
        DenseLinAlg.BackSubstitutionTranspose(K, lamY, lam);

        // x = H^-1 Aeq^T λ
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0.0;
            for (int j = 0; j < meq; j++) s += Aeq[j, i] * lam[j];
            x[i] = s / diag[i];
        }

        if (!BoundsSatisfied(x, lb, ub)) return null;

        double obj = 0.0;
        for (int i = 0; i < n; i++) obj += 0.5 * diag[i] * x[i] * x[i];
        return new ConvexQpResult(
            status: ConvexQpStatus.Optimal,
            x: x,
            objectiveValue: obj,
            solverMessage: $"Closed-form pseudoinverse (m_eq={meq}, n={n}).");
    }

    private static bool BoundsSatisfied(double[] x, double[] lb, double[] ub)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        const double tol = 1e-7;
        if (lb != null)
        {
            for (int i = 0; i < x.Length; i++)
            {
                if (!double.IsNegativeInfinity(lb[i]) && x[i] < lb[i] - tol) return false;
            }
        }
        if (ub != null)
        {
            for (int i = 0; i < x.Length; i++)
            {
                if (!double.IsPositiveInfinity(ub[i]) && x[i] > ub[i] + tol) return false;
            }
        }
        return true;
    }

    // ---- Projections ------------------------------------------------------

    /// <summary>
    /// Project x onto the affine set {Aeq * z = beq}. Closed-form:
    /// x_proj = x - Aeq^T * (Aeq*Aeq^T)^-1 * (Aeq*x - beq).
    /// </summary>
    private static void ProjectOntoEquality(double[] x, double[,] Aeq, double[] beq, double[,] cholK)
    {
        var r = DenseLinAlg.MatVec(Aeq, x);
        for (int i = 0; i < r.Length; i++) r[i] -= beq[i];

        var lambda = new double[r.Length];
        var y = new double[r.Length];
        DenseLinAlg.ForwardSubstitution(cholK, r, y);
        DenseLinAlg.BackSubstitutionTranspose(cholK, y, lambda);

        var correction = DenseLinAlg.MatTVec(Aeq, lambda);
        for (int i = 0; i < x.Length; i++) x[i] -= correction[i];
    }

    /// <summary>
    /// Sequential projection onto each violated half-space row of (Aineq, bineq).
    /// One sweep per outer iteration; Dykstra wraps this with the accumulator.
    /// </summary>
    private static void ProjectOntoInequalities(double[] x, double[,] Aineq, double[] bineq)
    {
        int m = Aineq.GetLength(0);
        int n = Aineq.GetLength(1);
        for (int i = 0; i < m; i++)
        {
            double dot = 0.0, normSq = 0.0;
            for (int j = 0; j < n; j++)
            {
                double aij = Aineq[i, j];
                dot += aij * x[j];
                normSq += aij * aij;
            }
            double slack = dot - bineq[i];
            if (slack <= 0.0 || normSq <= 0.0) continue;
            double t = slack / normSq;
            for (int j = 0; j < n; j++) x[j] -= t * Aineq[i, j];
        }
    }

    private static void ProjectOntoBounds(double[] x, double[] lb, double[] ub)
    {
        for (int i = 0; i < x.Length; i++)
        {
            if (lb != null && x[i] < lb[i]) x[i] = lb[i];
            if (ub != null && x[i] > ub[i]) x[i] = ub[i];
        }
    }

    private static bool NeedsBoundProjection(double[] lb, double[] ub)
    {
        if (lb == null && ub == null) return false;
        if (lb != null)
            for (int i = 0; i < lb.Length; i++)
                if (!double.IsNegativeInfinity(lb[i])) return true;
        if (ub != null)
            for (int i = 0; i < ub.Length; i++)
                if (!double.IsPositiveInfinity(ub[i])) return true;
        return false;
    }

    // ---- Residual helpers -------------------------------------------------

    private static double EqualityResidual(double[,] Aeq, double[] beq, double[] x)
    {
        var r = DenseLinAlg.MatVec(Aeq, x);
        double max = 0.0;
        for (int i = 0; i < r.Length; i++)
        {
            double diff = Math.Abs(r[i] - beq[i]);
            if (diff > max) max = diff;
        }
        return max;
    }

    private static double InequalityViolation(double[,] Aineq, double[] bineq, double[] x)
    {
        var r = DenseLinAlg.MatVec(Aineq, x);
        double max = 0.0;
        for (int i = 0; i < r.Length; i++)
        {
            double v = r[i] - bineq[i];
            if (v > max) max = v;
        }
        return max;
    }

    private static double BoundViolation(double[] x, double[] lb, double[] ub)
    {
        double max = 0.0;
        for (int i = 0; i < x.Length; i++)
        {
            if (lb != null && x[i] < lb[i])
            {
                double v = lb[i] - x[i];
                if (v > max) max = v;
            }
            if (ub != null && x[i] > ub[i])
            {
                double v = x[i] - ub[i];
                if (v > max) max = v;
            }
        }
        return max;
    }

    // ---- Tiny in-place vector helpers -------------------------------------

    private static void AddInPlace(double[] dst, double[] src)
    {
        for (int i = 0; i < dst.Length; i++) dst[i] += src[i];
    }

    /// <summary>p += a - b (component-wise).</summary>
    private static void AddSubInPlace(double[] p, double[] a, double[] b)
    {
        for (int i = 0; i < p.Length; i++) p[i] += a[i] - b[i];
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0.0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
