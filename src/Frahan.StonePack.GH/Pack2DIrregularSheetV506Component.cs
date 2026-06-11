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
/// Version 5.0.6 — fixes "curves off in the distance" caused by Rhino's right-to-left
/// transform multiplication convention reversing a composed compound transform.
/// Converts all inputs to polyline abstractions for robust containment on organic shapes.
/// Non-blocking async solve.
/// </summary>
[RelatedComponent("Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP)",
    Reason = "SUPERSEDED BY: Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap.")]
[Algorithm("NFP-assisted bottom-left irregular nesting",
    "Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). \"Complete and robust no-fit polygon generation for the irregular stock cutting problem.\" Eur. J. Oper. Res.",
    Doi = "10.1016/j.ejor.2006.03.011",
    WikiPath = "wiki/index/references.md#BurkeNFP2007")]
[Algorithm("Irregular-shape packing tutorial",
    "Bennell, J.A. & Oliveira, J.F. (2008). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(1)",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
public sealed class Pack2DIrregularSheetV506Component : GH_TaskCapableComponent<PackingResult>
{
    public Pack2DIrregularSheetV506Component()
        : base("Freeform Sheet Nest", "FreeNest",
            "PHASED OUT: superseded by Freeform Sheet Nest (Exact NFP) 'FreeNestX' — mean 53.9% waste-cut vs V506 at strict 0-overlap. Kept loadable for old canvases. " +
            "Packs closed planar parts into freeform sheet boundaries with holes using Frahan's V5.0.6 polygon-based nesting solver. " +
            "Supports organic sheet outlines, hole avoidance, spacing, rotation search, and non-blocking solve execution. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("D5E7A2B1-8C34-4F1E-A096-3B7F5D2E8A4C");
    // R3 PR 7: marked Obsolete now that the async unified component
    // (Frahan.GH.IrregularSheetFillComponentAsync, GUID AB12C00C-...)
    // covers V506's previous async-only differentiator. Replaced by
    // IrregularSheetFillComponentAsync (Variant=0). Will be removed in 0.8.0
    // per R3 plan PR 9. ComponentGuid unchanged so existing GH documents
    // continue to load and run.
    //
    // 2026-05-05: Exposure flipped to hidden alongside Bug B-2D-001 fix.
    // The unified components now do PIP-first hole routing (parity with
    // V506's private BuildHolesBySheet), so V506 standalone is no longer
    // needed as the user's escape hatch for multi-sheet hole drops.
    // Existing GH documents with this component on canvas still load and
    // run; the component is just hidden from the Frahan/2D Packing tab.
    public override bool Obsolete => true;
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override Bitmap? Icon => IconProvider.Load("Pack2D.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar part curves to pack. Any curve type — freeform, arc, polyline.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet boundary curves. Any curve type, including organic freeform.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc.",
            GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap",
            "Clearance between parts and between parts and boundaries. Minimum enforced: 0.1.",
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
            var parts        = new List<Curve>();
            var sheets       = new List<Curve>();
            GH_Structure<GH_Curve>? holesTree = null;
            var spacing      = 0.1;
            var rotationsDeg = new List<double>();
            var sortModeVal  = 1;
            var tolerance    = 0.01;
            var seed         = 0;
            var run          = false;
            var maxCandidates = 300;
            var cornerModeVal = 0;

            if (!da.GetDataList(0, parts))  return;
            if (!da.GetDataList(1, sheets)) return;
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

            var capturedParts  = parts.Select(c  => c.DuplicateCurve()).ToList();
            var capturedSheets = sheets.Select(c => c.DuplicateCurve()).ToList();
            var holesBySheet   = BuildHolesBySheet(sheets, holesTree, sheets.Count, tolerance);
            var sortMode       = ToSortMode(sortModeVal);
            var cornerMode     = ToCornerMode(cornerModeVal);
            if (rotationsDeg.Count == 0) rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

            var capturedRots  = rotationsDeg.ToList();
            var capturedHoles = holesBySheet
                .Select(l => (IReadOnlyList<Curve>)l.Select(c => c.DuplicateCurve()).ToList())
                .ToList();

            TaskList.Add(Task.Run(() =>
            {
                var solver = new IrregularSheetFillV506(
                    capturedSheets, capturedHoles,
                    spacing, capturedRots, tolerance,
                    sortMode, cornerMode, seed, maxCandidates);
                return solver.Pack(capturedParts);
            }));
            return;
        }

        PackingResult result;
        if (!GetSolveResults(da, out result))
        {
            // Synchronous fallback.
            var parts2        = new List<Curve>();
            var sheets2       = new List<Curve>();
            GH_Structure<GH_Curve>? holesTree2 = null;
            var spacing2      = 0.1;
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
                da.SetDataList(6, BuildPreview(sheets2, holesTree2, sheets2.Count, tolerance2));
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
                var solver = new IrregularSheetFillV506(
                    sheets2,
                    holesBySheet2.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                    spacing2, rotations2, tolerance2,
                    ToSortMode(sortModeVal2), ToCornerMode(cornerMode2), seed2, maxCand2);
                result = solver.Pack(parts2);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "V5.0.6 packing failed: " + ex.Message);
                return;
            }
        }

        if (result.UnplacedCurves.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.UnplacedCurves.Count} part(s) could not be placed.");

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
    //
    // Item F (2026-05-04): the unified components moved their copies of these
    // helpers to Frahan.GH.SheetHolesUtil. V506 keeps its own intentionally:
    // BuildHolesBySheet here additionally point-in-polygon-routes each hole
    // through FindContainingSheet, so the sheet a hole ends up under can be
    // different from its tree path. The unified components do not (yet)
    // replicate that. V506 is Obsolete and will be removed in 0.8.0 per
    // R3 plan PR 9, so the divergence is short-lived.

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
