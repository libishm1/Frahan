#nullable disable
using System;
using Frahan.GH.Masonry;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// MeshSanitizerTests — Phase 1 of robustness pass.
// =============================================================================

static class MeshSanitizerTests
{
    public static void Analyse_UnitCube_IsCleanSolid()
    {
        var snap = UnitCube();
        var r = MeshSanitizer.Analyse(snap);
        Assert(r.VertexCount == 8, $"V {r.VertexCount}");
        Assert(r.TriangleCount == 12, $"T {r.TriangleCount}");
        Assert(r.IsManifold, "manifold");
        Assert(r.IsClosed, "closed");
        Assert(r.HasConsistentNormals, "consistent normals");
        Assert(r.DuplicateVertexCount == 0, $"dupV {r.DuplicateVertexCount}");
        Assert(r.DegenerateTriangleCount == 0, $"degenT {r.DegenerateTriangleCount}");
        Assert(r.BoundaryEdgeCount == 0, $"boundary {r.BoundaryEdgeCount}");
        Assert(r.NonManifoldEdgeCount == 0, $"nonManifold {r.NonManifoldEdgeCount}");
        Assert(Math.Abs(r.SurfaceArea - 6.0) < 1e-9, $"area {r.SurfaceArea}");
        Assert(Math.Abs(Math.Abs(r.SignedVolume) - 1.0) < 1e-9, $"|vol| {r.Volume}");
        Assert(r.IsCleanSolid, "clean solid");
    }

    public static void Analyse_OpenCube_NotClosed()
    {
        // 5 faces of a cube — top removed.
        var verts = new double[]
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0,
            0,0,1, 1,0,1, 1,1,1, 0,1,1,
        };
        var tris = new int[]
        {
            0,3,2, 0,2,1,        // -Z
            0,1,5, 0,5,4,        // -Y
            1,2,6, 1,6,5,        // +X
            2,3,7, 2,7,6,        // +Y
            3,0,4, 3,4,7,        // -X
            // top omitted
        };
        var snap = new MeshSnapshot(verts, tris);
        var r = MeshSanitizer.Analyse(snap);
        Assert(r.IsManifold, "open cube must be manifold");
        Assert(!r.IsClosed, "open cube must not be closed");
        Assert(r.BoundaryEdgeCount == 4, $"boundary edges {r.BoundaryEdgeCount}");
        Assert(!r.IsCleanSolid, "open cube is not clean solid");
    }

    public static void Analyse_DuplicateVertices_Counted()
    {
        // Two coincident vertices and one extra triangle using the duplicate.
        var verts = new double[]
        {
            0,0,0, 1,0,0, 0,1,0, 0,0,0,    // v3 == v0
        };
        var tris = new int[] { 0, 1, 2, 3, 1, 2 };
        var snap = new MeshSnapshot(verts, tris);
        var r = MeshSanitizer.Analyse(snap);
        Assert(r.DuplicateVertexCount == 1, $"expected 1 dup, got {r.DuplicateVertexCount}");
    }

    public static void Analyse_DegenerateTriangle_Counted()
    {
        // A triangle with zero area (two coincident corners).
        var verts = new double[] { 0,0,0, 1,0,0, 1,0,0 };
        var tris = new int[] { 0, 1, 2 };
        var snap = new MeshSnapshot(verts, tris);
        var r = MeshSanitizer.Analyse(snap);
        Assert(r.DegenerateTriangleCount == 1, $"expected 1 degen, got {r.DegenerateTriangleCount}");
    }

    public static void Analyse_FlippedTriangle_NormalInconsistent()
    {
        // Two triangles forming a square, one with its winding reversed.
        var verts = new double[] { 0,0,0, 1,0,0, 1,1,0, 0,1,0 };
        var tris = new int[]
        {
            0, 1, 2,
            // Should be (0, 2, 3) for consistent CCW. Use (3, 2, 0) instead — same
            // winding around the shared edge (0, 2) → flip detected.
            3, 2, 0,
        };
        var snap = new MeshSnapshot(verts, tris);
        var r = MeshSanitizer.Analyse(snap);
        Assert(!r.HasConsistentNormals,
            "flipped triangle must produce normal inconsistency");
        Assert(r.NormalInconsistencyCount >= 1,
            $"normInconsist {r.NormalInconsistencyCount}");
    }

    // ─── Sanitize ───────────────────────────────────────────────────────

    public static void Sanitize_DedupVertices_MergesAndRemapsTriangles()
    {
        // 6 vertices but two pairs are duplicates → collapses to 4.
        var verts = new double[]
        {
            0,0,0, 1,0,0, 0,1,0,
            0,0,0,             // dup of v0
            1,0,0,             // dup of v1
            1,1,0,
        };
        var tris = new int[]
        {
            0, 1, 2,
            3, 4, 5,           // uses dups
        };
        var snap = new MeshSnapshot(verts, tris);
        var opts = new SanitizeOptions { DedupVertices = true, DedupTolerance = 1e-9 };
        var res = MeshSanitizer.Sanitize(snap, opts);
        Assert(res.VerticesMerged == 2, $"expected 2 merged, got {res.VerticesMerged}");
        Assert(res.Mesh.VertexCount == 4, $"after V {res.Mesh.VertexCount}");
        Assert(res.Mesh.TriangleCount == 2, $"after T {res.Mesh.TriangleCount}");
    }

    public static void Sanitize_DropDegenerate_RemovesZeroAreaTris()
    {
        var verts = new double[]
        {
            0,0,0, 1,0,0, 0,1,0, 1,1,0,
        };
        var tris = new int[]
        {
            0, 1, 2,
            0, 1, 1,           // degenerate (repeated index)
            1, 2, 3,
        };
        var snap = new MeshSnapshot(verts, tris);
        var opts = new SanitizeOptions { DropDegenerate = true };
        var res = MeshSanitizer.Sanitize(snap, opts);
        Assert(res.TrianglesDropped == 1, $"dropped {res.TrianglesDropped}");
        Assert(res.Mesh.TriangleCount == 2, $"remaining {res.Mesh.TriangleCount}");
    }

    public static void Sanitize_UnifyNormals_FlipsToConsistent()
    {
        var verts = new double[] { 0,0,0, 1,0,0, 1,1,0, 0,1,0 };
        var tris = new int[]
        {
            0, 1, 2,
            3, 2, 0,           // wrong winding around shared edge (0, 2)
        };
        var snap = new MeshSnapshot(verts, tris);
        var pre = MeshSanitizer.Analyse(snap);
        Assert(!pre.HasConsistentNormals, "input must be inconsistent");
        var opts = new SanitizeOptions { UnifyNormals = true };
        var res = MeshSanitizer.Sanitize(snap, opts);
        Assert(res.TrianglesFlipped >= 1, $"flipped {res.TrianglesFlipped}");
        Assert(res.After.HasConsistentNormals,
            "after sanitize, normals must be consistent");
    }

    // ─── GH metadata ────────────────────────────────────────────────────

    public static void Gh_MeshQualityReportComponent_Metadata()
    {
        var c = new MeshQualityReportComponent();
        Assert(c.ComponentGuid == new Guid("9ABCDEF0-1234-5678-9ABC-DEF012345678"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 12, $"Output count {c.Params.Output.Count}");
    }

    // ─── helpers ────────────────────────────────────────────────────────

    private static MeshSnapshot UnitCube()
    {
        var verts = new double[]
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0,
            0,0,1, 1,0,1, 1,1,1, 0,1,1,
        };
        var tris = new int[]
        {
            0, 3, 2,  0, 2, 1,    // -Z
            4, 5, 6,  4, 6, 7,    // +Z
            0, 1, 5,  0, 5, 4,    // -Y
            1, 2, 6,  1, 6, 5,    // +X
            2, 3, 7,  2, 7, 6,    // +Y
            3, 0, 4,  3, 4, 7,    // -X
        };
        return new MeshSnapshot(verts, tris);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
