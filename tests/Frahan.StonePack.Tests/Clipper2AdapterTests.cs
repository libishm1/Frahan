#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// Clipper2AdapterTests — sanity tests for the Clipper2 production back-end.
// Exercises the cases that hit Greiner-Hormann's degenerate-intersection
// limits (vertex-on-edge, fully-coincident edges, polygon-with-holes).
// =============================================================================

static class Clipper2AdapterTests
{
    public static void Intersect_TwoSquares_ReturnsOverlap()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = Clipper2Adapter.Intersect(a, b);
        Assert(result.Count == 1, $"expected 1 loop, got {result.Count}");
        double area = Math.Abs(RobustPolygon2D.SignedArea(result[0]));
        Assert(Math.Abs(area - 1.0) < 1e-9, $"area expected 1, got {area}");
    }

    // ─── Minkowski + NFP-BLF foundation (2026-06-03) ───────────────────
    // These verify the exact primitives the IrregularSheetFillNfpBlf solver
    // rests on, with NO Rhino dependency (so they RUN headless, unlike the
    // Rhino-curve solver tests which SKIP without a live Rhino). Mirrors the
    // Python study probe (outputs/2026-06-03/pack2d_nfp_evolution/).

    public static void MinkowskiSum_TwoUnitSquares_NfpIsExpected()
    {
        var a = new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1), (0, 1) };
        var b = new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1), (0, 1) };
        var refl = new List<(double X, double Y)>();
        foreach (var (x, y) in b) refl.Add((-x, -y));
        var nfp = Clipper2Adapter.MinkowskiSum(a, refl);
        Assert(nfp.Count == 1, $"expected 1 NFP loop, got {nfp.Count}");
        double area = Math.Abs(RobustPolygon2D.SignedArea(nfp[0]));
        Assert(Math.Abs(area - 4.0) < 1e-6, $"NFP area expected 4, got {area}");
        double minX = 1e18, minY = 1e18, maxX = -1e18, maxY = -1e18;
        foreach (var (x, y) in nfp[0])
        {
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y;
        }
        Assert(Math.Abs(minX + 1) < 1e-6 && Math.Abs(minY + 1) < 1e-6 &&
               Math.Abs(maxX - 1) < 1e-6 && Math.Abs(maxY - 1) < 1e-6,
               $"NFP bounds expected (-1,-1,1,1), got ({minX},{minY},{maxX},{maxY})");
    }

    public static void NfpBlf_PlaceAtBottomLeftVertex_NoOverlapByConstruction()
    {
        // Sheet [0,5]^2, one placed unit square A at [2,3]^2, moving unit square B.
        var sheet = new List<(double X, double Y)> { (0, 0), (5, 0), (5, 5), (0, 5) };
        var aAbs  = new List<(double X, double Y)> { (2, 2), (3, 2), (3, 3), (2, 3) };
        var b     = new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1), (0, 1) };
        var refl  = new List<(double X, double Y)>();
        foreach (var (x, y) in b) refl.Add((-x, -y));
        var hull = b; // already convex

        // IFP = intersection over hull verts of (sheet - v).
        var ifp = new List<List<(double X, double Y)>> { sheet };
        foreach (var (vx, vy) in hull)
        {
            var shifted = new List<(double X, double Y)>();
            foreach (var (x, y) in sheet) shifted.Add((x - vx, y - vy));
            ifp = Clipper2Adapter.IntersectLoops(ifp,
                new List<IReadOnlyList<(double X, double Y)>> { shifted });
        }
        // NFP of placed A.
        var nfp = Clipper2Adapter.MinkowskiSum(aAbs, refl);
        // feasible = IFP \ NFP.
        var feasible = Clipper2Adapter.DifferenceLoops(ifp,
            nfp.Select(l => (IReadOnlyList<(double X, double Y)>)l).ToList());
        Assert(feasible.Count >= 1, "feasible region must be non-empty");

        // Bottom-left vertex (min y, then x).
        double bx = 0, by = 0; bool found = false;
        foreach (var loop in feasible)
            foreach (var (x, y) in loop)
                if (!found || y < by || (y == by && x < bx)) { bx = x; by = y; found = true; }
        Assert(found, "BL vertex must exist");

        // Place B at (bx,by); assert no overlap with A and full containment in sheet.
        var bPlaced = new List<(double X, double Y)>();
        foreach (var (x, y) in b) bPlaced.Add((x + bx, y + by));
        var overlap = Clipper2Adapter.IntersectLoops(
            new List<IReadOnlyList<(double X, double Y)>> { bPlaced },
            new List<IReadOnlyList<(double X, double Y)>> { aAbs });
        double ov = 0; foreach (var lp in overlap) ov += Math.Abs(RobustPolygon2D.SignedArea(lp));
        Assert(ov < 1e-6, $"placed B must not overlap A, got overlap area {ov}");

        var inSheet = Clipper2Adapter.IntersectLoops(
            new List<IReadOnlyList<(double X, double Y)>> { bPlaced },
            new List<IReadOnlyList<(double X, double Y)>> { sheet });
        double inA = 0; foreach (var lp in inSheet) inA += Math.Abs(RobustPolygon2D.SignedArea(lp));
        Assert(Math.Abs(inA - 1.0) < 1e-6, $"placed B (area 1) must lie inside the sheet, got {inA}");
    }

    public static void InflateLoops_GrowsAndShrinks()
    {
        var sq = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (0, 10) };
        var grown = Clipper2Adapter.InflateLoops(
            new List<IReadOnlyList<(double X, double Y)>> { sq }, 1.0);
        double ga = 0; foreach (var l in grown) ga += Math.Abs(RobustPolygon2D.SignedArea(l));
        Assert(ga > 100.0, $"inflate should grow area beyond 100, got {ga}");
        var shrunk = Clipper2Adapter.InflateLoops(
            new List<IReadOnlyList<(double X, double Y)>> { sq }, -1.0);
        double sa = 0; foreach (var l in shrunk) sa += Math.Abs(RobustPolygon2D.SignedArea(l));
        Assert(sa < 100.0 && sa > 0.0, $"erode should shrink area below 100, got {sa}");
    }

    public static void Union_TwoOverlappingSquares_ReturnsSevenAreaShape()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = Clipper2Adapter.Union(a, b);
        Assert(result.Count == 1, $"expected 1 loop, got {result.Count}");
        double area = Math.Abs(RobustPolygon2D.SignedArea(result[0]));
        Assert(Math.Abs(area - 7.0) < 1e-9, $"area expected 7, got {area}");
    }

    public static void Difference_OverlappingSquares_ReturnsThreeArea()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = Clipper2Adapter.Difference(a, b);
        Assert(result.Count == 1, $"expected 1 loop, got {result.Count}");
        double area = Math.Abs(RobustPolygon2D.SignedArea(result[0]));
        Assert(Math.Abs(area - 3.0) < 1e-9, $"area expected 3, got {area}");
    }

    public static void Xor_OverlappingSquares_ReturnsSixAreaTwoLoops()
    {
        // XOR = (A ∪ B) - (A ∩ B). Total 7 - 1 = 6, in two L-shaped pieces.
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = Clipper2Adapter.Xor(a, b);
        double total = 0;
        for (int i = 0; i < result.Count; i++)
            total += Math.Abs(RobustPolygon2D.SignedArea(result[i]));
        Assert(Math.Abs(total - 6.0) < 1e-9, $"XOR area expected 6, got {total}");
    }

    // ─── The cases that broke Greiner-Hormann ──────────────────────────

    public static void Intersect_FullyCoincidentEdges_HandledRobustly()
    {
        // Two squares sharing the entire edge (1, 0) → (1, 1). GH chokes
        // on this; Clipper2 returns a clean intersection (the shared edge,
        // which has zero area but isn't an error condition).
        var a = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 0), (2, 0), (2, 1), (1, 1),
        };
        var result = Clipper2Adapter.Intersect(a, b);
        // Edge-touching with zero-area intersection: Clipper2 may return
        // an empty list (correct) or a degenerate path (also acceptable).
        // The point is it doesn't throw or hang.
        double total = 0;
        for (int i = 0; i < result.Count; i++)
            total += Math.Abs(RobustPolygon2D.SignedArea(result[i]));
        Assert(total < 1e-6, $"shared-edge intersection should have ~0 area, got {total}");
    }

    public static void Intersect_VertexOnEdgeCase_HandledRobustly()
    {
        // Triangle sharing a vertex with the edge of a square. GH's
        // alpha=0/1 handling is fragile here.
        var square = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        // Triangle whose tip is exactly on the right edge of the square.
        var triangle = new List<(double X, double Y)>
        {
            (1, 1), (3, 0), (3, 2),
        };
        var result = Clipper2Adapter.Intersect(square, triangle);
        Assert(result.Count >= 1, $"expected >= 1 loop, got {result.Count}");
        double total = 0;
        for (int i = 0; i < result.Count; i++)
            total += Math.Abs(RobustPolygon2D.SignedArea(result[i]));
        // The tip of the triangle is exactly at (1,1). The triangle edges
        // (1,1)→(3,0) and (1,1)→(3,2) cross the right edge of the square
        // at x=2. Intersection inside the square: triangle (1,1), (2,0.5),
        // (2,1.5) area = 0.5.
        Assert(total > 0.4 && total < 0.6,
            $"vertex-on-edge intersection area expected ~0.5, got {total}");
    }

    public static void Boolean_PolygonWithHole_DifferenceIsCorrect()
    {
        // Outer 4×4 square with an inner 2×2 hole. Subtract a 1×1 square
        // that overlaps the hole. Result: 4×4 minus 2×2 hole minus the
        // overlap of the 1×1 with the rest.
        var subjectWithHole = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double, double)> { (0, 0), (4, 0), (4, 4), (0, 4) },           // outer CCW
            new List<(double, double)> { (1, 1), (1, 3), (3, 3), (3, 1) },           // hole CW
        };
        var clip = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double, double)> { (0.5, 0.5), (1.5, 0.5), (1.5, 1.5), (0.5, 1.5) },
        };
        var result = Clipper2Adapter.Boolean(
            subjectWithHole, clip, ClipType.Difference);
        // Result area: outer 16 - hole 4 - clip-not-in-hole part.
        // Clip is at [0.5,1.5]² area = 1. Hole portion of clip is
        // [1, 1.5] × [1, 1.5] = 0.25, so non-hole portion = 0.75.
        // Subject area = 16 - 4 = 12. Diff = 12 - 0.75 = 11.25.
        double total = 0;
        for (int i = 0; i < result.Count; i++)
            total += RobustPolygon2D.SignedArea(result[i]);  // signed sum gives net area
        Assert(Math.Abs(total - 11.25) < 1e-6,
            $"polygon-with-hole diff expected 11.25, got {total}");
    }

    public static void NullSubject_Throws()
    {
        bool threw = false;
        try
        {
            _ = Clipper2Adapter.Intersect(null,
                new List<(double, double)> { (0, 0), (1, 0), (1, 1) });
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null subject must throw");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
