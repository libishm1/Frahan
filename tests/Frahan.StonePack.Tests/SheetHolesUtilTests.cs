using System;
using System.Collections.Generic;
using Frahan.GH;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.Tests;

// 2026-05-05 - SheetHolesUtil PIP-first routing tests (Bug B-2D-001).
//
// Routing contract under test:
//   - Per-hole, prefer the sheet whose outline geometrically contains the
//     hole's centroid (point-in-polygon).
//   - Fall back to the GH tree path index when no sheet contains the
//     centroid (covers degenerate / out-of-range / non-planar holes).
//   - Drop the hole only when both PIP and path-index lookups fail.
//
// All four scenarios below require RhinoCommon's Curve.Contains(...) which
// is implemented in rhcommon_c.dll. The test runner configures the native
// loader in Program.cs, so these tests run when Rhino 7+ is installed and
// SKIP otherwise (consistent with the rest of the "Rhino" suite).

static class SheetHolesUtilTests
{
    // -- Per-sheet branched tree (must not regress) -------------------------

    public static void PerSheetBranches_RoutesByPath()
    {
        var sheet0 = MakeRectangle(0, 0, 10, 10);
        var sheet1 = MakeRectangle(20, 0, 30, 10);
        var hole0  = MakeRectangle(2, 2, 4, 4);     // inside sheet 0
        var hole1  = MakeRectangle(22, 2, 24, 4);   // inside sheet 1

        var tree = new GH_Structure<GH_Curve>();
        tree.Append(new GH_Curve(hole0), new GH_Path(0));
        tree.Append(new GH_Curve(hole1), new GH_Path(1));

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0, sheet1 }, tree, 2, 0.01);

        Assert(result.Count == 2,             $"expected 2 sheet buckets, got {result.Count}");
        Assert(result[0].Count == 1,          $"sheet 0 should hold 1 hole, got {result[0].Count}");
        Assert(result[1].Count == 1,          $"sheet 1 should hold 1 hole, got {result[1].Count}");
    }

    // -- Flat list with sheet-disjoint holes (the failing case) -------------

    public static void FlatList_RoutesByPip()
    {
        var sheet0 = MakeRectangle(0, 0, 10, 10);
        var sheet1 = MakeRectangle(20, 0, 30, 10);
        var hole0  = MakeRectangle(2, 2, 4, 4);     // centroid (3,3) in sheet 0
        var hole1  = MakeRectangle(22, 2, 24, 4);   // centroid (23,3) in sheet 1

        // Both holes wired into branch {0} (typical flat-list user wiring).
        var tree = new GH_Structure<GH_Curve>();
        tree.Append(new GH_Curve(hole0), new GH_Path(0));
        tree.Append(new GH_Curve(hole1), new GH_Path(0));

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0, sheet1 }, tree, 2, 0.01);

        Assert(result.Count == 2,             $"expected 2 sheet buckets, got {result.Count}");
        Assert(result[0].Count == 1,          $"sheet 0 should hold 1 hole (PIP routed), got {result[0].Count}");
        Assert(result[1].Count == 1,          $"sheet 1 should hold 1 hole (PIP routed), got {result[1].Count}");
    }

    // -- Mismatched path: PIP overrides path --------------------------------

    public static void MismatchedPath_PipOverrides()
    {
        var sheet0 = MakeRectangle(0, 0, 10, 10);
        var sheet1 = MakeRectangle(20, 0, 30, 10);
        var hole0  = MakeRectangle(2, 2, 4, 4);     // geometrically in sheet 0

        // Path says sheet 1 but geometry says sheet 0.
        var tree = new GH_Structure<GH_Curve>();
        tree.Append(new GH_Curve(hole0), new GH_Path(1));

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0, sheet1 }, tree, 2, 0.01);

        Assert(result[0].Count == 1, $"sheet 0 should hold 1 hole (PIP wins over mismatched path), got {result[0].Count}");
        Assert(result[1].Count == 0, $"sheet 1 should hold 0 holes, got {result[1].Count}");
    }

    // -- Out-of-tree path with valid PIP (currently dropped → recovered) ----

    public static void OutOfRangePath_PipRecovers()
    {
        var sheet0 = MakeRectangle(0, 0, 10, 10);
        var sheet1 = MakeRectangle(20, 0, 30, 10);
        var hole0  = MakeRectangle(2, 2, 4, 4);     // geometrically in sheet 0

        // Path index 5 with only 2 sheets: previously dropped; now PIP recovers.
        var tree = new GH_Structure<GH_Curve>();
        tree.Append(new GH_Curve(hole0), new GH_Path(5));

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0, sheet1 }, tree, 2, 0.01);

        Assert(result[0].Count == 1, $"sheet 0 should hold 1 hole (PIP recovers out-of-range path), got {result[0].Count}");
        Assert(result[1].Count == 0, $"sheet 1 should hold 0 holes, got {result[1].Count}");
    }

    // -- Hole disjoint from every sheet: PIP fails, path fallback wins ------

    public static void DisjointHole_FallsBackToPath()
    {
        var sheet0  = MakeRectangle(0, 0, 10, 10);
        var sheet1  = MakeRectangle(20, 0, 30, 10);
        // Centroid (60, 60) - outside both sheets.
        var orphan  = MakeRectangle(58, 58, 62, 62);

        var tree = new GH_Structure<GH_Curve>();
        tree.Append(new GH_Curve(orphan), new GH_Path(1));

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0, sheet1 }, tree, 2, 0.01);

        Assert(result[0].Count == 0, $"sheet 0 should hold 0 holes, got {result[0].Count}");
        Assert(result[1].Count == 1, $"sheet 1 should hold 1 hole (path fallback), got {result[1].Count}");
    }

    // -- Empty tree: behavior unchanged -------------------------------------

    public static void EmptyTree_ReturnsEmptyBuckets()
    {
        var sheet0 = MakeRectangle(0, 0, 10, 10);
        var sheet1 = MakeRectangle(20, 0, 30, 10);

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0, sheet1 }, new GH_Structure<GH_Curve>(), 2, 0.01);

        Assert(result.Count == 2,    $"expected 2 sheet buckets, got {result.Count}");
        Assert(result[0].Count == 0, $"sheet 0 should be empty, got {result[0].Count}");
        Assert(result[1].Count == 0, $"sheet 1 should be empty, got {result[1].Count}");
    }

    // -- Single-sheet fall-through preserved --------------------------------

    public static void SingleSheet_AnyBranchFallsThrough()
    {
        var sheet0 = MakeRectangle(0, 0, 10, 10);
        var hole0  = MakeRectangle(2, 2, 4, 4);
        // Path index 7 with one sheet: legacy single-sheet fallthrough must survive.
        var tree = new GH_Structure<GH_Curve>();
        tree.Append(new GH_Curve(hole0), new GH_Path(7));

        var result = SheetHolesUtil.BuildHolesBySheet(
            new[] { sheet0 }, tree, 1, 0.01);

        Assert(result.Count == 1,    $"expected 1 sheet bucket, got {result.Count}");
        Assert(result[0].Count == 1, $"single sheet should hold the hole, got {result[0].Count}");
    }

    // -- Helpers ------------------------------------------------------------

    private static Curve MakeRectangle(double x0, double y0, double x1, double y1)
    {
        var pl = new Polyline(new[]
        {
            new Point3d(x0, y0, 0),
            new Point3d(x1, y0, 0),
            new Point3d(x1, y1, 0),
            new Point3d(x0, y1, 0),
            new Point3d(x0, y0, 0),
        });
        return new PolylineCurve(pl);
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception(msg);
    }
}
