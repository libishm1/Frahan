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
/// **R3 PR 6+ async** — non-blocking variant of
/// <see cref="IrregularSheetFillComponent"/>. Same input/output shape, same
/// Variant routing, but inherits <see cref="GH_TaskCapableComponent{T}"/>
/// so the solve runs on a background thread and Grasshopper stays
/// responsive while V2/V3/V506 packs are in flight.
///
/// Once this component lands, <see cref="Pack2DIrregularSheetV506Component"/>
/// can also be marked Obsolete (closes R3 PR 7), since the unified async
/// path covers V506's previous async-only differentiator.
///
/// V1 (IrregularSheetFillRhino) Pack is sync-only; selecting Variant=1
/// here still runs on a background thread (via Task.Run wrapping the sync
/// call), so Grasshopper stays responsive even though V1 itself doesn't
/// honor cancellation.
/// </summary>
[Algorithm("NFP-assisted bottom-left irregular nesting",
    "Burke, E., Hellier, R., Kendall, G., Whitwell, G. (2007). \"Complete and robust no-fit polygon generation for the irregular stock cutting problem.\" Eur. J. Oper. Res.",
    Doi = "10.1016/j.ejor.2006.03.011",
    WikiPath = "wiki/index/references.md#BurkeNFP2007")]
[Algorithm("Irregular-shape packing tutorial",
    "Bennell, J.A. & Oliveira, J.F. (2008). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(1)",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
[Algorithm("Variant dispatcher", "Frahan-original",
    Note = "routes Variant=V1/V2/V3/V506 onto a background thread; the dispatch is Frahan-original")]
public sealed class IrregularSheetFillComponentAsync : GH_TaskCapableComponent<PackingResult>
{
    public IrregularSheetFillComponentAsync()
        : base("Frahan Sheet Pack (Unified Async)", "FreeNestUA",
            "Async variant of Frahan Sheet Pack (Unified). Same Variant routing " +
            "as the sync version but runs on a background thread so Grasshopper " +
            "stays responsive during long packs. Pick the variant with the Variant " +
            "input; default is V506. Implements NFP-assisted bottom-left nesting (Burke et al. 2007).",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C00C-1A2B-4C3D-9E4F-5A6B7C8D9E0C");
    protected override Bitmap? Icon => IconProvider.Load("IrregularSheet.png");
    // 2026-05-05: retired (Obsolete + Hidden). The two-pass GH_TaskCapableComponent
    // dispatch + GH-thread input pre-duplication + cancellation-token churn made
    // this component perceptibly slower than the sync unified component on
    // typical Frahan workloads (a handful of packs per session, packs completing
    // in seconds rather than minutes). The original design intent — keeping GH
    // responsive during long-running packs — does not pay off when packs are
    // bounded and the user does not interact with GH during the solve.
    // ComponentGuid unchanged so existing GH documents load and run; the
    // component is just removed from the Frahan/2D Packing palette so new
    // canvases only see the sync unified component.
    public override bool Obsolete => true;
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar part curves to pack.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet boundary curves.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc.",
            GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap",
            "Clearance between parts and between parts and boundaries.",
            GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Rotations", "R",
            "Allowed rotation angles in degrees (default 0, 90, 180, 270).",
            GH_ParamAccess.list, 0.0);
        pManager.AddIntegerParameter("Sort Mode", "M",
            "0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓.",
            GH_ParamAccess.item, 1);
        pManager.AddNumberParameter("Tolerance", "T",
            "Geometric tolerance for containment and collision.",
            GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Seed", "Seed",
            "0 = deterministic; non-zero changes tie-breaking.",
            GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run",
            "Set to true to execute packing.", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Max Candidates", "Max",
            "Candidate budget per part per rotation.",
            GH_ParamAccess.item, 300);
        pManager.AddIntegerParameter("Corner Mode", "Cnr",
            "0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight.",
            GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Variant", "V",
            "0 V506 (default), 1 V1 polyline, 2 V2 freeform, 3 V3 adaptive non-convex.",
            GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Packed Curves", "C", "Placed part curves.", GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "X",
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
        pManager.AddTextParameter("Variant Used", "Vu",
            "Which variant actually ran (echoes the requested Variant input).",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            // Capture inputs on the GH thread, then start a Task that runs the solve.
            var parts = new List<Curve>();
            var sheets = new List<Curve>();
            GH_Structure<GH_Curve>? holesTree = null;
            double spacing = 0.1;
            var rotationsDeg = new List<double>();
            int sortModeVal = 1;
            double tolerance = 0.01;
            int seed = 0;
            bool run = false;
            int maxCandidates = 300;
            int cornerModeVal = 0;
            int variantVal = 0;

            if (!da.GetDataList(0, parts)) return;
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
            da.GetData(11, ref variantVal);

            if (!run)
            {
                da.SetDataList(6, SheetHolesUtil.BuildPreview(sheets, holesTree, sheets.Count, tolerance));
                da.SetData(7, "Run is false.");
                da.SetData(8, ToVariant(variantVal).ToString());
                return;
            }

            if (sheets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "At least one sheet outline is required.");
                return;
            }

            // Capture deeply so the background thread doesn't see GH-thread mutations.
            var capturedParts = parts.Select(c => c.DuplicateCurve()).ToList();
            var capturedSheets = sheets.Select(c => c.DuplicateCurve()).ToList();
            var holesBySheet = SheetHolesUtil.BuildHolesBySheet(sheets, holesTree, sheets.Count, tolerance);
            if (rotationsDeg.Count == 0) rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
            var capturedRots = rotationsDeg.ToList();
            var capturedHoles = holesBySheet
                .Select(l => (IReadOnlyList<Curve>)l.Select(c => c.DuplicateCurve()).ToList())
                .ToList();
            var sortMode = ToSortMode(sortModeVal);
            var cornerMode = ToCornerMode(cornerModeVal);
            var variant = ToVariant(variantVal);

            TaskList.Add(Task.Run(() =>
            {
                var solver = IrregularSheetFill.ForVariant(
                    variant: variant,
                    sheetOutlines: capturedSheets,
                    sheetHoles: capturedHoles,
                    spacing: spacing,
                    rotationsDeg: capturedRots,
                    tolerance: tolerance,
                    sortMode: sortMode,
                    cornerMode: cornerMode,
                    seed: seed,
                    maxCandidates: maxCandidates);
                return solver.Pack(capturedParts);
            }));
            return;
        }

        PackingResult result;
        if (!GetSolveResults(da, out result))
        {
            // Synchronous fallback (e.g., when called outside a GH task scheduler).
            int variantVal2 = 0;
            da.GetData(11, ref variantVal2);
            var variantFallback = ToVariant(variantVal2);

            var parts2 = new List<Curve>();
            var sheets2 = new List<Curve>();
            GH_Structure<GH_Curve>? holesTree2 = null;
            double spacing2 = 0.1;
            var rotations2 = new List<double>();
            int sortModeVal2 = 1;
            double tolerance2 = 0.01;
            int seed2 = 0;
            bool run2 = false;
            int maxCand2 = 300;
            int cornerMode2 = 0;

            if (!da.GetDataList(0, parts2)) return;
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
                da.SetDataList(6, SheetHolesUtil.BuildPreview(sheets2, holesTree2, sheets2.Count, tolerance2));
                da.SetData(7, "Run is false.");
                da.SetData(8, variantFallback.ToString());
                return;
            }

            if (sheets2.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "At least one sheet outline is required.");
                return;
            }

            var holesBySheet2 = SheetHolesUtil.BuildHolesBySheet(sheets2, holesTree2, sheets2.Count, tolerance2);
            if (rotations2.Count == 0) rotations2.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

            try
            {
                var solver = IrregularSheetFill.ForVariant(
                    variant: variantFallback,
                    sheetOutlines: sheets2,
                    sheetHoles: holesBySheet2.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                    spacing: spacing2,
                    rotationsDeg: rotations2,
                    tolerance: tolerance2,
                    sortMode: ToSortMode(sortModeVal2),
                    cornerMode: ToCornerMode(cornerMode2),
                    seed: seed2,
                    maxCandidates: maxCand2);
                result = solver.Pack(parts2);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Unified async pack ({variantFallback}) failed: {ex.Message}");
                return;
            }

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
            da.SetData(8, variantFallback.ToString());
            return;
        }

        // Async result available - emit it.
        int variantValEcho = 0;
        da.GetData(11, ref variantValEcho);
        var variantEcho = ToVariant(variantValEcho);

        if (result.UnplacedCurves.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.UnplacedCurves.Count} part(s) could not be placed.");
        if (result.InvalidCount > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.InvalidCount} input curve(s) ignored - must be closed and planar.");
        if (result.RuntimeMilliseconds > 8000)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Packing took {result.RuntimeMilliseconds} ms (variant {variantEcho}). Consider lowering rotations or Max Candidates.");

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
        da.SetData(8, variantEcho.ToString());
    }

    // -- Helpers --------------------------------------------------------------
    // BuildHolesBySheet + BuildPreview moved to Frahan.GH.SheetHolesUtil
    // (Item F, 2026-05-04). Sync unified component shares them.

    private PackingSortMode ToSortMode(int v)
    {
        if (Enum.IsDefined(typeof(PackingSortMode), v)) return (PackingSortMode)v;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
            $"Invalid Sort Mode {v}. Using AreaDescending.");
        return PackingSortMode.AreaDescending;
    }

    private PackingCornerMode ToCornerMode(int v)
    {
        if (Enum.IsDefined(typeof(PackingCornerMode), v)) return (PackingCornerMode)v;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
            $"Invalid Corner Mode {v}. Using BottomLeft.");
        return PackingCornerMode.BottomLeft;
    }

    private SolverVariant ToVariant(int v)
    {
        switch (v)
        {
            case 0: return SolverVariant.V506;
            case 1: return SolverVariant.V1;
            case 2: return SolverVariant.V2;
            case 3: return SolverVariant.V3;
            default:
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Invalid Variant {v}. Using V506.");
                return SolverVariant.V506;
        }
    }
}
