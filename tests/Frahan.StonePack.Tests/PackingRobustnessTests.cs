#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// PackingRobustnessTests — Phase 3 of the robustness pass.
// =============================================================================

static class PackingRobustnessTests
{
    // ─── RobustPolygon2D ───────────────────────────────────────────────

    public static void Polygon_SignedArea_UnitSquare_IsOne()
    {
        var sq = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        Assert(Math.Abs(RobustPolygon2D.SignedArea(sq) - 1.0) < 1e-12,
            $"unit-square area {RobustPolygon2D.SignedArea(sq)}");
    }

    public static void Polygon_Sanitize_DropsAdjacentDuplicates()
    {
        var sq = new List<(double X, double Y)>
        {
            (0, 0), (0, 0), (1, 0), (1, 1), (1, 1), (0, 1),
        };
        var clean = RobustPolygon2D.Sanitize(sq, 1e-9, 1e-9);
        Assert(clean.Count == 4, $"after dedup count {clean.Count}");
    }

    public static void Polygon_Sanitize_DropsCollinearMidpoint()
    {
        // Square with an extra point on one edge.
        var sq = new List<(double X, double Y)>
        {
            (0, 0), (0.5, 0), (1, 0), (1, 1), (0, 1),
        };
        var clean = RobustPolygon2D.Sanitize(sq, 1e-9, 1e-9);
        Assert(clean.Count == 4, $"after collinear drop count {clean.Count}");
    }

    public static void Clip_FullInside_ReturnsSubject()
    {
        var subject = new List<(double X, double Y)>
        {
            (0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75),
        };
        var clip = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var result = RobustPolygon2D.SutherlandHodgmanClip(subject, clip);
        Assert(result.Count == 4, $"full-inside expected 4 verts, got {result.Count}");
        Assert(Math.Abs(RobustPolygon2D.Area(result) - 0.25) < 1e-9,
            $"area {RobustPolygon2D.Area(result)}");
    }

    public static void Clip_PartialOverlap_ReturnsIntersection()
    {
        var subject = new List<(double X, double Y)>
        {
            (0.5, 0.5), (1.5, 0.5), (1.5, 1.5), (0.5, 1.5),
        };
        var clip = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var result = RobustPolygon2D.SutherlandHodgmanClip(subject, clip);
        // Intersection is the (0.5, 0.5) → (1, 1) square — area 0.25.
        Assert(Math.Abs(RobustPolygon2D.Area(result) - 0.25) < 1e-9,
            $"partial overlap area {RobustPolygon2D.Area(result)}");
    }

    public static void Clip_Disjoint_ReturnsEmpty()
    {
        var subject = new List<(double X, double Y)>
        {
            (5, 5), (6, 5), (6, 6), (5, 6),
        };
        var clip = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        var result = RobustPolygon2D.SutherlandHodgmanClip(subject, clip);
        Assert(result.Count == 0,
            $"disjoint expected 0 verts, got {result.Count}");
    }

    // ─── MeshPlanarPolygonExtractor ────────────────────────────────────

    public static void Extract_SimpleQuad_FindsOuterLoop()
    {
        // 2x2 grid → single quad (split into 2 tris). Boundary = 4 edges.
        var verts = new double[]
        {
            0, 0, 0,
            1, 0, 0,
            1, 1, 0,
            0, 1, 0,
        };
        var tris = new int[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new MeshSnapshot(verts, tris);
        var res = MeshPlanarPolygonExtractor.Extract(mesh,
            0, 0, 0,    // origin
            1, 0, 0,    // u
            0, 1, 0);   // v
        Assert(res.HasOuter, "outer expected");
        Assert(res.HoleCount == 0, $"hole count {res.HoleCount}");
        Assert(Math.Abs(RobustPolygon2D.Area(res.Outer) - 1.0) < 1e-9,
            $"outer area {RobustPolygon2D.Area(res.Outer)}");
        Assert(RobustPolygon2D.SignedArea(res.Outer) > 0,
            "outer must be CCW");
    }

    public static void Extract_AnnulusMesh_FindsOuterAndOneHole()
    {
        // Outer 2x2 quad, inner 1x1 hole in the middle. Build an "annulus"
        // by triangulating the gap between the two squares.
        // Verts: 4 outer + 4 inner.
        var verts = new double[]
        {
            // outer (CCW)
            -2, -2, 0,
             2, -2, 0,
             2,  2, 0,
            -2,  2, 0,
            // inner (CCW)
            -1, -1, 0,
             1, -1, 0,
             1,  1, 0,
            -1,  1, 0,
        };
        // 8 quads → 16 triangles (one band of 4 quads on each side).
        var tris = new int[]
        {
            // bottom band
            0, 1, 5,  0, 5, 4,
            // right band
            1, 2, 6,  1, 6, 5,
            // top band
            2, 3, 7,  2, 7, 6,
            // left band
            3, 0, 4,  3, 4, 7,
        };
        var mesh = new MeshSnapshot(verts, tris);
        var res = MeshPlanarPolygonExtractor.Extract(mesh,
            0, 0, 0, 1, 0, 0, 0, 1, 0);
        Assert(res.HasOuter, "outer expected");
        Assert(res.HoleCount == 1, $"hole count {res.HoleCount}");
        Assert(Math.Abs(RobustPolygon2D.Area(res.Outer) - 16.0) < 1e-9,
            $"outer area {RobustPolygon2D.Area(res.Outer)}");
        Assert(Math.Abs(RobustPolygon2D.Area(res.Holes[0]) - 4.0) < 1e-9,
            $"hole area {RobustPolygon2D.Area(res.Holes[0])}");
        Assert(RobustPolygon2D.SignedArea(res.Holes[0]) < 0,
            "hole must be CW");
    }

    // ─── GH metadata ───────────────────────────────────────────────────

    public static void Gh_PolygonSanitizeComponent_Metadata()
    {
        var c = new PolygonSanitizeComponent();
        Assert(c.ComponentGuid == new Guid("F2D000B1-CADC-4F2D-A0B1-7E60CADA15A0"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.Params.Input.Count == 4, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 3, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_MeshPlanarPolygonExtractorComponent_Metadata()
    {
        var c = new MeshPlanarPolygonExtractorComponent();
        Assert(c.ComponentGuid == new Guid("F2D000B2-CADC-4F2D-A0B2-7E60CADA15A0"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.Params.Input.Count == 2, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
