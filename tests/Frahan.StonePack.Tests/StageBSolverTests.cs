#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Solvers;

#pragma warning disable CS0618 // tests deliberately pin the LEGACY Build sign convention (M2)

namespace Frahan.Tests;

// =============================================================================
// StageBSolverTests — paper-faithful Hessian + QP closed-form fast path.
// =============================================================================

static class StageBSolverTests
{
    private const double Tol = 1e-6;

    // ─── RbeQpFormulation tangentialScale ────────────────────────────────────

    public static void RbeFormulation_TangentialScale_AppliesToTangentDiagonalsOnly()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        const double tang = 1000.0;
        var qp = RbeQpFormulation.Build(system, frictionAfr: null,
                                         hessianScale: 1.0, tangentialScale: tang);

        for (int i = 0; i < qp.VariableCount; i++)
        {
            ForceComponent c = system.ForceColumns[i].Component;
            double expected;
            switch (c)
            {
                case ForceComponent.Normal:
                case ForceComponent.NormalPositive:
                case ForceComponent.NormalNegative:
                    expected = 1.0;
                    break;
                case ForceComponent.Tangent1:
                case ForceComponent.Tangent2:
                    expected = tang;
                    break;
                default:
                    throw new InvalidOperationException();
            }
            Assert(Math.Abs(qp.Hessian[i, i] - expected) < Tol,
                $"H[{i},{i}] expected {expected}, got {qp.Hessian[i, i]} (component={c})");
        }
    }

    public static void RbeFormulation_TangentialScale_NonPositive_Throws()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        bool threw = false;
        try { _ = RbeQpFormulation.Build(system, frictionAfr: null, tangentialScale: -1.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative tangentialScale should throw ArgumentOutOfRangeException");
    }

    // ─── ManagedQpSolver closed-form fast path ───────────────────────────────

    public static void ManagedQp_ClosedForm_TwoVars_FindsMidpoint()
    {
        // min ½ ||x||² s.t. x[0] + x[1] = 1; closed-form: x=(0.5, 0.5).
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: new double[,] { { 1, 0 }, { 0, 1 } },
            linearObjective: new double[2],
            equalityMatrix: new double[,] { { 1, 1 } },
            equalityRhs: new double[] { 1.0 },
            inequalityMatrix: null, inequalityRhs: null,
            lowerBounds: null, upperBounds: null);
        var r = new ManagedQpSolver().Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}: {r.SolverMessage}");
        Assert(Math.Abs(r.X[0] - 0.5) < Tol, $"x[0] expected 0.5, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 0.5) < Tol, $"x[1] expected 0.5, got {r.X[1]}");
        Assert(r.SolverMessage.IndexOf("Closed-form", StringComparison.Ordinal) >= 0,
            $"expected closed-form path message, got: {r.SolverMessage}");
    }

    public static void ManagedQp_ClosedForm_NonUniformDiagonal_StillSolves()
    {
        // min ½(x[0]² + 4 x[1]²) s.t. x[0] + x[1] = 1.
        // Lagrangian: x[0] = λ, x[1] = λ/4. Sum = 1.25 λ = 1 → λ = 0.8.
        // Solution: x[0] = 0.8, x[1] = 0.2.
        var problem = new ConvexQpProblem(
            variableCount: 2,
            hessian: new double[,] { { 1.0, 0 }, { 0, 4.0 } },
            linearObjective: new double[2],
            equalityMatrix: new double[,] { { 1, 1 } },
            equalityRhs: new double[] { 1.0 },
            inequalityMatrix: null, inequalityRhs: null,
            lowerBounds: null, upperBounds: null);
        var r = new ManagedQpSolver().Solve(problem);
        Assert(r.Status == ConvexQpStatus.Optimal, $"expected Optimal, got {r.Status}");
        Assert(Math.Abs(r.X[0] - 0.8) < Tol, $"x[0] expected 0.8, got {r.X[0]}");
        Assert(Math.Abs(r.X[1] - 0.2) < Tol, $"x[1] expected 0.2, got {r.X[1]}");
    }

    public static void ManagedQp_ClosedForm_BoundsViolated_FallsThrough()
    {
        // min ½ x² s.t. x = -1, lb = 0. The closed-form gives x = -1 which
        // violates the bound; solver should fall back to iterative or report
        // a non-Optimal status. Either way it must NOT silently return -1
        // labeled Optimal.
        var problem = new ConvexQpProblem(
            variableCount: 1,
            hessian: new double[,] { { 1.0 } },
            linearObjective: new double[1],
            equalityMatrix: new double[,] { { 1.0 } },
            equalityRhs: new double[] { -1.0 },
            inequalityMatrix: null, inequalityRhs: null,
            lowerBounds: new double[] { 0.0 },
            upperBounds: new double[] { double.PositiveInfinity });
        var r = new ManagedQpSolver(maxIterations: 200).Solve(problem);
        Assert(r.Status != ConvexQpStatus.Optimal || r.X[0] >= -1e-7,
            $"closed-form must not silently return Optimal with x={r.X?[0]} below bound 0");
    }

    // ─── End-to-end: packed stack → RBE → ManagedQpSolver Optimal ────────────

    public static void EndToEnd_OneFreeOnGround_FeedsRbeSolver_ReturnsOptimal()
    {
        // Simpler than packed stack: hand-built 2-block assembly with 1 contact.
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.BuildPhysicsCorrected(system, frictionAfr: null);
        var r = new ManagedQpSolver().Solve(qp);
        if (r.Status != ConvexQpStatus.Optimal)
        {
            // Diagnostic: emit X (if any), residual, and beq snippet so we can see WHY.
            string xStr = r.X == null ? "null" : "[" + string.Join(", ", System.Array.ConvertAll(r.X, v => v.ToString("F4"))) + "]";
            string beqStr = "beq=[" + string.Join(", ", System.Array.ConvertAll(qp.EqualityRhs, v => v.ToString("F4"))) + "]";
            Assert(false, $"expected Optimal, got {r.Status}: {r.SolverMessage}; X={xStr}; {beqStr}");
        }
    }

    public static void EndToEnd_PackedStack_FeedsRbeSolver_ReturnsOptimal()
    {
        var slabs = new List<Slab>(2);
        for (int i = 0; i < 2; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.30, wallHeight: 0.30, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 2, "expected 2 placed");

        var system = EquilibriumMatrixBuilder.Build(result.Assembly, penalty: false);
        var qp = RbeQpFormulation.BuildPhysicsCorrected(system, frictionAfr: null);
        var solver = new ManagedQpSolver(tolerance: 1e-7, maxIterations: 1000);
        var r = solver.Solve(qp);
        Assert(r.Status == ConvexQpStatus.Optimal,
            $"expected Optimal from packed-stack RBE (closed-form path), got {r.Status}: {r.SolverMessage}");
        Assert(r.X != null && r.X.Length == qp.VariableCount,
            $"solution vector wrong length: {r.X?.Length}");
    }

    // ─── helpers (ported from MasonryRbeFormulationTests) ────────────────────

    private static MasonryAssembly OneFreeOnGroundAssembly()
    {
        var ground = new MasonryBlock("ground",
            new double[] { -1, -1, -1,  1, -1, -1,  1,  1, -1, -1,  1, -1,
                           -1, -1,  0,  1, -1,  0,  1,  1,  0, -1,  1,  0 },
            new int[] {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0 },
            density: 2400);
        var free = new MasonryBlock("free",
            new double[] { 0, 0, 0,  1, 0, 0,  1, 1, 0, 0, 1, 0,
                           0, 0, 1,  1, 0, 1,  1, 1, 1, 0, 1, 1 },
            new int[] {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0 },
            density: 2400);
        var contact = new ContactVertex[]
        {
            new ContactVertex(0, 0, 0),
            new ContactVertex(1, 0, 0),
            new ContactVertex(1, 1, 0),
            new ContactVertex(0, 1, 0),
        };
        var iface = new MasonryInterface("ground", "free", contact,
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
        var bc = new BoundaryConditions(new[] { "ground" });
        return new MasonryAssembly(new[] { ground, free }, new[] { iface }, bc);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
