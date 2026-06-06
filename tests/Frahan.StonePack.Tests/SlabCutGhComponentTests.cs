using System;
using Frahan.GH.Masonry;

namespace Frahan.Tests;

// Smoke tests for the slab-cutting GH components (SlabFromMeshComponent,
// SlabCutByFracturesComponent). Same SKIP-on-missing-Grasshopper pattern as
// MasonryGhComponentTests: instantiating a GH_Component subclass triggers
// PostConstructor which calls RegisterInputParams/RegisterOutputParams, so
// the headless test runner SKIPs these via Program.cs IsNativeRhinoException
// when Grasshopper.dll / RhinoCommon.dll are unavailable.

static class SlabCutGhComponentTests
{
    // -- SlabFromMeshComponent ----------------------------------------------

    public static void SlabFromMeshComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new SlabFromMeshComponent();
        var expected = new Guid("B1A2C3D4-5E6F-4789-9ABC-1D2E3F4A5B6C");
        Assert(c.ComponentGuid == expected,
            $"SlabFromMeshComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void SlabFromMeshComponent_Metadata_IsCorrect()
    {
        var c = new SlabFromMeshComponent();
        Assert(c.Name == "Slab From Mesh",
            $"Name should be 'Slab From Mesh', got '{c.Name}'");
        Assert(c.NickName == "Slab",
            $"NickName should be 'Slab', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan Cut', got '{c.Category}'");
        Assert(c.SubCategory == "Slab",
            $"SubCategory should be 'Slab', got '{c.SubCategory}'");
    }

    public static void SlabFromMeshComponent_HasExpectedInputAndOutputCount()
    {
        var c = new SlabFromMeshComponent();
        Assert(c.Params.Input.Count == 1,
            $"SlabFromMeshComponent input count should be 1, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 2,
            $"SlabFromMeshComponent output count should be 2 (Slab + Mesh), got {c.Params.Output.Count}");
    }

    // -- SlabCutByFracturesComponent ----------------------------------------

    public static void SlabCutByFracturesComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new SlabCutByFracturesComponent();
        var expected = new Guid("C2B3D4E5-6F7A-489B-AC1D-2E3F4A5B6C7D");
        Assert(c.ComponentGuid == expected,
            $"SlabCutByFracturesComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void SlabCutByFracturesComponent_Metadata_IsCorrect()
    {
        var c = new SlabCutByFracturesComponent();
        Assert(c.Name == "Slab Cut By Fractures",
            $"Name should be 'Slab Cut By Fractures', got '{c.Name}'");
        Assert(c.NickName == "SlabCut",
            $"NickName should be 'SlabCut', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan Cut', got '{c.Category}'");
        Assert(c.SubCategory == "Slab",
            $"SubCategory should be 'Slab', got '{c.SubCategory}'");
    }

    public static void SlabCutByFracturesComponent_HasExpectedInputAndOutputCount()
    {
        var c = new SlabCutByFracturesComponent();
        // 4 inputs since 2026-05-29: Mesh, Plane, Eps, Use CGAL (CGAL backend toggle).
        Assert(c.Params.Input.Count == 4,
            $"SlabCutByFracturesComponent input count should be 4, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5,
            $"SlabCutByFracturesComponent output count should be 5 (Slab+Parent+TotalVolume+Count+Mesh), got {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
