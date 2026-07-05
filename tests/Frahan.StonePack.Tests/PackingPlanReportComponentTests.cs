#nullable disable
using System;
using Frahan.GH;
using Grasshopper.Kernel;

namespace Frahan.Tests;

// Item D (2026-05-04) - smoke tests for PackingPlanReportComponent input
// shape after the additive DataTree<Number> "Edge Match Tree" input was
// introduced. Pure managed via PostConstructor, same pattern as
// IrregularSheetFillComponentTests. Requires Grasshopper.dll on the
// runtime path (covered by Item E flipping Private=true on the test refs).
//
// SolveInstance behavior is not exercised here; correctness of the tree
// flattening + opaque-vs-tree precedence is exercised indirectly through
// the existing PackingPlanReportTests on the Core builder, which the
// component delegates to without re-implementing.

static class PackingPlanReportComponentTests
{
    public static void Component_ComponentGuid_IsExpectedValue()
    {
        var c = new PackingPlanReportComponent();
        var expected = new Guid("AB12C008-1A2B-4C3D-9E4F-5A6B7C8D9E08");
        Assert(c.ComponentGuid == expected,
            $"ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void Component_Metadata_IsCorrect()
    {
        var c = new PackingPlanReportComponent();
        // Renamed 2026-05-29 (P0 hygiene): disambiguated from the other
        // "Packing Report" (PackingReportComponent). 2026-07-04 legibility
        // sweep dropped the "Frahan " display-name prefix (Category stays "Frahan").
        Assert(c.Name == "Packing Plan Report",
            $"Name should be 'Packing Plan Report', got '{c.Name}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Reports",
            $"SubCategory should be 'Reports', got '{c.SubCategory}'");
    }

    public static void Component_HasFourInputsAndFourOutputs()
    {
        var c = new PackingPlanReportComponent();
        // 4 inputs after Item D: Packing Metrics, Residual Voids,
        // Edge Match Scores (opaque), Edge Match Tree (DataTree).
        Assert(c.Params.Input.Count == 4,
            $"Input count should be 4 after Item D, got {c.Params.Input.Count}");
        // 4 outputs unchanged: Plan Report, Total Residual Void Area,
        // Avg Best Edge Match Score, Summary.
        Assert(c.Params.Output.Count == 4,
            $"Output count should be 4, got {c.Params.Output.Count}");
    }

    public static void Component_FourthInput_IsEdgeMatchTree()
    {
        var c = new PackingPlanReportComponent();
        var input3 = c.Params.Input[3];
        Assert(input3.Name == "Edge Match Tree",
            $"Input[3] name should be 'Edge Match Tree', got '{input3.Name}'");
        Assert(input3.NickName == "Et",
            $"Input[3] nickname should be 'Et', got '{input3.NickName}'");
        Assert(input3.Access == GH_ParamAccess.tree,
            $"Input[3] access should be tree, got {input3.Access}");
        Assert(input3.Optional,
            "Input[3] should be Optional");
    }

    public static void Component_ThirdInput_EdgeMatchScores_StillItemAccess()
    {
        var c = new PackingPlanReportComponent();
        var input2 = c.Params.Input[2];
        // Backward-compatibility regression check: the original opaque
        // input must remain present and item-access so callers that
        // already wire it keep working.
        Assert(input2.Name == "Edge Match Scores",
            $"Input[2] name should be 'Edge Match Scores', got '{input2.Name}'");
        Assert(input2.NickName == "E",
            $"Input[2] nickname should be 'E', got '{input2.NickName}'");
        Assert(input2.Access == GH_ParamAccess.item,
            $"Input[2] access should be item, got {input2.Access}");
        Assert(input2.Optional,
            "Input[2] should be Optional");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
