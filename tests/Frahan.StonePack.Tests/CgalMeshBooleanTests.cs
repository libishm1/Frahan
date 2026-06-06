#nullable disable
using System;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// CgalMeshBooleanTests — verifies the managed front-end's behaviour both
// when the native shim is absent (fallback to BSP CSG) and the contract
// it advertises. When the shim is built and on the loader path, the same
// tests run against CGAL directly.
// =============================================================================

static class CgalMeshBooleanTests
{
    public static void IsAvailable_DoesNotThrow_WhenDllAbsent()
    {
        // Probing must not throw even when the DLL isn't on the path.
        // It returns false silently and caches the result.
        bool _ = CgalMeshBoolean.IsAvailable;
        // Calling again must return the same value without re-probing.
        Assert(CgalMeshBoolean.IsAvailable == _,
            "IsAvailable cache mismatch on second call");
    }

    public static void Version_NonNull_RegardlessOfAvailability()
    {
        var v = CgalMeshBoolean.Version;
        Assert(v != null, "Version must not be null");
        // When DLL absent, version is empty string; when present, non-empty.
        if (CgalMeshBoolean.IsAvailable)
            Assert(v.Length > 0, "Version must be populated when CGAL is loaded");
    }

    public static void Union_FallbackMatchesBspWhenDllAbsent()
    {
        var a = UnitCube(0, 0, 0);
        var b = UnitCube(0.5, 0.5, 0.5);
        var result = CgalMeshBoolean.Union(a, b, out var backend);
        // When the native shim is absent (typical CI / dev environment),
        // the wrapper falls back to the BSP CSG and produces the same
        // volume as MeshCsg.Union directly.
        var rep = MeshSanitizer.Analyse(result);
        Assert(Math.Abs(rep.Volume - 1.875) < 1e-2,
            $"union volume expected 1.875, got {rep.Volume} (backend={backend})");
        // backend is whichever ran — both are valid, asserting the tag is
        // a known enum value is enough.
        Assert(backend == CsgBackend.ManagedBsp || backend == CsgBackend.Cgal,
            $"unexpected backend tag {backend}");
    }

    public static void Intersection_FallbackProducesCorrectVolume()
    {
        var a = UnitCube(0, 0, 0);
        var b = UnitCube(0.5, 0.5, 0.5);
        var result = CgalMeshBoolean.Intersection(a, b);
        var rep = MeshSanitizer.Analyse(result);
        Assert(Math.Abs(rep.Volume - 0.125) < 1e-3,
            $"intersection volume expected 0.125, got {rep.Volume}");
    }

    public static void Difference_FallbackProducesCorrectVolume()
    {
        var a = UnitCube(0, 0, 0);
        var b = UnitCube(0.5, 0.5, 0.5);
        var result = CgalMeshBoolean.Difference(a, b);
        var rep = MeshSanitizer.Analyse(result);
        Assert(Math.Abs(rep.Volume - 0.875) < 1e-2,
            $"difference volume expected 0.875, got {rep.Volume}");
    }

    public static void NullArgs_Throw()
    {
        bool t1 = false, t2 = false, t3 = false;
        try { _ = CgalMeshBoolean.Union(null, UnitCube(0, 0, 0)); }
        catch (ArgumentNullException) { t1 = true; }
        try { _ = CgalMeshBoolean.Intersection(UnitCube(0, 0, 0), null); }
        catch (ArgumentNullException) { t2 = true; }
        try { _ = CgalMeshBoolean.Difference(null, null); }
        catch (ArgumentNullException) { t3 = true; }
        Assert(t1 && t2 && t3, "null args must throw");
    }

    private static MeshSnapshot UnitCube(double x0, double y0, double z0)
    {
        var verts = new double[]
        {
            x0,     y0,     z0,
            x0 + 1, y0,     z0,
            x0 + 1, y0 + 1, z0,
            x0,     y0 + 1, z0,
            x0,     y0,     z0 + 1,
            x0 + 1, y0,     z0 + 1,
            x0 + 1, y0 + 1, z0 + 1,
            x0,     y0 + 1, z0 + 1,
        };
        var tris = new int[]
        {
            0, 3, 2,  0, 2, 1,
            4, 5, 6,  4, 6, 7,
            0, 1, 5,  0, 5, 4,
            1, 2, 6,  1, 6, 5,
            2, 3, 7,  2, 7, 6,
            3, 0, 4,  3, 4, 7,
        };
        return new MeshSnapshot(verts, tris);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
