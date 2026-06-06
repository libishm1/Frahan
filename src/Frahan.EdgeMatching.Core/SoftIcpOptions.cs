#nullable disable
namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Tuning for <see cref="SoftIcpRefiner"/>. Every length / temperature
    /// parameter is SCALE-RELATIVE (expressed as a factor of the median rim-sample
    /// spacing or the object bbox diagonal) per the cross-cutting scale rule in
    /// <c>wiki/research/differentiable_edge_matching.md §2</c>, so the same loss
    /// behaves identically on cm fragments and metre blocks. Defaults are tuned
    /// for the synthetic ~100u normalised fixtures.
    /// </summary>
    public sealed class SoftIcpOptions
    {
        /// <summary>
        /// Initial CPD temperature tau0 = <c>Tau0Factor * (median rim spacing)^2</c>.
        /// Larger = softer / wider correspondence at the start. Default 4 (tau0 =
        /// 4 spacings^2, so a sample sees neighbours within ~2 spacings strongly).
        /// </summary>
        public double Tau0Factor { get; set; } = 4.0;

        /// <summary>
        /// Geometric anneal factor applied to tau each outer iteration (0,1).
        /// Default 0.8 (tau shrinks ~20% per iter, sharpening the correspondence
        /// toward true contact as the pose settles).
        /// </summary>
        public double TauAnneal { get; set; } = 0.8;

        /// <summary>
        /// Floor for the annealed temperature, <c>TauFloorFactor * spacing^2</c>,
        /// so tau never collapses to a hard nearest-neighbour (keeps the loss
        /// smooth). Default 0.25.
        /// </summary>
        public double TauFloorFactor { get; set; } = 0.25;

        /// <summary>
        /// Uniform-outlier pseudo-weight in the softmax denominator (Myronenko-
        /// Song). A sample with no neighbour closer than ~sqrt(tau) gets most of
        /// its mass here and so a low confidence, auto-downweighting non-
        /// overlapping rim tails. Default 0.01.
        /// </summary>
        public double OutlierWeight { get; set; } = 0.01;

        /// <summary>
        /// Correspondence radius for the contact term, <c>CorrespondenceRadiusFactor *
        /// (median rim spacing)</c>. Neighbour rim samples beyond this distance
        /// contribute ZERO weight, so only the genuine MATING interface (rim points
        /// actually near another piece) drives the alignment; a piece's far-side
        /// boundary, which has no near neighbour, is ignored instead of being
        /// dragged toward a distant piece. This is the robust-ICP correspondence
        /// radius and is what makes the term "rims touch" rather than "pieces
        /// clump". Default 3.0 (3 sample spacings). Set 0 for no cutoff.
        /// </summary>
        public double CorrespondenceRadiusFactor { get; set; } = 3.0;

        /// <summary>
        /// Confidence floor: rim samples whose matched-mass fraction is below this
        /// are dropped from the weighted Kabsch (treated as outliers). Default 0.1.
        /// </summary>
        public double MinConfidence { get; set; } = 0.1;

        /// <summary>
        /// Weight w_contact of the contact term in L = w_contact*SoftRimSSD +
        /// w_pen*PenetrationHinge. The EM weighted-Kabsch M-step realises the full
        /// contact alignment, so in the EM path this is a documentation / future-
        /// LBFGS knob (the closed-form step is already the contact optimum). Default 1.0.
        /// </summary>
        public double ContactWeight { get; set; } = 1.0;

        /// <summary>
        /// Hinge weight w_pen / lambda for the non-penetration term
        /// lambda*max(0,depth)^2. In the EM path the hinge is realised by
        /// redirecting penetrating samples' targets to the surface (depth -> 0), so
        /// this is a documentation / future-LBFGS knob. Default 1.0.
        /// </summary>
        public double PenetrationWeight { get; set; } = 1.0;

        /// <summary>
        /// Hinge-gradient step size in (0,1], reserved for an explicit
        /// separating-translation penetration path (e.g. a future LBFGS or
        /// gradient optimiser). The default EM path does NOT use a separate push:
        /// it folds non-penetration into the contact solve by redirecting any
        /// penetrating rim sample's weighted-Kabsch target to the neighbour
        /// SURFACE (depth -> 0 without overshoot), which is unconditionally stable.
        /// Default 0.5 (mirror SettleContactComponent relax).
        /// </summary>
        public double PenetrationStep { get; set; } = 0.5;

        /// <summary>
        /// Damping on the contact (weighted-Kabsch) increment per EM iteration, in
        /// (0,1]. The full closed-form Kabsch step can overshoot near rim ends
        /// (one-sided soft targets), so the increment is taken FRACTIONALLY: the
        /// rotation angle and translation are scaled by this factor before the
        /// retraction (standard ICP relaxation, keeps the alternation stable and
        /// monotone on genuine mating rims). Default 0.5. Set 1.0 for the raw
        /// closed-form step.
        /// </summary>
        public double ContactStep { get; set; } = 0.5;

        /// <summary>
        /// Penetration ignore tolerance, <c>PenetrationTolFactor * objectScale</c>.
        /// Penetration shallower than this is treated as touching (no push) and is
        /// not reported. Default 0.002 (0.2% of the assembly diagonal).
        /// </summary>
        public double PenetrationTolFactor { get; set; } = 0.002;

        /// <summary>
        /// Cap on rim samples tested for the penetration inside-test per pair
        /// (stride-decimated) so large rims stay fast. Default 200.
        /// </summary>
        public int PenetrationSampleCap { get; set; } = 200;

        /// <summary>Max outer EM iterations. Default 40.</summary>
        public int MaxIterations { get; set; } = 40;

        /// <summary>
        /// Convergence translation gate, <c>ConvergeTransFactor * objectScale</c>.
        /// When every fragment's per-iteration increment is below this AND
        /// <see cref="ConvergeRotDeg"/>, the loop stops. Default 1e-4.
        /// </summary>
        public double ConvergeTransFactor { get; set; } = 1e-4;

        /// <summary>Convergence rotation gate in degrees. Default 1e-3.</summary>
        public double ConvergeRotDeg { get; set; } = 1e-3;

        /// <summary>
        /// Contact-band for the diagnostic contact-sample count,
        /// <c>ContactBandFactor * (median rim spacing)</c>. A rim sample whose
        /// nearest neighbour rim is within this is counted as "in contact".
        /// Default 0.6 (a bit over half a sample spacing, so independently-sampled
        /// coincident rims still register as in contact). Measurement only.
        /// </summary>
        public double ContactBandFactor { get; set; } = 0.6;

        // ====================================================================
        // Optimiser-strategy options (gradient-descent path, added 2026-05-31)
        // ====================================================================
        // Per Libish 2026-05-31 directive: "use gradient descent to optimise
        // the Soft ICP match to reduce wastage and tune the algorithm
        // accordingly." Per wiki/research/soft_icp_gradient/lbfgs_plan.md
        // research dossier: primary recommendation is L-BFGS via
        // MathNet.Numerics.Optimization.LimitedMemoryBfgsMinimizer (zero new
        // dependencies; MathNet 4.15 is already shipped). Fallback is
        // LevenbergMarquardtMinimizer (same package).
        //
        // SCAFFOLDING ONLY: the enum + 6 fields below are stable API hooks
        // for the upcoming RefineLbfgs3D method (task #60 v1.x build). The
        // default Strategy=EmAlternation preserves byte-stable back-compat;
        // until the LBFGS body lands, callers that select Strategy=LBFGS
        // get a runtime warning + fallback to EM.

        /// <summary>
        /// Optimiser strategy. Default <see cref="SoftIcpStrategy.EmAlternation"/>
        /// (the closed-form weighted-Kabsch path that <see cref="SoftIcpRefiner.Refine3D"/>
        /// already runs). The other values request the gradient-descent paths
        /// scheduled for the v1.x build per
        /// <c>wiki/research/soft_icp_gradient/lbfgs_plan.md</c>.
        /// </summary>
        public SoftIcpStrategy Strategy { get; set; } = SoftIcpStrategy.EmAlternation;

        /// <summary>
        /// L-BFGS history length (number of past gradient/parameter pairs
        /// kept for the inverse-Hessian approximation). Default 10
        /// (MathNet.Numerics default; appropriate for SE(3)^N tangent
        /// dimensions ≲ 60).
        /// </summary>
        public int LbfgsMemory { get; set; } = 10;

        /// <summary>L-BFGS gradient-norm convergence threshold. Default 1e-5.</summary>
        public double LbfgsGradTol { get; set; } = 1e-5;

        /// <summary>L-BFGS parameter-step-norm convergence threshold. Default 1e-7.</summary>
        public double LbfgsStepTol { get; set; } = 1e-7;

        /// <summary>L-BFGS function-value-change convergence threshold. Default 1e-9.</summary>
        public double LbfgsFuncTol { get; set; } = 1e-9;

        /// <summary>
        /// Number of random-restart escapes when the optimiser plateaus.
        /// Default 3. Restart perturbation: Gaussian σ = 0.01 × objectScale
        /// on translation, σ = 1° on rotation. Mitigates the local-minimum
        /// trap documented in Myronenko-Song 2010 §III.
        /// </summary>
        public int LbfgsRestarts { get; set; } = 3;

        /// <summary>
        /// Huber-knee parameter for the penetration hinge, in fraction of
        /// median rim spacing. Default 0.5 (knee at half a spacing). Mandatory
        /// for L-BFGS curvature-condition stability (the raw
        /// <c>max(0, depth)^2</c> hinge has a sub-gradient at d=0 that breaks
        /// BFGS). Ignored on the EM path.
        /// </summary>
        public double HuberPenetration { get; set; } = 0.5;
    }

    /// <summary>
    /// Optimiser strategy for <see cref="SoftIcpRefiner"/>. Selects between
    /// the existing closed-form EM weighted-Kabsch alternation and the new
    /// gradient-descent paths added per Libish 2026-05-31 directive
    /// (research dossier: <c>wiki/research/soft_icp_gradient/lbfgs_plan.md</c>).
    /// </summary>
    public enum SoftIcpStrategy
    {
        /// <summary>EM weighted-Kabsch alternation (default; closed-form M-step).</summary>
        EmAlternation = 0,

        /// <summary>L-BFGS quasi-Newton (primary gradient-descent path per dossier).
        /// MathNet.Numerics.Optimization.LimitedMemoryBfgsMinimizer; ~10-element
        /// history; warm-restarted at each tau-anneal level. Task #60.</summary>
        LBFGS = 1,

        /// <summary>Levenberg-Marquardt damped Gauss-Newton (fallback per dossier).
        /// MathNet.Numerics.Optimization.LevenbergMarquardtMinimizer. Fits the
        /// weighted-LS SoftRimSSD term perfectly; Huber-hinge folds as squared
        /// residual. Task #60 fallback.</summary>
        LM = 2,

        /// <summary>Adam adaptive-moment estimation. Listed for completeness;
        /// the dossier recommends AGAINST for this small smooth problem
        /// (slow + non-deterministic). Reserved for batched-3D variants
        /// (Kintsugi.Port path, not Frahan core). Task #60 stretch.</summary>
        Adam = 3,
    }
}
