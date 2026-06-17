#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry;

// =============================================================================
// Panel Tile Surface -- discretize a freeform facade surface into PLANAR stone-
// cladding panels. Stone cannot bend, so a curved surface is faceted: the surface
// is divided U x V and each quad is projected to its best-fit plane. The flat
// outline of each panel is the CUT TILE, laid in World XY ready to nest on slabs
// with Sheet Nest (Hole-Aware). Per-panel planarity (corner deviation from the
// panel plane) tells you where the facets are too coarse for the curvature.
//
// Frahan > Surface Packing > Panel Tile Surface. The curved-cladding counterpart
// to the uniform-grid tiling in example 37; the engine behind example 38.
// =============================================================================

/// <summary>
/// Frahan &gt; Surface Packing &gt; Panel Tile Surface. Discretize a surface into
/// planar cladding panels + their flat cut outlines (nestable on slabs).
/// </summary>
[RelatedComponent("Frahan > 2D Packing > Sheet Nest (Hole-Aware)", Reason = "Nest the flat Cut Tiles onto slab sheets to cut them from stock.")]
[RelatedComponent("Frahan > Quarry > Fracture Bounded Slabs", Reason = "Produces the slabs the cut tiles are nested onto.")]
[Algorithm("Planar-panel discretization of a surface for stone cladding (planarized U x V quads + flat cut outlines)",
    "Each UV quad is projected to its best-fit plane (Plane.FitPlaneToPoints); the flat outline mapped to World XY is the cut tile.",
    Note = "Planarity output = max corner deviation from the panel plane; raise U/V where it is too high for the stone thickness.")]
public sealed class PanelTileSurfaceComponent : FrahanComponentBase
{
    public PanelTileSurfaceComponent()
        : base("Panel Tile Surface", "PanelTile",
            "Discretize a freeform facade surface into PLANAR stone-cladding panels: divide the surface " +
            "U x V, project each quad to its best-fit plane (stone cannot bend), and output BOTH the 3D " +
            "panels on the surface AND their flat cut outlines (laid in World XY) ready to nest on slabs " +
            "with Sheet Nest (Hole-Aware). Reports per-panel planarity (corner deviation from the panel " +
            "plane) and area - raise U/V where planarity is too high for the curvature.",
            "Frahan", "Surface Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("A7E0B0F7-0C0F-4A16-9E3D-0FACE0FACE08");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddSurfaceParameter("Surface", "S",
            "Facade surface to panelize (a single surface; a Brep face is coerced).", GH_ParamAccess.item);
        p.AddIntegerParameter("U Count", "U", "Number of panels across the surface U direction.",
            GH_ParamAccess.item, 12);
        p.AddIntegerParameter("V Count", "V", "Number of panels across the surface V direction.",
            GH_ParamAccess.item, 8);
        p.AddNumberParameter("Joint", "J", "Grout / joint gap: each panel is inset by this toward its " +
            "centre (m). Default 0.005.", GH_ParamAccess.item, 0.005);
        p.AddBooleanParameter("Planarize", "Pl", "Project each quad to its best-fit plane so every panel " +
            "is a FLAT cuttable tile (stone cannot bend). False = leave the warped surface quad. Default true.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Panels", "P", "The 3D (planarized) cladding panels positioned on the surface.",
            GH_ParamAccess.list);
        p.AddCurveParameter("Cut Tiles", "T", "Flat closed outline per panel, mapped to the World XY plane, " +
            "ready to nest on slabs (wire into Sheet Nest (Hole-Aware) > Parts).", GH_ParamAccess.list);
        p.AddNumberParameter("Planarity", "Pl", "Max corner deviation from the panel plane (m) per panel. " +
            "High = the surface is too curved for that panel size; raise U / V.", GH_ParamAccess.list);
        p.AddNumberParameter("Area", "A", "Panel area (m2) per panel.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "R", "Panel count, total area, and worst planarity.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Rhino.Geometry.Surface srf = null;
        if (!da.GetData(0, ref srf) || srf == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No surface provided."); return; }
        int U = 12, V = 8; double joint = 0.005; bool planarize = true;
        da.GetData(1, ref U); da.GetData(2, ref V); da.GetData(3, ref joint); da.GetData(4, ref planarize);
        U = Math.Max(1, U); V = Math.Max(1, V); joint = Math.Max(0.0, joint);

        var du = srf.Domain(0); var dv = srf.Domain(1);
        var P = new Point3d[V + 1, U + 1];
        for (int j = 0; j <= V; j++)
            for (int i = 0; i <= U; i++)
                P[j, i] = srf.PointAt(du.ParameterAt((double)i / U), dv.ParameterAt((double)j / V));

        var panels = new List<Mesh>(); var tiles = new List<Curve>();
        var planr = new List<double>(); var areas = new List<double>();
        double maxPl = 0, totA = 0;

        for (int j = 0; j < V; j++)
            for (int i = 0; i < U; i++)
            {
                var c = new[] { P[j, i], P[j, i + 1], P[j + 1, i + 1], P[j + 1, i] };
                if (!Plane.FitPlaneToPoints(c, out Plane pl).Equals(PlaneFitResult.Success) && !pl.IsValid)
                    pl = new Plane(Centroid(c), Vector3d.ZAxis);
                double dev = c.Max(pt => Math.Abs(pl.DistanceTo(pt)));

                // planarized (or raw) corners, then inset toward the centroid by the joint gap
                var pc = (planarize ? c.Select(pt => pl.ClosestPoint(pt)) : c).ToArray();
                var cen = Centroid(pc);
                for (int k = 0; k < 4; k++)
                {
                    var dir = cen - pc[k]; double len = dir.Length;
                    if (len > 1e-9) pc[k] += dir / len * Math.Min(joint, len * 0.49);
                }

                var m = new Mesh();
                foreach (var pt in pc) m.Vertices.Add(pt);
                m.Faces.AddFace(0, 1, 2, 3);
                m.Normals.ComputeNormals(); m.UnifyNormals();
                panels.Add(m);

                // flat cut tile: map the planar quad to World XY (closed polyline)
                var to2d = Transform.PlaneToPlane(pl, Plane.WorldXY);
                var poly = new Polyline(pc.Concat(new[] { pc[0] }));
                poly.Transform(to2d);
                tiles.Add(poly.ToPolylineCurve());

                double a = AreaMassProperties.Compute(m)?.Area ?? 0.0;
                areas.Add(a); planr.Add(dev); totA += a; if (dev > maxPl) maxPl = dev;
            }

        da.SetDataList(0, panels);
        da.SetDataList(1, tiles);
        da.SetDataList(2, planr);
        da.SetDataList(3, areas);
        da.SetData(4, $"Panel Tile Surface: {panels.Count} panels ({U} x {V}), total {totA:0.0} m2, " +
            $"worst planarity {maxPl * 1000:0.#} mm (flat-cut deviation). " +
            (maxPl > 0.01 ? "Raise U/V to reduce facet deviation." : "Facets within ~10 mm."));
        if (maxPl > 0.02)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Worst panel deviates {maxPl * 1000:0} mm from flat; raise U/V for a tighter facet on this curvature.");
    }

    private static Point3d Centroid(Point3d[] q)
        => new Point3d((q[0].X + q[1].X + q[2].X + q[3].X) / 4.0,
                       (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4.0,
                       (q[0].Z + q[1].Z + q[2].Z + q[3].Z) / 4.0);
}
