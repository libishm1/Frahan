#nullable disable
using System;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.Solvers;

#pragma warning disable CS0618 // tests deliberately pin the LEGACY Build sign convention (M2)

namespace Frahan.Tests;

// Phase A.4 unit tests for the RBE QP formulation
// (Frahan.Masonry.Solvers.RbeQpFormulation). Verifies that the QP problem
// statement built from an EquilibriumSystem matches the Kao 2022 RBE
// formulation: identity-scaled Hessian, zero linear cost, Aeq f = -b
// equality, optional friction inequality, normal-only lower bound of 0,
// tangents unbounded. Pure-managed; no Rhino runtime needed.

static class MasonryRbeFormulationTests
{
    private const double Tol = 1e-12;

    // -- Argument validation -----------------------------------------------

    public static void RbeQpFormulation_NullEquilibrium_Throws()
    {
        // Source: RbeQpFormulation.Build throws ArgumentNullException
        // explicitly when equilibrium is null.
        bool threw = false;
        try { _ = RbeQpFormulation.Build(null, frictionAfr: null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null equilibrium should throw ArgumentNullException");
    }

    // -- Variable count ----------------------------------------------------

    public static void RbeQpFormulation_VariableCount_MatchesAeqColCount()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.Build(system, frictionAfr: null);

        Assert(qp.VariableCount == system.Aeq.ColCount,
            $"VariableCount expected {system.Aeq.ColCount}, got {qp.VariableCount}");
        Assert(qp.VariableCount == 12,
            $"VariableCount expected 12 (4 verts * 3 components), got {qp.VariableCount}");
    }

    // -- Hessian -----------------------------------------------------------

    public static void RbeQpFormulation_HessianIsScaledIdentity()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);

        // hessianScale = 1.0 -> identity.
        var qp1 = RbeQpFormulation.Build(system, frictionAfr: null, hessianScale: 1.0);
        int n = qp1.VariableCount;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double expected = (i == j) ? 1.0 : 0.0;
                Assert(Math.Abs(qp1.Hessian[i, j] - expected) < Tol,
                    $"hessianScale=1.0: H[{i},{j}] expected {expected}, got {qp1.Hessian[i, j]}");
            }
        }

        // hessianScale = 2.5 -> 2.5 * I.
        var qp2 = RbeQpFormulation.Build(system, frictionAfr: null, hessianScale: 2.5);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double expected = (i == j) ? 2.5 : 0.0;
                Assert(Math.Abs(qp2.Hessian[i, j] - expected) < Tol,
                    $"hessianScale=2.5: H[{i},{j}] expected {expected}, got {qp2.Hessian[i, j]}");
            }
        }
    }

    // -- Linear objective --------------------------------------------------

    public static void RbeQpFormulation_LinearObjective_IsZero()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.Build(system, frictionAfr: null);

        Assert(qp.LinearObjective != null, "LinearObjective should not be null");
        Assert(qp.LinearObjective.Length == qp.VariableCount,
            $"LinearObjective length expected {qp.VariableCount}, got {qp.LinearObjective.Length}");
        for (int i = 0; i < qp.LinearObjective.Length; i++)
        {
            Assert(qp.LinearObjective[i] == 0.0,
                $"LinearObjective[{i}] expected 0.0, got {qp.LinearObjective[i]}");
        }
    }

    // -- Equality block ----------------------------------------------------

    public static void RbeQpFormulation_EqualityRhs_IsNegB()
    {
        // EquilibriumSystem stores Aeq f + b = 0, so the QP equality is
        // Aeq f = -b. Therefore qp.EqualityRhs[i] == -system.B[i].
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.Build(system, frictionAfr: null);

        Assert(qp.EqualityRhs != null, "EqualityRhs should not be null");
        Assert(qp.EqualityRhs.Length == system.B.Count,
            $"EqualityRhs length expected {system.B.Count}, got {qp.EqualityRhs.Length}");

        for (int i = 0; i < system.B.Count; i++)
        {
            double expected = -system.B[i];
            Assert(Math.Abs(qp.EqualityRhs[i] - expected) < Tol,
                $"EqualityRhs[{i}] expected {expected}, got {qp.EqualityRhs[i]}");
        }

        Assert(qp.EqualityMatrix != null, "EqualityMatrix should not be null");
        Assert(qp.EqualityMatrix.GetLength(0) == system.Aeq.RowCount,
            $"EqualityMatrix row count expected {system.Aeq.RowCount}, got {qp.EqualityMatrix.GetLength(0)}");
        Assert(qp.EqualityMatrix.GetLength(1) == system.Aeq.ColCount,
            $"EqualityMatrix col count expected {system.Aeq.ColCount}, got {qp.EqualityMatrix.GetLength(1)}");
    }

    // -- Residual audit (fail-loud gate, risk H3) ---------------------------

    public static void ResidualAudit_ViolatedSolution_IsCaught()
    {
        // A force vector that does NOT satisfy Aeq f = b must produce a large
        // relative residual, so the checker refuses a stable verdict on it.
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.BuildPhysicsCorrected(system, frictionAfr: null);

        var zero = new double[qp.VariableCount]; // all-zero forces cannot balance gravity
        double r = MasonryStabilityChecker.EqualityResidualInf(qp, zero);
        Assert(r > MasonryStabilityChecker.ResidualAuditGate,
            $"all-zero forces should fail the audit, got relative residual {r}");
    }

    public static void ResidualAudit_FullCheckStillStable()
    {
        // The end-to-end checker (which now runs the audit before the verdict)
        // must still report a single block resting on the ground as stable:
        // a genuinely converged solve passes the gate with margin.
        var assembly = OneFreeOnGroundAssembly();
        var detailed = MasonryStabilityChecker.CheckDetailed(assembly);
        Assert(detailed.Result.IsStable,
            $"one block on ground should stay stable with the audit on: {detailed.Result.Message}");
    }

    // -- Box bounds --------------------------------------------------------

    public static void RbeQpFormulation_NormalColumnsLowerBoundIsZero_TangentsUnbounded()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.Build(system, frictionAfr: null);

        for (int col = 0; col < system.ForceColumns.Count; col++)
        {
            var fc = system.ForceColumns[col];
            switch (fc.Component)
            {
                case ForceComponent.Normal:
                case ForceComponent.NormalPositive:
                case ForceComponent.NormalNegative:
                    Assert(qp.LowerBounds[col] == 0.0,
                        $"col {col} ({fc.Component}) lower bound expected 0.0, got {qp.LowerBounds[col]}");
                    Assert(qp.UpperBounds[col] == double.PositiveInfinity,
                        $"col {col} ({fc.Component}) upper bound expected +inf, got {qp.UpperBounds[col]}");
                    break;
                case ForceComponent.Tangent1:
                case ForceComponent.Tangent2:
                    Assert(qp.LowerBounds[col] == double.NegativeInfinity,
                        $"col {col} ({fc.Component}) lower bound expected -inf, got {qp.LowerBounds[col]}");
                    Assert(qp.UpperBounds[col] == double.PositiveInfinity,
                        $"col {col} ({fc.Component}) upper bound expected +inf, got {qp.UpperBounds[col]}");
                    break;
                default:
                    Assert(false, $"Unhandled ForceComponent {fc.Component} at col {col}");
                    break;
            }
        }
    }

    // -- Inequality block --------------------------------------------------

    public static void RbeQpFormulation_NoFriction_InequalityIsNull()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var qp = RbeQpFormulation.Build(system, frictionAfr: null);

        Assert(qp.InequalityMatrix == null, "InequalityMatrix should be null when frictionAfr is null");
        Assert(qp.InequalityRhs == null, "InequalityRhs should be null when frictionAfr is null");
        Assert(qp.InequalityRowCount == 0,
            $"InequalityRowCount expected 0 when frictionAfr is null, got {qp.InequalityRowCount}");

        // Equality block must still be populated.
        Assert(qp.EqualityMatrix != null, "EqualityMatrix should still be non-null");
        Assert(qp.EqualityRhs != null, "EqualityRhs should still be non-null");
    }

    // -- Helpers -----------------------------------------------------------

    private static MasonryAssembly OneFreeOnGroundAssembly()
    {
        var ground = MakeUnitCubeAt("ground", 0, 0, 0);
        var top = MakeUnitCubeAt("top", 0, 0, 1);
        var iface = QuadInterfaceAtZ("ground", "top", z: 1.0);
        return new MasonryAssembly(
            blocks: new[] { ground, top },
            interfaces: new[] { iface },
            boundaryConditions: new BoundaryConditions(new[] { "ground" }));
    }

    private static MasonryBlock MakeUnitCubeAt(string id, double dx, double dy, double dz)
    {
        var verts = new double[]
        {
            0+dx, 0+dy, 0+dz,
            1+dx, 0+dy, 0+dz,
            1+dx, 1+dy, 0+dz,
            0+dx, 1+dy, 0+dz,
            0+dx, 0+dy, 1+dz,
            1+dx, 0+dy, 1+dz,
            1+dx, 1+dy, 1+dz,
            0+dx, 1+dy, 1+dz,
        };
        var tris = new[]
        {
            0,2,1, 0,3,2, // -Z
            4,5,6, 4,6,7, // +Z
            0,1,5, 0,5,4, // -Y
            2,3,7, 2,7,6, // +Y
            1,2,6, 1,6,5, // +X
            0,4,7, 0,7,3, // -X
        };
        return new MasonryBlock(id, verts, tris, density: 2400.0);
    }

    private static MasonryInterface QuadInterfaceAtZ(string a, string b, double z)
    {
        return new MasonryInterface(
            blockAId: a, blockBId: b,
            contactPolygon: new[]
            {
                new ContactVertex(0, 0, z),
                new ContactVertex(1, 0, z),
                new ContactVertex(1, 1, z),
                new ContactVertex(0, 1, z),
            },
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
