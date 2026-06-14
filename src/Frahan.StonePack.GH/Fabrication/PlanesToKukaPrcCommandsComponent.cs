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
// PlanesToKukaPrcCommandsComponent (GUID D5F10032)
//
// Phase B Stage 3 per wiki/specs/scan_to_mill_architecture.md §1.6 + research
// dossier wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md.
//
// Thin wrapper component: tags each Plane in the input list with a KUKAprc
// motion type (`LIN` for cutting moves, `PTP` for rapids) and emits the
// per-plane feed + spindle metadata. The user wires the outputs into KUKAprc
// Pro's `LIN` / `PTP` / `KRL Code Generator` components downstream.
//
// Why a thin wrapper:
//   - Per `[[feedback_example_files_over_reimplementation]]`, Frahan does not
//     compete with KUKAprc's command generation. The wrapper STOPS at
//     emitting Plane + Motion + Feed + Spindle, all of which are KUKAprc Pro
//     command-component inputs.
//   - KUKAprc Pro's `Generic NC Import` is paid-tier; this Frahan wrapper +
//     `GCodeParser` is the first FREE open-source G-code path INTO KUKAprc.
//
// Motion-type derivation:
//   - If Cut Path is wired (the typed record from GCodeParser) AND Segment
//     Indices is wired (from GCodeToPlanes), the wrapper looks up each
//     plane's source CutSegment.Kind: Rapid -> PTP, otherwise LIN.
//   - If Cut Path is not wired, the wrapper defaults all moves to LIN with
//     a warning. The user must then manually distinguish PTP/LIN in KUKAprc.
// =============================================================================

[Algorithm("CutSegmentKind -> KUKAprc Motion mapping",
    "Frahan-original; standard CAM-to-KRL motion-type convention (Rapid->PTP, Linear/Arc->LIN)",
    Note = "KUKAprc Pro CIRC component requires arc center metadata Frahan does not preserve through Plane[]; arcs ship as LIN sequences.")]
[DesignApplication(
    "Tag a Plane[] (from GCodeToPlanes or WireSawToolpath) with KUKAprc-compatible motion-type and feed metadata, ready for KUKAprc Pro Plane->LIN/PTP downstream consumption.",
    DesignFlow.Bridges,
    Precedent = "KUKAprc Pro (Brell-Cokcan + Braumann Vienna) Plane->LIN/PTP/CIRC component family; first FREE alternative to KUKAprc's paid Generic NC Import per wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md.",
    Tolerance = "round-trip identity on Plane[] passthrough (no resampling); motion-type mapping preserves CutSegment.Kind exactly when Cut Path is wired.",
    CardSet = "Template-General/outputs/2026-05-31/hitl_cards/gcode_to_kukaprc/ (proposed)")]
public sealed class PlanesToKukaPrcCommandsComponent : FrahanComponentBase
{
    public PlanesToKukaPrcCommandsComponent()
        : base("Planes to KUKAprc Commands", "Pl2KUKAprc",
            "Tag a Plane[] (from GCodeToPlanes or WireSawToolpath) with " +
            "KUKAprc-compatible motion-type (LIN / PTP) and feed metadata. " +
            "Thin wrapper -- Frahan stops here; KUKAprc Pro owns the final " +
            "Plane->LIN/PTP/CIRC command construction + KRL code generation. " +
            "This wrapper IS the first FREE open-source G-code path into " +
            "KUKAprc (paid Generic NC Import is the only alternative).",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10032-ED9E-4ED9-A032-ED9EED9E0032");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P",
            "Per-pose tool-axis frames (from GCodeToPlanes D5F10031 or " +
            "WireSawToolpath D5F10034). Each plane = one KUKAprc target.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Feed Rates", "F",
            "Per-plane feed rate (mm/min); parallels Planes list. The Frahan " +
            "convention is mm/min; KUKAprc consumes this as the LIN/PTP velocity " +
            "argument after the user maps it to their unit-system in KUKAprc.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Spindle Speeds", "S",
            "Per-plane spindle speed (RPM); parallels Planes list. KUKAprc has " +
            "no native spindle command; the user wires this into a KUKAprc " +
            "ENV-variable assignment or vendor-specific tool-trigger.",
            GH_ParamAccess.list);
        p.AddGenericParameter("Cut Path (optional)", "CP",
            "Optional: the source CutPath typed record (from GCodeParser " +
            "D5F10030). If wired, the wrapper distinguishes Rapid (PTP) from " +
            "Linear/Arc (LIN) per-plane via the Segment Indices lookup.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Segment Indices (optional)", "Si",
            "Optional: per-plane source segment index (from GCodeToPlanes). " +
            "Required when Cut Path is wired so the wrapper can look up the " +
            "CutSegmentKind for motion-type mapping.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P",
            "Plane[] passthrough; wire into KUKAprc Pro's Plane->LIN/PTP " +
            "command components.",
            GH_ParamAccess.list);
        p.AddTextParameter("Motion Types", "M",
            "Per-plane motion type: \"LIN\" (linear cut) or \"PTP\" " +
            "(point-to-point rapid). Wire into KUKAprc's command branch " +
            "selector.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Feed Rates", "F",
            "Per-plane feed rate passthrough (mm/min).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Spindle Speeds", "S",
            "Per-plane spindle RPM passthrough.",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "R",
            "KUKAprc Pro version + paid-tier dependency note + motion-type " +
            "histogram.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        var planes = new List<Plane>();
        var feeds = new List<double>();
        var spindles = new List<double>();
        IGH_Goo cutPathGoo = null;
        var segIndices = new List<int>();

        if (!DA.GetDataList(0, planes)) return;
        DA.GetDataList(1, feeds);
        DA.GetDataList(2, spindles);
        DA.GetData(3, ref cutPathGoo);
        DA.GetDataList(4, segIndices);

        if (planes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Planes input is empty.");
            return;
        }

        // Pad feed/spindle lists if shorter than planes (last-value extension).
        PadList(feeds, planes.Count, 0.0);
        PadList(spindles, planes.Count, 0.0);

        CutPath path = null;
        if (cutPathGoo is GH_ObjectWrapper ow && ow.Value is CutPath cp) path = cp;

        var motions = new List<string>(planes.Count);
        int ptpCount = 0;
        int linCount = 0;

        if (path != null && segIndices.Count >= planes.Count)
        {
            // Look up motion-type per plane via Segment Indices -> CutSegment.Kind.
            for (int i = 0; i < planes.Count; i++)
            {
                int si = segIndices[i];
                string m = "LIN";
                if (si >= 0 && si < path.Segments.Count)
                {
                    var kind = path.Segments[si].Kind;
                    m = kind == CutSegmentKind.Rapid ? "PTP" : "LIN";
                }
                motions.Add(m);
                if (m == "PTP") ptpCount++; else linCount++;
            }
        }
        else
        {
            for (int i = 0; i < planes.Count; i++)
            {
                motions.Add("LIN");
                linCount++;
            }
            if (path == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Cut Path not wired; all motions defaulted to LIN. Wire " +
                    "the Cut Path output from GCodeParser to distinguish PTP " +
                    "rapids from LIN cuts.");
            }
        }

        var remarks = new List<string>
        {
            "Emitted " + planes.Count + " KUKAprc-tagged Plane(s): " +
                linCount + " LIN + " + ptpCount + " PTP.",
            "KUKAprc Pro is REQUIRED for the downstream Plane->LIN/PTP/KRL " +
                "Code Generator components. Free tier does not include these.",
            "Reference: Brell-Cokcan + Braumann, Robots in Architecture, Vienna -- " +
                "https://www.robotsinarchitecture.org/kukaprc"
        };

        DA.SetDataList(0, planes);
        DA.SetDataList(1, motions);
        DA.SetDataList(2, feeds);
        DA.SetDataList(3, spindles);
        DA.SetDataList(4, remarks);
    }

    private static void PadList<T>(List<T> list, int targetCount, T defaultValue)
    {
        if (list.Count >= targetCount) return;
        T last = list.Count > 0 ? list[list.Count - 1] : defaultValue;
        while (list.Count < targetCount) list.Add(last);
    }
}
