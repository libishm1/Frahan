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
    // Vault Shell CRA — the STRUCTURAL vault, not a decorative skin. QuadRemesh a
    // funicular shell so the quad partition follows the thrust, extrude each face
    // into a voussoir from SHARED shell vertices (adjacent blocks share exact
    // contact faces -> contact by construction), and run whole-assembly rigid-block
    // CRA (compression-only, friction-bounded equilibrium, native OSQP). Supports
    // are the springing (lowest-z naked edges). Outputs the contact-ready blocks,
    // a role/stability-coloured coverage mesh, the interface axes, and the verdict.
    // This is what "following the thrust network + CRA contact logic" means for the
    // vault -- distinct from the geometric Voronoi rubble tessellation.
    // =========================================================================
    public sealed class VaultShellCraComponent : FrahanComponentBase
    {
        public VaultShellCraComponent()
            : base("Vault Shell CRA", "ShellCRA",
                "Whole-shell rigid-block CRA of a funicular vault. QuadRemesh so the partition follows " +
                "the thrust, extrude each face into a voussoir from SHARED vertices (contact by " +
                "construction -- neighbours share exact faces, no gaps), fix the springing (lowest-z " +
                "naked edges) as supports, and solve compression-only friction-bounded equilibrium (CRA, " +
                "native OSQP). Outputs the contact-ready blocks, a coverage mesh (blue = support, green = " +
                "stable / red = no admissible state), the interface axes, and the stability verdict.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-000B-4A11-B500-0000000000AB");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Shell", "M", "Funicular vault shell mesh (e.g. from the TNA form-finder).", GH_ParamAccess.item);
            p.AddNumberParameter("Edge Length", "E", "Target quad edge length (m) for the remesh. 0 = use the shell mesh as-is.", GH_ParamAccess.item, 0.4);
            p.AddNumberParameter("Thickness", "T", "Shell thickness (m); blocks extrude +/- T/2 along the vertex normals.", GH_ParamAccess.item, 0.30);
            p.AddNumberParameter("Friction", "F", "Mohr-Coulomb friction coefficient (tan phi).", GH_ParamAccess.item, 0.84);
            p.AddNumberParameter("Density", "D", "Stone density (kg/m^3) for self-weight.", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Support Band", "Sb", "Fix blocks whose naked edge sits within this fraction of the height above the lowest point (the springing).", GH_ParamAccess.item, 0.08);
            p.AddBooleanParameter("Run", "R", "Build the assembly + run CRA.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B", "The contact-ready voussoir blocks (one per face).", GH_ParamAccess.list);
            p.AddMeshParameter("Coverage", "C", "Role/stability coverage mesh (blue = support, green = stable / red = unstable).", GH_ParamAccess.item);
            p.AddLineParameter("Interfaces", "I", "Interface axes (contact-face centre -> outward normal).", GH_ParamAccess.list);
            p.AddBooleanParameter("Stable", "S", "Whole-assembly CRA verdict: an admissible compression-only, friction-bounded force state exists.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh shell = null;
            double edge = 0.4, thick = 0.30, friction = 0.84, density = 2400.0, band = 0.08;
            bool run = false;
            if (!GhGuard.Item(this, da, 0, ref shell, "Shell")) return;
            da.GetData(1, ref edge); da.GetData(2, ref thick); da.GetData(3, ref friction);
            da.GetData(4, ref density); da.GetData(5, ref band); da.GetData(6, ref run);
            if (!run) { da.SetData(4, "Run = false. Toggle to build the assembly + run CRA."); return; }

            Mesh quad = shell;
            if (edge > 0)
            {
                var qp = new QuadRemeshParameters { TargetEdgeLength = edge, AdaptiveSize = 0.0, AdaptiveQuadCount = false };
                quad = shell.QuadRemesh(qp) ?? shell.DuplicateMesh();
            }

            ShellAssemblyResult sa;
            bool stable;
            try
            {
                sa = VaultShellAssembly.Build(quad, thick, density, band);
                if (sa.SupportCount == 0)
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No supports found (raise Support Band or check the springing z-band); CRA will be unstable."); }
                stable = MasonryStabilityChecker.Check(sa.Assembly, friction, 8, true, 1.0, -9.80665).IsStable;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Shell CRA failed: " + ex.Message);
                return;
            }

            // coverage mesh: supports blue; free blocks green (stable) / red (unstable)
            var blue = Color.FromArgb(90, 130, 190);
            var green = Color.FromArgb(95, 175, 110);
            var red = Color.FromArgb(210, 105, 90);
            var freeCol = stable ? green : red;
            var support = new HashSet<int>(sa.FixedIndices);
            var cov = new Mesh();
            int baseIdx = 0;
            for (int i = 0; i < sa.Voussoirs.Count; i++)
            {
                var vm = sa.Voussoirs[i];
                var col = support.Contains(i) ? blue : freeCol;
                for (int v = 0; v < vm.Vertices.Count; v++) { cov.Vertices.Add(vm.Vertices[v]); cov.VertexColors.Add(col); }
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
            da.SetDataList(2, sa.InterfaceAxes);
            da.SetData(3, stable);
            da.SetData(4,
                $"Whole-shell CRA: {(stable ? "STABLE" : "NOT stable")}. " +
                $"{sa.BlockCount} voussoirs, {sa.InterfaceCount} contact interfaces, {sa.SupportCount} support blocks. " +
                $"thickness {thick:F2}m, friction {friction:F2}, edge {edge:F2}m. " +
                "Contact by construction (shared shell vertices); thrust-aligned quad partition.");
            Message = stable ? "STABLE" : "unstable";
        }
    }
}
