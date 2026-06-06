#nullable disable
using System;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// Tests for ManagedQpSolver — Dykstra's alternating projections for the
// H = c*I QP family. Tiny problems with hand-verified analytic optima.

static class ManagedQpSolverTests
{
    private const double Tol = 1e-6;

    public static void Solve_EqualityOnly_TwoVars_FindsMidpoint()
    {
        // min ½ ||x||²  s.t.  x[0] + x[1] = 1
        // Optimum: x = (0.5, 0.5), objective = 0.25.
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: Identity(2),
            linearObjective: new double[2],
            equalityMatrix: new double[,] { { 1, 1 } },
            equalityRhs: new double[] { 1.0 },
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: null,
            upperBounds: null);

        var solver = new ManagedQpSolver();
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}: {r.SolverMessage}");
        Assert(Math.Abs(r.X[0] - 0.5) < Tol, $"x[0] expected 0.5, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 0.5) < Tol, $"x[1] expected 0.5, got {r.X[1]}");
        Assert(Math.Abs(r.ObjectiveValue - 0.25) < Tol, $"objective expected 0.25, got {r.ObjectiveValue}");
    }

    public static void Solve_BoundsOnly_OneActive_GivesActiveCorner()
    {
        // min ½ ||x||²  s.t.  x[0] >= 1, x[1] free
        // Optimum: x = (1, 0).
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: Identity(2),
            linearObjective: new double[2],
            equalityMatrix: null,
            equalityRhs: null,
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: new double[] { 1.0, double.NegativeInfinity },
            upperBounds: new double[] { double.PositiveInfinity, double.PositiveInfinity });

        var solver = new ManagedQpSolver();
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}");
        Assert(Math.Abs(r.X[0] - 1.0) < Tol, $"x[0] expected 1.0, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 0.0) < Tol, $"x[1] expected 0.0, got {r.X[1]}");
    }

    public static void Solve_EqualityAndBounds_ProjectsOnSimplexEdge()
    {
        // min ½ ||x||²  s.t.  x[0] + x[1] = 2, x >= 0
        // Both bounds inactive at the analytic optimum (0.5,1.5)? No: equality
        // alone gives the projection onto the line x0+x1=2 closest to origin,
        // which is (1, 1). Both >= 0, so bounds inactive. Optimum: (1, 1).
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: Identity(2),
            linearObjective: new double[2],
            equalityMatrix: new double[,] { { 1, 1 } },
            equalityRhs: new double[] { 2.0 },
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: new double[] { 0.0, 0.0 },
            upperBounds: new double[] { double.PositiveInfinity, double.PositiveInfinity });

        var solver = new ManagedQpSolver();
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}");
        Assert(Math.Abs(r.X[0] - 1.0) < Tol, $"x[0] expected 1.0, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 1.0) < Tol, $"x[1] expected 1.0, got {r.X[1]}");
    }

    public static void Solve_EqualityClampsBoundActive()
    {
        // min ½ ||x||²  s.t.  x[0] + x[1] = 2, x[0] >= 1.5
        // The unconstrained-equality minimum is (1, 1); bound x[0] >= 1.5 cuts in.
        // Optimum: x[0] = 1.5, x[1] = 0.5.
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: Identity(2),
            linearObjective: new double[2],
            equalityMatrix: new double[,] { { 1, 1 } },
            equalityRhs: new double[] { 2.0 },
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: new double[] { 1.5, double.NegativeInfinity },
            upperBounds: new double[] { double.PositiveInfinity, double.PositiveInfinity });

        var solver = new ManagedQpSolver(tolerance: 1e-7, maxIterations: 2000);
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}: {r.SolverMessage}");
        Assert(Math.Abs(r.X[0] - 1.5) < 1e-4, $"x[0] expected 1.5, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 0.5) < 1e-4, $"x[1] expected 0.5, got {r.X[1]}");
    }

    public static void Solve_Inequality_HalfSpaceProjection()
    {
        // min ½ ||x||²  s.t.  x[0] + x[1] >= 1   <=>  -x[0] - x[1] <= -1
        // Optimum: closest point on the half-space line is (0.5, 0.5).
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: Identity(2),
            linearObjective: new double[2],
            equalityMatrix: null,
            equalityRhs: null,
            inequalityMatrix: new double[,] { { -1, -1 } },
            inequalityRhs: new double[] { -1.0 },
            lowerBounds: null,
            upperBounds: null);

        var solver = new ManagedQpSolver(tolerance: 1e-7, maxIterations: 2000);
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}: {r.SolverMessage}");
        Assert(Math.Abs(r.X[0] - 0.5) < 1e-4, $"x[0] expected 0.5, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 0.5) < 1e-4, $"x[1] expected 0.5, got {r.X[1]}");
    }

    public static void Solve_NonDiagonalHessian_ReturnsNotImplemented()
    {
        var H = new double[,] { { 2, 1 }, { 1, 2 } };
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: H,
            linearObjective: new double[2],
            equalityMatrix: null,
            equalityRhs: null,
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: null,
            upperBounds: null);

        var solver = new ManagedQpSolver();
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.NotImplemented,
            $"expected NotImplemented for non-diagonal H, got {r.Status}");
    }

    public static void Solve_NonZeroLinearObjective_ReturnsNotImplemented()
    {
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: Identity(2),
            linearObjective: new double[] { 1.0, 0.0 },
            equalityMatrix: null,
            equalityRhs: null,
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: null,
            upperBounds: null);

        var solver = new ManagedQpSolver();
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.NotImplemented,
            $"expected NotImplemented for non-zero linear objective, got {r.Status}");
    }

    public static void Solve_ScaledIdentityHessian_StillWorks()
    {
        // H = 2I; objective = ½ * 2 * ||x||² = ||x||².
        // Same minimiser as the plain identity case (x = 0.5, 0.5).
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: new double[,] { { 2, 0 }, { 0, 2 } },
            linearObjective: new double[2],
            equalityMatrix: new double[,] { { 1, 1 } },
            equalityRhs: new double[] { 1.0 },
            inequalityMatrix: null,
            inequalityRhs: null,
            lowerBounds: null,
            upperBounds: null);

        var solver = new ManagedQpSolver();
        var r = solver.Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}");
        Assert(Math.Abs(r.X[0] - 0.5) < Tol, $"x[0] expected 0.5, got {r.X[0]}");
        // Objective at x=(0.5,0.5) under H=2I is ½ * 2 * 0.5 = 0.5.
        Assert(Math.Abs(r.ObjectiveValue - 0.5) < Tol, $"objective expected 0.5, got {r.ObjectiveValue}");
    }

    public static void Solve_NullProblem_Throws()
    {
        var solver = new ManagedQpSolver();
        bool threw = false;
        try { _ = solver.Solve(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null problem should throw ArgumentNullException");
    }

    public static void Ctor_NonPositiveTolerance_Throws()
    {
        bool threw = false;
        try { _ = new ManagedQpSolver(tolerance: 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "non-positive tolerance should throw");
    }

    // ---- Helpers ----

    private static double[,] Identity(int n)
    {
        var I = new double[n, n];
        for (int i = 0; i < n; i++) I[i, i] = 1.0;
        return I;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
