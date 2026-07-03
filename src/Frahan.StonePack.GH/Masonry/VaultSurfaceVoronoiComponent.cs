#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Vault Surface Voronoi (Stage 2 of the rubble-vault tessellation).
    // Per-seed local tangent-plane Voronoi: project neighbours into each seed's
    // tangent frame and clip a box by perpendicular bisectors (Sutherland-Hodgman).
    // A columnness seal gradient shrinks vault cells to a hairline joint and lets
    // column cells overlap to seal the legs. Reproduces the Park Güell v004 cells.
    // Stage 2 of 4; feeds Vault Voussoir Moulds.
    // =========================================================================
    public sealed class VaultSurfaceVoronoiComponent : FrahanComponentBase
    {
        public VaultSurfaceVoronoiComponent()
            : base("Vault Surface Voronoi", "VaultVoronoi",
                "Per-seed local tangent-plane Voronoi cells for a rubble vault. For each sample, " +
                "builds a tangent frame (u = world-X projected to tangent, v = n x u), projects " +
                "neighbours in, and clips a square by each perpendicular bisector. Seal gradient " +
                "shrink = 0.92 + 0.22*columnness (vault hairline -> column overlap). Outputs cell " +
                "polylines + frames + aligned columnness. Stage 2 of 4; feeds Vault Voussoir Moulds.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0002-4A11-B500-0000000000A2");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Sample points (from Vault Surface Sampler).", GH_ParamAccess.list);
            p.AddVectorParameter("Normals", "N", "Surface normals.", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Per-sample columnness.", GH_ParamAccess.list);
            p.AddNumberParameter("r Vault", "rV", "Stone radius on the broad vault (m).", GH_ParamAccess.item, 0.21);
            p.AddNumberParameter("r Column", "rC", "Stone radius on the slender legs (m).", GH_ParamAccess.item, 0.11);
            p.AddBooleanParameter("Run", "R", "Execute the Voronoi.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Cells", "Ce", "Voronoi cell polylines (compacted, valid only).", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "Fr", "Per-cell tangent frame.", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Per-cell columnness (aligned with Cells).", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var pts = new List<Point3d>();
            var nrm = new List<Vector3d>();
            var col = new List<double>();
            double rV = 0.21, rC = 0.11; bool run = false;

            if (!GhGuard.List(this, da, 0, pts, "Points")) return;
            da.GetDataList(1, nrm);
            da.GetDataList(2, col);
            da.GetData(3, ref rV); da.GetData(4, ref rC); da.GetData(5, ref run);

            if (!run) { da.SetData(3, "Run = false. Toggle to build cells."); return; }
            if (nrm.Count != pts.Count || col.Count != pts.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Points / Normals / Columnness lengths differ.");
                return;
            }

            var res = VaultLocalVoronoi.Build(pts, nrm, col, rV, rC);
            da.SetDataList(0, res.Cells);
            da.SetDataList(1, res.Frames);
            da.SetDataList(2, res.Columnness);
            da.SetData(3, $"Built {res.Count} cells from {pts.Count} seeds.");
            Message = $"{res.Count} cells";
        }
    }
}
