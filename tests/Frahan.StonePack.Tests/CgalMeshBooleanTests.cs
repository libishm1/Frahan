#nullable disable
using System;
using System.IO;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Frahan.Core.ScanIngest;

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

    // Native-path regression: trim a REAL ETH1100 rubble stone with native CGAL
    // and assert the output is a clean, watertight, consistently-oriented mesh
    // (unified normals). This is the check that was MISSING — every other CGAL
    // test exercises only the managed BSP fallback ("when DLL absent"), so a
    // broken native shim (e.g. the un-bundled mpfr-6.dll dependency that made
    // frahan_cgal.dll fail to load) stayed invisible behind a green suite.
    // Gates: loudly SKIPs if the native CGAL shim is not on the loader path
    // (set FRAHAN_ETH_DIR / bundle frahan_cgal+gmp+mpfr beside the test exe) or
    // the ETH1100 data set is absent (a gitignored dev-machine asset).
    public static void CgalTrim_EthStone_ProducesCleanUnifiedMesh()
    {
        if (!CgalMeshBoolean.IsAvailable)
            throw new SkipTest("native CGAL shim (frahan_cgal + gmp-10 + mpfr-6) not on the loader path");

        string obj = FindEthStone();
        if (obj == null)
            throw new SkipTest("ETH1100 data not present (set FRAHAN_ETH_DIR or Data/eth1100/closed/...)");

        var meshes = ObjMeshReader.ReadFile(obj);
        Assert(meshes.Count > 0, "ETH obj parsed to zero meshes");
        var stone = new MeshSnapshot(meshes[0].VertexCoordsXyz, meshes[0].TriangleIndices);

        // Sanity: an ETH 'closed' stone must itself be a clean, oriented, closed
        // manifold — otherwise the boolean result tells us nothing.
        var inRep = MeshSanitizer.Analyse(stone);
        Assert(inRep.IsClosed && inRep.HasConsistentNormals,
            $"input ETH stone not clean: manifold={inRep.IsManifold} closed={inRep.IsClosed} " +
            $"consistentNormals={inRep.HasConsistentNormals}");

        // Knife: a half-space above mid-Z, padded well past the XY extent, so the
        // Difference squares off the top of the stone — the rubble-trim case.
        Bounds(stone, out double[] lo, out double[] hi);
        double midZ = 0.5 * (lo[2] + hi[2]);
        double pad = 0.25 * Math.Max(hi[0] - lo[0], hi[1] - lo[1]) + 1e-3;
        var knife = Box(lo[0] - pad, lo[1] - pad, midZ, hi[0] + pad, hi[1] + pad, hi[2] + pad);

        var trimmed = CgalMeshBoolean.Difference(stone, knife, out var backend);
        if (backend != CsgBackend.Cgal)
            throw new SkipTest($"boolean ran on {backend}, not native CGAL — cannot verify the native path");

        var rep = MeshSanitizer.Analyse(trimmed);
        Assert(rep.TriangleCount > 0 && rep.VertexCount > 0, "native CGAL trim produced an empty mesh");
        Assert(rep.IsManifold, $"trimmed mesh not 2-manifold: {rep.NonManifoldEdgeCount} non-manifold edges");
        Assert(rep.IsClosed, $"trimmed mesh not watertight: {rep.BoundaryEdgeCount} boundary edges");
        Assert(rep.HasConsistentNormals,
            $"trimmed mesh normals not unified: {rep.NormalInconsistencyCount} inconsistent edges");
        Assert(rep.SignedVolume > 0,
            $"trimmed mesh inward-oriented (signed volume {rep.SignedVolume:0.###e0} <= 0)");
        Assert(rep.Volume < inRep.Volume,
            $"trim removed no material (out vol {rep.Volume:0.####} >= input {inRep.Volume:0.####})");
        Console.WriteLine(
            $"      [cgal] ETH stone trim: in {inRep.TriangleCount} tris V={inRep.Volume:0.####} m^3 -> " +
            $"out {rep.TriangleCount} tris V={rep.Volume:0.####} m^3 (native CGAL: manifold+closed+unified normals)");
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

    // Axis-aligned box mesh [lo..hi] with outward-facing triangles (same winding
    // convention as UnitCube).
    private static MeshSnapshot Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var verts = new double[]
        {
            x0, y0, z0,  x1, y0, z0,  x1, y1, z0,  x0, y1, z0,
            x0, y0, z1,  x1, y0, z1,  x1, y1, z1,  x0, y1, z1,
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

    private static void Bounds(MeshSnapshot m, out double[] lo, out double[] hi)
    {
        lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        hi = new[] { double.MinValue, double.MinValue, double.MinValue };
        var c = m.VertexCoordsXyz;
        for (int i = 0; i + 2 < c.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                if (c[i + k] < lo[k]) lo[k] = c[i + k];
                if (c[i + k] > hi[k]) hi[k] = c[i + k];
            }
    }

    // Locate one ETH1100 closed stone mesh. Checks FRAHAN_ETH_DIR first, then a
    // couple of known dev-machine locations; returns null (=> SKIP) if none exist.
    private static string FindEthStone()
    {
        const string rel = "closed" + "/" + "1100 Closed Stone Meshes" + "/" + "0000.obj";
        var candidates = new System.Collections.Generic.List<string>();
        string env = Environment.GetEnvironmentVariable("FRAHAN_ETH_DIR");
        if (!string.IsNullOrEmpty(env)) candidates.Add(Path.Combine(env, "0000.obj"));
        candidates.Add(@"D:\code_ws\Data\eth1100\closed\1100 Closed Stone Meshes\0000.obj");
        // walk up from the test bin looking for a sibling Data/eth1100
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            candidates.Add(Path.Combine(dir, "Data", "eth1100", rel.Replace('/', Path.DirectorySeparatorChar)));
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
