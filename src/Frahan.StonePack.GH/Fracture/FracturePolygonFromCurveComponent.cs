#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // FracturePolygonFromCurveComponent — wraps a closed planar Rhino
    // polyline / PolylineCurve into a Frahan.Masonry.Cutting.FracturePolygon
    // DTO (Phase E.2 finite-extent fracture surface).
    //
    // Design:
    //   - Accepts any Curve input. Tries Curve.TryGetPolyline first; the curve
    //     must produce a closed polyline with at least 4 points (the closing
    //     duplicate is dropped before the FracturePolygon is built).
    //   - Verifies planarity by asking Rhino for a plane via the curve.
    //   - The FracturePolygon constructor itself enforces convexity,
    //     coplanarity, and minimum vertex count. Construction errors are
    //     surfaced as runtime-message errors with the inner exception text so
    //     the GH user sees the precise reason (non-convex, collinear, etc.).
    //
    // ComponentGuid: D3C4E5F6-7B8A-49AC-BD2E-3F4A5B6C7D8E
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Fracture Polygon From Curve.
    /// Wraps a closed planar polyline into a <see cref="FracturePolygon"/>
    /// DTO. The polygon must be convex; non-convex inputs surface as runtime
    /// errors raised by the underlying <see cref="FracturePolygon"/> ctor.
    /// </summary>
        [DesignApplication(
        "Wraps a closed planar polyline into a FracturePolygon DTO",
        DesignFlow.TopDown,
        Precedent = "Frahan-original curve-to-fracture-polygon converter")]
    public sealed class FracturePolygonFromCurveComponent : GH_Component
    {
        public FracturePolygonFromCurveComponent()
            : base(
                "Fracture Polygon From Curve", "FracPoly",
                "Wraps a closed planar polyline into a FracturePolygon DTO. " +
                "The curve must convert to a polyline with at least 4 points " +
                "(closing duplicate dropped). The polygon must be convex and " +
                "planar; the FracturePolygon constructor enforces both.",
                "Frahan", "Fracture")
        {
        }

        // GUID literal: D3C4E5F6-7B8A-49AC-BD2E-3F4A5B6C7D8E
        public override Guid ComponentGuid =>
            new Guid("D3C4E5F6-7B8A-49AC-BD2E-3F4A5B6C7D8E");

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Curve", "C",
                "Closed polyline (or PolylineCurve) describing a finite convex " +
                "fracture polygon. The curve must yield a polyline with at least " +
                "4 points and be closed. Planarity required unless ForceProject " +
                "is true.",
                GH_ParamAccess.item);
            p.AddBooleanParameter("ForceProject", "FP",
                "When true, near-planar curves are projected onto their best-fit " +
                "plane before constructing the polygon. Default false (strict).",
                GH_ParamAccess.item, false);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("FracturePolygon", "F",
                "FracturePolygon DTO. Wire into Slab Cut By Fracture Polygons.",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Curve curve = null;
            bool forceProject = false;

            if (!da.GetData(0, ref curve) || curve == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No curve provided.");
                return;
            }
            da.GetData(1, ref forceProject);

            Polyline pl;
            if (!curve.TryGetPolyline(out pl) || pl == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Curve is not a polyline (TryGetPolyline returned false).");
                return;
            }

            if (pl.Count < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Polyline must have at least 4 points (closing duplicate " +
                    $"included), got {pl.Count}.");
                return;
            }

            if (!pl.IsClosed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Polyline must be closed.");
                return;
            }

            Plane fitPlane;
            if (!pl.ToPolylineCurve().TryGetPlane(out fitPlane))
            {
                if (!forceProject)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Polyline is not planar. Set ForceProject=true to " +
                        "project onto the best-fit plane.");
                    return;
                }
                // Fit a best plane to the vertex set and project each vertex.
                var pts = new List<Point3d>(pl.Count);
                for (int i = 0; i < pl.Count - 1; i++) pts.Add(pl[i]);
                if (Plane.FitPlaneToPoints(pts, out fitPlane) != PlaneFitResult.Success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "FitPlaneToPoints failed; cannot project to a plane.");
                    return;
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "ForceProject active: vertices projected to best-fit plane.");
            }

            int n = pl.Count - 1;
            var coords = new double[n * 3];
            for (int i = 0; i < n; i++)
            {
                Point3d pt = pl[i];
                if (forceProject) pt = fitPlane.ClosestPoint(pt);
                coords[3 * i + 0] = pt.X;
                coords[3 * i + 1] = pt.Y;
                coords[3 * i + 2] = pt.Z;
            }

            // ---- Construct FracturePolygon (convexity / coplanarity / collinearity check) ----
            FracturePolygon polygon;
            try
            {
                polygon = new FracturePolygon(coords);
            }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"FracturePolygon construction failed: {ex.Message}");
                return;
            }

            da.SetData(0, polygon);
        }
    }
}
