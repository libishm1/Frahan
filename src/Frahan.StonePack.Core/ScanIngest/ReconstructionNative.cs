#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// ReconstructionNative — P/Invoke surface for Phase H + I native exports
// added 2026-05-19. Wraps frahan_cgal.dll (alpha shape, advancing front,
// estimate normals) and frahan_geogram.dll (Poisson, voxel downsample,
// kdtree query).
//
// All entry points are guarded so the .gha continues to load even when
// the native DLLs haven't been rebuilt with Phase H/I support yet. The
// safe wrappers (TryAlphaShape3 / TryPoisson / TryAdvancingFront /
// TryEstimateNormals / TryVoxelDownsample / TryKdTreeQuery) catch
// EntryPointNotFoundException and DllNotFoundException, returning false
// and setting an error string. Callers (GH components) surface that
// as a Warning bubble so users know to rebuild the native side.
// =============================================================================

public sealed class ReconstructionResult
{
    public ReconstructionResult(double[] vertices, int[] triangles)
    {
        Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
    }
    /// <summary>Flat xyz triples; length = 3 * vertex count.</summary>
    public double[] Vertices { get; }
    /// <summary>Flat triplets of vertex indices; length = 3 * triangle count.</summary>
    public int[] Triangles { get; }
    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => Triangles.Length / 3;
}

public static class ReconstructionNative
{
    // ─── frahan_cgal.dll P/Invokes ─────────────────────────────────────────

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_alpha_shape_3")]
    private static extern int CgalAlphaShape3(
        [In] double[] points, int pcount, double alpha,
        out IntPtr outVerts, out int outVcount,
        out IntPtr outTris, out int outTcount);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_advancing_front_reconstruct")]
    private static extern int CgalAdvancingFront(
        [In] double[] points, int pcount,
        double radiusRatio, double beta,
        out IntPtr outVerts, out int outVcount,
        out IntPtr outTris, out int outTcount);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_poisson_reconstruct")]
    private static extern int CgalPoisson(
        [In] double[] points, int pcount,
        [In] double[] normals,
        double smAngle, double smRadius, double smDistance,
        out IntPtr outVerts, out int outVcount,
        out IntPtr outTris, out int outTcount);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_estimate_normals")]
    private static extern int CgalEstimateNormals(
        [In] double[] points, int pcount,
        int kNeighbours, out IntPtr outNormals);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_free_buffers")]
    private static extern void CgalFreeBuffers(IntPtr verts, IntPtr tris);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_free_pdouble")]
    private static extern void CgalFreePDouble(IntPtr p);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_cgal_last_error")]
    private static extern IntPtr CgalLastError();

    // ─── frahan_geogram.dll P/Invokes ──────────────────────────────────────

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_geogram_poisson_reconstruct")]
    private static extern int GeoPoisson(
        [In] double[] points, int pcount,
        [In] double[] normals,
        int depth, double samplesPerNode,
        out IntPtr outVerts, out int outVcount,
        out IntPtr outTris, out int outTcount);

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_geogram_voxel_downsample")]
    private static extern int GeoVoxelDownsample(
        [In] double[] points, int pcount,
        double voxelSize,
        out IntPtr outCentroids, out int outCount);

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_geogram_kdtree_query")]
    private static extern int GeoKdTreeQuery(
        [In] double[] treePoints, int treeCount,
        [In] double[] queryPoints, int queryCount,
        out IntPtr outIndices,
        out IntPtr outSqDistances);

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_geogram_free_pdouble")]
    private static extern void GeoFreePDouble(IntPtr p);

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_geogram_free_pint")]
    private static extern void GeoFreePInt(IntPtr p);

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "frahan_geogram_last_error")]
    private static extern IntPtr GeoLastError();

    // ─── Safe managed wrappers ────────────────────────────────────────────

    private static string ReadCgalError()
    {
        try
        {
            var p = CgalLastError();
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
        }
        catch { return ""; }
    }
    private static string ReadGeoError()
    {
        try
        {
            var p = GeoLastError();
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
        }
        catch { return ""; }
    }

    private static ReconstructionResult MarshalMesh(IntPtr verts, int vcount, IntPtr tris, int tcount)
    {
        var v = new double[3 * vcount];
        Marshal.Copy(verts, v, 0, v.Length);
        var t = new int[3 * tcount];
        Marshal.Copy(tris, t, 0, t.Length);
        return new ReconstructionResult(v, t);
    }

    public static bool TryAlphaShape3(double[] points, double alpha,
        out ReconstructionResult result, out string error)
    {
        result = null; error = null;
        if (points == null) { error = "points is null"; return false; }
        if (points.Length % 3 != 0 || points.Length < 12)
        {
            error = $"points length must be a multiple of 3 and >= 12; got {points.Length}";
            return false;
        }
        try
        {
            int pcount = points.Length / 3;
            int rc = CgalAlphaShape3(points, pcount, alpha,
                out IntPtr v, out int vc, out IntPtr t, out int tc);
            if (rc != 0) { error = $"CGAL alpha_shape_3 returned {rc}: {ReadCgalError()}"; return false; }
            try { result = MarshalMesh(v, vc, t, tc); return true; }
            finally { CgalFreeBuffers(v, t); }
        }
        catch (DllNotFoundException) { error = "frahan_cgal.dll not found — rebuild the native shim"; return false; }
        catch (EntryPointNotFoundException) { error = "frahan_cgal_alpha_shape_3 entry point missing — rebuild with Phase H support"; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool TryAdvancingFront(double[] points,
        double radiusRatio, double beta,
        out ReconstructionResult result, out string error)
    {
        result = null; error = null;
        if (points == null || points.Length % 3 != 0 || points.Length < 12)
        { error = "points must be non-null, length divisible by 3, with >= 4 points"; return false; }
        try
        {
            int pcount = points.Length / 3;
            int rc = CgalAdvancingFront(points, pcount, radiusRatio, beta,
                out IntPtr v, out int vc, out IntPtr t, out int tc);
            if (rc != 0) { error = $"CGAL advancing_front returned {rc}: {ReadCgalError()}"; return false; }
            try { result = MarshalMesh(v, vc, t, tc); return true; }
            finally { CgalFreeBuffers(v, t); }
        }
        catch (DllNotFoundException) { error = "frahan_cgal.dll not found — rebuild the native shim"; return false; }
        catch (EntryPointNotFoundException) { error = "frahan_cgal_advancing_front_reconstruct entry point missing — rebuild with Phase H support"; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool TryEstimateNormals(double[] points, int kNeighbours,
        out double[] normals, out string error)
    {
        normals = null; error = null;
        if (points == null || points.Length % 3 != 0 || points.Length < 9)
        { error = "points must be non-null, length divisible by 3, with >= 3 points"; return false; }
        try
        {
            int pcount = points.Length / 3;
            int rc = CgalEstimateNormals(points, pcount, kNeighbours, out IntPtr n);
            if (rc != 0) { error = $"CGAL estimate_normals returned {rc}: {ReadCgalError()}"; return false; }
            try
            {
                normals = new double[3 * pcount];
                Marshal.Copy(n, normals, 0, normals.Length);
                return true;
            }
            finally { CgalFreePDouble(n); }
        }
        catch (DllNotFoundException) { error = "frahan_cgal.dll not found — rebuild the native shim"; return false; }
        catch (EntryPointNotFoundException) { error = "frahan_cgal_estimate_normals entry point missing — rebuild with Phase H/I support"; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool TryPoisson(double[] points, double[] normals,
        int depth, double samplesPerNode,
        out ReconstructionResult result, out string error)
    {
        result = null; error = null;
        if (points == null || normals == null
            || points.Length != normals.Length || points.Length % 3 != 0)
        { error = "points and normals must be non-null, same length, divisible by 3"; return false; }
        int pcount = points.Length / 3;
        // Primary: Geogram's screened-Poisson (Kazhdan PoissonRecon, bundled in
        // Geogram; wired 2026-05-28). Fallback: CGAL's Poisson if Geogram is
        // unavailable or yields an empty/failed surface — so "Poisson" mode is
        // robust regardless of which native shim is present.
        string geoErr = null;
        try
        {
            int rc = GeoPoisson(points, pcount, normals, depth, samplesPerNode,
                out IntPtr v, out int vc, out IntPtr t, out int tc);
            if (rc == 0)
            {
                try { result = MarshalMesh(v, vc, t, tc); }
                finally { GeoFreePDouble(v); GeoFreePInt(t); }
                if (result != null && result.Triangles != null && result.Triangles.Length > 0)
                    return true;
                geoErr = "Geogram poisson_reconstruct produced an empty surface";
            }
            else { geoErr = $"Geogram poisson_reconstruct returned {rc}: {ReadGeoError()}"; }
        }
        catch (DllNotFoundException) { geoErr = "frahan_geogram.dll not found"; }
        catch (EntryPointNotFoundException) { geoErr = "frahan_geogram_poisson_reconstruct entry point missing"; }
        catch (Exception ex) { geoErr = ex.Message; }

        // Fallback to CGAL Poisson (sm_angle/radius/distance = 0 -> CGAL defaults).
        try
        {
            int rc = CgalPoisson(points, pcount, normals, 0.0, 0.0, 0.0,
                out IntPtr v, out int vc, out IntPtr t, out int tc);
            if (rc != 0)
            {
                error = $"Poisson failed. Geogram: {geoErr}. CGAL: returned {rc}: {ReadCgalError()}";
                return false;
            }
            try { result = MarshalMesh(v, vc, t, tc); return true; }
            finally { CgalFreeBuffers(v, t); }
        }
        catch (DllNotFoundException) { error = $"Poisson unavailable. Geogram: {geoErr}. CGAL: frahan_cgal.dll not found"; return false; }
        catch (EntryPointNotFoundException) { error = $"Poisson unavailable. Geogram: {geoErr}. CGAL: frahan_cgal_poisson_reconstruct entry point missing"; return false; }
        catch (Exception ex) { error = $"Poisson failed. Geogram: {geoErr}. CGAL: {ex.Message}"; return false; }
    }

    /// <summary>CGAL screened-Poisson only (no Geogram). Used by Mode 4.</summary>
    public static bool TryPoissonCgal(double[] points, double[] normals,
        out ReconstructionResult result, out string error)
    {
        result = null; error = null;
        if (points == null || normals == null
            || points.Length != normals.Length || points.Length % 3 != 0)
        { error = "points and normals must be non-null, same length, divisible by 3"; return false; }
        try
        {
            int pcount = points.Length / 3;
            int rc = CgalPoisson(points, pcount, normals, 0.0, 0.0, 0.0,
                out IntPtr v, out int vc, out IntPtr t, out int tc);
            if (rc != 0) { error = $"CGAL poisson returned {rc}: {ReadCgalError()}"; return false; }
            try { result = MarshalMesh(v, vc, t, tc); return true; }
            finally { CgalFreeBuffers(v, t); }
        }
        catch (DllNotFoundException) { error = "frahan_cgal.dll not found"; return false; }
        catch (EntryPointNotFoundException) { error = "frahan_cgal_poisson_reconstruct entry point missing"; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool TryVoxelDownsample(double[] points, double voxelSize,
        out double[] centroids, out string error)
    {
        centroids = null; error = null;
        if (points == null || points.Length % 3 != 0 || points.Length < 3)
        { error = "points must be non-null and length divisible by 3"; return false; }
        if (!(voxelSize > 0.0)) { error = "voxelSize must be > 0"; return false; }
        try
        {
            int pcount = points.Length / 3;
            int rc = GeoVoxelDownsample(points, pcount, voxelSize, out IntPtr p, out int outN);
            if (rc != 0) { error = $"Geogram voxel_downsample returned {rc}: {ReadGeoError()}"; return false; }
            try
            {
                centroids = new double[3 * outN];
                Marshal.Copy(p, centroids, 0, centroids.Length);
                return true;
            }
            finally { GeoFreePDouble(p); }
        }
        catch (DllNotFoundException) { error = "frahan_geogram.dll not found — rebuild the native shim"; return false; }
        catch (EntryPointNotFoundException) { error = "frahan_geogram_voxel_downsample entry point missing — rebuild with Phase I support"; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static bool TryKdTreeQuery(double[] treePoints, double[] queryPoints,
        out int[] indices, out double[] sqDistances, out string error)
    {
        indices = null; sqDistances = null; error = null;
        if (treePoints == null || queryPoints == null
            || treePoints.Length % 3 != 0 || queryPoints.Length % 3 != 0)
        { error = "treePoints and queryPoints must be non-null and divisible by 3"; return false; }
        try
        {
            int tc = treePoints.Length / 3;
            int qc = queryPoints.Length / 3;
            int rc = GeoKdTreeQuery(treePoints, tc, queryPoints, qc,
                out IntPtr idx, out IntPtr sqd);
            if (rc != 0) { error = $"Geogram kdtree_query returned {rc}: {ReadGeoError()}"; return false; }
            try
            {
                indices = new int[qc];
                Marshal.Copy(idx, indices, 0, qc);
                sqDistances = new double[qc];
                Marshal.Copy(sqd, sqDistances, 0, qc);
                return true;
            }
            finally { GeoFreePInt(idx); GeoFreePDouble(sqd); }
        }
        catch (DllNotFoundException) { error = "frahan_geogram.dll not found — rebuild the native shim"; return false; }
        catch (EntryPointNotFoundException) { error = "frahan_geogram_kdtree_query entry point missing — rebuild with Phase I support"; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}
