#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Quarry;

namespace Frahan.Tests;

// =============================================================================
// JointSetDfnTests — joint-set construction + DFN generation per Priest 1993
// / ISRM Suggested Methods. The geomechanically faithful replacement for the
// older "grid cut" Quarry Decompose path.
// =============================================================================

static class JointSetDfnTests
{
    private static readonly BoundingBox3 UnitBox = new BoundingBox3(0, 0, 0, 1, 1, 1);

    // ─── JointSet construction ──────────────────────────────────────────────

    public static void JointSet_VerticalNorthDip_NormalIsNorth()
    {
        // dipDir = 0 (North), dip = 90 (vertical) → normal points North (+Y).
        var js = new JointSet(dipDirectionDeg: 0, dipDeg: 90, meanSpacing: 0.5);
        Assert(Math.Abs(js.NormalX) < 1e-9, $"NormalX expected ~0, got {js.NormalX}");
        Assert(Math.Abs(js.NormalY - 1.0) < 1e-9, $"NormalY expected 1, got {js.NormalY}");
        Assert(Math.Abs(js.NormalZ) < 1e-9, $"NormalZ expected ~0, got {js.NormalZ}");
    }

    public static void JointSet_HorizontalDip_NormalIsUp()
    {
        // dip = 0 → horizontal joint → normal +Z.
        var js = new JointSet(dipDirectionDeg: 0, dipDeg: 0, meanSpacing: 0.5);
        Assert(Math.Abs(js.NormalZ - 1.0) < 1e-9, $"NormalZ expected 1, got {js.NormalZ}");
    }

    public static void JointSet_VerticalEastDip_NormalIsEast()
    {
        // dipDir = 90 (East), dip = 90 → normal points East (+X).
        var js = new JointSet(dipDirectionDeg: 90, dipDeg: 90, meanSpacing: 0.5);
        Assert(Math.Abs(js.NormalX - 1.0) < 1e-9, $"NormalX expected 1, got {js.NormalX}");
        Assert(Math.Abs(js.NormalY) < 1e-9, $"NormalY expected ~0, got {js.NormalY}");
    }

    public static void JointSet_NegativeSpacing_Throws()
    {
        bool threw = false;
        try { _ = new JointSet(0, 90, -0.1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative spacing should throw");
    }

    public static void JointSet_OutOfRangeDipDirection_Throws()
    {
        bool threw = false;
        try { _ = new JointSet(360, 90, 0.5); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "dipDir = 360 should throw (range is [0, 360))");
    }

    public static void JointSet_OutOfRangeDip_Throws()
    {
        bool threw = false;
        try { _ = new JointSet(0, 91, 0.5); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "dip > 90 should throw");
    }

    // ─── DFN generation ─────────────────────────────────────────────────────

    public static void Dfn_OneVerticalSet_SpacingMatchesGridCount()
    {
        // Single vertical East-West joint set with spacing 0.25 over a unit box
        // along +Y. Expected: ~4 planes (box extent 1.0 / spacing 0.25 = 4).
        var js = new JointSet(dipDirectionDeg: 0, dipDeg: 90, meanSpacing: 0.25);
        var planes = JointSetDfnGenerator.Generate(new[] { js }, UnitBox, seed: 7);
        Assert(planes.Count >= 3 && planes.Count <= 5,
            $"expected ~4 planes (1.0 / 0.25), got {planes.Count}");
        // All planes should be parallel (normal +Y).
        for (int i = 0; i < planes.Count; i++)
        {
            Assert(Math.Abs(planes[i].NormalY - 1.0) < 1e-9,
                $"plane {i} normal Y expected 1, got {planes[i].NormalY}");
        }
    }

    public static void Dfn_TwoOrthogonalSets_AccumulatesPlanes()
    {
        // Two orthogonal vertical sets (East-West + North-South) with spacing 0.5.
        // Expected: ~2 + ~2 = ~4 planes.
        var jsEW = new JointSet(dipDirectionDeg: 0, dipDeg: 90, meanSpacing: 0.5);
        var jsNS = new JointSet(dipDirectionDeg: 90, dipDeg: 90, meanSpacing: 0.5);
        var planes = JointSetDfnGenerator.Generate(new[] { jsEW, jsNS }, UnitBox, seed: 7);
        Assert(planes.Count >= 3 && planes.Count <= 6,
            $"expected ~4 planes, got {planes.Count}");
    }

    public static void Dfn_DeterministicForSeed()
    {
        var js = new JointSet(dipDirectionDeg: 0, dipDeg: 90, meanSpacing: 0.3);
        var p1 = JointSetDfnGenerator.Generate(new[] { js }, UnitBox, seed: 42);
        var p2 = JointSetDfnGenerator.Generate(new[] { js }, UnitBox, seed: 42);
        Assert(p1.Count == p2.Count, $"counts differ: {p1.Count} vs {p2.Count}");
        for (int i = 0; i < p1.Count; i++)
        {
            Assert(Math.Abs(p1[i].PointY - p2[i].PointY) < 1e-12,
                $"plane {i} PointY mismatch");
        }
    }

    public static void Dfn_NullJointSets_Throws()
    {
        bool threw = false;
        try { _ = JointSetDfnGenerator.Generate(null, UnitBox, 0); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null jointSets should throw");
    }

    public static void Dfn_DecomposeByJointSets_CutsTheQuarry()
    {
        var quarry = Slab.Box(0, 0, 0, 1, 1, 1);
        var js = new JointSet(dipDirectionDeg: 0, dipDeg: 90, meanSpacing: 0.5);
        var result = JointSetDfnGenerator.DecomposeByJointSets(quarry, new[] { js }, seed: 7);
        Assert(result.Count >= 2, $"expected at least 2 cut pieces, got {result.Count}");
        double total = result.TotalVolume();
        Assert(Math.Abs(total - 1.0) < 1e-7, $"volume should conserve to 1.0, got {total}");
    }

    // ─── GH components ──────────────────────────────────────────────────────

    public static void Gh_JointSetComponent_Metadata()
    {
        var c = new JointSetComponent();
        Assert(c.ComponentGuid == new Guid("ECFDAEBF-CBDC-4345-6789-012345678BCD"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Name == "Joint Set", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Quarry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_QuarryDfnComponent_Metadata()
    {
        var c = new QuarryDfnComponent();
        Assert(c.ComponentGuid == new Guid("FDAEBFCA-DCED-4456-789A-CDEF01234567"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Name == "Quarry DFN", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Quarry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 2, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
