using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

[Algorithm("No-fit polygon construction", "Burke, Hellier, Kendall, Whitwell 2007, European Journal of Operational Research 179(1):27-49 Complete and robust no-fit polygon generation for the irregular stock cutting problem", Doi = "10.1016/j.ejor.2006.03.011", WikiPath = "wiki/algorithms/surface_mosaicing/primitives/no_fit_polygon.md")]
[Algorithm("Nesting heuristic review", "Bennell and Oliveira 2008, Journal of the Operational Research Society 60(supp 1):S93-S105 The geometry of nesting problems: a tutorial", Doi = "10.1057/jors.2008.169", Note = "NFP-assisted bottom-left placement with deterministic swap-search optimiser")]
[DesignApplication(
    "NFP-assisted 2D irregular packing with diagnostics and optional sequence optimization",
    DesignFlow.BottomUp,
    Precedent = "Burke Hellier Kendall Whitwell 2007 No-Fit Polygon (DOI 10.1016/j.ejor.2006.03.011); Bennell Oliveira 2008 review (DOI 10.1057/jors.2008.169)",
    Tolerance = "0 overlap; >= 90 % container utilisation on convex inputs")]
public sealed class NfpPack2DComponent : FrahanComponentBase
{
    public NfpPack2DComponent()
        : base("2D NFP Pack", "NFP Pack",
            "NFP-assisted 2D irregular packing with diagnostics and optional sequence optimization. [Burke et al. 2007]",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("0B164F89-A199-4264-88FD-A91E508DBEC3");
    protected override Bitmap? Icon => IconProvider.Load("NoFitPolygon.png");

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
        pManager.AddNumberParameter("Tolerance", "T", "Collision and NFP tolerance.", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("NFP Max Iterations", "NI", "Budget for triangulated concave NFP construction. Higher values allow more concave detail but solve slower.", GH_ParamAccess.item, 2500);
        pManager.AddIntegerParameter("Optimizer Mode", "OM", "0 None, 1 sort variants, 2 sort variants plus reverse, 3 deterministic swap search.", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Optimizer Iterations", "OI", "Additional deterministic swap-search iterations used when Optimizer Mode is 3.", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Seed", "Seed", "0 keeps the original deterministic source behavior. Nonzero values explore alternate rotation and swap-search options.", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Corner Mode", "Cnr", "0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight.", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run", "Execute packing.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Packed Curves", "C", "Placed curves.", GH_ParamAccess.list);
        pManager.AddGenericParameter("Transforms", "X", "Placement transforms applied to source curves.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Unplaced", "U", "Curves that could not be placed.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Preview", "B", "Preview rectangle for the sheet.", GH_ParamAccess.item);
        pManager.AddCurveParameter("NFP Preview", "N", "Diagnostic no-fit regions used during placement. Capped to keep Grasshopper responsive.", GH_ParamAccess.list);
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
        var nfpMaxIterations = 2500;
        var optimizationMode = 0;
        var optimizationIterations = 0;
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
        da.GetData(9, ref nfpMaxIterations);
        da.GetData(10, ref optimizationMode);
        da.GetData(11, ref optimizationIterations);
        da.GetData(12, ref seed);
        da.GetData(13, ref cornerModeValue);
        da.GetData(14, ref run);

        if (sheetWidth <= 0 || sheetLength <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sheet width and length must be positive.");
            return;
        }

        var sortMode = ToSortMode(sortModeValue);
        var cornerMode = ToCornerMode(cornerModeValue);
        if (!run)
        {
            da.SetData(3, new NfpBottomLeftFillRhino(sheetWidth, sheetLength, spacing, rotationsDeg, tolerance, sortMode, simplifyCurves, simplifyTolerance, nfpMaxIterations, optimizationMode, optimizationIterations, seed, cornerMode).GetSheetPreviewCurve());
            da.SetData(7, "Run is false.");
            return;
        }

        if (rotationsDeg.Count == 0)
        {
            rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        }

        PackingResult result;
        try
        {
            result = new NfpBottomLeftFillRhino(sheetWidth, sheetLength, spacing, rotationsDeg, tolerance, sortMode, simplifyCurves, simplifyTolerance, nfpMaxIterations, optimizationMode, optimizationIterations, seed, cornerMode)
                .Pack(inputCurves);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "NFP packing failed: " + ex.Message);
            return;
        }

        AddWarnings(result, 8000);
        da.SetDataList(0, result.PackedCurves);
        da.SetDataList(1, result.Transforms);
        da.SetDataList(2, result.UnplacedCurves);
        da.SetData(3, result.SheetPreview);
        da.SetDataList(4, Array.Empty<Curve>());
        da.SetData(5, result.UsedLength);
        da.SetData(6, result.Utilization);
        da.SetData(7, result.Report);
        da.SetDataList(8, result.SourceIndices);
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
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"NFP packing took {result.RuntimeMilliseconds} ms. Increase Simplify Tolerance, reduce rotations, or set Optimizer Mode to 0 while editing.");
        }
    }
}
