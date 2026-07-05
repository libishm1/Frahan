#nullable disable
using System;
using Frahan.GH.EdgeMatch3D;

namespace Frahan.Tests;

// Metadata smoke test for the Edge Gap (Fréchet) node: stable GUID + I/O shape.
// Construction is headless (no live Rhino); mirrors the other component smoke tests.
static class EdgeGapFrechetComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new EdgeGapFrechetComponent();
        Assert(c.ComponentGuid == new Guid("E7C4A6F0-1D3B-4A2E-9F5C-8B7A6D5E4C3B"),
            $"GUID: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category: {c.Category}");
        Assert(c.SubCategory == "EdgeMatch", $"SubCategory: {c.SubCategory}");
        Assert(c.NickName == "EdgeGap", $"NickName: {c.NickName}");
        Assert(c.Name.StartsWith("Edge Gap", StringComparison.Ordinal), $"Name: {c.Name}");
        Assert(c.Params.Input.Count == 4, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 2, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException("EdgeGapFrechet: " + msg);
    }
}
