#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Frahan.Masonry.Solvers
{
    // =========================================================================
    // OsqpNative — P/Invoke declarations for frahan_osqp.dll.
    //
    // The DLL wraps OSQP (Stellato et al. 2020) statically.  It accepts dense
    // row-major matrices matching ConvexQpProblem's layout and handles the
    // dense→CSC conversion + OSQP setup/solve/cleanup internally.
    //
    // All arrays passed as [In] pointers; no heap ownership crosses the boundary.
    // =========================================================================
    internal static class OsqpNative
    {
        private const string Dll = "frahan_osqp";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr frahan_osqp_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr frahan_osqp_last_error();

        // Main entry point.  All pointer params may be null per the header contract.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int frahan_osqp_solve(
            int             n,
            int             meq,
            int             mineq,
            [In]  double[]  P,           // upper-triangle Hessian [n*n] or null
            [In]  double[]  q,           // linear objective [n] or null
            [In]  double[]  Aeq,         // equality matrix [meq*n] or null
            [In]  double[]  beq,         // equality rhs [meq] or null
            [In]  double[]  Aineq,       // inequality matrix [mineq*n] or null
            [In]  double[]  bineq,       // inequality rhs [mineq] or null
            [In]  double[]  lb,          // lower bounds [n] or null
            [In]  double[]  ub,          // upper bounds [n] or null
            double          eps_abs,
            double          eps_rel,
            int             max_iter,
            int             polish,
            [In]  double[]  warm_start_x,// primal warm start [n] or null
            [Out] double[]  x_out,       // primal solution [n]
            out   double    obj_out,
            out   int       iter_out,
            out   int       status_val,
            [Out] byte[]    msg,         // message buffer (UTF-8)
            int             msg_len
        );

        // ---- Availability ----
        internal static bool Available { get; private set; } = true;

        internal static string Version()
        {
            try {
                var ptr = frahan_osqp_version();
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : "(null)";
            } catch {
                Available = false;
                return null;
            }
        }

        internal static string LastError()
        {
            try {
                var ptr = frahan_osqp_last_error();
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : "";
            } catch { return "frahan_osqp.dll not available"; }
        }

        // ---- Convenience: flatten a dense 2D C# array to a 1D row-major double[]. ----
        internal static double[] Flatten(double[,] m)
        {
            if (m == null) return null;
            int rows = m.GetLength(0), cols = m.GetLength(1);
            var f = new double[rows * cols];
            Buffer.BlockCopy(m, 0, f, 0, f.Length * sizeof(double));
            return f;
        }
    }
}
