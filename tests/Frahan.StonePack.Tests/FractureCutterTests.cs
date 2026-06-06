#nullable disable
using System;
using Frahan.Masonry.Cutting;

namespace Frahan.Tests;

// Phase E.2 unit tests for the FractureCutter (finite-extent fracture polygon
// splitting of convex Slabs). All pure-managed; no Rhino runtime needed; should
// run as PASS on the headless host. Behavioural contract follows the public API
// in Frahan.Masonry.Cutting.FractureCutter / FracturePolygon / FractureCutOptions.

static class FractureCutterTests
{
    // Tolerances. Tol is for volume / signed-distance comparisons; LooseTol is
    // for any 2D-projection comparison where a couple of orders of magnitude of
    // floating-point slack are expected.
    private const double Tol = 1e-7;
    private const double LooseTol = 1e-4;

    // =========================================================================
    // Group A: FracturePolygon construction and validation
    // =========================================================================

    public static void FracturePolygon_NullBoundary_Throws()
    {
        bool threw = false;
        try { _ = new FracturePolygon(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null boundary should throw ArgumentNullException");
    }

    public static void FracturePolygon_TooFewVertices_Throws()
    {
        // 2 points = 6 doubles, multiple of 3 but only 2 vertices.
        bool threw = false;
        try { _ = new FracturePolygon(new[] { 0.0, 0, 0, 1.0, 0, 0 }); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "polygon with 2 vertices should throw ArgumentException");
    }

    public static void FracturePolygon_LengthNotMultipleOf3_Throws()
    {
        bool threw = false;
        try { _ = new FracturePolygon(new[] { 0.0, 0, 0, 1, 0, 0, 1 }); } // length 7
        catch (ArgumentException) { threw = true; }
        Assert(threw, "boundary length not multiple of 3 should throw ArgumentException");
    }

    public static void FracturePolygon_NonCoplanar_Throws()
    {
        // 4 points; 4th vertex offset 0.5 from XY plane => violates planarity.
        bool threw = false;
        try
        {
            _ = new FracturePolygon(new[]
            {
                0.0, 0, 0,
                1.0, 0, 0,
                1.0, 1, 0,
                0.0, 1, 0.5,   // out of plane
            });
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "non-coplanar polygon should throw ArgumentException");
    }

    public static void FracturePolygon_NonConvex_Throws()
    {
        // Concave at vertex (0.5, 0.1): going CCW around (0,0)->(1,0)->(0.5,0.1)->(0,1)
        // creates a cross-product sign flip relative to the surrounding edges.
        bool threw = false;
        try
        {
            _ = new FracturePolygon(new[]
            {
                0.0, 0,   0,
                1.0, 0,   0,
                0.5, 0.1, 0,
                0.0, 1,   0,
            });
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "non-convex polygon should throw ArgumentException");
    }

    public static void FracturePolygon_ValidRectangle_StoresPlaneAndVerts()
    {
        var poly = MakeXyRect(0, 1, 0, 1, 0.5);

        Assert(poly.VertexCount == 4, $"expected 4 vertices, got {poly.VertexCount}");

        // The plane normal should point along +/- Z (allow either orientation
        // since RectangleXY's CCW from +Z view yields +Z, but be tolerant in
        // case the derivation flipped it).
        var p = poly.SupportingPlane;
        Assert(Math.Abs(p.NormalX) < 1e-6, $"NormalX expected ~0, got {p.NormalX}");
        Assert(Math.Abs(p.NormalY) < 1e-6, $"NormalY expected ~0, got {p.NormalY}");
        Assert(Math.Abs(Math.Abs(p.NormalZ) - 1.0) < 1e-6,
            $"|NormalZ| expected ~1.0, got {p.NormalZ}");
    }

    // =========================================================================
    // Group B: SlabCrossSection
    // =========================================================================

    public static void SlabCrossSection_PlaneMisses_ReturnsEmpty()
    {
        var cube = MakeUnitCube();
        var plane = new FracturePlane(0, 0, 2.0, 0, 0, 1);

        var xs = SlabCrossSection.Compute(cube, plane);

        Assert(xs != null, "result should not be null");
        Assert(xs.Length == 0, $"expected empty cross-section, got length {xs.Length}");
    }

    public static void SlabCrossSection_AxisAlignedBisector_ReturnsUnitSquare()
    {
        var cube = MakeUnitCube();
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);

        var xs = SlabCrossSection.Compute(cube, plane);

        Assert(xs.Length == 12, $"expected 4 vertices (12 doubles), got {xs.Length}");

        double sumX = 0.0;
        for (int i = 0; i < 4; i++)
        {
            sumX += xs[3 * i];
            double z = xs[3 * i + 2];
            Assert(Math.Abs(z - 0.5) < Tol, $"vertex {i} z expected 0.5, got {z}");
        }
        // Centroid x = 0.5 => sum of 4 x-values ~ 2.0.
        Assert(Math.Abs(sumX - 2.0) < Tol, $"sum of x expected ~2.0, got {sumX}");
    }

    public static void SlabCrossSection_DiagonalBisector_ReturnsValidPolygon()
    {
        var cube = MakeUnitCube();
        // Plane through cube centroid with normal (1,1,0) (constructor normalises).
        var plane = new FracturePlane(0.5, 0.5, 0.5, 1, 1, 0);

        var xs = SlabCrossSection.Compute(cube, plane);

        int n = xs.Length / 3;
        Assert(n == 4, $"expected 4 vertices on diagonal cross-section, got {n}");

        for (int i = 0; i < n; i++)
        {
            double d = plane.SignedDistance(xs[3 * i], xs[3 * i + 1], xs[3 * i + 2]);
            Assert(Math.Abs(d) <= 1e-6,
                $"vertex {i} signed distance expected ~0, got {d}");
        }
    }

    public static void SlabCrossSection_NegativeEps_Throws()
    {
        var cube = MakeUnitCube();
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);

        bool threw = false;
        try { _ = SlabCrossSection.Compute(cube, plane, eps: -1e-9); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative eps should throw ArgumentOutOfRangeException");
    }

    // =========================================================================
    // Group C: FractureCutter — Spans
    // =========================================================================

    public static void FractureCut_RectangleLargerThanSlab_OutcomeIsSpansAndProducesTwoHalves()
    {
        var cube = MakeUnitCube();
        // 3x3 rectangle at z=0.5 fully containing the unit-square cross-section.
        var poly = MakeXyRect(-1, 2, -1, 2, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Spans,
            $"expected Spans, got {result.Outcome}");
        Assert(result.Slabs.Count == 2,
            $"expected 2 output slabs, got {result.Slabs.Count}");

        double total = 0.0;
        for (int i = 0; i < result.Slabs.Count; i++)
        {
            double v = result.Slabs[i].SignedVolume();
            Assert(Math.Abs(v - 0.5) < Tol,
                $"piece {i} volume expected ~0.5, got {v}");
            total += v;
        }
        Assert(Math.Abs(total - 1.0) < Tol,
            $"total volume expected ~1.0, got {total}");
    }

    public static void FractureCut_RectangleExactlyMatchingCrossSection_OutcomeIsSpans()
    {
        var cube = MakeUnitCube();
        // Rectangle exactly matching the unit cross-section at z=0.5.
        var poly = MakeXyRect(0, 1, 0, 1, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Spans,
            $"exact-match polygon expected Spans (within default ContainmentTolerance), got {result.Outcome}");
    }

    public static void FractureCut_DiagonalLargePolygon_OutcomeIsSpans()
    {
        var cube = MakeUnitCube();
        // Big horizontal rectangle at z=0.5; the cross-section is the unit
        // square in XY at z=0.5, fully contained.
        var poly = MakeXyRect(-5, 5, -5, 5, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Spans,
            $"large-containing polygon expected Spans, got {result.Outcome}");
        Assert(result.Slabs.Count == 2,
            $"expected 2 output slabs, got {result.Slabs.Count}");
    }

    public static void FractureCut_SpansResult_VolumeConserved()
    {
        var cube = MakeUnitCube();
        var poly = MakeXyRect(-1, 2, -1, 2, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        double total = 0.0;
        for (int i = 0; i < result.Slabs.Count; i++)
            total += result.Slabs[i].SignedVolume();

        Assert(Math.Abs(total - 1.0) < Tol,
            $"Spans result total volume expected ~1.0, got {total}");
    }

    // =========================================================================
    // Group D: FractureCutter — Miss
    // =========================================================================

    public static void FractureCut_PlaneAboveSlab_OutcomeIsMissAndPassthrough()
    {
        var cube = MakeUnitCube();
        // Plane at z=2.0 sits well above the cube; cross-section is empty.
        var poly = MakeXyRect(-5, 5, -5, 5, 2.0);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Miss,
            $"expected Miss, got {result.Outcome}");
        Assert(result.Slabs.Count == 1,
            $"expected 1 passthrough slab, got {result.Slabs.Count}");

        var piece = result.Slabs[0];
        Assert(piece.VertexCount == cube.VertexCount,
            $"passthrough VertexCount expected {cube.VertexCount}, got {piece.VertexCount}");
        Assert(piece.FaceCount == cube.FaceCount,
            $"passthrough FaceCount expected {cube.FaceCount}, got {piece.FaceCount}");
    }

    public static void FractureCut_MissResult_PreservesSlab()
    {
        var cube = MakeUnitCube();
        var poly = MakeXyRect(-5, 5, -5, 5, 2.0);

        var result = FractureCutter.Cut(cube, poly);

        double v = result.Slabs[0].SignedVolume();
        Assert(Math.Abs(v - 1.0) < Tol,
            $"Miss passthrough volume expected ~1.0, got {v}");
    }

    public static void FractureCut_PlaneBelowSlab_OutcomeIsMiss()
    {
        var cube = MakeUnitCube();
        // Plane at z=-1 sits below the cube; cross-section is empty.
        var poly = MakeXyRect(-5, 5, -5, 5, -1.0);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Miss,
            $"plane below slab expected Miss, got {result.Outcome}");
        Assert(result.Slabs.Count == 1,
            $"expected 1 passthrough slab, got {result.Slabs.Count}");
    }

    // =========================================================================
    // Group E: FractureCutter — Partial
    // =========================================================================

    public static void FractureCut_SmallRectangle_OutcomeIsPartialAndPassthrough()
    {
        var cube = MakeUnitCube();
        // 0.2x0.2 rectangle inside the unit cross-section: polygon is smaller,
        // cross-section corners fall outside the polygon => Partial.
        var poly = MakeXyRect(0.2, 0.4, 0.2, 0.4, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Partial,
            $"expected Partial, got {result.Outcome}");
        Assert(result.Slabs.Count == 1,
            $"expected 1 passthrough slab, got {result.Slabs.Count}");
    }

    public static void FractureCut_PartialResult_PreservesSlabVolume()
    {
        var cube = MakeUnitCube();
        var poly = MakeXyRect(0.2, 0.4, 0.2, 0.4, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        double v = result.Slabs[0].SignedVolume();
        Assert(Math.Abs(v - 1.0) < Tol,
            $"Partial passthrough volume expected ~1.0, got {v}");
    }

    public static void FractureCut_OffsetRectangle_OutcomeIsPartial()
    {
        var cube = MakeUnitCube();
        // Polygon overlaps only the +X+Y quadrant of the unit-square cross-section.
        var poly = MakeXyRect(0.5, 1.5, 0.5, 1.5, 0.5);

        var result = FractureCutter.Cut(cube, poly);

        Assert(result.Outcome == FractureCutOutcome.Partial,
            $"offset polygon expected Partial, got {result.Outcome}");
    }

    public static void FractureCut_PartialExtended_OutcomeIsPartialExtendedAndProducesTwoHalves()
    {
        var cube = MakeUnitCube();
        var poly = MakeXyRect(0.2, 0.4, 0.2, 0.4, 0.5);
        var options = new FractureCutOptions(extendPartialToInfinitePlane: true);

        var result = FractureCutter.Cut(cube, poly, options);

        Assert(result.Outcome == FractureCutOutcome.PartialExtended,
            $"expected PartialExtended, got {result.Outcome}");
        Assert(result.Slabs.Count == 2,
            $"expected 2 output slabs, got {result.Slabs.Count}");

        double total = 0.0;
        for (int i = 0; i < result.Slabs.Count; i++)
            total += result.Slabs[i].SignedVolume();

        Assert(Math.Abs(total - 1.0) < Tol,
            $"PartialExtended total volume expected ~1.0, got {total}");
    }

    // =========================================================================
    // Group F: Argument validation
    // =========================================================================

    public static void FractureCut_NullSlab_Throws()
    {
        var poly = MakeXyRect(-1, 2, -1, 2, 0.5);
        bool threw = false;
        try { _ = FractureCutter.Cut(null, poly); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null slab should throw ArgumentNullException");
    }

    public static void FractureCut_NullPolygon_Throws()
    {
        var cube = MakeUnitCube();
        bool threw = false;
        try { _ = FractureCutter.Cut(cube, null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null polygon should throw ArgumentNullException");
    }

    public static void FractureCut_NullOptions_UsesDefault()
    {
        var cube = MakeUnitCube();
        var poly = MakeXyRect(-1, 2, -1, 2, 0.5);

        // Passing null options should fall back to FractureCutOptions.Default.
        var result = FractureCutter.Cut(cube, poly, options: null);

        Assert(result.Outcome == FractureCutOutcome.Spans,
            $"null options should default to Spans-capable behaviour, got {result.Outcome}");
        Assert(result.Slabs.Count == 2,
            $"null options expected 2 output slabs, got {result.Slabs.Count}");
    }

    // =========================================================================
    // Group G: CutMany / multi-fracture
    // =========================================================================

    public static void FractureCutMany_TwoOrthogonalRectangles_ProducesFourPieces()
    {
        var cube = MakeUnitCube();

        // Polygon 1: large horizontal rect at z=0.5 (+Z normal).
        var polyZ = MakeXyRect(-5, 5, -5, 5, 0.5);

        // Polygon 2: large vertical rect at x=0.5 with +X normal. Vertices CCW
        // from +X view: (0.5, -5, -5) -> (0.5, 5, -5) -> (0.5, 5, 5) -> (0.5, -5, 5).
        var polyX = new FracturePolygon(new[]
        {
            0.5, -5.0, -5.0,
            0.5,  5.0, -5.0,
            0.5,  5.0,  5.0,
            0.5, -5.0,  5.0,
        });

        var pieces = FractureCutter.CutMany(
            new[] { cube },
            new[] { polyZ, polyX });

        Assert(pieces.Count == 4,
            $"expected 4 pieces after two orthogonal cuts, got {pieces.Count}");

        double total = 0.0;
        for (int i = 0; i < pieces.Count; i++)
            total += pieces[i].SignedVolume();

        Assert(Math.Abs(total - 1.0) < Tol,
            $"CutMany total volume expected ~1.0, got {total}");
    }

    public static void FractureCutMany_NullSlabsList_Throws()
    {
        var poly = MakeXyRect(-1, 2, -1, 2, 0.5);
        bool threw = false;
        try { _ = FractureCutter.CutMany(null, new[] { poly }); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null slabs list should throw ArgumentNullException");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static Slab MakeUnitCube()
    {
        return Slab.Box(0, 0, 0, 1, 1, 1);
    }

    private static Slab MakeCenteredCube(double half)
    {
        return Slab.Box(-half, -half, -half, half, half, half);
    }

    private static FracturePolygon MakeXyRect(double minX, double maxX, double minY, double maxY, double z)
    {
        return FracturePolygon.RectangleXY(minX, maxX, minY, maxY, z);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
