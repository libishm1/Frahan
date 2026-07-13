#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Frahan.Masonry.Nbo
{
    // =========================================================================
    // LapjvNative — P/Invoke shim for frahan_lapjv.dll.
    //
    // frahan_lapjv.dll is a self-contained native DLL (no vcpkg deps).
    // Build via native/lapjv_shim/build_native.ps1.
    // When the DLL is absent, Available = false and callers fall back to the
    // managed GreedyMatcher.
    // =========================================================================
    internal static class LapjvNative
    {
        private const string Dll = "frahan_lapjv";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int frahan_lapjv_solve(
            int n,
            [In]  double[] cost,
            [Out] int[]    rowsol,
            [Out] int[]    colsol,
            [Out] double[] u,
            [Out] double[] v,
            out   double   obj_out);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi)]
        private static extern IntPtr frahan_lapjv_version();

        // ---- availability probe (lazy, once) --------------------------------
        private static bool? _available;

        internal static bool Available
        {
            get
            {
                if (_available.HasValue) return _available.Value;
                try
                {
                    IntPtr _ = frahan_lapjv_version();
                    _available = true;
                }
                catch
                {
                    _available = false;
                }
                return _available.Value;
            }
        }

        internal static string Version()
        {
            if (!Available) return null;
            try { return Marshal.PtrToStringAnsi(frahan_lapjv_version()); }
            catch { return null; }
        }

        // ---- public wrapper -------------------------------------------------
        /// <summary>
        /// Solve an n×n dense linear assignment problem (minimisation).
        /// cost is row-major, length n*n.
        /// rowsol[i] = assigned column for row i.
        /// colsol[j] = assigned row for column j.
        /// Returns 0 on success.
        /// </summary>
        internal static int Solve(
            int n, double[] cost,
            int[] rowsol, int[] colsol,
            double[] u, double[] v,
            out double obj)
        {
            if (!Available) { obj = double.NaN; return -99; }
            return frahan_lapjv_solve(n, cost, rowsol, colsol, u, v, out obj);
        }
    }
}
