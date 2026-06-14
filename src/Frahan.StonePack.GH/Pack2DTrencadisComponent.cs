#nullable disable
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
/// Skeleton GH component for the Trencadís packer (F-2D-002).
///
/// Trencadís is the "broken-tile" mosaic technique (Gaudí). Pieces
/// overlap slightly and get chipped (boolean-differenced) to fit;
/// grout fills the gaps. This component is purpose-built for that —
/// trim-aware is the CORE primitive here, not an add-on.
///
/// Why a separate component (vs another V506 mode):
///   • Trim plumbing in V506 (Half J) contaminated spacing semantics
///     and never integrated cleanly with the boundary-rail logic.
///     Slated for rollback. Trencadís keeps the trim idea but ships it
///     in its own surface so the placement loop can be designed
///     around overlap acceptance rather than overlap rejection.
///   • The user wants a one-click "trencadís mode" without having to
///     remember which V506 inputs to set. UX is the deliverable.
///
/// Status (2026-05-06): SKELETON. Inputs/outputs registered, solver
/// returns an empty result with a "not implemented" report. See
/// <see cref="TrencadisFill"/> TODO list for the implementation
/// roadmap.
/// </summary>
[Algorithm("Trencadis greedy pack basic", "Gaudi Park Guell broken-tile mosaic technique")]
[Algorithm("NFP boundary slide", "Minkowski-difference arc-length sampler")]
[DesignApplication(
    "Trencadís ('broken-tile') 2D mosaic packer",
    DesignFlow.BottomUp,
    Precedent = "Gaudi Park Guell Trencadis; Battiato 2013 CVD+GVF Trencadis synthesis",
    CardSet = "wiki/research/hitl_cards/em_2d_trencadis_solve/")]
public sealed class Pack2DTrencadisComponent : FrahanComponentBase
{
    public Pack2DTrencadisComponent()
        : base("Frahan Trencadís Pack", "Trencadis",
            "Trencadís ('broken-tile') 2D mosaic packer. Places irregular " +
            "pieces with bounded overlap, then boolean-differences " +
            "the overlapping bits so pieces butt edge-to-edge with " +
            "characteristic chipped fits. Optional grout offset leaves " +
            "the mortar gap. SKELETON — returns empty result.",
            "Frahan", "Trencadis")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D00002-CADC-4F2D-9001-7E60CADA15A0");
    protected override Bitmap Icon => IconProvider.Load("Trencadis.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar shard curves to pack. Irregular shapes welcome — " +
            "trencadís is at its best with non-uniform pieces.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet boundary curves. The mosaic is built " +
            "inside these.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree. Branch {0} = sheet 0, {1} = sheet 1, etc.",
            GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap",
            "Pre-trim part-to-part clearance. The trim post-pass removes " +
            "everything inside this clearance, so think of it as the " +
            "MAXIMUM grout gap (actual gap = Grout, see below).",
            GH_ParamAccess.item, 0.05);
        pManager.AddNumberParameter("Rotations", "R",
            "Allowed rotation angles in degrees. Default 0/45/90/135 to " +
            "encourage varied edge orientation typical of trencadís work.",
            GH_ParamAccess.list);
        pManager[4].Optional = true;
        pManager.AddNumberParameter("Tolerance", "T",
            "Geometric tolerance for containment / collision / boolean " +
            "difference.", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Seed", "Seed",
            "0 = deterministic; non-zero changes tie-breaking randomisation " +
            "of placement order.", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("Run", "Run",
            "Set to true to execute packing.", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Max Candidates", "Max",
            "Candidate budget per part per rotation. Trencadís typically " +
            "needs MORE candidates than V506 because trim acceptance " +
            "expands the feasible set. 0 = default (600).",
            GH_ParamAccess.item, 600);
        pManager.AddNumberParameter("Trim Tolerance", "TrimT",
            "Maximum allowed part-to-part overlap depth (document units) " +
            "during placement. Larger = more aggressive chipping, denser " +
            "pack. Default 0.2; for meter-scale 0.1–1.0.",
            GH_ParamAccess.item, 0.2);
        pManager.AddNumberParameter("Grout", "Gr",
            "Inward offset applied to each piece AFTER trim, to leave the " +
            "characteristic trencadís mortar gap. 0 = no grout (raw " +
            "edge-to-edge). Default 0.02.", GH_ParamAccess.item, 0.02);
        pManager.AddIntegerParameter("Boundary Mode", "BMode",
            "0 = off (interior fill only). " +
            "1 = boundary-aware bias: shards with edges matching the sheet " +
            "or hole edges are placed first AND auto-rotated to align with " +
            "the matched boundary tangent. All candidate sources used. " +
            "2 = strict two-phase ring/interior — boundary-worthy shards " +
            "use only boundary-anchor candidates first (true ring), then " +
            "non-boundary shards fill the interior. Falls back to all " +
            "candidates if a phase is saturated. " +
            "3 = uniform curve division — divide each boundary curve by " +
            "arc length; place each shard with longest edge tangent to " +
            "the curve at its assigned position.",
            GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Min Boundary Affinity", "BAff",
            "Edge-match score at or above which a shard is considered " +
            "boundary-worthy. Range [0, 1]. Only applies when Boundary " +
            "Mode > 0.", GH_ParamAccess.item, 0.5);
        pManager.AddNumberParameter("Cut Budget", "Cut",
            "Battiato 2013 §4 cumulative-cut cap as a fraction of each " +
            "shard's area. T_N = budget on a NEW shard's total chipping " +
            "across all neighbours; T_P = budget on a PLACED shard " +
            "(derived as Cut/2); single-cut caps S_N (Cut/2) and S_P " +
            "(Cut/4) cap any one chip. Default 0.35 matches Battiato's " +
            "recommended T_N. Lower → less aggressive chipping, more " +
            "shard-shape preservation. 0 → no cuts allowed (strict " +
            "no-overlap; defeats the trencadís technique).",
            GH_ParamAccess.item, 0.35);
        pManager.AddBooleanParameter("Use CVD Seeds", "CVD",
            "Initialize per-sheet placement using CVD-Lloyd seed points " +
            "(blue-noise distribution). Improves coverage uniformity vs " +
            "the bbox-corner default starting point.",
            GH_ParamAccess.item, true);
        pManager.AddBooleanParameter("Use GVF Orientation", "GVF",
            "Compute Gradient Vector Flow over each sheet to bias shard " +
            "rotation toward the local boundary tangent. Pieces follow " +
            "curves like Gaudí's columns. Slower than the discrete " +
            "rotation list alone but produces the flow-line look.",
            GH_ParamAccess.item, true);
        pManager.AddNumberParameter("GVF Smoothness", "GMu",
            "GVF μ (smoothness regularizer). Lower (0.05–0.15) → field " +
            "follows boundary closely, sharper. Higher (0.3–0.5) → " +
            "smoother propagation into interior. Default 0.2 (Battiato " +
            "2008).", GH_ParamAccess.item, 0.2);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Trencadís Pieces", "C",
            "Final shard curves after trim + grout. Use these for " +
            "downstream rendering / fabrication.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Pre-Trim Pieces", "C0",
            "Placed shards BEFORE the trim post-pass. Useful for " +
            "debugging which piece chipped which.", GH_ParamAccess.list);
        pManager.AddGenericParameter("Transforms", "X",
            "Placement transforms (per source curve).", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src",
            "Original input curve index for each placed shard.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sheet Indices", "Sh",
            "Sheet index used for each placed shard.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Trim Adjacency", "Ta",
            "DataTree per shard: branch i lists the SOURCE indices of " +
            "earlier-placed shards that chipped Trencadís Pieces[i].",
            GH_ParamAccess.tree);
        pManager.AddCurveParameter("Unplaced", "U",
            "Shards that could not be placed even with trim tolerance.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Why",
            "Reason for each unplaced shard.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Preview", "B",
            "Outer sheet and hole preview curves.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Trencadís packing report (counts, timings, trim events).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var parts = new List<Curve>();
        var sheets = new List<Curve>();
        GH_Structure<GH_Curve> holesTree = null;
        double spacing = 0.05;
        var rotationsDeg = new List<double>();
        double tolerance = 0.01;
        int seed = 0;
        bool run = false;
        int maxCandidates = 600;
        double trimTolerance = 0.2;
        double grout = 0.02;
        int boundaryMode = 0;
        double minBoundaryAffinity = 0.5;
        double cutBudget = 0.35;
        bool useCvdSeeds = true;
        bool useGvf = true;
        double gvfMu = 0.2;

        if (!da.GetDataList(0, parts)) return;
        if (!da.GetDataList(1, sheets)) return;
        da.GetDataTree(2, out holesTree);
        da.GetData(3, ref spacing);
        da.GetDataList(4, rotationsDeg);
        da.GetData(5, ref tolerance);
        da.GetData(6, ref seed);
        da.GetData(7, ref run);
        da.GetData(8, ref maxCandidates);
        da.GetData(9, ref trimTolerance);
        da.GetData(10, ref grout);
        da.GetData(11, ref boundaryMode);
        da.GetData(12, ref minBoundaryAffinity);
        da.GetData(13, ref cutBudget);
        da.GetData(14, ref useCvdSeeds);
        da.GetData(15, ref useGvf);
        da.GetData(16, ref gvfMu);

        if (!run)
        {
            da.SetDataList(8, SheetHolesUtil.BuildPreview(sheets, holesTree, sheets.Count, tolerance));
            da.SetData(9, "Run is false. Trencadís MVP — set Run=true to pack.");
            return;
        }

        if (sheets.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one sheet outline is required.");
            return;
        }

        if (rotationsDeg.Count == 0)
            rotationsDeg.AddRange(new[] { 0.0, 45.0, 90.0, 135.0 });

        var holesBySheet = SheetHolesUtil.BuildHolesBySheet(
            sheets, holesTree, sheets.Count, tolerance);

        PackingResult result;
        try
        {
            var solver = new TrencadisFill(
                sheetOutlines: sheets,
                sheetHoles: holesBySheet.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                spacing: spacing,
                rotationsDeg: rotationsDeg,
                tolerance: tolerance,
                seed: seed,
                maxCandidates: maxCandidates,
                trimTolerance: trimTolerance,
                grout: grout,
                boundaryMode: boundaryMode,
                minBoundaryAffinity: minBoundaryAffinity,
                cutBudget: cutBudget,
                useCvdSeeds: useCvdSeeds,
                useGvf: useGvf,
                gvfMu: gvfMu);
            result = solver.Pack(parts);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Trencadís pack failed: {ex.Message}");
            return;
        }


        // Final pieces = trimmed (preferred) else pre-trim (until implemented).
        var finalPieces = result.TrimmedCurves.Count > 0
            ? result.TrimmedCurves
            : result.PackedCurves;

        da.SetDataList(0, finalPieces);
        da.SetDataList(1, result.PackedCurves);
        da.SetDataList(2, result.Transforms);
        da.SetDataList(3, result.SourceIndices);
        da.SetDataList(4, result.SheetIndices);

        var trimTree = new GH_Structure<GH_Integer>();
        for (int i = 0; i < result.TrimAdjacency.Count; i++)
        {
            var path = new GH_Path(i);
            foreach (var srcIdx in result.TrimAdjacency[i])
                trimTree.Append(new GH_Integer(srcIdx), path);
        }
        da.SetDataTree(5, trimTree);

        da.SetDataList(6, result.UnplacedCurves);
        da.SetDataList(7, result.FailureReasons);
        da.SetDataList(8, result.SheetPreviewCurves.Count > 0
            ? result.SheetPreviewCurves
            : new List<Curve>());
        da.SetData(9, result.Report);
    }
}
