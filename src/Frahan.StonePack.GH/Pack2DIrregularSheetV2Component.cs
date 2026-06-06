using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Version 2 of the irregular sheet packing component.
/// Accepts any closed planar curve for both sheets and parts (not just polylines).
/// Inputs are projected to the sheet plane automatically.
/// Runs the solver on a background thread via GH_TaskCapableComponent so Rhino
/// stays responsive during long solves.
/// </summary>
[Algorithm("NFP-assisted bottom-left irregular nesting",
    "Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). \"Complete and robust no-fit polygon generation for the irregular stock cutting problem.\" Eur. J. Oper. Res.",
    Doi = "10.1016/j.ejor.2006.03.011",
    WikiPath = "wiki/index/references.md#BurkeNFP2007")]
[Algorithm("Irregular-shape packing tutorial",
    "Bennell, J.A. & Oliveira, J.F. (2008). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(1)",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
public sealed class Pack2DIrregularSheetV2Component : GH_TaskCapableComponent<PackingResult>
{
    public Pack2DIrregularSheetV2Component()
        : base("2D Freeform Sheet Pack", "Freeform Pack",
            "Pack any closed planar curves (freeform arcs, splines, polygons) into freeform " +
            "sheet outlines with optional holes. Non-blocking async solve. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).",
            "Frahan", "2D Packing")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    // R3 PR 7: marked obsolete - replaced by Frahan.GH.IrregularSheetFillComponent
    // (Variant=2). Will be removed in 0.8.0 per R3 plan PR 9. Existing GH
    // documents that reference this component will continue to load and run;
    // they'll just show the obsolete-component visual treatment.
    public override bool Obsolete => true;
    public override Guid ComponentGuid => new Guid("A7F52C1D-3E84-4B09-9CF1-85D74A2E0B3F");
    protected override Bitmap? Icon => IconProvider.Load("IrregularSheet.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar part curves to pack. Any curve type accepted — freeform, arc, polyline.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet boundary curves. Any curve type accepted.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc.",
            GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap",
            "Clearance between packed parts and between parts and boundaries. Minimum enforced: 0.1.",
            GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Rotations", "R",
            "Allowed rotation angles in degrees (e.g. 0, 90, 180, 270).",
            GH_ParamAccess.list, 0.0);
        pManager.AddIntegerParameter("Sort Mode", "M",
            "0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓.",
            GH_ParamAccess.item, 1);
        pManager.AddNumberParameter("Tolerance", "T",
            "Geometric tolerance for containment and collision checks.",
            GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Seed", "Seed",
            "0 = deterministic. Non-zero changes tie-breaking randomisation.",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run",
            "Set to true to execute packing.",
            GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Max Candidates", "Max",
            "Candidate budget per part per rotation. 0 = default (300).",
            GH_ParamAccess.item, 300);
        pManager.AddIntegerParameter("Corner Mode", "Cnr",
            "0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight.",
            GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Packed Curves", "C", "Placed part curves.", GH_ParamAccess.list);
        pManager.AddGenericParameter("Transforms", "X",
            "Placement transforms applied to each source curve.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src",
            "Original input curve index for each packed curve.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sheet Indices", "Sh",
            "Sheet index used for each packed curve.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Unplaced", "U",
            "Curves that could not be placed.", GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Why",
            "Reason for each unplaced curve.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Preview", "B",
            "Outer sheet and hole preview curves.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Packing report.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            // ── Collect inputs ──────────────────────────────────────────────
            var parts         = new List<Curve>();
            var sheets        = new List<Curve>();
            GH_Structure<GH_Curve>? holesTree = null;
            var spacing       = 0.0;
            var rotationsDeg  = new List<double>();
            var sortModeVal   = 1;
            var tolerance     = 0.01;
            var seed          = 0;
            var run           = false;
            var maxCandidates = 300;
            var cornerModeVal = 0;

            if (!da.GetDataList(0, parts))   return;
            if (!da.GetDataList(1, sheets))  return;
            da.GetDataTree(2, out holesTree);
            da.GetData(3, ref spacing);
            da.GetDataList(4, rotationsDeg);
            da.GetData(5, ref sortModeVal);
            da.GetData(6, ref tolerance);
            da.GetData(7, ref seed);
            da.GetData(8, ref run);
            da.GetData(9, ref maxCandidates);
            da.GetData(10, ref cornerModeVal);

            if (!run)
            {
                // Preview only — no task.
                var preview = BuildPreview(sheets, holesTree, sheets.Count, tolerance);
                da.SetDataList(6, preview);
                da.SetData(7, "Run is false.");
                return;
            }

            if (sheets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one sheet outline is required.");
                return;
            }

            // Capture everything needed by the solver into local variables for the closure.
            var capturedParts   = parts.Select(c  => c.DuplicateCurve()).ToList();
            var capturedSheets  = sheets.Select(c => c.DuplicateCurve()).ToList();
            var holesBySheet    = BuildHolesBySheet(sheets, holesTree, sheets.Count, tolerance);
            var sortMode        = ToSortMode(sortModeVal);
            var cornerMode      = ToCornerMode(cornerModeVal);
            if (rotationsDeg.Count == 0) rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

            var capturedRots = rotationsDeg.ToList();
            var capturedHoles = holesBySheet.Select(l => (IReadOnlyList<Curve>)l.Select(c => c.DuplicateCurve()).ToList()).ToList();

            TaskList.Add(Task.Run(() =>
            {
                var solver = new IrregularSheetFillV2(
                    capturedSheets, capturedHoles,
                    spacing, capturedRots, tolerance,
                    sortMode, cornerMode, seed, maxCandidates);
                return solver.Pack(capturedParts);
            }));

            return;
        }

        // ── Retrieve task result ────────────────────────────────────────────
        PackingResult result;
        if (!GetSolveResults(da, out result))
        {
            // Synchronous fallback (first open or when caching is disabled).
            var parts2        = new List<Curve>();
            var sheets2       = new List<Curve>();
            GH_Structure<GH_Curve>? holesTree2 = null;
            var spacing2      = 0.0;
            var rotations2    = new List<double>();
            var sortModeVal2  = 1;
            var tolerance2    = 0.01;
            var seed2         = 0;
            var run2          = false;
            var maxCand2      = 300;
            var cornerMode2   = 0;

            if (!da.GetDataList(0, parts2))  return;
            if (!da.GetDataList(1, sheets2)) return;
            da.GetDataTree(2, out holesTree2);
            da.GetData(3, ref spacing2);
            da.GetDataList(4, rotations2);
            da.GetData(5, ref sortModeVal2);
            da.GetData(6, ref tolerance2);
            da.GetData(7, ref seed2);
            da.GetData(8, ref run2);
            da.GetData(9, ref maxCand2);
            da.GetData(10, ref cornerMode2);

            if (!run2)
            {
                var preview = BuildPreview(sheets2, holesTree2, sheets2.Count, tolerance2);
                da.SetDataList(6, preview);
                da.SetData(7, "Run is false.");
                return;
            }

            if (sheets2.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one sheet outline is required.");
                return;
            }

            var holesBySheet2 = BuildHolesBySheet(sheets2, holesTree2, sheets2.Count, tolerance2);
            if (rotations2.Count == 0) rotations2.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

            try
            {
                var solver = new IrregularSheetFillV2(
                    sheets2,
                    holesBySheet2.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                    spacing2, rotations2, tolerance2,
                    ToSortMode(sortModeVal2), ToCornerMode(cornerMode2), seed2, maxCand2);
                result = solver.Pack(parts2);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Freeform sheet packing failed: " + ex.Message);
                return;
            }
        }

        // ── Set outputs ─────────────────────────────────────────────────────
        if (result.UnplacedCurves.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.UnplacedCurves.Count} part(s) were not placed.");

        if (result.InvalidCount > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.InvalidCount} input curve(s) ignored — must be closed and planar.");

        if (result.RuntimeMilliseconds > 8000)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Packing took {result.RuntimeMilliseconds} ms. Reduce rotations or Max Candidates.");

        da.SetDataList(0, result.PackedCurves);
        da.SetDataList(1, result.Transforms);
        da.SetDataList(2, result.SourceIndices);
        da.SetDataList(3, result.SheetIndices);
        da.SetDataList(4, result.UnplacedCurves);
        da.SetDataList(5, result.FailureReasons);
        da.SetDataList(6, result.SheetPreviewCurves.Count > 0
            ? result.SheetPreviewCurves
            : new List<Curve>());
        da.SetData(7, result.Report);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static List<List<Curve>> BuildHolesBySheet(
        IReadOnlyList<Curve> sheets, GH_Structure<GH_Curve>? holesTree,
        int sheetCount, double tolerance)
    {
        var holes = new List<List<Curve>>(sheetCount);
        for (var i = 0; i < sheetCount; i++) holes.Add(new List<Curve>());

        if (holesTree == null || sheetCount == 0) return holes;

        foreach (var path in holesTree.Paths)
        {
            var si = path.Length > 0 ? path[0] : 0;
            if (si < 0 || si >= sheetCount) si = sheetCount == 1 ? 0 : -1;
            if (si < 0) continue;

            foreach (var item in holesTree.get_Branch(path))
            {
                if (item is GH_Curve gc && gc.Value != null)
                {
                    var hole = gc.Value.DuplicateCurve();
                    var cs   = FindContainingSheet(sheets, hole, tolerance);
                    holes[cs >= 0 ? cs : si].Add(hole);
                }
            }
        }
        return holes;
    }

    private static int FindContainingSheet(IReadOnlyList<Curve> sheets, Curve hole, double tolerance)
    {
        var pt  = AreaMassProperties.Compute(hole)?.Centroid ?? hole.GetBoundingBox(true).Center;
        var tol = Math.Max(tolerance, 0.01);
        for (var i = 0; i < sheets.Count; i++)
        {
            if (sheets[i].TryGetPlane(out var pl, tol))
            {
                var c = sheets[i].Contains(pt, pl, tol);
                if (c == PointContainment.Inside || c == PointContainment.Coincident) return i;
            }
            var c2 = sheets[i].Contains(pt, Rhino.Geometry.Plane.WorldXY, tol);
            if (c2 == PointContainment.Inside || c2 == PointContainment.Coincident) return i;
        }
        return -1;
    }

    private static List<Curve> BuildPreview(
        IReadOnlyList<Curve> sheets, GH_Structure<GH_Curve>? holesTree,
        int sheetCount, double tolerance)
    {
        var holes   = BuildHolesBySheet(sheets, holesTree, sheetCount, tolerance);
        var preview = new List<Curve>();
        for (var i = 0; i < sheets.Count; i++)
        {
            preview.Add(sheets[i].DuplicateCurve());
            if (i < holes.Count) preview.AddRange(holes[i].Select(h => h.DuplicateCurve()));
        }
        return preview;
    }

    private PackingSortMode ToSortMode(int v)
    {
        if (Enum.IsDefined(typeof(PackingSortMode), v)) return (PackingSortMode)v;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid sort mode. Using AreaDescending.");
        return PackingSortMode.AreaDescending;
    }

    private PackingCornerMode ToCornerMode(int v)
    {
        if (Enum.IsDefined(typeof(PackingCornerMode), v)) return (PackingCornerMode)v;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid corner mode. Using BottomLeft.");
        return PackingCornerMode.BottomLeft;
    }
}
