#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// MeshContactDetectorTests — Cockroach-style proximity-based contact
// detection. All tests are pure-managed (no Rhino runtime needed); they
// build MeshSnapshots from raw vertex / triangle arrays.
// =============================================================================

static class MeshContactDetectorTests
{
    // ─── Basic contact cases ────────────────────────────────────────────────

    public static void Detect_TwoStackedCubes_FindsOneContact()
    {
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(0, 0, 1);   // stacked on top
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-3);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalZ - 1.0) < 1e-6,
            $"normal Z expected 1, got {ifaces[0].NormalZ}");
        Assert(ifaces[0].ContactPolygon.Count >= 3,
            $"contact polygon must have >= 3 vertices, got {ifaces[0].ContactPolygon.Count}");
    }

    public static void Detect_TwoCubesSideBySide_FindsOneContact()
    {
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(1, 0, 0);   // side-by-side along +X
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "L", "R" },
            distanceTol: 1e-3);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalX - 1.0) < 1e-6,
            $"normal X expected 1, got {ifaces[0].NormalX}");
    }

    public static void Detect_GapWithinTolerance_StillFindsContact()
    {
        // 0.5 mm gap — should still count as contact with tol 1 mm.
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(0, 0, 1.0005);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 0.001);
        Assert(ifaces.Count == 1,
            $"expected 1 contact within 1 mm tolerance, got {ifaces.Count}");
    }

    public static void Detect_GapBeyondTolerance_FindsNothing()
    {
        // 5 mm gap — beyond 1 mm tolerance.
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(0, 0, 1.005);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 0.001);
        Assert(ifaces.Count == 0, $"expected 0 contacts (gap > tol), got {ifaces.Count}");
    }

    public static void Detect_NoOverlap_FindsNothing()
    {
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(10, 10, 10);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 0.001);
        Assert(ifaces.Count == 0, $"expected 0 contacts, got {ifaces.Count}");
    }

    public static void Detect_PartialOverlap_DetectsCorrectArea()
    {
        // A occupies [0,1]^3. B is at [0.5..1.5, 0.5..1.5, 1..2].
        // They share a 0.5 × 0.5 face on the z=1 plane.
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(0.5, 0.5, 1);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 0.001);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalZ - 1.0) < 1e-6,
            $"normal Z expected 1, got {ifaces[0].NormalZ}");
        // Polygon area should be approximately 0.25 (0.5 × 0.5 quad).
        double area = PolygonArea3D(ifaces[0].ContactPolygon);
        Assert(area > 0.10 && area < 0.30,
            $"contact polygon area expected ~0.25, got {area}");
    }

    // ─── Argument validation ────────────────────────────────────────────────

    public static void Detect_NullMeshes_Throws()
    {
        bool threw = false;
        try { _ = MeshContactDetector.Detect(null, new[] { "A" }); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null meshes should throw");
    }

    public static void Detect_MismatchedIdsCount_Throws()
    {
        bool threw = false;
        var a = MakeUnitCube(0, 0, 0);
        try
        {
            _ = MeshContactDetector.Detect(
                new[] { a }, new[] { "A", "B" });
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "mismatched ids count should throw");
    }

    // ─── Broad-phase (sweep-and-prune) correctness ─────────────────────────

    public static void Detect_ManyStackedCubes_ChainsContactsCorrectly()
    {
        // 8 unit cubes stacked along +Z. Expected: 7 bed-joint contacts
        // (one between each consecutive pair) and 0 contacts between
        // non-adjacent pairs. This exercises the broad-phase: with N=8
        // there are C(8,2)=28 candidate pairs but only 7 actually touch.
        var snaps = new List<MeshSnapshot>(8);
        var ids = new List<string>(8);
        for (int i = 0; i < 8; i++)
        {
            snaps.Add(MakeUnitCube(0, 0, i));
            ids.Add($"cube_{i}");
        }
        var ifaces = MeshContactDetector.Detect(
            snaps, ids, distanceTol: 1e-3);
        Assert(ifaces.Count == 7,
            $"expected 7 bed-joint contacts (consecutive pairs), got {ifaces.Count}");
        // All normals should be +Z.
        for (int i = 0; i < ifaces.Count; i++)
        {
            Assert(Math.Abs(ifaces[i].NormalZ - 1.0) < 1e-6,
                $"contact {i} normal Z expected 1, got {ifaces[i].NormalZ}");
        }
    }

    public static void Detect_DistantClusters_BroadPhaseSkipsCleanly()
    {
        // Two clusters of touching cubes, far apart on +X. Each cluster
        // should produce its own contact; no cross-cluster contacts.
        var snaps = new List<MeshSnapshot>
        {
            MakeUnitCube(0, 0, 0),    // cluster 1
            MakeUnitCube(0, 0, 1),    // touches cube 0
            MakeUnitCube(50, 0, 0),   // cluster 2 (far away)
            MakeUnitCube(50, 0, 1),   // touches cube 2
        };
        var ids = new List<string> { "a0", "a1", "b0", "b1" };
        var ifaces = MeshContactDetector.Detect(
            snaps, ids, distanceTol: 1e-3);
        Assert(ifaces.Count == 2,
            $"expected 2 contacts (one per cluster), got {ifaces.Count}");
    }

    public static void Detect_NegativeTolerance_Throws()
    {
        bool threw = false;
        var a = MakeUnitCube(0, 0, 0);
        var b = MakeUnitCube(0, 0, 1);
        try
        {
            _ = MeshContactDetector.Detect(
                new[] { a, b }, new[] { "A", "B" },
                distanceTol: -0.001);
        }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative tol should throw");
    }

    // ─── Spatial grid (third indexing layer) ───────────────────────────────

    public static void Grid_TwoOverlappingCubes_YieldsOnePair()
    {
        var snaps = new[] { MakeUnitCube(0, 0, 0), MakeUnitCube(0, 0, 1) };
        var grid = new MeshSpatialGrid(snaps, 1.0);
        int count = 0;
        var seen = new HashSet<long>();
        foreach (var (i, j) in grid.CandidatePairs())
        {
            count++;
            long key = ((long)i << 32) | (uint)j;
            Assert(seen.Add(key), $"pair ({i},{j}) reported twice");
        }
        Assert(count == 1, $"expected 1 candidate pair, got {count}");
    }

    public static void Grid_DistantCubes_YieldNoPair()
    {
        var snaps = new[] { MakeUnitCube(0, 0, 0), MakeUnitCube(50, 50, 50) };
        var grid = new MeshSpatialGrid(snaps, 1.0);
        int count = 0;
        foreach (var _ in grid.CandidatePairs()) count++;
        Assert(count == 0, $"expected 0 candidate pairs, got {count}");
    }

    public static void Grid_ChainOfCubes_YieldsConsecutivePairsOnce()
    {
        // 5 cubes stacked along +Z. The grid should report exactly 4
        // consecutive pairs, and each pair exactly once even though
        // adjacent cubes share many cells.
        var snaps = new List<MeshSnapshot>(5);
        for (int i = 0; i < 5; i++) snaps.Add(MakeUnitCube(0, 0, i));
        var grid = new MeshSpatialGrid(snaps, 1.0);
        var pairs = new List<(int, int)>();
        var seen = new HashSet<long>();
        foreach (var (i, j) in grid.CandidatePairs())
        {
            long key = ((long)i << 32) | (uint)j;
            Assert(seen.Add(key), $"pair ({i},{j}) reported twice");
            pairs.Add((i, j));
        }
        // Cubes share faces and corners → grid will yield more than just
        // the 4 consecutive pairs (it's a broad-phase, after all). The
        // contract is: every consecutive pair must be present, no pair
        // duplicated, and far-apart pairs must NOT be present.
        for (int i = 0; i < 4; i++)
            Assert(pairs.Contains((i, i + 1)),
                $"missing consecutive pair ({i},{i + 1})");
        Assert(!pairs.Contains((0, 4)),
            "pair (0,4) must not appear — cubes too far apart");
    }

    public static void Grid_BadCellSize_Throws()
    {
        var snaps = new[] { MakeUnitCube(0, 0, 0) };
        bool threw = false;
        try { _ = new MeshSpatialGrid(snaps, 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "cellSize <= 0 must throw");
    }

    public static void Detect_OverThreshold_StillReturnsCorrectContacts()
    {
        // GridSwitchThreshold is 1000. Build 1000 unit cubes in a line
        // along +Z so each cube only touches its immediate +Z neighbor.
        // The grid path must produce exactly 999 contacts — same as
        // the sweep-and-prune path would on the same input.
        const int count = MeshContactDetector.GridSwitchThreshold;
        var snaps = new List<MeshSnapshot>(count);
        var ids = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            snaps.Add(MakeUnitCube(0, 0, i));
            ids.Add($"c{i}");
        }
        var ifaces = MeshContactDetector.Detect(snaps, ids, distanceTol: 1e-3);
        Assert(ifaces.Count == count - 1,
            $"expected {count - 1} bed-joint contacts via grid, got {ifaces.Count}");
        // Sanity: all normals +Z.
        for (int i = 0; i < ifaces.Count; i++)
            Assert(Math.Abs(ifaces[i].NormalZ - 1.0) < 1e-6,
                $"contact {i} normal Z expected 1, got {ifaces[i].NormalZ}");
    }

    // ─── GH component metadata ──────────────────────────────────────────────

    public static void Gh_RobustAutoInterfacesComponent_Metadata()
    {
        var c = new RobustAutoInterfacesComponent();
        Assert(c.ComponentGuid == new Guid("F2D000B4-CADC-4F2D-A0B4-7E60CADA15A0"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 5, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 2, $"Output count {c.Params.Output.Count}");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static MeshSnapshot MakeUnitCube(double x0, double y0, double z0)
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
        // 12 outward triangles (CCW from outside).
        var tris = new int[]
        {
            // -Z bottom (CCW from below)
            0, 3, 2,  0, 2, 1,
            // +Z top (CCW from above)
            4, 5, 6,  4, 6, 7,
            // -Y front (CCW from -Y side)
            0, 1, 5,  0, 5, 4,
            // +X right
            1, 2, 6,  1, 6, 5,
            // +Y back
            2, 3, 7,  2, 7, 6,
            // -X left
            3, 0, 4,  3, 4, 7,
        };
        return new MeshSnapshot(verts, tris);
    }

    private static double PolygonArea3D(IReadOnlyList<ContactVertex> p)
    {
        if (p == null || p.Count < 3) return 0.0;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < p.Count; i++) { cx += p[i].X; cy += p[i].Y; cz += p[i].Z; }
        cx /= p.Count; cy /= p.Count; cz /= p.Count;
        double sx = 0, sy = 0, sz = 0;
        for (int i = 0; i < p.Count; i++)
        {
            int j = (i + 1) % p.Count;
            double ax = p[i].X - cx, ay = p[i].Y - cy, az = p[i].Z - cz;
            double bx = p[j].X - cx, by = p[j].Y - cy, bz = p[j].Z - cz;
            sx += ay * bz - az * by;
            sy += az * bx - ax * bz;
            sz += ax * by - ay * bx;
        }
        return 0.5 * Math.Sqrt(sx * sx + sy * sy + sz * sz);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
