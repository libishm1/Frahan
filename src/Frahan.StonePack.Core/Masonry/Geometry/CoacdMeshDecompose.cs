#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// CoacdMeshDecompose - managed front-end for the optional native CoACD shim
// (frahan_coacd.dll / libfrahan_coacd.so / .dylib). Wraps SarahWeiii/CoACD
// (SIGGRAPH 2022) for approximate convex decomposition of 3D meshes.
//
// Build the shim from native/coacd_shim/. See native/coacd_shim/BUILD.md.
// CoACD is MIT-licensed, so the DLL can ship inside the .gha directly.
//
// No managed fallback. Decomposition has no in-tree alternative; if the
// shim is absent, IsAvailable returns false and Decompose throws.
// Callers should guard on IsAvailable.
// =============================================================================

/// <summary>
/// Tunables for <see cref="CoacdMeshDecompose.Decompose"/>. Defaults match
/// CoACD's upstream defaults at tag 1.0.11.
/// </summary>
public sealed class CoacdParameters
{
    /// <summary>
    /// Concavity threshold. Lower produces more pieces and a tighter
    /// fit. CoACD default 0.05 in normalized units, or in metres when
    /// <see cref="RealMetric"/> is true.
    /// </summary>
    public double Threshold { get; set; } = 0.05;

    /// <summary>
    /// 0 = auto, 1 = on, 2 = off. Mirrors CoACD's preprocess_mode.
    /// "auto" runs OpenVDB-based manifold preprocessing only when the
    /// input is non-manifold. Requires shim built with
    /// FRAHAN_COACD_WITH_3RD_PARTY=ON (the default). Without the
    /// 3rd-party libs, non-manifold inputs fail with
    /// "The mesh is not a 2-manifold!".
    /// </summary>
    public int PreprocessMode { get; set; } = 0;

    /// <summary>Manifold-isation voxel grid (default 50, range ~30..100).</summary>
    public int PreprocessResolution { get; set; } = 50;

    /// <summary>Concavity sampling resolution (default 2000).</summary>
    public int SampleResolution { get; set; } = 2000;

    /// <summary>MCTS nodes per cut (default 20).</summary>
    public int MctsNodes { get; set; } = 20;

    /// <summary>MCTS iterations per cut (default 150).</summary>
    public int MctsIterations { get; set; } = 150;

    /// <summary>MCTS tree depth (default 3).</summary>
    public int MctsMaxDepth { get; set; } = 3;

    /// <summary>Align cuts to PCA frame (default false).</summary>
    public bool Pca { get; set; } = false;

    /// <summary>
    /// Post-merge convex pieces where merging stays convex (default true).
    /// </summary>
    public bool Merge { get; set; } = true;

    /// <summary>
    /// Cap on output piece count. -1 = unlimited (default).
    /// </summary>
    public int MaxConvexHull { get; set; } = -1;

    /// <summary>RNG seed for reproducibility.</summary>
    public uint Seed { get; set; } = 0;

    /// <summary>
    /// When true, <see cref="Threshold"/> is interpreted as metres rather
    /// than CoACD's normalized [0..1] units. Maps to CoACD's `-rm` flag
    /// (added in 1.0.11). For statue-scale architectural input, set this
    /// to true and pass Threshold in metres.
    /// </summary>
    public bool RealMetric { get; set; } = false;
}

public static class CoacdMeshDecompose
{
    private static bool? _isAvailable;
    private static string _version;
    private static readonly object _lock = new object();

    /// <summary>
    /// True iff <c>frahan_coacd</c> can be loaded by the platform loader.
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
                    var ptr = Native.frahan_coacd_version();
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
    /// The CoACD shim's reported version, e.g. "Frahan-CoACD 0.1 (CoACD upstream)".
    /// Empty string when the shim is not available.
    /// </summary>
    public static string Version
    {
        get
        {
            _ = IsAvailable;
            return _version ?? string.Empty;
        }
    }

    /// <summary>
    /// Sets the CoACD log level. Accepts "off", "error", "warn", "info",
    /// "debug". No-op when the shim is not loaded.
    /// </summary>
    public static void SetLogLevel(string level)
    {
        if (!IsAvailable) return;
        Native.frahan_coacd_set_log_level(level ?? string.Empty);
    }

    /// <summary>
    /// Decomposes the input mesh into a list of approximately convex
    /// pieces. Each output mesh is one convex piece with triangle indices
    /// rooted at 0.
    /// </summary>
    /// <exception cref="ArgumentNullException">input or parameters is null.</exception>
    /// <exception cref="NotSupportedException">Native shim not available.</exception>
    /// <exception cref="InvalidOperationException">CoACD returned an error.</exception>
    public static IReadOnlyList<MeshSnapshot> Decompose(
        MeshSnapshot input, CoacdParameters parameters)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (parameters == null) throw new ArgumentNullException(nameof(parameters));
        if (!IsAvailable)
            throw new NotSupportedException(
                "frahan_coacd shim not loaded. Build it from native/coacd_shim/ " +
                "and place the DLL alongside Frahan.StonePack.gha.");

        var verts = ToArrayD(input.VertexCoordsXyz);
        var tris  = ToArrayI(input.TriangleIndices);

        IntPtr outVerts = IntPtr.Zero;
        IntPtr outVertStarts = IntPtr.Zero;
        IntPtr outTris = IntPtr.Zero;
        IntPtr outTriStarts = IntPtr.Zero;
        int partCount = 0, vertCount = 0, triCount = 0;

        int rc = Native.frahan_coacd_decompose(
            verts, verts.Length / 3,
            tris,  tris.Length  / 3,
            parameters.Threshold,
            parameters.PreprocessMode,
            parameters.PreprocessResolution,
            parameters.SampleResolution,
            parameters.MctsNodes,
            parameters.MctsIterations,
            parameters.MctsMaxDepth,
            parameters.Pca   ? 1 : 0,
            parameters.Merge ? 1 : 0,
            parameters.MaxConvexHull,
            parameters.Seed,
            parameters.RealMetric ? 1 : 0,
            out partCount,
            out outVerts, out outVertStarts, out vertCount,
            out outTris,  out outTriStarts,  out triCount);

        if (rc != 0)
        {
            string err;
            try { err = Marshal.PtrToStringAnsi(Native.frahan_coacd_last_error()) ?? "(none)"; }
            catch { err = "(could not read error)"; }
            FreeAll(outVerts, outVertStarts, outTris, outTriStarts);
            throw new InvalidOperationException(
                $"CoACD decompose failed (rc={rc}): {err}");
        }

        try
        {
            return BuildResult(partCount,
                               outVerts, outVertStarts, vertCount,
                               outTris,  outTriStarts,  triCount);
        }
        finally
        {
            FreeAll(outVerts, outVertStarts, outTris, outTriStarts);
        }
    }

    // --- Implementation -----------------------------------------------------

    private static IReadOnlyList<MeshSnapshot> BuildResult(
        int partCount,
        IntPtr outVerts, IntPtr outVertStarts, int vertCount,
        IntPtr outTris,  IntPtr outTriStarts,  int triCount)
    {
        if (partCount <= 0) return Array.Empty<MeshSnapshot>();

        // Per-part start arrays have N + 1 entries; index N is the total.
        var vertStarts = new int[partCount + 1];
        var triStarts  = new int[partCount + 1];
        if (outVertStarts != IntPtr.Zero) Marshal.Copy(outVertStarts, vertStarts, 0, partCount + 1);
        if (outTriStarts  != IntPtr.Zero) Marshal.Copy(outTriStarts,  triStarts,  0, partCount + 1);

        // Pull all vertex / triangle bytes once, then slice per part.
        var allVerts = new double[vertCount * 3];
        if (vertCount > 0 && outVerts != IntPtr.Zero)
            Marshal.Copy(outVerts, allVerts, 0, vertCount * 3);
        var allTris = new int[triCount * 3];
        if (triCount > 0 && outTris != IntPtr.Zero)
            Marshal.Copy(outTris, allTris, 0, triCount * 3);

        var result = new MeshSnapshot[partCount];
        for (int i = 0; i < partCount; i++)
        {
            int vBegin = vertStarts[i];
            int vEnd   = vertStarts[i + 1];
            int tBegin = triStarts[i];
            int tEnd   = triStarts[i + 1];
            int vLen   = vEnd - vBegin;
            int tLen   = tEnd - tBegin;

            var pv = new double[vLen * 3];
            if (vLen > 0) Array.Copy(allVerts, vBegin * 3, pv, 0, vLen * 3);
            var pt = new int[tLen * 3];
            if (tLen > 0) Array.Copy(allTris, tBegin * 3, pt, 0, tLen * 3);

            result[i] = new MeshSnapshot(pv, pt);
        }
        return result;
    }

    private static void FreeAll(IntPtr v, IntPtr vs, IntPtr t, IntPtr ts)
    {
        if (v  != IntPtr.Zero) Native.frahan_coacd_free_pdouble(v);
        if (vs != IntPtr.Zero) Native.frahan_coacd_free_pint(vs);
        if (t  != IntPtr.Zero) Native.frahan_coacd_free_pint(t);
        if (ts != IntPtr.Zero) Native.frahan_coacd_free_pint(ts);
    }

    private static double[] ToArrayD(IReadOnlyList<double> v)
    {
        var a = new double[v.Count];
        for (int i = 0; i < v.Count; i++) a[i] = v[i];
        return a;
    }

    private static int[] ToArrayI(IReadOnlyList<int> v)
    {
        var a = new int[v.Count];
        for (int i = 0; i < v.Count; i++) a[i] = v[i];
        return a;
    }

    // --- P/Invoke -----------------------------------------------------------

    private static class Native
    {
        // Loader resolves "frahan_coacd" -> "frahan_coacd.dll" on Windows,
        // "libfrahan_coacd.so" on Linux, "libfrahan_coacd.dylib" on macOS.
        private const string Dll = "frahan_coacd";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_coacd_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_coacd_last_error();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false,
                   ThrowOnUnmappableChar = true)]
        public static extern void frahan_coacd_set_log_level(
            [MarshalAs(UnmanagedType.LPStr)] string level);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_coacd_decompose(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            double threshold,
            int    preprocessMode,
            int    preprocessResolution,
            int    sampleResolution,
            int    mctsNodes,
            int    mctsIterations,
            int    mctsMaxDepth,
            int    pca,
            int    merge,
            int    maxConvexHull,
            uint   seed,
            int    realMetric,
            out int    outPartCount,
            out IntPtr outVerts,       out IntPtr outVertStarts, out int outVertCount,
            out IntPtr outTris,        out IntPtr outTriStarts,  out int outTriCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_coacd_free_pdouble(IntPtr p);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_coacd_free_pint(IntPtr p);
    }
}
