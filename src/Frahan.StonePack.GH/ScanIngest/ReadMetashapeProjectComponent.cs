#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// ReadMetashapeProjectComponent (MRAC Proposal 1) - REAL GAP closer
//
// Reads an Agisoft Metashape .psx project (XML + nested zipped XML).
// Surfaces: document + Metashape version, chunk count, active chunk id,
// per-chunk sensor calibration, chunk transform, camera + marker counts,
// resolved mesh.ply path. Optionally extracts mesh.ply to a temp dir for
// downstream Load PLY Mesh consumption.
//
// No existing Rhino/GH plugin reads .psx natively per the gap-map
// (wiki/research/fabrication_bridge_gap_map.md §Scan ingest).
// =============================================================================

[Algorithm("XML + nested-zip walk: .psx -> .files/project.zip -> 0/chunk.zip",
    "Frahan-original; tolerant XML parser handles version skew",
    Note = "v1 surfaces metadata + resolves mesh.ply path; v1.x extends with per-camera pose extraction")]
[DesignApplication(
    "Read Agisoft Metashape .psx projects into a typed MetashapeProject record (sensor calibration + chunk transform + marker positions + mesh path). Closes the photogrammetry-project ingest gap.",
    DesignFlow.Bridges,
    Precedent = "MRAC IAAC Barcelona 2023 photogrammetry workflow (Metashape 1.8.2.0) per wiki/research/mrac_workshop_2023/exercise_dossier.md; Agisoft Metashape File > Save As convention",
    Tolerance = "tolerant XML parse (handles version skew); chunk transform parsed to within float precision; mesh.ply path resolves to exact on-disk file when Extract Mesh is true",
    CardSet = "Template-General/outputs/2026-05-31/hitl_cards/scan_to_cut_pipeline/cards/01_load_quarry_scan_ply.md (related)")]
public sealed class ReadMetashapeProjectComponent : FrahanComponentBase
{
    public ReadMetashapeProjectComponent()
        : base("Read Metashape Project", "ReadPsx",
            "Read an Agisoft Metashape .psx project into a typed " +
            "MetashapeProject record: sensor calibration, chunk transform, " +
            "camera + marker counts, and the resolved mesh.ply path. With " +
            "Extract Mesh true, the component unzips mesh.ply to a temp " +
            "dir and returns the on-disk path so Frahan's Load PLY Mesh " +
            "component can consume it directly.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10042-ED9E-4ED9-A042-ED9EED9E0042");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap Icon => IconProvider.Load("Downsample.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Psx File", "F",
            "Path to an Agisoft Metashape .psx project descriptor. The " +
            "sibling .files/ directory must be present (Metashape's standard " +
            "save convention).",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Chunk Id", "Ci",
            "Which chunk to return. Default 0 (the active chunk in most " +
            "projects). Use -1 to return the project's active chunk per the " +
            ".psx active_id.",
            GH_ParamAccess.item, -1);
        p.AddBooleanParameter("Extract Mesh", "Em",
            "If true, extract the chunk's mesh.ply to a temp dir and return " +
            "the on-disk path via Resolved Ply. Default true.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Metashape Project", "MP",
            "Typed MetashapeProject record (Frahan.Core.ScanIngest.MetashapeProject).",
            GH_ParamAccess.item);
        p.AddTextParameter("Doc Version", "Dv",
            "Document schema version (e.g. 1.2.0).", GH_ParamAccess.item);
        p.AddTextParameter("Metashape Version", "Mv",
            "Metashape application version if recoverable.", GH_ParamAccess.item);
        p.AddIntegerParameter("Chunk Count", "Nc",
            "Number of chunks in the project.", GH_ParamAccess.item);
        p.AddPlaneParameter("Chunk Plane", "Cp",
            "Chunk transform represented as a Rhino Plane (origin + axes).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Chunk Scale", "Cs",
            "Chunk scale factor (Metashape internal units -> world units).",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Camera Count", "Nc2",
            "Camera count in the selected chunk.", GH_ParamAccess.item);
        p.AddPointParameter("Marker World Positions", "Mp",
            "Reference (world) marker positions, one per marker with reference data.",
            GH_ParamAccess.list);
        p.AddTextParameter("Marker Labels", "Ml",
            "Marker labels parallel to Marker World Positions.",
            GH_ParamAccess.list);
        p.AddTextParameter("Resolved Ply", "Ply",
            "On-disk path to mesh.ply (when Extract Mesh = true). Wire into " +
            "Frahan's Load PLY Mesh.",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "R",
            "Per-pipeline diagnostics + parser flags.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        string psxPath = null;
        int chunkSel = -1;
        bool extractMesh = true;

        if (!DA.GetData(0, ref psxPath)) return;
        DA.GetData(1, ref chunkSel);
        DA.GetData(2, ref extractMesh);

        MetashapeProject project;
        try
        {
            project = MetashapeReader.Read(psxPath);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Metashape read failed: " + ex.Message);
            return;
        }

        if (project.Chunks == null || project.Chunks.Length == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Project has no chunks.");
            return;
        }

        int activeId = chunkSel < 0 ? project.ActiveChunkId : chunkSel;
        var chunk = FindChunkById(project, activeId) ?? project.Chunks[0];

        // Convert chunk transform to a Rhino Plane (origin + X/Y/Z axes).
        var ct = chunk.ChunkTransform;
        var origin = new Point3d(ct.M03, ct.M13, ct.M23);
        var xaxis = new Vector3d(ct.M00, ct.M10, ct.M20);
        var yaxis = new Vector3d(ct.M01, ct.M11, ct.M21);
        if (!xaxis.IsValid || !yaxis.IsValid || xaxis.IsZero || yaxis.IsZero)
        {
            xaxis = Vector3d.XAxis;
            yaxis = Vector3d.YAxis;
        }
        var chunkPlane = new Plane(origin, xaxis, yaxis);

        // Markers
        var markerPts = new List<Point3d>();
        var markerLabels = new List<string>();
        if (chunk.Markers != null)
        {
            foreach (var m in chunk.Markers)
            {
                if (m.HasReferencePosition)
                {
                    markerPts.Add(m.ReferencePosition);
                    markerLabels.Add(m.Label ?? "");
                }
            }
        }

        string plyPath = null;
        if (extractMesh && !string.IsNullOrEmpty(chunk.ResolvedPlyPath))
        {
            plyPath = MetashapeReader.ExtractMeshPly(chunk);
        }
        else if (!string.IsNullOrEmpty(chunk.ResolvedPlyPath))
        {
            // Just report the in-zip path without extracting.
            plyPath = chunk.ResolvedPlyPath;
        }

        var remarks = new List<string>
        {
            "Document version: " + (project.DocumentVersion ?? "<unknown>"),
            "Metashape version: " + (project.MetashapeVersion ?? "<unknown>"),
            "Last saved (file mtime): " + project.LastSavedUtc.ToString("u"),
            "Chunks: " + project.Chunks.Length + "; active id: " + project.ActiveChunkId,
            "Selected chunk: id=" + chunk.Id + " label=" + (chunk.Label ?? "") +
                " sensors=" + (chunk.Sensors?.Length ?? 0) +
                " cameras=" + chunk.CameraCount +
                " markers=" + (chunk.Markers?.Length ?? 0),
            "Chunk scale: " + chunk.ChunkScale.ToString("G6"),
            extractMesh
                ? "Mesh: " + (plyPath ?? "<none>") + " (extracted to temp dir)"
                : "Mesh path (in-zip): " + (plyPath ?? "<none>"),
            "v1 limitation: per-camera pose extraction deferred to v1.x."
        };

        DA.SetData(0, new GH_ObjectWrapper(project));
        DA.SetData(1, project.DocumentVersion);
        DA.SetData(2, project.MetashapeVersion);
        DA.SetData(3, project.Chunks.Length);
        DA.SetData(4, chunkPlane);
        DA.SetData(5, chunk.ChunkScale);
        DA.SetData(6, chunk.CameraCount);
        DA.SetDataList(7, markerPts);
        DA.SetDataList(8, markerLabels);
        DA.SetData(9, plyPath ?? "");
        DA.SetDataList(10, remarks);
    }

    private static MetashapeChunk FindChunkById(MetashapeProject p, int id)
    {
        if (p.Chunks == null) return null;
        foreach (var c in p.Chunks)
            if (c.Id == id) return c;
        return null;
    }
}
