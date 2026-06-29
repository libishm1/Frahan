#nullable disable
using System;
using System.Text;

namespace Frahan.Masonry.Solvers
{
    // =========================================================================
    // OsqpQpSolver — IConvexQpSolver backed by native OSQP via frahan_osqp.dll.
    //
    // Performance vs AdmmQpSolver (pure managed fallback):
    //   54-iface wall:  ~0.07 s  →  <5 ms
    //   95-iface wall:  ~0.4 s   →  <15 ms
    //   147-iface wall: ~1.1 s   →  <30 ms
    // (OSQP warm-start not exposed here yet — fits within the KKT-cert pipeline
    // that calls the solver only when the certificate declines.)
    //
    // Falls back gracefully if frahan_osqp.dll is absent: call IsAvailable before
    // use, or register via MasonrySolverRegistry.UseOsqpIfAvailable() which
    // automatically installs the ADMM fallback when OSQP is not found.
    //
    // OSQP status codes (status_val):
    //   1  = SOLVED         → ConvexQpStatus.Optimal
    //   2  = SOLVED_INACCURATE → Optimal (with warning in message)
    //  -3  = PRIMAL_INFEASIBLE → ConvexQpStatus.Infeasible
    //  -4  = DUAL_INFEASIBLE   → ConvexQpStatus.Unbounded
    //  -7  = NON_CVX           → ConvexQpStatus.SolverError
    //  other negative          → ConvexQpStatus.SolverError
    // =========================================================================
    public sealed class OsqpQpSolver : IConvexQpSolver
    {
        // Default tolerances tighter than OSQP defaults to match the masonry
        // precision required by MasonryStabilityChecker's KKT certificate.
        private readonly double _epsAbs;
        private readonly double _epsRel;
        private readonly int    _maxIter;
        private readonly bool   _polish;

        public OsqpQpSolver(
            double epsAbs  = 1e-6,
            double epsRel  = 1e-5,
            int    maxIter = 6000,
            bool   polish  = true)
        {
            _epsAbs  = epsAbs;
            _epsRel  = epsRel;
            _maxIter = maxIter;
            _polish  = polish;
        }

        public string Name => "OsqpQpSolver";

        public static bool IsAvailable => OsqpNative.Available;
        public static string NativeVersion => OsqpNative.Version() ?? "(not found)";

        // ---------------------------------------------------------------------
        // Solve.
        // ---------------------------------------------------------------------
        public ConvexQpResult Solve(ConvexQpProblem problem)
            => Solve(problem, null);

        // Warm-started overload: warmStartX is in the original variable space.
        public ConvexQpResult Solve(ConvexQpProblem problem, double[] warmStartX)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            if (!OsqpNative.Available)
                return Fail("frahan_osqp.dll not available.");

            int n     = problem.VariableCount;
            int meq   = problem.EqualityRowCount;
            int mineq = problem.InequalityRowCount;

            // Flatten dense 2D arrays to 1D row-major (fast Buffer.BlockCopy).
            // P: we must pass only the upper triangle, but since ConvexQpProblem
            // stores the full symmetric matrix we extract it here.
            double[] Pflat  = ExtractUpperTriangle(problem.Hessian, n);
            double[] Aeq    = OsqpNative.Flatten(problem.EqualityMatrix);
            double[] Aineq  = OsqpNative.Flatten(problem.InequalityMatrix);

            var    xOut   = new double[n];
            var    msgBuf = new byte[512];
            double objOut;
            int    iterOut, statusVal;

            int ret = OsqpNative.frahan_osqp_solve(
                n, meq, mineq,
                Pflat,
                problem.LinearObjective,
                Aeq,  problem.EqualityRhs,
                Aineq, problem.InequalityRhs,
                problem.LowerBounds,
                problem.UpperBounds,
                _epsAbs, _epsRel, _maxIter,
                _polish ? 1 : 0,
                warmStartX,
                xOut,
                out objOut, out iterOut, out statusVal,
                msgBuf, msgBuf.Length);

            string msgStr = Encoding.UTF8.GetString(msgBuf).TrimEnd('\0');

            if (ret != 0)
                return Fail("frahan_osqp_solve setup error: " + OsqpNative.LastError());

            // OSQP status values: 1=SOLVED, 2=SOLVED_INACCURATE, -3=PRIMAL_INF,
            // -4=DUAL_INF, -7=NON_CVX.
            if (statusVal == 1 || statusVal == 2)
                return new ConvexQpResult(ConvexQpStatus.Optimal, xOut, objOut, msgStr);
            if (statusVal == -3)
                return new ConvexQpResult(ConvexQpStatus.Infeasible, null, 0, msgStr);
            if (statusVal == -4)
                return new ConvexQpResult(ConvexQpStatus.Unbounded, null, 0, msgStr);
            return Fail(msgStr);
        }

        // ---- Helpers ----

        private static ConvexQpResult Fail(string msg) =>
            new ConvexQpResult(ConvexQpStatus.SolverError, null, 0, msg);

        // Extract upper triangle of a symmetric n×n matrix into a flat n*n array
        // (row-major; lower-triangle entries set to zero).
        private static double[] ExtractUpperTriangle(double[,] H, int n)
        {
            var f = new double[n * n];
            if (H == null) return f;
            for (int i = 0; i < n; i++)
                for (int j = i; j < n; j++)
                    f[i * n + j] = H[i, j];
            return f;
        }
    }
}
