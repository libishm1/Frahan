#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// IConvexQpSolver — boundary between the masonry-specific QP formulation
// (RbeQpFormulation) and any concrete solver back-end. The same interface
// is consumed by:
//   * a pure-managed default (ManagedQpSolver, landed; default registration
//     happens via MasonrySolverRegistry.EnsureDefaultSolver in plugin OnLoad
//     and defensively from the GH component itself), and
//   * an optional IPOPT P/Invoke implementation (IpoptNlpSolver, Phase C,
//     not yet wired — IpoptManagedStub currently reports IsAvailable=false).
//
// Source / inspiration: Kao 2022, BlockResearchGroup/compas_cra (MIT).
// The masonry RBE problem is a convex QP, so we deliberately keep this
// interface QP-shaped (Hessian + linear objective + equality + inequality +
// box bounds). NLP-only features (e.g. nonlinear inequalities) are not in
// the boundary; an IPOPT adapter must lower the QP to NLP form internally.
// =============================================================================

/// <summary>
/// Solves a strictly-convex (or convex) QP of the form
///   min  ½ x^T H x + c^T x
///   s.t. Aeq x = beq
///        Aineq x &lt;= bineq
///        lb &lt;= x &lt;= ub
/// where any of the constraint blocks may be empty (null or zero rows).
///
/// Implementations:
///   ManagedQpSolver — pure-managed Dykstra default (landed 2026-05-15)
///   IpoptNlpSolver  — P/Invoke around IPOPT (Phase C, not yet wired)
/// </summary>
public interface IConvexQpSolver
{
    /// <summary>Short identifier (e.g. "ManagedQpSolver", "IpoptNlpSolver"). Used in logs and result messages.</summary>
    string Name { get; }

    /// <summary>Solve the supplied problem. Implementations must not mutate <paramref name="problem"/>.</summary>
    ConvexQpResult Solve(ConvexQpProblem problem);
}

/// <summary>
/// Termination status returned by an <see cref="IConvexQpSolver"/>.
/// </summary>
public enum ConvexQpStatus
{
    /// <summary>An optimal primal solution was found within the solver's tolerances.</summary>
    Optimal,

    /// <summary>The constraint set is empty: no x satisfies the equalities, inequalities, and bounds simultaneously.</summary>
    Infeasible,

    /// <summary>Objective is unbounded below over the feasible set (only possible when H is not strictly positive definite).</summary>
    Unbounded,

    /// <summary>Solver internal failure (numerical breakdown, max-iterations without convergence, etc.).</summary>
    SolverError,

    /// <summary>The selected solver implementation does not yet handle this problem shape.</summary>
    NotImplemented,
}

/// <summary>
/// Result of an <see cref="IConvexQpSolver.Solve"/> call.
/// </summary>
public sealed class ConvexQpResult
{
    /// <summary>
    /// Construct a solver result. When <paramref name="status"/> is <see cref="ConvexQpStatus.Optimal"/>,
    /// both <paramref name="x"/> and <paramref name="solverMessage"/> must be non-null. For any other
    /// status, either may be null (e.g. infeasible problems have no x to report).
    /// </summary>
    public ConvexQpResult(
        ConvexQpStatus status,
        double[] x,
        double objectiveValue,
        string solverMessage)
    {
        if (status == ConvexQpStatus.Optimal)
        {
            if (x == null)
                throw new ArgumentNullException(nameof(x), "Optimal status requires a non-null primal solution vector.");
            if (solverMessage == null)
                throw new ArgumentNullException(nameof(solverMessage), "Optimal status requires a non-null solver message.");
        }

        Status = status;
        X = x;
        ObjectiveValue = objectiveValue;
        SolverMessage = solverMessage;
    }

    /// <summary>Termination status; see <see cref="ConvexQpStatus"/>.</summary>
    public ConvexQpStatus Status { get; }

    /// <summary>Primal solution vector. Length equals <c>ConvexQpProblem.VariableCount</c> when <see cref="Status"/> is Optimal; may be null otherwise.</summary>
    public double[] X { get; }

    /// <summary>Objective value <c>½ X^T H X + c^T X</c> at the returned X. Undefined when <see cref="Status"/> is not Optimal.</summary>
    public double ObjectiveValue { get; }

    /// <summary>Free-form diagnostic from the solver (iteration count, residuals, error reason, etc.). May be null when not Optimal.</summary>
    public string SolverMessage { get; }

    public override string ToString() =>
        $"ConvexQpResult(status={Status}, obj={ObjectiveValue:G6}, n={(X == null ? 0 : X.Length)})";
}
