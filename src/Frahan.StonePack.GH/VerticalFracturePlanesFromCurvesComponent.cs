#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// Vertical Fracture Planes From Curves (2026-05-28). The bridge that lets REAL
// mapped fracture traces (plan-view curves, e.g. from Vector Fractures Loader on
// a Loviisa shapefile) drive the slab cutter: each trace -> a VERTICAL fracture
// plane (normal horizontal, perpendicular to the trace; the plane contains the
// trace direction + world Z). Output Planes feed Slab Cut By Fractures.
// =============================================================================

[RelatedComponent("Frahan > Ingest > Vector Fractures Loader", Reason = "Source of the real fracture trace curves (.shp / .geojson).")]
[RelatedComponent("Frahan > Slab > Slab Cut By Fractures", Reason = "Consumes these vertical planes to cut a block into slabs.")]
[Algorithm("Vertical plane per fracture trace",
    "Frahan-original: plan-view trace -> vertical cutting plane (contains the trace direction + Z)",
    Note = "Per-segment option for wiggly traces; default = one best-fit vertical plane per curve.")]
[DesignApplication(
    "Turn plan-view fracture trace curves (e.g",
    DesignFlow.TopDown,
    Precedent = "Frahan-original vertical-curve-to-fracture-plane sweep")]
public sealed class VerticalFracturePlanesFromCurvesComponent : FrahanComponentBase
{
    public VerticalFracturePlanesFromCurvesComponent()
        : base("Vertical Fracture Planes From Curves", "FracPlanes",
            "Turn plan-view fracture trace curves (e.g. from Vector Fractures " +
            "Loader on a real fracture shapefile) into VERTICAL cutting planes " +
            "for Slab Cut By Fractures. Per Segment = a plane per polyline segment " +
            "(faithful to wiggly traces); off = one best-fit vertical plane per curve.",
            "Frahan", "Slab")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A09-1A2B-4C3D-9E4F-5A6B7C8D9E09");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("PoissonReconstruct.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Curves", "T", "Fracture trace curves (plan-view).", GH_ParamAccess.list);
        p.AddBooleanParameter("Per Segment", "Sg",
            "TRUE = one vertical plane per polyline segment (faithful to curved traces); " +
            "FALSE (default) = one best-fit vertical plane per curve (start->end direction).",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P", "Vertical fracture planes (feed Slab Cut By Fractures 'Plane').", GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N", "Number of planes produced.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var curves = new List<Curve>();
        bool perSeg = false;
        if (!da.GetDataList(0, curves) || curves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No curves provided.");
            return;
        }
        da.GetData(1, ref perSeg);

        var planes = new List<Plane>();
        foreach (var c in curves)
        {
            if (c == null) continue;
            if (perSeg && c.TryGetPolyline(out Polyline pl) && pl.Count >= 2)
            {
                for (int i = 0; i + 1 < pl.Count; i++)
                {
                    var pp = VerticalPlane(pl[i], pl[i + 1]);
                    if (pp.HasValue) planes.Add(pp.Value);
                }
            }
            else
            {
                var pp = VerticalPlane(c.PointAtStart, c.PointAtEnd);
                if (pp.HasValue) planes.Add(pp.Value);
                else
                {
                    // degenerate start==end (closed/tiny): use bbox diagonal direction.
                    var bb = c.GetBoundingBox(true);
                    if (bb.IsValid)
                    {
                        var pp2 = VerticalPlane(bb.Min, new Point3d(bb.Max.X, bb.Max.Y, bb.Min.Z));
                        if (pp2.HasValue) planes.Add(pp2.Value);
                    }
                }
            }
        }

        da.SetDataList(0, planes);
        da.SetData(1, planes.Count);
        if (planes.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid planes (traces too short / vertical).");
    }

    // Vertical plane through two plan points: origin = midpoint, X-axis = the
    // horizontal trace direction, Y-axis = world Z. (Normal is horizontal,
    // perpendicular to the trace -> a vertical cutting plane.)
    private static Plane? VerticalPlane(Point3d a, Point3d b)
    {
        var dir = new Vector3d(b.X - a.X, b.Y - a.Y, 0.0);   // project to XY
        if (dir.Length < 1e-9) return null;
        dir.Unitize();
        var origin = new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        return new Plane(origin, dir, Vector3d.ZAxis);
    }
}
