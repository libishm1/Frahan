#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// BlockFaceMapComponent (D5F10056, Frahan > Fabricate)
//
// Cross-unfolds the six faces of a world-aligned block or bench box into one flat
// 2D shop-sheet frame -- the top (PLAN) face sits in the middle and the four side
// elevations fold outward from it, each keeping its shared top edge adjacent to the
// plan, the way a fabricator's shop drawing lays a block open. Bed / flaw / joint
// meshes are sectioned against every face plane and the resulting traces are mapped
// into the same sheet frame, and plan-view saw passes are mapped both onto the plan
// and onto the elevation view(s) they actually reach. Downstream, DXF Cut Plan turns
// the outlines / traces / cut lines into a layered DXF a stone-CAM or a yard crew can
// read directly.
// =============================================================================

[Algorithm("Unfolded block face map", "Cross-unfold of the block faces; fracture traces via mesh-plane sections, saw passes mapped per face",
    Note = "The shop-drawing view a fabricator reads. Cut labels pass through the mapping: a plan pass and its elevation views share one label (one physical cut = one number across views).")]
[RelatedComponent("Frahan > Fabricate > DXF Cut Plan", Reason = "Face outlines / ids / fracture traces / cut traces feed straight into the DXF export (FRACTURES + CUT_SEQUENCE layers).")]
[RelatedComponent("Frahan > Block > Fracture Block Pack", Reason = "Its Saw passes output is the Cut lines input here.")]
[RelatedComponent("Frahan > Quarry > GPR Fracture Surfaces 3D", Reason = "Its kriged bed meshes are the Fractures input.")]
public sealed class BlockFaceMapComponent : FrahanComponentBase
{
    public BlockFaceMapComponent()
        : base("Block Face Map", "FaceMap",
            "Cross-unfold the six faces of a (world-aligned) block or bench box into one 2D shop-sheet " +
            "frame and map bed/flaw traces (mesh-plane sections) plus saw-pass positions onto every face. " +
            "Feed the outputs into DXF Cut Plan (Curves / Piece Ids / Fracture traces / Cut lines) for a " +
            "fabricator-readable layered DXF.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10056-ED9E-4ED9-A056-ED9EED9E0056");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DxfCutPlan.png");

    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // Face order fixed by the spec: PLAN, NORTH, SOUTH, EAST, WEST, then BOTTOM if requested.
    private enum Face { Plan, North, South, East, West, Bottom }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBoxParameter("Block", "B", "World-aligned block / bench box to unfold.", GH_ParamAccess.item);
        p.AddMeshParameter("Fractures", "F", "Bed / flaw / joint meshes to trace onto every face (mesh-plane section).", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddLineParameter("Cut lines", "Cl",
            "Plan-view saw passes (vertical cuts), e.g. Fracture Block Pack > Saw passes. Mapped onto the " +
            "plan and onto each elevation they reach.", GH_ParamAccess.list);
        p[2].Optional = true;
        p.AddNumberParameter("Gap", "G", "Spacing between unfolded faces (model units).", GH_ParamAccess.item, 0.15);
        p.AddBooleanParameter("Bottom", "Bt", "Also emit the bottom face.", GH_ParamAccess.item, false);
        p.AddTextParameter("Cut labels", "Cll",
            "Optional label per cut line, parallel to Cut lines (e.g. Q1../F1.. from Cut Stage Split). " +
            "Absent = sequential 1..N.", GH_ParamAccess.list);
        p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Face outlines", "O", "Closed rectangle outline per unfolded face.", GH_ParamAccess.list);
        p.AddTextParameter("Face ids", "Id", "Face name per outline: PLAN, NORTH, SOUTH, EAST, WEST, BOTTOM.", GH_ParamAccess.list);
        p.AddCurveParameter("Fracture traces", "Ft", "Fracture traces mapped into the unfolded sheet frame.", GH_ParamAccess.list);
        p.AddLineParameter("Cut traces", "Ct",
            "Saw-pass traces: plan passes first (input order, so their sheet numbers match the saw " +
            "sequence), then the elevation views of each pass.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Re", "Per-face summary.", GH_ParamAccess.item);
        p.AddTextParameter("Trace labels", "Tl",
            "Label per cut trace, parallel to Cut traces. Plan traces carry their pass label; each " +
            "elevation view repeats the label of the pass it shows.", GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Box box = Box.Unset;
        if (!da.GetData(0, ref box) || !box.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Provide a valid block Box."); return; }
        var fractures = new List<Mesh>(); da.GetDataList(1, fractures);
        var cutLinesIn = new List<Line>(); da.GetDataList(2, cutLinesIn);
        double gap = 0.15; da.GetData(3, ref gap);
        bool bottom = false; da.GetData(4, ref bottom);
        var labels = new List<string>(); da.GetDataList(5, labels);

        var bb = box.BoundingBox;
        Point3d min = bb.Min, max = bb.Max;
        double L = max.X - min.X, W = max.Y - min.Y, H = max.Z - min.Z;
        if (L <= 0.0 || W <= 0.0 || H <= 0.0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block must have positive length, width and height."); return; }

        var ff = new FaceFrame(min, max, L, W, H, gap);
        var faces = new List<Face> { Face.Plan, Face.North, Face.South, Face.East, Face.West };
        if (bottom) faces.Add(Face.Bottom);

        // ---- face outlines + ids ----
        var outlines = new List<Curve>();
        var ids = new List<string>();
        foreach (var face in faces)
        {
            var corners = FaceCornersWorld(face, min, max);
            var sheetPts = corners.Select(c => ff.MapToFace(face, c)).ToList();
            sheetPts.Add(sheetPts[0]);
            outlines.Add(new Polyline(sheetPts).ToPolylineCurve());
            ids.Add(FaceName(face));
        }

        // ---- fracture traces: mesh-plane section per face, clipped to the face rect, mapped to sheet ----
        var fractureTraces = new List<Curve>();
        var chunkCounts = new Dictionary<Face, int>();
        foreach (var face in faces)
        {
            chunkCounts[face] = 0;
            if (fractures.Count == 0) continue;
            Plane plane = SectionPlane(face, min, max);
            LocalRanges(face, L, W, H, out double umax, out double vmax);
            foreach (var mesh in fractures)
            {
                if (mesh == null || !mesh.IsValid) continue;
                Polyline[] sections;
                try { sections = Rhino.Geometry.Intersect.Intersection.MeshPlane(mesh, plane); }
                catch { sections = null; }
                if (sections == null) continue;
                foreach (var pl in sections)
                {
                    if (pl == null || pl.Count < 2) continue;
                    var segs = pl.GetSegments();
                    if (segs == null) continue;
                    List<Point3d> chunk = null;
                    foreach (var seg in segs)
                    {
                        Point3d p0 = seg.From, p1 = seg.To;
                        LocalUV(face, p0, min, out double u0, out double v0);
                        LocalUV(face, p1, min, out double u1, out double v1);
                        bool hit = LiangBarskyClip(u0, v0, u1, v1, umax, vmax, out double t0, out double t1);
                        if (!hit)
                        {
                            EmitChunk(chunk, fractureTraces, chunkCounts, face);
                            chunk = null;
                            continue;
                        }
                        Point3d wStart = Lerp(p0, p1, t0);
                        Point3d wEnd = Lerp(p0, p1, t1);
                        Point3d sStart = ff.MapToFace(face, wStart);
                        Point3d sEnd = ff.MapToFace(face, wEnd);
                        if (chunk == null)
                        {
                            chunk = new List<Point3d> { sStart, sEnd };
                        }
                        else if (chunk[chunk.Count - 1].DistanceTo(sStart) <= 1e-9)
                        {
                            chunk.Add(sEnd);
                        }
                        else
                        {
                            EmitChunk(chunk, fractureTraces, chunkCounts, face);
                            chunk = new List<Point3d> { sStart, sEnd };
                        }
                    }
                    EmitChunk(chunk, fractureTraces, chunkCounts, face);
                }
            }
        }

        // ---- cut traces: plan passes (input order) first, then each pass's elevation view(s).
        // Labels pass through: a plan trace and every elevation view of the same pass share
        // one label (one physical cut = one number across views, standard drafting). ----
        var cutTraces = new List<Line>();
        var traceLabels = new List<string>();
        string LabelOf(int i) => (i < labels.Count && !string.IsNullOrEmpty(labels[i]))
            ? labels[i] : (i + 1).ToString(CI);
        double tol = 0.02 * Math.Max(L, W);
        for (int i = 0; i < cutLinesIn.Count; i++)
        {
            var line = cutLinesIn[i];
            double dx = Math.Abs(line.To.X - line.From.X);
            double dy = Math.Abs(line.To.Y - line.From.Y);
            bool xRip = dx < dy;
            if (xRip)
            {
                double c = Clamp(line.From.X, min.X, max.X);
                double lo = Clamp(Math.Min(line.From.Y, line.To.Y), min.Y, max.Y);
                double hi = Clamp(Math.Max(line.From.Y, line.To.Y), min.Y, max.Y);
                cutTraces.Add(new Line(
                    ff.MapToFace(Face.Plan, new Point3d(c, lo, min.Z)),
                    ff.MapToFace(Face.Plan, new Point3d(c, hi, min.Z))));
            }
            else
            {
                double c = Clamp(line.From.Y, min.Y, max.Y);
                double lo = Clamp(Math.Min(line.From.X, line.To.X), min.X, max.X);
                double hi = Clamp(Math.Max(line.From.X, line.To.X), min.X, max.X);
                cutTraces.Add(new Line(
                    ff.MapToFace(Face.Plan, new Point3d(lo, c, min.Z)),
                    ff.MapToFace(Face.Plan, new Point3d(hi, c, min.Z))));
            }
            traceLabels.Add(LabelOf(i));
        }
        int elevationViews = 0;
        for (int i = 0; i < cutLinesIn.Count; i++)
        {
            var line = cutLinesIn[i];
            double dx = Math.Abs(line.To.X - line.From.X);
            double dy = Math.Abs(line.To.Y - line.From.Y);
            bool xRip = dx < dy;
            if (xRip)
            {
                double c = line.From.X;
                if (c < min.X - tol || c > max.X + tol) continue;
                double lo = Math.Min(line.From.Y, line.To.Y);
                double hi = Math.Max(line.From.Y, line.To.Y);
                if (faces.Contains(Face.North) && hi >= max.Y - tol)
                {
                    cutTraces.Add(new Line(
                        ff.MapToFace(Face.North, new Point3d(c, max.Y, min.Z)),
                        ff.MapToFace(Face.North, new Point3d(c, max.Y, max.Z))));
                    traceLabels.Add(LabelOf(i));
                    elevationViews++;
                }
                if (faces.Contains(Face.South) && lo <= min.Y + tol)
                {
                    cutTraces.Add(new Line(
                        ff.MapToFace(Face.South, new Point3d(c, min.Y, min.Z)),
                        ff.MapToFace(Face.South, new Point3d(c, min.Y, max.Z))));
                    traceLabels.Add(LabelOf(i));
                    elevationViews++;
                }
            }
            else
            {
                double c = line.From.Y;
                if (c < min.Y - tol || c > max.Y + tol) continue;
                double lo = Math.Min(line.From.X, line.To.X);
                double hi = Math.Max(line.From.X, line.To.X);
                if (faces.Contains(Face.East) && hi >= max.X - tol)
                {
                    cutTraces.Add(new Line(
                        ff.MapToFace(Face.East, new Point3d(max.X, c, min.Z)),
                        ff.MapToFace(Face.East, new Point3d(max.X, c, max.Z))));
                    traceLabels.Add(LabelOf(i));
                    elevationViews++;
                }
                if (faces.Contains(Face.West) && lo <= min.X + tol)
                {
                    cutTraces.Add(new Line(
                        ff.MapToFace(Face.West, new Point3d(min.X, c, min.Z)),
                        ff.MapToFace(Face.West, new Point3d(min.X, c, max.Z))));
                    traceLabels.Add(LabelOf(i));
                    elevationViews++;
                }
            }
        }

        // ---- report ----
        var rpt = new StringBuilder();
        rpt.AppendLine($"block {L.ToString("0.###", CI)} x {W.ToString("0.###", CI)} x {H.ToString("0.###", CI)} (L x W x H), gap {gap.ToString("0.###", CI)}");
        rpt.AppendLine("faces emitted: " + string.Join(", ", faces.Select(FaceName)));
        foreach (var face in faces)
            rpt.AppendLine($"  {FaceName(face)}: {chunkCounts[face]} fracture trace chunk(s)");
        rpt.AppendLine($"cut traces: {cutLinesIn.Count} plan pass(es) + {elevationViews} elevation view(s)");
        rpt.AppendLine("plan traces and elevation views share one label per physical pass (see Trace labels).");

        da.SetDataList(0, outlines);
        da.SetDataList(1, ids);
        da.SetDataList(2, fractureTraces);
        da.SetDataList(3, cutTraces);
        da.SetData(4, rpt.ToString().TrimEnd());
        da.SetDataList(5, traceLabels);
    }

    private static void EmitChunk(List<Point3d> chunk, List<Curve> traces, Dictionary<Face, int> counts, Face face)
    {
        if (chunk == null || chunk.Count < 2) return;
        traces.Add(new Polyline(chunk).ToPolylineCurve());
        counts[face] = counts[face] + 1;
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private static Point3d Lerp(Point3d a, Point3d b, double t) =>
        new Point3d(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), a.Z + t * (b.Z - a.Z));

    private static string FaceName(Face f)
    {
        switch (f)
        {
            case Face.Plan: return "PLAN";
            case Face.North: return "NORTH";
            case Face.South: return "SOUTH";
            case Face.East: return "EAST";
            case Face.West: return "WEST";
            case Face.Bottom: return "BOTTOM";
            default: return f.ToString().ToUpperInvariant();
        }
    }

    // World-space corners of one face of the block, in a consistent winding order.
    private static Point3d[] FaceCornersWorld(Face face, Point3d min, Point3d max)
    {
        switch (face)
        {
            case Face.Plan:
                return new[]
                {
                    new Point3d(min.X, min.Y, max.Z), new Point3d(max.X, min.Y, max.Z),
                    new Point3d(max.X, max.Y, max.Z), new Point3d(min.X, max.Y, max.Z),
                };
            case Face.North:
                return new[]
                {
                    new Point3d(min.X, max.Y, max.Z), new Point3d(max.X, max.Y, max.Z),
                    new Point3d(max.X, max.Y, min.Z), new Point3d(min.X, max.Y, min.Z),
                };
            case Face.South:
                return new[]
                {
                    new Point3d(min.X, min.Y, max.Z), new Point3d(max.X, min.Y, max.Z),
                    new Point3d(max.X, min.Y, min.Z), new Point3d(min.X, min.Y, min.Z),
                };
            case Face.East:
                return new[]
                {
                    new Point3d(max.X, min.Y, max.Z), new Point3d(max.X, max.Y, max.Z),
                    new Point3d(max.X, max.Y, min.Z), new Point3d(max.X, min.Y, min.Z),
                };
            case Face.West:
                return new[]
                {
                    new Point3d(min.X, min.Y, max.Z), new Point3d(min.X, max.Y, max.Z),
                    new Point3d(min.X, max.Y, min.Z), new Point3d(min.X, min.Y, min.Z),
                };
            case Face.Bottom:
                return new[]
                {
                    new Point3d(max.X, min.Y, min.Z), new Point3d(max.X, max.Y, min.Z),
                    new Point3d(min.X, max.Y, min.Z), new Point3d(min.X, min.Y, min.Z),
                };
            default:
                return Array.Empty<Point3d>();
        }
    }

    // Mesh-plane section plane per face, as specified: PLAN/BOTTOM cut horizontally at the
    // top/bottom Z, the four elevations cut vertically at their own face coordinate.
    private static Plane SectionPlane(Face face, Point3d min, Point3d max)
    {
        switch (face)
        {
            case Face.Plan: return new Plane(new Point3d(0, 0, max.Z), Vector3d.ZAxis);
            case Face.North: return new Plane(new Point3d(0, max.Y, 0), Vector3d.YAxis);
            case Face.South: return new Plane(new Point3d(0, min.Y, 0), Vector3d.YAxis);
            case Face.West: return new Plane(new Point3d(min.X, 0, 0), Vector3d.XAxis);
            case Face.East: return new Plane(new Point3d(max.X, 0, 0), Vector3d.XAxis);
            case Face.Bottom: return new Plane(new Point3d(0, 0, min.Z), Vector3d.ZAxis);
            default: return Plane.WorldXY;
        }
    }

    // Face-local (u, v) extents used to clip fracture traces before mapping to the sheet.
    private static void LocalRanges(Face face, double L, double W, double H, out double umax, out double vmax)
    {
        switch (face)
        {
            case Face.Plan:
            case Face.Bottom:
                umax = L; vmax = W; break;
            case Face.North:
            case Face.South:
                umax = L; vmax = H; break;
            case Face.East:
            case Face.West:
                umax = W; vmax = H; break;
            default:
                umax = L; vmax = W; break;
        }
    }

    // Face-local (u, v) coordinate of a world point, matching LocalRanges per face.
    private static void LocalUV(Face face, Point3d wp, Point3d min, out double u, out double v)
    {
        double xb = wp.X - min.X, yb = wp.Y - min.Y, zb = wp.Z - min.Z;
        switch (face)
        {
            case Face.Plan:
            case Face.Bottom:
                u = xb; v = yb; break;
            case Face.North:
            case Face.South:
                u = xb; v = zb; break;
            case Face.East:
            case Face.West:
                u = yb; v = zb; break;
            default:
                u = xb; v = yb; break;
        }
    }

    // Liang-Barsky clip of segment (u0,v0)-(u1,v1) against the axis-aligned rect [0,umax] x
    // [0,vmax]. Returns false if the segment lies entirely outside; otherwise t0 <= t1 in
    // [0,1] mark the surviving fraction of the ORIGINAL segment.
    private static bool LiangBarskyClip(double u0, double v0, double u1, double v1,
        double umax, double vmax, out double t0, out double t1)
    {
        t0 = 0.0; t1 = 1.0;
        double du = u1 - u0, dv = v1 - v0;
        double[] p = { -du, du, -dv, dv };
        double[] q = { u0 - 0.0, umax - u0, v0 - 0.0, vmax - v0 };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12)
            {
                if (q[i] < 0.0) return false;
                continue;
            }
            double r = q[i] / p[i];
            if (p[i] < 0.0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
        }
        return t0 <= t1;
    }

    // The cross-unfold: world point on a face -> flat 2D sheet point (z = 0). PLAN sits in
    // the middle; each elevation folds outward so its shared TOP edge (world Z = max.Z, the
    // roofline) stays adjacent to the plan. BOTTOM (optional) unfolds past EAST.
    private readonly struct FaceFrame
    {
        private readonly Point3d _min;
        private readonly double _L, _W, _H, _gap;

        public FaceFrame(Point3d min, Point3d max, double l, double w, double h, double gap)
        {
            _min = min; _L = l; _W = w; _H = h; _gap = gap;
        }

        public Point3d MapToFace(Face face, Point3d worldPt)
        {
            double xb = worldPt.X - _min.X, yb = worldPt.Y - _min.Y, zb = worldPt.Z - _min.Z;
            switch (face)
            {
                case Face.Plan: return new Point3d(xb, yb, 0.0);
                case Face.North: return new Point3d(xb, _W + _gap + (_H - zb), 0.0);
                case Face.South: return new Point3d(xb, -_gap - (_H - zb), 0.0);
                case Face.West: return new Point3d(-_gap - (_H - zb), yb, 0.0);
                case Face.East: return new Point3d(_L + _gap + (_H - zb), yb, 0.0);
                case Face.Bottom: return new Point3d(_L + _H + 2.0 * _gap + (_L - xb), yb, 0.0);
                default: return new Point3d(xb, yb, 0.0);
            }
        }
    }
}
