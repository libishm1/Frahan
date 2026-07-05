#nullable disable
using System;
using System.Runtime.InteropServices;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// CgalMeshBoolean — managed front-end for the optional native CGAL shim
// (frahan_cgal.dll / libfrahan_cgal.so / .dylib). When the shim is present
// on the loader search path, mesh Booleans use CGAL's Polygon Mesh
// Processing with exact-predicates inexact-constructions kernel — the
// gold standard for 3D mesh robustness. When the shim is absent, falls
// back transparently to the in-tree BSP CSG kernel (MeshCsg).
//
// Build the shim from native/cgal_shim/. See native/cgal_shim/BUILD.md
// for vcpkg / apt / brew instructions. The Frahan plugin runs without it
// — only build when you need CGAL-grade robustness on real-world meshes.
//
// API design: same surface as MeshCsg (Union / Intersection / Difference
// taking and returning MeshSnapshot). The Backend property reports which
// kernel actually executed the call so users can confirm CGAL is wired in.
// =============================================================================

public enum CsgBackend
{
    /// <summary>Pure-managed BSP-tree CSG (in-tree fallback).</summary>
    ManagedBsp,
    /// <summary>Native CGAL Polygon Mesh Processing.</summary>
    Cgal,
}

/// <summary>
/// Kernel selection for CGAL-backed mesh booleans. See
/// wiki/specs/20_frahan_cgal_audit.md for the trade-off table.
/// </summary>
public enum CsgKernelMode
{
    /// <summary>
    /// EPICK only: storage and intersection construction both use
    /// inexact doubles. Fastest. Good enough for most well-conditioned
    /// inputs. Default.
    /// </summary>
    Inexact,
    /// <summary>
    /// HYBRID: storage stays in EPICK (fast cache), intersection
    /// vertices constructed in EPECK (exact), round-tripped via
    /// Cartesian_converter. Pattern from COMPAS_CGAL. Recommended
    /// when inputs may produce numerical edge cases (multi-cut chains,
    /// near-tangent contacts).
    /// </summary>
    Hybrid,
}

public static class CgalMeshBoolean
{
    private static bool? _isAvailable;
    private static string _version;
    private static readonly object _lock = new object();

    /// <summary>
    /// True iff <c>frahan_cgal</c> can be loaded by the platform loader.
    /// First call probes the DLL; subsequent calls return the cached
    /// answer. Probing is thread-safe via a one-shot lock.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue) return _isAvailable.Value;
            lock (_lock)
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;
                try
                {
                    var ptr = Native.frahan_cgal_version();
                    _version = Marshal.PtrToStringAnsi(ptr) ?? "(unknown)";
                    _isAvailable = true;
                }
                catch (DllNotFoundException) { _isAvailable = false; }
                catch (EntryPointNotFoundException) { _isAvailable = false; }
                catch (BadImageFormatException) { _isAvailable = false; }
            }
            return _isAvailable.Value;
        }
    }

    /// <summary>
    /// The CGAL shim's reported version, e.g. "Frahan-CGAL 0.1 (CGAL 5.6)".
    /// Empty string when the shim is not available.
    /// </summary>
    public static string Version
    {
        get
        {
            _ = IsAvailable;  // probe / populate _version
            return _version ?? string.Empty;
        }
    }

    public static MeshSnapshot Union(MeshSnapshot a, MeshSnapshot b)
        => Run(a, b, NativeOp.Union, CsgKernelMode.Inexact, out _);

    public static MeshSnapshot Intersection(MeshSnapshot a, MeshSnapshot b)
        => Run(a, b, NativeOp.Intersection, CsgKernelMode.Inexact, out _);

    public static MeshSnapshot Difference(MeshSnapshot a, MeshSnapshot b)
        => Run(a, b, NativeOp.Difference, CsgKernelMode.Inexact, out _);

    /// <summary>
    /// Same as <see cref="Union(MeshSnapshot, MeshSnapshot)"/> but also
    /// reports which back-end actually ran. Useful for diagnostic UIs.
    /// </summary>
    public static MeshSnapshot Union(MeshSnapshot a, MeshSnapshot b, out CsgBackend backend)
        => Run(a, b, NativeOp.Union, CsgKernelMode.Inexact, out backend);

    public static MeshSnapshot Intersection(MeshSnapshot a, MeshSnapshot b, out CsgBackend backend)
        => Run(a, b, NativeOp.Intersection, CsgKernelMode.Inexact, out backend);

    public static MeshSnapshot Difference(MeshSnapshot a, MeshSnapshot b, out CsgBackend backend)
        => Run(a, b, NativeOp.Difference, CsgKernelMode.Inexact, out backend);

    /// <summary>
    /// Kernel-mode-aware overloads. <paramref name="kernel"/> = Hybrid
    /// routes through the COMPAS_CGAL-style EPICK+EPECK shim. Hybrid
    /// requires the native shim to be available; falls back to managed
    /// BSP CSG (Inexact) when the shim is absent — the BSP fallback
    /// has no kernel-mode equivalent, so the output backend is
    /// ManagedBsp regardless of the requested kernel.
    /// </summary>
    public static MeshSnapshot Union(MeshSnapshot a, MeshSnapshot b, CsgKernelMode kernel, out CsgBackend backend)
        => Run(a, b, NativeOp.Union, kernel, out backend);

    public static MeshSnapshot Intersection(MeshSnapshot a, MeshSnapshot b, CsgKernelMode kernel, out CsgBackend backend)
        => Run(a, b, NativeOp.Intersection, kernel, out backend);

    public static MeshSnapshot Difference(MeshSnapshot a, MeshSnapshot b, CsgKernelMode kernel, out CsgBackend backend)
        => Run(a, b, NativeOp.Difference, kernel, out backend);

    // ─── Implementation ────────────────────────────────────────────────

    private enum NativeOp { Union, Intersection, Difference }

    private static MeshSnapshot Run(
        MeshSnapshot a, MeshSnapshot b, NativeOp op, CsgKernelMode kernel, out CsgBackend backend)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));

        if (!IsAvailable)
        {
            backend = CsgBackend.ManagedBsp;
            return ManagedFallback(a, b, op);
        }

        // Flatten inputs.
        var av = ToArrayD(a.VertexCoordsXyz);
        var at = ToArrayI(a.TriangleIndices);
        var bv = ToArrayD(b.VertexCoordsXyz);
        var bt = ToArrayI(b.TriangleIndices);

        IntPtr outVerts = IntPtr.Zero, outTris = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        int rc;
        if (kernel == CsgKernelMode.Hybrid)
        {
            // Single entry point with op_kind = 0/1/2.
            int kind = op switch
            {
                NativeOp.Union => 0,
                NativeOp.Intersection => 1,
                _ => 2,
            };
            rc = Native.frahan_cgal_mesh_boolean_hybrid(
                kind,
                av, av.Length / 3, at, at.Length / 3,
                bv, bv.Length / 3, bt, bt.Length / 3,
                out outVerts, out outVc, out outTris, out outTc);
        }
        else
        switch (op)
        {
            case NativeOp.Union:
                rc = Native.frahan_cgal_mesh_union(
                    av, av.Length / 3, at, at.Length / 3,
                    bv, bv.Length / 3, bt, bt.Length / 3,
                    out outVerts, out outVc, out outTris, out outTc);
                break;
            case NativeOp.Intersection:
                rc = Native.frahan_cgal_mesh_intersection(
                    av, av.Length / 3, at, at.Length / 3,
                    bv, bv.Length / 3, bt, bt.Length / 3,
                    out outVerts, out outVc, out outTris, out outTc);
                break;
            default:
                rc = Native.frahan_cgal_mesh_difference(
                    av, av.Length / 3, at, at.Length / 3,
                    bv, bv.Length / 3, bt, bt.Length / 3,
                    out outVerts, out outVc, out outTris, out outTc);
                break;
        }

        if (rc != 0)
        {
            string err;
            try { err = Marshal.PtrToStringAnsi(Native.frahan_cgal_last_error()) ?? "(none)"; }
            catch { err = "(could not read error)"; }
            // Free anything the shim might have allocated despite the failure.
            Native.frahan_cgal_free_buffers(outVerts, outTris);
            throw new InvalidOperationException(
                $"CGAL {op} failed (rc={rc}): {err}");
        }

        // Marshal output and free native buffers.
        double[] verts;
        int[] tris;
        try
        {
            verts = new double[outVc * 3];
            if (outVc > 0) Marshal.Copy(outVerts, verts, 0, outVc * 3);
            tris = new int[outTc * 3];
            if (outTc > 0) Marshal.Copy(outTris, tris, 0, outTc * 3);
        }
        finally
        {
            Native.frahan_cgal_free_buffers(outVerts, outTris);
        }

        backend = CsgBackend.Cgal;
        return new MeshSnapshot(verts, tris);
    }

    private static MeshSnapshot ManagedFallback(
        MeshSnapshot a, MeshSnapshot b, NativeOp op)
    {
        var ca = MeshCsg.FromMesh(a);
        var cb = MeshCsg.FromMesh(b);
        switch (op)
        {
            case NativeOp.Union:        return MeshCsg.Union(ca, cb).ToMesh();
            case NativeOp.Intersection: return MeshCsg.Intersection(ca, cb).ToMesh();
            default:                    return MeshCsg.Difference(ca, cb).ToMesh();
        }
    }

    private static double[] ToArrayD(System.Collections.Generic.IReadOnlyList<double> v)
    {
        var a = new double[v.Count];
        for (int i = 0; i < v.Count; i++) a[i] = v[i];
        return a;
    }

    private static int[] ToArrayI(System.Collections.Generic.IReadOnlyList<int> v)
    {
        var a = new int[v.Count];
        for (int i = 0; i < v.Count; i++) a[i] = v[i];
        return a;
    }

    // ─── P/Invoke ──────────────────────────────────────────────────────

    private static class Native
    {
        // Loader resolves "frahan_cgal" → "frahan_cgal.dll" on Windows,
        // "libfrahan_cgal.so" on Linux, "libfrahan_cgal.dylib" on macOS.
        private const string Dll = "frahan_cgal";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_cgal_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_cgal_last_error();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_mesh_union(
            [In] double[] aVerts, int avc, [In] int[] aTris, int atc,
            [In] double[] bVerts, int bvc, [In] int[] bTris, int btc,
            out IntPtr outVerts, out int outVc,
            out IntPtr outTris,  out int outTc);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_mesh_intersection(
            [In] double[] aVerts, int avc, [In] int[] aTris, int atc,
            [In] double[] bVerts, int bvc, [In] int[] bTris, int btc,
            out IntPtr outVerts, out int outVc,
            out IntPtr outTris,  out int outTc);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_mesh_difference(
            [In] double[] aVerts, int avc, [In] int[] aTris, int atc,
            [In] double[] bVerts, int bvc, [In] int[] bTris, int btc,
            out IntPtr outVerts, out int outVc,
            out IntPtr outTris,  out int outTc);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_mesh_boolean_hybrid(
            int opKind,
            [In] double[] aVerts, int avc, [In] int[] aTris, int atc,
            [In] double[] bVerts, int bvc, [In] int[] bTris, int btc,
            out IntPtr outVerts, out int outVc,
            out IntPtr outTris,  out int outTc);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_cgal_free_buffers(
            IntPtr verts, IntPtr tris);
    }
}
