using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// EdgeMatch-powered Trencadís packer. A first-class alternative to
/// the existing Pack2DTrencadis family (skeleton / catalog / dynamic /
/// pipeline) — instead of CVD seeding + GVF orientation + Battiato
/// chipping, this component runs the deterministic
/// <see cref="AssemblySolver"/> against the sheet outline as anchored
/// frame and treats each part curve as a fracture-shaped shard.
/// Useful when the part list comes from real scanned fragments (e.g.
/// quarry fracture cuts) and you want edge-to-edge fit rather than
/// synthesised fragmentation.
///
/// Use the other Trencadís components for generative work (synthesise
/// a mosaic from scratch); use this one when the geometry already
/// exists and you want it placed by edge complementarity.
/// </summary>
[Algorithm("EdgeMatch-powered Trencadis pack", "Frahan-original alternative to Battiato 2013 CVD+GVF stack", Note = "Uses 5-stage EdgeMatch pipeline as the placement engine")]
[Algorithm("Beam-search assembly solver", "Frahan-original deterministic beam search", Note = "Drives panel placement in Trencadis mode")]
[DesignApplication(
    "Trencadís packer driven by the EdgeMatch beam-search solver",
    DesignFlow.BottomUp,
    Precedent = "Gaudi Park Guell Trencadis (1900-1914); Battiato 2013 CVD+GVF Trencadis synthesis; IAAC MRAC RoboMosaic 2022-23",
    Tolerance = "mean joint Hausdorff <= 5 mm",
    CardSet = "wiki/research/hitl_cards/em_2d_trencadis_solve/")]
public sealed class TrencadisEdgeMatchComponent : GH_Component
{
    public TrencadisEdgeMatchComponent()
        : base("Frahan Trencadís EdgeMatch", "TrencEM",
            "Trencadís packer driven by the EdgeMatch beam-search solver. " +
            "Each sheet outline becomes an anchored frame; parts are placed " +
            "by their complementary edges against the frame and against " +
            "previously-placed parts. Output is deterministic for fixed " +
            "input order.",
            "Frahan", "Trencadis")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D0000A-CADC-4F2D-900A-7E60CADA15A0");
    protected override Bitmap? Icon => IconProvider.Load("EdgeMatchSolve.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar shard curves to pack.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed sheet boundary curves. Each becomes an anchored frame " +
            "for one independent EdgeMatch run.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Joint Width", "J",
            "Allowed mean edge-to-edge gap (document units). Mapped onto " +
            "EdgeMatch's residual threshold: matches further than this are " +
            "rejected. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        pManager.AddNumberParameter("Sample Spacing", "Sp",
            "Arc-length sample spacing along each contour. Match scanner " +
            "resolution.", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Break Angle", "Ba",
            "Curvature break-point threshold in degrees per window.",
            GH_ParamAccess.item, 18.0);
        pManager.AddNumberParameter("Min Segment Length", "Ms",
            "Below this chord length a segment is treated as noise.",
            GH_ParamAccess.item, 8.0);
        pManager.AddIntegerParameter("Beam Width", "Bw",
            "Concurrent beam states retained per iteration. 16 recommended " +
            "for Trencadís (more local minima than wood).",
            GH_ParamAccess.item, 16);
        pManager.AddIntegerParameter("Max Iterations", "Mi",
            "Outer-loop iteration cap.", GH_ParamAccess.item, 1000);
        pManager.AddBooleanParameter("Run", "R",
            "Execute the solver.", GH_ParamAccess.item, false);
        // Appended LAST (after Run) so existing canvases keep their wiring.
        pManager.AddBooleanParameter("Non-Crossing", "Nc",
            "Order-preserving rim correspondence. FALSE (default) = free " +
            "nearest-point ICP (unchanged behaviour). TRUE = monotone, " +
            "non-crossing point pairing between shard edges; more robust on " +
            "wiggly / noisy fracture edges where free matching tangles.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Placed Pieces", "C",
            "Shard contours transformed into their solved placements. " +
            "Sheet outlines are not included (they are the identity frame).",
            GH_ParamAccess.list);
        pManager.AddGenericParameter("Transforms", "X",
            "Per-piece rigid transform.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src",
            "Original Parts list index for each placed piece.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sheet Indices", "Sh",
            "Sheet list index this piece was placed onto.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Unplaced", "U",
            "Source-curve copies of parts that did not find a match on any sheet.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Residuals", "Re",
            "Per-placement ICP residual.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Total Residual", "Tr",
            "Sum of all per-placement residuals across all sheets.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "Rp",
            "Human-readable per-sheet summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var partCurves = new List<Curve>();
        var sheetCurves = new List<Curve>();
        double jointWidth = 0.5;
        double sampleSpacing = 1.0;
        double breakAngleDeg = 18.0;
        double minSegmentLength = 8.0;
        int beamWidth = 16;
        int maxIterations = 1000;
        bool run = false;
        bool nonCrossing = false;

        if (!da.GetDataList(0, partCurves)) return;
        if (!da.GetDataList(1, sheetCurves)) return;
        da.GetData(2, ref jointWidth);
        da.GetData(3, ref sampleSpacing);
        da.GetData(4, ref breakAngleDeg);
        da.GetData(5, ref minSegmentLength);
        da.GetData(6, ref beamWidth);
        da.GetData(7, ref maxIterations);
        da.GetData(8, ref run);
        // Backward-compatible read: only present on canvases saved after this
        // input was added.
        if (Params.Input.Count > 9) da.GetData(9, ref nonCrossing);

        if (!run)
        {
            da.SetData(7, "Run is false; toggle to execute.");
            return;
        }

        if (sheetCurves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one Sheet Outline is required.");
            return;
        }

        // Convert all parts to PolylineCurves once. The same source set
        // is re-Panel'd per sheet (Panel mutates AppliedTransform; sharing
        // across sheets would corrupt state).
        var partPolylines = new List<PolylineCurve?>(partCurves.Count);
        for (int i = 0; i < partCurves.Count; i++)
        {
            if (TryToPolylineCurve(partCurves[i], out var pc) && pc.IsClosed)
            {
                partPolylines.Add(pc);
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Part {i} is not a closed polyline-convertible curve; skipping.");
                partPolylines.Add(null);
            }
        }

        var placedCurves = new List<Curve>();
        var transforms = new List<object>();
        var sourceIndices = new List<int>();
        var sheetIndices = new List<int>();
        var residuals = new List<double>();
        double totalResidual = 0.0;
        var placedSourceIdxSet = new HashSet<int>();
        var report = new StringBuilder();

        var segOpt = new SegmenterOptions
        {
            SampleSpacing = sampleSpacing,
            BreakAngleDeg = breakAngleDeg,
            MinSegmentLength = minSegmentLength,
        };
        var segOpt3D = new SegmenterOptions3D
        {
            SampleSpacing = sampleSpacing,
            BreakAngleDeg = breakAngleDeg,
            MinSegmentLength = minSegmentLength,
        };

        for (int sheetIdx = 0; sheetIdx < sheetCurves.Count; sheetIdx++)
        {
            if (!TryToPolylineCurve(sheetCurves[sheetIdx], out var sheetPc) || !sheetPc.IsClosed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Sheet {sheetIdx} is not a closed polyline-convertible curve; skipping.");
                report.AppendLine($"Sheet {sheetIdx}: SKIPPED (not closed polyline).");
                continue;
            }

            var framePanel = new Panel($"frame{sheetIdx}", sheetPc, PanelKind.Frame);

            // Build a fresh Panel per part for this sheet so AppliedTransform
            // starts at identity and is independent across sheets.
            var partPanels = new List<Panel>();
            var partPanelToSourceIdx = new List<int>();
            for (int i = 0; i < partPolylines.Count; i++)
            {
                if (partPolylines[i] == null) continue;
                if (placedSourceIdxSet.Contains(i)) continue;
                partPanels.Add(new Panel($"sheet{sheetIdx}/s{i:D4}", partPolylines[i]!, PanelKind.Shard));
                partPanelToSourceIdx.Add(i);
            }

            if (partPanels.Count == 0)
            {
                report.AppendLine($"Sheet {sheetIdx}: no remaining parts to place.");
                continue;
            }

            var index = new SegmentHashIndex();
            AddSegmentsFor(framePanel, segOpt, segOpt3D, index);
            foreach (var p in partPanels) AddSegmentsFor(p, segOpt, segOpt3D, index);

            var asmOpt = new AssemblyOptions
            {
                BeamWidth = Math.Max(1, beamWidth),
                MaxIterations = Math.Max(1, maxIterations),
                ResidualThreshold = jointWidth,
                NonCrossingCorrespondence = nonCrossing,
            };
            var solver = new AssemblySolver(index, asmOpt, segOpt, segOpt3D);
            var state = solver.Solve(new[] { framePanel }, partPanels);

            int placedThisSheet = 0;
            foreach (var panel in state.PlacedPanels)
            {
                if (panel.Id == framePanel.Id) continue;

                int panelIdx = partPanels.IndexOf(panel);
                if (panelIdx < 0) continue;
                int sourceIdx = partPanelToSourceIdx[panelIdx];

                var t = state.AppliedTransforms[panel.Id];
                var c = (Curve)panel.SourceContour.DuplicateCurve();
                c.Transform(t);
                placedCurves.Add(c);
                transforms.Add(new GH_Transform(t));
                sourceIndices.Add(sourceIdx);
                sheetIndices.Add(sheetIdx);
                placedSourceIdxSet.Add(sourceIdx);
                placedThisSheet++;
            }
            foreach (var h in state.History) residuals.Add(h.Residual);
            totalResidual += state.TotalResidual;

            report.AppendLine(
                $"Sheet {sheetIdx}: placed {placedThisSheet} of {partPanels.Count} candidates " +
                $"(residual {state.TotalResidual:F4}).");
        }

        var unplaced = new List<Curve>();
        for (int i = 0; i < partPolylines.Count; i++)
        {
            if (partPolylines[i] == null) continue;
            if (placedSourceIdxSet.Contains(i)) continue;
            unplaced.Add((Curve)partPolylines[i]!.DuplicateCurve());
        }

        report.AppendLine(
            $"Totals: {placedCurves.Count} placed, {unplaced.Count} unplaced, residual {totalResidual:F4}.");

        da.SetDataList(0, placedCurves);
        da.SetDataList(1, transforms);
        da.SetDataList(2, sourceIndices);
        da.SetDataList(3, sheetIndices);
        da.SetDataList(4, unplaced);
        da.SetDataList(5, residuals);
        da.SetData(6, totalResidual);
        da.SetData(7, report.ToString());
    }

    private static void AddSegmentsFor(
        Panel panel, SegmenterOptions segOpt, SegmenterOptions3D segOpt3D, SegmentHashIndex index)
    {
        var segs = panel.Mode == PanelMode.Spatial3D
            ? BoundarySegmenter3D.Segment(panel, segOpt3D)
            : BoundarySegmenter.Segment(panel, segOpt);
        foreach (var s in segs) index.Add(s);
    }

    private static bool TryToPolylineCurve(Curve? c, out PolylineCurve pc)
    {
        pc = null!;
        if (c == null) return false;
        if (c is PolylineCurve already) { pc = already; return true; }
        if (c.TryGetPolyline(out Polyline poly))
        {
            pc = poly.ToPolylineCurve();
            return true;
        }
        return false;
    }
}
