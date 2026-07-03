#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Frahan.GH.ScanIngest;
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
    // CRA (compression-only, friction-bounded equilibrium).
    //
    // ASYNC + OUT-OF-PROCESS (P2 wiring, 2026-07-02): the assembly build + CRA run
    // on a background task and the QP itself runs in frahan_cra_worker.exe, so the
    // canvas never freezes on a big solve and a native-solver crash cannot take
    // Rhino down. Falls back to the in-process checker when the worker is absent.
    // Same inputs/outputs as the original sync component (saved files unaffected).
    // =========================================================================
    public sealed class VaultShellCraComponent
        : AsyncScanComponent<VaultShellCraComponent.Snapshot, VaultShellCraComponent.Payload>
    {
        public sealed class Snapshot
        {
            public Mesh Shell;
            public double Edge, Thick, Friction, Density, Band;
            public bool Stagger;
            public double MinBlock;
            public bool HubCapstone;
        }

        public sealed class Payload
        {
            public ShellAssemblyResult Sa;
            public bool Stable;
            public string SolverNote;
            public double Edge, Thick, Friction;
            public bool NoSupports;
            public bool Stagger;
        }

        public VaultShellCraComponent()
            : base("Vault Shell CRA", "ShellCRA",
                "Whole-shell rigid-block CRA of a funicular vault. QuadRemesh so the partition follows " +
                "the thrust (Edge Length 0 = use the input quads as-is, e.g. from Thrust Quad Remesh), " +
                "extrude each face into a voussoir from SHARED vertices (contact by construction), fix " +
                "the springing (lowest-z naked edges) as supports, and solve compression-only friction-" +
                "bounded equilibrium (CRA; Kao 2022). ASYNC: runs on a background task with the QP in the " +
                "out-of-process frahan_cra_worker (in-process fallback), so the canvas stays responsive. " +
                "Outputs the contact-ready blocks, a coverage mesh (blue = support, green = stable / red " +
                "= no admissible state), the interface axes, and the stability verdict.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-000B-4A11-B500-0000000000AB");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Shell", "M", "Funicular vault shell mesh (e.g. from the TNA form-finder or Thrust Quad Remesh).", GH_ParamAccess.item);
            p.AddNumberParameter("Edge Length", "E", "Target quad edge length (m) for the remesh. 0 = use the shell mesh as-is.", GH_ParamAccess.item, 0.4);
            p.AddNumberParameter("Thickness", "T", "Shell thickness (m); blocks extrude +/- T/2 along the vertex normals.", GH_ParamAccess.item, 0.30);
            p.AddNumberParameter("Friction", "F", "Mohr-Coulomb friction coefficient (tan phi).", GH_ParamAccess.item, 0.84);
            p.AddNumberParameter("Density", "D", "Stone density (kg/m^3) for self-weight.", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Support Band", "Sb", "Fix blocks whose naked edge sits within this fraction of the height above the lowest point (the springing).", GH_ParamAccess.item, 0.08);
            p.AddBooleanParameter("Run", "R", "Build the assembly + run CRA (async; canvas stays responsive).", GH_ParamAccess.item, false);
            // APPENDED (wiring-safe): whole-shell running bond.
            p.AddBooleanParameter("Stagger", "St", "Running bond: alternate courses merge quad PAIRS with an offset of one, so head joints never align (contact-by-construction preserved).", GH_ParamAccess.item, false);
            p.AddNumberParameter("Min Block", "Mb", "Stagger only: minimum block size as a fraction of the largest standard block (courses keep merging until reached; 0.45 validated).", GH_ParamAccess.item, 0.45);
            p.AddBooleanParameter("Hub Capstone", "Hc", "Stagger only: merge the singularity spiral into ONE keystone block per hub (radial cross-course merge) instead of granular rings.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B", "The contact-ready voussoir blocks (one per face).", GH_ParamAccess.list);
            p.AddMeshParameter("Coverage", "C", "Role/stability coverage mesh (blue = support, green = stable / red = unstable).", GH_ParamAccess.item);
            p.AddLineParameter("Interfaces", "I", "Interface axes (contact-face centre -> outward normal).", GH_ParamAccess.list);
            p.AddBooleanParameter("Stable", "S", "Whole-assembly CRA verdict: an admissible compression-only, friction-bounded force state exists.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary / status.", GH_ParamAccess.item);
        }

        protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
        {
            snapshot = null;
            run = false;
            da.GetData(6, ref run);
            if (!run) return true;

            Mesh shell = null;
            double edge = 0.4, thick = 0.30, friction = 0.84, density = 2400.0, band = 0.08;
            if (!da.GetData(0, ref shell) || shell == null || shell.Vertices.Count < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Shell mesh is missing or too small.");
                return false;
            }
            da.GetData(1, ref edge); da.GetData(2, ref thick); da.GetData(3, ref friction);
            da.GetData(4, ref density); da.GetData(5, ref band);
            bool stagger = false, hubCap = false;
            double minBlock = 0.45;
            da.GetData(7, ref stagger);
            da.GetData(8, ref minBlock);
            da.GetData(9, ref hubCap);
            snapshot = new Snapshot
            {
                Shell = shell.DuplicateMesh(),
                Edge = edge, Thick = thick, Friction = friction, Density = density, Band = band,
                Stagger = stagger, MinBlock = minBlock, HubCapstone = hubCap,
            };
            return true;
        }

        protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
        {
            Mesh quad = s.Shell;
            if (s.Edge > 0)
            {
                progress?.Invoke("remeshing...");
                var qp = new QuadRemeshParameters { TargetEdgeLength = s.Edge, AdaptiveSize = 0.0, AdaptiveQuadCount = false };
                quad = s.Shell.QuadRemesh(qp) ?? s.Shell.DuplicateMesh();
            }
            token.ThrowIfCancellationRequested();

            progress?.Invoke(s.Stagger ? "building STAGGERED assembly..." : "building assembly...");
            var sa = s.Stagger
                ? VaultShellAssembly.BuildStaggered(quad, s.Thick, s.Density, s.Band, s.MinBlock, s.HubCapstone)
                : VaultShellAssembly.Build(quad, s.Thick, s.Density, s.Band);
            token.ThrowIfCancellationRequested();

            bool stable;
            string note;
            if (CraWorkerClient.Available())
            {
                progress?.Invoke($"CRA worker ({sa.InterfaceCount} ifaces)...");
                if (CraWorkerClient.TrySolve(sa.Assembly, s.Friction, 8, true, 1.0, -9.80665,
                        timeoutMs: 3600_000, out stable, out _, out _, out string msg))
                    note = "out-of-process (frahan_cra_worker): " + (msg ?? "");
                else
                {
                    progress?.Invoke("worker failed; in-process CRA...");
                    var r = MasonryStabilityChecker.Check(sa.Assembly, s.Friction, 8, true, 1.0, -9.80665);
                    stable = r.IsStable;
                    note = "in-process fallback (" + (CraWorkerClient.LastError ?? "worker unavailable") + "): " + (r.Message ?? "");
                }
            }
            else
            {
                progress?.Invoke($"CRA in-process ({sa.InterfaceCount} ifaces)...");
                var r = MasonryStabilityChecker.Check(sa.Assembly, s.Friction, 8, true, 1.0, -9.80665);
                stable = r.IsStable;
                note = "in-process (worker not deployed): " + (r.Message ?? "");
            }

            return new Payload
            {
                Sa = sa, Stable = stable, SolverNote = note,
                Edge = s.Edge, Thick = s.Thick, Friction = s.Friction,
                NoSupports = sa.SupportCount == 0,
                Stagger = s.Stagger,
            };
        }

        protected override void EmitResult(IGH_DataAccess da, Payload r)
        {
            if (r.NoSupports)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No supports found (raise Support Band or check the springing z-band); CRA will be unstable.");

            var sa = r.Sa;
            var blue = Color.FromArgb(90, 130, 190);
            var green = Color.FromArgb(95, 175, 110);
            var red = Color.FromArgb(210, 105, 90);
            var freeCol = r.Stable ? green : red;
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
            da.SetData(3, r.Stable);
            da.SetData(4,
                $"Whole-shell CRA: {(r.Stable ? "STABLE" : "NOT stable")}. " +
                $"{sa.BlockCount} voussoirs, {sa.InterfaceCount} contact interfaces, {sa.SupportCount} support blocks. " +
                $"thickness {r.Thick:F2}m, friction {r.Friction:F2}, edge {r.Edge:F2}m. " +
                (r.Stagger ? "RUNNING BOND (staggered pairs). " : "") +
                $"Contact by construction (shared shell vertices). Solver: {r.SolverNote}");
            Message = r.Stable ? "STABLE" : "unstable";
        }

        protected override void EmitIdle(IGH_DataAccess da, string message)
        {
            da.SetData(4, message);
        }
    }
}
