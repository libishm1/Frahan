#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// GreinerHormannClipperTests — non-convex polygon Boolean ops.
// =============================================================================

static class GreinerHormannClipperTests
{
    // ─── Basic intersection ────────────────────────────────────────────

    public static void Intersection_TwoSquaresOverlap_ReturnsOverlap()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Intersection);
        Assert(result.Count == 1, $"expected 1 loop, got {result.Count}");
        double area = RobustPolygon2D.Area(result[0]);
        Assert(Math.Abs(area - 1.0) < 1e-6,
            $"intersection area expected 1.0, got {area}");
    }

    public static void Intersection_DisjointSquares_ReturnsEmpty()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var b = new List<(double X, double Y)>
        {
            (5, 5), (6, 5), (6, 6), (5, 6),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Intersection);
        Assert(result.Count == 0, $"disjoint expected 0, got {result.Count}");
    }

    public static void Intersection_SubjectFullyInside_ReturnsSubject()
    {
        var a = new List<(double X, double Y)>
        {
            (0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75),
        };
        var b = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Intersection);
        Assert(result.Count == 1, $"contained subject expected 1 loop, got {result.Count}");
        double area = RobustPolygon2D.Area(result[0]);
        Assert(Math.Abs(area - 0.25) < 1e-6, $"area {area}");
    }

    // ─── Union ──────────────────────────────────────────────────────────

    public static void Union_OverlappingSquares_ReturnsLShape()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Union);
        Assert(result.Count == 1, $"expected 1 loop, got {result.Count}");
        // Union area = a + b - intersection = 4 + 4 - 1 = 7.
        double area = RobustPolygon2D.Area(result[0]);
        Assert(Math.Abs(area - 7.0) < 1e-6, $"union area expected 7, got {area}");
    }

    public static void Union_DisjointSquares_ReturnsBoth()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var b = new List<(double X, double Y)>
        {
            (5, 5), (6, 5), (6, 6), (5, 6),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Union);
        Assert(result.Count == 2, $"disjoint union expected 2, got {result.Count}");
    }

    // ─── Difference ────────────────────────────────────────────────────

    public static void Difference_OverlappingSquares_ReturnsSubjectMinusOverlap()
    {
        var a = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 2), (0, 2),
        };
        var b = new List<(double X, double Y)>
        {
            (1, 1), (3, 1), (3, 3), (1, 3),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Difference);
        Assert(result.Count == 1, $"expected 1 loop, got {result.Count}");
        // a - intersection = 4 - 1 = 3.
        double area = RobustPolygon2D.Area(result[0]);
        Assert(Math.Abs(area - 3.0) < 1e-6, $"diff area expected 3, got {area}");
    }

    public static void Difference_SubjectInsideClip_ReturnsEmpty()
    {
        var a = new List<(double X, double Y)>
        {
            (0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75),
        };
        var b = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Difference);
        Assert(result.Count == 0, $"contained subject - clip expected 0, got {result.Count}");
    }

    // ─── Non-convex clipping ───────────────────────────────────────────

    public static void Intersection_LShapeAndSquare_HandlesNonConvex()
    {
        // L-shape: (0,0)->(2,0)->(2,1)->(1,1)->(1,2)->(0,2)
        var lshape = new List<(double X, double Y)>
        {
            (0, 0), (2, 0), (2, 1), (1, 1), (1, 2), (0, 2),
        };
        // Unit square at (0.5, 0.5) → (1.5, 1.5).
        var square = new List<(double X, double Y)>
        {
            (0.5, 0.5), (1.5, 0.5), (1.5, 1.5), (0.5, 1.5),
        };
        var result = GreinerHormannClipper.Compute(lshape, square, BooleanOp.Intersection);
        Assert(result.Count >= 1, $"expected >= 1 loop, got {result.Count}");
        // Intersection should equal the L-shaped region of the square: a
        // 1x0.5 strip on the bottom plus a 0.5x0.5 square on the upper left.
        // Area = 0.5 + 0.25 = 0.75.
        double totalArea = 0;
        for (int i = 0; i < result.Count; i++)
            totalArea += RobustPolygon2D.Area(result[i]);
        Assert(Math.Abs(totalArea - 0.75) < 1e-6,
            $"L-square intersection area expected 0.75, got {totalArea}");
    }

    // ─── Argument validation ──────────────────────────────────────────

    public static void Compute_NullSubject_Throws()
    {
        bool threw = false;
        try
        {
            _ = GreinerHormannClipper.Compute(null,
                new List<(double, double)> { (0, 0), (1, 0), (1, 1) },
                BooleanOp.Intersection);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null subject must throw");
    }

    public static void Compute_TooFewVertices_ReturnsEmpty()
    {
        var a = new List<(double X, double Y)> { (0, 0), (1, 0) };
        var b = new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1) };
        var result = GreinerHormannClipper.Compute(a, b, BooleanOp.Intersection);
        Assert(result.Count == 0, "polygon with < 3 verts should give empty");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
