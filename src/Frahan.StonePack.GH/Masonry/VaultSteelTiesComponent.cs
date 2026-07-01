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
    // Vault Steel Ties — resolve a compression-only vault's outward horizontal
    // support thrusts with a closed steel TENSION RING (Armadillo Vault tie logic).
    // Feed the springing points + the outward horizontal thrust vector at each
    // (from the TNA reactions, or built with native GH vector tools). Returns the
    // tie segments, tension-only forces, and a sized round bar per tie.
    // =========================================================================
    public sealed class VaultSteelTiesComponent : FrahanComponentBase
    {
        public VaultSteelTiesComponent()
            : base("Vault Steel Ties", "VaultTies",
                "Resolve a vault's OUTWARD horizontal support thrusts with a closed steel tension ring " +
                "(the Armadillo Vault's steel-tie logic). Inputs: support (springing) points and the outward " +
                "horizontal thrust vector at each (from the TNA reactions, or built with native GH vector tools). " +
                "Sorts the supports into a convex ring, solves tension-only node equilibrium for the tie forces, " +
                "and sizes a round steel bar per tie. For a regular N-gon with equal radial thrust H this gives " +
                "the classic ring force T = H / (2 sin(pi/N)).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0008-4A11-B500-0000000000A8");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter("Supports", "P", "Support (springing) points, one per support.", GH_ParamAccess.list);
            p.AddVectorParameter("Thrust", "H", "Outward horizontal thrust vector at each support (parallel to Supports; z ignored).", GH_ParamAccess.list);
            p.AddNumberParameter("Allowable Stress", "s", "Allowable steel stress (Pa). Default S355 yield.", GH_ParamAccess.item, 355e6);
            p.AddNumberParameter("Safety", "k", "Safety factor on tie tension for bar sizing.", GH_ParamAccess.item, 1.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddLineParameter("Ties", "T", "Steel tie segments (the support tension ring).", GH_ParamAccess.list);
            p.AddNumberParameter("Tension", "Fn", "Tie tension per segment (N, >= 0).", GH_ParamAccess.list);
            p.AddNumberParameter("Diameter", "d", "Required round-bar diameter per tie (m).", GH_ParamAccess.list);
            p.AddNumberParameter("Max Tension", "Mx", "Largest tie tension (N).", GH_ParamAccess.item);
            p.AddNumberParameter("Steel Volume", "Vol", "Total tie steel volume (m^3).", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var pts = new List<Point3d>();
            var thrust = new List<Vector3d>();
            double allow = 355e6, safety = 1.5;
            if (!da.GetDataList(0, pts) || pts.Count < 2)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need >= 2 support points."); return; }
            da.GetDataList(1, thrust);
            da.GetData(2, ref allow); da.GetData(3, ref safety);

            if (thrust.Count != pts.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Thrust ({thrust.Count}) must parallel Supports ({pts.Count})."); return; }

            var res = VaultSteelTies.Generate(pts, thrust, allow, safety);

            da.SetDataList(0, res.Ties);
            da.SetDataList(1, res.Tension);
            da.SetDataList(2, res.Diameter);
            da.SetData(3, res.MaxTension);
            da.SetData(4, res.TotalSteelVolume);
            da.SetData(5, $"{res.Note}; {res.Ties.Count} ties; steel {res.TotalSteelVolume * 7850.0:0.0} kg @ 7850 kg/m^3.");
            Message = $"{res.MaxTension / 1000.0:0.0} kN";
        }
    }
}
