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
    // MeshPlanarPolygonExtractorComponent — extract a planar polygon (with
    // holes) from a mesh's boundary loops. Use to feed mesh-based geometry
    // (e.g. BFF-flattened patches) into the 2D packers without going
    // through Rhino's lossy curve extraction.
    //
    // ComponentGuid: F2D000B2-CADC-4F2D-A0B2-7E60CADA15A0
    // (was CDEF0123-4567-89AB-CDEF-0123456789AB; collided with MeshDiagnosticsComponent)
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Mesh Planar Polygon Extractor.
    /// Walks mesh boundary edges, builds closed loops, projects onto the
    /// supplied plane. Largest-area loop is the outer; the rest are
    /// holes.
    /// </summary>
        [Algorithm("Boundary-loop extraction + signed-area outer/hole classification",
        "Frahan-original",
        Note = "Boundary-edge walk + signed-area test; standard building blocks, no single canonical source")]
        [DesignApplication(
        "Extracts the outer + hole loops from a mesh's boundary  and projects them into a 2D plane",
        DesignFlow.Bridges,
        Precedent = "Frahan-original planar-polygon extractor")]
    public sealed class MeshPlanarPolygonExtractorComponent : GH_Component
    {
        public MeshPlanarPolygonExtractorComponent()
            : base(
                "Mesh Planar Polygon Extractor", "MeshPlnPoly",
                "Extracts the outer + hole loops from a mesh's boundary " +
                "and projects them into a 2D plane. Frahan-original method.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F2D000B2-CADC-4F2D-A0B2-7E60CADA15A0");

        protected override Bitmap Icon => IconProvider.Load("MortarJoint.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Mesh whose boundary loops are extracted.",
                GH_ParamAccess.item);
            p.AddPlaneParameter("Plane", "Pl",
                "Projection plane (origin + axes). Default world XY.",
                GH_ParamAccess.item, Plane.WorldXY);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Outer", "O",
                "Closed CCW polyline curve for the outermost loop.",
                GH_ParamAccess.item);
            p.AddCurveParameter("Holes", "H",
                "Closed CW polyline curves for each hole (parallel to " +
                "Hole Areas).",
                GH_ParamAccess.list);
            p.AddNumberParameter("Outer Area", "Ao",
                "Signed area of the outer loop in plane units².",
                GH_ParamAccess.item);
            p.AddNumberParameter("Hole Areas", "Ah",
                "Signed area of each hole.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Mesh m = null;
            if (!da.GetData(0, ref m) || m == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh.");
                return;
            }
            Plane plane = Plane.WorldXY;
            da.GetData(1, ref plane);

            var snap = MeshQualityReportComponent.MeshToSnapshot(m);
            var res = MeshPlanarPolygonExtractor.Extract(snap,
                plane.OriginX, plane.OriginY, plane.OriginZ,
                plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z,
                plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z);

            if (!res.HasOuter)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No outer boundary loop found. Mesh may be closed " +
                    "(no boundary edges) or non-manifold.");
                return;
            }

            var outerCurve = ToPolylineCurve(res.Outer, plane);
            da.SetData(0, outerCurve);

            var holeCurves = new List<Curve>(res.Holes.Count);
            var holeAreas = new List<double>(res.Holes.Count);
            for (int i = 0; i < res.Holes.Count; i++)
            {
                holeCurves.Add(ToPolylineCurve(res.Holes[i], plane));
                holeAreas.Add(RobustPolygon2D.SignedArea(res.Holes[i]));
            }
            da.SetDataList(1, holeCurves);
            da.SetData(2, RobustPolygon2D.SignedArea(res.Outer));
            da.SetDataList(3, holeAreas);
        }

        private static Curve ToPolylineCurve(
            List<(double X, double Y)> poly, Plane plane)
        {
            var pts = new List<Point3d>(poly.Count + 1);
            for (int i = 0; i < poly.Count; i++)
                pts.Add(plane.PointAt(poly[i].X, poly[i].Y, 0));
            if (pts.Count >= 3) pts.Add(pts[0]);
            return new Polyline(pts).ToPolylineCurve();
        }
    }
}
