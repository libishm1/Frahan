#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// GPR Picks From Points (2026-05-28). The INTERACTIVE pick path: most GPR files
// carry no interpreted reflectors, so the user picks them by hand. Drop Rhino
// Point objects onto the GPR Radargram Mesh section (snap to the curtain), wire
// them here, and this turns them into reflector picks ready for GPR Fractures on
// Mesh -- recovering true depth (undoing the Depth Scale) and (optionally)
// exporting a picks CSV that round-trips back into GPR Radargram Mesh's
// "Picks CSV" input for reuse.
// =============================================================================

[RelatedComponent("Frahan > Ingest > GPR Radargram Mesh", Reason = "Snap points onto its section, then convert them here; the CSV out reloads via its Picks CSV input.")]
[RelatedComponent("Frahan > Quarry > GPR Fractures on Mesh", Reason = "Feed Pick Points + Labels straight into the overlay.")]
[Algorithm("Interactive reflector picking",
    "Frahan-original: viewport points on the radargram section -> reflector picks (depth = -z / DepthScale)",
    Note = "Pairs with GPR Radargram Mesh (Depth Scale must match) and exports a reusable picks CSV.")]
[DesignApplication(
    "Turn points picked on a GPR Radargram Mesh section into reflector  picks for GPR Fractures on Mesh",
    DesignFlow.Bridges,
    Precedent = "Frahan-original reflector-pick conversion")]
public sealed class GprPicksFromPointsComponent : FrahanComponentBase
{
    public GprPicksFromPointsComponent()
        : base("GPR Picks From Points", "GprPicks",
            "Turn points picked on a GPR Radargram Mesh section into reflector " +
            "picks for GPR Fractures on Mesh. Recovers true depth by undoing the " +
            "Depth Scale, tags label + confidence, and optionally writes a picks " +
            "CSV (x_m,y_m,depth_m,confidence_01,label) that reloads via GPR " +
            "Radargram Mesh's Picks CSV input.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A07-1A2B-4C3D-9E4F-5A6B7C8D9E07");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Points", "P",
            "Points picked on the radargram section (snap to the section mesh).", GH_ParamAccess.list);
        p.AddNumberParameter("Depth Scale", "Z",
            "The Depth Scale used on GPR Radargram Mesh (to recover true depth = -z / Z). " +
            "Match it. Default 1.", GH_ParamAccess.item, 1.0);
        p.AddTextParameter("Label", "L",
            "Fracture label(s). One value = applied to all picks; a list of the same " +
            "length = per-pick (group picks into distinct fractures). Default 'pick'.",
            GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddNumberParameter("Confidence", "Cf",
            "Confidence 0..1 for the picks. Default 1.", GH_ParamAccess.item, 1.0);
        p.AddTextParameter("CSV Out", "F",
            "OPTIONAL path to write a picks CSV (reloadable via GPR Radargram Mesh Picks CSV).",
            GH_ParamAccess.item);
        p[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Pick Points", "Pd",
            "The picks (section frame) — wire into GPR Fractures on Mesh 'Picks'.", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "Resolved label per pick.", GH_ParamAccess.list);
        p.AddNumberParameter("Confidence", "Cf", "Confidence per pick.", GH_ParamAccess.list);
        p.AddTextParameter("Picks CSV", "Csv", "The picks CSV content (also written if CSV Out is set).", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var pts = new List<Point3d>();
        double scale = 1.0;
        var labels = new List<string>();
        double conf = 1.0;
        string csvOut = null;
        if (!da.GetDataList(0, pts) || pts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No points provided.");
            return;
        }
        da.GetData(1, ref scale);
        da.GetDataList(2, labels);
        da.GetData(3, ref conf);
        da.GetData(4, ref csvOut);
        if (Math.Abs(scale) < 1e-12) scale = 1.0;
        if (conf < 0) conf = 0; if (conf > 1) conf = 1;

        var outLabels = new List<string>(pts.Count);
        var outConf = new List<double>(pts.Count);
        var sb = new StringBuilder();
        sb.AppendLine("# x_m,y_m,depth_m,confidence_01,label");
        for (int i = 0; i < pts.Count; i++)
        {
            string lab;
            if (labels.Count == pts.Count) lab = labels[i] ?? "pick";
            else if (labels.Count == 1) lab = labels[0] ?? "pick";
            else lab = "pick";
            double depth = -pts[i].Z / scale;   // undo Depth Scale; section z is negative-down
            outLabels.Add(lab);
            outConf.Add(conf);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:G9},{1:G9},{2:G9},{3:G4},{4}", pts[i].X, pts[i].Y, depth, conf, lab));
        }

        if (!string.IsNullOrWhiteSpace(csvOut))
        {
            try { System.IO.File.WriteAllText(csvOut, sb.ToString()); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"CSV write failed: {ex.Message}"); }
        }

        da.SetDataList(0, pts);          // pass-through (section frame) for the overlay
        da.SetDataList(1, outLabels);
        da.SetDataList(2, outConf);
        da.SetData(3, sb.ToString());
    }
}
