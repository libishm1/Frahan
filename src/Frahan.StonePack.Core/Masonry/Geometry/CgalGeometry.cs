#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// CgalGeometry — managed front-end for the extended frahan_cgal native shim.
// Exposes oriented bounding box (OBB), 2D straight skeleton, and 2D polygon
// partition. Mirrors the CgalMeshBoolean pattern: probes the shim on first
// call, caches IsAvailable, throws InvalidOperationException when CGAL is
// requested but unavailable. No managed fallback for these — they are CGAL-
// specific and have no in-tree equivalent.
//
// All entry points marshal flat managed arrays in/out of malloc'd native
// buffers. Native buffers are released via frahan_cgal_free_pdouble /
// frahan_cgal_free_pint immediately after marshal-back, so no GC pressure
// or pinning concerns.
//
// License: the CGAL packages used by these calls (Optimal_bounding_box,
// Straight_skeleton_2, Partition_2) are GPL-licensed in the open-source
// CGAL distribution. Build the shim only on machines that have CGAL
// installed; ship the .gha without the .dll and let the user opt in by
// installing CGAL themselves (BUILD.md option C). The .gha runs without
// any CGAL dependency when these methods are not called.
// =============================================================================

/// <summary>3D oriented bounding box.</summary>
public readonly struct ObbResult
{
    public ObbResult(
        double ox, double oy, double oz,
        double xax_x, double xax_y, double xax_z,
        double yax_x, double yax_y, double yax_z,
        double zax_x, double zax_y, double zax_z,
        double extX, double extY, double extZ)
    {
        OriginX = ox; OriginY = oy; OriginZ = oz;
        XAxisX = xax_x; XAxisY = xax_y; XAxisZ = xax_z;
        YAxisX = yax_x; YAxisY = yax_y; YAxisZ = yax_z;
        ZAxisX = zax_x; ZAxisY = zax_y; ZAxisZ = zax_z;
        ExtentX = extX; ExtentY = extY; ExtentZ = extZ;
    }

    public double OriginX { get; }
    public double OriginY { get; }
    public double OriginZ { get; }
    public double XAxisX { get; }
    public double XAxisY { get; }
    public double XAxisZ { get; }
    public double YAxisX { get; }
    public double YAxisY { get; }
    public double YAxisZ { get; }
    public double ZAxisX { get; }
    public double ZAxisY { get; }
    public double ZAxisZ { get; }
    public double ExtentX { get; }
    public double ExtentY { get; }
    public double ExtentZ { get; }
}

/// <summary>2D straight-skeleton result graph.</summary>
public sealed class StraightSkeletonResult
{
    public StraightSkeletonResult(double[] verts, int[] edges, double[] times)
    {
        Vertices = verts ?? Array.Empty<double>();
        Edges = edges ?? Array.Empty<int>();
        Times = times ?? Array.Empty<double>();
    }

    /// <summary>Flat 2D coordinates: 2 * VertexCount doubles.</summary>
    public double[] Vertices { get; }

    /// <summary>Flat edge endpoints: 2 * EdgeCount int32s, indices into Vertices.</summary>
    public int[] Edges { get; }

    /// <summary>Time-of-arrival per vertex: VertexCount doubles. Boundary verts have time 0.</summary>
    public double[] Times { get; }

    public int VertexCount => Vertices.Length / 2;
    public int EdgeCount => Edges.Length / 2;
}

/// <summary>2D polygon-partition result.</summary>
public sealed class PolygonPartitionResult
{
    public PolygonPartitionResult(double[] verts, int[] indices, int[] starts)
    {
        Vertices = verts ?? Array.Empty<double>();
        Indices = indices ?? Array.Empty<int>();
        Starts = starts ?? Array.Empty<int>();
    }

    /// <summary>Flat 2D coordinates: 2 * VertexCount doubles.</summary>
    public double[] Vertices { get; }

    /// <summary>Flat vertex indices listing each sub-polygon's verts in order.</summary>
    public int[] Indices { get; }

    /// <summary>Length PolygonCount + 1. Polygon i = Indices[Starts[i]..Starts[i+1]).</summary>
    public int[] Starts { get; }

    public int VertexCount => Vertices.Length / 2;
    public int PolygonCount => Math.Max(0, Starts.Length - 1);

    /// <summary>Returns the vertex coordinates of polygon <paramref name="i"/> as flat 2D pairs.</summary>
    public double[] GetPolygon(int i)
    {
        if (i < 0 || i >= PolygonCount) throw new ArgumentOutOfRangeException(nameof(i));
        var start = Starts[i];
        var end = Starts[i + 1];
        var coords = new double[(end - start) * 2];
        for (int k = 0; k < end - start; k++)
        {
            var vi = Indices[start + k];
            coords[2 * k + 0] = Vertices[2 * vi + 0];
            coords[2 * k + 1] = Vertices[2 * vi + 1];
        }
        return coords;
    }
}

/// <summary>
/// Result of <see cref="CgalGeometry.SegmentMeshBySdf"/>. One sub-mesh
/// per non-empty SDF segment, plus the actual segment count CGAL
/// produced (which may be smaller than the requested
/// <c>nbClusters</c> if the graph-cut step left some clusters empty).
/// </summary>
public sealed class SdfSegmentResult
{
    public SdfSegmentResult(IReadOnlyList<MeshSnapshot> segments, int actualClusters)
    {
        Segments = segments ?? Array.Empty<MeshSnapshot>();
        ActualClusters = actualClusters;
    }
    /// <summary>One sub-mesh per non-empty segment, indexed 0..SegmentCount-1.</summary>
    public IReadOnlyList<MeshSnapshot> Segments { get; }
    public int SegmentCount => Segments.Count;
    /// <summary>CGAL-reported actual cluster count (may differ from SegmentCount if some clusters were empty).</summary>
    public int ActualClusters { get; }
}

public enum PartitionKind
{
    /// <summary>Hertel-Mehlhorn approximate convex partition (fast).</summary>
    ApproxConvex = 0,
    /// <summary>Greene optimal convex partition (O(n^4), minimal pieces).</summary>
    OptimalConvex = 1,
    /// <summary>Y-monotone partition.</summary>
    YMonotone = 2,
}

/// <summary>Stop predicate for <see cref="CgalGeometry.DecimateMesh"/>.</summary>
public enum DecimateStopKind
{
    /// <summary>Stop when remaining_edges / initial_edges &lt;= value (value in (0, 1)). Most common.</summary>
    CountRatio = 0,
    /// <summary>Stop when remaining edge count &lt;= value (rounded to size_t, &gt;= 1).</summary>
    EdgeCount = 1,
    /// <summary>Stop when the next edge to collapse has length &gt;= value. Preserves edges shorter than the threshold (keeps sharp features intact).</summary>
    EdgeLength = 2,
}

public static class CgalGeometry
{
    /// <summary>True iff the native shim is loadable.</summary>
    public static bool IsAvailable => CgalMeshBoolean.IsAvailable;

    // ─── OBB (3D) ────────────────────────────────────────────────────────

    /// <summary>
    /// True iff the native shim was built with Eigen3 (OBB requires
    /// CGAL's oriented_bounding_box, which transitively requires Eigen).
    /// First call probes for the symbol; subsequent calls return cached
    /// answer. False on any of: shim absent, shim built without Eigen,
    /// older shim that pre-dates the OBB entry point.
    /// </summary>
    public static bool IsObbAvailable
    {
        get
        {
            if (_isObbAvailable.HasValue) return _isObbAvailable.Value;
            if (!IsAvailable) { _isObbAvailable = false; return false; }
            try
            {
                // Probe with a known-trivial single-point input. The shim
                // returns -30 ("non-positive vertex count") for vcount <= 0,
                // but a 1-vertex call exercises the symbol resolution.
                var probe = new double[] { 0, 0, 0 };
                var outBuf = new double[15];
                _ = Native.frahan_cgal_obb_3d(probe, 1, Array.Empty<int>(), 0, outBuf);
                _isObbAvailable = true;
            }
            catch (EntryPointNotFoundException) { _isObbAvailable = false; }
            catch (DllNotFoundException) { _isObbAvailable = false; }
            return _isObbAvailable ?? false;
        }
    }
    private static bool? _isObbAvailable;

    /// <summary>
    /// Compute the optimal oriented bounding box of a 3D point set. The
    /// optional triangle topology is currently unused by CGAL's OBB
    /// algorithm but accepted to mirror the mesh-boolean signature.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Native shim not available, OR shim built without Eigen3 (in which
    /// case <see cref="IsObbAvailable"/> is false), OR CGAL returned an
    /// error. Check IsObbAvailable before calling to distinguish "build
    /// without Eigen" from "runtime error".
    /// </exception>
    public static ObbResult OrientedBoundingBox(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndicesOrNull = null)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (vertexCoordsXyz.Count < 3) throw new ArgumentException("Need at least one vertex (3 coords).", nameof(vertexCoordsXyz));
        if (vertexCoordsXyz.Count % 3 != 0) throw new ArgumentException("Vertex coords must be a multiple of 3.", nameof(vertexCoordsXyz));
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; OBB requires CGAL.");
        if (!IsObbAvailable) throw new InvalidOperationException("frahan_cgal native shim was built without Eigen3 — OBB entry point absent. Rebuild with Eigen3 installed (see BUILD.md) to enable.");

        var verts = ToArrayD(vertexCoordsXyz);
        var tris = triangleIndicesOrNull != null ? ToArrayI(triangleIndicesOrNull) : Array.Empty<int>();
        var outBuf = new double[15];

        int rc;
        try
        {
            rc = Native.frahan_cgal_obb_3d(
                verts, verts.Length / 3,
                tris, tris.Length / 3,
                outBuf);
        }
        catch (EntryPointNotFoundException)
        {
            _isObbAvailable = false;
            throw new InvalidOperationException("frahan_cgal_obb_3d entry point missing; shim built without Eigen3.");
        }

        if (rc != 0)
        {
            var err = ReadLastError();
            throw new InvalidOperationException($"CGAL OBB failed (rc={rc}): {err}");
        }
        return new ObbResult(
            outBuf[0], outBuf[1], outBuf[2],
            outBuf[3], outBuf[4], outBuf[5],
            outBuf[6], outBuf[7], outBuf[8],
            outBuf[9], outBuf[10], outBuf[11],
            outBuf[12], outBuf[13], outBuf[14]);
    }

    // ─── Straight skeleton (2D) ──────────────────────────────────────────

    /// <summary>
    /// Compute the interior straight skeleton of a 2D simple polygon
    /// (with optional holes). Outer ring should be CCW; holes CW. The
    /// native side reverses if needed.
    /// </summary>
    /// <param name="outerVertsXy">2 * outerCount doubles (CCW preferred).</param>
    /// <param name="holesVertsXy">Concatenation of all holes' flat coords (2 * sum(holeVcounts) doubles), or null.</param>
    /// <param name="holeVcounts">Per-hole vertex count, or null when no holes.</param>
    public static StraightSkeletonResult StraightSkeleton2D(
        IReadOnlyList<double> outerVertsXy,
        IReadOnlyList<double> holesVertsXy = null,
        IReadOnlyList<int> holeVcounts = null)
    {
        if (outerVertsXy == null) throw new ArgumentNullException(nameof(outerVertsXy));
        if (outerVertsXy.Count < 6) throw new ArgumentException("Outer ring needs at least 3 points (6 coords).", nameof(outerVertsXy));
        if (outerVertsXy.Count % 2 != 0) throw new ArgumentException("Outer coords must be a multiple of 2.", nameof(outerVertsXy));
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; straight skeleton requires CGAL.");

        var outer = ToArrayD(outerVertsXy);
        double[] holes = holesVertsXy != null ? ToArrayD(holesVertsXy) : Array.Empty<double>();
        int[] holeCounts = holeVcounts != null ? ToArrayI(holeVcounts) : Array.Empty<int>();

        IntPtr pVerts = IntPtr.Zero, pTimes = IntPtr.Zero;
        IntPtr pEdges = IntPtr.Zero;
        int vc = 0, ec = 0, tc = 0;

        var rc = Native.frahan_cgal_straight_skeleton_2d(
            outer, outer.Length / 2,
            holes, holeCounts, holeCounts.Length,
            out pVerts, out vc,
            out pEdges, out ec,
            out pTimes, out tc);

        if (rc != 0)
        {
            var err = ReadLastError();
            Native.frahan_cgal_free_pdouble(pVerts);
            Native.frahan_cgal_free_pint(pEdges);
            Native.frahan_cgal_free_pdouble(pTimes);
            throw new InvalidOperationException($"CGAL straight skeleton failed (rc={rc}): {err}");
        }

        double[] verts;
        int[] edges;
        double[] times;
        try
        {
            verts = new double[vc * 2];
            if (vc > 0) Marshal.Copy(pVerts, verts, 0, vc * 2);
            edges = new int[ec * 2];
            if (ec > 0) Marshal.Copy(pEdges, edges, 0, ec * 2);
            times = new double[tc];
            if (tc > 0) Marshal.Copy(pTimes, times, 0, tc);
        }
        finally
        {
            Native.frahan_cgal_free_pdouble(pVerts);
            Native.frahan_cgal_free_pint(pEdges);
            Native.frahan_cgal_free_pdouble(pTimes);
        }

        return new StraightSkeletonResult(verts, edges, times);
    }

    // ─── Mesh repair ─────────────────────────────────────────────────────

    /// <summary>
    /// Robust mesh repair pipeline via CGAL Polygon_mesh_processing:
    /// triangulate_faces → stitch_borders → remove_degenerate_faces →
    /// orient_to_bound_a_volume (if closed) → collect_garbage.
    ///
    /// Stronger than Rhino's built-in <c>Mesh.RebuildNormals</c> +
    /// <c>UnifyNormals</c> + <c>FillHoles</c> because it uses
    /// CGAL's exact-predicate adjacency to actually merge coincident
    /// half-edges (not just normal-vector heuristics).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Native shim not available, or CGAL refused the input (typically
    /// non-manifold beyond what stitch_borders can fix).
    /// </exception>
    public static MeshSnapshot RepairMesh(MeshSnapshot mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; mesh repair requires CGAL.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_cgal_repair_mesh(
            v, v.Length / 3,
            t, t.Length / 3,
            out outV, out outVc,
            out outT, out outTc);

        if (rc != 0)
        {
            var err = ReadLastError();
            // free anything the shim might have returned despite failure
            CgalMeshBoolean_FreeBuffers(outV, outT);
            throw new InvalidOperationException($"CGAL repair_mesh failed (rc={rc}): {err}");
        }

        double[] rv;
        int[] rt;
        try
        {
            rv = new double[outVc * 3];
            if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
            rt = new int[outTc * 3];
            if (outTc > 0) Marshal.Copy(outT, rt, 0, outTc * 3);
        }
        finally
        {
            CgalMeshBoolean_FreeBuffers(outV, outT);
        }

        return new MeshSnapshot(rv, rt);
    }

    // free helper that matches the boolean-output free-buffer contract
    [DllImport("frahan_cgal", EntryPoint = "frahan_cgal_free_buffers", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CgalMeshBoolean_FreeBuffers(IntPtr verts, IntPtr tris);

    // ─── Mesh decimation ─────────────────────────────────────────────────

    /// <summary>
    /// Mesh simplification via CGAL Surface_mesh_simplification edge
    /// collapse with the default Lindstrom-Turk cost/placement policies.
    /// Three stop modes, see <see cref="DecimateStopKind"/>.
    /// </summary>
    /// <param name="mesh">Input mesh; should be a valid 2-manifold for stable results.</param>
    /// <param name="stopKind">Which stop predicate to use.</param>
    /// <param name="stopValue">Stop threshold; meaning depends on stopKind.</param>
    /// <exception cref="InvalidOperationException">
    /// Native shim not available, or CGAL refused the input / stop value.
    /// </exception>
    public static MeshSnapshot DecimateMesh(
        MeshSnapshot mesh, DecimateStopKind stopKind, double stopValue)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; decimation requires CGAL.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_cgal_decimate_mesh(
            v, v.Length / 3,
            t, t.Length / 3,
            (int)stopKind, stopValue,
            out outV, out outVc,
            out outT, out outTc);

        if (rc != 0)
        {
            var err = ReadLastError();
            CgalMeshBoolean_FreeBuffers(outV, outT);
            throw new InvalidOperationException($"CGAL decimate_mesh failed (rc={rc}): {err}");
        }

        double[] rv;
        int[] rt;
        try
        {
            rv = new double[outVc * 3];
            if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
            rt = new int[outTc * 3];
            if (outTc > 0) Marshal.Copy(outT, rt, 0, outTc * 3);
        }
        finally
        {
            CgalMeshBoolean_FreeBuffers(outV, outT);
        }

        return new MeshSnapshot(rv, rt);
    }

    // ─── Polygon partition (2D) ──────────────────────────────────────────

    public static PolygonPartitionResult PolygonPartition2D(
        IReadOnlyList<double> vertsXy,
        PartitionKind kind = PartitionKind.ApproxConvex)
    {
        if (vertsXy == null) throw new ArgumentNullException(nameof(vertsXy));
        if (vertsXy.Count < 6) throw new ArgumentException("Polygon needs at least 3 vertices (6 coords).", nameof(vertsXy));
        if (vertsXy.Count % 2 != 0) throw new ArgumentException("Coords must be a multiple of 2.", nameof(vertsXy));
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; partition requires CGAL.");

        var verts = ToArrayD(vertsXy);
        IntPtr pVerts = IntPtr.Zero, pIndices = IntPtr.Zero, pStarts = IntPtr.Zero;
        int vc = 0, ic = 0, pc = 0;

        var rc = Native.frahan_cgal_polygon_partition_2d(
            verts, verts.Length / 2,
            (int)kind,
            out pVerts, out vc,
            out pIndices, out ic,
            out pStarts, out pc);

        if (rc != 0)
        {
            var err = ReadLastError();
            Native.frahan_cgal_free_pdouble(pVerts);
            Native.frahan_cgal_free_pint(pIndices);
            Native.frahan_cgal_free_pint(pStarts);
            throw new InvalidOperationException($"CGAL partition failed (rc={rc}): {err}");
        }

        double[] ov;
        int[] oi;
        int[] os;
        try
        {
            ov = new double[vc * 2];
            if (vc > 0) Marshal.Copy(pVerts, ov, 0, vc * 2);
            oi = new int[ic];
            if (ic > 0) Marshal.Copy(pIndices, oi, 0, ic);
            os = new int[pc + 1];
            if (pc + 1 > 0) Marshal.Copy(pStarts, os, 0, pc + 1);
        }
        finally
        {
            Native.frahan_cgal_free_pdouble(pVerts);
            Native.frahan_cgal_free_pint(pIndices);
            Native.frahan_cgal_free_pint(pStarts);
        }

        return new PolygonPartitionResult(ov, oi, os);
    }

    // ─── Surface mesh segmentation (SDF) ────────────────────────────────

    /// <summary>
    /// Segment a surface mesh into clusters via Shape Diameter Function
    /// (SDF) - CGAL's tried-and-tested feature-based decomposition.
    /// Returns one MeshSnapshot per output segment. The actual segment
    /// count may be less than <paramref name="nbClusters"/> if some
    /// clusters end up empty after graph-cut smoothing.
    /// </summary>
    /// <param name="mesh">Input surface mesh. Should be a 2-manifold for stable SDF estimation.</param>
    /// <param name="nbClusters">Target number of segments (>= 2). CGAL's example uses 5.</param>
    /// <param name="smoothingLambda">Graph-cut smoothness penalty in [0, 1]. CGAL default is 0.26. Higher = more spatially coherent / fewer islands.</param>
    /// <param name="coneAngleRadians">SDF inward cone half-angle. Pass &lt;= 0 to use CGAL's default (2/3 * pi).</param>
    /// <param name="nbRays">Rays per facet for SDF estimation. Pass &lt;= 0 to use CGAL's default (25).</param>
    /// <param name="postprocess">Run CGAL's SDF postprocess (smoothing + connected-component cleanup).</param>
    public static SdfSegmentResult SegmentMeshBySdf(
        MeshSnapshot mesh,
        int nbClusters,
        double smoothingLambda = 0.26,
        double coneAngleRadians = 0.0,
        int nbRays = 0,
        bool postprocess = true)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (nbClusters < 2) throw new ArgumentOutOfRangeException(nameof(nbClusters), "must be >= 2");
        if (smoothingLambda < 0.0 || smoothingLambda > 1.0)
            throw new ArgumentOutOfRangeException(nameof(smoothingLambda), "must be in [0, 1]");
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; segmentation requires CGAL.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr pIds = IntPtr.Zero;
        int idCount = 0, actualClusters = 0;

        var rc = Native.frahan_cgal_segment_sdf(
            v, v.Length / 3,
            t, t.Length / 3,
            nbClusters, smoothingLambda,
            coneAngleRadians, nbRays, postprocess ? 1 : 0,
            out pIds, out idCount,
            out actualClusters);

        if (rc != 0)
        {
            var err = ReadLastError();
            if (pIds != IntPtr.Zero) Native.frahan_cgal_free_pint(pIds);
            throw new InvalidOperationException($"CGAL segment_sdf failed (rc={rc}): {err}");
        }

        int[] segIds;
        try
        {
            segIds = new int[idCount];
            if (idCount > 0) Marshal.Copy(pIds, segIds, 0, idCount);
        }
        finally
        {
            if (pIds != IntPtr.Zero) Native.frahan_cgal_free_pint(pIds);
        }

        var subMeshes = SplitTrianglesBySegmentId(v, t, segIds);
        return new SdfSegmentResult(subMeshes, actualClusters);
    }

    /// <summary>
    /// Cluster mesh faces by dihedral-angle change. Two-stage CGAL
    /// pipeline: detect_sharp_edges marks edges whose dihedral exceeds
    /// the threshold; connected_components flood-fills faces while
    /// treating those edges as walls. Returns one MeshSnapshot per
    /// smoothly-connected region.
    ///
    /// Tuning: 5-15 deg = strict planarity, 30-60 deg = smooth-band
    /// detection on curved forms, 90+ deg = only orthogonal-ish creases
    /// separate regions.
    /// </summary>
    /// <param name="mesh">Input surface mesh. Should be 2-manifold for stable dihedral computation.</param>
    /// <param name="angleThresholdDegrees">Dihedral angle threshold in degrees, in (0, 180).</param>
    public static SdfSegmentResult SegmentMeshByAngle(
        MeshSnapshot mesh,
        double angleThresholdDegrees)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (!(angleThresholdDegrees > 0.0 && angleThresholdDegrees < 180.0))
            throw new ArgumentOutOfRangeException(nameof(angleThresholdDegrees), "must be in (0, 180)");
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; segmentation requires CGAL.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr pIds = IntPtr.Zero;
        int idCount = 0, actualClusters = 0;

        var rc = Native.frahan_cgal_segment_by_angle(
            v, v.Length / 3,
            t, t.Length / 3,
            angleThresholdDegrees,
            out pIds, out idCount,
            out actualClusters);

        if (rc != 0)
        {
            var err = ReadLastError();
            if (pIds != IntPtr.Zero) Native.frahan_cgal_free_pint(pIds);
            throw new InvalidOperationException($"CGAL segment_by_angle failed (rc={rc}): {err}");
        }

        int[] segIds;
        try
        {
            segIds = new int[idCount];
            if (idCount > 0) Marshal.Copy(pIds, segIds, 0, idCount);
        }
        finally
        {
            if (pIds != IntPtr.Zero) Native.frahan_cgal_free_pint(pIds);
        }

        var subMeshes = SplitTrianglesBySegmentId(v, t, segIds);
        return new SdfSegmentResult(subMeshes, actualClusters);
    }

    /// <summary>
    /// Geodesic Voronoi segmentation via the Heat Method (Crane et al.
    /// 2013). For each seed point, snaps to the nearest mesh vertex and
    /// computes a geodesic distance field FROM that vertex; each
    /// surface point joins the cell of the seed with the smallest
    /// on-surface distance. The result is one mesh per cell, with cell
    /// boundaries that follow surface curvature (unlike Euclidean RVD,
    /// which can slice through curvy regions).
    /// </summary>
    /// <param name="mesh">Input surface mesh. Should be 2-manifold for a stable cotangent Laplacian.</param>
    /// <param name="seedPointsXyz">Flat 3D points: 3 * nbSeeds doubles. Each is snapped to the nearest mesh vertex internally.</param>
    public static SdfSegmentResult SegmentMeshByGeodesicVoronoi(
        MeshSnapshot mesh,
        IReadOnlyList<double> seedPointsXyz)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (seedPointsXyz == null) throw new ArgumentNullException(nameof(seedPointsXyz));
        if (seedPointsXyz.Count < 3 || seedPointsXyz.Count % 3 != 0)
            throw new ArgumentException("seedPointsXyz must be a non-empty flat array of 3-tuples.", nameof(seedPointsXyz));
        if (!IsAvailable) throw new InvalidOperationException("frahan_cgal native shim not loaded; geodesic Voronoi requires CGAL.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        var seeds = ToArrayD(seedPointsXyz);
        IntPtr pIds = IntPtr.Zero;
        int idCount = 0, actualClusters = 0;

        var rc = Native.frahan_cgal_geodesic_voronoi(
            v, v.Length / 3,
            t, t.Length / 3,
            seeds, seeds.Length / 3,
            out pIds, out idCount,
            out actualClusters);

        if (rc != 0)
        {
            var err = ReadLastError();
            if (pIds != IntPtr.Zero) Native.frahan_cgal_free_pint(pIds);
            throw new InvalidOperationException($"CGAL geodesic_voronoi failed (rc={rc}): {err}");
        }

        int[] segIds;
        try
        {
            segIds = new int[idCount];
            if (idCount > 0) Marshal.Copy(pIds, segIds, 0, idCount);
        }
        finally
        {
            if (pIds != IntPtr.Zero) Native.frahan_cgal_free_pint(pIds);
        }

        var subMeshes = SplitTrianglesBySegmentId(v, t, segIds);
        return new SdfSegmentResult(subMeshes, actualClusters);
    }

    private static IReadOnlyList<MeshSnapshot> SplitTrianglesBySegmentId(
        double[] verts, int[] tris, int[] segIds)
    {
        if (segIds.Length == 0) return Array.Empty<MeshSnapshot>();

        int maxId = -1;
        for (int i = 0; i < segIds.Length; i++) if (segIds[i] > maxId) maxId = segIds[i];
        var nSegs = maxId + 1;

        var bucketTris = new List<int>[nSegs];
        for (int i = 0; i < nSegs; i++) bucketTris[i] = new List<int>();
        for (int f = 0; f < segIds.Length; f++)
        {
            int sid = segIds[f];
            if (sid < 0 || sid >= nSegs) continue;
            bucketTris[sid].Add(tris[3 * f + 0]);
            bucketTris[sid].Add(tris[3 * f + 1]);
            bucketTris[sid].Add(tris[3 * f + 2]);
        }

        var segs = new List<MeshSnapshot>(nSegs);
        for (int s = 0; s < nSegs; s++)
        {
            var globalIdxs = bucketTris[s];
            if (globalIdxs.Count == 0) continue;
            var localOf = new Dictionary<int, int>();
            var localVerts = new List<double>();
            var localTris = new List<int>(globalIdxs.Count);
            for (int k = 0; k < globalIdxs.Count; k++)
            {
                int g = globalIdxs[k];
                if (!localOf.TryGetValue(g, out int lv))
                {
                    lv = localVerts.Count / 3;
                    localOf[g] = lv;
                    localVerts.Add(verts[3 * g + 0]);
                    localVerts.Add(verts[3 * g + 1]);
                    localVerts.Add(verts[3 * g + 2]);
                }
                localTris.Add(lv);
            }
            segs.Add(new MeshSnapshot(localVerts.ToArray(), localTris.ToArray()));
        }
        return segs;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string ReadLastError()
    {
        try
        {
            var ptr = Native.frahan_cgal_last_error();
            return Marshal.PtrToStringAnsi(ptr) ?? "(none)";
        }
        catch { return "(could not read error)"; }
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

    // ─── P/Invoke ────────────────────────────────────────────────────────

    private static class Native
    {
        private const string Dll = "frahan_cgal";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_cgal_last_error();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_cgal_free_pdouble(IntPtr p);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_cgal_free_pint(IntPtr p);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_obb_3d(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            [Out] double[] outObb);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_straight_skeleton_2d(
            [In] double[] outerVerts, int outerVc,
            [In] double[] holeVerts, [In] int[] holeVcounts, int holeCount,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outEdges, out int outEcount,
            out IntPtr outTimes, out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_polygon_partition_2d(
            [In] double[] verts, int vcount,
            int kind,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outIndices, out int outIcount,
            out IntPtr outStarts, out int outPcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_repair_mesh(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTris,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_decimate_mesh(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            int stopKind, double stopValue,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTris,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_segment_sdf(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            int nbClusters, double smoothingLambda,
            double coneAngleRadians, int nbRays, int postprocess,
            out IntPtr outSegmentIds, out int outIdcount,
            out int outActualClusters);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_segment_by_angle(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            double angleThresholdDegrees,
            out IntPtr outSegmentIds, out int outIdcount,
            out int outActualClusters);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_cgal_geodesic_voronoi(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            [In] double[] seedPoints, int seedCount,
            out IntPtr outSegmentIds, out int outIdcount,
            out int outActualClusters);
    }
}
