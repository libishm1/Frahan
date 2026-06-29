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
    // Vault Voussoir Moulds (Stage 3 of the rubble-vault tessellation).
    // Lifts each Voronoi cell polygon to a closed capped voussoir mould by
    // offsetting +/-(depth/2 + protrude) along the cell normal. depth =
    // dVault + (dCol-dVault)*columnness. Stage 3 of 4; feeds Vault Stone Fit & Trim.
    // =========================================================================
    public sealed class VaultVoussoirMouldsComponent : FrahanComponentBase
    {
        public VaultVoussoirMouldsComponent()
            : base("Vault Voussoir Moulds", "VaultMould",
                "Lift each Voronoi cell to a closed capped voussoir mould by offsetting +/-(D/2 + " +
                "protrude) along the cell normal. depth D = dVault + (dCol - dVault)*columnness " +
                "(thinner on the legs). Outputs the closed mould meshes plus the aligned cells / " +
                "frames / columnness. Stage 3 of 4; feeds Vault Stone Fit & Trim.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0003-4A11-B500-0000000000A3");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        private static PolylineCurve ToPolylineCurve(Curve c)
        {
            if (c == null) return null;
            var pc = c as PolylineCurve;
            if (pc != null) return pc;
            Polyline p;
            if (c.TryGetPolyline(out p)) return new PolylineCurve(p);
            return null;
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Cells", "Ce", "Voronoi cell polylines (from Vault Surface Voronoi).", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "Fr", "Per-cell tangent frames.", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Per-cell columnness.", GH_ParamAccess.list);
            p.AddNumberParameter("d Vault", "dV", "Stone depth on the broad vault (m).", GH_ParamAccess.item, 0.26);
            p.AddNumberParameter("d Column", "dC", "Stone depth on the legs (m).", GH_ParamAccess.item, 0.20);
            p.AddNumberParameter("Protrude", "Pr", "Mould overshoot past the shell (m).", GH_ParamAccess.item, 0.05);
            p.AddBooleanParameter("Run", "R", "Execute the capper.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Moulds", "Mo", "Closed voussoir mould meshes (compacted).", GH_ParamAccess.list);
            p.AddCurveParameter("Cells", "Ce", "Cells aligned with Moulds.", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "Fr", "Frames aligned with Moulds.", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Columnness aligned with Moulds.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var curves = new List<Curve>();
            var frames = new List<Plane>();
            var col = new List<double>();
            double dV = 0.26, dC = 0.20, pr = 0.05; bool run = false;

            if (!GhGuard.List(this, da, 0, curves, "Cells")) return;
            da.GetDataList(1, frames);
            da.GetDataList(2, col);
            da.GetData(3, ref dV); da.GetData(4, ref dC); da.GetData(5, ref pr); da.GetData(6, ref run);

            if (!run) { da.SetData(4, "Run = false. Toggle to cap."); return; }
            if (frames.Count != curves.Count || col.Count != curves.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cells / Frames / Columnness lengths differ.");
                return;
            }

            var cells = new List<PolylineCurve>(curves.Count);
            for (int i = 0; i < curves.Count; i++) cells.Add(ToPolylineCurve(curves[i]));

            var res = VaultVoussoirCapper.Cap(cells, frames, col, dV, dC, pr);
            da.SetDataList(0, res.Moulds);
            da.SetDataList(1, res.Cells);
            da.SetDataList(2, res.Frames);
            da.SetDataList(3, res.Columnness);
            da.SetData(4, $"Capped {res.Count} moulds from {curves.Count} cells.");
            Message = $"{res.Count} moulds";
        }
    }
}
