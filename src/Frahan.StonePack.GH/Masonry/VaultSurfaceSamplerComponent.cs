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
    // Vault Surface Sampler (Stage 1 of the rubble-vault tessellation).
    // Variable-density Poisson-disk sampling of a reference mesh: stones pack
    // finer on the colonnade legs (high columnness) than on the broad vault.
    // Reproduces the Park Güell v004 sampling step. Output feeds Vault Surface
    // Voronoi. Method: area-weighted face pick + spatial-grid blue-noise rejection.
    // =========================================================================
    public sealed class VaultSurfaceSamplerComponent : FrahanComponentBase
    {
        public VaultSurfaceSamplerComponent()
            : base("Vault Surface Sampler", "VaultSample",
                "Variable-density Poisson-disk (blue-noise) sampling of a reference mesh for a " +
                "rubble vault. columnness c = (1-smoothstep(z,zLo,zHi))*smoothstep(y,yLo,yHi) drives " +
                "the disk radius from rVault (broad vault) down to rCol (slender legs). Outputs sample " +
                "points, surface normals, and the per-sample columnness field. Stage 1 of 4; feeds " +
                "Vault Surface Voronoi.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0001-4A11-B500-0000000000A1");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "Reference surface mesh (e.g. SubD->mesh of the vault).", GH_ParamAccess.item);
            p.AddNumberParameter("r Vault", "rV", "Stone radius on the broad vault (m).", GH_ParamAccess.item, 0.21);
            p.AddNumberParameter("r Column", "rC", "Stone radius on the slender legs (m).", GH_ParamAccess.item, 0.11);
            p.AddNumberParameter("z Lo", "zL", "columnness Z band low (m).", GH_ParamAccess.item, 2.4);
            p.AddNumberParameter("z Hi", "zH", "columnness Z band high (m).", GH_ParamAccess.item, 3.3);
            p.AddNumberParameter("y Lo", "yL", "columnness Y band low (m).", GH_ParamAccess.item, 2.3);
            p.AddNumberParameter("y Hi", "yH", "columnness Y band high (m).", GH_ParamAccess.item, 3.3);
            p.AddIntegerParameter("Seed", "S", "Random seed.", GH_ParamAccess.item, 18);
            p.AddBooleanParameter("Run", "R", "Execute the sampler.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Sample points on the surface.", GH_ParamAccess.list);
            p.AddVectorParameter("Normals", "N", "Surface normal per sample.", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Per-sample columnness [0..1].", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            double rV = 0.21, rC = 0.11, zL = 2.4, zH = 3.3, yL = 2.3, yH = 3.3;
            int seed = 18; bool run = false;

            if (!GhGuard.Item(this, da, 0, ref mesh, "Mesh")) return;
            da.GetData(1, ref rV); da.GetData(2, ref rC);
            da.GetData(3, ref zL); da.GetData(4, ref zH);
            da.GetData(5, ref yL); da.GetData(6, ref yH);
            da.GetData(7, ref seed); da.GetData(8, ref run);

            if (!run) { da.SetData(3, "Run = false. Toggle to sample."); return; }

            var res = VaultSurfaceSampler.Sample(mesh, rV, rC, zL, zH, yL, yH, seed);
            int colSamples = 0;
            for (int i = 0; i < res.Columnness.Count; i++) if (res.Columnness[i] > 0.5) colSamples++;

            da.SetDataList(0, res.Points);
            da.SetDataList(1, res.Normals);
            da.SetDataList(2, res.Columnness);
            da.SetData(3, $"Sampled {res.Count} points ({colSamples} in column zone). rVault {rV:F3} / rCol {rC:F3}.");
            Message = $"{res.Count} pts";
        }
    }
}
