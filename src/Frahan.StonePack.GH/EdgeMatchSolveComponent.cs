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
/// Trencadís / live-edge edge-matching solver. Consumes a frame curve
/// (anchored) plus a list of shard / plank curves, optionally a curved
/// substrate Brep, runs the full pipeline (segment → hash → coarse
/// phase → ICP → beam-search assembly), and emits placement transforms.
///
/// Per Panel.Mode the pipeline auto-dispatches between the 2D ICP and
/// the 3D Kabsch ICP. The substrate is only consulted by the 3D path
/// and may be left disconnected for flat assemblies.
/// </summary>
[Algorithm("Boundary segmenter", "Frahan-original arc-length curvature/torsion signature", Note = "Stage 1 of 5-stage pipeline")]
[Algorithm("Segment hash index", "Frahan-original planarity-aware spatial bucketing", Note = "Stage 2; auto-dispatches 2D vs 3D")]
[Algorithm("Phase correlator FFT", "Classical cross-correlation lag estimation", Note = "Stage 3; turning-signature alignment")]
[Algorithm("Constrained ICP", "Besl and McKay 1992 iterative closest point; MathNet.Numerics SVD", Note = "Stage 4; 2D point-to-segment or 3D point cloud")]
[Algorithm("Beam-search assembly solver", "Frahan-original deterministic beam search with state cloning", Note = "Stage 5; lexical tie-breaking")]
[DesignApplication(
    "Reassemble Trencadis fragments inside a closed boundary by matching their edges.",
    DesignFlow.BottomUp,
    Precedent = "Gaudi Park Guell Trencadis (1900-1914); IAAC MRAC RoboMosaic (2022-23)",
    Tolerance = "mean joint Hausdorff <= 5 mm, coverage >= 95 %",
    CardSet = "wiki/research/hitl_cards/em_2d_trencadis_solve/")]
public sealed class EdgeMatchSolveComponent : FrahanComponentBase
{
    public EdgeMatchSolveComponent()
        : base("EdgeMatch Solve", "EMSolve",
            "Edge-matching beam search for Trencadís shards or live-edge planks. " +
            "Anchors against a frame curve, places each candidate using ICP-refined " +
            "complementary-edge matches, and emits the placement transform set.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10001-ED9E-4ED9-A001-ED9EED9E0001");
    protected override Bitmap? Icon => IconProvider.Load("EdgeMatchSolve.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Frame", "Fr",
            "Closed boundary curve. Anchored at the identity transform; shards match against it first.",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Shards", "S",
            "Closed shard / plank curves to place.", GH_ParamAccess.list);
        pManager.AddBrepParameter("Substrate", "Sb",
            "Optional curved substrate Brep. Only consulted by the 3D ICP path; pass nothing for flat assemblies.",
            GH_ParamAccess.item);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Planarity Tolerance", "Pt",
            "RMS planarity threshold (mm). Below this, panels take the 2D path.",
            GH_ParamAccess.item, Panel.DefaultPlanarityTolerance);
        pManager.AddNumberParameter("Sample Spacing", "Sp",
            "Arc-length sample spacing along each contour (mm).",
            GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Break Angle", "Ba",
            "Curvature break-point threshold in degrees per window.",
            GH_ParamAccess.item, 18.0);
        pManager.AddNumberParameter("Min Segment Length", "Ms",
            "Below this chord length, a segment is treated as noise and discarded.",
            GH_ParamAccess.item, 8.0);
        pManager.AddNumberParameter("Residual Threshold", "Rt",
            "Maximum mean point-to-point ICP residual for an accepted match (mm).",
            GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("Beam Width", "Bw",
            "Number of concurrent beam states retained between iterations.",
            GH_ParamAccess.item, 8);
        pManager.AddIntegerParameter("Max Iterations", "Mi",
            "Maximum outer-loop iterations.", GH_ParamAccess.item, 1000);
        pManager.AddBooleanParameter("Run", "R",
            "Execute the solver.", GH_ParamAccess.item, false);
        // Appended LAST (after Run) so existing canvases keep their wiring.
        pManager.AddBooleanParameter("Non-Crossing", "Nc",
            "Order-preserving rim correspondence. FALSE (default) = free " +
            "nearest-point ICP (unchanged behaviour). TRUE = monotone, " +
            "non-crossing point pairing between rims (OrderedBoundaryMatcher); " +
            "more robust on wiggly / noisy rims where free matching tangles.",
            GH_ParamAccess.item, false);
        // Appended LAST (after Non-Crossing) so existing canvases keep their
        // wiring + indices. Optional AssemblyOptions DTO from EdgeMatch Options.
        // When connected, its ADVANCED fields override the equivalent fields on
        // the options built from the simple inputs; the simple inputs keep owning
        // the basic fields. When disconnected -> byte-identical to before.
        pManager.AddGenericParameter("Options", "Opt",
            "Optional AssemblyOptions DTO from EdgeMatch Options. When wired, " +
            "its advanced flags (Mode, scale-relative gates, partial sub-segment " +
            "matching, overlap resolve, Soft-ICP refine, projection bootstrap) " +
            "override the defaults; the simple inputs above keep owning the basic " +
            "fields. Leave disconnected for unchanged behaviour.",
            GH_ParamAccess.item);
        pManager[12].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Placed", "P",
            "Shard contours transformed by their solved placements. Frame is included as Identity.",
            GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "X",
            "Per-panel rigid transform.", GH_ParamAccess.list);
        pManager.AddTextParameter("Ids", "Id",
            "Panel ids matching the Placed and Transforms order.", GH_ParamAccess.list);
        pManager.AddTextParameter("Modes", "Md",
            "Per-panel mode: Planar2D or Spatial3D.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Planarity RMS", "Rm",
            "Per-panel best-fit plane RMS (mm).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Residuals", "Re",
            "Per-placement ICP residual. Length = placed shard count (frame excluded).",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Total Residual", "Tr",
            "Sum of per-placement residuals.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "Rp",
            "Human-readable summary of the solve.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve? frameCurve = null;
        var shardCurves = new List<Curve>();
        Brep? substrate = null;
        double planarityTol = Panel.DefaultPlanarityTolerance;
        double sampleSpacing = 1.0;
        double breakAngleDeg = 18.0;
        double minSegmentLength = 8.0;
        double residualThreshold = 1.0;
        int beamWidth = 8;
        int maxIterations = 1000;
        bool run = false;
        bool nonCrossing = false;

        if (!da.GetData(0, ref frameCurve)) return;
        if (!da.GetDataList(1, shardCurves)) return;
        da.GetData(2, ref substrate);
        da.GetData(3, ref planarityTol);
        da.GetData(4, ref sampleSpacing);
        da.GetData(5, ref breakAngleDeg);
        da.GetData(6, ref minSegmentLength);
        da.GetData(7, ref residualThreshold);
        da.GetData(8, ref beamWidth);
        da.GetData(9, ref maxIterations);
        da.GetData(10, ref run);
        // Backward-compatible read: only present if the param exists on a
        // canvas saved after this input was added.
        if (Params.Input.Count > 11) da.GetData(11, ref nonCrossing);

        if (!run)
        {
            da.SetData(7, "Run is false; toggle to execute.");
            return;
        }

        if (!TryToPolylineCurve(frameCurve, out var framePc))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Frame curve must be a polyline (use Curve to Polyline upstream if needed).");
            return;
        }

        var framePanel = new Panel("frame", framePc, PanelKind.Frame, planarityTol);

        var shardPanels = new List<Panel>(shardCurves.Count);
        for (int i = 0; i < shardCurves.Count; i++)
        {
            if (!TryToPolylineCurve(shardCurves[i], out var pc))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Shard {i} is not a polyline; skipping.");
                continue;
            }
            if (!pc.IsClosed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Shard {i} is not closed; skipping.");
                continue;
            }
            shardPanels.Add(new Panel($"s{i:D4}", pc, PanelKind.Shard, planarityTol));
        }

        if (shardPanels.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable shard panels.");
            return;
        }

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
        var index = new SegmentHashIndex();

        AddSegmentsFor(framePanel, segOpt, segOpt3D, index);
        foreach (var p in shardPanels)
            AddSegmentsFor(p, segOpt, segOpt3D, index);

        var asmOpt = new AssemblyOptions
        {
            BeamWidth = Math.Max(1, beamWidth),
            MaxIterations = Math.Max(1, maxIterations),
            ResidualThreshold = residualThreshold,
            NonCrossingCorrespondence = nonCrossing,
        };

        // Optional advanced-options override. Only present if the param exists
        // on a canvas saved after this input was added; read backward-compatibly.
        // The simple inputs above continue to own the basic fields (BeamWidth,
        // MaxIterations, ResidualThreshold, NonCrossingCorrespondence); the
        // supplied options only contribute the ADVANCED fields. When Opt is not
        // connected this block is a no-op and the solve is byte-identical.
        if (Params.Input.Count > 12)
        {
            object rawOptions = null;
            da.GetData(12, ref rawOptions);
            var supplied = UnwrapOptions(rawOptions);
            if (supplied != null)
            {
                asmOpt.Mode = supplied.Mode;
                asmOpt.NonCrossingMaxGap = supplied.NonCrossingMaxGap;
                asmOpt.PhaseScoreThreshold = supplied.PhaseScoreThreshold;
                asmOpt.ResidualThresholdFactor = supplied.ResidualThresholdFactor;
                asmOpt.EmitPartials = supplied.EmitPartials;
                asmOpt.PartialFractions = supplied.PartialFractions;
                asmOpt.PartialStrideFraction = supplied.PartialStrideFraction;
                asmOpt.OverlapPenalty = supplied.OverlapPenalty;
                asmOpt.EdgeExclusivity = supplied.EdgeExclusivity;
                asmOpt.ResolveOverlap = supplied.ResolveOverlap;
                asmOpt.ResolveOverlapTolerance = supplied.ResolveOverlapTolerance;
                asmOpt.ResolveOverlapIterations = supplied.ResolveOverlapIterations;
                asmOpt.ResolveOverlapRelaxation = supplied.ResolveOverlapRelaxation;
                asmOpt.SoftIcpRefine = supplied.SoftIcpRefine;
                if (supplied.SoftIcp != null) asmOpt.SoftIcp = supplied.SoftIcp;
                asmOpt.ProjectionBootstrap = supplied.ProjectionBootstrap;
                asmOpt.ProjectionSampleSpacingFactor = supplied.ProjectionSampleSpacingFactor;
                asmOpt.ProjectionPlanarityFactor = supplied.ProjectionPlanarityFactor;
                asmOpt.ProjectionVerifyFactor = supplied.ProjectionVerifyFactor;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Advanced Options override active: Mode + advanced flags " +
                    "taken from the EdgeMatch Options input.");
            }
        }

        var solver = new AssemblySolver(index, asmOpt, segOpt, segOpt3D, substrate);
        var state = solver.Solve(new[] { framePanel }, shardPanels);

        var placed = new List<Curve>();
        var transforms = new List<object>();
        var ids = new List<string>();
        var modes = new List<string>();
        var rmsList = new List<double>();
        foreach (var panel in state.PlacedPanels)
        {
            var t = state.AppliedTransforms[panel.Id];
            var c = (Curve)panel.SourceContour.DuplicateCurve();
            c.Transform(t);
            placed.Add(c);
            transforms.Add(new GH_Transform(t));
            ids.Add(panel.Id);
            modes.Add(panel.Mode.ToString());
            rmsList.Add(panel.PlanarityRms);
        }

        var residuals = new List<double>(state.History.Count);
        foreach (var h in state.History) residuals.Add(h.Residual);

        var report = new StringBuilder();
        report.AppendLine($"Frame: {framePanel.Id} mode={framePanel.Mode} rms={framePanel.PlanarityRms:F4}");
        report.AppendLine($"Shards: {shardPanels.Count} (placed: {state.PlacedPanels.Count - 1})");
        report.AppendLine($"Index: 2D buckets contain {index.Count2D} segments, 3D buckets contain {index.Count3D} segments.");
        report.AppendLine($"Total residual: {state.TotalResidual:F4}");
        report.AppendLine($"Placement history: {state.History.Count} events");

        da.SetDataList(0, placed);
        da.SetDataList(1, transforms);
        da.SetDataList(2, ids);
        da.SetDataList(3, modes);
        da.SetDataList(4, rmsList);
        da.SetDataList(5, residuals);
        da.SetData(6, state.TotalResidual);
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

    // Accept the AssemblyOptions DTO whether it arrives bare or wrapped in a
    // GH_ObjectWrapper (the repo's GenericParameter convention; mirrors
    // AshlarPackComponent.UnwrapOptions).
    private static AssemblyOptions? UnwrapOptions(object? raw)
    {
        if (raw == null) return null;
        if (raw is AssemblyOptions direct) return direct;
        if (raw is GH_ObjectWrapper wrap && wrap.Value is AssemblyOptions fromWrap)
            return fromWrap;
        return null;
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
