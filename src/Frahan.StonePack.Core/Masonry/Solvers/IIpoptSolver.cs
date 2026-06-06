#nullable disable

namespace Frahan.Masonry.Solvers;

// =============================================================================
// IIpoptSolver — sub-interface of IConvexQpSolver that documents the
// contract for a future native IPOPT binding. Phase C of the compas_cra
// port (Kao 2022 §6: full nonlinear coupling with friction multipliers) is
// the long-term goal; the structure here lets a concrete IPOPT-backed
// implementation drop in without changing the rest of the masonry pipeline.
//
// No native binding is shipped today. IpoptManagedStub returns
// NotImplemented; the user's MasonrySolverRegistry continues to use
// ManagedQpSolver until an IPOPT DLL is bound (see notes in
// IpoptManagedStub for the entry-point contract).
// =============================================================================

/// <summary>
/// Marker interface for an IPOPT-backed convex QP / NLP solver. Distinct
/// from the plain <see cref="IConvexQpSolver"/> so callers can opt in via
/// <c>MasonrySolverRegistry.UseIpoptIfAvailable()</c> when an IPOPT
/// implementation is available; otherwise the registry falls back to
/// <see cref="ManagedQpSolver"/>.
/// </summary>
public interface IIpoptSolver : IConvexQpSolver
{
    /// <summary>
    /// True when the underlying IPOPT native library is loaded and ready.
    /// A managed stub returns <c>false</c>.
    /// </summary>
    bool IsAvailable { get; }
}
