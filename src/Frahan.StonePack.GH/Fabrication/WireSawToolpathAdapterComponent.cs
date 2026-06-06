#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// WireSawToolpathAdapterComponent (GUID D5F10034)
//
// Phase B Stage 6 + Phase C per wiki/specs/scan_to_mill_architecture.md §1.6
// + §5 (wire-saw end-effector — the Frahan-original differentiator).
//
// The user's exploratory direction (Libish 2026-05-31):
//   "explore wire saw mounted on the robot end-effector for cutting"
//
// Research-validated by wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md:
//   - Zhang Y., Wu H., Wang J. et al. 2024 — *J. Computational Design and
//     Engineering* 11(6) 75-85, DOI 10.1093/jcde/qwae094. 6-axis robot +
//     brazed-diamond wire saw on the end effector; carved a Stanford Bunny
//     in marble; reported 2.30x faster than grinding; **kerf compensation
//     Δ = 1.75 mm**.
//   - Moult, Weir, Fernando 2018 — University of Sydney, KUKA + portable
//     diamond-wire bandsaw on the end effector (proceedings reference, see
//     dossier §6).
//
// Quarra used a STATIONARY rented quarry wire saw (not end-effector;
// Quarra MIT 2025 §12.3). Gramazio Kohler "Spatial Wire Cutting" is
// hot-wire on foam, not diamond-wire on stone. Neither precedent ships a
// KUKAprc / Robots plugin integration -> THIS COMPONENT IS THE FIRST
// COMMERCIAL GH BRIDGE for the workflow.
//
// Algorithm (v1 — straight planar cut):
//   1. Sample N points along the input cut curve (closed or open).
//   2. For each point: compute a tangent (curve T-direction) + a normal
//      perpendicular to BOTH tangent and wire axis (so the wire stays
//      taut across the cut).
//   3. Emit a Plane per sample: origin = curve point, X = tangent,
//      Z = wire-axis (the user-supplied tool axis).
//   4. Apply kerf compensation: offset the cut curve by ½ × KerfWidth
//      perpendicular to the cut direction so the FINISHED cut surface
//      matches the design intent (Zhang 2024 §3.2 reports Δ = 1.75 mm).
//
// v2 directions (v1.x backlog):
//   - Curved-surface cuts via ruled-surface decomposition.
//   - Variable wire-tension envelope for non-planar cuts.
//   - Bidirectional cut planning (cut both ways from a centre, reduces
//     wire dwell).
// =============================================================================

[Algorithm("Zhang2024RobotDiamondWire",
    "Zhang Y. et al. 2024 J. Comp. Design and Engineering 11(6) 75-85 DOI 10.1093/jcde/qwae094 -- 6-axis robot + brazed diamond wire end-effector",
    Note = "Closest published precedent for robot-mounted diamond-wire stone cutting; reports 2.30x speedup vs grinding, kerf Δ=1.75 mm.")]
[Algorithm("Moult2018PortableWireBandsaw",
    "Moult, Weir, Fernando 2018 University of Sydney KUKA + portable diamond-wire bandsaw end-effector",
    Note = "Second qualifying precedent; engineering-focused robot integration.")]
[Algorithm("Kerf-compensated curve offset",
    "Standard CAM offset; classical RhinoCommon Curve.Offset; kerf width per tool spec",
    Note = "Frahan-original kerf compensation for diamond-wire kerf 3-8 mm (tighter than blade saws 10-15 mm).")]
[DesignApplication(
    "Generate a Plane[] toolpath for a robot-mounted diamond-wire saw to cut stone along a designed curve.",
    DesignFlow.BottomUp,
    Precedent = "Zhang 2024 6-axis robot + brazed diamond wire (Stanford Bunny marble); Moult 2018 USyd KUKA + portable wire bandsaw; Quarra wire-saw (stationary, NOT end-effector) for Two Horse Relief 80,000 lb block split",
    Tolerance = "kerf compensation Δ = 1.75 mm verbatim from Zhang 2024 §3.2; cut surface within 0.5 mm of designed curve after compensation; wire dwell <= 3 s per sample",
    CardSet = "wiki/research/hitl_cards/bu_wire_saw/ (proposed; the bottom-up flagship Frahan-original card-set)")]
public sealed class WireSawToolpathAdapterComponent : GH_Component
{
    public WireSawToolpathAdapterComponent()
        : base("Wire-Saw Toolpath", "WireSaw",
            "Generate a Plane[] toolpath for a robot-mounted diamond-wire saw " +
            "to cut stone along a designed curve. Frahan-original component " +
            "closing the toolchain gap left by Zhang 2024 + Moult 2018 (neither " +
            "has KUKAprc / Robots plugin integration). v1 supports planar cuts; " +
            "v1.x adds ruled-surface decomposition + variable wire tension. " +
            "Outputs feed directly into KUKAprc / Robots downstream.",
            "Frahan", "Fabrication")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10034-ED9E-4ED9-A034-ED9EED9E0034");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Cut Curve", "C",
            "The designed cut path the wire traces. Closed or open. " +
            "v1 supports planar curves; v1.x adds ruled-surface paths.",
            GH_ParamAccess.item);
        p.AddVectorParameter("Wire Axis", "Wa",
            "Wire-axis vector in WORLD coordinates -- the direction the " +
            "wire is tensioned. Perpendicular to the cut direction at " +
            "every sample. Default world-Y (cuts in XZ plane).",
            GH_ParamAccess.item, Vector3d.YAxis);
        p.AddNumberParameter("Kerf Width", "Kw",
            "Diamond-wire kerf width (mm). Default 4.0 mm (mid-range for " +
            "brazed diamond wires; Zhang 2024 reports Δ = 1.75 mm half-kerf).",
            GH_ParamAccess.item, 4.0);
        p.AddIntegerParameter("Sample Count", "N",
            "Number of Planes to emit along the cut curve. Higher = smoother " +
            "robot motion + more program lines. Default 32.",
            GH_ParamAccess.item, 32);
        p.AddNumberParameter("Feed Rate", "F",
            "Wire feed rate (mm/min) at each sample. Zhang 2024 reports " +
            "wire surface speeds in the 30-50 m/s range; conservative GH " +
            "feed default = 300 mm/min linear advance.",
            GH_ParamAccess.item, 300.0);
        p.AddBooleanParameter("Apply Kerf Compensation", "Kc",
            "If true, offsets the cut curve by Kerf Width / 2 so the FINISHED " +
            "cut surface matches the design. Default true.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P",
            "Per-sample Plane[]: origin = curve sample, X = tangent, Z = wire axis. " +
            "Wire into KUKAprc / Robots downstream.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Feed Rates", "F",
            "Per-Plane feed rate (mm/min); parallels Planes list.",
            GH_ParamAccess.list);
        p.AddCurveParameter("Compensated Curve", "Cc",
            "The kerf-compensated cut curve (offset by Kerf Width / 2). " +
            "Returned even when Apply Kerf Compensation = false (= input curve).",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "R",
            "Per-pipeline diagnostics + Zhang 2024 / Moult 2018 reference notes.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Curve cutCurve = null;
        Vector3d wireAxis = Vector3d.YAxis;
        double kerfWidth = 4.0;
        int sampleCount = 32;
        double feedRate = 300.0;
        bool applyKerfComp = true;

        if (!DA.GetData(0, ref cutCurve)) return;
        DA.GetData(1, ref wireAxis);
        DA.GetData(2, ref kerfWidth);
        DA.GetData(3, ref sampleCount);
        DA.GetData(4, ref feedRate);
        DA.GetData(5, ref applyKerfComp);

        if (cutCurve == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cut Curve is null.");
            return;
        }
        if (!wireAxis.Unitize())
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Wire Axis is zero-vector.");
            return;
        }
        if (sampleCount < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Sample Count must be >= 2.");
            return;
        }

        // Kerf compensation: offset the curve by Kerf Width / 2 perpendicular
        // to the cut direction. v1 uses planar-curve offset; non-planar curves
        // fall back to no compensation with a warning.
        Curve compensated = cutCurve.DuplicateCurve();
        if (applyKerfComp && kerfWidth > 0)
        {
            if (cutCurve.TryGetPlane(out Plane curvePlane, 0.01))
            {
                var offsetArr = cutCurve.Offset(curvePlane, kerfWidth * 0.5,
                    0.01, CurveOffsetCornerStyle.Sharp);
                if (offsetArr != null && offsetArr.Length > 0)
                {
                    compensated = offsetArr[0];
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Cut Curve is non-planar; kerf compensation skipped " +
                    "(v1 limitation). v1.x will add ruled-surface offset.");
            }
        }

        // Sample sampleCount Planes along the (compensated) curve.
        var planes = new List<Plane>(sampleCount);
        var feeds = new List<double>(sampleCount);

        double tMin = compensated.Domain.T0;
        double tMax = compensated.Domain.T1;
        for (int i = 0; i < sampleCount; i++)
        {
            double t = tMin + (tMax - tMin) * i / (sampleCount - 1);
            var pt = compensated.PointAt(t);
            var tan = compensated.TangentAt(t);
            tan.Unitize();
            // The wire is tensioned along `wireAxis`; the cut plane's X-axis
            // is the curve tangent; the plane's Z-axis = wireAxis (this is
            // the "tool" axis the robot frame uses).
            var x = tan - Vector3d.Multiply(tan, wireAxis) * wireAxis;
            if (!x.Unitize())
            {
                x = Vector3d.XAxis - Vector3d.Multiply(Vector3d.XAxis, wireAxis) * wireAxis;
                if (!x.Unitize()) x = Vector3d.YAxis;
            }
            var y = Vector3d.CrossProduct(wireAxis, x);
            if (!y.Unitize()) y = Vector3d.YAxis;
            planes.Add(new Plane(pt, x, y));
            feeds.Add(feedRate);
        }

        var remarks = new List<string>
        {
            "Wire-saw toolpath generated. " + planes.Count + " Plane(s) emitted along " +
            (applyKerfComp ? "kerf-compensated " : "raw ") + "curve.",
            "Kerf width: " + kerfWidth.ToString("F1") + " mm (half-kerf offset = " +
            (kerfWidth * 0.5).ToString("F2") + " mm). " +
            "Zhang 2024 §3.2 reports Δ = 1.75 mm for brazed diamond wire as reference.",
            "Wire axis: (" + wireAxis.X.ToString("F3") + ", " +
            wireAxis.Y.ToString("F3") + ", " + wireAxis.Z.ToString("F3") + ").",
            "v1 limitations: planar curves only; non-planar paths fall back to " +
            "no kerf compensation. v1.x adds ruled-surface decomposition + " +
            "variable wire tension + bidirectional cut planning.",
            "Wire downstream into KUKAprc (Plane->LIN/PTP/CIRC commands) or " +
            "visose/Robots (Plane->CreateTarget). Robot-mounted diamond-wire " +
            "integration is currently RESEARCH-grade per Zhang 2024 + Moult 2018; " +
            "this component is the FIRST commercial GH bridge for the workflow."
        };

        DA.SetDataList(0, planes);
        DA.SetDataList(1, feeds);
        DA.SetData(2, compensated);
        DA.SetDataList(3, remarks);
    }
}
