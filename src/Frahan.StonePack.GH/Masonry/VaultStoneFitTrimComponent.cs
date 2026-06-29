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
    // Vault Stone Fit & Trim (Stage 4 of the rubble-vault tessellation).
    // Orients a chunky ETH1100 stone into each voussoir mould (thin axis -> cell
    // normal = radial, long axis -> tangent), scales + inflates, then boolean-
    // intersects to leave a raw rubble face with flat joints. Reproduces v004.
    // Stage 4 of 4; output = the rubble stones for baking / CRA validation.
    // =========================================================================
    public sealed class VaultStoneFitTrimComponent : FrahanComponentBase
    {
        public VaultStoneFitTrimComponent()
            : base("Vault Stone Fit & Trim", "VaultStone",
                "Orient + scale a chunky ETH1100 stone into each voussoir mould (thin axis -> cell " +
                "normal, long axis -> tangent), inflate, and boolean-intersect stock with the mould " +
                "to leave a raw rubble face + flat joints. overfill = overfill0 + 0.34*columnness and " +
                "inflate grow column stones to seal the legs. Reads .obj stones from a folder. " +
                "Stage 4 of 4; output is the rubble vault.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0004-4A11-B500-0000000000A4");
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
            p.AddMeshParameter("Moulds", "Mo", "Voussoir mould meshes (from Vault Voussoir Moulds).", GH_ParamAccess.list);
            p.AddCurveParameter("Cells", "Ce", "Cells aligned with Moulds.", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "Fr", "Frames aligned with Moulds.", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Columnness aligned with Moulds.", GH_ParamAccess.list);
            p.AddNumberParameter("d Vault", "dV", "Stone depth on the broad vault (m).", GH_ParamAccess.item, 0.26);
            p.AddNumberParameter("d Column", "dC", "Stone depth on the legs (m).", GH_ParamAccess.item, 0.20);
            p.AddTextParameter("ETH Dir", "Dir", "Folder of closed ETH1100 .obj stone meshes.", GH_ParamAccess.item);
            p.AddIntegerParameter("Seed", "S", "Pool sampling seed.", GH_ParamAccess.item, 18);
            p.AddNumberParameter("Overfill", "Ov", "Stock over-fill factor.", GH_ParamAccess.item, 1.16);
            p.AddNumberParameter("Pool AR", "AR", "Max bbox aspect ratio of admitted stones.", GH_ParamAccess.item, 2.2);
            p.AddBooleanParameter("Run", "R", "Execute the fit + trim.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Rubble", "Ru", "Trimmed rubble stones.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var moulds = new List<Mesh>();
            var curves = new List<Curve>();
            var frames = new List<Plane>();
            var col = new List<double>();
            double dV = 0.26, dC = 0.20, overfill = 1.16, poolAr = 2.2;
            string ethDir = null; int seed = 18; bool run = false;

            if (!GhGuard.List(this, da, 0, moulds, "Moulds")) return;
            da.GetDataList(1, curves);
            da.GetDataList(2, frames);
            da.GetDataList(3, col);
            da.GetData(4, ref dV); da.GetData(5, ref dC);
            da.GetData(6, ref ethDir); da.GetData(7, ref seed);
            da.GetData(8, ref overfill); da.GetData(9, ref poolAr); da.GetData(10, ref run);

            if (!run) { da.SetData(1, "Run = false. Toggle to fit + trim."); return; }
            if (curves.Count != moulds.Count || frames.Count != moulds.Count || col.Count != moulds.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Moulds / Cells / Frames / Columnness lengths differ.");
                return;
            }

            var cells = new List<PolylineCurve>(curves.Count);
            for (int i = 0; i < curves.Count; i++) cells.Add(ToPolylineCurve(curves[i]));

            var res = VaultStoneFitter.FitAndTrim(moulds, cells, frames, col, dV, dC, ethDir, seed, overfill, poolAr);
            if (res.PoolSize == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Stone pool empty (check ETH Dir path / .obj files).");

            da.SetDataList(0, res.Rubble);
            da.SetData(1, $"Carved {res.Count} rubble stones (pool {res.PoolSize}).");
            Message = $"{res.Count} stones";
        }
    }
}
