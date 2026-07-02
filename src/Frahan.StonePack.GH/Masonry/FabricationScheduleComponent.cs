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
    // Fabrication Schedule — the shop-paperwork end of the vault pipeline
    // (P2 fabrication outputs, 2026-07-02). Feed the voussoirs (Vault Shell
    // CRA Blocks, Quad Courses Voussoirs, or the fitted rubble) and get stable
    // IDs, tag points for labelling, a CSV cutting/handling schedule (dims,
    // volume, weight, fabrication order = largest first), and an optional flat
    // inspection layout. For 2D cut sheets of planar faces use Sheet Nest
    // (Hole-Aware) downstream.
    // =========================================================================
    public sealed class FabricationScheduleComponent : FrahanComponentBase
    {
        public FabricationScheduleComponent()
            : base("Fabrication Schedule", "FabSched",
                "Shop paperwork for a voussoir/stone list: stable IDs (largest-first fabrication order), " +
                "tag points for labelling, a CSV schedule (bbox dims for saw envelopes, volume, weight at " +
                "the given density, centroid), and an optional flat inspection LAYOUT (blocks re-arranged " +
                "on a ground grid in ID order). Wire Blocks from Vault Shell CRA, Voussoirs from Vault " +
                "Quad Courses, or Rubble from Vault Stone Fit & Trim.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0014-4A11-B500-0000000000B4");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B", "Voussoir / stone meshes to schedule.", GH_ParamAccess.list);
            p.AddNumberParameter("Density", "D", "Stone density (kg/m^3) for weights.", GH_ParamAccess.item, 2400.0);
            p.AddTextParameter("Prefix", "P", "ID prefix (V001, V002, ...).", GH_ParamAccess.item, "V");
            p.AddNumberParameter("Layout Spacing", "S", "Gap (m) between blocks in the flat inspection layout; 0 = no layout.", GH_ParamAccess.item, 0.3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("IDs", "Id", "Stable block IDs (fabrication order, largest first).", GH_ParamAccess.list);
            p.AddPointParameter("Tag Points", "Tp", "Block centroids for text tags (align with IDs).", GH_ParamAccess.list);
            p.AddTextParameter("CSV", "C", "Cutting/handling schedule (save as .csv).", GH_ParamAccess.item);
            p.AddMeshParameter("Layout", "L", "Flat inspection layout (empty when Layout Spacing = 0).", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var blocks = new List<Mesh>();
            double density = 2400.0, spacing = 0.3;
            string prefix = "V";
            if (!da.GetDataList(0, blocks) || blocks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No blocks supplied.");
                return;
            }
            da.GetData(1, ref density); da.GetData(2, ref prefix); da.GetData(3, ref spacing);

            var r = FabricationSchedule.Build(blocks, density, prefix, spacing);
            da.SetDataList(0, r.Ids);
            da.SetDataList(1, r.TagPoints);
            da.SetData(2, r.Csv);
            da.SetDataList(3, r.Layout);
            da.SetData(4,
                $"{r.Count} blocks scheduled: total volume {r.TotalVolume:0.00} m^3, " +
                $"total weight {r.TotalWeight / 1000.0:0.00} t @ {density:0} kg/m^3. " +
                "Order = largest first; CSV columns: id, source_index, dims, volume, weight, centroid.");
            Message = $"{r.Count} blocks, {r.TotalWeight / 1000.0:0.0} t";
        }
    }
}
