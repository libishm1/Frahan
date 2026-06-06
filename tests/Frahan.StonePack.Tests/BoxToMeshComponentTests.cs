#nullable disable
using System;
using Frahan.GH.Quarry;

namespace Frahan.Tests;

// =============================================================================
// BoxToMeshComponentTests — metadata smoke test for Frahan > Quarry >
// Box To Mesh. Mirrors the metadata-only smoke tests used elsewhere.
// =============================================================================

static class BoxToMeshComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new BoxToMeshComponent();
        Assert(c.ComponentGuid == new Guid("D3E4F5A6-3004-4F5E-A6B7-C8D9E0F12345"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category: {c.Category}");
        Assert(c.SubCategory == "Quarry", $"SubCategory: {c.SubCategory}");
        Assert(c.Name == "Box To Mesh", $"Name: {c.Name}");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
