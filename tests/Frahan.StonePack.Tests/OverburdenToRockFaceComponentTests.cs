#nullable disable
using System;
using Frahan.GH.Quarry;

namespace Frahan.Tests;

// Metadata smoke test for Frahan > Quarry > Overburden To Rock Face (W16; wraps
// Core OverburdenVolume). Mirrors BoxToMeshComponentTests.

static class OverburdenToRockFaceComponentTests
{
    public static void Metadata_IsExpectedValues()
    {
        var c = new OverburdenToRockFaceComponent();
        Assert(c.ComponentGuid == new Guid("A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE01"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category: {c.Category}");
        Assert(c.SubCategory == "Quarry", $"SubCategory: {c.SubCategory}");
        Assert(c.Name == "Overburden To Rock Face", $"Name: {c.Name}");
        Assert(c.NickName == "Overburden", $"NickName: {c.NickName}");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 7, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("OverburdenToRockFace: " + message);
    }
}
