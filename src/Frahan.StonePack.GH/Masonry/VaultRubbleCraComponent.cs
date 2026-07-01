#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH;
using Frahan.Masonry.Solvers;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Vault Rubble CRA — the ABUTTING-CELLS structural check. Takes the Voronoi
    // rubble cells (from Vault Surface Voronoi), UN-SHRINKS them so neighbours abut
    // (removing the seal-gradient hairline gap), extrudes each into a voussoir mould,
    // DETECTS the shared contact faces, fixes the springing (low-z cells), and runs
    // whole-assembly rigid-block CRA (native OSQP). Answers "does the as-drawn rubble
    // tessellation itself stand?" -- the raw ETH stones remain a display skin; CRA
    // runs on these idealized cells. Blue-noise (NOT thrust-aligned); for the
    // thrust-aligned structural model use Vault Shell CRA. Keep cells <= a few
    // hundred (contact detection cost); coarsen the Poisson radius otherwise.
    // =========================================================================
    public sealed class VaultRubbleCraComponent : FrahanComponentBase
    {
        public VaultRubbleCraComponent()
            : base("Vault Rubble CRA", "RubbleCRA",
                "Whole-assembly CRA of the Voronoi rubble cells. Un-shrinks the cells so neighbours abut, " +
                "extrudes each into a voussoir mould, detects the shared contact faces (MeshContactDetector), " +
                "fixes the low-z springing as supports, and solves compression-only friction-bounded " +
                "equilibrium (native OSQP). The raw ETH-fitted rubble stays a skin; CRA runs on the idealized " +
                "abutting cells. Blue-noise, not thrust-aligned -- for the thrust-aligned model use Vault Shell CRA.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-000C-4A11-B500-0000000000AC");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        private static PolylineCurve ToPolylineCurve(Curve c)
        {
            if (c == null) return null;
            if (c is PolylineCurve pc) return pc;
            return c.TryGetPolyline(out Polyline p) ? new PolylineCurve(p) : null;
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Cells", "Ce", "Voronoi rubble cell polylines (from Vault Surface Voronoi).", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "Fr", "Per-cell tangent frames (aligned with Cells).", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Per-cell columnness [0..1] (aligned with Cells).", GH_ParamAccess.list);
            p.AddNumberParameter("d Vault", "dV", "Voussoir depth on the broad vault (m).", GH_ParamAccess.item, 0.26);
            p.AddNumberParameter("d Column", "dC", "Voussoir depth on the legs (m).", GH_ParamAccess.item, 0.20);
            p.AddNumberParameter("Friction", "F", "Mohr-Coulomb friction coefficient (tan phi).", GH_ParamAccess.item, 0.84);
            p.AddNumberParameter("Density", "D", "Stone density (kg/m^3).", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Support Band", "Sb", "Fix cells whose centroid sits within this fraction of the height above the lowest point (the springing).", GH_ParamAccess.item, 0.08);
            p.AddBooleanParameter("Run", "R", "Build the abutting-cell assembly + run CRA.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B", "The abutting voussoir moulds (idealized cells).", GH_ParamAccess.list);
            p.AddMeshParameter("Coverage", "Cv", "Role/stability coverage (blue = support, green = stable / red = unstable).", GH_ParamAccess.item);
            p.AddBooleanParameter("Stable", "S", "Whole-assembly CRA verdict.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var curves = new List<Curve>();
            var frames = new List<Plane>();
            var col = new List<double>();
            double dV = 0.26, dC = 0.20, friction = 0.84, density = 2400.0, band = 0.08;
            bool run = false;
            if (!da.GetDataList(0, curves) || curves.Count == 0) return;
            da.GetDataList(1, frames); da.GetDataList(2, col);
            da.GetData(3, ref dV); da.GetData(4, ref dC); da.GetData(5, ref friction);
            da.GetData(6, ref density); da.GetData(7, ref band); da.GetData(8, ref run);
            if (!run) { da.SetData(3, "Run = false. Toggle to build the abutting-cell assembly + run CRA."); return; }
            if (frames.Count != curves.Count || col.Count != curves.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cells / Frames / Columnness lengths differ."); return; }

            var cells = new List<PolylineCurve>(curves.Count);
            foreach (var c in curves) cells.Add(ToPolylineCurve(c));

            ShellAssemblyResult sa; bool stable;
            try
            {
                sa = VaultRubbleAssembly.Build(cells, frames, col, dV, dC, density, 0.0, band);
                if (sa.SupportCount == 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No supports found (raise Support Band / check springing); CRA will be unstable.");
                if (sa.InterfaceCount == 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No contact interfaces detected (cells may not abut); the cells need to tile.");
                stable = MasonryStabilityChecker.Check(sa.Assembly, friction, 8, true, 1.0, -9.80665).IsStable;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Rubble CRA failed: " + ex.Message);
                return;
            }

            var blue = Color.FromArgb(90, 130, 190);
            var freeCol = stable ? Color.FromArgb(95, 175, 110) : Color.FromArgb(210, 105, 90);
            var support = new HashSet<int>(sa.FixedIndices);
            var cov = new Mesh(); int baseIdx = 0;
            for (int i = 0; i < sa.Voussoirs.Count; i++)
            {
                var vm = sa.Voussoirs[i];
                var colr = support.Contains(i) ? blue : freeCol;
                for (int v = 0; v < vm.Vertices.Count; v++) { cov.Vertices.Add(vm.Vertices[v]); cov.VertexColors.Add(colr); }
                foreach (var f in vm.Faces)
                {
                    if (f.IsQuad) cov.Faces.AddFace(baseIdx + f.A, baseIdx + f.B, baseIdx + f.C, baseIdx + f.D);
                    else cov.Faces.AddFace(baseIdx + f.A, baseIdx + f.B, baseIdx + f.C);
                }
                baseIdx += vm.Vertices.Count;
            }
            cov.Normals.ComputeNormals();

            da.SetDataList(0, sa.Voussoirs);
            da.SetData(1, cov);
            da.SetData(2, stable);
            da.SetData(3,
                $"Rubble (abutting-cell) CRA: {(stable ? "STABLE" : "NOT stable")}. " +
                $"{sa.BlockCount} cells, {sa.InterfaceCount} detected interfaces, {sa.SupportCount} supports. " +
                $"depth {dV:F2}/{dC:F2}m, friction {friction:F2}. Idealized abutting cells; raw rubble is the skin.");
            Message = stable ? "STABLE" : "unstable";
        }
    }
}
