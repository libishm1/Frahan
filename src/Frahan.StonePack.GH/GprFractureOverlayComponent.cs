#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Frahan.GH;

// =============================================================================
// GPR Fractures on Mesh (2026-05-28). The interop connector between fracture
// mapping (GPR), quarry, and block: drape GPR reflector picks onto a target
// bench/block mesh (so everything shares one coordinate frame), connect them
// into fracture trace curves, and optionally build fracture SHEETS (surface ->
// reflector depth) that feed Cut By Fractures (CGAL) / BlockCutOpt.
//
// Chains from "GPR Radargram Mesh" (its Pick Points / Labels / Confidence
// outputs) or any point source. Recenter the GPR data and the mesh with
// "Move to Origin" first so they live in the same frame.
// =============================================================================

[RelatedComponent("Frahan > Ingest > GPR Radargram Mesh", Reason = "Source of the reflector picks this overlays.")]
[RelatedComponent("Frahan > Cut > Cut By Fractures (CGAL)", Reason = "Fracture-sheet output feeds the CGAL fracture cutter.")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Load Fractures", Reason = "Fracture meshes are the BlockCutOpt fracture input.")]
[RelatedComponent("Frahan > Mesh > Move to Origin", Reason = "Bring GPR picks + the bench mesh into one coordinate frame first.")]
[Algorithm("GPR reflector drape + fracture-sheet build",
    "Frahan-original: project reflector picks onto a target mesh; loft surface-to-reflector ribbons per fracture",
    Note = "Bridges GPR fracture mapping to quarry-bench / block cutting.")]
[DesignApplication(
    "Overlay GPR reflector picks onto a target bench/block mesh: drape  each pick onto the surface, connect pick...",
    DesignFlow.Bridges,
    Precedent = "Elkarmoty Bondua Bruno 2018 GPR-on-limestone (Construction and Building Materials); GPR fracture-overlay workflow")]
public sealed class GprFractureOverlayComponent : GH_Component
{
    public GprFractureOverlayComponent()
        : base("GPR Fractures on Mesh", "GprOverlay",
            "Overlay GPR reflector picks onto a target bench/block mesh: drape " +
            "each pick onto the surface, connect picks into per-fracture trace " +
            "curves, and (optionally) build fracture sheets from the surface down " +
            "to the reflector depth for use with Cut By Fractures / BlockCutOpt. " +
            "Feed Pick Points from GPR Radargram Mesh; put both in the same frame " +
            "with Move to Origin first.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A05-1A2B-4C3D-9E4F-5A6B7C8D9E05");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Target bench / block mesh (same coordinate frame as the picks).", GH_ParamAccess.item);
        p.AddPointParameter("Picks", "P", "GPR reflector pick points (from GPR Radargram Mesh).", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "Optional fracture label per pick (groups picks into distinct fractures). " +
            "If absent or mismatched, all picks form one fracture.", GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddIntegerParameter("Project", "X",
            "How a pick maps to the mesh: 0 = closest point on mesh, 1 = drop along -Z, " +
            "2 = raise along +Z. Default 0.", GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Make Sheets", "S",
            "Also build fracture sheets (ribbons from the draped surface point down to the " +
            "reflector pick) as meshes, for Cut By Fractures / BlockCutOpt. Default false.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Draped", "Pd", "Picks projected onto the mesh surface.", GH_ParamAccess.list);
        p.AddCurveParameter("Fracture Curves", "C", "One polyline per fracture, on the mesh surface.", GH_ParamAccess.list);
        p.AddMeshParameter("Fracture Sheets", "F", "Surface->reflector ribbon mesh per fracture (when Make Sheets).", GH_ParamAccess.list);
        p.AddTextParameter("Report", "R", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh mesh = null;
        var picks = new List<Point3d>();
        var labels = new List<string>();
        int proj = 0;
        bool makeSheets = false;
        if (!da.GetData(0, ref mesh) || mesh == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Target mesh required."); return; }
        if (!da.GetDataList(1, picks) || picks.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No picks provided."); return; }
        da.GetDataList(2, labels);
        da.GetData(3, ref proj);
        da.GetData(4, ref makeSheets);

        if (!mesh.IsValid) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Target mesh is not valid; closest-point may be approximate. Consider Sanitize Mesh.");

        bool useLabels = labels.Count == picks.Count && labels.Count > 0;

        // Drape each pick onto the mesh.
        var draped = new List<Point3d>(picks.Count);
        int draggedToClosest = 0;
        foreach (var pk in picks)
        {
            Point3d hit;
            if (proj == 0)
            {
                hit = mesh.ClosestPoint(pk);
            }
            else
            {
                var dir = proj == 1 ? -Vector3d.ZAxis : Vector3d.ZAxis;
                double t = Intersection.MeshRay(mesh, new Ray3d(pk, dir));
                if (t >= 0.0) hit = new Ray3d(pk, dir).PointAt(t);
                else { hit = mesh.ClosestPoint(pk); draggedToClosest++; }
            }
            draped.Add(hit);
        }

        // Group into fractures, ordered along the dominant horizontal axis.
        var groups = new Dictionary<string, List<int>>();
        for (int i = 0; i < picks.Count; i++)
        {
            string key = useLabels ? (labels[i] ?? "") : "GPR";
            if (!groups.TryGetValue(key, out var lst)) { lst = new List<int>(); groups[key] = lst; }
            lst.Add(i);
        }

        var curves = new List<Curve>();
        var sheets = new List<Mesh>();
        foreach (var kv in groups)
        {
            var idx = kv.Value;
            // order along X then Y (survey-line order); robust enough for trace lines.
            idx.Sort((a, b) =>
            {
                int cx = picks[a].X.CompareTo(picks[b].X);
                return cx != 0 ? cx : picks[a].Y.CompareTo(picks[b].Y);
            });
            if (idx.Count >= 2)
            {
                var poly = new Polyline(idx.Select(i => draped[i]));
                curves.Add(poly.ToPolylineCurve());

                if (makeSheets)
                {
                    var sheet = new Mesh();
                    for (int k = 0; k < idx.Count; k++)
                    {
                        sheet.Vertices.Add(draped[idx[k]]);   // 2k   : surface
                        sheet.Vertices.Add(picks[idx[k]]);    // 2k+1 : reflector depth
                    }
                    for (int k = 0; k < idx.Count - 1; k++)
                    {
                        int a = 2 * k, b = 2 * (k + 1);
                        sheet.Faces.AddFace(a, b, b + 1, a + 1);
                    }
                    sheet.Normals.ComputeNormals();
                    sheet.Compact();
                    if (sheet.Faces.Count > 0) sheets.Add(sheet);
                }
            }
        }

        if (draggedToClosest > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{draggedToClosest} pick(s) missed the mesh along the projection axis; used closest point instead.");

        da.SetDataList(0, draped);
        da.SetDataList(1, curves);
        da.SetDataList(2, sheets);
        da.SetData(3,
            $"Picks    : {picks.Count}\n" +
            $"Fractures: {groups.Count} ({(useLabels ? "by label" : "single group")})\n" +
            $"Projection: {(proj == 0 ? "closest point" : proj == 1 ? "drop -Z" : "raise +Z")}\n" +
            $"Curves   : {curves.Count}\n" +
            $"Sheets   : {sheets.Count}{(makeSheets ? "" : " (Make Sheets off)")}");
    }
}
