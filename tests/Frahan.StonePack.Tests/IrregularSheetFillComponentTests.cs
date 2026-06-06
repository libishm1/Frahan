using System;
using Frahan.GH;
using Frahan.GH.TwoD;

namespace Frahan.Tests;

// R3 PR 6 - smoke tests for the unified IrregularSheetFillComponent.
// Pure managed: instantiating GH_Component runs the framework's
// PostConstructor which calls RegisterInputParams/RegisterOutputParams,
// so Params.Input.Count and Params.Output.Count are populated without
// needing a Grasshopper UI runtime.
//
// These tests do NOT exercise SolveInstance (which would need rhcommon_c.dll).
// SolveInstance correctness is covered transitively by the ForVariant
// routing tests + the V*_Facade_Equals_Legacy_OnEmptyInputs equivalence
// tests in IrregularSheetFillEquivalenceTests.cs.

static class IrregularSheetFillComponentTests
{
    public static void Component_ComponentGuid_IsExpectedValue()
    {
        var c = new IrregularSheetFillComponent();
        var expected = new Guid("AB12C00B-1A2B-4C3D-9E4F-5A6B7C8D9E0B");
        Assert(c.ComponentGuid == expected,
            $"ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void Component_Metadata_IsCorrect()
    {
        var c = new IrregularSheetFillComponent();
        Assert(c.Name == "Frahan Sheet Pack (Unified)",
            $"Name should be 'Frahan Sheet Pack (Unified)', got '{c.Name}'");
        Assert(c.NickName == "FreeNestU",
            $"NickName should be 'FreeNestU', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "2D Packing",
            $"SubCategory should be '2D Packing', got '{c.SubCategory}'");
    }

    public static void Component_HasExpectedInputAndOutputCount()
    {
        var c = new IrregularSheetFillComponent();
        // 16 inputs (Half J adds Trim Tolerance):
        //   Parts, Sheet Outlines, Sheet Holes, Spacing, Rotations,
        //   Sort Mode, Tolerance, Seed, Run, Max Candidates, Corner Mode,
        //   Variant, Boundary Mode, Min Boundary Affinity,
        //   Discretization Tolerance, Trim Tolerance
        Assert(c.Params.Input.Count == 16,
            $"Input count should be 16, got {c.Params.Input.Count}");
        // 11 outputs (Half J adds Trimmed Curves + Trim Adjacency)
        Assert(c.Params.Output.Count == 11,
            $"Output count should be 11, got {c.Params.Output.Count}");
    }

    public static void Component_BoundaryInputs_HaveExpectedNames()
    {
        var c = new IrregularSheetFillComponent();
        var b = c.Params.Input[12];
        Assert(b.Name == "Boundary Mode",
            $"Input 12 should be 'Boundary Mode', got '{b.Name}'");
        Assert(b.NickName == "BMode",
            $"Input 12 nickname should be 'BMode', got '{b.NickName}'");
        var a = c.Params.Input[13];
        Assert(a.Name == "Min Boundary Affinity",
            $"Input 13 should be 'Min Boundary Affinity', got '{a.Name}'");
        Assert(a.NickName == "BAff",
            $"Input 13 nickname should be 'BAff', got '{a.NickName}'");
        var d = c.Params.Input[14];
        Assert(d.Name == "Discretization Tolerance",
            $"Input 14 should be 'Discretization Tolerance', got '{d.Name}'");
        Assert(d.NickName == "DTol",
            $"Input 14 nickname should be 'DTol', got '{d.NickName}'");
    }

    public static void Component_LastInput_IsVariant()
    {
        // 2026-05-05: after the boundary-aware fold-in, Variant is at index
        // 11 (12th input) but no longer the last — Min Boundary Affinity is
        // the new last input. Assert the position rather than "last".
        var c = new IrregularSheetFillComponent();
        var variantInput = c.Params.Input[11];
        Assert(variantInput.Name == "Variant",
            $"Input 11 should be 'Variant', got '{variantInput.Name}'");
        Assert(variantInput.NickName == "V",
            $"Variant input nickname should be 'V', got '{variantInput.NickName}'");
    }

    public static void Component_LastOutput_IsVariantUsed()
    {
        // Half J: Variant Used is at index 8 (was last before Half J added
        // Trimmed Curves [9] and Trim Adjacency [10]). Assert position
        // rather than "last".
        var c = new IrregularSheetFillComponent();
        var variantUsed = c.Params.Output[8];
        Assert(variantUsed.Name == "Variant Used",
            $"Output 8 should be 'Variant Used', got '{variantUsed.Name}'");
        Assert(variantUsed.NickName == "Vu",
            $"Variant Used output nickname should be 'Vu', got '{variantUsed.NickName}'");
    }

    // -- Async unified component (R3 PR 6+ async) ---------------------------

    public static void AsyncComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new IrregularSheetFillComponentAsync();
        var expected = new Guid("AB12C00C-1A2B-4C3D-9E4F-5A6B7C8D9E0C");
        Assert(c.ComponentGuid == expected,
            $"Async component ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void AsyncComponent_HasExpectedInputAndOutputCount()
    {
        var c = new IrregularSheetFillComponentAsync();
        Assert(c.Params.Input.Count == 12,
            $"Async component input count should be 12, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 9,
            $"Async component output count should be 9, got {c.Params.Output.Count}");
    }

    public static void AsyncComponent_NickName_IsFreeNestUA()
    {
        var c = new IrregularSheetFillComponentAsync();
        Assert(c.NickName == "FreeNestUA",
            $"Async component NickName should be 'FreeNestUA', got '{c.NickName}'");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
