#nullable disable

namespace Frahan.Masonry.Solvers;

// =============================================================================
// MasonrySolverRegistry — process-wide service locator for the convex QP solver
// used by the Masonry stability components.
//
// Phase B / Phase C of the masonry port will plug in concrete solvers
// (ManagedQpSolver, IpoptNlpSolver). Until they exist, the registry stays
// null and consumer code (e.g. MasonryStabilityRbeComponent) must cope by
// reporting an explicit "no solver registered" verdict to the user.
//
// The registry is intentionally a plain mutable static so an init module can
// assign it once at plugin load:
//
//     MasonrySolverRegistry.Default = new ManagedQpSolver();
//
// Tests can also assign and reset it freely. Thread safety is not required
// at this stage: assignment happens once during plugin OnLoad and reads
// happen on the GH worker thread (or pool thread for the async component).
// If contention ever becomes a real risk, this can be promoted to a
// volatile field or wrapped in a lock without changing the public API.
// =============================================================================

/// <summary>
/// Process-wide registry for the default <see cref="IConvexQpSolver"/>
/// used by the Masonry stability components.
/// </summary>
/// <remarks>
/// Returns <c>null</c> until an init module assigns a concrete solver.
/// Consumers should treat <c>null</c> as a recoverable "no solver registered"
/// state and surface it to the user, not throw.
/// </remarks>
public static class MasonrySolverRegistry
{
    /// <summary>
    /// The currently-registered default solver, or <c>null</c> if none has
    /// been set. Set this once during plugin initialisation.
    /// </summary>
    public static IConvexQpSolver Default { get; set; }

    /// <summary>
    /// Idempotent wiring: assigns a fresh <see cref="ManagedQpSolver"/> to
    /// <see cref="Default"/> only if no solver is currently registered.
    /// Intended to be called from the Rhino plugin's <c>OnLoad</c> so the
    /// stability components have a working solver out of the box, while
    /// still letting external init code (or tests) pre-register a different
    /// solver before plugin load.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this call assigned a new solver, <c>false</c> if a
    /// solver was already registered and this call was a no-op.
    /// </returns>
    public static bool EnsureDefaultSolver()
    {
        if (Default != null) return false;
        Default = new ManagedQpSolver();
        return true;
    }

    /// <summary>
    /// Probe for a native IPOPT binding and install it as the default solver
    /// when available. Falls back to <see cref="ManagedQpSolver"/> if not.
    /// Returns the registered solver name.
    /// </summary>
    /// <remarks>
    /// Today the only <see cref="IIpoptSolver"/> implementation is
    /// <see cref="IpoptManagedStub"/>, which reports
    /// <see cref="IIpoptSolver.IsAvailable"/> = <c>false</c>; this method
    /// therefore always falls back to ManagedQpSolver. Once a real
    /// P/Invoke binding ships, replace the stub instantiation below with
    /// the real implementation and the rest of the masonry pipeline will
    /// pick it up automatically.
    /// </remarks>
    public static string UseIpoptIfAvailable()
    {
        IIpoptSolver candidate = new IpoptManagedStub();
        if (candidate.IsAvailable)
        {
            Default = candidate;
            return candidate.Name;
        }
        if (Default == null) Default = new ManagedQpSolver();
        return Default.Name;
    }

    /// <summary>
    /// Probe for the native OSQP binding (frahan_osqp.dll) and install
    /// <see cref="OsqpQpSolver"/> as the default when available.
    ///
    /// Priority ladder (first available wins):
    ///   1. OsqpQpSolver   — native OSQP (~10-50× faster than ADMM for walls > 50 interfaces)
    ///   2. AdmmQpSolver   — pure-managed ADMM fallback (always available)
    ///
    /// ManagedQpSolver (Dykstra) is no longer in the ladder: it diverges on
    /// mixed-scale masonry RBE systems.  AdmmQpSolver is the reliable managed
    /// fallback.
    ///
    /// Returns the name of the solver that was registered.
    /// </summary>
    public static string UseOsqpIfAvailable()
    {
        if (Default != null) return Default.Name;

        if (OsqpQpSolver.IsAvailable)
        {
            Default = new OsqpQpSolver();
            return Default.Name;
        }

        // Fallback: robust managed ADMM (pure C#, no native deps).
        Default = new AdmmQpSolver();
        return Default.Name;
    }

    /// <summary>
    /// Returns a human-readable string describing the current default solver
    /// and its source (native vs managed), for use in GH component messages.
    /// </summary>
    public static string SolverDescription()
    {
        if (Default is OsqpQpSolver)
            return "OSQP native (frahan_osqp v" + OsqpQpSolver.NativeVersion + ")";
        if (Default is AdmmQpSolver)
            return "ADMM managed (pure C# fallback — build native/osqp_shim for 10-50x speedup)";
        if (Default is ManagedQpSolver)
            return "Dykstra managed (legacy — prefer AdmmQpSolver or OSQP)";
        return Default != null ? Default.Name : "(no solver)";
    }
}
