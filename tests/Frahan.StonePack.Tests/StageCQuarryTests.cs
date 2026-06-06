#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry;

namespace Frahan.Tests;

// =============================================================================
// StageCQuarryTests — MeshShellSplitter + ConvexHullBuilder.
// =============================================================================

static class StageCQuarryTests
{
    // ─── MeshShellSplitter ───────────────────────────────────────────────────

    public static void Shell_OneClosedTetrahedron_ReturnsOneShell()
    {
        // Single tetrahedron, 4 verts, 4 faces.
        var verts = new double[] { 0,0,0,  1,0,0,  0,1,0,  0,0,1 };
        var tris = new int[] { 0,2,1,  0,1,3,  1,2,3,  2,0,3 };
        var shells = MeshShellSplitter.Split(verts, tris);
        Assert(shells.Count == 1, $"expected 1 shell, got {shells.Count}");
        Assert(shells[0].VertexCount == 4, $"expected 4 verts, got {shells[0].VertexCount}");
        Assert(shells[0].TriangleCount == 4, $"expected 4 tris, got {shells[0].TriangleCount}");
    }

    public static void Shell_TwoSeparatedTetrahedra_ReturnsTwoShells()
    {
        // Two tetrahedra in separate regions, no shared vertices.
        var verts = new double[]
        {
            0,0,0, 1,0,0, 0,1,0, 0,0,1,            // tetra 1
            10,0,0, 11,0,0, 10,1,0, 10,0,1,        // tetra 2
        };
        var tris = new int[]
        {
            0,2,1, 0,1,3, 1,2,3, 2,0,3,
            4,6,5, 4,5,7, 5,6,7, 6,4,7,
        };
        var shells = MeshShellSplitter.Split(verts, tris);
        Assert(shells.Count == 2, $"expected 2 shells, got {shells.Count}");
        Assert(shells[0].VertexCount == 4 && shells[0].TriangleCount == 4,
            $"shell 0: V={shells[0].VertexCount} T={shells[0].TriangleCount}");
        Assert(shells[1].VertexCount == 4 && shells[1].TriangleCount == 4,
            $"shell 1: V={shells[1].VertexCount} T={shells[1].TriangleCount}");
    }

    public static void Shell_EmptyTriangles_ReturnsEmpty()
    {
        var verts = new double[] { 0,0,0, 1,0,0 };
        var tris = new int[0];
        var shells = MeshShellSplitter.Split(verts, tris);
        Assert(shells.Count == 0, $"expected 0 shells for empty triangles, got {shells.Count}");
    }

    public static void Shell_BadIndex_Throws()
    {
        var verts = new double[] { 0,0,0, 1,0,0, 0,1,0 };
        var tris = new int[] { 0, 1, 5 };  // index 5 out of range
        bool threw = false;
        try { MeshShellSplitter.Split(verts, tris); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "out-of-range triangle index should throw");
    }

    // ─── ConvexHullBuilder ───────────────────────────────────────────────────

    public static void Hull_UnitCube8Vertices_ReturnsClosedHull()
    {
        var pts = new double[]
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0,
            0,0,1, 1,0,1, 1,1,1, 0,1,1,
        };
        var slab = ConvexHullBuilder.BuildSlab(pts);
        Assert(slab.VertexCount == 8, $"expected 8 hull verts, got {slab.VertexCount}");
        Assert(slab.FaceCount >= 12 && slab.FaceCount <= 12,
            $"expected 12 triangle faces (6 quads * 2), got {slab.FaceCount}");
        // Volume should be ~1.0.
        double vol = slab.SignedVolume();
        Assert(Math.Abs(vol - 1.0) < 1e-6,
            $"unit cube hull volume expected ~1.0, got {vol}");
    }

    public static void Hull_NonConvexInput_BoundsConvexShape()
    {
        // Cube vertices + an interior point that should be excluded by the hull.
        var pts = new double[]
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0,
            0,0,1, 1,0,1, 1,1,1, 0,1,1,
            0.5, 0.5, 0.5,            // interior
        };
        var slab = ConvexHullBuilder.BuildSlab(pts);
        // Hull should be the cube; volume ~1.0 regardless of the interior point.
        double vol = slab.SignedVolume();
        Assert(Math.Abs(vol - 1.0) < 1e-6,
            $"hull volume with interior point expected ~1.0, got {vol}");
    }

    public static void Hull_Coplanar_Throws()
    {
        // 4 coplanar points (z=0) → no 3D hull.
        var pts = new double[]
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0,
        };
        bool threw = false;
        try { _ = ConvexHullBuilder.BuildSlab(pts); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "coplanar input should throw ArgumentException");
    }

    public static void Hull_Collinear_Throws()
    {
        var pts = new double[]
        {
            0,0,0, 1,0,0, 2,0,0, 3,0,0,
        };
        bool threw = false;
        try { _ = ConvexHullBuilder.BuildSlab(pts); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "collinear input should throw ArgumentException");
    }

    public static void Hull_TooFewPoints_Throws()
    {
        var pts = new double[] { 0,0,0, 1,0,0, 0,1,0 };
        bool threw = false;
        try { _ = ConvexHullBuilder.BuildSlab(pts); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "fewer than 4 points should throw");
    }

    public static void Hull_NullPoints_Throws()
    {
        bool threw = false;
        try { _ = ConvexHullBuilder.BuildSlab(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null points should throw ArgumentNullException");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
