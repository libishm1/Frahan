using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

[RelatedComponent("Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP)",
    Reason = "SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.")]
[DesignApplication(
    "Pack closed planar parts into irregular sheet outlines with optional per-sheet hole curves",
    DesignFlow.BottomUp,
    Precedent = "Burke 2007 NFP + Bennell Oliveira 2008 review")]
[Algorithm("NFP-assisted bottom-left irregular nesting",
    "Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). \"Complete and robust no-fit polygon generation for the irregular stock cutting problem.\" Eur. J. Oper. Res.",
    Doi = "10.1016/j.ejor.2006.03.011",
    WikiPath = "wiki/index/references.md#BurkeNFP2007")]
[Algorithm("Irregular-shape packing tutorial",
    "Bennell, J.A. & Oliveira, J.F. (2008). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(1)",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
public sealed class Pack2DIrregularSheetComponent : FrahanComponentBase
{
    public Pack2DIrregularSheetComponent()
        : base("2D Irregular Sheet Pack", "Sheet Pack",
            "PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases. " +
            "Pack closed planar parts into irregular sheet outlines with optional per-sheet hole curves. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).",
            "Frahan", "2D Packing")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    // R3 PR 7: marked obsolete - this is the V1 (IrregularSheetFillRhino) wrapper
    // (note the Simplify + SimplifyTolerance inputs that V2/V3/V506 don't have).
    // Replaced by Frahan.GH.IrregularSheetFillComponent (Variant=1). Users who
    // need V1's Simplify/SimplifyTolerance knobs should call IrregularSheetFillRhino
    // directly. Will be removed in 0.8.0 per R3 plan PR 9.
    public override bool Obsolete => true;
    public override Guid ComponentGuid => new Guid("8233FA3B-12F7-4D37-BBE5-6D3ECAB0FAE1");
    protected override Bitmap? Icon => IconProvider.Load("Pack2D.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P", "Closed planar part curves to pack.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S", "Closed planar outer sheet curves.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H", "Hole curves as a tree. Branch {0} belongs to sheet 0, {1} to sheet 1, and so on.", GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap", "Clearance between packed parts and sheet/hole boundaries.", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Rotations", "R", "Allowed rotations in degrees. Example: 0, 90, 180, 270.", GH_ParamAccess.list, 0.0);
        pManager.AddIntegerParameter("Sort Mode", "M", "0 UserOrder, 1 Area, 2 Width, 3 Height, 4 MaxDimension.", GH_ParamAccess.item, 1);
        pManager.AddBooleanParameter("Simplify", "Si", "Simplify curves before packing.", GH_ParamAccess.item, true);
        pManager.AddNumberParameter("Simplify Tolerance", "St", "Curve simplification tolerance.", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Tolerance", "T", "Collision and containment tolerance.", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Seed", "Seed", "0 is deterministic. Nonzero values change tie-breaking.", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run", "Execute packing.", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Max Candidates", "Max", "Candidate budget per part/rotation/sheet. Use 0 for default.", GH_ParamAccess.item, 300);
        pManager.AddIntegerParameter("Corner Mode", "Cnr", "0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight.", GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Packed Curves", "C", "Placed part curves.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "X", "Placement transforms applied to source curves.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src", "Original input curve index for each packed curve and transform.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sheet Indices", "Sh", "Sheet index used for each packed curve.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Unplaced", "U", "Curves that could not be placed.", GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Why", "Reason for each unplaced curve.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Preview", "B", "Outer sheet and hole preview curves.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Packing report.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var parts = new List<Curve>();
        var sheets = new List<Curve>();
        GH_Structure<GH_Curve>? holesTree = null;
        var spacing = 0.0;
        var rotationsDeg = new List<double>();
        var sortModeValue = 1;
        var simplifyCurves = true;
        var simplifyTolerance = 1.0;
        var tolerance = 0.01;
        var seed = 0;
        var run = false;
        var maxCandidates = 300;
        var cornerModeValue = 0;

        if (!da.GetDataList(0, parts)) return;
        if (!da.GetDataList(1, sheets)) return;
        da.GetDataTree(2, out holesTree);
        da.GetData(3, ref spacing);
        da.GetDataList(4, rotationsDeg);
        da.GetData(5, ref sortModeValue);
        da.GetData(6, ref simplifyCurves);
        da.GetData(7, ref simplifyTolerance);
        da.GetData(8, ref tolerance);
        da.GetData(9, ref seed);
        da.GetData(10, ref run);
        da.GetData(11, ref maxCandidates);
        da.GetData(12, ref cornerModeValue);

        var holesBySheet = BuildHolesBySheet(sheets, holesTree, tolerance);
        var preview = BuildPreview(sheets, holesBySheet);
        if (!run)
        {
            da.SetDataList(6, preview);
            da.SetData(7, "Run is false.");
            return;
        }

        if (sheets.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one sheet outline is required.");
            return;
        }

        var sortMode = ToSortMode(sortModeValue);
        var cornerMode = ToCornerMode(cornerModeValue);
        if (rotationsDeg.Count == 0)
        {
            rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        }

        PackingResult result;
        try
        {
            result = new IrregularSheetFillRhino(
                    sheets,
                    holesBySheet,
                    spacing,
                    rotationsDeg,
                    tolerance,
                    sortMode,
                    simplifyCurves,
                    simplifyTolerance,
                    seed,
                    maxCandidates,
                    cornerMode)
                .Pack(parts);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Irregular sheet packing failed: " + ex.Message);
            return;
        }

        if (result.UnplacedCurves.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{result.UnplacedCurves.Count} part(s) were not placed.");
        }

        if (result.InvalidCount > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{result.InvalidCount} input curve(s) were ignored. Parts must be closed and planar.");
        }

        if (result.RuntimeMilliseconds > 8000)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Irregular sheet packing took {result.RuntimeMilliseconds} ms. Increase Simplify Tolerance, reduce rotations, or lower Max Candidates while editing.");
        }

        da.SetDataList(0, result.PackedCurves);
        da.SetDataList(1, result.Transforms);
        da.SetDataList(2, result.SourceIndices);
        da.SetDataList(3, result.SheetIndices);
        da.SetDataList(4, result.UnplacedCurves);
        da.SetDataList(5, result.FailureReasons);
        da.SetDataList(6, result.SheetPreviewCurves.Count > 0 ? result.SheetPreviewCurves : preview);
        da.SetData(7, result.Report);
    }

    private List<List<Curve>> BuildHolesBySheet(IReadOnlyList<Curve> sheets, GH_Structure<GH_Curve>? holesTree, double tolerance)
    {
        var sheetCount = sheets.Count;
        var holes = Enumerable.Range(0, Math.Max(0, sheetCount)).Select(_ => new List<Curve>()).ToList();
        if (holesTree == null || sheetCount == 0)
        {
            return holes;
        }

        foreach (var path in holesTree.Paths)
        {
            var sheetIndex = path.Length > 0 ? path[0] : 0;
            if (sheetIndex < 0 || sheetIndex >= sheetCount)
            {
                if (sheetCount == 1)
                {
                    sheetIndex = 0;
                }
                else
                {
                    continue;
                }
            }

            foreach (var branchItem in holesTree.get_Branch(path))
            {
                if (branchItem is GH_Curve item && item.Value != null)
                {
                    var hole = item.Value.DuplicateCurve();
                    var containingSheet = FindContainingSheet(sheets, hole, tolerance);
                    holes[containingSheet >= 0 ? containingSheet : sheetIndex].Add(hole);
                }
            }
        }

        return holes;
    }

    private static int FindContainingSheet(IReadOnlyList<Curve> sheets, Curve hole, double tolerance)
    {
        var point = AreaMassProperties.Compute(hole)?.Centroid ?? hole.GetBoundingBox(true).Center;
        var containsTolerance = Math.Max(tolerance, 0.01);

        for (var i = 0; i < sheets.Count; i++)
        {
            if (sheets[i].TryGetPlane(out var sheetPlane, containsTolerance))
            {
                var containment = sheets[i].Contains(point, sheetPlane, containsTolerance);
                if (containment == PointContainment.Inside || containment == PointContainment.Coincident)
                {
                    return i;
                }
            }

            var sheet = sheets[i].DuplicateCurve();
            var worldContainment = sheet.Contains(point, Plane.WorldXY, containsTolerance);
            if (worldContainment == PointContainment.Inside || worldContainment == PointContainment.Coincident)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<Curve> BuildPreview(IReadOnlyList<Curve> sheets, IReadOnlyList<IReadOnlyList<Curve>> holesBySheet)
    {
        var preview = new List<Curve>();
        for (var i = 0; i < sheets.Count; i++)
        {
            preview.Add(sheets[i].DuplicateCurve());
            if (i < holesBySheet.Count)
            {
                preview.AddRange(holesBySheet[i].Select(hole => hole.DuplicateCurve()));
            }
        }

        return preview;
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
}
