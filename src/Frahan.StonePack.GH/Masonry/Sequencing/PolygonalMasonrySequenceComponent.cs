#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Sequencing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Masonry.Sequencing
{
    // =========================================================================
    // PolygonalMasonrySequenceComponent — Kim 2024 (DETC2024-142563) polygonal-
    // masonry installation order. Take chains + wall rectangle on the canvas,
    // compute the planar arrangement, build the install DAG (rules 5-8), and
    // run the reversed Kahn's depth search from Code 1 of the paper.
    //
    // Inputs are unprojected to the world XY plane (z is ignored). Chains
    // must each be a function y = f(x) on their domain, OR a purely vertical
    // connector (x constant, y monotone). Chains share endpoints at meetings
    // (paper sec. 5.2 Fig. 11); proper crossings between chains are not
    // supported. Holes can be marked by passing a list of XY probe points
    // that fall inside the regions to remove (sec. 5.4).
    //
    // ComponentGuid: B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Polygonal Masonry Sequence.
    /// Computes the vertical install order for a polygonal stone wall from
    /// Kim 2024 (Finding Installation Sequence of Polygonal Masonry through
    /// Design and Depth Search of a Directed Acyclic Graph).
    /// </summary>
    [Algorithm("Polygonal masonry install sequence", "Kim 2024 ASME DETC2024-142563 Finding Installation Sequence of Polygonal Masonry through Design and Depth Search of a DAG", Note = "Rules 5-8 + reversed Kahn depth search; Code 1 of the paper")]
    [Algorithm("Planar straight-line graph (PSLG) arrangement", "Kim 2024 section 5.2 chain meetings", Note = "Function-y=f(x) chain assumption")]
        [DesignApplication(
        "Installation-order DAG for a polygonal-masonry wall  (Kim 2024)",
        DesignFlow.BottomUp,
        Precedent = "Kim 2024 polygonal masonry install order DAG")]
    public sealed class PolygonalMasonrySequenceComponent : GH_Component
    {
        public PolygonalMasonrySequenceComponent()
            : base(
                "Polygonal Masonry Sequence", "PolyMasonrySeq",
                "Installation-order DAG for a polygonal-masonry wall " +
                "(Kim 2024). Inputs are 2D chains and a wall rectangle. " +
                "Each chain must be monotone in x or a vertical connector. " +
                "Output is one closed polyline per stone, parallel install " +
                "order, depth from Code 1, and DAG edge line segments.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("B4E07A3C-7F4D-4E5B-9C71-0EAF21C9B6A1");

        protected override Bitmap Icon => IconProvider.Load("CourseGenerator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Chains", "C",
                "Polylines or curves defining the wall partition. Each must " +
                "be monotone in x or a purely vertical connector. Chains " +
                "may share endpoints at meetings; they must not cross.",
                GH_ParamAccess.list);
            p.AddRectangleParameter("Wall", "W",
                "Axis-aligned wall rectangle. Defines the bbox and the " +
                "wall boundary (paper sec. 5.3).",
                GH_ParamAccess.item);
            p.AddPointParameter("Hole Probes", "H",
                "Optional. Each probe point marks the region containing it " +
                "as a hole; that region is removed before depth search " +
                "(paper sec. 5.4).",
                GH_ParamAccess.list);
            p[2].Optional = true;
            p.AddNumberParameter("Epsilon", "e",
                "Tolerance for vertex deduplication and predicates.",
                GH_ParamAccess.item, 1e-7);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Stones", "S",
                "One closed polyline per stone region (finite, non-hole). " +
                "Includes the two infinite top/bottom bands.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Install Order", "i",
                "1-based install index per stone, parallel to Stones. " +
                "1 = installed first (bottom), max = installed last.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Depth", "d",
                "Reversed-Kahn depth per stone. Higher = installed earlier. " +
                "Sinks (last-installed) have depth 0.",
                GH_ParamAccess.list);
            p.AddLineParameter("DAG Edges", "E",
                "One line segment per DAG edge from lower-order centroid to " +
                "higher-order centroid. Visualises the install constraint " +
                "graph (paper Figs. 5, 13, 14).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Region Count", "n",
                "Number of finite stone regions (excludes the bbox " +
                "surroundings and any hole-marked regions).",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var curves = new List<Curve>();
            if (!da.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No chains provided.");
                return;
            }

            Rectangle3d wallRect = default;
            if (!da.GetData(1, ref wallRect))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No wall rectangle provided.");
                return;
            }

            var holeProbes = new List<Point3d>();
            da.GetDataList(2, holeProbes);

            double eps = 1e-7;
            da.GetData(3, ref eps);
            if (eps <= 0.0 || eps > 1.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Epsilon must be in (0, 1], got {eps}.");
                return;
            }

            var bbox = ExtractBbox(wallRect);

            var chains = new List<IReadOnlyList<(double X, double Y)>>(curves.Count);
            for (int i = 0; i < curves.Count; i++)
            {
                var pts = TryCurveToXyChain(curves[i], eps);
                if (pts == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Chain {i}: could not extract a polyline; skipped.");
                    continue;
                }
                if (pts.Count < 2)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Chain {i}: fewer than 2 points; skipped.");
                    continue;
                }
                chains.Add(pts);
            }

            if (chains.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No usable chains after extraction.");
                return;
            }

            Wall wall;
            try
            {
                wall = Wall.FromChains(chains, bbox, true, eps);
            }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            if (holeProbes.Count > 0)
            {
                var holeIds = ResolveHoleProbes(wall, holeProbes);
                wall.RemoveRegions(holeIds);
            }

            InstallationPlan plan;
            try
            {
                plan = wall.InstallSequence();
            }
            catch (InvalidOperationException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var stones = new List<Curve>();
            var orderList = new List<int>();
            var depthList = new List<int>();
            int regionCount = 0;

            foreach (var r in wall.Regions)
            {
                if (r.IsOuter) continue;
                if (wall.Holes.Contains(r.Id)) continue;
                if (!plan.Order.TryGetValue(r.Id, out int orderIdx)) continue;
                var poly = RingToPolyline(r.Ring);
                if (poly == null) continue;
                stones.Add(poly);
                orderList.Add(orderIdx);
                depthList.Add(plan.Depth[r.Id]);
                regionCount++;
            }

            var edges = new List<Line>();
            foreach (var (low, high) in plan.DagEdges)
            {
                if (wall.Holes.Contains(low) || wall.Holes.Contains(high)) continue;
                var a = Geom2D.RingCentroid(wall.Regions[low].Ring);
                var b = Geom2D.RingCentroid(wall.Regions[high].Ring);
                edges.Add(new Line(new Point3d(a.X, a.Y, 0.0),
                                     new Point3d(b.X, b.Y, 0.0)));
            }

            da.SetDataList(0, stones);
            da.SetDataList(1, orderList);
            da.SetDataList(2, depthList);
            da.SetDataList(3, edges);
            da.SetData(4, regionCount);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static (double XMin, double YMin, double XMax, double YMax)
            ExtractBbox(Rectangle3d rect)
        {
            double x0 = rect.X.Min, x1 = rect.X.Max;
            double y0 = rect.Y.Min, y1 = rect.Y.Max;
            // World-XY mapping. Rectangle3d may live on a non-world plane;
            // we map its four corners and take the AABB in world XY.
            var p0 = rect.PointAt(0);
            var p1 = rect.PointAt(1);
            var p2 = rect.PointAt(2);
            var p3 = rect.PointAt(3);
            double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
            double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
            double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
            double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
            return (minX, minY, maxX, maxY);
        }

        private static List<(double X, double Y)> TryCurveToXyChain(
            Curve c, double eps)
        {
            if (c == null) return null;
            if (c is PolylineCurve plc)
            {
                var poly = plc.ToPolyline();
                return PolylineToXy(poly);
            }
            if (c.TryGetPolyline(out Polyline poly2))
            {
                return PolylineToXy(poly2);
            }
            // Fallback: sample non-polyline curves.
            var t0 = c.Domain.T0;
            var t1 = c.Domain.T1;
            int samples = Math.Max(16, (int)Math.Ceiling(c.GetLength() / Math.Max(eps, 0.01)));
            samples = Math.Min(samples, 512);
            var pts = new List<(double X, double Y)>(samples + 1);
            for (int i = 0; i <= samples; i++)
            {
                double t = t0 + (t1 - t0) * (i / (double)samples);
                var p = c.PointAt(t);
                if (pts.Count == 0 ||
                    (Math.Abs(pts[pts.Count - 1].X - p.X) > eps ||
                     Math.Abs(pts[pts.Count - 1].Y - p.Y) > eps))
                {
                    pts.Add((p.X, p.Y));
                }
            }
            return pts;
        }

        private static List<(double X, double Y)> PolylineToXy(Polyline poly)
        {
            var pts = new List<(double X, double Y)>(poly.Count);
            for (int i = 0; i < poly.Count; i++)
            {
                pts.Add((poly[i].X, poly[i].Y));
            }
            // Drop duplicate last vertex of closed polylines.
            if (pts.Count >= 2)
            {
                var first = pts[0];
                var last = pts[pts.Count - 1];
                if (Math.Abs(first.X - last.X) < 1e-12 &&
                    Math.Abs(first.Y - last.Y) < 1e-12)
                {
                    pts.RemoveAt(pts.Count - 1);
                }
            }
            return pts;
        }

        private static PolylineCurve RingToPolyline(
            IReadOnlyList<(double X, double Y)> ring)
        {
            if (ring == null || ring.Count < 3) return null;
            var pts = new Point3d[ring.Count + 1];
            for (int i = 0; i < ring.Count; i++)
            {
                pts[i] = new Point3d(ring[i].X, ring[i].Y, 0.0);
            }
            pts[ring.Count] = pts[0];
            return new PolylineCurve(pts);
        }

        private static List<int> ResolveHoleProbes(Wall wall, List<Point3d> probes)
        {
            var holeIds = new List<int>();
            foreach (var p in probes)
            {
                var probe = (p.X, p.Y);
                foreach (var r in wall.Regions)
                {
                    if (!r.IsFinite || r.IsOuter) continue;
                    if (Geom2D.PointInRing(probe, r.Ring))
                    {
                        holeIds.Add(r.Id);
                        break;
                    }
                }
            }
            return holeIds;
        }
    }
}
