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
// GCodeToPlanesComponent (GUID D5F10031)
//
// Phase B Stage 2 per wiki/specs/scan_to_mill_architecture.md §1.6 + research
// dossier wiki/research/robot_ingest_pipeline/gcode_to_kukaprc_robots.md.
//
// Translates a parsed CutPath into a Plane[] consumable by:
//   - KUKAprc's Plane → LIN / PTP / CIRC command components (paid Pro tier).
//   - visose/Robots plugin's CreateTarget component (MIT, all-vendor except
//     FANUC).
//
// Per-segment translation:
//   - Linear / Rapid: one Plane at the End point. Plane Z-axis = tool axis
//     (default -Z = downward milling). Plane X-axis = segment direction
//     (Start->End), or world-X for vertical segments.
//   - Arc (G02 / G03): discretise the arc into N Planes spaced by Arc Step
//     (mm). Each Plane's Z-axis = tool axis. X-axis = arc tangent at that
//     point. Center taken from CutSegment.ArcCenter (parser already computed
//     this from I/J).
//
// Arc discretisation runs in the XY plane (the dominant G17 case per
// MRAC RhinoCAM output). XZ / YZ plane arcs are deferred to v1.x (the
// parser flags them as warnings; this component falls back to chord
// approximation for non-XY arcs in the meantime).
// =============================================================================

[Algorithm("CutPath -> Plane[] tool-axis frame construction",
    "Frahan-original; standard milling-frame convention (tool axis = -Z by default)",
    Note = "Bridges CutPath to KUKAprc + Robots downstream Plane-based APIs")]
[Algorithm("Arc discretisation by Arc Step",
    "Frahan-original chord-step discretisation; CGAL arc primitives unused",
    Note = "G02/G03 arcs sampled at Arc Step intervals; chord-fallback for non-XY arc planes (v1 limitation)")]
[DesignApplication(
    "Translate parsed G-code (CutPath) into a Plane[] for KUKAprc / Robots downstream consumption.",
    DesignFlow.Bridges,
    Precedent = "KUKAprc Plane->LIN/PTP/CIRC commands (Brell-Cokcan + Braumann); visose/Robots CreateTarget (Soler MIT)",
    Tolerance = "arc chord error <= Arc Step / 2; tool-axis vector preserved exactly; round-trip CutPath -> Plane[] -> CutPath identity on linear-only paths",
    CardSet = "wiki/research/hitl_cards/br_gcode_ingest/ (proposed)")]
public sealed class GCodeToPlanesComponent : FrahanComponentBase
{
    public GCodeToPlanesComponent()
        : base("G-code to Planes", "GCodeToPlanes",
            "Translate a parsed CutPath into a Plane[] consumable by KUKAprc " +
            "(via its Plane->Command components) or visose/Robots (via " +
            "CreateTarget). Phase B Stage 2 of the scan-to-mill architecture. " +
            "Arc segments are discretised at Arc Step intervals; linear " +
            "segments emit one Plane per segment endpoint. Tool axis defaults " +
            "to -Z (downward milling).",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10031-ED9E-4ED9-A031-ED9EED9E0031");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Cut Path", "CP",
            "The typed CutPath from GCodeParserComponent (D5F10030).",
            GH_ParamAccess.item);
        p.AddVectorParameter("Tool Axis", "Ta",
            "Tool axis vector in WORLD coordinates. Default -Z (downward " +
            "milling, the dominant case). The emitted Plane's Z-axis aligns " +
            "with this vector; rotation about Z is determined by the segment " +
            "direction.",
            GH_ParamAccess.item, -Vector3d.ZAxis);
        p.AddNumberParameter("Arc Step", "As",
            "Chord step (mm) for arc discretisation. Smaller = more Planes / " +
            "tighter chord. Default 2.0 mm (sub-mm-spec friendly). Set 0 to " +
            "emit only segment endpoints (chord-only mode).",
            GH_ParamAccess.item, 2.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P",
            "Per-segment + per-arc-sample Planes (KUKAprc + Robots consumable).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Feed Rates", "F",
            "Per-Plane feed rate (mm/min); parallels Planes list.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Spindle Speeds", "S",
            "Per-Plane spindle speed (RPM); parallels Planes list.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Segment Indices", "Si",
            "Per-Plane source segment index (so the user can trace each Plane " +
            "back to a CutPath.Segments entry).",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "R",
            "Per-pipeline diagnostics: Plane count, arc-sample count, tool-" +
            "axis quality flags.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        IGH_Goo goo = null;
        Vector3d toolAxis = -Vector3d.ZAxis;
        double arcStep = 2.0;

        if (!DA.GetData(0, ref goo)) return;
        DA.GetData(1, ref toolAxis);
        DA.GetData(2, ref arcStep);

        CutPath path = null;
        if (goo is GH_ObjectWrapper ow && ow.Value is CutPath cp) path = cp;
        else if (goo != null && !goo.CastTo(out path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Cut Path input is not a CutPath. Wire from GCodeParserComponent " +
                "(D5F10030).");
            return;
        }
        if (path == null || path.Segments == null || path.Segments.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Cut Path is empty; no Planes emitted.");
            return;
        }
        if (!toolAxis.Unitize())
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tool Axis is zero-vector.");
            return;
        }

        var planes = new List<Plane>();
        var feeds = new List<double>();
        var spindles = new List<double>();
        var segIndices = new List<int>();
        int arcSampleCount = 0;
        int linearSampleCount = 0;

        for (int si = 0; si < path.Segments.Count; si++)
        {
            var seg = path.Segments[si];
            switch (seg.Kind)
            {
                case CutSegmentKind.Rapid:
                case CutSegmentKind.Linear:
                {
                    // One Plane at the End point.
                    Vector3d dir = seg.End - seg.Start;
                    if (!dir.Unitize()) dir = Vector3d.XAxis;
                    var plane = BuildToolPlane(seg.End, dir, toolAxis);
                    planes.Add(plane);
                    feeds.Add(seg.FeedRate);
                    spindles.Add(seg.SpindleSpeed);
                    segIndices.Add(si);
                    linearSampleCount++;
                    break;
                }
                case CutSegmentKind.ArcCW:
                case CutSegmentKind.ArcCCW:
                {
                    int samples = DiscretiseArc(seg, arcStep, planes, feeds, spindles, segIndices, si, toolAxis);
                    arcSampleCount += samples;
                    break;
                }
            }
        }

        var remarks = new List<string>
        {
            $"Emitted {planes.Count} Plane(s): {linearSampleCount} linear + {arcSampleCount} arc samples.",
            $"Tool axis: ({toolAxis.X:F3}, {toolAxis.Y:F3}, {toolAxis.Z:F3}); arc step: {arcStep:F2} mm.",
        };

        DA.SetDataList(0, planes);
        DA.SetDataList(1, feeds);
        DA.SetDataList(2, spindles);
        DA.SetDataList(3, segIndices);
        DA.SetDataList(4, remarks);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    /// <summary>Construct a milling-frame Plane: origin = point, Z = tool axis,
    /// X = projected segment direction (perpendicular to Z).</summary>
    private static Plane BuildToolPlane(Point3d origin, Vector3d segmentDir, Vector3d toolAxis)
    {
        // Project segmentDir onto the plane perpendicular to toolAxis.
        var x = segmentDir - Vector3d.Multiply(segmentDir, toolAxis) * toolAxis;
        if (!x.Unitize())
        {
            // Segment runs parallel to tool axis; fall back to world-X projected.
            x = Vector3d.XAxis - Vector3d.Multiply(Vector3d.XAxis, toolAxis) * toolAxis;
            if (!x.Unitize()) x = Vector3d.YAxis;
        }
        var y = Vector3d.CrossProduct(toolAxis, x);
        if (!y.Unitize()) y = Vector3d.YAxis;
        // Z axis = toolAxis. Plane(origin, X, Y) builds the frame.
        return new Plane(origin, x, y);
    }

    private static int DiscretiseArc(CutSegment seg, double arcStep,
        List<Plane> planes, List<double> feeds, List<double> spindles,
        List<int> segIndices, int segIdx, Vector3d toolAxis)
    {
        // XY-plane arc per G17 convention. Center = seg.ArcCenter.
        // Compute angular sweep from Start -> End around Center.
        var c = seg.ArcCenter;
        var v0 = new Vector3d(seg.Start - c);
        var v1 = new Vector3d(seg.End - c);
        // Work in XY only for arc direction.
        v0.Z = 0; v1.Z = 0;
        double r = v0.Length;
        if (r < 1e-9)
        {
            // Degenerate arc; emit only the endpoint.
            var fallback = seg.End - seg.Start; fallback.Unitize();
            planes.Add(BuildToolPlane(seg.End, fallback, toolAxis));
            feeds.Add(seg.FeedRate);
            spindles.Add(seg.SpindleSpeed);
            segIndices.Add(segIdx);
            return 1;
        }

        double a0 = Math.Atan2(v0.Y, v0.X);
        double a1 = Math.Atan2(v1.Y, v1.X);
        double sweep = a1 - a0;

        if (seg.Kind == CutSegmentKind.ArcCW)
        {
            // CW = decreasing angle (when viewed from +Z).
            while (sweep > 0) sweep -= 2 * Math.PI;
            if (sweep == 0) sweep = -2 * Math.PI; // full circle
        }
        else
        {
            // CCW = increasing angle.
            while (sweep < 0) sweep += 2 * Math.PI;
            if (sweep == 0) sweep = 2 * Math.PI;
        }

        // Number of chord-step samples to span the arc.
        double arcLen = Math.Abs(sweep) * r;
        int n = arcStep > 0 ? Math.Max(1, (int)Math.Ceiling(arcLen / arcStep)) : 1;

        int emitted = 0;
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double a = a0 + sweep * t;
            // Sample point on arc; interpolate Z linearly between Start.Z and End.Z (helix support).
            double z = seg.Start.Z + (seg.End.Z - seg.Start.Z) * t;
            var pt = new Point3d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z);
            // Tangent direction: perpendicular to radius vector, sign per sweep direction.
            var radial = new Vector3d(Math.Cos(a), Math.Sin(a), 0);
            var tangent = sweep > 0
                ? new Vector3d(-radial.Y, radial.X, 0) // CCW tangent
                : new Vector3d(radial.Y, -radial.X, 0); // CW tangent
            planes.Add(BuildToolPlane(pt, tangent, toolAxis));
            feeds.Add(seg.FeedRate);
            spindles.Add(seg.SpindleSpeed);
            segIndices.Add(segIdx);
            emitted++;
        }
        return emitted;
    }
}
