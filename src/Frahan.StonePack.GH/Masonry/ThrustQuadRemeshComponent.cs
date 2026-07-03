#nullable disable
using System;
using System.Threading;
using Frahan.GH.ScanIngest;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Thrust Quad Remesh (QuadWild) — reliable watertight all-quad remeshing that
    // FOLLOWS THE THRUST. Our thrust-potential cross-field (frahan_quadremesh) is
    // traced by QuadWild and quantized by Bi-MDF (Pietroni et al. 2021; Campen
    // group Bi-MDF, GPL-3.0, run out of process, no Gurobi), so it handles vaults
    // with openings/holes where the single-chart route folds. Async: the canvas
    // never freezes; toggle Run and results pop in.
    //
    // Validated on the Park Güell portico (16 x 4.9 x 3.6 m, 7 openings):
    // thrust field -> 7283 quads, 0 tris, watertight, all 8 boundary loops kept,
    // 37 irregular vertices (unrefined field 53; curvature mode 32).
    // =========================================================================
    public sealed class ThrustQuadRemeshComponent
        : AsyncScanComponent<ThrustQuadRemeshComponent.Snapshot, ThrustQuadRemeshComponent.Payload>
    {
        public sealed class Snapshot
        {
            public Mesh Mesh;
            public bool ThrustField;
            public double SupportFrac;
            public int SmoothSweeps;
            public double Coarseness;
        }

        public sealed class Payload
        {
            public Mesh Quads;
            public string Report;
        }

        public ThrustQuadRemeshComponent()
            : base("Thrust Quad Remesh (QuadWild)", "ThrustQuad",
                "Watertight all-quad remesh aligned to the THRUST flow. Our thrust-potential cross-field " +
                "(load-path Poisson, confidence-weighted 4-RoSy smoothing) is traced by QuadWild and " +
                "quantized by Bi-MDF (Pietroni et al. 2021 'Reliable Feature-Line Driven Quad-Remeshing'; " +
                "Campen group Bi-MDF/LEMON — no Gurobi; GPL-3.0 workers run out of process). Handles vaults " +
                "with openings where single-chart parametrization folds: holes are preserved and the output " +
                "is 100% quads. Thrust Field off = QuadWild's own curvature field. Async: canvas stays " +
                "responsive; toggle Run.",
                "Frahan", "Vault")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-000D-4A11-B500-0000000000AD");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "Vault surface mesh (tri or mixed; openings/holes are fine).", GH_ParamAccess.item);
            p.AddBooleanParameter("Thrust Field", "T", "Align quads to the thrust-potential field (needs frahan_quadremesh.exe). Off = QuadWild curvature field.", GH_ParamAccess.item, true);
            p.AddNumberParameter("Support Band", "S", "Support detection: boundary vertices in the lowest fraction of the z-range act as supports for the potential solve.", GH_ParamAccess.item, QuadWildRemesher.DefaultSupportFrac);
            p.AddIntegerParameter("Smooth", "W", "Field-smoothing sweeps (confidence-weighted 4-RoSy). Cleans noise singularities; 0 = raw field.", GH_ParamAccess.item, QuadWildRemesher.DefaultSmoothSweeps);
            p.AddNumberParameter("Coarseness", "C", "CONTINUOUS quad-size control (flow scaleFact): 1 = fine skin (7283 quads on the portico), 1.5 -> 3297, 2 -> 1879, 2.5 -> 1208, 5 -> 544 (CRA regime). Any fractional value works.", GH_ParamAccess.item, QuadWildRemesher.DefaultCoarseness);
            p.AddBooleanParameter("Run", "R", "Execute (async; the canvas stays responsive).", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Quad Mesh", "Q", "Watertight all-quad mesh (thrust- or curvature-aligned).", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary / status.", GH_ParamAccess.item);
        }

        protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
        {
            snapshot = null;
            run = false;
            da.GetData(5, ref run);
            if (!run) return true;

            Mesh mesh = null;
            bool thrust = true;
            double frac = QuadWildRemesher.DefaultSupportFrac;
            int sweeps = QuadWildRemesher.DefaultSmoothSweeps;
            double scale = QuadWildRemesher.DefaultCoarseness;
            if (!da.GetData(0, ref mesh) || mesh == null || mesh.Vertices.Count < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh input is missing or too small.");
                return false;
            }
            da.GetData(1, ref thrust);
            da.GetData(2, ref frac);
            da.GetData(3, ref sweeps);
            da.GetData(4, ref scale);
            if (!QuadWildRemesher.Available())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "quadwild-bimdf workers not found: expected thirdparty/quadwild-bimdf/bin beside the plugin (or set QUADWILD_HOME).");
                return false;
            }
            snapshot = new Snapshot
            {
                Mesh = mesh.DuplicateMesh(),
                ThrustField = thrust,
                SupportFrac = Math.Max(0.02, Math.Min(0.95, frac)),
                SmoothSweeps = Math.Max(0, sweeps),
                Coarseness = Math.Max(1.0, scale),
            };
            return true;
        }

        protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
        {
            Mesh q = QuadWildRemesher.Remesh(s.Mesh, s.ThrustField, s.SupportFrac, s.SmoothSweeps,
                                             s.Coarseness, progress, token, out string report);
            if (q == null) throw new InvalidOperationException(QuadWildRemesher.LastError ?? "QuadWild remesh failed.");
            return new Payload { Quads = q, Report = report };
        }

        protected override void EmitResult(IGH_DataAccess da, Payload r)
        {
            da.SetData(0, r.Quads);
            da.SetData(1, r.Report);
            Message = r.Quads.Faces.QuadCount + " quads";
        }

        protected override void EmitIdle(IGH_DataAccess da, string message)
        {
            da.SetData(1, message);
        }
    }
}
