using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Debug / inspection component. Runs the EdgeMatch boundary segmenter
/// on a single closed curve and exposes each resulting segment plus its
/// turning, curvature, and (3D only) torsion signatures. Useful for
/// tuning SampleSpacing / BreakAngle / MinSegmentLength before running
/// the full solver.
/// </summary>
[Algorithm("Boundary segmenter", "Frahan-original arc-length curvature/torsion signature", Note = "EdgeMatch pipeline Stage 1")]
[Algorithm("Segment hash index", "Frahan-original planarity-aware spatial bucketing", Note = "EdgeMatch pipeline Stage 2")]
[DesignApplication(
    "Inspect the per-segment signatures of a boundary curve before running the matcher (debug surface).",
    DesignFlow.Bridges,
    Precedent = "Stage-1 segmenter of the canonical 5-stage EdgeMatch pipeline (Frahan-original)",
    Tolerance = "no numeric pass criterion -- pure inspection / tuning tool",
    CardSet = "wiki/research/hitl_cards/em_2d_boundary_match/ (the consumer card-set)")]
public sealed class EdgeMatchSegmentsComponent : GH_Component
{
    public EdgeMatchSegmentsComponent()
        : base("EdgeMatch Segments", "EMSegs",
            "Run the EdgeMatch boundary segmenter on one curve and expose " +
            "the per-segment polylines and signatures. Auto-dispatches between " +
            "the 2D planar segmenter and the 3D Frenet-invariant segmenter " +
            "based on the curve's best-fit planarity.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10002-ED9E-4ED9-A002-ED9EED9E0002");
    protected override Bitmap? Icon => IconProvider.Load("BoundarySegmenter.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Curve", "C",
            "Closed planar or spatial polyline-convertible curve.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Planarity Tolerance", "Pt",
            "RMS planarity threshold (mm) deciding the 2D vs 3D path.",
            GH_ParamAccess.item, Panel.DefaultPlanarityTolerance);
        pManager.AddNumberParameter("Sample Spacing", "Sp",
            "Arc-length sample spacing (mm).", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Break Angle", "Ba",
            "Curvature break threshold in degrees per window.",
            GH_ParamAccess.item, 18.0);
        pManager.AddNumberParameter("Min Segment Length", "Ms",
            "Below this chord length a segment is treated as noise.",
            GH_ParamAccess.item, 8.0);
        pManager.AddIntegerParameter("Signature Bins", "Sb",
            "Resampled signature length (power of 2 recommended).",
            GH_ParamAccess.item, 128);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Segments", "Sg",
            "One polyline per detected segment.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Chord Lengths", "L",
            "Per-segment chord length.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Total Turning", "T",
            "Per-segment signed turning integral.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Sign", "Sn",
            "+1 convex (relative to panel interior), -1 concave.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Turning Signatures", "Tg",
            "One branch per segment; resampled signed-turning signal.", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Curvature Signatures", "Kg",
            "One branch per segment; |turning| for the planar path or discrete Frenet curvature for the 3D path.",
            GH_ParamAccess.tree);
        pManager.AddNumberParameter("Torsion Signatures", "Wg",
            "One branch per segment; populated only when the curve takes the 3D path.",
            GH_ParamAccess.tree);
        pManager.AddTextParameter("Mode", "Md",
            "Detected panel mode: Planar2D or Spatial3D.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Planarity RMS", "Rm",
            "Best-fit plane RMS (mm) for the input curve.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Curve? curve = null;
        double planarityTol = Panel.DefaultPlanarityTolerance;
        double sampleSpacing = 1.0;
        double breakAngleDeg = 18.0;
        double minSegmentLength = 8.0;
        int signatureBins = 128;

        if (!da.GetData(0, ref curve)) return;
        da.GetData(1, ref planarityTol);
        da.GetData(2, ref sampleSpacing);
        da.GetData(3, ref breakAngleDeg);
        da.GetData(4, ref minSegmentLength);
        da.GetData(5, ref signatureBins);

        if (!TryToPolylineCurve(curve, out var pc))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Curve must be polyline-convertible.");
            return;
        }

        // Allow open curves by routing through PanelKind.Frame, which is
        // the only kind that may be non-closed. This lets the debug
        // component inspect partial boundaries during tuning.
        var kind = pc.IsClosed ? PanelKind.Shard : PanelKind.Frame;
        Panel panel;
        try
        {
            panel = new Panel("debug", pc, kind, planarityTol);
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        List<Segment> segs;
        if (panel.Mode == PanelMode.Spatial3D)
        {
            segs = BoundarySegmenter3D.Segment(panel, new SegmenterOptions3D
            {
                SampleSpacing = sampleSpacing,
                BreakAngleDeg = breakAngleDeg,
                MinSegmentLength = minSegmentLength,
                SignatureBins = signatureBins,
            });
        }
        else
        {
            segs = BoundarySegmenter.Segment(panel, new SegmenterOptions
            {
                SampleSpacing = sampleSpacing,
                BreakAngleDeg = breakAngleDeg,
                MinSegmentLength = minSegmentLength,
                SignatureBins = signatureBins,
            });
        }

        var polylines = new List<Curve>(segs.Count);
        var chords = new List<double>(segs.Count);
        var turnings = new List<double>(segs.Count);
        var signs = new List<int>(segs.Count);
        var turningTree = new DataTree<double>();
        var curvatureTree = new DataTree<double>();
        var torsionTree = new DataTree<double>();

        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            polylines.Add(s.LocalPolyline.ToPolylineCurve());
            chords.Add(s.ChordLength);
            turnings.Add(s.TotalTurning);
            signs.Add(s.Sign);
            var path = new GH_Path(i);
            turningTree.AddRange(s.TurningSignature, path);
            curvatureTree.AddRange(s.CurvatureSignature, path);
            if (s.TorsionSignature != null)
                torsionTree.AddRange(s.TorsionSignature, path);
        }

        da.SetDataList(0, polylines);
        da.SetDataList(1, chords);
        da.SetDataList(2, turnings);
        da.SetDataList(3, signs);
        da.SetDataTree(4, turningTree);
        da.SetDataTree(5, curvatureTree);
        da.SetDataTree(6, torsionTree);
        da.SetData(7, panel.Mode.ToString());
        da.SetData(8, panel.PlanarityRms);
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
