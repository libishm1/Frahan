#nullable disable
using System;
using Frahan.GH.TwoD;

namespace Frahan.Tests;

static class SheetNestLiveComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new SheetNestLiveComponent();
        Assert(c.ComponentGuid == new Guid("2ACEE264-21AC-4095-9E93-10CD96776BB2"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category: {c.Category}");
        Assert(c.SubCategory == "2D Packing", $"SubCategory: {c.SubCategory}");
        Assert(c.NickName == "NestLive", $"NickName: {c.NickName}");
        Assert(c.Name.StartsWith("Sheet Nest"), $"Name: {c.Name}");
        // HoleNest's 9 inputs (Sheets, Sheet Holes, Parts, Part Holes, Spacing,
        // BaseRotations, ContactRotations, Resolution, MultiStart) + the evolved
        // Boundary Mode + Min Boundary Contact pair + a trailing Run gate
        // (AsyncScanComponent contract) = 12.
        Assert(c.Params.Input.Count == 12, $"Input count {c.Params.Input.Count}");
        // Placed, Source, Transform, Nested, Sheet, Report.
        Assert(c.Params.Output.Count == 6, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
