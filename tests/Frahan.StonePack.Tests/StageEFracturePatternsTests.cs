#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Tests;

// =============================================================================
// StageEFracturePatternsTests — Layered / Radial / BrickPattern / JitteredGrid /
// FilterToBox + GH smoke tests for the five new fracture-pattern components.
// =============================================================================

static class StageEFracturePatternsTests
{
    private static readonly BoundingBox3 UnitBox = new BoundingBox3(0, 0, 0, 1, 1, 1);

    // ─── Layered ─────────────────────────────────────────────────────────────

    public static void Layered_AxisX_ReturnsExpectedCount()
    {
        var planes = FracturePlaneGenerators.Layered(UnitBox, axis: 0, count: 3);
        Assert(planes.Count == 3, $"expected 3, got {planes.Count}");
        Assert(Math.Abs(planes[0].NormalX - 1) < 1e-12, "normal must be +X");
        Assert(Math.Abs(planes[0].PointX - 0.25) < 1e-12, $"plane 0 X expected 0.25, got {planes[0].PointX}");
    }

    public static void Layered_AxisOutOfRange_Throws()
    {
        bool threw = false;
        try { _ = FracturePlaneGenerators.Layered(UnitBox, axis: 3, count: 1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "axis=3 should throw");
    }

    // ─── Radial ──────────────────────────────────────────────────────────────

    public static void Radial_FourPlanes_AreOrthogonalToAxis()
    {
        var planes = FracturePlaneGenerators.Radial(0, 0, 0, 0, 0, 1, 4);
        Assert(planes.Count == 4, $"expected 4, got {planes.Count}");
        for (int i = 0; i < planes.Count; i++)
        {
            double dot = planes[i].NormalX * 0 + planes[i].NormalY * 0 + planes[i].NormalZ * 1;
            Assert(Math.Abs(dot) < 1e-9, $"plane {i} normal not perpendicular to +Z (dot={dot})");
        }
    }

    public static void Radial_ZeroAxis_Throws()
    {
        bool threw = false;
        try { _ = FracturePlaneGenerators.Radial(0, 0, 0, 0, 0, 0, 1); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "zero axis should throw");
    }

    public static void Radial_CountZero_ReturnsEmpty()
    {
        var planes = FracturePlaneGenerators.Radial(0, 0, 0, 0, 0, 1, 0);
        Assert(planes.Count == 0, $"expected 0, got {planes.Count}");
    }

    // ─── BrickPattern ────────────────────────────────────────────────────────

    public static void BrickPattern_OnlyHorizontals_ReturnsZParallelOnly()
    {
        var planes = FracturePlaneGenerators.BrickPattern(UnitBox, nX: 0, nZ: 2);
        Assert(planes.Count == 2, $"expected 2 horizontals, got {planes.Count}");
        for (int i = 0; i < planes.Count; i++)
        {
            Assert(Math.Abs(planes[i].NormalZ - 1) < 1e-9,
                $"plane {i} normal expected +Z, got ({planes[i].NormalX},{planes[i].NormalY},{planes[i].NormalZ})");
        }
    }

    public static void BrickPattern_HasBothOrientations()
    {
        var planes = FracturePlaneGenerators.BrickPattern(UnitBox, nX: 2, nZ: 1);
        bool sawX = false, sawZ = false;
        for (int i = 0; i < planes.Count; i++)
        {
            if (Math.Abs(planes[i].NormalX - 1) < 1e-9) sawX = true;
            if (Math.Abs(planes[i].NormalZ - 1) < 1e-9) sawZ = true;
        }
        Assert(sawX && sawZ, "expected both X-orthogonal and Z-orthogonal planes");
    }

    public static void BrickPattern_NegativeCount_Throws()
    {
        bool threw = false;
        try { _ = FracturePlaneGenerators.BrickPattern(UnitBox, nX: -1, nZ: 1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative nX should throw");
    }

    // ─── JitteredGrid ────────────────────────────────────────────────────────

    public static void JitteredGrid_DeterministicForSeed()
    {
        var p1 = FracturePlaneGenerators.JitteredGrid(UnitBox, 2, 0, 2, jitter: 0.2, seed: 7);
        var p2 = FracturePlaneGenerators.JitteredGrid(UnitBox, 2, 0, 2, jitter: 0.2, seed: 7);
        Assert(p1.Count == p2.Count, $"counts differ: {p1.Count} vs {p2.Count}");
        for (int i = 0; i < p1.Count; i++)
        {
            Assert(Math.Abs(p1[i].PointX - p2[i].PointX) < 1e-12,
                $"plane {i} PointX mismatch");
        }
    }

    public static void JitteredGrid_JitterOutOfRange_Throws()
    {
        bool threw = false;
        try { _ = FracturePlaneGenerators.JitteredGrid(UnitBox, 1, 0, 0, jitter: 0.6, seed: 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "jitter >= 0.5 should throw");
    }

    // ─── FilterToBox ─────────────────────────────────────────────────────────

    public static void Filter_KeepsIntersectingPlanes_DropsOutsidePlanes()
    {
        var inside = new FracturePlane(0.5, 0.5, 0.5, 1, 0, 0);     // crosses the box
        var outside = new FracturePlane(10, 0, 0, 1, 0, 0);          // miles away
        var planes = new List<FracturePlane> { inside, outside };
        var filtered = FracturePlaneGenerators.FilterToBox(planes, UnitBox);
        Assert(filtered.Count == 1, $"expected 1 kept, got {filtered.Count}");
        Assert(ReferenceEquals(filtered[0], inside), "should keep the inside plane");
    }

    public static void Filter_OnFace_KeepsPlane()
    {
        // Plane exactly on the +Z face of the unit box.
        var onFace = new FracturePlane(0.5, 0.5, 1.0, 0, 0, 1);
        var filtered = FracturePlaneGenerators.FilterToBox(
            new List<FracturePlane> { onFace }, UnitBox);
        Assert(filtered.Count == 1, "plane on a box face should be kept");
    }

    public static void Filter_NullPlanes_Throws()
    {
        bool threw = false;
        try { _ = FracturePlaneGenerators.FilterToBox(null, UnitBox); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null planes should throw ArgumentNullException");
    }

    // ─── GH component smoke tests ────────────────────────────────────────────

    public static void Gh_LayeredFracturePlanes_Metadata()
    {
        var c = new LayeredFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("F8A9CADB-ECFD-4345-6789-012345678ABC"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Name == "Layered Fracture Planes", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan" && c.SubCategory == "Fracture",
            $"tab: {c.Category}/{c.SubCategory}");
        Assert(c.Params.Input.Count == 3 && c.Params.Output.Count == 1,
            $"I/O counts: {c.Params.Input.Count}/{c.Params.Output.Count}");
    }

    public static void Gh_RadialFracturePlanes_Metadata()
    {
        var c = new RadialFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("A9CADBEC-FDAE-4456-789A-012345678BCD"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Params.Input.Count == 3 && c.Params.Output.Count == 1,
            $"I/O counts: {c.Params.Input.Count}/{c.Params.Output.Count}");
    }

    public static void Gh_BrickPatternFracturePlanes_Metadata()
    {
        var c = new BrickPatternFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("BADBECFD-AEBF-4567-89AB-CDEF01234567"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Params.Input.Count == 3 && c.Params.Output.Count == 1,
            $"I/O counts: {c.Params.Input.Count}/{c.Params.Output.Count}");
    }

    public static void Gh_JitteredGridFracturePlanes_Metadata()
    {
        var c = new JitteredGridFracturePlanesComponent();
        Assert(c.ComponentGuid == new Guid("CBECFDAE-BFCA-4678-9ABC-DEF012345678"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Params.Input.Count == 6 && c.Params.Output.Count == 1,
            $"I/O counts: {c.Params.Input.Count}/{c.Params.Output.Count}");
    }

    public static void Gh_FracturePlaneFilter_Metadata()
    {
        var c = new FracturePlaneFilterComponent();
        Assert(c.ComponentGuid == new Guid("DCFDAEBF-CADB-4789-ABCD-EF0123456789"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Params.Input.Count == 2 && c.Params.Output.Count == 1,
            $"I/O counts: {c.Params.Input.Count}/{c.Params.Output.Count}");
    }

    public static void Gh_StageE_AllGuidsUnique()
    {
        var ids = new[]
        {
            new Guid("F8A9CADB-ECFD-4345-6789-012345678ABC"),  // Layered
            new Guid("A9CADBEC-FDAE-4456-789A-012345678BCD"),  // Radial
            new Guid("BADBECFD-AEBF-4567-89AB-CDEF01234567"),  // Brick
            new Guid("CBECFDAE-BFCA-4678-9ABC-DEF012345678"),  // JitteredGrid
            new Guid("DCFDAEBF-CADB-4789-ABCD-EF0123456789"),  // Filter
        };
        for (int i = 0; i < ids.Length; i++)
            for (int j = i + 1; j < ids.Length; j++)
                Assert(ids[i] != ids[j], $"GUID collision at ids[{i}] == ids[{j}]");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
