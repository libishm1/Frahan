#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Frahan.EdgeMatching.Matching;

// =============================================================================
// MatcherRegistry + substrate types — Phase 1 of the component decomposition
// per wiki/specs/component_decomposition_plan.md and the architectural
// decisions locked in wiki/specs/architectural_decisions_2026-05-31.md SS1-SS4.
//
// Mirrors structuralCircle's Matching class / helper_methods.py / @_matching_decorator
// pattern (Tomczak / Haakonsen / Luczkowski 2023, DOI 10.1088/2634-4505/acf341).
//
// Key shape:
//   * MatcherContext   = the canonical state record (demand + supply + constraints +
//                        incidence + weight + result). Pre-populated by a builder
//                        before any solver runs. structuralCircle Matching class
//                        constructor analogue.
//   * ConstraintDictionary = type-level numeric + categorical operators. Replaces
//                            structuralCircle's runtime isinstance branch on string
//                            vs numeric demand.
//   * IncidenceMatrix  = bool feasibility matrix N_ij. Computed ONCE upstream of
//                        any solver.
//   * WeightMatrix     = double cost matrix W_ij with NaN sentinels where N_ij=False.
//   * ISolver          = the seven structuralCircle methods as a single interface.
//                        Each implementation does ONLY assignment; the registry
//                        wraps reset / timing / score / log (mirrors
//                        @_matching_decorator at lines 226-247 of matching.py).
//   * MatcherRegistry  = the Dictionary<string, ISolver> dispatcher. Replaces the
//                        11-boolean-flag run_matching() signature explicitly
//                        rejected in architectural_decisions SS2.
//   * AssignmentResult = the typed-record output every solver emits.
//
// Per Frahan-original "no invented citations": each interface / class header
// names the source pattern verbatim and the dossier reference.
// =============================================================================

// ----------------------------------------------------------------------------
// MatcherContext  — the canonical "matching context" state record.
// ----------------------------------------------------------------------------

/// <summary>
/// Canonical state for a matching problem instance. Mirrors structuralCircle
/// Matching class (matching.py lines 89-130) — state assembled before any
/// solver runs. Pre-populated by a builder (see <see cref="MatcherContextBuilder"/>).
/// </summary>
public sealed class MatcherContext
{
    /// <summary>Demand items (e.g. designed templates, voussoirs, panel slots).</summary>
    public IReadOnlyList<MatchItem> Demand { get; set; }

    /// <summary>Supply items (e.g. quarry stones, rubble, scanned inventory).</summary>
    public IReadOnlyList<MatchItem> Supply { get; set; }

    /// <summary>Feasibility constraints (per-property numeric + categorical operators).</summary>
    public ConstraintDictionary Constraints { get; set; }

    /// <summary>Scoring function that produces the cost weight for a feasible pair.</summary>
    public IScoreFunction Score { get; set; }

    /// <summary>Pre-computed feasibility matrix N_ij. Built ONCE upstream of any solver.</summary>
    public IncidenceMatrix Incidence { get; set; }

    /// <summary>Pre-computed weight matrix W_ij. NaN where Incidence is False.</summary>
    public WeightMatrix Weights { get; set; }

    /// <summary>The assignment result. Filled in by the solver via <see cref="MatcherRegistry.Run"/>.</summary>
    public AssignmentResult Result { get; set; }

    /// <summary>Solve time in seconds. Filled by the registry wrapper.</summary>
    public double SolutionTimeSeconds { get; set; }
}

/// <summary>
/// A single demand or supply item carries a property bag plus a stable ID.
/// Properties drive the constraint evaluation and the weight computation.
/// </summary>
public sealed class MatchItem
{
    public MatchItem(string id, IReadOnlyDictionary<string, double> numeric,
        IReadOnlyDictionary<string, string> categorical)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Numeric = numeric ?? new Dictionary<string, double>();
        Categorical = categorical ?? new Dictionary<string, string>();
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, double> Numeric { get; }
    public IReadOnlyDictionary<string, string> Categorical { get; }

    public double GetNumeric(string key) => Numeric.TryGetValue(key, out var v) ? v : double.NaN;
    public string GetCategorical(string key) => Categorical.TryGetValue(key, out var v) ? v : null;
}

// ----------------------------------------------------------------------------
// ConstraintDictionary — type-level numeric + categorical operator split.
// ----------------------------------------------------------------------------

/// <summary>
/// Per-property comparison operators. structuralCircle's Listing 2:
/// <c>{'Area':'>=', 'Length':'>=', 'Moment of Inertia':'>=', 'Material':'=='}</c>.
/// Frahan splits numeric vs categorical at the type level (architectural
/// decisions SS5) to avoid the runtime isinstance branch.
/// </summary>
public sealed class ConstraintDictionary
{
    private readonly Dictionary<string, Func<double, double, bool>> _numeric;
    private readonly Dictionary<string, Func<string, string, bool>> _categorical;

    public ConstraintDictionary()
    {
        _numeric = new Dictionary<string, Func<double, double, bool>>();
        _categorical = new Dictionary<string, Func<string, string, bool>>();
    }

    /// <summary>Add a numeric constraint: supply[property] OP demand[property].</summary>
    public void AddNumeric(string property, string op)
    {
        if (!NumericOps.TryGetValue(op, out var fn))
            throw new ArgumentException(
                $"Unknown numeric operator '{op}'. Supported: {string.Join(", ", NumericOps.Keys)}.");
        _numeric[property] = fn;
    }

    /// <summary>Add a categorical constraint: supply[property] OP demand[property].</summary>
    public void AddCategorical(string property, string op)
    {
        if (!CategoricalOps.TryGetValue(op, out var fn))
            throw new ArgumentException(
                $"Unknown categorical operator '{op}'. Supported: {string.Join(", ", CategoricalOps.Keys)}.");
        _categorical[property] = fn;
    }

    /// <summary>Evaluate all constraints between a demand item and a supply item.</summary>
    public bool IsFeasible(MatchItem demand, MatchItem supply)
    {
        foreach (var kv in _numeric)
        {
            double d = demand.GetNumeric(kv.Key);
            double s = supply.GetNumeric(kv.Key);
            if (double.IsNaN(d) || double.IsNaN(s)) return false;
            if (!kv.Value(s, d)) return false;
        }
        foreach (var kv in _categorical)
        {
            string d = demand.GetCategorical(kv.Key);
            string s = supply.GetCategorical(kv.Key);
            if (d == null || s == null) return false;
            if (!kv.Value(s, d)) return false;
        }
        return true;
    }

    public IReadOnlyDictionary<string, Func<double, double, bool>> Numeric => _numeric;
    public IReadOnlyDictionary<string, Func<string, string, bool>> Categorical => _categorical;

    // Canonical operator lookup. Replaces structuralCircle's
    // `numexpr.evaluate(f'{var} {compare} demand_array')` string-eval trick
    // with a typed-delegate dictionary (architectural decisions SS5).
    private static readonly Dictionary<string, Func<double, double, bool>> NumericOps =
        new Dictionary<string, Func<double, double, bool>>
        {
            [">="] = (s, d) => s >= d,
            ["<="] = (s, d) => s <= d,
            ["=="] = (s, d) => s == d,
            ["!="] = (s, d) => s != d,
            [">"]  = (s, d) => s >  d,
            ["<"]  = (s, d) => s <  d,
        };

    private static readonly Dictionary<string, Func<string, string, bool>> CategoricalOps =
        new Dictionary<string, Func<string, string, bool>>
        {
            ["=="] = (s, d) => string.Equals(s, d, StringComparison.Ordinal),
            ["!="] = (s, d) => !string.Equals(s, d, StringComparison.Ordinal),
        };
}

// ----------------------------------------------------------------------------
// IncidenceMatrix + WeightMatrix — the separated boolean / float matrices.
// ----------------------------------------------------------------------------

/// <summary>
/// Boolean feasibility matrix N_ij = ConstraintDictionary.IsFeasible(D_i, S_j).
/// Computed ONCE upstream of any solver (Tomczak 2023 Table 2; architectural
/// decisions SS4).
/// </summary>
public sealed class IncidenceMatrix
{
    public IncidenceMatrix(int demandCount, int supplyCount)
    {
        DemandCount = demandCount;
        SupplyCount = supplyCount;
        _data = new bool[demandCount * supplyCount];
    }

    public int DemandCount { get; }
    public int SupplyCount { get; }
    private readonly bool[] _data;

    public bool this[int i, int j]
    {
        get => _data[i * SupplyCount + j];
        set => _data[i * SupplyCount + j] = value;
    }

    /// <summary>Build from a ConstraintDictionary applied to demand × supply.</summary>
    public static IncidenceMatrix Build(IReadOnlyList<MatchItem> demand,
        IReadOnlyList<MatchItem> supply, ConstraintDictionary constraints)
    {
        var n = new IncidenceMatrix(demand.Count, supply.Count);
        for (int i = 0; i < demand.Count; i++)
        for (int j = 0; j < supply.Count; j++)
            n[i, j] = constraints.IsFeasible(demand[i], supply[j]);
        return n;
    }

    public int FeasibleCount
    {
        get
        {
            int c = 0;
            for (int k = 0; k < _data.Length; k++) if (_data[k]) c++;
            return c;
        }
    }
}

/// <summary>
/// Float cost matrix W_ij. NaN where the incidence is False — solvers skip
/// those cells. Mirrors structuralCircle's
/// <c>np.full(self.incidence.shape, np.nan)</c> at matching.py lines 204-213.
/// </summary>
public sealed class WeightMatrix
{
    public WeightMatrix(int demandCount, int supplyCount)
    {
        DemandCount = demandCount;
        SupplyCount = supplyCount;
        _data = new double[demandCount * supplyCount];
        for (int k = 0; k < _data.Length; k++) _data[k] = double.NaN;
    }

    public int DemandCount { get; }
    public int SupplyCount { get; }
    private readonly double[] _data;

    public double this[int i, int j]
    {
        get => _data[i * SupplyCount + j];
        set => _data[i * SupplyCount + j] = value;
    }

    public bool IsSparse(int i, int j) => double.IsNaN(_data[i * SupplyCount + j]);

    /// <summary>Populate W_ij = score(D_i, S_j) for all cells where N_ij = True.</summary>
    public static WeightMatrix Build(IReadOnlyList<MatchItem> demand,
        IReadOnlyList<MatchItem> supply, IncidenceMatrix incidence,
        IScoreFunction score)
    {
        var w = new WeightMatrix(demand.Count, supply.Count);
        for (int i = 0; i < demand.Count; i++)
        for (int j = 0; j < supply.Count; j++)
            if (incidence[i, j]) w[i, j] = score.Score(demand[i], supply[j]);
        return w;
    }
}

// ----------------------------------------------------------------------------
// IScoreFunction — the cost-per-pair primitive.
// ----------------------------------------------------------------------------

/// <summary>
/// Per-pair scoring. Implementations: yield-cost, grain-cost, carving-cost,
/// Hausdorff-cost, GWP-cost (structuralCircle), composite-weighted-cost. See
/// component_decomposition_plan.md §5.3.
/// </summary>
public interface IScoreFunction
{
    string Name { get; }
    double Score(MatchItem demand, MatchItem supply);
}

// ----------------------------------------------------------------------------
// AssignmentResult — the typed-record output every solver emits.
// ----------------------------------------------------------------------------

/// <summary>Per-pair assignment outcome.</summary>
public readonly struct Pair
{
    public Pair(int demandIndex, int supplyIndex, double cost)
    {
        DemandIndex = demandIndex;
        SupplyIndex = supplyIndex;
        Cost = cost;
    }
    public int DemandIndex { get; }
    public int SupplyIndex { get; }
    public double Cost { get; }
}

/// <summary>
/// The canonical output of any solver. Includes the assignment, unassigned
/// demand indices, unused supply indices, total objective, and the
/// solver-supplied diagnostic remarks.
/// </summary>
public sealed class AssignmentResult
{
    public AssignmentResult(string solverName, IReadOnlyList<Pair> pairs,
        IReadOnlyList<int> unassignedDemand, IReadOnlyList<int> unusedSupply,
        double totalCost, IReadOnlyList<string> remarks)
    {
        SolverName = solverName;
        Pairs = pairs;
        UnassignedDemand = unassignedDemand;
        UnusedSupply = unusedSupply;
        TotalCost = totalCost;
        Remarks = remarks ?? new List<string>();
    }

    public string SolverName { get; }
    public IReadOnlyList<Pair> Pairs { get; }
    public IReadOnlyList<int> UnassignedDemand { get; }
    public IReadOnlyList<int> UnusedSupply { get; }
    public double TotalCost { get; }
    public IReadOnlyList<string> Remarks { get; }
}

// ----------------------------------------------------------------------------
// ISolver — the seven structuralCircle methods as one interface.
// ----------------------------------------------------------------------------

/// <summary>
/// One solver = one matching algorithm. Implementations:
/// - GreedySingleSolver, GreedyPluralSolver
/// - HungarianSolver (uses HungarianAssigner.cs)
/// - BipartiteSolver
/// - MilpSolver (Google.OrTools NuGet)
/// - NsgaIIParetoSolver (v1.x Frahan-original ~300 LoC)
/// - BruteForceSolver (oracle for tests)
///
/// Each implementation does ONLY the assignment logic (mirrors
/// structuralCircle's seven @_matching_decorator-wrapped methods at
/// matching.py lines 250-650). Outer concerns — reset / time / score /
/// log — owned by <see cref="MatcherRegistry"/>.
/// </summary>
public interface ISolver
{
    /// <summary>Canonical key under which the registry exposes this solver.</summary>
    string Name { get; }

    /// <summary>Solve the matching problem. Returns the AssignmentResult.</summary>
    AssignmentResult Solve(MatcherContext context);
}

// ----------------------------------------------------------------------------
// MatcherRegistry — the dispatcher. Replaces structuralCircle's 11-bool flag.
// ----------------------------------------------------------------------------

/// <summary>
/// The dispatcher. Owns reset / timing / score / log per the
/// @_matching_decorator pattern (matching.py lines 226-247).
/// Implements the architectural decisions SS2-SS3 calls verbatim.
///
/// Usage:
///   <code>
///   MatcherRegistry.Register("hungarian", new HungarianSolver());
///   var result = MatcherRegistry.Run("hungarian", context);
///   </code>
/// </summary>
public static class MatcherRegistry
{
    private static readonly Dictionary<string, ISolver> _solvers =
        new Dictionary<string, ISolver>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register an ISolver implementation under a canonical key.</summary>
    public static void Register(string name, ISolver solver)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Solver name must not be empty.", nameof(name));
        if (solver == null) throw new ArgumentNullException(nameof(solver));
        _solvers[name] = solver;
    }

    /// <summary>True if a solver with this name is registered.</summary>
    public static bool IsRegistered(string name) => _solvers.ContainsKey(name);

    /// <summary>The registered solver names (for UI dropdowns + diagnostics).</summary>
    public static IReadOnlyCollection<string> Names => _solvers.Keys;

    /// <summary>
    /// Run the named solver against the context. The wrapper owns:
    /// (1) timing, (2) result population on context, (3) log entry. The
    /// solver implementation does ONLY assignment.
    /// </summary>
    public static AssignmentResult Run(string solverName, MatcherContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!_solvers.TryGetValue(solverName, out var solver))
            throw new ArgumentException(
                $"Solver '{solverName}' is not registered. Registered: " +
                $"{string.Join(", ", _solvers.Keys)}.", nameof(solverName));
        if (context.Incidence == null || context.Weights == null)
            throw new InvalidOperationException(
                "MatcherContext.Incidence and Weights must be populated " +
                "before Run. Use MatcherContextBuilder upstream.");

        var sw = Stopwatch.StartNew();
        var result = solver.Solve(context);
        sw.Stop();

        context.Result = result;
        context.SolutionTimeSeconds = sw.Elapsed.TotalSeconds;
        return result;
    }
}

// ----------------------------------------------------------------------------
// MatcherContextBuilder — the upstream pre-processing helper.
// ----------------------------------------------------------------------------

/// <summary>
/// Pre-processing helper that pre-populates a MatcherContext before any
/// solver runs. Equivalent to structuralCircle Matching.__init__ +
/// evaluate_incidence + evaluate_weights pre-pass.
/// </summary>
public static class MatcherContextBuilder
{
    public static MatcherContext Build(
        IReadOnlyList<MatchItem> demand,
        IReadOnlyList<MatchItem> supply,
        ConstraintDictionary constraints,
        IScoreFunction score)
    {
        if (demand == null) throw new ArgumentNullException(nameof(demand));
        if (supply == null) throw new ArgumentNullException(nameof(supply));
        if (constraints == null) throw new ArgumentNullException(nameof(constraints));
        if (score == null) throw new ArgumentNullException(nameof(score));

        var ctx = new MatcherContext
        {
            Demand = demand,
            Supply = supply,
            Constraints = constraints,
            Score = score,
        };
        ctx.Incidence = IncidenceMatrix.Build(demand, supply, constraints);
        ctx.Weights = WeightMatrix.Build(demand, supply, ctx.Incidence, score);
        return ctx;
    }
}
