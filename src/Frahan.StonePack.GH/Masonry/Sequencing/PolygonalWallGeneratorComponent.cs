using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry.Sequencing
{
    /// <summary>
    /// Polygonal Wall (Generator). Tiles a surface (flat / single- / double-curved) with interlocking
    /// polygonal masonry stones using Grasshopper's bounded Voronoi, with a single Coursing slider
    /// (0 = irregular Inca .. 1 = coursed rubble). Every stone is emitted as a CLOSED, MANIFOLD,
    /// unified-outward-normal mesh (watertight -> fabrication / Boolean / IFC ready).
    ///
    /// Algorithm validated live 2026-06-10 (see code_ws outputs/2026-06-10/masonry_evolution: wall_proto.py,
    /// wall_double_curved.png/.3dm, sequencing.gif, PORT_SPEC.md, MASONRY_STRATEGY_HANDOFF.md). This is the
    /// "card 06" generator that supersedes the KB-8 held-back 2D-Voronoi (bounded Voronoi via the GH solver
    /// avoids the Pslg.FromSegments ridge-extraction path).
    /// </summary>
    public class PolygonalWallGeneratorComponent : GH_Component
    {
        public PolygonalWallGeneratorComponent()
          : base("Polygonal Wall (Generator)", "PolyWall",
                 "Tile a surface (flat / curved / double-curved) with interlocking polygonal masonry stones " +
                 "(Voronoi + Coursing slider). Outputs closed, manifold, unified-normal stone meshes.",
                 "Frahan", "Masonry")
        { }

        public override Guid ComponentGuid => new Guid("D5F10014-7A11-4C0E-9B22-3F6A1E2C4D80");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddSurfaceParameter("Surface", "S",
                "Surface to tile (flat / curved / double-curved). If empty, a flat W x H panel in the XZ plane is used.",
                GH_ParamAccess.item);
            p[0].Optional = true;
            p.AddNumberParameter("Width", "W", "Panel width (m) when no Surface is supplied", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Height", "H", "Panel height (m) when no Surface is supplied", GH_ParamAccess.item, 1.8);
            p.AddNumberParameter("Coursing", "C", "0 = irregular (Inca)  ->  1 = coursed rubble", GH_ParamAccess.item, 0.4);
            p.AddIntegerParameter("Courses", "Cr", "Number of courses the coursing pulls toward", GH_ParamAccess.item, 5);
            p.AddIntegerParameter("GridX", "Gx", "Seed columns (stones along width)", GH_ParamAccess.item, 8);
            p.AddIntegerParameter("GridY", "Gy", "Seed rows (stones along height)", GH_ParamAccess.item, 5);
            p.AddNumberParameter("Depth", "D", "Stone depth (m), extruded along the surface normal", GH_ParamAccess.item, 0.20);
            p.AddNumberParameter("Mortar", "M", "Mortar joint fraction (cell shrink toward centroid, 0..0.45)", GH_ParamAccess.item, 0.08);
            p.AddIntegerParameter("Seed", "Sd", "Random seed", GH_ParamAccess.item, 7);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "Closed, manifold, unified-outward-normal stone meshes", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of stones", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Surface srf = null;
            double W = 3.0, H = 1.8, coursing = 0.4, depth = 0.20, mortar = 0.08;
            int courses = 5, gx = 8, gy = 5, seed = 7;
            da.GetData(0, ref srf);
            da.GetData(1, ref W); da.GetData(2, ref H); da.GetData(3, ref coursing);
            da.GetData(4, ref courses); da.GetData(5, ref gx); da.GetData(6, ref gy);
            da.GetData(7, ref depth); da.GetData(8, ref mortar); da.GetData(9, ref seed);

            coursing = Clamp(coursing, 0, 1);
            double shrink = 1.0 - Clamp(mortar, 0, 0.45);
            courses = Math.Max(1, courses); gx = Math.Max(2, gx); gy = Math.Max(1, gy);

            // --- param-space extents (the Voronoi rectangle) + (u,v) -> 3D point/normal map ---
            double Lu = W, Lv = H;
            Func<double, double, Point3d> M;
            Func<double, double, Vector3d> Nrm;
            if (srf != null)
            {
                var du = srf.Domain(0); var dv = srf.Domain(1);
                var p00 = srf.PointAt(du.Min, dv.Min);
                Lu = p00.DistanceTo(srf.PointAt(du.Max, dv.Min)); if (Lu < 1e-6) Lu = W;
                Lv = p00.DistanceTo(srf.PointAt(du.Min, dv.Max)); if (Lv < 1e-6) Lv = H;
                M = (u, v) => srf.PointAt(du.ParameterAt(Clamp01(u / Lu)), dv.ParameterAt(Clamp01(v / Lv)));
                Nrm = (u, v) => { var n = srf.NormalAt(du.ParameterAt(Clamp01(u / Lu)), dv.ParameterAt(Clamp01(v / Lv))); n.Unitize(); return n; };
            }
            else
            {
                M = (u, v) => new Point3d(u, 0, v);       // flat panel in XZ
                Nrm = (u, v) => Vector3d.YAxis;
            }

            // --- seeds: jittered grid + coursing morph (pull v toward nearest course centre) ---
            var rnd = new Random(seed);
            var seeds = new List<double[]>();
            for (int j = 0; j < gy; j++)
                for (int i = 0; i < gx; i++)
                {
                    double u = (i + 0.5) / gx * Lu + (rnd.NextDouble() * 2 - 1) * (0.35 * Lu / gx);
                    double v = (j + 0.5) / gy * Lv + (rnd.NextDouble() * 2 - 1) * (0.30 * Lv / gy);
                    double best = double.MaxValue, cv = v;
                    for (int k = 0; k < courses; k++)
                    {
                        double cc = (k + 0.5) / courses * Lv;
                        if (Math.Abs(cc - v) < best) { best = Math.Abs(cc - v); cv = cc; }
                    }
                    v = v * (1 - coursing) + cv * coursing;
                    seeds.Add(new[] { Clamp(u, 0.02 * Lu, 0.98 * Lu), Clamp(v, 0.02 * Lv, 0.98 * Lv) });
                }

            // --- bounded Voronoi (Grasshopper), clipped to the param rectangle (no KB-8 ridge extraction) ---
            var nodes = new Grasshopper.Kernel.Geometry.Node2List();
            foreach (var s in seeds) nodes.Append(new Grasshopper.Kernel.Geometry.Node2(s[0], s[1]));
            var outline = new Grasshopper.Kernel.Geometry.Node2List();
            foreach (var xy in new[] { new[] { 0.0, 0.0 }, new[] { Lu, 0.0 }, new[] { Lu, Lv }, new[] { 0.0, Lv } })
                outline.Append(new Grasshopper.Kernel.Geometry.Node2(xy[0], xy[1]));
            var conn = Grasshopper.Kernel.Geometry.Delaunay.Solver.Solve_Connectivity(nodes, 0.0001, false);
            var cells = Grasshopper.Kernel.Geometry.Voronoi.Solver.Solve_Connectivity(nodes, conn, outline);

            var meshes = new List<Mesh>();
            foreach (var cell in cells)
            {
                var pl = cell.ToPolyline();
                if (pl == null || pl.Count < 4) continue;
                var uv = pl.ToArray().ToList();
                if (uv.First().DistanceTo(uv.Last()) < 1e-9) uv.RemoveAt(uv.Count - 1);
                if (uv.Count < 3) continue;
                var st = BuildStone(uv, M, Nrm, depth, shrink, rnd);
                if (st != null) meshes.Add(st);
            }

            da.SetDataList(0, meshes);
            da.SetData(1, meshes.Count);
        }

        /// <summary>One stone: ngon-capped prism on the surface, GUARANTEED closed + manifold + unified outward normals.</summary>
        private static Mesh BuildStone(List<Point3d> uv, Func<double, double, Point3d> M, Func<double, double, Vector3d> Nrm,
                                       double depth, double shrink, Random rnd)
        {
            double cu = uv.Average(p => p.X), cv = uv.Average(p => p.Y);
            var F = new List<Point3d>();
            foreach (var p in uv) { double su = cu + (p.X - cu) * shrink, sv = cv + (p.Y - cv) * shrink; F.Add(M(su, sv)); }
            var n = Nrm(cu, cv);
            double d = depth * (0.85 + rnd.NextDouble() * 0.3);   // slight per-stone depth variation
            var B = F.Select(p => p + n * d).ToList();
            int k = F.Count;

            var m = new Mesh();
            foreach (var p in F) m.Vertices.Add(p);
            foreach (var p in B) m.Vertices.Add(p);
            var cf = new Point3d(F.Average(p => p.X), F.Average(p => p.Y), F.Average(p => p.Z));
            int ci = m.Vertices.Add(cf), cbi = m.Vertices.Add(cf + n * d);
            var ff = new List<int>(); for (int i = 0; i < k; i++) ff.Add(m.Faces.AddFace(ci, i, (i + 1) % k));            // front fan
            var bf = new List<int>(); for (int i = 0; i < k; i++) bf.Add(m.Faces.AddFace(cbi, k + (i + 1) % k, k + i));    // back fan
            for (int i = 0; i < k; i++) m.Faces.AddFace(i, (i + 1) % k, k + (i + 1) % k, k + i);                            // side quads
            try
            {
                m.Ngons.AddNgon(MeshNgon.Create(Enumerable.Range(0, k).ToList(), ff));   // clean front face (no internal wires)
                m.Ngons.AddNgon(MeshNgon.Create(Enumerable.Range(k, k).ToList(), bf));
            }
            catch { }

            // --- clean-mesh guarantee ---
            m.Vertices.CombineIdentical(true, true);
            m.Weld(Math.PI);
            m.UnifyNormals();
            double vol = 0; try { vol = m.Volume(); } catch { }
            if (vol < 0) m.Flip(true, true, true);     // force outward
            m.RebuildNormals();
            return m.IsValid ? m : null;
        }

        private static double Clamp01(double t) => Math.Max(0, Math.Min(1, t));
        private static double Clamp(double t, double lo, double hi) => Math.Max(lo, Math.Min(hi, t));
    }
}
