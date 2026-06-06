#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// MeshOps - backend selector facade. Picks the best available native
// shim per operation, falls back to the next, throws if none.
//
// Selection policy (per the wiki + license footprint):
//   Repair    : Geogram > CGAL              (Geogram is BSD-3, faster)
//   Decimate  : Geogram > CGAL              (Geogram vertex-clustering by default)
//   OBB       : Geogram > CGAL              (Geogram no Eigen dep)
//   Remesh    : Geogram only                (CGAL doesn't have it)
//   Boolean   : CGAL only                    (Geogram doesn't have CSG)
//   Voronoi   : Geogram only                (CGAL doesn't have 3D RVD)
//   Skeleton  : CGAL only                   (Geogram doesn't have it)
//
// Existing Cgal* / Geogram* explicit entry points stay public for callers
// who want to force a specific backend. Use MeshOps when you don't care.
// =============================================================================

/// <summary>Which backend ran an operation, returned via <c>out</c> for diagnostics.</summary>
public enum MeshBackend
{
    None    = 0,
    Geogram = 1,
    Cgal    = 2,
}

public static class MeshOps
{
    /// <summary>True iff at least one backend (Geogram or CGAL) is loadable.</summary>
    public static bool IsAvailable => GeogramMesh.IsAvailable || CgalMeshBoolean.IsAvailable;

    /// <summary>Lists which native shims are loaded right now. Useful for diagnostic GH outputs.</summary>
    public static string Diagnostics
    {
        get
        {
            var g = GeogramMesh.IsAvailable ? GeogramMesh.Version : "(not loaded)";
            var c = CgalMeshBoolean.IsAvailable ? CgalMeshBoolean.Version : "(not loaded)";
            return $"Geogram: {g}\nCGAL:    {c}";
        }
    }

    // -- Repair --------------------------------------------------------------

    /// <summary>
    /// Repair a mesh using whichever backend is available, Geogram first.
    /// Sensible defaults: triangulate + colocate + dedup-facets.
    /// </summary>
    public static MeshSnapshot Repair(MeshSnapshot mesh, out MeshBackend backend)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (GeogramMesh.IsAvailable)
        {
            backend = MeshBackend.Geogram;
            return GeogramMesh.RepairMesh(mesh, GeogramRepairMode.Default, 0.0);
        }
        if (CgalMeshBoolean.IsAvailable)
        {
            backend = MeshBackend.Cgal;
            return CgalGeometry.RepairMesh(mesh);
        }
        backend = MeshBackend.None;
        throw new NotSupportedException(
            "Neither Geogram nor CGAL native shim is loaded. Build at least one " +
            "from native/{geogram,cgal}_shim/.");
    }

    public static MeshSnapshot Repair(MeshSnapshot mesh) => Repair(mesh, out _);

    // -- Decimate ------------------------------------------------------------

    /// <summary>
    /// Decimate a mesh. Geogram default uses vertex clustering with
    /// <paramref name="targetRatio"/> mapped to a sensible bin count
    /// (higher ratio = more bins = more detail). CGAL fallback uses
    /// edge-collapse with the same ratio target.
    /// </summary>
    /// <param name="targetRatio">In (0, 1). 0.5 keeps ~half the detail.</param>
    public static MeshSnapshot Decimate(
        MeshSnapshot mesh, double targetRatio, out MeshBackend backend)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (targetRatio <= 0.0 || targetRatio >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(targetRatio), "targetRatio must be in (0, 1).");

        if (GeogramMesh.IsAvailable)
        {
            // Map targetRatio in (0,1) to nb_bins in [30..300]. Higher
            // ratio = more detail = more bins. Geogram's voxel grid
            // bin count is the primary detail knob.
            int bins = Math.Max(30, Math.Min(300, (int)Math.Round(30 + targetRatio * 270)));
            backend = MeshBackend.Geogram;
            return GeogramMesh.DecimateMesh(mesh, bins, GeogramDecimateMode.Default);
        }
        if (CgalMeshBoolean.IsAvailable)
        {
            backend = MeshBackend.Cgal;
            return CgalGeometry.DecimateMesh(mesh, DecimateStopKind.CountRatio, targetRatio);
        }
        backend = MeshBackend.None;
        throw new NotSupportedException("Neither Geogram nor CGAL native shim is loaded.");
    }

    public static MeshSnapshot Decimate(MeshSnapshot mesh, double targetRatio)
        => Decimate(mesh, targetRatio, out _);

    // -- OBB -----------------------------------------------------------------

    /// <summary>
    /// Compute an oriented bounding box. Geogram preferred (no Eigen
    /// dependency, faster); CGAL fallback if Geogram absent and the
    /// CGAL shim was built with Eigen.
    /// </summary>
    public static ObbResult OrientedBoundingBox(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndicesOrNull,
        out MeshBackend backend)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (GeogramMesh.IsAvailable)
        {
            backend = MeshBackend.Geogram;
            return GeogramMesh.OrientedBoundingBox(vertexCoordsXyz, triangleIndicesOrNull);
        }
        if (CgalMeshBoolean.IsAvailable && CgalGeometry.IsObbAvailable)
        {
            backend = MeshBackend.Cgal;
            return CgalGeometry.OrientedBoundingBox(vertexCoordsXyz, triangleIndicesOrNull);
        }
        backend = MeshBackend.None;
        throw new NotSupportedException(
            "OBB requires Geogram (any build) or CGAL with Eigen. " +
            "Neither is available right now.");
    }

    public static ObbResult OrientedBoundingBox(MeshSnapshot mesh, out MeshBackend backend)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        return OrientedBoundingBox(mesh.VertexCoordsXyz, mesh.TriangleIndices, out backend);
    }

    public static ObbResult OrientedBoundingBox(MeshSnapshot mesh)
        => OrientedBoundingBox(mesh, out _);
}
