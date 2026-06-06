#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.EdgeMatching;
using Frahan.EdgeMatching.Matching;

namespace Frahan.Tests;

// =============================================================================
// MatcherRegistryTests + HungarianAssignerTests — pure-managed unit tests for
// the matching substrate per wiki/specs/component_decomposition_plan.md §3
// + wiki/specs/architectural_decisions_2026-05-31.md §1-§4.
//
// No Rhino runtime needed. Validates:
//   * HungarianAssigner (Kuhn 1955 bipartite assignment) on trivial /
//     square / rectangular / infeasible / empty inputs.
//   * ConstraintDictionary numeric + categorical operator dispatch.
//   * IncidenceMatrix.Build feasibility.
//   * WeightMatrix.Build NaN-sentinel discipline.
//   * MatcherRegistry register / IsRegistered / Run lifecycle.
//   * End-to-end: build context + register HungarianSolver + run + verify
//     AssignmentResult.
//
// AGENTS.md §9 compliance: every numeric tolerance below is justified in
// the test comment OR is a structural invariant (e.g. "1×1 identity").
// =============================================================================

internal static class HungarianAssignerTests
{
    // ─── Trivial cases ─────────────────────────────────────────────────────

    public static void Solve_Empty_ReturnsEmpty()
    {
        var result = HungarianAssigner.Solve(new double[0], 0, 0);
        Assert(result.Length == 0, "Empty cost matrix → empty assignment");
    }

    public static void Solve_OneByOne_ReturnsZero()
    {
        var result = HungarianAssigner.Solve(new[] { 42.0 }, 1, 1);
        Assert(result.Length == 1, "1×1 → length 1");
        Assert(result[0] == 0, $"1×1 → row 0 assigned to col 0; got {result[0]}");
    }

    // ─── Square (identity-best) ────────────────────────────────────────────

    public static void Solve_Identity3x3_PicksDiagonal()
    {
        // c[i,j] = 1 except diagonal = 0 → optimum is (0,0),(1,1),(2,2).
        var c = new double[]
        {
            0, 1, 1,
            1, 0, 1,
            1, 1, 0,
        };
        var r = HungarianAssigner.Solve(c, 3, 3);
        Assert(r[0] == 0 && r[1] == 1 && r[2] == 2,
            $"Diagonal-optimum got [{r[0]},{r[1]},{r[2]}]");
    }

    // ─── Square (off-diagonal optimum) ─────────────────────────────────────

    public static void Solve_AntiDiagonal3x3_PicksAntiDiagonal()
    {
        // Anti-diagonal is cheap: (0,2),(1,1),(2,0).
        var c = new double[]
        {
            5, 5, 0,
            5, 0, 5,
            0, 5, 5,
        };
        var r = HungarianAssigner.Solve(c, 3, 3);
        Assert(r[0] == 2 && r[1] == 1 && r[2] == 0,
            $"Anti-diagonal got [{r[0]},{r[1]},{r[2]}]");
    }

    // ─── Rectangular (M < N: more supply than demand) ──────────────────────

    public static void Solve_2x4_PicksTwoBestCols()
    {
        // 2 demand, 4 supply. Cheapest cols: 1 (cost 0) for row 0, 3 (cost 0)
        // for row 1.
        var c = new double[]
        {
            5, 0, 5, 5,
            5, 5, 5, 0,
        };
        var r = HungarianAssigner.Solve(c, 2, 4);
        Assert(r.Length == 2, $"2×4 → length 2; got {r.Length}");
        Assert(r[0] == 1 && r[1] == 3, $"Got [{r[0]},{r[1]}]");
    }

    // ─── Rectangular (M > N: more demand than supply) ──────────────────────

    public static void Solve_4x2_LeavesTwoUnassigned()
    {
        // 4 demand, 2 supply. 2 rows must be Unassigned (-1). Solver picks
        // the 2 lowest-cost rows.
        var c = new double[]
        {
            1.0, 1.0,
            10.0, 10.0,  // worst
            2.0, 2.0,
            10.0, 10.0,  // worst
        };
        var r = HungarianAssigner.Solve(c, 4, 2);
        Assert(r.Length == 4, $"4×2 → length 4; got {r.Length}");
        int assignedCount = 0;
        foreach (int v in r) if (v != HungarianAssigner.Unassigned) assignedCount++;
        Assert(assignedCount == 2, $"Expected 2 assigned (others Unassigned); got {assignedCount}");
    }

    // ─── Infeasible (all costs = Infeasible) ───────────────────────────────

    public static void Solve_AllInfeasible_AllUnassigned()
    {
        const int n = 3;
        var c = new double[n * n];
        for (int i = 0; i < c.Length; i++) c[i] = HungarianAssigner.Infeasible;
        var r = HungarianAssigner.Solve(c, n, n);
        foreach (int v in r)
            Assert(v == HungarianAssigner.Unassigned,
                $"All-infeasible matrix → all Unassigned; got {v}");
    }

    // ─── Mixed-feasibility ─────────────────────────────────────────────────

    public static void Solve_PartialFeasibility_RoutesAroundInfeasible()
    {
        // Row 0 can only go to col 0; row 1 can only go to col 1;
        // row 2 has multiple options but should pick col 2 because 0,1 taken.
        var c = new double[]
        {
            1, HungarianAssigner.Infeasible, HungarianAssigner.Infeasible,
            HungarianAssigner.Infeasible, 2, HungarianAssigner.Infeasible,
            5, 5, 3,
        };
        var r = HungarianAssigner.Solve(c, 3, 3);
        Assert(r[0] == 0 && r[1] == 1 && r[2] == 2,
            $"Partial-feasibility got [{r[0]},{r[1]},{r[2]}]");
    }

    public static void Solve_NullCost_Throws()
    {
        try { HungarianAssigner.Solve(null, 1, 1); }
        catch (ArgumentNullException) { return; }
        throw new InvalidOperationException("Null cost did not throw ArgumentNullException");
    }

    public static void Solve_BadDimensions_Throws()
    {
        // cost.Length != rows * cols → ArgumentException.
        try { HungarianAssigner.Solve(new double[5], 2, 3); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Bad dimensions did not throw ArgumentException");
    }

    // ─── Determinism ───────────────────────────────────────────────────────

    public static void Solve_Deterministic_SameInputSameOutput()
    {
        var c = new double[]
        {
            0.6, 0.7, 0.5,
            0.3, 0.9, 0.8,
            0.8, 0.4, 0.7,
        };
        var r1 = HungarianAssigner.Solve(c, 3, 3);
        var r2 = HungarianAssigner.Solve((double[])c.Clone(), 3, 3);
        for (int i = 0; i < r1.Length; i++)
            Assert(r1[i] == r2[i], $"Deterministic: r1[{i}]={r1[i]}, r2[{i}]={r2[i]}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Hungarian assertion failed: " + message);
    }
}

internal static class MatcherRegistryTests
{
    // ─── ConstraintDictionary ──────────────────────────────────────────────

    public static void ConstraintDictionary_NumericGe_PassesAndFails()
    {
        var cd = new ConstraintDictionary();
        cd.AddNumeric("Volume", ">=");
        var demand = MakeItem("D0", new Dictionary<string, double> { ["Volume"] = 100.0 }, null);
        var supplyOk = MakeItem("S0", new Dictionary<string, double> { ["Volume"] = 200.0 }, null);
        var supplyFail = MakeItem("S1", new Dictionary<string, double> { ["Volume"] = 50.0 }, null);
        Assert(cd.IsFeasible(demand, supplyOk), "200 >= 100 should be feasible");
        Assert(!cd.IsFeasible(demand, supplyFail), "50 >= 100 should fail");
    }

    public static void ConstraintDictionary_CategoricalEq_PassesAndFails()
    {
        var cd = new ConstraintDictionary();
        cd.AddCategorical("LithologyClass", "==");
        var demand = MakeItem("D0", null, new Dictionary<string, string> { ["LithologyClass"] = "granite" });
        var supplyOk = MakeItem("S0", null, new Dictionary<string, string> { ["LithologyClass"] = "granite" });
        var supplyFail = MakeItem("S1", null, new Dictionary<string, string> { ["LithologyClass"] = "marble" });
        Assert(cd.IsFeasible(demand, supplyOk), "granite == granite should be feasible");
        Assert(!cd.IsFeasible(demand, supplyFail), "marble == granite should fail");
    }

    public static void ConstraintDictionary_UnknownOperator_Throws()
    {
        var cd = new ConstraintDictionary();
        try { cd.AddNumeric("Foo", "@@invalid@@"); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Unknown operator did not throw");
    }

    // ─── IncidenceMatrix ───────────────────────────────────────────────────

    public static void IncidenceMatrix_Build_TabulatesFeasibility()
    {
        var demand = new List<MatchItem>
        {
            MakeItem("D0", new Dictionary<string, double> { ["Volume"] = 100.0 }, null),
            MakeItem("D1", new Dictionary<string, double> { ["Volume"] = 500.0 }, null),
        };
        var supply = new List<MatchItem>
        {
            MakeItem("S0", new Dictionary<string, double> { ["Volume"] = 150.0 }, null),
            MakeItem("S1", new Dictionary<string, double> { ["Volume"] = 200.0 }, null),
            MakeItem("S2", new Dictionary<string, double> { ["Volume"] = 600.0 }, null),
        };
        var cd = new ConstraintDictionary();
        cd.AddNumeric("Volume", ">=");
        var n = IncidenceMatrix.Build(demand, supply, cd);

        // D0 (vol 100): supply S0 (150), S1 (200), S2 (600) all feasible.
        Assert(n[0, 0] && n[0, 1] && n[0, 2], "D0 should match all three supplies");
        // D1 (vol 500): only S2 (600) feasible.
        Assert(!n[1, 0] && !n[1, 1] && n[1, 2], "D1 should match only S2");

        Assert(n.FeasibleCount == 4, $"FeasibleCount expected 4, got {n.FeasibleCount}");
    }

    // ─── WeightMatrix (NaN-sentinel discipline) ────────────────────────────

    public static void WeightMatrix_Build_NaNWhereInfeasible()
    {
        var demand = new List<MatchItem>
        {
            MakeItem("D0", new Dictionary<string, double> { ["Volume"] = 100.0 }, null),
        };
        var supply = new List<MatchItem>
        {
            MakeItem("S0", new Dictionary<string, double> { ["Volume"] = 150.0 }, null),  // feasible
            MakeItem("S1", new Dictionary<string, double> { ["Volume"] = 50.0 }, null),   // infeasible
        };
        var cd = new ConstraintDictionary();
        cd.AddNumeric("Volume", ">=");
        var n = IncidenceMatrix.Build(demand, supply, cd);
        var w = WeightMatrix.Build(demand, supply, n,
            new ConstantScore("yield_inverse", scoreValue: 0.42));

        Assert(!double.IsNaN(w[0, 0]), "Feasible cell should NOT be NaN");
        Assert(Math.Abs(w[0, 0] - 0.42) < 1e-9, $"Feasible cell should be 0.42; got {w[0, 0]}");
        Assert(double.IsNaN(w[0, 1]), "Infeasible cell SHOULD be NaN");
        Assert(w.IsSparse(0, 1), "Infeasible cell SHOULD report IsSparse=true");
    }

    // ─── MatcherRegistry ───────────────────────────────────────────────────

    public static void MatcherRegistry_RegisterAndRun()
    {
        // Reset any prior registration of "test_hungarian" by re-registering.
        MatcherRegistry.Register("test_hungarian", new HungarianTestSolver());
        Assert(MatcherRegistry.IsRegistered("test_hungarian"), "Registered solver should be retrievable");

        var demand = new List<MatchItem>
        {
            MakeItem("D0", new Dictionary<string, double> { ["Volume"] = 100.0 }, null),
            MakeItem("D1", new Dictionary<string, double> { ["Volume"] = 200.0 }, null),
        };
        var supply = new List<MatchItem>
        {
            MakeItem("S0", new Dictionary<string, double> { ["Volume"] = 150.0 }, null),
            MakeItem("S1", new Dictionary<string, double> { ["Volume"] = 250.0 }, null),
        };
        var cd = new ConstraintDictionary();
        cd.AddNumeric("Volume", ">=");
        var ctx = MatcherContextBuilder.Build(demand, supply, cd,
            new YieldScore("yield"));
        var result = MatcherRegistry.Run("test_hungarian", ctx);

        Assert(result != null, "MatcherRegistry.Run returned null");
        Assert(result.SolverName == "test_hungarian",
            $"SolverName expected 'test_hungarian'; got '{result.SolverName}'");
        Assert(result.Pairs.Count == 2, $"Expected 2 pairs; got {result.Pairs.Count}");
        Assert(ctx.SolutionTimeSeconds >= 0, "SolutionTimeSeconds should be non-negative");
    }

    public static void MatcherRegistry_UnknownSolver_Throws()
    {
        var demand = new List<MatchItem> { MakeItem("D0", null, null) };
        var supply = new List<MatchItem> { MakeItem("S0", null, null) };
        var cd = new ConstraintDictionary();
        var ctx = MatcherContextBuilder.Build(demand, supply, cd,
            new ConstantScore("zero", 0));
        try { MatcherRegistry.Run("nonexistent_solver_xyz", ctx); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Unknown solver did not throw");
    }

    // ─── End-to-end (Voussoir-shape with Hungarian) ───────────────────────

    public static void EndToEnd_TwoVoussoirsTwoStones_OptimalAssignment()
    {
        // Two voussoirs of different volumes; two stones with the matching
        // volume capacities. Optimum is straight pairing.
        var voussoirs = new List<MatchItem>
        {
            MakeItem("V0", new Dictionary<string, double> { ["Volume"] = 100.0 }, null),
            MakeItem("V1", new Dictionary<string, double> { ["Volume"] = 500.0 }, null),
        };
        var stones = new List<MatchItem>
        {
            MakeItem("S0", new Dictionary<string, double> { ["Volume"] = 120.0 }, null),  // best for V0
            MakeItem("S1", new Dictionary<string, double> { ["Volume"] = 600.0 }, null),  // best for V1
        };
        var cd = new ConstraintDictionary();
        cd.AddNumeric("Volume", ">=");
        var ctx = MatcherContextBuilder.Build(voussoirs, stones, cd,
            new YieldScore("yield"));
        MatcherRegistry.Register("e2e_hungarian", new HungarianTestSolver());
        var r = MatcherRegistry.Run("e2e_hungarian", ctx);

        Assert(r.Pairs.Count == 2, "End-to-end: 2 pairs");
        // Find V0's pair.
        var p0 = r.Pairs.FirstOrDefault(p => p.DemandIndex == 0);
        var p1 = r.Pairs.FirstOrDefault(p => p.DemandIndex == 1);
        Assert(p0.SupplyIndex == 0, $"V0 should pair with S0; got S{p0.SupplyIndex}");
        Assert(p1.SupplyIndex == 1, $"V1 should pair with S1; got S{p1.SupplyIndex}");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static MatchItem MakeItem(string id,
        Dictionary<string, double> numeric,
        Dictionary<string, string> categorical)
    {
        return new MatchItem(id, numeric, categorical);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("MatcherRegistry assertion failed: " + message);
    }
}

// ─── Test-side score functions ─────────────────────────────────────────────

internal sealed class ConstantScore : IScoreFunction
{
    private readonly double _value;
    public ConstantScore(string name, double scoreValue) { Name = name; _value = scoreValue; }
    public string Name { get; }
    public double Score(MatchItem demand, MatchItem supply) => _value;
}

internal sealed class YieldScore : IScoreFunction
{
    public YieldScore(string name) { Name = name; }
    public string Name { get; }
    public double Score(MatchItem demand, MatchItem supply)
    {
        double dv = demand.GetNumeric("Volume");
        double sv = supply.GetNumeric("Volume");
        if (sv <= 0 || double.IsNaN(sv)) return 1e9;
        return 1.0 - dv / sv;
    }
}

// ─── Test-side Hungarian wrapper as an ISolver ─────────────────────────────

internal sealed class HungarianTestSolver : ISolver
{
    public string Name => "test_hungarian";

    public AssignmentResult Solve(MatcherContext ctx)
    {
        int m = ctx.Demand.Count;
        int n = ctx.Supply.Count;
        var cost = new double[m * n];
        for (int i = 0; i < m; i++)
        for (int j = 0; j < n; j++)
        {
            cost[i * n + j] = ctx.Incidence[i, j]
                ? ctx.Weights[i, j]
                : HungarianAssigner.Infeasible;
        }
        var assignment = HungarianAssigner.Solve(cost, m, n);
        var pairs = new List<Pair>();
        var unassigned = new List<int>();
        var used = new HashSet<int>();
        double total = 0;
        for (int i = 0; i < m; i++)
        {
            int j = assignment[i];
            if (j == HungarianAssigner.Unassigned)
            {
                unassigned.Add(i);
                continue;
            }
            pairs.Add(new Pair(i, j, cost[i * n + j]));
            used.Add(j);
            total += cost[i * n + j];
        }
        var unused = new List<int>();
        for (int j = 0; j < n; j++) if (!used.Contains(j)) unused.Add(j);
        return new AssignmentResult(Name, pairs, unassigned, unused, total,
            new List<string> { $"HungarianTestSolver: {pairs.Count} pairs, {unassigned.Count} unassigned." });
    }
}
