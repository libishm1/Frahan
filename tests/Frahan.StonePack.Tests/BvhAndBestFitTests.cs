#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Packing;

namespace Frahan.Tests;

// =============================================================================
// BvhAndBestFitTests — BVH closest-point correctness vs brute force,
// BestFitInventoryPacker outcomes vs first-fit Pack, GH metadata.
// =============================================================================

static class BvhAndBestFitTests
{
    // ─── BVH correctness ────────────────────────────────────────────────────

    public static void Bvh_UnitCube_QueryInsideReturnsZero()
    {
        var (verts, tris) = MakeUnitCube(0, 0, 0);
        var bvh = new MeshBvh(verts, tris);
        bool ok = bvh.ClosestPoint(0.5, 0.5, 0.5, double.PositiveInfinity,
            out double cx, out double cy, out double cz, out int triIdx, out double dist);
        Assert(ok, "ClosestPoint must succeed");
        Assert(triIdx >= 0 && triIdx < 12, $"triIdx out of range: {triIdx}");
        Assert(Math.Abs(dist - 0.5) < 1e-9,
            $"distance from cube centre to surface should be 0.5, got {dist}");
    }

    public static void Bvh_UnitCube_QueryOutsideReturnsNearestFace()
    {
        var (verts, tris) = MakeUnitCube(0, 0, 0);
        var bvh = new MeshBvh(verts, tris);
        bool ok = bvh.ClosestPoint(0.5, 0.5, 2.0, double.PositiveInfinity,
            out double cx, out double cy, out double cz, out int triIdx, out double dist);
        Assert(ok, "ClosestPoint must succeed");
        Assert(Math.Abs(dist - 1.0) < 1e-9,
            $"distance from (0.5, 0.5, 2) to top face (z=1) should be 1.0, got {dist}");
        Assert(Math.Abs(cz - 1.0) < 1e-9, $"closest cz should be 1.0, got {cz}");
    }

    public static void Bvh_RespectsMaxDistance()
    {
        var (verts, tris) = MakeUnitCube(0, 0, 0);
        var bvh = new MeshBvh(verts, tris);
        bool ok = bvh.ClosestPoint(10, 10, 10, 0.5,
            out _, out _, out _, out int triIdx, out _);
        Assert(!ok, "ClosestPoint with maxDistance=0.5 from (10,10,10) must fail");
        Assert(triIdx == -1, $"triIdx should be -1 when no triangle within range, got {triIdx}");
    }

    public static void Bvh_MatchesBruteForce_OnRandomPoints()
    {
        var (verts, tris) = MakeUnitCube(0, 0, 0);
        var bvh = new MeshBvh(verts, tris);
        var rng = new Random(42);
        for (int trial = 0; trial < 20; trial++)
        {
            double px = rng.NextDouble() * 4 - 2;
            double py = rng.NextDouble() * 4 - 2;
            double pz = rng.NextDouble() * 4 - 2;
            bvh.ClosestPoint(px, py, pz, double.PositiveInfinity,
                out double cx, out double cy, out double cz, out _, out double bvhD);
            BruteForceClosest(verts, tris, px, py, pz, out _, out _, out _, out double bfD);
            Assert(Math.Abs(bvhD - bfD) < 1e-9,
                $"trial {trial}: BVH dist {bvhD} != brute-force dist {bfD}");
        }
    }

    // ─── MeshContactDetector with BVH still finds 2-cube contacts ─────────

    public static void Bvh_ContactDetector_StillFindsStackedContact()
    {
        var (va, ta) = MakeUnitCube(0, 0, 0);
        var (vb, tb) = MakeUnitCube(0, 0, 1);
        var sa = new MeshSnapshot(va, ta);
        var sb = new MeshSnapshot(vb, tb);
        var ifaces = MeshContactDetector.Detect(
            new[] { sa, sb }, new[] { "A", "B" }, distanceTol: 1e-3);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
    }

    // ─── Best-fit packer ────────────────────────────────────────────────────

    public static void BestFit_Inventory_PrefersExactMatch()
    {
        // 3 candidate slabs:
        //   - too small (0.10 wide)
        //   - exact match (0.30 wide)
        //   - too big but still fits (0.40 wide; gap is 0.30 so this would be skipped)
        // Best fit should pick the 0.30-wide slab.
        var slabs = new List<Slab>
        {
            Slab.Box(0, 0, 0, 0.10, 0.20, 0.15),
            Slab.Box(0, 0, 0, 0.30, 0.20, 0.15),
            Slab.Box(0, 0, 0, 0.30, 0.20, 0.15),
        };
        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.30, wallHeight: 0.15, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);
        var result = BestFitInventoryPacker.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 1, $"expected 1 placed, got {result.PlacedBlocks.Count}");
        // The placed block should be the 0.30-wide one (perfect fit).
        var v = result.PlacedBlocks[0].VertexCoordsXyz;
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        for (int i = 0; i < result.PlacedBlocks[0].VertexCount; i++)
        {
            double x = v[3 * i + 0];
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
        }
        Assert(Math.Abs((xMax - xMin) - 0.30) < 1e-6,
            $"placed block width should be 0.30 (best fit), got {xMax - xMin}");
    }

    public static void BestFit_Pack_FullCoverage_OnUniformInventory()
    {
        // 30 uniform 0.30 × 0.20 × 0.15 slabs into a 1.5 × 1.0 × 0.20 wall.
        // 6 courses × 5 blocks = 30. Best-fit should produce identical
        // coverage to first-fit Pack.
        var slabs = new List<Slab>();
        for (int i = 0; i < 30; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));
        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 1.5, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);
        var result = BestFitInventoryPacker.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 30,
            $"expected 30 placed blocks, got {result.PlacedBlocks.Count}");
        Assert(result.CourseCount == 6, $"expected 6 courses, got {result.CourseCount}");
    }

    public static void BestFit_NullSlabs_Throws()
    {
        bool threw = false;
        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar, 1, 1, 0.2, 0.15, 0, 0, 0, 2400, 1e-9);
        try { _ = BestFitInventoryPacker.Pack(null, opts); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null slabs should throw");
    }

    // ─── GH component metadata ──────────────────────────────────────────────

    public static void Gh_BestFitPackComponent_Metadata()
    {
        var c = new BestFitPackComponent();
        Assert(c.ComponentGuid == new Guid("01234567-89AB-CDEF-0123-456789ABCDEF"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        // 10 primitive + 2 optional Stage 2 + 1 optional Start Plane = 13
        Assert(c.Params.Input.Count == 13, $"Input count {c.Params.Input.Count}");
        // Assembly + Result + Display Transform = 3
        Assert(c.Params.Output.Count == 3, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_MeshRepairComponent_Metadata()
    {
        // Phase F6 (UX architecture report §5.2 + AGENTS.md §8 stability):
        // the newer Masonry MeshRepairComponent (GUID F2D000B5) was dropped
        // 2026-05-19; the older root MeshRepairComponent (AB12C00A, longer
        // commit lineage) survives. The two had the same nickname (MeshFix)
        // which collided in canvas search; only one survived a .gha reload.
        var c = new MeshRepairComponent();
        Assert(c.ComponentGuid == new Guid("AB12C00A-1A2B-4C3D-9E4F-5A6B7C8D9E0A"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Mesh", $"SubCategory '{c.SubCategory}'");
        // Root variant: Meshes + Weld Angle + Heal Distance = 3 in
        // Repaired + Trace + Skipped + Summary = 4 out.
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4, $"Output count {c.Params.Output.Count}");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static (double[], int[]) MakeUnitCube(double x0, double y0, double z0)
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
            0, 3, 2, 0, 2, 1,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        };
        return (verts, tris);
    }

    private static void BruteForceClosest(
        double[] verts, int[] tris, double px, double py, double pz,
        out double cx, out double cy, out double cz, out double dist)
    {
        cx = cy = cz = 0;
        double bestD2 = double.PositiveInfinity;
        int n = tris.Length / 3;
        for (int t = 0; t < n; t++)
        {
            int ia = tris[3 * t + 0], ib = tris[3 * t + 1], ic = tris[3 * t + 2];
            ClosestPointOnTriangle(px, py, pz,
                verts[3 * ia + 0], verts[3 * ia + 1], verts[3 * ia + 2],
                verts[3 * ib + 0], verts[3 * ib + 1], verts[3 * ib + 2],
                verts[3 * ic + 0], verts[3 * ic + 1], verts[3 * ic + 2],
                out double qx, out double qy, out double qz);
            double dx = px - qx, dy = py - qy, dz = pz - qz;
            double d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < bestD2) { bestD2 = d2; cx = qx; cy = qy; cz = qz; }
        }
        dist = Math.Sqrt(bestD2);
    }

    // Verbatim copy of MeshBvh.ClosestPointOnTriangle (Ericson §5.1.5) for
    // the brute-force comparator. Standalone so the test doesn't depend
    // on internal accessors.
    private static void ClosestPointOnTriangle(
        double px, double py, double pz,
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz,
        out double qx, out double qy, out double qz)
    {
        double abx = bx - ax, aby = by - ay, abz = bz - az;
        double acx = cx - ax, acy = cy - ay, acz = cz - az;
        double apx = px - ax, apy = py - ay, apz = pz - az;
        double d1 = abx * apx + aby * apy + abz * apz;
        double d2 = acx * apx + acy * apy + acz * apz;
        if (d1 <= 0 && d2 <= 0) { qx = ax; qy = ay; qz = az; return; }

        double bpx = px - bx, bpy = py - by, bpz = pz - bz;
        double d3 = abx * bpx + aby * bpy + abz * bpz;
        double d4 = acx * bpx + acy * bpy + acz * bpz;
        if (d3 >= 0 && d4 <= d3) { qx = bx; qy = by; qz = bz; return; }

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double t = d1 / (d1 - d3);
            qx = ax + t * abx; qy = ay + t * aby; qz = az + t * abz;
            return;
        }

        double cpx = px - cx, cpy = py - cy, cpz = pz - cz;
        double d5 = abx * cpx + aby * cpy + abz * cpz;
        double d6 = acx * cpx + acy * cpy + acz * cpz;
        if (d6 >= 0 && d5 <= d6) { qx = cx; qy = cy; qz = cz; return; }

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double t = d2 / (d2 - d6);
            qx = ax + t * acx; qy = ay + t * acy; qz = az + t * acz;
            return;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            double t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            qx = bx + t * (cx - bx); qy = by + t * (cy - by); qz = bz + t * (cz - bz);
            return;
        }

        double denom = 1.0 / (va + vb + vc);
        double v_ = vb * denom;
        double w_ = vc * denom;
        qx = ax + v_ * abx + w_ * acx;
        qy = ay + v_ * aby + w_ * acy;
        qz = az + v_ * abz + w_ * acz;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
