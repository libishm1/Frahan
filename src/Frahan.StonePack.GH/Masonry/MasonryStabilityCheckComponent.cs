using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.Masonry.Solvers;

namespace Frahan.StonePack.GH.Masonry
{
    /// <summary>
    /// Masonry Stability Check — the shared "does it stand?" gate (evolution P1,
    /// 2026-06-10). Stone meshes -> auto-detected contact interfaces ->
    /// MasonryAssembly -> RBE QP (Kao 2021/2022 rigid-block equilibrium;
    /// compression-only, Coulomb friction as an INSCRIBED K-face pyramid,
    /// mu_eff = mu*cos(pi/K), conservative) -> stable verdict + per-interface
    /// friction utilization. Blocks whose lowest vertex sits within FixBelowZ of
    /// the global minimum Z are treated as fixed (the ground course).
    /// RBE is the force-only necessary condition; the coupled CRA refinement
    /// (Kao 2022 Eqs 8-14) is evolution phase P2.
    /// </summary>
    public class MasonryStabilityCheckComponent : GH_Component
    {
        public MasonryStabilityCheckComponent()
          : base("Masonry Stability Check", "MasonStable",
                 "Rigid-block equilibrium (RBE) stability check for a stone assembly: contacts are " +
                 "auto-detected, the Kao 2021/2022 compression-only + Coulomb-friction QP is solved, " +
                 "and the verdict + per-interface friction utilization are reported. Friction uses a " +
                 "conservative INSCRIBED K-face pyramid (mu_eff = mu*cos(pi/K)). " +
                 "Refs: Kao et al. 2021 (J Mech Des) / 2022 (CAD 146:103216, compas_cra).",
                 "Frahan", "Masonry")
        { }

        public override Guid ComponentGuid => new Guid("D5F10015-2B43-4E8A-A1C7-9D0F4B6E2A91");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "Closed stone meshes in their placed positions", GH_ParamAccess.list);
            p.AddNumberParameter("Mu", "Mu", "Coulomb friction coefficient (0.84 ~ dry stone, 40 deg)", GH_ParamAccess.item, 0.84);
            p.AddIntegerParameter("Faces", "K", "Friction pyramid face count (>= 3; 8 recommended)", GH_ParamAccess.item, 8);
            p.AddNumberParameter("FixBelowZ", "Fz", "Blocks whose lowest vertex is within this of the global min Z are fixed (ground)", GH_ParamAccess.item, 0.01);
            p.AddNumberParameter("Density", "Rho", "Stone density (kg/m^3)", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("ContactTol", "Ct", "Contact detection distance tolerance (model units)", GH_ParamAccess.item, 0.005);
            p.AddNumberParameter("AngleTol", "At", "Contact detection face-angle tolerance (degrees). Raise to ~12-20 for stones on CURVED surfaces, where adjacent stones extrude along different normals and joint faces tilt apart.", GH_ParamAccess.item, 5.0);
            p.AddGenericParameter("Assembly", "A",
                "OPTIONAL: a pre-built assembly (e.g. the Polygonal Wall Generator's Assembly output, with " +
                "exact generator-adjacency joints). When supplied, Stones/tolerances are ignored and the " +
                "check runs directly on it - much faster and tolerance-free.", GH_ParamAccess.item);
            p[7].Optional = true;
            p.AddBooleanParameter("CRA", "Cr",
                "Use the COUPLED rigid-block analysis (Kao 2022 Eqs 8-14, alternating convex certificate) " +
                "instead of force-only RBE. CRA also checks that a kinematically consistent virtual motion " +
                "exists, rejecting self-stressed states RBE wrongly accepts (the H-model).",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Stable", "OK", "True when an admissible compressive/friction-consistent force state exists (RBE-stable)", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Verdict, counts, max compression, worst friction utilization, weakest interface", GH_ParamAccess.item);
            p.AddNumberParameter("Utilization", "U", "Per-interface max friction utilization (1.0 = cone saturated)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var stones = new List<Mesh>();
            double mu = 0.84, fixBelowZ = 0.01, density = 2400.0, contactTol = 0.005, angleTol = 5.0;
            int faces = 8;
            bool useCra = false;
            object assemblyInput = null;
            da.GetData(7, ref assemblyInput);
            da.GetData(8, ref useCra);
            bool hasAssembly = TryUnwrapAssembly(assemblyInput, out var preBuilt);
            if (!hasAssembly && (!da.GetDataList(0, stones) || stones.Count == 0)) return;
            da.GetData(1, ref mu); da.GetData(2, ref faces); da.GetData(3, ref fixBelowZ);
            da.GetData(4, ref density); da.GetData(5, ref contactTol); da.GetData(6, ref angleTol);

            if (hasAssembly)
            {
                SolveOnAssembly(da, preBuilt, mu, faces, useCra, "exact joints (generator adjacency)");
                return;
            }

            var coordsList = new List<IReadOnlyList<double>>(stones.Count);
            var trisList = new List<IReadOnlyList<int>>(stones.Count);
            for (int i = 0; i < stones.Count; i++)
            {
                var m = stones[i];
                if (m == null) continue;
                var t = m.DuplicateMesh();
                t.Faces.ConvertQuadsToTriangles();
                var coords = new List<double>(t.Vertices.Count * 3);
                for (int v = 0; v < t.Vertices.Count; v++)
                {
                    var pt = t.Vertices[v];
                    coords.Add(pt.X); coords.Add(pt.Y); coords.Add(pt.Z);
                }
                var tris = new List<int>(t.Faces.Count * 3);
                for (int f = 0; f < t.Faces.Count; f++)
                {
                    var face = t.Faces[f];
                    tris.Add(face.A); tris.Add(face.B); tris.Add(face.C);
                }
                coordsList.Add(coords); trisList.Add(tris);
            }
            if (coordsList.Count == 0) return;

            Frahan.Masonry.DataModel.MasonryAssembly meshAssembly;
            try
            {
                meshAssembly = MasonryStabilityChecker.BuildAssemblyFromMeshes(
                    coordsList, trisList,
                    density: density,
                    contactDistanceTol: contactTol,
                    contactAngleTolDeg: Math.Max(0.1, Math.Min(45.0, angleTol)),
                    fixBelowZ: fixBelowZ);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Contact detection failed: " + ex.Message);
                return;
            }
            SolveOnAssembly(da, meshAssembly, mu, faces, useCra, "detected contacts");
        }

        private static bool TryUnwrapAssembly(object input, out Frahan.Masonry.DataModel.MasonryAssembly assembly)
        {
            assembly = null;
            object v = input;
            if (v is Grasshopper.Kernel.Types.GH_ObjectWrapper w) v = w.Value;
            if (v is Frahan.Masonry.Sequencing.WallAssembly wa) { assembly = wa.Assembly; return true; }
            if (v is Frahan.Masonry.DataModel.MasonryAssembly ma) { assembly = ma; return true; }
            return false;
        }

        private void SolveOnAssembly(IGH_DataAccess da, Frahan.Masonry.DataModel.MasonryAssembly assembly,
                                     double mu, int faces, bool useCra, string source)
        {
            StabilityResult result;
            string craNote = "";
            try
            {
                if (useCra)
                {
                    var cra = Frahan.Masonry.Solvers.CraStabilityChecker.Check(
                        assembly, mu: mu, faceCount: Math.Max(3, faces), inscribed: true);
                    result = cra.FinalForces;
                    craNote = (cra.Certified
                        ? "CRA-CERTIFIED (residual " + cra.CertificateResidual.ToString("0.00") + "e, " + cra.Iterations + " iter) | "
                        : "CRA verdict (not certified; " + cra.Iterations + " iter) | ");
                    if (cra.IsStable != result.IsStable)
                        result = new StabilityResult(cra.IsStable, result.Status, cra.Message,
                            result.MaxCompression, result.MaxFrictionUtilization, result.WeakestInterfaceIndex,
                            result.Interfaces, result.FreeBlockCount, result.InterfaceCount, result.ContactVertexCount);
                }
                else
                {
                    result = MasonryStabilityChecker.Check(
                        assembly, mu: mu, faceCount: Math.Max(3, faces), inscribed: true);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Stability check failed: " + ex.Message);
                return;
            }
            EmitOutputs(da, result, craNote + source);
        }

        private void EmitOutputs(IGH_DataAccess da, StabilityResult result, string sourceNote)
        {

            var sb = new StringBuilder();
            sb.Append(result.IsStable ? "STABLE" : "NOT STABLE").Append(" | ").Append(sourceNote).Append(" | ");
            sb.Append("blocks free ").Append(result.FreeBlockCount)
              .Append(", interfaces ").Append(result.InterfaceCount)
              .Append(", contact vertices ").Append(result.ContactVertexCount).Append(" | ");
            sb.Append("max compression ").Append(result.MaxCompression.ToString("0.###"))
              .Append(", worst friction util ").Append(result.MaxFrictionUtilization.ToString("0.00"));
            if (result.WeakestInterfaceIndex >= 0 && result.Interfaces.Count > 0)
            {
                foreach (var u in result.Interfaces)
                {
                    if (u.InterfaceIndex != result.WeakestInterfaceIndex) continue;
                    sb.Append(" | weakest: ").Append(u.BlockAId).Append("<->").Append(u.BlockBId);
                    break;
                }
            }
            sb.Append(" | ").Append(result.Message);

            var utils = new List<double>(result.Interfaces.Count);
            foreach (var u in result.Interfaces) utils.Add(u.MaxFrictionUtilization);

            if (!result.IsStable)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Assembly is not RBE-stable: " + result.Message);

            da.SetData(0, result.IsStable);
            da.SetData(1, sb.ToString());
            da.SetDataList(2, utils);
        }
    }
}
