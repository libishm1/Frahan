using System;
using Frahan.GH.Masonry;

namespace Frahan.Tests;

// Smoke tests for the Phase E.2 fracture-cutting GH components
// (FracturePolygonFromCurveComponent, SlabCutByFracturePolygonsComponent).
// Same SKIP-on-missing-Grasshopper pattern as SlabCutGhComponentTests:
// instantiating a GH_Component subclass triggers PostConstructor which calls
// RegisterInputParams/RegisterOutputParams, so the headless test runner
// SKIPs these via Program.cs IsNativeRhinoException when Grasshopper.dll /
// RhinoCommon.dll are unavailable.

static class FractureCutGhComponentTests
{
    // -- FracturePolygonFromCurveComponent ----------------------------------

    public static void FracturePolygonFromCurveComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new FracturePolygonFromCurveComponent();
        var expected = new Guid("D3C4E5F6-7B8A-49AC-BD2E-3F4A5B6C7D8E");
        Assert(c.ComponentGuid == expected,
            $"FracturePolygonFromCurveComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void FracturePolygonFromCurveComponent_Metadata_IsCorrect()
    {
        var c = new FracturePolygonFromCurveComponent();
        Assert(c.Name == "Fracture Polygon From Curve",
            $"Name should be 'Fracture Polygon From Curve', got '{c.Name}'");
        Assert(c.NickName == "FracPoly",
            $"NickName should be 'FracPoly', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan Cut', got '{c.Category}'");
        Assert(c.SubCategory == "Fracture",
            $"SubCategory should be 'Fracture', got '{c.SubCategory}'");
    }

    public static void FracturePolygonFromCurveComponent_HasExpectedInputAndOutputCount()
    {
        var c = new FracturePolygonFromCurveComponent();
        Assert(c.Params.Input.Count == 2,
            $"FracturePolygonFromCurveComponent input count should be 2 (Curve + ForceProject), got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1,
            $"FracturePolygonFromCurveComponent output count should be 1, got {c.Params.Output.Count}");
    }

    // -- SlabCutByFracturePolygonsComponent ---------------------------------

    public static void SlabCutByFracturePolygonsComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new SlabCutByFracturePolygonsComponent();
        var expected = new Guid("E4D5F607-8C9B-40BD-CE3F-405162738491");
        Assert(c.ComponentGuid == expected,
            $"SlabCutByFracturePolygonsComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void SlabCutByFracturePolygonsComponent_Metadata_IsCorrect()
    {
        var c = new SlabCutByFracturePolygonsComponent();
        Assert(c.Name == "Slab Cut By Fracture Polygons",
            $"Name should be 'Slab Cut By Fracture Polygons', got '{c.Name}'");
        Assert(c.NickName == "SlabCutFP",
            $"NickName should be 'SlabCutFP', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan Cut', got '{c.Category}'");
        Assert(c.SubCategory == "Fracture",
            $"SubCategory should be 'Fracture', got '{c.SubCategory}'");
    }

    public static void SlabCutByFracturePolygonsComponent_HasExpectedInputAndOutputCount()
    {
        var c = new SlabCutByFracturePolygonsComponent();
        Assert(c.Params.Input.Count == 4,
            $"SlabCutByFracturePolygonsComponent input count should be 4, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4,
            $"SlabCutByFracturePolygonsComponent output count should be 4 (Slab+Count+TotalVolume+Mesh), got {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
