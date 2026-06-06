#nullable disable
using System;
using Frahan.Core.Quarry;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// BenchBoundaryTests — Phase G of the UX architecture report §7.8.
// Covers FromBox / FromMesh / FromBoxAndMesh constructors plus
// ContainsBoxCentre / ContainsBox containment helpers.
// =============================================================================

static class BenchBoundaryTests
{
    public static void FromMesh_NullMesh_Throws()
    {
        try
        {
            BenchBoundary.FromMesh(null);
            throw new Exception("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException) { /* expected */ }
    }

    public static void FromBox_PreservesAabb()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3), new Interval(0, 5));
        var bb = BenchBoundary.FromBox(box);
        Assert(!bb.HasMesh, "BenchBoundary.FromBox should not carry a Mesh");
        AssertNear(bb.Aabb.X.Length, 2.0, 1e-9, "x length");
        AssertNear(bb.Aabb.Y.Length, 3.0, 1e-9, "y length");
        AssertNear(bb.Aabb.Z.Length, 5.0, 1e-9, "z length");
    }

    public static void FromMesh_DerivesAabb()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 4), new Interval(0, 6), new Interval(0, 8));
        var mesh = Mesh.CreateFromBox(box, 1, 1, 1);
        var bb = BenchBoundary.FromMesh(mesh);
        Assert(bb.HasMesh, "BenchBoundary.FromMesh should carry the Mesh");
        AssertNear(bb.Aabb.X.Length, 4.0, 1e-9, "derived x length");
        AssertNear(bb.Aabb.Y.Length, 6.0, 1e-9, "derived y length");
        AssertNear(bb.Aabb.Z.Length, 8.0, 1e-9, "derived z length");
    }

    public static void ContainsBoxCentre_InsideMeshBench_ReturnsTrue()
    {
        var benchMesh = Mesh.CreateFromBox(
            new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)),
            1, 1, 1);
        var bb = BenchBoundary.FromMesh(benchMesh);
        var cell = new Box(Plane.WorldXY, new Interval(2, 3), new Interval(2, 3), new Interval(2, 3));
        Assert(bb.ContainsBoxCentre(cell),
            "centre (2.5, 2.5, 2.5) should be inside the 10×10×10 mesh bench");
    }

    public static void ContainsBoxCentre_OutsideMesh_ReturnsFalse()
    {
        var benchMesh = Mesh.CreateFromBox(
            new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)),
            1, 1, 1);
        var bb = BenchBoundary.FromMesh(benchMesh);
        // Cell centred outside the 10×10×10 bench.
        var cell = new Box(Plane.WorldXY, new Interval(20, 21), new Interval(0, 1), new Interval(0, 1));
        Assert(!bb.ContainsBoxCentre(cell),
            "centre at x=20.5 should be outside the 10×10×10 mesh bench");
    }

    public static void ContainsBox_AllCornersInside_PassesAtFullThreshold()
    {
        var benchMesh = Mesh.CreateFromBox(
            new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)),
            1, 1, 1);
        var bb = BenchBoundary.FromMesh(benchMesh);
        // Fully-inside cell.
        var cell = new Box(Plane.WorldXY, new Interval(2, 3), new Interval(2, 3), new Interval(2, 3));
        Assert(bb.ContainsBox(cell, insideFractionThreshold: 1.0),
            "fully-inside cell should pass even at threshold = 1.0");
    }

    public static void ContainsBox_NoCornersInside_FailsAtAnyThreshold()
    {
        var benchMesh = Mesh.CreateFromBox(
            new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)),
            1, 1, 1);
        var bb = BenchBoundary.FromMesh(benchMesh);
        // Cell far away.
        var cell = new Box(Plane.WorldXY, new Interval(20, 21), new Interval(20, 21), new Interval(20, 21));
        Assert(!bb.ContainsBox(cell, insideFractionThreshold: 0.5),
            "fully-outside cell should fail at threshold = 0.5");
    }

    public static void FromBoxAndMesh_NullMesh_FallsBackToFromBox()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1));
        var bb = BenchBoundary.FromBoxAndMesh(box, null);
        Assert(!bb.HasMesh, "null mesh should fall back to FromBox behaviour");
    }

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
