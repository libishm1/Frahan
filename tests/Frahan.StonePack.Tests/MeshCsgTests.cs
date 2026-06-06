#nullable disable
using System;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// MeshCsgTests — 3D mesh-mesh CSG via BSP trees. Pure-managed.
// =============================================================================

static class MeshCsgTests
{
    public static void Union_DisjointCubes_PreservesBothVolumes()
    {
        var a = MeshCsg.FromMesh(UnitCube(0, 0, 0));
        var b = MeshCsg.FromMesh(UnitCube(10, 10, 10));
        var u = MeshCsg.Union(a, b);
        var mesh = u.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(Math.Abs(rep.Volume - 2.0) < 1e-3,
            $"disjoint union volume expected ~2, got {rep.Volume}");
    }

    public static void Union_OverlappingCubes_VolumeMatchesInclusionExclusion()
    {
        // Two unit cubes overlapping in a 0.5 × 0.5 × 0.5 region.
        // Inclusion-exclusion: |A| + |B| - |A∩B| = 1 + 1 - 0.125 = 1.875.
        var a = MeshCsg.FromMesh(UnitCube(0, 0, 0));
        var b = MeshCsg.FromMesh(UnitCube(0.5, 0.5, 0.5));
        var u = MeshCsg.Union(a, b);
        var mesh = u.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(Math.Abs(rep.Volume - 1.875) < 1e-2,
            $"union volume expected 1.875, got {rep.Volume}");
    }

    public static void Intersection_OverlappingCubes_ReturnsOverlap()
    {
        var a = MeshCsg.FromMesh(UnitCube(0, 0, 0));
        var b = MeshCsg.FromMesh(UnitCube(0.5, 0.5, 0.5));
        var i = MeshCsg.Intersection(a, b);
        var mesh = i.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(Math.Abs(rep.Volume - 0.125) < 1e-3,
            $"intersection volume expected 0.125, got {rep.Volume}");
    }

    public static void Intersection_DisjointCubes_ReturnsEmpty()
    {
        var a = MeshCsg.FromMesh(UnitCube(0, 0, 0));
        var b = MeshCsg.FromMesh(UnitCube(10, 10, 10));
        var i = MeshCsg.Intersection(a, b);
        var mesh = i.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(rep.Volume < 1e-3,
            $"disjoint intersection volume expected ~0, got {rep.Volume}");
    }

    public static void Difference_OverlappingCubes_ReturnsAMinusB()
    {
        // A \ B = |A| - |A∩B| = 1 - 0.125 = 0.875.
        var a = MeshCsg.FromMesh(UnitCube(0, 0, 0));
        var b = MeshCsg.FromMesh(UnitCube(0.5, 0.5, 0.5));
        var d = MeshCsg.Difference(a, b);
        var mesh = d.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(Math.Abs(rep.Volume - 0.875) < 1e-2,
            $"diff volume expected 0.875, got {rep.Volume}");
    }

    public static void Difference_BFullyInsideA_ReturnsAWithCavity()
    {
        // 2× cube minus 1× cube fully contained → volume = 8 - 1 = 7.
        var a = MeshCsg.FromMesh(Cube(-1, -1, -1, 1, 1, 1));   // 2 × 2 × 2 = 8
        var b = MeshCsg.FromMesh(Cube(-0.5, -0.5, -0.5, 0.5, 0.5, 0.5));  // 1
        var d = MeshCsg.Difference(a, b);
        var mesh = d.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(Math.Abs(rep.Volume - 7.0) < 1e-2,
            $"big-minus-inner expected 7, got {rep.Volume}");
    }

    public static void Difference_OnlySubject_ReturnsAUnchanged()
    {
        var a = MeshCsg.FromMesh(UnitCube(0, 0, 0));
        var b = MeshCsg.FromMesh(UnitCube(50, 50, 50));
        var d = MeshCsg.Difference(a, b);
        var mesh = d.ToMesh();
        var rep = MeshSanitizer.Analyse(mesh);
        Assert(Math.Abs(rep.Volume - 1.0) < 1e-3,
            $"disjoint diff volume expected 1, got {rep.Volume}");
    }

    public static void Csg_NullArgs_Throw()
    {
        bool t1 = false, t2 = false, t3 = false;
        try { _ = MeshCsg.Union(null, null); }
        catch (ArgumentNullException) { t1 = true; }
        try { _ = MeshCsg.Intersection(null, MeshCsg.FromMesh(UnitCube(0, 0, 0))); }
        catch (ArgumentNullException) { t2 = true; }
        try { _ = MeshCsg.Difference(MeshCsg.FromMesh(UnitCube(0, 0, 0)), null); }
        catch (ArgumentNullException) { t3 = true; }
        Assert(t1 && t2 && t3, "null args must throw");
    }

    // ─── helpers ────────────────────────────────────────────────────────

    private static MeshSnapshot UnitCube(double x0, double y0, double z0) =>
        Cube(x0, y0, z0, x0 + 1, y0 + 1, z0 + 1);

    private static MeshSnapshot Cube(
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        var verts = new double[]
        {
            minX, minY, minZ,
            maxX, minY, minZ,
            maxX, maxY, minZ,
            minX, maxY, minZ,
            minX, minY, maxZ,
            maxX, minY, maxZ,
            maxX, maxY, maxZ,
            minX, maxY, maxZ,
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
