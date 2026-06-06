#nullable disable
using System;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// Stage D: IPOPT shim — IIpoptSolver / IpoptManagedStub /
// MasonrySolverRegistry.UseIpoptIfAvailable. Verifies the shim shape and
// the registry probe semantics.

static class StageDIpoptTests
{
    public static void IpoptStub_IsAvailable_IsFalse()
    {
        var stub = new IpoptManagedStub();
        Assert(!stub.IsAvailable, "IpoptManagedStub.IsAvailable should be false");
    }

    public static void IpoptStub_Name_IsExpected()
    {
        var stub = new IpoptManagedStub();
        Assert(stub.Name == "IpoptManagedStub", $"Name '{stub.Name}'");
    }

    public static void IpoptStub_Solve_ReturnsNotImplemented()
    {
        var problem = new ConvexQpProblem(
            variableCount: 1,
            hessian: new double[,] { { 1.0 } },
            linearObjective: new double[1],
            equalityMatrix: null, equalityRhs: null,
            inequalityMatrix: null, inequalityRhs: null,
            lowerBounds: null, upperBounds: null);
        var r = new IpoptManagedStub().Solve(problem);
        Assert(r.Status == ConvexQpStatus.NotImplemented,
            $"expected NotImplemented, got {r.Status}");
        Assert(r.SolverMessage.IndexOf("IpoptManagedStub", StringComparison.Ordinal) >= 0,
            $"expected message to mention IpoptManagedStub, got: {r.SolverMessage}");
    }

    public static void IpoptStub_NullProblem_Throws()
    {
        bool threw = false;
        try { new IpoptManagedStub().Solve(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null problem should throw ArgumentNullException");
    }

    public static void Registry_UseIpoptIfAvailable_FallsBackToManaged()
    {
        var snapshot = MasonrySolverRegistry.Default;
        try
        {
            MasonrySolverRegistry.Default = null;
            string name = MasonrySolverRegistry.UseIpoptIfAvailable();
            // Stub IsAvailable=false → falls back to ManagedDykstra.
            Assert(name == "ManagedDykstra",
                $"expected ManagedDykstra fallback, got '{name}'");
            Assert(MasonrySolverRegistry.Default is ManagedQpSolver,
                $"Registry.Default should be ManagedQpSolver, got " +
                $"{MasonrySolverRegistry.Default?.GetType().FullName ?? "null"}");
        }
        finally { MasonrySolverRegistry.Default = snapshot; }
    }

    public static void Registry_UseIpoptIfAvailable_PreservesExisting()
    {
        var snapshot = MasonrySolverRegistry.Default;
        try
        {
            var sentinel = new ManagedQpSolver(tolerance: 1e-3, maxIterations: 7);
            MasonrySolverRegistry.Default = sentinel;
            string name = MasonrySolverRegistry.UseIpoptIfAvailable();
            // Stub not available, existing registration preserved.
            Assert(ReferenceEquals(MasonrySolverRegistry.Default, sentinel),
                "UseIpoptIfAvailable must not overwrite an existing registration when IPOPT not present");
        }
        finally { MasonrySolverRegistry.Default = snapshot; }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
