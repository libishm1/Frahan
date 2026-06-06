#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Registration;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// ImportPhotoMarkersComponent — "Import Photo Markers".
//
// Import photogrammetry markers / GCPs (Metashape / COLMAP / RealityCapture
// export, or a plain GCP CSV) so a floating photogrammetry result can be
// positioned + scaled onto a known base. Outputs World points (target/base
// frame) and, when present, Model points (source/scan frame). Wire World ->
// Target and Model -> Source of **Georeference (Align by Points)** (with Scale =
// true for photogrammetry's unknown scale) to place the scan. If the file has
// no model points, pick them in Rhino on the scan's markers and wire those as
// Georeference Source.
//
// We ingest markers; we do NOT reconstruct photogrammetry (see future-work note).
// =============================================================================

[DesignApplication(
    "Read photogrammetry markers / GCPs from a CSV (Metashape / COLMAP /  RealityCapture export or a plain GCP f...",
    DesignFlow.Bridges,
    Precedent = "OpenCV ArUco / ChArUco markers; ARCore alignment (per project_photogrammetry_scope_decision)")]
public sealed class ImportPhotoMarkersComponent : GH_Component
{
    public ImportPhotoMarkersComponent()
        : base("Import Photo Markers", "PhotoMarkers",
            "Read photogrammetry markers / GCPs from a CSV (Metashape / COLMAP / "
            + "RealityCapture export or a plain GCP file): "
            + "'label, worldX,Y,Z [, modelX,Y,Z]'. Outputs World points (base "
            + "frame) + Model points (scan frame, if present) + labels. Feed "
            + "World -> Target and Model -> Source of Georeference (Align by "
            + "Points), Scale = true, to position the scan on its base.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D07A03-1A2B-4C3D-9E4F-5A6B7C8D9E03");
    protected override Bitmap Icon => IconProvider.Load("GeoreferenceMarker.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File", "F", "Path to a marker / GCP CSV.", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Labels", "L", "Per-marker label.", GH_ParamAccess.list);
        p.AddPointParameter("World", "W", "Marker positions in the base / world frame (-> Georeference Target).", GH_ParamAccess.list);
        p.AddPointParameter("Model", "Mo", "Marker positions in the scan / model frame, if present (-> Georeference Source).", GH_ParamAccess.list);
        p.AddBooleanParameter("Has Model", "Hm", "True if the file carried model-frame positions.", GH_ParamAccess.item);
        p.AddIntegerParameter("Count", "N", "Number of markers read.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string path = null;
        if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No marker file path provided."); return; }

        IReadOnlyList<MarkerControlPoint> markers;
        try { markers = MarkerFileReader.Read(path); }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Read failed: {ex.Message}"); return; }

        if (markers.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No markers parsed (expected 'label, worldX,Y,Z [, modelX,Y,Z]')."); }

        var labels = new List<string>(markers.Count);
        var world = new List<Point3d>(markers.Count);
        var model = new List<Point3d>(markers.Count);
        bool anyModel = false;
        foreach (var m in markers)
        {
            labels.Add(m.Label);
            world.Add(new Point3d(m.World[0], m.World[1], m.World[2]));
            if (m.HasModel) { model.Add(new Point3d(m.Model[0], m.Model[1], m.Model[2])); anyModel = true; }
        }

        if (!anyModel)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No model-frame points in file. Pick the markers on the scan in Rhino and wire those as Georeference Source.");

        da.SetDataList(0, labels);
        da.SetDataList(1, world);
        da.SetDataList(2, model);
        da.SetData(3, anyModel);
        da.SetData(4, markers.Count);
    }
}
