#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Geometry;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // PolygonSanitizeComponent — drop duplicate / collinear vertices and
    // sliver edges from a closed polyline. Use as a pre-processing step
    // before packing or cutting algorithms that are sensitive to slivers.
    //
    // ComponentGuid: F2D000B1-CADC-4F2D-A0B1-7E60CADA15A0
    // (was BCDEF012-3456-789A-BCDE-F0123456789A; collided with MeshPcaComponent)
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Polygon Sanitize.
    /// Robust 2D polygon sanitiser: dedup adjacent verts, drop collinear-
    /// chain verts, sliver-aware. Pure-managed.
    /// </summary>
        [Algorithm("Polygon vertex cleanup (dedup + collinearity drop)",
        "Frahan-original",
        Note = "Generic per-vertex triangle-area cleanup; textbook geometry utility, no single canonical source")]
        [DesignApplication(
        "Drops duplicate / collinear vertices and sliver edges  from a closed polyline",
        DesignFlow.Bridges,
        Precedent = "Frahan-original polygon-cleanup utility")]
    public sealed class PolygonSanitizeComponent : GH_Component
    {
        public PolygonSanitizeComponent()
            : base(
                "Polygon Sanitize", "PolySan",
                "Drops duplicate / collinear vertices and sliver edges " +
                "from a closed polyline. Operates in 2D — points are " +
                "projected onto the supplied plane (default world XY). " +
                "Frahan-original method.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F2D000B1-CADC-4F2D-A0B1-7E60CADA15A0");

        protected override Bitmap Icon => IconProvider.Load("PolygonSimplify.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Polyline", "P",
                "Closed polyline curve.",
                GH_ParamAccess.item);
            p.AddPlaneParameter("Plane", "Pl",
                "2D projection plane. Default world XY.",
                GH_ParamAccess.item, Plane.WorldXY);
            p[1].Optional = true;
            p.AddNumberParameter("Dedup Tolerance", "Td",
                "Adjacent-vertex dedup tolerance. Default 1e-6.",
                GH_ParamAccess.item, 1e-6);
            p[2].Optional = true;
            p.AddNumberParameter("Collinear Tolerance", "Tc",
                "Triangle area threshold for collinear-chain dropping. " +
                "Default 1e-6.",
                GH_ParamAccess.item, 1e-6);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Sanitized", "S",
                "Cleaned closed polyline.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Verts Dropped", "Vd",
                "Number of vertices removed.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Area", "A",
                "Signed area of the sanitized polygon (in plane units²).",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Curve crv = null;
            if (!da.GetData(0, ref crv) || crv == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No polyline.");
                return;
            }
            Plane plane = Plane.WorldXY;
            da.GetData(1, ref plane);
            double dedupTol = 1e-6, collinearTol = 1e-6;
            da.GetData(2, ref dedupTol);
            da.GetData(3, ref collinearTol);

            if (!crv.TryGetPolyline(out Polyline pl))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Curve is not a polyline.");
                return;
            }

            var pts2d = new List<(double X, double Y)>(pl.Count);
            for (int i = 0; i < pl.Count; i++)
            {
                if (i == pl.Count - 1 && pl[i].EpsilonEquals(pl[0], 1e-12)) break;
                plane.RemapToPlaneSpace(pl[i], out Point3d pp);
                pts2d.Add((pp.X, pp.Y));
            }

            int before = pts2d.Count;
            var clean = RobustPolygon2D.Sanitize(pts2d, dedupTol, collinearTol);
            int dropped = before - clean.Count;
            double area = RobustPolygon2D.SignedArea(clean);

            // Lift cleaned 2D back to 3D using the plane.
            var pts3d = new List<Point3d>(clean.Count + 1);
            for (int i = 0; i < clean.Count; i++)
            {
                var lp = new Point3d(clean[i].X, clean[i].Y, 0);
                pts3d.Add(plane.PointAt(lp.X, lp.Y, lp.Z));
            }
            if (pts3d.Count >= 3) pts3d.Add(pts3d[0]); // close
            var outPl = new Polyline(pts3d);
            da.SetData(0, outPl.ToPolylineCurve());
            da.SetData(1, dropped);
            da.SetData(2, area);
        }
    }
}
