#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// MetashapeProject typed records (MRAC Proposal 1)
//
// Parsed view of an Agisoft Metashape .psx project. The .psx is an XML
// manifest pointing at a `.files/` directory containing zipped XML layers
// (project.zip -> chunk.zip -> frame.zip) plus point_cloud / model assets.
//
// v1 ships the metadata SURFACE: version + chunk count + active chunk +
// sensor calibration + camera / marker counts + chunk transform + resolved
// PLY path. v1.x adds per-camera pose extraction.
// =============================================================================

/// <summary>Sensor calibration from chunk.zip doc.xml (frame model).</summary>
public sealed class MetashapeSensor
{
    public string Label;
    public int WidthPx;
    public int HeightPx;
    public double PixelPitchMm; // pixel pitch in millimetres
    public double FocalMm;      // focal length in millimetres
    public double FocalPx;      // focal length in pixels (Metashape internal)
    public double Cx;           // principal-point offset x (px from center)
    public double Cy;           // principal-point offset y (px from center)
    public double K1, K2, K3;   // radial distortion
    public double P1, P2;       // tangential distortion
}

/// <summary>Marker observation summary (no per-camera observation list in v1).</summary>
public sealed class MetashapeMarker
{
    public string Label;
    public bool HasReferencePosition;
    public Point3d ReferencePosition; // world / reference coords if available
}

/// <summary>Chunk-level data (one Metashape chunk = one rig setup).</summary>
public sealed class MetashapeChunk
{
    public int Id;
    public string Label;
    /// <summary>Chunk transform: rotation 9 elements (row-major) + translation 3 + scale 1.</summary>
    public Transform ChunkTransform;
    public double ChunkScale;
    public MetashapeSensor[] Sensors;
    public int CameraCount;
    public MetashapeMarker[] Markers;
    public string ResolvedPlyPath;   // path inside .files/ (e.g. ".files/0/0/model/model.zip:mesh.ply")
    public string ResolvedPlyOnDisk; // path-on-disk after the component extracts mesh.ply to a temp dir
}

/// <summary>Top-level parsed Metashape project.</summary>
public sealed class MetashapeProject
{
    public string PsxPath;
    public string FilesDir;
    public string DocumentVersion; // e.g. "1.2.0"
    public string MetashapeVersion; // e.g. "1.8.2.0"
    public DateTime LastSavedUtc;   // file mtime fallback if not in XML
    public int ActiveChunkId;
    public MetashapeChunk[] Chunks;
}
