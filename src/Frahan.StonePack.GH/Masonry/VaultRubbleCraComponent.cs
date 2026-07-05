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
    // Vault Rubble CRA — the ABUTTING-CELLS structural check. Takes the rubble
    // cells (Vault Surface Voronoi OR Quad Cells), UN-SHRINKS them so neighbours
    // abut, extrudes each into a voussoir mould, DETECTS the shared contact faces,
    // fixes the springing (low-z cells), and runs whole-assembly rigid-block CRA.
    //
    // ASYNC + OUT-OF-PROCESS (P2 wiring, 2026-07-02): contact detection (the
    // dominant cost) AND the QP run off the canvas thread; the QP itself runs in
    // frahan_cra_worker.exe (in-process fallback). Snapshot capture stays O(N)
    // cheap — cells are polyline curves, not meshes. Same inputs/outputs as the
    // original sync component (saved files unaffected).
    // =========================================================================
    public sealed class VaultRubbleCraComponent
        : AsyncScanComponent<VaultRubbleCraComponent.Snapshot, VaultRubbleCraComponent.Payload>
    {
        public sealed class Snapshot
        {
            public List<PolylineCurve> Cells;
            public List<Plane> Frames;
            public List<double> Columnness;
            public double DV, DC, Friction, Density, Band;
        }

        public sealed class Payload
        {
            public ShellAssemblyResult Sa;
            public bool Stable;
            public string SolverNote;
            public double DV, DC, Friction;
        }

        public VaultRubbleCraComponent()
            : base("Vault Rubble CRA", "RubbleCRA",
                "Whole-assembly CRA of the abutting rubble cells. Un-shrinks the cells so neighbours abut, " +
                "extrudes each into a voussoir mould, detects the shared contact faces (MeshContactDetector), " +
                "fixes the low-z springing as supports, and solves compression-only friction-bounded " +
                "equilibrium. ASYNC: contact detection + the QP run on a background task with the solve in " +
                "the out-of-process frahan_cra_worker (in-process fallback) — the canvas stays responsive. " +
                "The raw ETH-fitted rubble stays a skin; CRA runs on the idealized abutting cells. " +
                "Blue-noise/quad cells; for the thrust-aligned contact-by-construction model use Vault Shell CRA.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-000C-4A11-B500-0000000000AC");
        protected override System.Drawing.Bitmap Icon => Frahan.GH.IconProvider.Load("VaultRubbleCra.png");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        private static PolylineCurve ToPolylineCurve(Curve c)
        {
            if (c == null) return null;
            if (c is PolylineCurve pc) return pc;
            return c.TryGetPolyline(out Polyline p) ? new PolylineCurve(p) : null;
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Cells", "Ce", "Rubble cell polylines (Vault Surface Voronoi or Quad Cells; 0.92-shrunk).", GH_ParamAccess.list);
            p.AddPlaneParameter("Frames", "Fr", "Per-cell tangent frames (aligned with Cells).", GH_ParamAccess.list);
            p.AddNumberParameter("Columnness", "C", "Per-cell columnness [0..1] (aligned with Cells).", GH_ParamAccess.list);
            p.AddNumberParameter("d Vault", "dV", "Voussoir depth on the broad vault (m).", GH_ParamAccess.item, 0.26);
            p.AddNumberParameter("d Column", "dC", "Voussoir depth on the legs (m).", GH_ParamAccess.item, 0.20);
            p.AddNumberParameter("Friction", "F", "Mohr-Coulomb friction coefficient (tan phi).", GH_ParamAccess.item, 0.84);
            p.AddNumberParameter("Density", "D", "Stone density (kg/m^3).", GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Support Band", "Sb", "Fix cells whose centroid sits within this fraction of the height above the lowest point (the springing).", GH_ParamAccess.item, 0.08);
            p.AddBooleanParameter("Run", "R", "Build the abutting-cell assembly + run CRA (async).", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Blocks", "B", "The abutting voussoir moulds (idealized cells).", GH_ParamAccess.list);
            p.AddMeshParameter("Coverage", "Cv", "Role/stability coverage (blue = support, green = stable / red = unstable).", GH_ParamAccess.item);
            p.AddBooleanParameter("Stable", "S", "Whole-assembly CRA verdict.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary / status.", GH_ParamAccess.item);
        }

        protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
        {
            snapshot = null;
            run = false;
            da.GetData(8, ref run);
            if (!run) return true;

            var curves = new List<Curve>();
            var frames = new List<Plane>();
            var col = new List<double>();
            double dV = 0.26, dC = 0.20, friction = 0.84, density = 2400.0, band = 0.08;
            if (!da.GetDataList(0, curves) || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cells input is missing/empty.");
                return false;
            }
            da.GetDataList(1, frames); da.GetDataList(2, col);
            da.GetData(3, ref dV); da.GetData(4, ref dC); da.GetData(5, ref friction);
            da.GetData(6, ref density); da.GetData(7, ref band);
            if (frames.Count != curves.Count || col.Count != curves.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cells / Frames / Columnness lengths differ.");
                return false;
            }

            // owned snapshot: duplicate the polylines (cheap — curves, not meshes);
            // Plane/double are value types, the new lists own them.
            var cells = new List<PolylineCurve>(curves.Count);
            foreach (var c in curves)
            {
                var pc = ToPolylineCurve(c);
                cells.Add(pc != null ? (PolylineCurve)pc.DuplicateCurve() : null);
            }
            snapshot = new Snapshot
            {
                Cells = cells,
                Frames = new List<Plane>(frames),
                Columnness = new List<double>(col),
                DV = dV, DC = dC, Friction = friction, Density = density, Band = band,
            };
            return true;
        }

        protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
        {
            progress?.Invoke($"cells + contact detection ({s.Cells.Count})...");
            var sa = VaultRubbleAssembly.Build(s.Cells, s.Frames, s.Columnness,
                                               s.DV, s.DC, s.Density, 0.0, s.Band);
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

            return new Payload { Sa = sa, Stable = stable, SolverNote = note, DV = s.DV, DC = s.DC, Friction = s.Friction };
        }

        protected override void EmitResult(IGH_DataAccess da, Payload r)
        {
            var sa = r.Sa;
            if (sa.SupportCount == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No supports found (raise Support Band / check springing); CRA will be unstable.");
            if (sa.InterfaceCount == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No contact interfaces detected (cells may not abut); the cells need to tile.");

            var blue = Color.FromArgb(90, 130, 190);
            var freeCol = r.Stable ? Color.FromArgb(95, 175, 110) : Color.FromArgb(210, 105, 90);
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
            da.SetData(2, r.Stable);
            da.SetData(3,
                $"Rubble (abutting-cell) CRA: {(r.Stable ? "STABLE" : "NOT stable")}. " +
                $"{sa.BlockCount} cells, {sa.InterfaceCount} detected interfaces, {sa.SupportCount} supports. " +
                $"depth {r.DV:F2}/{r.DC:F2}m, friction {r.Friction:F2}. Idealized abutting cells; raw rubble is the skin. " +
                $"Solver: {r.SolverNote}");
            Message = r.Stable ? "STABLE" : "unstable";
        }

        protected override void EmitIdle(IGH_DataAccess da, string message)
        {
            da.SetData(3, message);
        }
    }
}
