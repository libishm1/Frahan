#nullable disable
using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.Solvers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MasonryStabilityRbeComponent — async (GH_TaskCapableComponent) wrapper
    // around the Rigid-Block Equilibrium QP pipeline:
    //
    //     EquilibriumMatrixBuilder.Build(...)
    //         -> EquilibriumSystem
    //     FrictionConeBuilder.Build(equilibrium, mu, faces)
    //         -> FrictionConeMatrix
    //     RbeQpFormulation.BuildPhysicsCorrected(equilibrium, friction.Afr)
    //         -> ConvexQpProblem
    //         (NOT the legacy Build: that overload's sign convention makes
    //         f_n >= 0 infeasible for any real assembly; BuildPhysicsCorrected
    //         flips the sign so f_n >= 0 means compression. The legacy Build
    //         survives only in tests. KB-3 resolved 2026-06-11.)
    //     MasonrySolverRegistry.Default.Solve(problem)
    //         -> ConvexQpResult
    //
    // Solver lookup (updated 2026-06-11): MasonrySolverRegistry resolves to
    // the managed AdmmQpSolver (OSQP-style ADMM; see its header for the
    // ~50-interface conditioning ceiling) wired in plugin OnLoad; the IPOPT
    // P/Invoke seam remains an honest stub (IpoptManagedStub). If no solver
    // is registered the component reports Verdict = "no solver registered".
    //
    // ComponentGuid: F6BAC3D4-4E5F-4071-BC3D-5E6F7A8B9CAD
    //
    // Per project memory: GH_TaskCapableComponent<T> requires T to be public
    // (T must be at least as accessible as the derived component class).
    // MasonryStabilityRbeResult is therefore declared `public sealed class`
    // ABOVE the component class.
    // =========================================================================

    /// <summary>
    /// Result of a single RBE solve, carried across the GH async dispatch
    /// boundary. Public per the GH_TaskCapableComponent accessibility rule.
    /// </summary>
    public sealed class MasonryStabilityRbeResult
    {
        public string Verdict;        // "stable" | "infeasible" | "no solver registered" | "error" | etc.
        public double Objective;      // ½ x^T H x + c^T x at the optimum (NaN when not Optimal)
        public double ResidualNorm;   // ||Aeq x - beq||_2 at the returned x (0 by construction when Optimal)
        public string SolverName;     // e.g. "ManagedQpSolver", "IpoptNlpSolver", or "(none)"
        public string Report;         // free-form human-readable diagnostic
        public string ErrorMessage;   // non-null when SolveInstance should surface a runtime Error
    }

    /// <summary>
    /// Frahan &gt; Masonry &gt; Masonry Stability (RBE).
    /// Convex-QP stability check using the Rigid-Block Equilibrium formulation
    /// (Kao et al. 2022, CAD vol 146 art 103216; ported from
    /// BlockResearchGroup/compas_cra, MIT). Async; runs the assemble + solve
    /// on a pool thread so Grasshopper stays responsive.
    /// </summary>
    [RelatedComponent("Frahan > Masonry > Masonry Stability Check",
        Reason = "Certification path: CRA (Kao 2022) rejects self-stressed states that RBE accepts (H-model); certify via Masonry Stability Check + CRA.")]
    [Algorithm("Rigid-Block Equilibrium QP", "Kao et al. 2022, Computer-Aided Design 146:103216 Coupled Rigid-Block Analysis", Doi = "10.1016/j.cad.2022.103216", WikiPath = "wiki/algorithms/masonry/rbe_kao_2022.md")]
    [Algorithm("Coulomb friction cone", "Kao 2022 section 4 contact mechanics formulation")]
    [Algorithm("Convex QP managed solver", "Kao 2022 section 5 + MIT BlockResearchGroup compas_cra reference impl", Note = "ManagedQpSolver wrapped via MasonrySolverRegistry; IpoptManagedStub for nonlinear extension")]
    public sealed class MasonryStabilityRbeComponent
        : GH_TaskCapableComponent<MasonryStabilityRbeResult>
    {
        public MasonryStabilityRbeComponent()
            : base(
                "Masonry Stability (RBE)", "MasRBE",
                "Convex-QP rigid-block-equilibrium stability check for a MasonryAssembly. " +
                "NOTE: RBE is the permissive check; CRA (Kao 2022) rejects self-stressed " +
                "states RBE accepts (H-model). Certify via Masonry Stability Check + CRA. " +
                "Asynchronous: assembles the equilibrium + friction QP and solves on a " +
                "pool thread.",
                "Frahan", "Masonry")
        {
        }

        // GUID literal: F6BAC3D4-4E5F-4071-BC3D-5E6F7A8B9CAD
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override Guid ComponentGuid =>
            new Guid("F6BAC3D4-4E5F-4071-BC3D-5E6F7A8B9CAD");

        protected override Bitmap Icon => IconProvider.Load("EquilibriumRBE.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly from Masonry Assembly.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Mu", "Mu",
                "Coulomb friction coefficient (default 0.84, ~40 deg).",
                GH_ParamAccess.item, 0.84);
            p.AddIntegerParameter("Faces", "F",
                "Number of pyramidal faces used to linearise the friction cone " +
                "(>= 3, default 4).",
                GH_ParamAccess.item, 4);
            p.AddBooleanParameter("Penalty", "P",
                "If true, use the penalty form (split normals f_n+/f_n-) for the " +
                "equilibrium matrix.",
                GH_ParamAccess.item, false);
            p.AddBooleanParameter("Run", "R",
                "Set true to execute the solve.",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Verdict", "V",
                "Short verdict: 'stable', 'infeasible', 'no solver registered', or an " +
                "error class.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Objective", "O",
                "QP objective value at the returned x (NaN when not Optimal).",
                GH_ParamAccess.item);
            p.AddNumberParameter("ResidualNorm", "R",
                "L2 norm of the equilibrium residual ||Aeq x - beq||.",
                GH_ParamAccess.item);
            p.AddTextParameter("SolverName", "S",
                "Identifier of the solver used (e.g. 'ManagedQpSolver').",
                GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rpt",
                "Human-readable diagnostic.",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (InPreSolve)
            {
                // Capture inputs on the GH thread, then queue the work.
                object rawAssembly = null;
                double mu = 0.84;
                int faces = 4;
                bool penalty = false;
                bool run = false;

                if (!da.GetData(0, ref rawAssembly) || rawAssembly == null)
                {
                    da.SetData(0, "no assembly");
                    da.SetData(1, double.NaN);
                    da.SetData(2, double.NaN);
                    da.SetData(3, "(none)");
                    da.SetData(4, "No assembly provided.");
                    return;
                }
                da.GetData(1, ref mu);
                da.GetData(2, ref faces);
                da.GetData(3, ref penalty);
                da.GetData(4, ref run);

                if (!run)
                {
                    da.SetData(0, "not run");
                    da.SetData(1, double.NaN);
                    da.SetData(2, double.NaN);
                    da.SetData(3, "(none)");
                    da.SetData(4, "Run is false.");
                    return;
                }

                var assembly = UnwrapAssembly(rawAssembly);
                if (assembly == null)
                {
                    da.SetData(0, "bad assembly");
                    da.SetData(1, double.NaN);
                    da.SetData(2, double.NaN);
                    da.SetData(3, "(none)");
                    da.SetData(4, $"Assembly is not a MasonryAssembly (got " +
                        $"{(rawAssembly == null ? "null" : rawAssembly.GetType().FullName)}).");
                    return;
                }

                // The DTOs are immutable, so handing them across thread
                // boundaries is safe without a deep clone. Pull the registry
                // value once on the GH thread to avoid a torn read later.
                // 2026-05-15: defensively ensure a default solver is wired
                // even when only the .gha is loaded (StonePackPlugin.OnLoad
                // wires this, but the .gha can be used without the .rhp).
                MasonrySolverRegistry.EnsureDefaultSolver();
                var solver = MasonrySolverRegistry.Default;
                double muCopy = mu;
                int facesCopy = faces;
                bool penaltyCopy = penalty;

                TaskList.Add(Task.Run(() =>
                    Compute(assembly, muCopy, facesCopy, penaltyCopy, solver)));
                return;
            }

            // ── Post-solve: emit results ───────────────────────────────────
            MasonryStabilityRbeResult result;
            if (!GetSolveResults(da, out result))
            {
                // Synchronous fallback: GH called us without a queued task.
                object rawAssembly2 = null;
                double mu2 = 0.84;
                int faces2 = 4;
                bool penalty2 = false;
                bool run2 = false;

                if (!da.GetData(0, ref rawAssembly2) || rawAssembly2 == null)
                {
                    EmitVerdictOnly(da, "no assembly", "No assembly provided.");
                    return;
                }
                da.GetData(1, ref mu2);
                da.GetData(2, ref faces2);
                da.GetData(3, ref penalty2);
                da.GetData(4, ref run2);

                if (!run2)
                {
                    EmitVerdictOnly(da, "not run", "Run is false.");
                    return;
                }

                var assembly2 = UnwrapAssembly(rawAssembly2);
                if (assembly2 == null)
                {
                    EmitVerdictOnly(da, "bad assembly",
                        $"Assembly is not a MasonryAssembly (got " +
                        $"{(rawAssembly2 == null ? "null" : rawAssembly2.GetType().FullName)}).");
                    return;
                }

                MasonrySolverRegistry.EnsureDefaultSolver();
                result = Compute(assembly2, mu2, faces2, penalty2,
                    MasonrySolverRegistry.Default);
            }

            if (result == null)
            {
                EmitVerdictOnly(da, "error", "Solver returned a null result.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Solver returned a null result.");
                return;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result.ErrorMessage);

            da.SetData(0, result.Verdict ?? "(unknown)");
            da.SetData(1, result.Objective);
            da.SetData(2, result.ResidualNorm);
            da.SetData(3, result.SolverName ?? "(none)");
            da.SetData(4, result.Report ?? string.Empty);
        }

        // ─── Worker ─────────────────────────────────────────────────────────

        private static MasonryStabilityRbeResult Compute(
            MasonryAssembly assembly,
            double mu,
            int faces,
            bool penalty,
            IConvexQpSolver solver)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var log = new StringBuilder();
            var result = new MasonryStabilityRbeResult
            {
                Objective = double.NaN,
                ResidualNorm = double.NaN,
                SolverName = solver != null ? solver.Name : "(none)",
            };

            try
            {
                // 1) Equilibrium system.
                var equilibrium = EquilibriumMatrixBuilder.Build(
                    assembly, penalty: penalty);
                log.AppendLine(
                    $"Equilibrium: rows={equilibrium.Aeq.RowCount}, " +
                    $"cols={equilibrium.Aeq.ColCount}, " +
                    $"free_blocks={equilibrium.FreeBlockIds.Count}, " +
                    $"shift={equilibrium.ForceComponentsPerVertex}.");

                // 2) Friction cone.
                var friction = FrictionConeBuilder.Build(
                    equilibrium, mu: mu, faceCount: faces);
                log.AppendLine(
                    $"Friction: rows={friction.Afr.RowCount}, " +
                    $"faces={friction.FaceCount}, mu={friction.Mu}.");

                // 3) QP problem. Use BuildPhysicsCorrected, NOT Build: the
                // original Build sets equalityRhs = -b which (with the builder's
                // Aeq[F_z] = -1 and b[F_z] = -m*g) yields f_n = -m*g, making the
                // f_n >= 0 bound INFEASIBLE for any real assembly (verified; the
                // legacy Build is kept only for sign-pinning unit tests).
                // BuildPhysicsCorrected flips the sign so f_n >= 0 means
                // compression. StageBSolverTests.EndToEnd_* confirm it returns
                // Optimal on a real packed stack. Fixed 2026-06-05 (W4).
                var problem = RbeQpFormulation.BuildPhysicsCorrected(equilibrium, friction.Afr);
                log.AppendLine(
                    $"QP: n={problem.VariableCount}, " +
                    $"meq={problem.EqualityRowCount}, " +
                    $"mineq={problem.InequalityRowCount}.");

                // 4) Solver lookup. Phase B (2026-05-15): wired in
                // StonePackPlugin.OnLoad via MasonrySolverRegistry.EnsureDefaultSolver,
                // and again defensively in SolveInstance for .gha-only loads.
                // The null path here is now strictly the "someone reset the
                // registry to null" case (e.g. a test that nulled it out).
                if (solver == null)
                {
                    result.Verdict = "no solver registered";
                    result.SolverName = "(none)";
                    result.Report = log.AppendLine(
                        "No IConvexQpSolver registered. Call " +
                        "MasonrySolverRegistry.EnsureDefaultSolver() before " +
                        "solving.").ToString().TrimEnd();
                    return result;
                }

                // 5) Solve.
                ConvexQpResult qpResult;
                try
                {
                    qpResult = solver.Solve(problem);
                }
                catch (Exception solverEx)
                {
                    result.Verdict = "error";
                    result.Report = log.AppendLine(
                        $"Solver '{solver.Name}' threw: " +
                        $"{solverEx.GetType().Name}: {solverEx.Message}").ToString().TrimEnd();
                    return result;
                }

                if (qpResult == null)
                {
                    result.Verdict = "error";
                    result.Report = log.AppendLine(
                        $"Solver '{solver.Name}' returned null.").ToString().TrimEnd();
                    return result;
                }

                result.Objective = qpResult.ObjectiveValue;
                log.AppendLine(
                    $"Solver: {solver.Name}, status={qpResult.Status}, " +
                    $"obj={qpResult.ObjectiveValue:G6}, " +
                    $"elapsed={sw.ElapsedMilliseconds} ms.");

                if (!string.IsNullOrEmpty(qpResult.SolverMessage))
                    log.AppendLine($"  message: {qpResult.SolverMessage}");

                switch (qpResult.Status)
                {
                    case ConvexQpStatus.Optimal:
                        result.Verdict = "stable";
                        result.ResidualNorm = ComputeResidualNorm(problem, qpResult.X);
                        log.AppendLine(
                            $"Residual ||Aeq x - beq|| = {result.ResidualNorm:G6}.");
                        break;
                    case ConvexQpStatus.Infeasible:
                        result.Verdict = "infeasible";
                        break;
                    case ConvexQpStatus.Unbounded:
                        result.Verdict = "unbounded";
                        break;
                    case ConvexQpStatus.NotImplemented:
                        result.Verdict = "not implemented";
                        break;
                    case ConvexQpStatus.SolverError:
                    default:
                        result.Verdict = "error";
                        break;
                }

                result.Report = log.ToString().TrimEnd();
                return result;
            }
            catch (Exception ex)
            {
                result.Verdict = "error";
                result.ErrorMessage =
                    $"RBE pipeline failed: {ex.GetType().Name}: {ex.Message}";
                log.AppendLine(result.ErrorMessage);
                result.Report = log.ToString().TrimEnd();
                return result;
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static MasonryAssembly UnwrapAssembly(object raw)
        {
            if (raw is MasonryAssembly direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is MasonryAssembly fromWrap)
                return fromWrap;
            return null;
        }

        private static void EmitVerdictOnly(IGH_DataAccess da, string verdict, string report)
        {
            da.SetData(0, verdict);
            da.SetData(1, double.NaN);
            da.SetData(2, double.NaN);
            da.SetData(3, "(none)");
            da.SetData(4, report);
        }

        /// <summary>
        /// Computes the L2 norm of <c>Aeq x - beq</c> at the supplied x. Used
        /// to surface a sanity-check residual to the user; for an Optimal QP
        /// solution this should be near zero by construction.
        /// </summary>
        private static double ComputeResidualNorm(ConvexQpProblem problem, double[] x)
        {
            if (x == null || problem.EqualityMatrix == null) return 0.0;
            int meq = problem.EqualityMatrix.GetLength(0);
            int n = problem.EqualityMatrix.GetLength(1);
            double sumSq = 0.0;
            for (int i = 0; i < meq; i++)
            {
                double row = -problem.EqualityRhs[i];
                for (int j = 0; j < n; j++)
                {
                    row += problem.EqualityMatrix[i, j] * x[j];
                }
                sumSq += row * row;
            }
            return Math.Sqrt(sumSq);
        }
    }
}
