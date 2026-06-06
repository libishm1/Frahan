#nullable disable
using System;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// MeshQualityReport — diagnostic snapshot used by every robustness-sensitive
// algorithm in the masonry pipeline (contact detection, packing, cutting,
// quarry decomposition). Algorithms read this report once and decide whether
// to refuse, auto-repair, or just adapt their tolerances.
//
// Computed by MeshSanitizer.Analyze. Cheap enough to run on every input
// (single pass over triangles + edge map). For very large meshes (> 1M
// triangles) the edge map dominates; the report struct itself is tiny.
// =============================================================================

/// <summary>
/// Mesh quality + topology snapshot. Immutable.
/// </summary>
public sealed class MeshQualityReport
{
    public MeshQualityReport(
        int vertexCount,
        int triangleCount,
        int duplicateVertexCount,
        int degenerateTriangleCount,
        int boundaryEdgeCount,
        int nonManifoldEdgeCount,
        int normalInconsistencyCount,
        bool isManifold,
        bool isClosed,
        bool hasConsistentNormals,
        double minEdgeLength,
        double maxEdgeLength,
        double meanEdgeLength,
        double medianEdgeLength,
        double surfaceArea,
        double signedVolume)
    {
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
        DuplicateVertexCount = duplicateVertexCount;
        DegenerateTriangleCount = degenerateTriangleCount;
        BoundaryEdgeCount = boundaryEdgeCount;
        NonManifoldEdgeCount = nonManifoldEdgeCount;
        NormalInconsistencyCount = normalInconsistencyCount;
        IsManifold = isManifold;
        IsClosed = isClosed;
        HasConsistentNormals = hasConsistentNormals;
        MinEdgeLength = minEdgeLength;
        MaxEdgeLength = maxEdgeLength;
        MeanEdgeLength = meanEdgeLength;
        MedianEdgeLength = medianEdgeLength;
        SurfaceArea = surfaceArea;
        SignedVolume = signedVolume;
    }

    public int VertexCount { get; }
    public int TriangleCount { get; }

    /// <summary>Pairs of vertices whose XYZ are within the dedup tolerance.</summary>
    public int DuplicateVertexCount { get; }

    /// <summary>Triangles whose computed area is below the degeneracy threshold.</summary>
    public int DegenerateTriangleCount { get; }

    /// <summary>Edges incident to exactly one triangle (open boundaries).</summary>
    public int BoundaryEdgeCount { get; }

    /// <summary>Edges incident to three or more triangles (T-junctions, butterfly defects).</summary>
    public int NonManifoldEdgeCount { get; }

    /// <summary>Triangle pairs sharing an edge with the same winding direction (one is flipped).</summary>
    public int NormalInconsistencyCount { get; }

    /// <summary>True when every edge is incident to exactly 1 or 2 triangles.</summary>
    public bool IsManifold { get; }

    /// <summary>True when every edge is incident to exactly 2 triangles (no boundaries).</summary>
    public bool IsClosed { get; }

    /// <summary>True when no triangle pair shares an edge with the same winding.</summary>
    public bool HasConsistentNormals { get; }

    public double MinEdgeLength { get; }
    public double MaxEdgeLength { get; }
    public double MeanEdgeLength { get; }
    public double MedianEdgeLength { get; }

    /// <summary>Sum of triangle areas.</summary>
    public double SurfaceArea { get; }

    /// <summary>
    /// Signed volume via divergence theorem: V = (1/6) Σ (a · (b × c)).
    /// Only meaningful for closed, consistently-oriented meshes; positive
    /// when normals point outward, negative when they point inward.
    /// </summary>
    public double SignedVolume { get; }

    /// <summary>
    /// Convenience: |SignedVolume| — the volume regardless of orientation.
    /// </summary>
    public double Volume => Math.Abs(SignedVolume);

    /// <summary>
    /// True when the mesh is suitable for algorithms that assume a clean
    /// closed solid (contact detection, CSG, volume-based packing). Looser
    /// algorithms can tolerate <see cref="IsManifold"/> only.
    /// </summary>
    public bool IsCleanSolid =>
        IsClosed && IsManifold && HasConsistentNormals
        && DegenerateTriangleCount == 0
        && DuplicateVertexCount == 0;

    public override string ToString() =>
        $"MeshQuality(V={VertexCount}, T={TriangleCount}, " +
        $"manifold={IsManifold}, closed={IsClosed}, normals={HasConsistentNormals}, " +
        $"dupV={DuplicateVertexCount}, degenT={DegenerateTriangleCount}, " +
        $"boundary={BoundaryEdgeCount}, nonManifoldE={NonManifoldEdgeCount}, " +
        $"normInconsist={NormalInconsistencyCount}, " +
        $"edgeLen=[{MinEdgeLength:0.###e+00}, {MedianEdgeLength:0.###e+00}, {MaxEdgeLength:0.###e+00}], " +
        $"area={SurfaceArea:0.###}, vol={SignedVolume:0.###})";
}
