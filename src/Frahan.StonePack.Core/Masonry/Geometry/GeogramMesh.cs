#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// GeogramMesh - managed front-end for the optional native Geogram shim
// (frahan_geogram.dll / libfrahan_geogram.so / .dylib). Wraps Bruno
// Levy's Geogram (Inria) mesh operations.
//
// v1 surface: vertex-clustering decimation. Future: surface remesh,
// restricted Voronoi diagrams - the natural Geogram on-ramp toward the
// masonry block-partition pipeline.
//
// License: Geogram is BSD-3 throughout - safe to ship inside a binary
// plugin without the GPL ceremony that CGAL requires.
// =============================================================================

/// <summary>
/// Mode flags for <see cref="GeogramMesh.DecimateMesh"/>. Bitwise-OR
/// combinable. Mirrors GEO::MeshDecimateMode.
/// </summary>
[Flags]
public enum GeogramDecimateMode
{
    /// <summary>Fast / raw result (no extra cleanup).</summary>
    Fast = 0,
    /// <summary>Remove duplicated vertices.</summary>
    RemoveDuplicates = 1,
    /// <summary>Remove degree-3 vertices.</summary>
    RemoveDegree3 = 2,
    /// <summary>Preserve borders (open edges of the mesh).</summary>
    KeepBorders = 4,
    /// <summary>RemoveDuplicates | RemoveDegree3 | KeepBorders. Default.</summary>
    Default = RemoveDuplicates | RemoveDegree3 | KeepBorders,
}

/// <summary>
/// Mode flags for <see cref="GeogramMesh.RepairMesh"/>. Bitwise-OR
/// combinable. Mirrors GEO::MeshRepairMode.
/// </summary>
[Flags]
public enum GeogramRepairMode
{
    /// <summary>Always done; dissociate non-manifold vertices.</summary>
    TopologyOnly = 0,
    /// <summary>Merge identical vertices (within colocate epsilon).</summary>
    Colocate = 1,
    /// <summary>Remove duplicated facets.</summary>
    RemoveDuplicateFacets = 2,
    /// <summary>Triangulate non-triangle facets.</summary>
    Triangulate = 4,
    /// <summary>Post-process Co3Ne reconstruction.</summary>
    Reconstruct = 8,
    /// <summary>Suppress messages.</summary>
    Quiet = 16,
    /// <summary>Colocate | RemoveDuplicateFacets | Triangulate. Default.</summary>
    Default = Colocate | RemoveDuplicateFacets | Triangulate,
}

/// <summary>Volumetric tet mesh result from <see cref="GeogramMesh.Tetrahedralize"/>.</summary>
public sealed class TetMeshSnapshot
{
    public TetMeshSnapshot(double[] verts, int[] tets)
    {
        VertexCoordsXyz = verts ?? Array.Empty<double>();
        TetIndices = tets ?? Array.Empty<int>();
    }
    /// <summary>Flat 3D coordinates: 3 * VertexCount doubles.</summary>
    public double[] VertexCoordsXyz { get; }
    /// <summary>Flat tet indices: 4 * TetCount int32s.</summary>
    public int[] TetIndices { get; }
    public int VertexCount => VertexCoordsXyz.Length / 3;
    public int TetCount    => TetIndices.Length / 4;
}

/// <summary>RVD result: one MeshSnapshot per Voronoi cell.</summary>
public sealed class RvdResult
{
    public RvdResult(IReadOnlyList<MeshSnapshot> cells)
    {
        Cells = cells ?? Array.Empty<MeshSnapshot>();
    }
    /// <summary>One sub-mesh per Voronoi cell, indexed 0..CellCount-1.</summary>
    public IReadOnlyList<MeshSnapshot> Cells { get; }
    public int CellCount => Cells.Count;
}

public static class GeogramMesh
{
    private static bool? _isAvailable;
    private static string _version;
    private static readonly object _lock = new object();

    /// <summary>
    /// True iff <c>frahan_geogram</c> can be loaded by the platform loader.
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
                    var ptr = Native.frahan_geogram_version();
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
    /// Reported version string from the loaded shim, e.g.
    /// "Frahan-Geogram 0.1 (Geogram 1.9.9)". Empty when not available.
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
    /// Vertex-clustering decimation via GEO::mesh_decimate_vertex_clustering.
    /// Higher <paramref name="nbBins"/> = finer voxel grid = more
    /// detailed output. Typical range 50..300; default 100.
    ///
    /// This is a different decimation flavour from <see
    /// cref="CgalGeometry.DecimateMesh"/> (edge-collapse). Voxel binning
    /// is fast and produces a regular spatial sampling; edge-collapse
    /// is topology-preserving and quadric-error-driven. Use Geogram's
    /// for very high-poly scans where you want a controlled spatial
    /// resolution; CGAL's for precise count targeting.
    /// </summary>
    /// <exception cref="ArgumentNullException">mesh is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">nbBins &lt; 2.</exception>
    /// <exception cref="NotSupportedException">Native shim not available.</exception>
    /// <exception cref="InvalidOperationException">Geogram returned an error.</exception>
    public static MeshSnapshot DecimateMesh(
        MeshSnapshot mesh,
        int nbBins = 100,
        GeogramDecimateMode mode = GeogramDecimateMode.Default)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (nbBins < 2) throw new ArgumentOutOfRangeException(nameof(nbBins), "nbBins must be >= 2 (typical 50..300).");
        if (!IsAvailable)
            throw new NotSupportedException(
                "frahan_geogram shim not loaded. Build it from native/geogram_shim/ " +
                "and place the DLL alongside Frahan.StonePack.gha.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_geogram_decimate_mesh(
            v, v.Length / 3,
            t, t.Length / 3,
            nbBins, (int)mode,
            out outV, out outVc,
            out outT, out outTc);

        if (rc != 0)
        {
            string err;
            try { err = Marshal.PtrToStringAnsi(Native.frahan_geogram_last_error()) ?? "(none)"; }
            catch { err = "(could not read error)"; }
            FreeAll(outV, outT);
            throw new InvalidOperationException($"Geogram decimate_mesh failed (rc={rc}): {err}");
        }

        var rv = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
        var rt = new int[outTc * 3];
        if (outTc > 0) Marshal.Copy(outT, rt, 0, outTc * 3);
        FreeAll(outV, outT);

        return new MeshSnapshot(rv, rt);
    }

    // -- 2. Mesh Repair -------------------------------------------------------

    /// <summary>
    /// Topology-aware mesh repair via GEO::mesh_repair (BSD-3 parallel
    /// to <see cref="CgalGeometry.RepairMesh"/>). Default mode:
    /// colocate + remove duplicate facets + triangulate.
    /// </summary>
    public static MeshSnapshot RepairMesh(
        MeshSnapshot mesh,
        GeogramRepairMode mode = GeogramRepairMode.Default,
        double colocateEpsilon = 0.0)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_geogram_repair_mesh(
            v, v.Length / 3, t, t.Length / 3,
            (int)mode, colocateEpsilon,
            out outV, out outVc, out outT, out outTc);

        if (rc != 0)
        {
            var err = ReadLastError();
            FreeAll(outV, outT);
            throw new InvalidOperationException($"Geogram repair_mesh failed (rc={rc}): {err}");
        }
        var rv = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
        var rt = new int[outTc * 3];
        if (outTc > 0) Marshal.Copy(outT, rt, 0, outTc * 3);
        FreeAll(outV, outT);
        return new MeshSnapshot(rv, rt);
    }

    /// <summary>
    /// Triangulate open boundary loops smaller than a size threshold.
    /// Use it to close spurious sliver-holes in a Voronoi cell sub-mesh
    /// before feeding to BFF, while keeping the main outer boundary
    /// open. BSD-3 (GEO::fill_holes).
    /// </summary>
    /// <param name="maxHoleArea">Maximum hole AREA (input units squared) to fill. 0 fills nothing; pass <see cref="double.PositiveInfinity"/> to fill every hole regardless of size.</param>
    /// <param name="maxHoleEdges">Maximum boundary edges per hole. 0 or negative = no edge limit.</param>
    /// <param name="repairAfter">Run mesh_repair (DEFAULT mode) after filling to clean up duplicate vertices / facets.</param>
    public static MeshSnapshot FillHoles(
        MeshSnapshot mesh,
        double maxHoleArea,
        int maxHoleEdges = 0,
        bool repairAfter = true)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");
        if (maxHoleArea < 0.0) throw new ArgumentOutOfRangeException(nameof(maxHoleArea), "must be >= 0");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_geogram_fill_holes(
            v, v.Length / 3, t, t.Length / 3,
            maxHoleArea, maxHoleEdges, repairAfter ? 1 : 0,
            out outV, out outVc, out outT, out outTc);

        if (rc != 0)
        {
            var err = ReadLastError();
            FreeAll(outV, outT);
            throw new InvalidOperationException($"Geogram fill_holes failed (rc={rc}): {err}");
        }
        var rv = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
        var rt = new int[outTc * 3];
        if (outTc > 0) Marshal.Copy(outT, rt, 0, outTc * 3);
        FreeAll(outV, outT);
        return new MeshSnapshot(rv, rt);
    }

    // -- 3. OBB ---------------------------------------------------------------

    /// <summary>
    /// Oriented bounding box via PrincipalAxes3d. Output mirrors
    /// <see cref="CgalGeometry.OrientedBoundingBox"/>'s ObbResult layout.
    /// Lighter than CGAL OBB - no Eigen dependency. BSD-3.
    /// </summary>
    public static ObbResult OrientedBoundingBox(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndicesOrNull = null)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (vertexCoordsXyz.Count < 3) throw new ArgumentException("Need at least one vertex.", nameof(vertexCoordsXyz));
        if (vertexCoordsXyz.Count % 3 != 0) throw new ArgumentException("Vertex coords must be a multiple of 3.", nameof(vertexCoordsXyz));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var verts = ToArrayD(vertexCoordsXyz);
        var tris = triangleIndicesOrNull != null ? ToArrayI(triangleIndicesOrNull) : Array.Empty<int>();
        var outBuf = new double[15];

        var rc = Native.frahan_geogram_obb_3d(
            verts, verts.Length / 3,
            tris,  tris.Length  / 3,
            outBuf);

        if (rc != 0)
        {
            var err = ReadLastError();
            throw new InvalidOperationException($"Geogram obb_3d failed (rc={rc}): {err}");
        }
        return new ObbResult(
            outBuf[0], outBuf[1], outBuf[2],
            outBuf[3], outBuf[4], outBuf[5],
            outBuf[6], outBuf[7], outBuf[8],
            outBuf[9], outBuf[10], outBuf[11],
            outBuf[12], outBuf[13], outBuf[14]);
    }

    // -- 4. Remesh Uniform ----------------------------------------------------

    /// <summary>
    /// Uniform surface remeshing via centroidal-Voronoi-driven
    /// Lloyd + Newton optimization (GEO::remesh_smooth).
    /// </summary>
    public static MeshSnapshot RemeshUniform(
        MeshSnapshot mesh, int nbPoints,
        int nbLloyd = 5, int nbNewton = 30)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (nbPoints < 4) throw new ArgumentOutOfRangeException(nameof(nbPoints), "nbPoints must be >= 4.");
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_geogram_remesh_uniform(
            v, v.Length / 3, t, t.Length / 3,
            nbPoints, nbLloyd, nbNewton,
            out outV, out outVc, out outT, out outTc);

        if (rc != 0)
        {
            var err = ReadLastError();
            FreeAll(outV, outT);
            throw new InvalidOperationException($"Geogram remesh_uniform failed (rc={rc}): {err}");
        }
        var rv = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
        var rt = new int[outTc * 3];
        if (outTc > 0) Marshal.Copy(outT, rt, 0, outTc * 3);
        FreeAll(outV, outT);
        return new MeshSnapshot(rv, rt);
    }

    // -- 5. Tetrahedralize ----------------------------------------------------

    /// <summary>
    /// Tetrahedralize a closed surface mesh via GEO::mesh_tetrahedralize.
    /// REQUIRES the shim to be built with GEOGRAM_WITH_TETGEN=ON. The
    /// default shim build has it OFF for BSD-3 license cleanliness.
    /// When OFF, this throws InvalidOperationException with rc=-210.
    /// </summary>
    public static TetMeshSnapshot Tetrahedralize(
        MeshSnapshot mesh,
        bool preprocess = true,
        bool refine = false,
        double quality = 2.0,
        bool keepRegions = false)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outV = IntPtr.Zero, outTets = IntPtr.Zero;
        int outVc = 0, outTc = 0;

        var rc = Native.frahan_geogram_tetrahedralize(
            v, v.Length / 3, t, t.Length / 3,
            preprocess ? 1 : 0, refine ? 1 : 0, quality, keepRegions ? 1 : 0,
            out outV, out outVc, out outTets, out outTc);

        if (rc != 0)
        {
            var err = ReadLastError();
            FreeAll(outV, outTets);
            throw new InvalidOperationException($"Geogram tetrahedralize failed (rc={rc}): {err}");
        }
        var rv = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, rv, 0, outVc * 3);
        var rt = new int[outTc * 4];
        if (outTc > 0) Marshal.Copy(outTets, rt, 0, outTc * 4);
        FreeAll(outV, outTets);
        return new TetMeshSnapshot(rv, rt);
    }

    // -- 6. CVT (centroidal Voronoi tessellation seeds) -----------------------

    /// <summary>
    /// Compute optimized seed positions via Centroidal Voronoi
    /// Tessellation. Output is a flat point array (3 * N doubles)
    /// suitable for feeding into <see cref="VoronoiPartition"/>.
    /// </summary>
    public static double[] CvtSeeds(
        MeshSnapshot mesh, int nbPoints,
        int nbLloyd = 5, int nbNewton = 30)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (nbPoints < 4) throw new ArgumentOutOfRangeException(nameof(nbPoints), "nbPoints must be >= 4.");
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        IntPtr outP = IntPtr.Zero;
        int outPc = 0;

        var rc = Native.frahan_geogram_cvt_compute(
            v, v.Length / 3, t, t.Length / 3,
            nbPoints, nbLloyd, nbNewton,
            out outP, out outPc);

        if (rc != 0)
        {
            var err = ReadLastError();
            if (outP != IntPtr.Zero) Native.frahan_geogram_free_pdouble(outP);
            throw new InvalidOperationException($"Geogram cvt_compute failed (rc={rc}): {err}");
        }
        var pts = new double[outPc * 3];
        if (outPc > 0) Marshal.Copy(outP, pts, 0, outPc * 3);
        if (outP != IntPtr.Zero) Native.frahan_geogram_free_pdouble(outP);
        return pts;
    }

    // -- 7. RVD (restricted Voronoi diagram, partitions surface into cells) --

    /// <summary>
    /// Partition the input surface mesh into Voronoi cells, one per
    /// seed point. Output is one MeshSnapshot per cell, each containing
    /// only the facets nearest to its seed.
    /// </summary>
    /// <param name="seedPointsXyz">Flat seed positions: 3 * nbSeeds doubles. Get from <see cref="CvtSeeds"/> for uniform-area cells.</param>
    public static RvdResult VoronoiPartition(
        MeshSnapshot mesh,
        IReadOnlyList<double> seedPointsXyz)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (seedPointsXyz == null) throw new ArgumentNullException(nameof(seedPointsXyz));
        if (seedPointsXyz.Count < 3 || seedPointsXyz.Count % 3 != 0)
            throw new ArgumentException("seedPointsXyz must be a non-empty flat array of 3-tuples.", nameof(seedPointsXyz));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        var seeds = ToArrayD(seedPointsXyz);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero, outIds = IntPtr.Zero;
        int outVc = 0, outTc = 0, outIdc = 0;

        var rc = Native.frahan_geogram_rvd_compute(
            v, v.Length / 3, t, t.Length / 3,
            seeds, seeds.Length / 3,
            out outV, out outVc,
            out outT, out outTc,
            out outIds, out outIdc);

        if (rc != 0)
        {
            var err = ReadLastError();
            if (outV   != IntPtr.Zero) Native.frahan_geogram_free_pdouble(outV);
            if (outT   != IntPtr.Zero) Native.frahan_geogram_free_pint(outT);
            if (outIds != IntPtr.Zero) Native.frahan_geogram_free_pint(outIds);
            throw new InvalidOperationException($"Geogram rvd_compute failed (rc={rc}): {err}");
        }

        var allVerts = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, allVerts, 0, outVc * 3);
        var allTris = new int[outTc * 3];
        if (outTc > 0) Marshal.Copy(outT, allTris, 0, outTc * 3);
        var seedIds = new int[outIdc];
        if (outIdc > 0) Marshal.Copy(outIds, seedIds, 0, outIdc);

        if (outV   != IntPtr.Zero) Native.frahan_geogram_free_pdouble(outV);
        if (outT   != IntPtr.Zero) Native.frahan_geogram_free_pint(outT);
        if (outIds != IntPtr.Zero) Native.frahan_geogram_free_pint(outIds);

        return new RvdResult(SplitBySeedId(allVerts, allTris, seedIds));
    }

    /// <summary>
    /// Same as <see cref="VoronoiPartition"/> but with an optional
    /// pre-RVD uniform remesh of the input. Use when the input mesh's
    /// triangulation is too coarse and cell-boundary cuts show as
    /// stair-step / sawtooth in the cell sub-meshes.
    /// </summary>
    /// <param name="remeshNbPoints">Target vertex count for the pre-RVD remesh. 0 = skip remeshing (then this matches <see cref="VoronoiPartition"/>). Typical 5_000..50_000.</param>
    /// <param name="remeshNbLloyd">Lloyd iterations for the pre-remesh. -1 = default (5).</param>
    /// <param name="remeshNbNewton">Newton-Lloyd iterations for the pre-remesh. -1 = default (30).</param>
    public static RvdResult VoronoiPartitionSmooth(
        MeshSnapshot mesh,
        IReadOnlyList<double> seedPointsXyz,
        int remeshNbPoints,
        int remeshNbLloyd = -1,
        int remeshNbNewton = -1)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (seedPointsXyz == null) throw new ArgumentNullException(nameof(seedPointsXyz));
        if (seedPointsXyz.Count < 3 || seedPointsXyz.Count % 3 != 0)
            throw new ArgumentException("seedPointsXyz must be a non-empty flat array of 3-tuples.", nameof(seedPointsXyz));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        var seeds = ToArrayD(seedPointsXyz);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero, outIds = IntPtr.Zero;
        int outVc = 0, outTc = 0, outIdc = 0;

        var rc = Native.frahan_geogram_rvd_compute_smooth(
            v, v.Length / 3, t, t.Length / 3,
            seeds, seeds.Length / 3,
            remeshNbPoints, remeshNbLloyd, remeshNbNewton,
            out outV, out outVc,
            out outT, out outTc,
            out outIds, out outIdc);

        return FinishRvd(rc, outV, outVc, outT, outTc, outIds, outIdc, "rvd_compute_smooth");
    }

    /// <summary>
    /// Volumetric Voronoi block decomposition - returns CLOSED
    /// polyhedral cells (the input solid sliced into Voronoi blocks).
    /// Each cell sub-mesh in the returned <see cref="RvdResult"/> is a
    /// watertight surface bounding one block.
    ///
    /// Requires the native shim to be built with FRAHAN_WITH_TETGEN=ON
    /// (the default since v0.2). When TetGen is off, this method
    /// throws <see cref="InvalidOperationException"/> with a clear
    /// message; the surface-only <see cref="VoronoiPartition"/> still
    /// works regardless.
    ///
    /// Seeds outside the input solid are silently dropped from the
    /// output cell set.
    /// </summary>
    public static RvdResult VoronoiBlocks(
        MeshSnapshot mesh,
        IReadOnlyList<double> seedPointsXyz)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (seedPointsXyz == null) throw new ArgumentNullException(nameof(seedPointsXyz));
        if (seedPointsXyz.Count < 3 || seedPointsXyz.Count % 3 != 0)
            throw new ArgumentException("seedPointsXyz must be a non-empty flat array of 3-tuples.", nameof(seedPointsXyz));
        if (!IsAvailable) throw new NotSupportedException("frahan_geogram shim not loaded.");

        var v = mesh.VertexCoordsXyz is double[] vd ? vd : ToArrayD(mesh.VertexCoordsXyz);
        var t = mesh.TriangleIndices is int[] ti ? ti : ToArrayI(mesh.TriangleIndices);
        var seeds = ToArrayD(seedPointsXyz);
        IntPtr outV = IntPtr.Zero, outT = IntPtr.Zero, outIds = IntPtr.Zero;
        int outVc = 0, outTc = 0, outIdc = 0;

        var rc = Native.frahan_geogram_voronoi_blocks_compute(
            v, v.Length / 3, t, t.Length / 3,
            seeds, seeds.Length / 3,
            out outV, out outVc,
            out outT, out outTc,
            out outIds, out outIdc);

        return FinishRvd(rc, outV, outVc, outT, outTc, outIds, outIdc, "voronoi_blocks_compute");
    }

    private static RvdResult FinishRvd(
        int rc,
        IntPtr outV, int outVc,
        IntPtr outT, int outTc,
        IntPtr outIds, int outIdc,
        string call)
    {
        if (rc != 0)
        {
            var err = ReadLastError();
            if (outV   != IntPtr.Zero) Native.frahan_geogram_free_pdouble(outV);
            if (outT   != IntPtr.Zero) Native.frahan_geogram_free_pint(outT);
            if (outIds != IntPtr.Zero) Native.frahan_geogram_free_pint(outIds);
            throw new InvalidOperationException($"Geogram {call} failed (rc={rc}): {err}");
        }

        var allVerts = new double[outVc * 3];
        if (outVc > 0) Marshal.Copy(outV, allVerts, 0, outVc * 3);
        var allTris = new int[outTc * 3];
        if (outTc > 0) Marshal.Copy(outT, allTris, 0, outTc * 3);
        var seedIds = new int[outIdc];
        if (outIdc > 0) Marshal.Copy(outIds, seedIds, 0, outIdc);

        if (outV   != IntPtr.Zero) Native.frahan_geogram_free_pdouble(outV);
        if (outT   != IntPtr.Zero) Native.frahan_geogram_free_pint(outT);
        if (outIds != IntPtr.Zero) Native.frahan_geogram_free_pint(outIds);

        return new RvdResult(SplitBySeedId(allVerts, allTris, seedIds));
    }

    private static IReadOnlyList<MeshSnapshot> SplitBySeedId(
        double[] allVerts, int[] allTris, int[] seedIds)
    {
        // Group facets by seed_id, then for each group build a sub-mesh
        // with re-indexed vertices (only the verts used by this group).
        if (seedIds.Length == 0) return Array.Empty<MeshSnapshot>();

        int maxId = -1;
        for (int i = 0; i < seedIds.Length; i++) if (seedIds[i] > maxId) maxId = seedIds[i];
        var nCells = maxId + 1;

        var cellTris = new List<int>[nCells];
        for (int i = 0; i < nCells; i++) cellTris[i] = new List<int>();
        for (int f = 0; f < seedIds.Length; f++)
        {
            int sid = seedIds[f];
            if (sid < 0 || sid >= nCells) continue;
            cellTris[sid].Add(allTris[3 * f + 0]);
            cellTris[sid].Add(allTris[3 * f + 1]);
            cellTris[sid].Add(allTris[3 * f + 2]);
        }

        var cells = new List<MeshSnapshot>(nCells);
        for (int c = 0; c < nCells; c++)
        {
            var globalIdxs = cellTris[c];
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
                    localVerts.Add(allVerts[3 * g + 0]);
                    localVerts.Add(allVerts[3 * g + 1]);
                    localVerts.Add(allVerts[3 * g + 2]);
                }
                localTris.Add(lv);
            }
            cells.Add(new MeshSnapshot(localVerts.ToArray(), localTris.ToArray()));
        }
        return cells;
    }

    // -- helpers --------------------------------------------------------------

    private static string ReadLastError()
    {
        try { return Marshal.PtrToStringAnsi(Native.frahan_geogram_last_error()) ?? "(none)"; }
        catch { return "(could not read error)"; }
    }

    private static void FreeAll(IntPtr v, IntPtr t)
    {
        if (v != IntPtr.Zero) Native.frahan_geogram_free_pdouble(v);
        if (t != IntPtr.Zero) Native.frahan_geogram_free_pint(t);
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

    private static class Native
    {
        // Loader resolves "frahan_geogram" -> "frahan_geogram.dll" on Windows,
        // "libfrahan_geogram.so" on Linux, "libfrahan_geogram.dylib" on macOS.
        private const string Dll = "frahan_geogram";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_geogram_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr frahan_geogram_last_error();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_decimate_mesh(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            int nbBins,
            int modeFlags,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTris,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_repair_mesh(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            int modeFlags, double colocateEpsilon,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTris,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_fill_holes(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            double maxHoleArea, int maxHoleEdges, int repairAfter,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTris,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_obb_3d(
            [In] double[] verts, int vcount,
            [In] int[] tris, int tcount,
            [Out] double[] outObb);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_remesh_uniform(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            int nbPoints, int nbLloyd, int nbNewton,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTris,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_tetrahedralize(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            int preprocess, int refine, double quality, int keepRegions,
            out IntPtr outVerts, out int outVcount,
            out IntPtr outTets,  out int outTcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_cvt_compute(
            [In] double[] verts, int vcount,
            [In] int[]    tris,  int tcount,
            int nbPoints, int nbLloyd, int nbNewton,
            out IntPtr outPoints, out int outPcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_rvd_compute(
            [In] double[] meshVerts, int meshVcount,
            [In] int[]    meshTris,  int meshTcount,
            [In] double[] seedPoints, int seedCount,
            out IntPtr outVerts,    out int outVcount,
            out IntPtr outTris,     out int outTcount,
            out IntPtr outSeedIds,  out int outIdcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_rvd_compute_smooth(
            [In] double[] meshVerts, int meshVcount,
            [In] int[]    meshTris,  int meshTcount,
            [In] double[] seedPoints, int seedCount,
            int remeshNbPoints, int remeshNbLloyd, int remeshNbNewton,
            out IntPtr outVerts,    out int outVcount,
            out IntPtr outTris,     out int outTcount,
            out IntPtr outSeedIds,  out int outIdcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int frahan_geogram_voronoi_blocks_compute(
            [In] double[] meshVerts, int meshVcount,
            [In] int[]    meshTris,  int meshTcount,
            [In] double[] seedPoints, int seedCount,
            out IntPtr outVerts,    out int outVcount,
            out IntPtr outTris,     out int outTcount,
            out IntPtr outSeedIds,  out int outIdcount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_geogram_free_pdouble(IntPtr p);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void frahan_geogram_free_pint(IntPtr p);
    }
}
