#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.ScanIngest;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// PackDiagnosticsTests — Phase F5 of the UX architecture report §7.7.C.
// Covers PerStoneOverlap, CentreOfMassInContainer, PileStability.
// Most cases need Rhino native runtime (Mesh.CreateFromBox / IsPointInside)
// and skip cleanly under FRAHAN_SKIP_NATIVE.
// =============================================================================

static class PackDiagnosticsTests
{
    // ─── PerStoneOverlap ─────────────────────────────────────────────────

    public static void PerStoneOverlap_NullInput_Throws()
    {
        try
        {
            PackDiagnostics.PerStoneOverlap(null);
            throw new Exception("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException) { /* expected */ }
    }

    public static void PerStoneOverlap_SingleStone_ZeroFraction()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1));
        var mesh = Mesh.CreateFromBox(box, 1, 1, 1);
        var result = PackDiagnostics.PerStoneOverlap(new[] { mesh });
        Assert(result.Length == 1, $"expected 1 result, got {result.Length}");
        AssertNear(result[0], 0.0, 1e-9, "single-stone overlap must be 0");
    }

    public static void PerStoneOverlap_DisjointStones_ZeroFraction()
    {
        var a = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1)), 1, 1, 1);
        var b = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(2, 3), new Interval(0, 1), new Interval(0, 1)), 1, 1, 1);
        var result = PackDiagnostics.PerStoneOverlap(new[] { a, b });
        AssertNear(result[0], 0.0, 1e-9, "disjoint a overlap must be 0");
        AssertNear(result[1], 0.0, 1e-9, "disjoint b overlap must be 0");
    }

    public static void PerStoneOverlap_FullyContainedStone_HighFraction()
    {
        var big = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 4), new Interval(0, 4), new Interval(0, 4)), 1, 1, 1);
        var small = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(1, 2), new Interval(1, 2), new Interval(1, 2)), 1, 1, 1);
        var result = PackDiagnostics.PerStoneOverlap(new[] { big, small });
        // All of `small`'s vertices lie inside `big`. Vertices on the
        // boundary of `big` would not count as inside (strict containment),
        // so all 8 small-cube verts at positions (1..2, 1..2, 1..2) are
        // strictly inside `big`.
        AssertNear(result[1], 1.0, 1e-9, "fully-contained small cube overlap must be 1.0");
        // `big`'s vertices are at the corners of (0..4)^3; none are inside
        // the strictly smaller `small` cube.
        AssertNear(result[0], 0.0, 1e-9, "big cube vs small interior must be 0");
    }

    // ─── CentreOfMassInContainer ─────────────────────────────────────────

    public static void ComCheck_NullContainer_Throws()
    {
        try
        {
            PackDiagnostics.CentreOfMassInContainer(new List<Mesh>(), null);
            throw new Exception("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException) { /* expected */ }
    }

    public static void ComCheck_StoneInsideContainer_PassesCheck()
    {
        var container = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)), 1, 1, 1);
        var stone = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(2, 3), new Interval(2, 3), new Interval(2, 3)), 1, 1, 1);
        var (inside, coms) = PackDiagnostics.CentreOfMassInContainer(new[] { stone }, container);
        Assert(inside[0], "stone inside container should be reported as inside");
        AssertNear(coms[0].X, 2.5, 1e-6, "com.x");
    }

    public static void ComCheck_StoneOutsideContainer_FailsCheck()
    {
        var container = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)), 1, 1, 1);
        var stone = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(20, 21), new Interval(0, 1), new Interval(0, 1)), 1, 1, 1);
        var (inside, _) = PackDiagnostics.CentreOfMassInContainer(new[] { stone }, container);
        Assert(!inside[0], "stone clearly outside container should fail check");
    }

    // ─── PileStability ───────────────────────────────────────────────────

    public static void PileStability_GroundedStone_IsStable()
    {
        // 1×1×1 cube sitting at z = 0..1. Floor = 0. CoM at (0.5, 0.5, 0.5)
        // lies inside its own XY footprint. Should be stable.
        var stone = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1)), 1, 1, 1);
        var (stable, falling) = PackDiagnostics.PileStability(new[] { stone });
        Assert(stable[0], "grounded centred stone should be stable");
        Assert(falling.Length == 0, $"no falling stones expected, got {falling.Length}");
    }

    public static void PileStability_StoneSupportedByAnother_IsStable()
    {
        var bottom = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 2), new Interval(0, 2), new Interval(0, 1)), 1, 1, 1);
        // Top stone sits on bottom's top face (z=1), centred over its footprint.
        var top = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0.5, 1.5), new Interval(0.5, 1.5), new Interval(1, 2)), 1, 1, 1);
        var (stable, falling) = PackDiagnostics.PileStability(new[] { bottom, top });
        Assert(stable[0] && stable[1], $"both should be stable; got bottom={stable[0]} top={stable[1]}");
        Assert(falling.Length == 0, "no falling stones expected");
    }

    public static void PileStability_FloatingStone_IsFalling()
    {
        // Floating stone with no supporter and not on the floor.
        var floating = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(5, 6)), 1, 1, 1);
        var (stable, falling) = PackDiagnostics.PileStability(new[] { floating });
        Assert(!stable[0], "floating stone with no supporter should be unstable");
        Assert(falling.Length == 1 && falling[0] == 0, $"falling list should be [0], got [{string.Join(",", falling)}]");
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} ± {tol}, got {actual}");
    }
}
