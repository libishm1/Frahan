#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// IpoptManagedStub — placeholder IIpoptSolver that returns NotImplemented
// for every Solve call and reports IsAvailable = false. The point of this
// stub is to keep the registry / fallback machinery testable without
// shipping a native IPOPT binary.
//
// To plug in real IPOPT later:
//   1. Add Frahan.Native.Ipopt.dll to the install footprint.
//   2. Implement a P/Invoke wrapper exposing
//        - Ipopt::CreateIpoptApplication
//        - Ipopt::SetIntegerValue / SetNumericValue
//        - Ipopt::SolveProblem  (with TNLP-style callback for f, g, jac, hess)
//   3. Translate ConvexQpProblem fields into IPOPT's TNLP API:
//        Hessian       -> eval_h
//        LinearObjective -> eval_grad_f / eval_f
//        EqualityMatrix / EqualityRhs / InequalityMatrix / InequalityRhs
//                      -> eval_jac_g  (and lower/upper g_l/g_u bounds)
//        LowerBounds / UpperBounds -> x_l / x_u
//   4. Map IPOPT's status codes onto ConvexQpStatus
//      (Solve_Succeeded => Optimal, Solved_To_Acceptable_Level => Optimal,
//       Infeasible_Problem_Detected => Infeasible, others => SolverError).
//   5. Replace IpoptManagedStub with the new implementation in
//      MasonrySolverRegistry.UseIpoptIfAvailable().
// =============================================================================

/// <summary>
/// Managed stub for <see cref="IIpoptSolver"/>. Always reports
/// <see cref="ConvexQpStatus.NotImplemented"/> with a message documenting
/// the missing binding. Useful in tests and as the negative case for
/// <c>MasonrySolverRegistry.UseIpoptIfAvailable</c>.
/// </summary>
public sealed class IpoptManagedStub : IIpoptSolver
{
    public string Name => "IpoptManagedStub";

    public bool IsAvailable => false;

    public ConvexQpResult Solve(ConvexQpProblem problem)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        return new ConvexQpResult(
            status: ConvexQpStatus.NotImplemented,
            x: null,
            objectiveValue: 0.0,
            solverMessage:
                "IpoptManagedStub: native IPOPT binding is not present. " +
                "Install Frahan.Native.Ipopt.dll and switch the registry to " +
                "the real binding (see IpoptManagedStub.cs comments) or use " +
                "ManagedQpSolver for diagonal-H equality-bound problems.");
    }
}
