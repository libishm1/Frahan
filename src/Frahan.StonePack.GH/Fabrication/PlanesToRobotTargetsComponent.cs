#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// PlanesToRobotTargetsComponent (GUID D5F10033)
//
// Phase B Stage 3 per wiki/specs/scan_to_mill_architecture.md §1.6 + research
// dossier wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md.
//
// Thin wrapper for the visose/Robots plugin (https://github.com/visose/Robots,
// MIT, v1.9.0, 6 vendors no FANUC). Per the gap-map sub-agent finding 2026-05-31,
// visose/Robots has ZERO native G-code ingest path. This wrapper + Frahan's
// GCodeParser closes that gap entirely.
//
// What this wrapper emits:
//   - Plane[] passthrough (ready for visose/Robots `Create Target`.Plane input).
//   - Motion[] (Linear / Joint) per-plane.
//   - Speed[] per-plane (mm/s; visose/Robots convention).
//   - Zone[] per-plane (mm blending radius; visose/Robots convention).
//   - Tool-axis hint string (the user wires this into Robots' tool definition).
//
// Why this wrapper does NOT call into Robots.dll directly:
//   - Per `[[feedback_example_files_over_reimplementation]]`, Frahan emits
//     metadata; the user wires it into visose/Robots' own component family
//     in the canvas. No hard dependency on Robots.dll inside Frahan.gha.
//   - Packaging decision (bundle Robots.dll inside Frahan deploy zip vs
//     require user install from Food4Rhino) is OPEN per handoff §5.6
//     question 2.
// =============================================================================

[Algorithm("CutSegmentKind -> visose/Robots Motion mapping",
    "Frahan-original; standard CAM-to-robot-target motion convention (Rapid->Joint, Linear/Arc->Linear)",
    Note = "visose/Robots motion enum is {Joint, Linear}; Frahan maps PTP=Joint, LIN=Linear.")]
[DesignApplication(
    "Tag a Plane[] (from GCodeToPlanes or WireSawToolpath) with visose/Robots-compatible motion-type and speed metadata, ready for the Robots plugin CreateTarget downstream consumption.",
    DesignFlow.Bridges,
    Precedent = "visose/Robots plugin (Vicente Soler, MIT, v1.9.0) Plane->CreateTarget API; closes the zero-G-code-ingest gap in the Robots plugin per wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md and wiki/research/fabrication_bridge_gap_map.md.",
    Tolerance = "round-trip identity on Plane[] passthrough; motion-type mapping preserves CutSegment.Kind exactly when Cut Path is wired; speed unit converted mm/min -> mm/s (factor 1/60).",
    CardSet = "Template-General/outputs/2026-05-31/hitl_cards/gcode_to_robots/ (proposed)")]
public sealed class PlanesToRobotTargetsComponent : FrahanComponentBase
{
    public PlanesToRobotTargetsComponent()
        : base("Planes to Robot Targets", "Pl2Robots",
            "Tag a Plane[] (from GCodeToPlanes or WireSawToolpath) with " +
            "visose/Robots-compatible motion (Linear / Joint), speed (mm/s), " +
            "and zone (mm blending) metadata. Thin wrapper -- Frahan stops " +
            "here; visose/Robots owns the Plane->CreateTarget construction + " +
            "kinematic simulation. This wrapper + Frahan's GCodeParser is the " +
            "only path from G-code into visose/Robots (the plugin has zero " +
            "native G-code ingest).",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10033-ED9E-4ED9-A033-ED9EED9E0033");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P",
            "Per-pose tool-axis frames (from GCodeToPlanes D5F10031 or " +
            "WireSawToolpath D5F10034). Each Plane = one visose/Robots Target.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Feed Rates", "F",
            "Per-plane feed rate (mm/min); parallels Planes. Converted to " +
            "mm/s (factor 1/60) on the way to visose/Robots' Speed convention.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Spindle Speeds", "S",
            "Per-plane spindle RPM passthrough. visose/Robots does not consume " +
            "spindle directly; the user wires this into a Robots `Command` " +
            "tool-trigger if needed.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Default Zone", "Z",
            "Blending zone radius (mm) applied to all targets when CutPath " +
            "is not wired. Default 1.0 mm (visose/Robots fine zone).",
            GH_ParamAccess.item, 1.0);
        p.AddGenericParameter("Cut Path (optional)", "CP",
            "Optional CutPath typed record (from GCodeParser D5F10030). If " +
            "wired, the wrapper distinguishes Rapid (Joint motion) from " +
            "Linear / Arc (Linear motion).",
            GH_ParamAccess.item);
        p[4].Optional = true;   // named optional; was registered required -> component never solved without it
        p.AddIntegerParameter("Segment Indices (optional)", "Si",
            "Optional: per-plane source segment index (from GCodeToPlanes); " +
            "required when Cut Path is wired.",
            GH_ParamAccess.list);
        p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P",
            "Plane[] passthrough; wire into visose/Robots `Create Target`.Plane.",
            GH_ParamAccess.list);
        p.AddTextParameter("Motions", "M",
            "Per-plane motion: \"Linear\" or \"Joint\". Wire into visose/Robots " +
            "`Create Target`.Motion.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Speeds", "Sp",
            "Per-plane speed (mm/s); fed into visose/Robots' Speed parameter.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Zones", "Z",
            "Per-plane blending zone (mm); fed into visose/Robots' Zone parameter.",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "R",
            "visose/Robots version + packaging note + motion-type histogram.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        var planes = new List<Plane>();
        var feeds = new List<double>();
        var spindles = new List<double>();
        double defaultZone = 1.0;
        IGH_Goo cutPathGoo = null;
        var segIndices = new List<int>();

        if (!DA.GetDataList(0, planes)) return;
        DA.GetDataList(1, feeds);
        DA.GetDataList(2, spindles);
        DA.GetData(3, ref defaultZone);
        DA.GetData(4, ref cutPathGoo);
        DA.GetDataList(5, segIndices);

        if (planes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Planes input is empty.");
            return;
        }

        PadList(feeds, planes.Count, 0.0);
        PadList(spindles, planes.Count, 0.0);

        CutPath path = null;
        if (cutPathGoo is GH_ObjectWrapper ow && ow.Value is CutPath cp) path = cp;

        var motions = new List<string>(planes.Count);
        var speeds = new List<double>(planes.Count);
        var zones = new List<double>(planes.Count);
        int jointCount = 0;
        int linearCount = 0;

        for (int i = 0; i < planes.Count; i++)
        {
            string motion = "Linear";
            double zone = defaultZone;
            if (path != null && segIndices.Count > i)
            {
                int si = segIndices[i];
                if (si >= 0 && si < path.Segments.Count)
                {
                    var kind = path.Segments[si].Kind;
                    if (kind == CutSegmentKind.Rapid)
                    {
                        motion = "Joint";
                        // Rapids use a larger blending zone (visose/Robots
                        // best-practice when joint-interpolated approach).
                        zone = Math.Max(defaultZone, 5.0);
                    }
                }
            }
            motions.Add(motion);
            // visose/Robots Speed convention is mm/s; Frahan feed is mm/min.
            speeds.Add(feeds[i] / 60.0);
            zones.Add(zone);
            if (motion == "Joint") jointCount++; else linearCount++;
        }

        var remarks = new List<string>
        {
            "Emitted " + planes.Count + " visose/Robots-tagged Plane(s): " +
                linearCount + " Linear + " + jointCount + " Joint.",
            "visose/Robots v1.9.0 -- https://github.com/visose/Robots (MIT, 6 " +
                "vendors no FANUC).",
            "Packaging: Frahan does NOT bundle Robots.dll; user must install " +
                "the Robots plugin via Food4Rhino first. (Open question -- " +
                "see handoff section 5.6.)",
            "Speed unit converted mm/min -> mm/s via factor 1/60 to match " +
                "visose/Robots' Speed parameter convention."
        };

        DA.SetDataList(0, planes);
        DA.SetDataList(1, motions);
        DA.SetDataList(2, speeds);
        DA.SetDataList(3, zones);
        DA.SetDataList(4, remarks);
    }

    private static void PadList<T>(List<T> list, int targetCount, T defaultValue)
    {
        if (list.Count >= targetCount) return;
        T last = list.Count > 0 ? list[list.Count - 1] : defaultValue;
        while (list.Count < targetCount) list.Add(last);
    }
}
