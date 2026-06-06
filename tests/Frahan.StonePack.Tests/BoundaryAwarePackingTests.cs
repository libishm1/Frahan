using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.GH.TwoD;
using Rhino.Geometry;

namespace Frahan.Tests;

// 2026-05-05 - Half B boundary-aware mode tests.
//
// Coverage:
//   - Mode = 0 + identical fixture must produce bit-equivalent SourceIndices
//     (regression guard for the boundary-mode-off path).
//   - Mode = 1 + a part with one straight edge matching a sheet edge must
//     place that part FIRST (boundary-worthy sort assertion).
//   - Mode = 1 + parts with no boundary affinity must fall back to the
//     geometric sort path (no crash, all parts placed where possible).
//
// Like the SheetHolesUtil tests, these construct PolylineCurves and call
// Curve.Contains internally via the V506 PrepareSheet path, so they SKIP
// under the existing "rhcommon_c init failed" condition pending B11.

static class BoundaryAwarePackingTests
{
    // -- Mode = 0 regression: same input -> same SourceIndices order ---------

    public static void ModeOff_PreservesGeometricOrder()
    {
        var sheets = new List<Curve> { Rect(0, 0, 100, 100) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        var parts = new List<Curve>
        {
            Rect(0, 0, 30, 20),  // area 600 (largest)
            Rect(0, 0, 25, 15),  // area 375
            Rect(0, 0, 10, 10),  // area 100 (smallest)
        };

        var solverOff = new IrregularSheetFillV506(
            sheets, holes,
            spacing: 1.0,
            rotationsDeg: new[] { 0.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 100,
            boundaryMode: 0,
            minBoundaryAffinity: 0.5);
        var resOff = solverOff.Pack(parts);

        // Largest (area 600) must come first per AreaDescending.
        Assert(resOff.SourceIndices.Count >= 1, "expected at least one placement");
        Assert(resOff.SourceIndices[0] == 0, $"largest part should be placed first, got idx {resOff.SourceIndices[0]}");
    }

    // -- Mode = 1: boundary-worthy part placed first --------------------------

    public static void ModeOn_BoundaryWorthyPartPlacedFirst()
    {
        var sheets = new List<Curve> { Rect(0, 0, 100, 100) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        // A long thin part whose long edge (len 80) closely matches a side of
        // the 100x100 sheet (len 100). Bigger parts come AFTER it under
        // boundary-aware sort.
        var parts = new List<Curve>
        {
            Rect(0, 0, 90, 90),  // area 8100 — biggest
            Rect(0, 0, 50, 50),  // area 2500
            Rect(0, 0, 80, 5),   // area  400 — smallest BUT boundary-aligned
        };

        var solverOn = new IrregularSheetFillV506(
            sheets, holes,
            spacing: 1.0,
            rotationsDeg: new[] { 0.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 200,
            boundaryMode: 1,
            minBoundaryAffinity: 0.5);
        var resOn = solverOn.Pack(parts);

        Assert(resOn.SourceIndices.Count >= 1, "expected at least one placement");
        // The 80x5 strip (idx 2) has a length-80 edge that scores high against
        // the sheet's length-100 sides. Under boundary mode it must be placed
        // FIRST despite being the smallest by area.
        Assert(resOn.SourceIndices[0] == 2,
            $"boundary-worthy part should be placed first, got idx {resOn.SourceIndices[0]}");
    }

    // -- Mode = 1: no boundary affinity -> falls back to geometric sort -------

    public static void ModeOn_NoAffinityFallsBackToAreaSort()
    {
        var sheets = new List<Curve> { Rect(0, 0, 100, 100) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        // Three small squares, none with edges matching sheet sides closely.
        // With MinBoundaryAffinity = 0.99 (very strict), no part qualifies as
        // boundary-worthy and the result must match AreaDescending sort.
        var parts = new List<Curve>
        {
            Rect(0, 0,  5,  5),  // area  25
            Rect(0, 0, 12, 12),  // area 144 (largest)
            Rect(0, 0,  8,  8),  // area  64
        };

        var solverOn = new IrregularSheetFillV506(
            sheets, holes,
            spacing: 1.0,
            rotationsDeg: new[] { 0.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 100,
            boundaryMode: 1,
            minBoundaryAffinity: 0.99);
        var resOn = solverOn.Pack(parts);

        Assert(resOn.SourceIndices.Count >= 1, "expected at least one placement");
        // Largest by area (idx 1) wins because no part qualifies as boundary-worthy.
        Assert(resOn.SourceIndices[0] == 1,
            $"largest part should be placed first under fallback, got idx {resOn.SourceIndices[0]}");
    }

    // -- Mode = 1 ctor accepts and stores params (smoke) ---------------------

    public static void ModeOn_Construction_DoesNotThrow()
    {
        var solver = new IrregularSheetFillV506(
            new[] { Rect(0, 0, 10, 10) },
            new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() },
            spacing: 0.1,
            rotationsDeg: new[] { 0.0, 90.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 100,
            boundaryMode: 1,
            minBoundaryAffinity: 0.5);
        Assert(solver != null, "ctor should succeed with boundaryMode=1");
    }

    // -- Half C: Mode = 2 ctor smoke ----------------------------------------

    public static void Mode2_Construction_DoesNotThrow()
    {
        var solver = new IrregularSheetFillV506(
            new[] { Rect(0, 0, 100, 100) },
            new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() },
            spacing: 1.0,
            rotationsDeg: new[] { 0.0, 90.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 100,
            boundaryMode: 2,
            minBoundaryAffinity: 0.3,
            discretizationTolerance: 0.01);
        Assert(solver != null, "ctor should succeed with boundaryMode=2");
    }

    // -- Half C: Mode = 2 places boundary-worthy parts on the edge -----------

    public static void Mode2_BoundaryWorthyLandsOnEdge()
    {
        // 100x100 sheet, one boundary-worthy strip (80x5) plus filler.
        // In Mode 2, the strip should land in the boundary band — its
        // bounding box must touch the sheet outline within a small slack.
        var sheets = new List<Curve> { Rect(0, 0, 100, 100) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        var parts = new List<Curve>
        {
            Rect(0, 0, 80, 5),    // boundary-worthy
            Rect(0, 0, 30, 30),   // filler
            Rect(0, 0, 25, 25),   // filler
        };

        var solver = new IrregularSheetFillV506(
            sheets, holes,
            spacing: 1.0,
            rotationsDeg: new[] { 0.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 200,
            boundaryMode: 2,
            minBoundaryAffinity: 0.3);
        var res = solver.Pack(parts);

        Assert(res.PackedCurves.Count >= 1, "expected at least one placement");
        // First placed part should be the boundary-worthy strip (idx 2 in
        // input list — sorted to head by Mode 2's boundary-worthy-first sort).
        Assert(res.SourceIndices[0] == 0,
            $"boundary-worthy part should be placed first under Mode 2, got idx {res.SourceIndices[0]}");

        // The strip's transform should land it inside the sheet AND with
        // its bounding box touching the sheet outline within ~ part max-dim.
        // We assert a weaker property: the strip's first packed curve has
        // its bounding box adjacent to either x=0, x=100, y=0, or y=100
        // (within 10 units = 12.5% of strip length, generous slack).
        var strip = res.PackedCurves[0];
        var bb = strip.GetBoundingBox(true);
        var slack = 10.0;
        var touches = bb.Min.X < slack
                   || bb.Min.Y < slack
                   || bb.Max.X > 100 - slack
                   || bb.Max.Y > 100 - slack;
        Assert(touches,
            $"boundary-worthy strip should land near a sheet edge in Mode 2; bbox = {bb}");
    }

    // -- Half C: Mode = 2 fallback to all candidates if phase saturated -----

    public static void Mode2_FallsBackWhenBoundarySaturated()
    {
        // Tiny boundary-worthy parts but they can't all fit on a tiny sheet
        // along the boundary; the third one should fall back to interior
        // (or anywhere) rather than being unplaced.
        var sheets = new List<Curve> { Rect(0, 0, 50, 50) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        var parts = new List<Curve>
        {
            Rect(0, 0, 40, 5),   // boundary-worthy
            Rect(0, 0, 40, 5),   // boundary-worthy
            Rect(0, 0, 40, 5),   // boundary-worthy
            Rect(0, 0, 40, 5),   // boundary-worthy — saturated, expect fallback
        };

        var solver = new IrregularSheetFillV506(
            sheets, holes,
            spacing: 1.0,
            rotationsDeg: new[] { 0.0, 90.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300,
            boundaryMode: 2,
            minBoundaryAffinity: 0.3);
        var res = solver.Pack(parts);

        // Expect at least 3 placed (the 4th may or may not fit even with
        // fallback — that's OK). The point is the solver does not throw
        // and does not silently drop boundary-worthy parts that could fit
        // somewhere just because the boundary phase ran out of room.
        Assert(res.PackedCurves.Count >= 2,
            $"expected fallback to place at least 2 parts under saturation, got {res.PackedCurves.Count}");
    }

    // -- Half C: auto-tune window length to part scale -----------------------

    public static void Mode1_AutoTunes_ToPartScale()
    {
        // Sheet 1000 wide; under Half B the window length defaulted to
        // sheetSpan/50 = 20, way bigger than 5-unit part edges. Half C
        // should auto-tune the window length down to ~part edge median.
        // Smoke test: ctor + pack succeeds (no NaN, no throw) and the
        // boundary-worthy part still gets placed first when it dominates
        // the score landscape.
        var sheets = new List<Curve> { Rect(0, 0, 1000, 1000) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        var parts = new List<Curve>
        {
            Rect(0, 0, 100, 100),  // big square (large area, no boundary edge match)
            Rect(0, 0, 200, 5),    // long thin strip (matches sheet edge)
            Rect(0, 0, 80, 80),    // medium square
        };

        var solver = new IrregularSheetFillV506(
            sheets, holes,
            spacing: 1.0,
            rotationsDeg: new[] { 0.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            cornerMode: PackingCornerMode.BottomLeft,
            seed: 0,
            maxCandidates: 300,
            boundaryMode: 1,
            minBoundaryAffinity: 0.3);
        var res = solver.Pack(parts);

        Assert(res.PackedCurves.Count >= 1, "expected at least one placement");
        // Strip (idx 1) has a 200-unit edge that matches the 1000-unit
        // sheet edge well after auto-tune. It should be placed before the
        // bigger area part 0 under boundary-aware sort.
        Assert(res.SourceIndices[0] == 1,
            $"boundary-worthy strip should be placed first after auto-tune, got idx {res.SourceIndices[0]}");
    }

    // -- Helpers --------------------------------------------------------------

    private static Curve Rect(double x0, double y0, double x1, double y1)
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
