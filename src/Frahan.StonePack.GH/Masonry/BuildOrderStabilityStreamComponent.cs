#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Solvers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BuildOrderStabilityStreamComponent — walk the build order and run the
    // RBE QP on each partial assembly. Reports the FIRST step at which the
    // in-progress wall becomes unstable. That's the actionable answer for
    // a builder; checking only the finished assembly hides the moment the
    // wall actually fails.
    //
    // Synchronous (no async dispatch) so per-step verdicts are emitted in
    // order. For long sequences the user can disable Run, set Stop On First
    // Infeasible = true (default), or supply a Max Steps cap.
    //
    // ComponentGuid: F2D000B3-CADC-4F2D-A0B3-7E60CADA15A0
    // (was ABCDEF01-2345-6789-ABCD-EF0123456789; collided with MeshAabbComponent)
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Build-Order Stability Stream.
    /// Walks the build order, runs RBE QP on each partial assembly,
    /// surfaces the first unstable step.
    /// </summary>
        [DesignApplication(
        "Walks a masonry build order and runs the RBE convex-QP  stability check on each partial assembly",
        DesignFlow.BottomUp,
        Precedent = "Kim 2024 polygonal masonry install order; Heyman 1966 + Kao 2022 CRA stability gates")]
    public sealed class BuildOrderStabilityStreamComponent : FrahanComponentBase
    {
        public BuildOrderStabilityStreamComponent()
            : base(
                "Build-Order Stability Stream", "StabStream",
                "Walks a masonry build order and runs the RBE convex-QP " +
                "stability check on each partial assembly. Reports the " +
                "first step at which the in-progress wall becomes unstable.",
                "Frahan", "Masonry")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override Guid ComponentGuid =>
            new Guid("F2D000B3-CADC-4F2D-A0B3-7E60CADA15A0");

        protected override Bitmap Icon => IconProvider.Load("EquilibriumRBE.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "Full MasonryAssembly DTO.",
                GH_ParamAccess.item);
            p.AddTextParameter("Ordered Block Ids", "Id",
                "Block ids in build order. Output of Block Build Order.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Mu", "Mu",
                "Coulomb friction coefficient. Default 0.84 (~40°).",
                GH_ParamAccess.item, 0.84);
            p[2].Optional = true;
            p.AddIntegerParameter("Faces", "F",
                "Pyramidal friction-cone face count. Default 4.",
                GH_ParamAccess.item, 4);
            p[3].Optional = true;
            p.AddBooleanParameter("Penalty", "P",
                "Penalty form (split f_n+/f_n-). Default false.",
                GH_ParamAccess.item, false);
            p[4].Optional = true;
            p.AddBooleanParameter("Stop On First Infeasible", "Stop",
                "Stop streaming as soon as one step is infeasible. Default true.",
                GH_ParamAccess.item, true);
            p[5].Optional = true;
            p.AddIntegerParameter("Max Steps", "Max",
                "Cap on steps to evaluate. -1 = no cap (default).",
                GH_ParamAccess.item, -1);
            p[6].Optional = true;
            p.AddBooleanParameter("Run", "R",
                "Set true to run.",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("First Unstable Step", "Step*",
                "0-based index of the first infeasible step. -1 if every " +
                "evaluated step was stable.",
                GH_ParamAccess.item);
            p.AddTextParameter("First Unstable Block Id", "Id*",
                "Block id placed at the first unstable step. Empty when " +
                "all evaluated steps are stable.",
                GH_ParamAccess.item);
            p.AddTextParameter("Verdict Per Step", "V",
                "Per-step verdict (parallel to Ordered Block Ids up to the " +
                "evaluated count).",
                GH_ParamAccess.list);
            p.AddNumberParameter("Objective Per Step", "Obj",
                "Per-step QP objective value.",
                GH_ParamAccess.list);
            p.AddTextParameter("Report", "R",
                "Multi-line summary log.",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            object rawAssembly = null;
            var ids = new List<string>();
            double mu = 0.84;
            int faces = 4;
            bool penalty = false;
            bool stopOnFirst = true;
            int maxSteps = -1;
            bool run = false;

            if (!da.GetData(0, ref rawAssembly) || rawAssembly == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No assembly provided.");
                return;
            }
            if (!da.GetDataList(1, ids) || ids.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No ordered block ids provided.");
                return;
            }
            da.GetData(2, ref mu);
            da.GetData(3, ref faces);
            da.GetData(4, ref penalty);
            da.GetData(5, ref stopOnFirst);
            da.GetData(6, ref maxSteps);
            da.GetData(7, ref run);

            var assembly = UnwrapAssembly(rawAssembly);
            if (assembly == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Assembly is not a MasonryAssembly (got {GhInterop.DescribeType(rawAssembly)}).");
                return;
            }

            if (!run)
            {
                da.SetData(0, -1);
                da.SetData(1, "");
                da.SetData(4, "Run is false.");
                return;
            }

            // Defensive: register the managed QP solver if the plugin OnLoad
            // path (StonePackPlugin -> EnsureDefaultSolver) has not run, e.g.
            // when only the .gha is loaded. Idempotent; matches the pattern in
            // MasonryStabilityRbeComponent.
            MasonrySolverRegistry.UseOsqpIfAvailable();
            var solver = MasonrySolverRegistry.Default;
            if (solver == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No IConvexQpSolver registered (MasonrySolverRegistry.Default).");
                da.SetData(0, -1);
                da.SetData(1, "");
                da.SetData(4, "No solver registered.");
                return;
            }

            int totalSteps = ids.Count;
            if (maxSteps > 0 && maxSteps < totalSteps) totalSteps = maxSteps;

            var verdicts = new List<string>(totalSteps);
            var objectives = new List<double>(totalSteps);
            int firstUnstable = -1;
            string firstUnstableId = "";
            var log = new StringBuilder();
            log.AppendLine($"Streaming {totalSteps} step(s); solver={solver.Name}; " +
                $"mu={mu}; faces={faces}; penalty={penalty}; stopOnFirst={stopOnFirst}.");

            for (int step = 0; step < totalSteps; step++)
            {
                MasonryAssembly partial;
                try
                {
                    partial = BuildOrderPartialAssembler.BuildPartial(assembly, ids, step);
                }
                catch (Exception ex)
                {
                    verdicts.Add($"build error: {ex.Message}");
                    objectives.Add(double.NaN);
                    log.AppendLine($"step {step} ({ids[step]}): build error — {ex.Message}");
                    if (firstUnstable < 0) { firstUnstable = step; firstUnstableId = ids[step]; }
                    if (stopOnFirst) break;
                    continue;
                }

                string verdict;
                double obj = double.NaN;
                try
                {
                    var equilibrium = EquilibriumMatrixBuilder.Build(partial, penalty: penalty);
                    var friction = FrictionConeBuilder.Build(equilibrium, mu: mu, faceCount: faces);
                    // BuildPhysicsCorrected, not Build: the legacy Build makes the
                    // f_n >= 0 bound infeasible (sign convention bug). See
                    // MasonryStabilityRbeComponent + StageBSolverTests. Fixed 2026-06-05 (W4).
                    var problem = RbeQpFormulation.BuildPhysicsCorrected(equilibrium, friction.Afr);
                    var qp = solver.Solve(problem);
                    // A solver DECLINING (ManagedQpSolver NotImplemented on a
                    // non-diagonal Hessian) is not a structural verdict — retry
                    // on the managed ADMM, same as the checker's fallback chain.
                    if (qp != null && qp.Status == ConvexQpStatus.NotImplemented)
                    {
                        try { qp = new AdmmQpSolver(epsAbs: 1e-4, epsRel: 1e-4).Solve(problem) ?? qp; }
                        catch { /* keep the declined result; mapped below */ }
                    }
                    if (qp == null) { verdict = "error"; }
                    else
                    {
                        obj = qp.ObjectiveValue;
                        switch (qp.Status)
                        {
                            case ConvexQpStatus.Optimal:       verdict = "stable"; break;
                            case ConvexQpStatus.Infeasible:    verdict = "infeasible"; break;
                            case ConvexQpStatus.Unbounded:     verdict = "unbounded"; break;
                            case ConvexQpStatus.NotImplemented: verdict = "solver declined (no fallback available)"; break;
                            default:                            verdict = "error"; break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    verdict = $"error: {ex.GetType().Name}";
                }

                verdicts.Add(verdict);
                objectives.Add(obj);
                log.AppendLine($"step {step} ({ids[step]}): {verdict} (obj={obj:G4})");

                if (verdict != "stable" && firstUnstable < 0)
                {
                    firstUnstable = step;
                    firstUnstableId = ids[step];
                    if (stopOnFirst) { log.AppendLine("stop-on-first triggered."); break; }
                }
            }

            da.SetData(0, firstUnstable);
            da.SetData(1, firstUnstableId);
            da.SetDataList(2, verdicts);
            da.SetDataList(3, objectives);
            da.SetData(4, log.ToString().TrimEnd());
        }

        private static MasonryAssembly UnwrapAssembly(object raw)
        {
            if (raw is MasonryAssembly direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is MasonryAssembly fromWrap)
                return fromWrap;
            return null;
        }
    }
}
