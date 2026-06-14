using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

[RelatedComponent("Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP)",
    Reason = "SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.")]
[DesignApplication(
    "Greedy 2D irregular packing using RhinoCommon curves",
    DesignFlow.BottomUp,
    Precedent = "Bottom-Left fill heuristic (Baker Coffman Rivest 1980 / Chazelle 1983 variants); Bennell Oliveira 2008 review",
    Tolerance = ">= 80 % sheet utilisation on convex inputs")]
[Algorithm("Bottom-left-fill placement heuristic",
    "Baker, B.S., Coffman, E.G., Rivest, R.L. (1980). \"Orthogonal packings in two dimensions.\" SIAM J. Comput. 9(4):846-855",
    WikiPath = "wiki/index/references.md#BakerCoffmanRivest1980BL")]
[Algorithm("Irregular-shape packing tutorial",
    "Bennell, J.A. & Oliveira, J.F. (2008). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(1)",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
public sealed class Pack2DBottomLeftComponent : FrahanComponentBase
{
    public Pack2DBottomLeftComponent()
        : base("2D Bottom Left Pack", "BL Pack",
            "PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases. " +
            "Greedy 2D irregular packing using RhinoCommon curves. Implements bottom-left fill (Baker, Coffman & Rivest 1980).",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("6E63E716-84E5-4E1B-9673-8D9C12C4D8B1");
    protected override Bitmap? Icon => IconProvider.Load("BottomLeftPacker.png");
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    // 2026-06-05 (W7, keep-or-cut): marked Obsolete with measured evidence.
    // The --packbench benchmark (24 synthetic parts on a 60%-break-even sheet)
    // showed standalone BLF dominated on every axis: 60.5% fill / 5260 ms /
    // 1 overlap pair, versus NFP-BLF (NfpPack2DComponent) at 65.2% / 0 overlap
    // and the unified sheet packer at 60.0% / 94 ms / 0 overlap. The bottom-left
    // strategy survives inside NfpBottomLeftFillRhino (the NFP nester's placement
    // step), so no capability is lost. GUID unchanged: old canvases still load.
    // See outputs/2026-06-05/keep_or_cut/PACKING_BENCHMARK.md.
    public override bool Obsolete => true;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P", "Closed planar curves to pack.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Sheet Width", "W", "Sheet width in Y direction.", GH_ParamAccess.item, 1000.0);
        pManager.AddNumberParameter("Sheet Length", "L", "Sheet length in X direction.", GH_ParamAccess.item, 3000.0);
        pManager.AddNumberParameter("Spacing", "S", "Clearance between packed parts.", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Rotations", "R", "Allowed rotations in degrees. Example: 0, 90, 180, 270.", GH_ParamAccess.list, 0.0);
        pManager.AddIntegerParameter("Sort Mode", "M", "0 UserOrder, 1 Area, 2 Width, 3 Height, 4 MaxDimension.", GH_ParamAccess.item, 1);
        pManager.AddBooleanParameter("Simplify", "Si", "Simplify curves before packing.", GH_ParamAccess.item, true);
        pManager.AddNumberParameter("Simplify Tolerance", "St", "Curve simplification tolerance.", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Tolerance", "T", "Collision tolerance.", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Seed", "Seed", "0 keeps the original deterministic source behavior. Nonzero values explore alternate tie/order options.", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Corner Mode", "Cnr", "0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight.", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run", "Execute packing.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Packed Curves", "C", "Placed curves.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "X", "Placement transforms applied to source curves.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Unplaced", "U", "Curves that could not be placed.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Preview", "B", "Preview rectangle for the sheet.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Used Length", "L", "Used sheet length.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Utilization", "A", "Area utilization inside used sheet length.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Packing report.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Source Indices", "Src", "Original input curve index for each packed curve and transform.", GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var inputCurves = new List<Curve>();
        var sheetWidth = 1000.0;
        var sheetLength = 3000.0;
        var spacing = 0.0;
        var rotationsDeg = new List<double>();
        var sortModeValue = 1;
        var simplifyCurves = true;
        var simplifyTolerance = 1.0;
        var tolerance = 0.01;
        var seed = 0;
        var cornerModeValue = 0;
        var run = false;

        if (!da.GetDataList(0, inputCurves)) return;
        if (!da.GetData(1, ref sheetWidth)) return;
        if (!da.GetData(2, ref sheetLength)) return;
        da.GetData(3, ref spacing);
        da.GetDataList(4, rotationsDeg);
        da.GetData(5, ref sortModeValue);
        da.GetData(6, ref simplifyCurves);
        da.GetData(7, ref simplifyTolerance);
        da.GetData(8, ref tolerance);
        da.GetData(9, ref seed);
        da.GetData(10, ref cornerModeValue);
        da.GetData(11, ref run);

        if (sheetWidth <= 0 || sheetLength <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sheet width and length must be positive.");
            return;
        }

        var sortMode = ToSortMode(sortModeValue);
        var cornerMode = ToCornerMode(cornerModeValue);
        if (!run)
        {
            da.SetData(3, new BottomLeftFillRhino(sheetWidth, sheetLength, spacing, rotationsDeg, tolerance, sortMode, simplifyCurves, simplifyTolerance, seed, cornerMode).GetSheetPreviewCurve());
            da.SetData(6, "Run is false.");
            return;
        }

        if (rotationsDeg.Count == 0)
        {
            rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        }

        PackingResult result;
        try
        {
            result = new BottomLeftFillRhino(sheetWidth, sheetLength, spacing, rotationsDeg, tolerance, sortMode, simplifyCurves, simplifyTolerance, seed, cornerMode)
                .Pack(inputCurves);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Packing failed: " + ex.Message);
            return;
        }

        AddWarnings(result, 5000);
        da.SetDataList(0, result.PackedCurves);
        da.SetDataList(1, result.Transforms);
        da.SetDataList(2, result.UnplacedCurves);
        da.SetData(3, result.SheetPreview);
        da.SetData(4, result.UsedLength);
        da.SetData(5, result.Utilization);
        da.SetData(6, result.Report);
        da.SetDataList(7, result.SourceIndices);
    }

    private PackingSortMode ToSortMode(int value)
    {
        if (Enum.IsDefined(typeof(PackingSortMode), value))
        {
            return (PackingSortMode)value;
        }

        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid sort mode. Using AreaDescending.");
        return PackingSortMode.AreaDescending;
    }

    private PackingCornerMode ToCornerMode(int value)
    {
        if (Enum.IsDefined(typeof(PackingCornerMode), value))
        {
            return (PackingCornerMode)value;
        }

        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid corner mode. Using BottomLeft.");
        return PackingCornerMode.BottomLeft;
    }

    private void AddWarnings(PackingResult result, long slowThreshold)
    {
        if (result.UnplacedCurves.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{result.UnplacedCurves.Count} part(s) were not placed.");
        }

        if (result.InvalidCount > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{result.InvalidCount} input curve(s) were ignored. Parts must be closed and planar.");
        }

        if (result.RuntimeMilliseconds > slowThreshold)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Packing took {result.RuntimeMilliseconds} ms. Increase Simplify Tolerance, reduce rotations, or disable the solver while editing inputs.");
        }
    }
}
