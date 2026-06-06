using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// **R3 PR 6** — unified Grasshopper façade for the four 2D irregular-sheet
/// solver variants. Single component, single ComponentGuid, single canvas
/// box; the user picks V1 / V2 / V3 / V506 with the Variant input.
///
/// This is the synchronous-only version. For the non-blocking variant,
/// see <see cref="IrregularSheetFillComponentAsync"/> (R3 PR 7 closure).
/// All four legacy V-suffixed components (including
/// <see cref="Pack2DIrregularSheetV506Component"/>) are now Obsolete and
/// scheduled for deletion in 0.8.0 per R3 plan PR 9.
///
/// Note on V1: the unified component hard-codes V1's two extra knobs
/// (<c>simplifyCurves=false, simplifyTolerance=tolerance</c>). Users who
/// need V1's curve-simplification features should use
/// <see cref="IrregularSheetFillRhino"/> directly or wait for a future
/// PR that exposes them as optional inputs.
/// </summary>
[Algorithm("No-fit polygon construction", "Burke, Hellier, Kendall, Whitwell 2007, European Journal of Operational Research 179(1):27-49 Complete and robust no-fit polygon generation for the irregular stock cutting problem", Doi = "10.1016/j.ejor.2006.03.011", WikiPath = "wiki/algorithms/surface_mosaicing/primitives/no_fit_polygon.md")]
[Algorithm("Bottom-left placement strategy", "Bennell and Oliveira 2008, Journal of the Operational Research Society 60(supp 1):S93-S105 The geometry of nesting problems: a tutorial", Doi = "10.1057/jors.2008.169", Note = "Variant dispatcher V1/V2/V3/V506; Frahan-original strategy selector")]
[DesignApplication(
    "Unified entry point for Frahan's four 2D irregular-sheet solver  variants (V1 / V2 / V3 / V506)",
    DesignFlow.BottomUp,
    Precedent = "Burke 2007 NFP + Bennell Oliveira 2008 irregular-shape packing review",
    Tolerance = "0 overlap; >= 85 % sheet utilisation")]
public sealed class IrregularSheetFillComponent : GH_Component
{
    public IrregularSheetFillComponent()
        : base("Frahan Sheet Pack (Unified)", "FreeNestU",
            "Unified entry point for Frahan's four 2D irregular-sheet solver " +
            "variants (V1 / V2 / V3 / V506). Pick the variant with the Variant " +
            "input; default is V506. Synchronous solve only - for the async " +
            "variant, use 'Frahan Sheet Pack (Unified Async)' / FreeNestUA.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C00B-1A2B-4C3D-9E4F-5A6B7C8D9E0B");
    protected override Bitmap? Icon => IconProvider.Load("IrregularSheet.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

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
            "0 V506 (default, recommended), 2 V2 freeform (recommended; V506 " +
            "delegates to this engine). 1 V1 polyline and 3 V3 adaptive " +
            "non-convex are RETAINED FOR REPRODUCIBILITY ONLY: the 2026-06-05 " +
            "--packbench benchmark measured V1 at 44.6% fill with 9 overlap " +
            "pairs and 3166 ms, and V3 at 21/24 placed (dominated by V2's " +
            "24/24 at the same fill). Prefer 0 or 2 for new work. See " +
            "outputs/2026-06-05/keep_or_cut/PACKING_BENCHMARK.md.",
            GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Boundary Mode", "BMode",
            "0 = off (geometric only). " +
            "1 = boundary-aware bias: parts with edges matching the sheet " +
            "outline / hole edges are placed first AND auto-rotated to align " +
            "with the matched boundary tangent; all candidate sources (boundary " +
            "anchors + interior grid) used. " +
            "2 = strict two-phase ring/interior: boundary-worthy parts use only " +
            "boundary-anchor candidates (true ring), then non-boundary parts " +
            "fill the interior. Falls back to all candidates if a phase is " +
            "saturated. " +
            "3 = uniform curve division: divide each boundary curve by arc " +
            "length, place each part at its assigned position with longest " +
            "edge tangent to the curve. Most predictable ring layout. Min " +
            "Boundary Affinity is ignored in this mode. " +
            "V506 only — other variants ignore.",
            GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Min Boundary Affinity", "BAff",
            "Edge-match score at or above which an edge is considered " +
            "boundary-worthy. Range [0, 1]; default 0.5. Only applies when " +
            "Boundary Mode > 0.",
            GH_ParamAccess.item, 0.5);
        pManager.AddNumberParameter("Discretization Tolerance", "DTol",
            "ToPolyline tolerance for both sheet boundaries and part curves. " +
            "Set to a positive value to control polyline density independently " +
            "of the geometric Tolerance. Default -1 (means: use Tolerance). " +
            "Lower = finer polylines, more detail captured but more matching " +
            "work. Higher = coarser, faster, but may miss small features.",
            GH_ParamAccess.item, -1.0);
        pManager.AddNumberParameter("Trim Tolerance", "TrimT",
            "Maximum part-to-part overlap depth (in document units) allowed " +
            "during placement. After all parts are placed, overlapping pairs " +
            "are boolean-differenced — the EARLIER-placed part wins, the " +
            "later-placed part loses material at the contact. Sheet outline " +
            "and holes are NEVER trimmed (only part-to-part collisions). " +
            "0 = trim off (strict no-overlap, legacy behavior). Default 0.1; " +
            "for meter-scale work try 0.1–1.0. Most useful with Boundary " +
            "Mode > 0 where parts get pushed close together along the " +
            "boundary; the trim cleans the contacts.",
            GH_ParamAccess.item, 0.1);
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
        pManager.AddTextParameter("Variant Used", "Vu",
            "Which variant actually ran (echoes the requested Variant input).",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Trimmed Curves", "Tc",
            "Per-part post-trim curves. Same length as Packed Curves. When " +
            "Trim Tolerance == 0, this output is empty. When > 0, each " +
            "entry is either the original packed curve (no trim happened) " +
            "or the boolean-difference result from being trimmed by an " +
            "earlier-placed neighbor.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Trim Adjacency", "Ta",
            "DataTree per packed part: branch i lists the SOURCE indices " +
            "of earlier-placed parts that trimmed Trimmed Curves[i]. Empty " +
            "branches indicate parts that were not trimmed.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
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
        int boundaryModeVal = 0;
        double minBoundaryAffinity = 0.5;
        double discretizationTol = -1.0;
        double trimTolerance = 0.1;

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
        da.GetData(12, ref boundaryModeVal);
        da.GetData(13, ref minBoundaryAffinity);
        da.GetData(14, ref discretizationTol);
        da.GetData(15, ref trimTolerance);

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

        var holesBySheet = SheetHolesUtil.BuildHolesBySheet(sheets, holesTree, sheets.Count, tolerance);
        if (rotationsDeg.Count == 0) rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

        var sortMode = ToSortMode(sortModeVal);
        var cornerMode = ToCornerMode(cornerModeVal);
        var variant = ToVariant(variantVal);

        PackingResult result;
        try
        {
            var solver = IrregularSheetFill.ForVariant(
                variant: variant,
                sheetOutlines: sheets,
                sheetHoles: holesBySheet.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                spacing: spacing,
                rotationsDeg: rotationsDeg,
                tolerance: tolerance,
                sortMode: sortMode,
                cornerMode: cornerMode,
                seed: seed,
                maxCandidates: maxCandidates,
                boundaryMode: boundaryModeVal,
                minBoundaryAffinity: minBoundaryAffinity,
                discretizationTolerance: discretizationTol,
                trimTolerance: trimTolerance);
            result = solver.Pack(parts);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Unified pack ({variant}) failed: {ex.Message}");
            return;
        }

        if (result.UnplacedCurves.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.UnplacedCurves.Count} part(s) could not be placed.");
        if (result.InvalidCount > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.InvalidCount} input curve(s) ignored - must be closed and planar.");
        if (result.RuntimeMilliseconds > 8000)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Packing took {result.RuntimeMilliseconds} ms (variant {variant}). Consider lowering rotations or Max Candidates.");

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
        da.SetData(8, variant.ToString());
        da.SetDataList(9, result.TrimmedCurves);
        var trimTree = new Grasshopper.Kernel.Data.GH_Structure<Grasshopper.Kernel.Types.GH_Integer>();
        for (int i = 0; i < result.TrimAdjacency.Count; i++)
        {
            var path = new Grasshopper.Kernel.Data.GH_Path(i);
            foreach (var srcIdx in result.TrimAdjacency[i])
                trimTree.Append(new Grasshopper.Kernel.Types.GH_Integer(srcIdx), path);
        }
        da.SetDataTree(10, trimTree);
    }

    // -- Helpers --------------------------------------------------------------
    // BuildHolesBySheet + BuildPreview moved to Frahan.GH.SheetHolesUtil
    // (Item F, 2026-05-04). The async unified component shares them.
    // Pack2DIrregularSheetV506Component keeps its own copy because its
    // BuildHolesBySheet additionally point-in-polygon-routes each hole
    // (FindContainingSheet); see SheetHolesUtil.cs for the full note.

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
