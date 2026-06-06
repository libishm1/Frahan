using System;
using Frahan.GH.Masonry;

namespace Frahan.Tests;

// Smoke tests for the five new ashlar-pack GH components. Mirrors the
// pattern in MasonryGhComponentTests: instantiating a GH_Component subclass
// triggers PostConstructor → RegisterInputParams / RegisterOutputParams.
// In a headless host without Grasshopper.dll the constructor will throw a
// FileNotFoundException for "Grasshopper" → Program.cs flips that to SKIP.
// With Rhino 8 + Grasshopper.dll on PATH (FRAHAN_SKIP_NATIVE != 1) these
// promote to PASS.

static class AshlarPackGhComponentTests
{
    // ─── AshlarPackComponent ────────────────────────────────────────────────

    public static void AshlarPackComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new AshlarPackComponent();
        var expected = new Guid("F1A2B3C4-D5E6-4789-9ABC-DEF012345678");
        Assert(c.ComponentGuid == expected,
            $"AshlarPackComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void AshlarPackComponent_Metadata_IsCorrect()
    {
        var c = new AshlarPackComponent();
        Assert(c.Name == "Ashlar Pack", $"Name '{c.Name}'");
        Assert(c.NickName == "AshlarPack", $"NickName '{c.NickName}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
    }

    public static void AshlarPackComponent_HasExpectedInputAndOutputCount()
    {
        var c = new AshlarPackComponent();
        // 11 primitive + 2 optional Stage 2 + 1 optional Start Plane = 14
        Assert(c.Params.Input.Count == 14,
            $"AshlarPackComponent input count should be 14 (11 primitive + 2 optional Stage 2 + 1 optional Start Plane), got {c.Params.Input.Count}");
        // Assembly + Result + Display Transform = 3
        Assert(c.Params.Output.Count == 3,
            $"AshlarPackComponent output count should be 3 (Assembly + Result + Display Transform), got {c.Params.Output.Count}");
    }

    public static void AshlarPackComponent_OptionalInputs_AreOptional()
    {
        var c = new AshlarPackComponent();
        Assert(c.Params.Input[11].Optional,
            "AshlarPackComponent input 11 (Wall Frame) should be Optional");
        Assert(c.Params.Input[12].Optional,
            "AshlarPackComponent input 12 (Options) should be Optional");
    }

    // ─── WallFrameComponent ─────────────────────────────────────────────────

    public static void WallFrameComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new WallFrameComponent();
        var expected = new Guid("A2B3C4D5-E6F7-489A-BCDE-F01234567890");
        Assert(c.ComponentGuid == expected,
            $"WallFrameComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void WallFrameComponent_Metadata_IsCorrect()
    {
        var c = new WallFrameComponent();
        Assert(c.Name == "Wall Frame", $"Name '{c.Name}'");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
    }

    public static void WallFrameComponent_HasExpectedInputAndOutputCount()
    {
        var c = new WallFrameComponent();
        Assert(c.Params.Input.Count == 3,
            $"WallFrameComponent input count should be 3, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1,
            $"WallFrameComponent output count should be 1, got {c.Params.Output.Count}");
    }

    // ─── AshlarPackOptionsComponent ─────────────────────────────────────────

    public static void AshlarPackOptionsComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new AshlarPackOptionsComponent();
        var expected = new Guid("B3C4D5E6-F7A8-49AB-CDEF-012345678901");
        Assert(c.ComponentGuid == expected,
            $"AshlarPackOptionsComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void AshlarPackOptionsComponent_HasExpectedInputAndOutputCount()
    {
        var c = new AshlarPackOptionsComponent();
        Assert(c.Params.Input.Count == 7,
            $"AshlarPackOptionsComponent input count should be 7, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1,
            $"AshlarPackOptionsComponent output count should be 1, got {c.Params.Output.Count}");
    }

    // ─── PackDiagnosticsComponent ───────────────────────────────────────────

    public static void PackDiagnosticsComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new PackDiagnosticsComponent();
        var expected = new Guid("C4D5E6F7-A8B9-4ABC-DEF0-123456789012");
        Assert(c.ComponentGuid == expected,
            $"PackDiagnosticsComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void PackDiagnosticsComponent_HasExpectedInputAndOutputCount()
    {
        var c = new PackDiagnosticsComponent();
        Assert(c.Params.Input.Count == 1,
            $"PackDiagnosticsComponent input count should be 1, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5,
            $"PackDiagnosticsComponent output count should be 5, got {c.Params.Output.Count}");
    }

    // ─── PackPreviewComponent ───────────────────────────────────────────────

    public static void PackPreviewComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new PackPreviewComponent();
        var expected = new Guid("D5E6F7A8-B9CA-4BCD-EF01-234567890123");
        Assert(c.ComponentGuid == expected,
            $"PackPreviewComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void PackPreviewComponent_HasExpectedInputAndOutputCount()
    {
        var c = new PackPreviewComponent();
        Assert(c.Params.Input.Count == 1,
            $"PackPreviewComponent input count should be 1, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1,
            $"PackPreviewComponent output count should be 1, got {c.Params.Output.Count}");
    }

    // ─── No-collision check across all five new GUIDs ───────────────────────

    public static void NewComponentGuids_AreUnique()
    {
        var ids = new[]
        {
            new Guid("F1A2B3C4-D5E6-4789-9ABC-DEF012345678"),
            new Guid("A2B3C4D5-E6F7-489A-BCDE-F01234567890"),
            new Guid("B3C4D5E6-F7A8-49AB-CDEF-012345678901"),
            new Guid("C4D5E6F7-A8B9-4ABC-DEF0-123456789012"),
            new Guid("D5E6F7A8-B9CA-4BCD-EF01-234567890123"),
        };
        for (int i = 0; i < ids.Length; i++)
        {
            for (int j = i + 1; j < ids.Length; j++)
            {
                Assert(ids[i] != ids[j],
                    $"GUID collision: ids[{i}] == ids[{j}] ({ids[i]})");
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
