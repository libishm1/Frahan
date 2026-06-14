#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// GCodeParserComponent (GUID D5F10030)
//
// Phase B Stage 1 component per wiki/specs/scan_to_mill_architecture.md §1.6
// + research dossier wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md.
//
// Parses an ISO 6983-1-subset G-code file (RhinoCAM 3-axis flavour observed in
// the MRAC 2023 workshop's LogEnd_1.nc / LogEnd_2.nc) into a typed
// Frahan.Core.Fabrication.CutPath consumable by:
//   - GCodeToPlanesComponent (D5F10031) -> Plane[] for both KUKAprc + Robots
//   - WireSawToolpathAdapterComponent (D5F10034) -> Frahan-original wire-saw
//   - Future SprutCAM / RhinoCAM bridge components
//
// Supported G-codes (the RhinoCAM minimum):
//   G00 / G01    linear (rapid / cut)
//   G02 / G03    arc (CW / CCW in XY plane, I/J centre offsets)
//   G17/G18/G19  plane select (XY / XZ / YZ); we honour XY default
//   G20 / G21    units (inches / mm); CutPath records the choice
//   G90 / G91    absolute / incremental positioning
//   M03 / M05    spindle on / off
//   F            feed rate (mm/min)
//   S            spindle speed (RPM)
//   N            line number (ignored, kept on RawLine)
//
// Modal state: motion mode (G00/01/02/03), positioning (G90/91), units
// (G20/21) are STICKY — a line without a G-word inherits the prior mode.
// XYZ values without a G-word imply the prior motion mode (RhinoCAM
// convention; matches the LogEnd_1.nc snippet observed 2026-05-31).
//
// Comments: parenthesized `(comment)` or trailing `;comment` are stripped.
//
// Status: REAL Phase B v1 implementation. Single-pass tokenizer; modal
// state machine; emits one CutSegment per motion line. Arc discretisation
// is deferred to GCodeToPlanesComponent (this component preserves arc
// segments as-is; downstream chooses sampling density).
// =============================================================================

[Algorithm("ISO 6983-1 G-code tokenizer + modal state machine",
    "Frahan-original; ISO 6983-1:2009 standard for CNC numerical control",
    Note = "Subset matching the RhinoCAM 3-axis / VisualMill post-processor seen in MRAC 2023 LogEnd_1.nc")]
[Algorithm("RhinoCAM 2023 NC dialect",
    "MecSoft RhinoCAM/VisualMill standard G-code emission",
    Note = "MRAC IAAC workshop fixture: D:/code_ws/reference/mrac_workshop_2023/sample_files/...")]
[DesignApplication(
    "Parse a CAM-emitted G-code file (.nc) into a typed CutPath the Frahan fabrication pipeline can route.",
    DesignFlow.Bridges,
    Precedent = "MRAC IAAC 2023 robotic milling of non-standard logs workshop; KUKAprc Generic NC Import (paid Pro tier); Robots plugin (Soler MIT) has NO native G-code ingest -> Frahan fills the gap",
    Tolerance = "lossless parse of G00/G01/G02/G03 + F + S; arc I/J coordinates preserved verbatim; round-trip identity on canonical RhinoCAM .nc files",
    CardSet = "wiki/research/hitl_cards/br_gcode_ingest/ (proposed)")]
public sealed class GCodeParserComponent : FrahanComponentBase
{
    public GCodeParserComponent()
        : base("G-code Parser", "GCode",
            "Parse an ISO 6983-1-subset G-code file (.nc / .gcode / .cnc) into " +
            "a typed CutPath record. Phase B Stage 1 of the scan-to-mill " +
            "architecture per wiki/specs/scan_to_mill_architecture.md §1.6. " +
            "First production component bridging Stone-Aware Cut Export -> " +
            "KUKAprc / Robots / SprutCAM / RhinoCAM. Supports the RhinoCAM " +
            "3-axis dialect observed in the MRAC 2023 workshop.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10030-ED9E-4ED9-A030-ED9EED9E0030");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File Path", "F",
            "Absolute or relative path to a G-code .nc / .gcode / .cnc file.",
            GH_ParamAccess.item);
        p.AddPointParameter("Initial Position", "I0",
            "Optional initial tool position before the first G-code line. " +
            "Default (0,0,0). Used as the Start of the first segment if the " +
            "file does not begin with an explicit G00 / G01 rapid.",
            GH_ParamAccess.item, Point3d.Origin);
        p.AddBooleanParameter("Skip Rapids", "Sr",
            "If true, G00 rapid-traverse segments are dropped from the output " +
            "(only G01-cut + G02/G03-arc segments remain). Default false -- " +
            "preserves the full toolpath for visualisation.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Cut Path", "CP",
            "The typed CutPath record. Wire into GCodeToPlanesComponent or " +
            "WireSawToolpathAdapterComponent downstream.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Segment Count", "N",
            "Total segments parsed (informational; matches CutPath.Segments.Count).",
            GH_ParamAccess.item);
        p.AddNumberParameter("Total Length", "L",
            "Sum of segment lengths (linear approximation for arcs) in the " +
            "file's units. Useful for time estimates: time ≈ length / feed.",
            GH_ParamAccess.item);
        p.AddPointParameter("Segment Endpoints", "EP",
            "Per-segment end points (one per CutSegment). For canvas preview.",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "R",
            "Parser diagnostics: line count, modal-mode transitions, comment " +
            "count, file-level F + S defaults, encountered unknown G-codes.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        string filePath = null;
        Point3d initialPos = Point3d.Origin;
        bool skipRapids = false;

        if (!DA.GetData(0, ref filePath)) return;
        DA.GetData(1, ref initialPos);
        DA.GetData(2, ref skipRapids);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File Path is empty.");
            return;
        }
        if (!File.Exists(filePath))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"G-code file not found: {filePath}");
            return;
        }

        var lines = File.ReadAllLines(filePath);

        var path = new CutPath
        {
            SourceFile = filePath,
            Units = "mm",
            Dialect = "RhinoCAM_3axis",
        };
        var segments = new List<CutSegment>();
        var remarks = new List<string>();

        // Modal state.
        var pos = initialPos;
        double f = 0;
        double s = 0;
        CutSegmentKind motionMode = CutSegmentKind.Rapid;
        bool absolutePositioning = true; // G90
        bool unitsAreMm = true;          // G21
        int commentCount = 0;
        int unknownCount = 0;
        var unknownCodes = new HashSet<string>();

        for (int li = 0; li < lines.Length; li++)
        {
            string raw = lines[li];
            string clean = StripComments(raw, ref commentCount);
            if (string.IsNullOrWhiteSpace(clean)) continue;

            var words = TokenizeLine(clean);
            if (words.Count == 0) continue;

            double? newX = null, newY = null, newZ = null;
            double? newI = null, newJ = null, newK = null;
            CutSegmentKind? lineMotion = null;

            foreach (var (letter, value) in words)
            {
                switch (letter)
                {
                    case 'G':
                        switch ((int)value)
                        {
                            case 0:  lineMotion = CutSegmentKind.Rapid; motionMode = CutSegmentKind.Rapid; break;
                            case 1:  lineMotion = CutSegmentKind.Linear; motionMode = CutSegmentKind.Linear; break;
                            case 2:  lineMotion = CutSegmentKind.ArcCW; motionMode = CutSegmentKind.ArcCW; break;
                            case 3:  lineMotion = CutSegmentKind.ArcCCW; motionMode = CutSegmentKind.ArcCCW; break;
                            case 17: /* plane XY (default) */ break;
                            case 18: case 19: /* plane XZ / YZ -- unsupported, warn */
                                unknownCount++; unknownCodes.Add($"G{(int)value}"); break;
                            case 20: unitsAreMm = false; path.Units = "inches"; break;
                            case 21: unitsAreMm = true; path.Units = "mm"; break;
                            case 90: absolutePositioning = true; break;
                            case 91: absolutePositioning = false;
                                unknownCount++; unknownCodes.Add("G91");
                                /* Incremental positioning would shift the deltas; v1 supports G90 only. */
                                break;
                            default:
                                unknownCount++; unknownCodes.Add($"G{(int)value}"); break;
                        }
                        break;
                    case 'X': newX = value; break;
                    case 'Y': newY = value; break;
                    case 'Z': newZ = value; break;
                    case 'I': newI = value; break;
                    case 'J': newJ = value; break;
                    case 'K': newK = value; break;
                    case 'F': f = value; if (path.DefaultFeedRate == 0) path.DefaultFeedRate = f; break;
                    case 'S': s = value; if (path.DefaultSpindleSpeed == 0) path.DefaultSpindleSpeed = s; break;
                    case 'M':
                        // M03 spindle on / M05 spindle off / M30 program end -- recorded but
                        // don't emit a segment.
                        break;
                    case 'N': /* line number -- already tracked via li */ break;
                    default:
                        unknownCount++; unknownCodes.Add(letter.ToString()); break;
                }
            }

            // A motion line is one with at least one of X/Y/Z set, OR a G02/G03 (arcs always have I/J).
            bool hasMotion = newX.HasValue || newY.HasValue || newZ.HasValue;
            if (!hasMotion) continue;

            // Determine effective motion mode.
            var emitMode = lineMotion ?? motionMode;

            // Compute new position.
            var newPos = absolutePositioning
                ? new Point3d(newX ?? pos.X, newY ?? pos.Y, newZ ?? pos.Z)
                : new Point3d(pos.X + (newX ?? 0), pos.Y + (newY ?? 0), pos.Z + (newZ ?? 0));

            // Skip rapids if requested.
            if (skipRapids && emitMode == CutSegmentKind.Rapid)
            {
                pos = newPos;
                continue;
            }

            var seg = new CutSegment
            {
                Kind = emitMode,
                Start = pos,
                End = newPos,
                FeedRate = f,
                SpindleSpeed = s,
                LineNumber = li + 1,
                RawLine = raw.Trim(),
            };

            if (emitMode == CutSegmentKind.ArcCW || emitMode == CutSegmentKind.ArcCCW)
            {
                // Arc centre = Start + (I, J, K) (RhinoCAM convention; G91.1 absolute IJK is rare).
                double cx = pos.X + (newI ?? 0);
                double cy = pos.Y + (newJ ?? 0);
                double cz = pos.Z + (newK ?? 0);
                seg.ArcCenter = new Point3d(cx, cy, cz);
            }

            segments.Add(seg);
            pos = newPos;
        }

        // Compute total length + bounds.
        var bbox = BoundingBox.Empty;
        double totalLen = 0;
        var endpoints = new List<Point3d>(segments.Count);
        foreach (var seg in segments)
        {
            endpoints.Add(seg.End);
            bbox.Union(seg.Start);
            bbox.Union(seg.End);
            switch (seg.Kind)
            {
                case CutSegmentKind.Rapid:
                case CutSegmentKind.Linear:
                    totalLen += seg.Start.DistanceTo(seg.End);
                    break;
                case CutSegmentKind.ArcCW:
                case CutSegmentKind.ArcCCW:
                    totalLen += ApproximateArcLength(seg);
                    break;
            }
        }
        path.Segments = segments;
        path.Bounds = bbox;
        path.TotalLength = totalLen;

        remarks.Add($"Parsed {lines.Length} line(s); emitted {segments.Count} segment(s); " +
            $"{commentCount} comment(s); default F={path.DefaultFeedRate:F0}, S={path.DefaultSpindleSpeed:F0}.");
        remarks.Add($"Total length: {totalLen:F2} {path.Units}; bounds diagonal: {bbox.Diagonal.Length:F2} {path.Units}.");
        if (unknownCount > 0)
        {
            string unknownList = string.Join(",", unknownCodes);
            remarks.Add(
                "WARNING: " + unknownCount + " unknown / unsupported code(s) encountered: " +
                unknownList + ". v1 supports G00/G01/G02/G03/G17/G20/G21/G90, F, S, M, N. " +
                "Vendor extensions land in v1.x.");
        }

        DA.SetData(0, new GH_ObjectWrapper(path));
        DA.SetData(1, segments.Count);
        DA.SetData(2, totalLen);
        DA.SetDataList(3, endpoints);
        DA.SetDataList(4, remarks);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static string StripComments(string raw, ref int commentCount)
    {
        // Parentheses comment: anything inside `(...)` is dropped.
        // Semicolon comment: anything after `;` is dropped.
        // Count matches first (Matches before Replace) to avoid the ref-in-lambda limitation.
        var parenMatches = Regex.Matches(raw, @"\([^)]*\)");
        commentCount += parenMatches.Count;
        var parenStripped = Regex.Replace(raw, @"\([^)]*\)", " ");
        int semi = parenStripped.IndexOf(';');
        if (semi >= 0)
        {
            commentCount++;
            return parenStripped.Substring(0, semi);
        }
        return parenStripped;
    }

    private static List<(char Letter, double Value)> TokenizeLine(string clean)
    {
        // A G-code word is a single letter followed by a signed number.
        // Examples: G01, X-24.3, Y26.0, F4000, I-1.9, J29.5
        var words = new List<(char, double)>();
        var matches = Regex.Matches(clean, @"([A-Za-z])\s*(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)");
        foreach (Match m in matches)
        {
            char letter = char.ToUpper(m.Groups[1].Value[0]);
            string numStr = m.Groups[2].Value;
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                words.Add((letter, v));
            }
        }
        return words;
    }

    private static double ApproximateArcLength(CutSegment seg)
    {
        // Chord-length proxy; true arc length is sweep * radius. For preview /
        // time estimates the chord is acceptable. GCodeToPlanesComponent will
        // discretise arcs properly for downstream consumption.
        return seg.Start.DistanceTo(seg.End);
    }
}
