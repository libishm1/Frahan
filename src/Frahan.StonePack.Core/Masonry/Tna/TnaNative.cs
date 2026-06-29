#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Frahan.Masonry.Tna
{
    // =========================================================================
    // Flat structs that mirror the C layout in frahan_tna.h.
    // Sequential layout + explicit double/int types match the MSVC ABI.
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct TnaNodeFlat
    {
        public double X, Y, Z;
        public int    Fixed;    // 1 = boundary, 0 = free
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TnaEdgeFlat
    {
        public int    I, J;
        public double Q;        // force density (N/m or dimensionless)
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TnaLoadFlat
    {
        public double Pz;       // vertical load (positive = downward, N)
        public double Px, Py;   // horizontal loads (N)
    }

    // =========================================================================
    // P/Invoke declarations for frahan_tna.dll.
    // The DLL must be in the Grasshopper Libraries folder alongside the .gha.
    // =========================================================================
    internal static class TnaNative
    {
        private const string Dll = "frahan_tna";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr frahan_tna_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr frahan_tna_last_error();

        // Solve for nodal heights.
        // loads may be null (pass null double[] → IntPtr.Zero via overload below).
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int frahan_tna_solve(
            int               n_nodes,
            [In]  TnaNodeFlat[] nodes,
            int               n_edges,
            [In]  TnaEdgeFlat[] edges,
            [In]  TnaLoadFlat[] loads,       // may be null
            [Out] double[]      z_out,
            [Out] double[]      rx_out,      // may be null
            [Out] double[]      ry_out,      // may be null
            [Out] double[]      rz_out,      // may be null
            [Out] double[]      branch_force // may be null
        );

        // Gradient update of force densities toward a target vault shape.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int frahan_tna_update_q(
            int               n_nodes,
            [In]  TnaNodeFlat[] nodes,
            int               n_edges,
            [In]  TnaEdgeFlat[] edges,
            [In]  double[]      z_current,
            [In]  double[]      z_target,
            double              alpha,
            [Out] double[]      q_out
        );

        internal static bool Available { get; private set; } = true;

        // Check availability without throwing.
        internal static string Version()
        {
            try
            {
                var ptr = frahan_tna_version();
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : "(null)";
            }
            catch
            {
                Available = false;
                return null;
            }
        }

        internal static string LastError()
        {
            try
            {
                var ptr = frahan_tna_last_error();
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : "";
            }
            catch { return "frahan_tna.dll not available"; }
        }
    }
}
