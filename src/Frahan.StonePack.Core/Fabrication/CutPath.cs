#nullable disable
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Fabrication;

// =============================================================================
// CutPath — typed record carrying a parsed G-code toolpath through the
// Frahan fabrication-side pipeline. Produced by GCodeParserComponent
// (D5F10030, Frahan > Fabrication > G-code Parser) and consumed by
// GCodeToPlanesComponent (D5F10031) → KUKAprc / Robots plugin downstream.
//
// Spec: wiki/specs/scan_to_mill_architecture.md §1.6 + research dossier at
// wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md.
//
// Mirrors QuarryBlock + VoussoirRecord pattern (simple public fields, no
// behaviour). Reference type so cheap on GH wires.
// =============================================================================

/// <summary>A single G-code segment: linear move (G01) or arc (G02/G03).</summary>
public sealed class CutSegment
{
    /// <summary>Segment kind. G01 = linear; G02 = clockwise arc; G03 = counter-clockwise arc.</summary>
    public CutSegmentKind Kind;

    /// <summary>Start point (carried from the previous segment's End or the initial G0/G1).</summary>
    public Point3d Start;

    /// <summary>End point (the X Y Z after the G-code command).</summary>
    public Point3d End;

    /// <summary>Arc center (I J K offsets relative to Start). Only meaningful for G02 / G03.
    /// I J are RhinoCAM-conventional XY-plane offsets; K is the optional Z offset.</summary>
    public Point3d ArcCenter;

    /// <summary>Tool feed rate in mm/min (or canvas units / min). Carried from the latest F-word.</summary>
    public double FeedRate;

    /// <summary>Spindle speed in RPM. Carried from the latest S-word.</summary>
    public double SpindleSpeed;

    /// <summary>Optional line number from the source .nc file (for error reporting + sequencing).</summary>
    public int LineNumber;

    /// <summary>Optional raw G-code line for debug / re-emission.</summary>
    public string RawLine;
}

public enum CutSegmentKind
{
    /// <summary>Rapid traverse (G00). Tool above stock; no cut.</summary>
    Rapid = 0,
    /// <summary>Linear cut (G01).</summary>
    Linear = 1,
    /// <summary>Clockwise arc (G02; CW in the XY plane).</summary>
    ArcCW = 2,
    /// <summary>Counter-clockwise arc (G03; CCW in the XY plane).</summary>
    ArcCCW = 3,
}

/// <summary>
/// The whole parsed toolpath. Carries an ordered list of CutSegment plus
/// the file-level metadata (spindle, units, default feed) needed by
/// downstream Plane / KUKAprc / Robots adapters.
/// </summary>
public sealed class CutPath
{
    /// <summary>Ordered list of segments in original .nc file order.</summary>
    public IReadOnlyList<CutSegment> Segments;

    /// <summary>Source .nc file path (for provenance + debug).</summary>
    public string SourceFile;

    /// <summary>Default units assumed for X Y Z values. Default "mm" (matches RhinoCAM 3-axis output).</summary>
    public string Units;

    /// <summary>Default spindle speed (RPM) read from the file header.</summary>
    public double DefaultSpindleSpeed;

    /// <summary>Default feed rate (units/min) read from the file header.</summary>
    public double DefaultFeedRate;

    /// <summary>Bounding box of all segment endpoints (for canvas-side preview + sanity check).</summary>
    public BoundingBox Bounds;

    /// <summary>Total path length in input units (sum of segment lengths, linear approximation for arcs).</summary>
    public double TotalLength;

    /// <summary>Optional dialect tag. Default "RhinoCAM_3axis"; future variants:
    /// "Fusion360_3axis", "SprutCAM_5axis", "MasterCAM_3axis", etc.</summary>
    public string Dialect;

    public CutPath()
    {
        Segments = new List<CutSegment>();
        SourceFile = "";
        Units = "mm";
        Dialect = "RhinoCAM_3axis";
    }
}
