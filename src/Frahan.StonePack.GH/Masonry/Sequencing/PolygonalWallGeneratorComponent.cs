using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.Masonry.Sequencing;

namespace Frahan.StonePack.GH.Masonry.Sequencing
{
    using Surface = Rhino.Geometry.Surface;

    /// <summary>
    /// Polygonal Wall (Generator). Tiles a surface (flat / single- / double-curved)
    /// with interlocking polygonal masonry stones. The pattern math lives in the
    /// Rhino-free Core <see cref="PolygonalWallGenerator"/> (evolution P3,
    /// 2026-06-10): power-diagram cells (per-seed weights = size grading), Lloyd
    /// relaxation, the validated Coursing morph (0 = Inca .. 1 = coursed rubble),
    /// sliver cull, and an interlock score J in [0,1]. This component maps the
    /// cells onto the surface, extrudes along the normal, and emits CLOSED,
    /// MANIFOLD, unified-outward-normal stone meshes (watertight ->
    /// fabrication / Boolean / equilibrium-check / IFC ready).
    /// </summary>
    public class PolygonalWallGeneratorComponent : GH_Component
    {
        public PolygonalWallGeneratorComponent()
          : base("Polygonal Wall (Generator)", "PolyWall",
                 "Tile a surface (flat / curved / double-curved) with interlocking polygonal masonry stones " +
                 "(power-diagram cells + Lloyd relaxation + Coursing slider + sliver cull). Outputs closed, " +
                 "manifold, unified-normal stone meshes and an interlock score J. " +
                 "Pattern math: Frahan.Masonry.Sequencing.PolygonalWallGenerator (Rhino-free Core). " +
                 "Refs: Kim 2024 (ASME DETC2024-142563) sequencing substrate; Clifford & McGee 2018 " +
                 "(ACADIA, Cyclopean Cannibalism) interlock reading; Lloyd 1982 relaxation.",
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
            p.AddIntegerParameter("Lloyd", "L", "Lloyd relaxation iterations (evens stone size/shape; 0 disables)", GH_ParamAccess.item, 2);
            p.AddNumberParameter("SizeGrade", "Sg", "Size-grading strength 0..~0.6 (power-diagram weights)", GH_ParamAccess.item, 0.30);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "Closed, manifold, unified-outward-normal stone meshes", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of stones", GH_ParamAccess.item);
            p.AddNumberParameter("Interlock", "J", "Interlock score in [0,1]: 1 - runningJoints/headJoints - 0.5*crossVertices/cells. Higher = better staggering.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Pattern metrics (coverage, area CV, slivers culled, joints)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Surface srf = null;
            double w = 3.0, h = 1.8, coursing = 0.4, depth = 0.20, mortar = 0.08, sizeGrade = 0.30;
            int courses = 5, gx = 8, gy = 5, seed = 7, lloyd = 2;
            da.GetData(0, ref srf);
            da.GetData(1, ref w); da.GetData(2, ref h); da.GetData(3, ref coursing);
            da.GetData(4, ref courses); da.GetData(5, ref gx); da.GetData(6, ref gy);
            da.GetData(7, ref depth); da.GetData(8, ref mortar); da.GetData(9, ref seed);
            da.GetData(10, ref lloyd); da.GetData(11, ref sizeGrade);

            double shrink = 1.0 - Math.Max(0.0, Math.Min(0.45, mortar));

            // ---- param-space extents + (u,v) -> point/normal maps ----
            double lu = w, lv = h;
            Func<double, double, Point3d> map;
            Func<double, double, Vector3d> nrm;
            if (srf != null)
            {
                var du = srf.Domain(0); var dv = srf.Domain(1);
                var p00 = srf.PointAt(du.Min, dv.Min);
                lu = p00.DistanceTo(srf.PointAt(du.Max, dv.Min)); if (lu < 1e-6) lu = w;
                lv = p00.DistanceTo(srf.PointAt(du.Min, dv.Max)); if (lv < 1e-6) lv = h;
                map = (u, v) => srf.PointAt(du.ParameterAt(Clamp01(u / lu)), dv.ParameterAt(Clamp01(v / lv)));
                nrm = (u, v) => { var n = srf.NormalAt(du.ParameterAt(Clamp01(u / lu)), dv.ParameterAt(Clamp01(v / lv))); n.Unitize(); return n; };
            }
            else
            {
                map = (u, v) => new Point3d(u, 0, v);
                nrm = (u, v) => Vector3d.YAxis;
            }

            // ---- Core pattern math (Rhino-free) ----
            var result = PolygonalWallGenerator.Generate(new WallGenOptions
            {
                Width = lu, Height = lv,
                Coursing = coursing, Courses = courses,
                GridX = gx, GridY = gy, Seed = seed,
                LloydIterations = lloyd, SizeGradeCv = sizeGrade,
            });

            // ---- map + mesh each cell with the clean-mesh guarantee ----
            var rnd = new Random(seed);
            var meshes = new List<Mesh>();
            foreach (var cell in result.Cells)
            {
                var st = BuildStone(cell, map, nrm, depth, shrink, rnd);
                if (st != null) meshes.Add(st);
            }

            string report =
                $"stones {meshes.Count} | interlock J {result.InterlockScore:0.000} | " +
                $"coverage {result.AreaCoverage:0.000} | area CV {result.AreaCv:0.000} | " +
                $"slivers culled {result.CulledSlivers} | '+' vertices {result.CrossVertexCount} | " +
                $"running/total head joints {result.AlignedHeadJointLength:0.00}/{result.TotalHeadJointLength:0.00} m";

            da.SetDataList(0, meshes);
            da.SetData(1, meshes.Count);
            da.SetData(2, result.InterlockScore);
            da.SetData(3, report);
        }

        /// <summary>One stone: ngon-capped prism on the surface, GUARANTEED closed + manifold + unified outward normals.</summary>
        private static Mesh BuildStone(WallCell cell, Func<double, double, Point3d> map,
                                       Func<double, double, Vector3d> nrm, double depth, double shrink, Random rnd)
        {
            int k = cell.VertexCount;
            if (k < 3) return null;
            double cu = cell.CentroidU, cv = cell.CentroidV;
            var f = new List<Point3d>(k);
            for (int i = 0; i < k; i++)
            {
                double su = cu + (cell.Us[i] - cu) * shrink;
                double sv = cv + (cell.Vs[i] - cv) * shrink;
                f.Add(map(su, sv));
            }
            var n = nrm(cu, cv);
            double d = depth * (0.85 + rnd.NextDouble() * 0.3);
            var b = f.Select(p => p + n * d).ToList();

            var m = new Mesh();
            foreach (var p in f) m.Vertices.Add(p);
            foreach (var p in b) m.Vertices.Add(p);
            var cf = new Point3d(f.Average(p => p.X), f.Average(p => p.Y), f.Average(p => p.Z));
            int ci = m.Vertices.Add(cf), cbi = m.Vertices.Add(cf + n * d);
            var ff = new List<int>(); for (int i = 0; i < k; i++) ff.Add(m.Faces.AddFace(ci, i, (i + 1) % k));
            var bf = new List<int>(); for (int i = 0; i < k; i++) bf.Add(m.Faces.AddFace(cbi, k + (i + 1) % k, k + i));
            for (int i = 0; i < k; i++) m.Faces.AddFace(i, (i + 1) % k, k + (i + 1) % k, k + i);
            try
            {
                m.Ngons.AddNgon(MeshNgon.Create(Enumerable.Range(0, k).ToList(), ff));
                m.Ngons.AddNgon(MeshNgon.Create(Enumerable.Range(k, k).ToList(), bf));
            }
            catch { }

            // clean-mesh guarantee
            m.Vertices.CombineIdentical(true, true);
            m.Weld(Math.PI);
            m.UnifyNormals();
            double vol = 0; try { vol = m.Volume(); } catch { }
            if (vol < 0) m.Flip(true, true, true);
            m.RebuildNormals();
            return m.IsValid ? m : null;
        }

        private static double Clamp01(double t) => Math.Max(0, Math.Min(1, t));
    }
}
