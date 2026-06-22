using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;

namespace Frahan.GH;

/// <summary>
/// Bundles the EdgeMatch solver's ADVANCED AssemblyOptions flags into a
/// single AssemblyOptions DTO. Wire the "Options" output into EdgeMatch
/// Solve's optional "Opt" input to override the advanced fields while the
/// solver's own simple inputs keep owning the basic fields (Residual
/// Threshold, Beam Width, Max Iterations, Non-Crossing, sample spacing,
/// break angle, min segment length).
///
/// Every input is OPTIONAL and defaults to the
/// <see cref="Frahan.EdgeMatching.AssemblyOptions"/> Core default, so an
/// untouched component emits a default AssemblyOptions == today's behaviour.
///
/// PROJECTION BOOTSTRAP is UNVERIFIED work-in-progress (defaults OFF). It
/// only takes effect on the agglomerative 3D path and is gated by its own
/// HITL review before any visual-correctness claim.
///
/// DTO is passed by the repo's established convention: emitted on a
/// GenericParameter and read back through GH_ObjectWrapper on the consumer
/// (mirrors Ashlar Pack Options -> Ashlar Pack and Surface Chart -> Pack On
/// Surface), so no custom GH_Goo / Param_ wrapper is introduced.
/// </summary>
[Algorithm("Beam-search assembly solver", "Frahan-original deterministic beam search with state cloning", Note = "These options tune Stages 4-5 + post-solve polish of the EdgeMatch pipeline")]
[Algorithm("Agglomerative pair-graph assembly", "Frahan-original minimum-residual spanning tree", Note = "Mode=Agglomerative; pairwise 3D reassembly path")]
[DesignApplication(
    "Tune the EdgeMatch solver's advanced behaviour (assembly mode, scale gates, Soft-ICP refine, projection bootstrap).",
    DesignFlow.Bridges,
    Precedent = "DTO bundle shared across the EdgeMatch family (Solve + Trencadis + future Components A/B/C/D)",
    Tolerance = "no numeric pass criterion -- options DTO only",
    CardSet = "wiki/research/hitl_cards/em_2d_trencadis_solve/ (the primary consumer)")]
[RelatedComponent("Frahan > EdgeMatch > EdgeMatch Solve",
    Reason = "Consumes this Options DTO on its optional Opt input to override the advanced fields",
    ComponentGuid = "D5F10001-ED9E-4ED9-A001-ED9EED9E0001")]
[RelatedComponent("Frahan > Kintsugi > Kintsugi",
    Reason = "3D fragment reassembly that shares the agglomerative + Soft-ICP refine machinery these knobs tune")]
[RelatedComponent("Frahan > EdgeMatch > Trencadis EdgeMatch",
    Reason = "2D Trencadís edge-matching that runs the same FrameAnchored beam these options tune")]
public sealed class EdgeMatchOptionsComponent : FrahanComponentBase
{
    public EdgeMatchOptionsComponent()
        : base("EdgeMatch Options", "EMOpts",
            "Bundle the EdgeMatch solver's advanced AssemblyOptions flags " +
            "(assembly mode, scale-relative gates, partial sub-segment " +
            "matching, overlap resolve, Soft-ICP rim-contact refine, and the " +
            "WIP projection bootstrap) into one AssemblyOptions DTO. Wire into " +
            "EdgeMatch Solve's optional Opt input. Every input is optional and " +
            "defaults to the Core default, so an empty component emits the " +
            "default options (unchanged behaviour).",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10003-ED9E-4ED9-A003-ED9EED9E0003");
    protected override Bitmap? Icon => IconProvider.Load("EdgeMatchOptions.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    // Input indices. Documented so the round-trip read in SolveInstance stays
    // aligned with RegisterInputParams.
    private const int IAgglomerative = 0;
    private const int INonCrossingMaxGap = 1;
    private const int IPhaseScoreThreshold = 2;
    private const int IResidualThresholdFactor = 3;
    private const int IEmitPartials = 4;
    private const int IPartialFractions = 5;
    private const int IPartialStrideFraction = 6;
    private const int IOverlapPenalty = 7;
    private const int IEdgeExclusivity = 8;
    private const int IResolveOverlap = 9;
    private const int IResolveOverlapTolerance = 10;
    private const int IResolveOverlapIterations = 11;
    private const int IResolveOverlapRelaxation = 12;
    private const int ISoftIcpRefine = 13;
    private const int ISoftIcpTau0Factor = 14;
    private const int ISoftIcpTauAnneal = 15;
    private const int ISoftIcpCorrRadiusFactor = 16;
    private const int ISoftIcpContactWeight = 17;
    private const int ISoftIcpPenetrationWeight = 18;
    private const int ISoftIcpMaxIterations = 19;
    private const int IProjectionBootstrap = 20;
    private const int IProjectionSampleSpacingFactor = 21;
    private const int IProjectionPlanarityFactor = 22;
    private const int IProjectionVerifyFactor = 23;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        // Core defaults are referenced from a fresh AssemblyOptions / SoftIcpOptions
        // so the GH defaults stay in lockstep with the source of truth.
        var d = new AssemblyOptions();
        var sd = d.SoftIcp;

        // --- Mode ---------------------------------------------------------
        p.AddBooleanParameter("Agglomerative", "Ag",
            "Assembly mode. FALSE (default) = FrameAnchored beam (2D Trencadís; " +
            "every existing canvas + test path). TRUE = Agglomerative pairwise " +
            "spanning-tree assembly for free 3D fragment reassembly.",
            GH_ParamAccess.item, d.Mode == AssemblyMode.Agglomerative);

        // --- Scale-relative gates (A1) ------------------------------------
        p.AddIntegerParameter("Non-Crossing Max Gap", "Ng",
            "Index-band bound for the monotone non-crossing DP. 0 (default) = " +
            "unbounded. Only consulted when Non-Crossing is on at the solver.",
            GH_ParamAccess.item, d.NonCrossingMaxGap);
        p.AddNumberParameter("Phase Score Threshold", "Ps",
            "Minimum phase-correlator similarity to accept a candidate pair. " +
            "Default 0.5 (the original hardcoded gate).",
            GH_ParamAccess.item, d.PhaseScoreThreshold);
        p.AddNumberParameter("Residual Threshold Factor", "Rf",
            "When > 0 the residual gate becomes factor * objectScale (bbox " +
            "diagonal), making acceptance scale-relative. 0 (default) keeps the " +
            "absolute Residual Threshold from the solver. Suggested ~0.01.",
            GH_ParamAccess.item, d.ResidualThresholdFactor);

        // --- Partial sub-segment matching (R1) ----------------------------
        p.AddBooleanParameter("Emit Partials", "Ep",
            "When TRUE the segmenters also emit shorter sub-windows so a long " +
            "edge can mate a short complementary edge. FALSE (default) = " +
            "candidate generation identical to before.",
            GH_ParamAccess.item, d.EmitPartials);
        p.AddNumberParameter("Partial Fractions", "Pf",
            "Partial window lengths as fractions of each base segment span. " +
            "Default {0.5, 0.25}. Only consulted when Emit Partials is on.",
            GH_ParamAccess.list, new List<double>(d.PartialFractions ?? Array.Empty<double>()));
        p.AddNumberParameter("Partial Stride Fraction", "Pst",
            "Stride between consecutive partial windows as a fraction of the " +
            "window length. Default 1.0 (non-overlapping tiling). Only consulted " +
            "when Emit Partials is on.",
            GH_ParamAccess.item, d.PartialStrideFraction);

        // --- Global non-overlap resolve (R2) ------------------------------
        p.AddNumberParameter("Overlap Penalty", "Op",
            "When > 0 a candidate's score is penalised by penalty * overlap " +
            "area fraction, lowering overlapping placements. 0 (default) = no " +
            "penalty. A working value is ~1.0.",
            GH_ParamAccess.item, d.OverlapPenalty);
        p.AddBooleanParameter("Edge Exclusivity", "Ex",
            "When TRUE a placed panel's matched segment is consumed so two " +
            "pieces cannot snap to the same placed edge. FALSE (default) = a " +
            "segment can be reused (existing behaviour).",
            GH_ParamAccess.item, d.EdgeExclusivity);
        p.AddBooleanParameter("Resolve Overlap", "Ro",
            "When TRUE the caller runs a post-solve 2D rigid depenetration " +
            "polish (translation only, anchor-locked) until pairwise overlap is " +
            "within tolerance. FALSE (default) = no polish.",
            GH_ParamAccess.item, d.ResolveOverlap);
        p.AddNumberParameter("Resolve Overlap Tolerance", "Rot",
            "Target max pairwise overlap area as a fraction of the smaller " +
            "contour, for the Resolve Overlap polish. Default 0.001 (0.1%).",
            GH_ParamAccess.item, d.ResolveOverlapTolerance);
        p.AddIntegerParameter("Resolve Overlap Iterations", "Roi",
            "Max relaxation iterations for the Resolve Overlap polish. " +
            "Default 50.",
            GH_ParamAccess.item, d.ResolveOverlapIterations);
        p.AddNumberParameter("Resolve Overlap Relaxation", "Ror",
            "Per-iteration step factor in (0,1] for the Resolve Overlap polish. " +
            "Lower = stabler but slower. Default 0.5.",
            GH_ParamAccess.item, d.ResolveOverlapRelaxation);

        // --- Soft-ICP rim-contact refine (Pillar A) -----------------------
        p.AddBooleanParameter("Soft-ICP Refine", "Si",
            "When TRUE the caller runs the Soft-ICP refiner after the solve to " +
            "pull open-mesh rims into contact with a non-penetration hinge. " +
            "FALSE (default) = no refine. Only the keys below are exposed; other " +
            "SoftIcpOptions fields keep their Core defaults.",
            GH_ParamAccess.item, d.SoftIcpRefine);
        p.AddNumberParameter("Soft-ICP Tau0 Factor", "Si0",
            "Initial CPD temperature tau0 = factor * (median rim spacing)^2. " +
            "Larger = softer / wider start. Default 4.0.",
            GH_ParamAccess.item, sd.Tau0Factor);
        p.AddNumberParameter("Soft-ICP Tau Anneal", "SiA",
            "Geometric anneal factor applied to tau each iteration, in (0,1). " +
            "Default 0.8.",
            GH_ParamAccess.item, sd.TauAnneal);
        p.AddNumberParameter("Soft-ICP Correspondence Radius Factor", "SiR",
            "Contact correspondence radius = factor * (median rim spacing). " +
            "Neighbours beyond contribute zero weight. Default 3.0. 0 = no cutoff.",
            GH_ParamAccess.item, sd.CorrespondenceRadiusFactor);
        p.AddNumberParameter("Soft-ICP Contact Weight", "SiC",
            "Weight w_contact of the contact term. Default 1.0.",
            GH_ParamAccess.item, sd.ContactWeight);
        p.AddNumberParameter("Soft-ICP Penetration Weight", "SiP",
            "Hinge weight w_pen / lambda for the non-penetration term. " +
            "Default 1.0.",
            GH_ParamAccess.item, sd.PenetrationWeight);
        p.AddIntegerParameter("Soft-ICP Max Iterations", "SiI",
            "Max outer EM iterations for the Soft-ICP refiner. Default 40.",
            GH_ParamAccess.item, sd.MaxIterations);

        // --- 2.5D projection bootstrap (WIP, OFF by default) --------------
        p.AddBooleanParameter("Projection Bootstrap", "Pb",
            "WIP / UNVERIFIED. When TRUE the caller bootstraps 3D candidate " +
            "pairs by per-facet 2D projection + lift (agglomerative 3D path " +
            "only). FALSE (default) = no projection bootstrap. Needs its own " +
            "HITL before any visual-correctness claim.",
            GH_ParamAccess.item, d.ProjectionBootstrap);
        p.AddNumberParameter("Projection Sample Spacing Factor", "Pbs",
            "Resample spacing for the projected 2D rim as a fraction of the " +
            "loop bbox diagonal. Default 0.02.",
            GH_ParamAccess.item, d.ProjectionSampleSpacingFactor);
        p.AddNumberParameter("Projection Planarity Factor", "Pbp",
            "Planarity flag threshold for a projected rim as a fraction of the " +
            "loop bbox diagonal. Default 0.05.",
            GH_ParamAccess.item, d.ProjectionPlanarityFactor);
        p.AddNumberParameter("Projection Verify Factor", "Pbv",
            "3D verification gate for a lifted pair as a fraction of the " +
            "projected-rim scale. Default 0.12.",
            GH_ParamAccess.item, d.ProjectionVerifyFactor);

        // Mark every input optional so an empty component emits Core defaults.
        for (int i = 0; i < p.ParamCount; i++) p[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Options", "O",
            "AssemblyOptions DTO bundling the advanced EdgeMatch flags. Wire " +
            "into EdgeMatch Solve's optional Opt input. When wired, the solver " +
            "copies these advanced fields onto the options it builds from its " +
            "simple inputs; the simple inputs keep owning the basic fields.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var opts = new AssemblyOptions();
        var soft = opts.SoftIcp; // never null; default instance

        // --- Mode ---------------------------------------------------------
        bool agglomerative = opts.Mode == AssemblyMode.Agglomerative;
        da.GetData(IAgglomerative, ref agglomerative);
        opts.Mode = agglomerative ? AssemblyMode.Agglomerative : AssemblyMode.FrameAnchored;

        // --- Scale-relative gates (A1) ------------------------------------
        int nonCrossingMaxGap = opts.NonCrossingMaxGap;
        da.GetData(INonCrossingMaxGap, ref nonCrossingMaxGap);
        opts.NonCrossingMaxGap = nonCrossingMaxGap;

        double phaseScore = opts.PhaseScoreThreshold;
        da.GetData(IPhaseScoreThreshold, ref phaseScore);
        opts.PhaseScoreThreshold = phaseScore;

        double residualFactor = opts.ResidualThresholdFactor;
        da.GetData(IResidualThresholdFactor, ref residualFactor);
        opts.ResidualThresholdFactor = residualFactor;

        // --- Partial sub-segment matching (R1) ----------------------------
        bool emitPartials = opts.EmitPartials;
        da.GetData(IEmitPartials, ref emitPartials);
        opts.EmitPartials = emitPartials;

        var partialFractions = new List<double>();
        if (da.GetDataList(IPartialFractions, partialFractions) && partialFractions.Count > 0)
            opts.PartialFractions = partialFractions.ToArray();
        // else keep the Core default {0.5, 0.25}

        double partialStride = opts.PartialStrideFraction;
        da.GetData(IPartialStrideFraction, ref partialStride);
        opts.PartialStrideFraction = partialStride;

        // --- Global non-overlap resolve (R2) ------------------------------
        double overlapPenalty = opts.OverlapPenalty;
        da.GetData(IOverlapPenalty, ref overlapPenalty);
        opts.OverlapPenalty = overlapPenalty;

        bool edgeExclusivity = opts.EdgeExclusivity;
        da.GetData(IEdgeExclusivity, ref edgeExclusivity);
        opts.EdgeExclusivity = edgeExclusivity;

        bool resolveOverlap = opts.ResolveOverlap;
        da.GetData(IResolveOverlap, ref resolveOverlap);
        opts.ResolveOverlap = resolveOverlap;

        double resolveTol = opts.ResolveOverlapTolerance;
        da.GetData(IResolveOverlapTolerance, ref resolveTol);
        opts.ResolveOverlapTolerance = resolveTol;

        int resolveIters = opts.ResolveOverlapIterations;
        da.GetData(IResolveOverlapIterations, ref resolveIters);
        opts.ResolveOverlapIterations = resolveIters;

        double resolveRelax = opts.ResolveOverlapRelaxation;
        da.GetData(IResolveOverlapRelaxation, ref resolveRelax);
        opts.ResolveOverlapRelaxation = resolveRelax;

        // --- Soft-ICP rim-contact refine (Pillar A) -----------------------
        bool softRefine = opts.SoftIcpRefine;
        da.GetData(ISoftIcpRefine, ref softRefine);
        opts.SoftIcpRefine = softRefine;

        double tau0 = soft.Tau0Factor;
        da.GetData(ISoftIcpTau0Factor, ref tau0);
        soft.Tau0Factor = tau0;

        double tauAnneal = soft.TauAnneal;
        da.GetData(ISoftIcpTauAnneal, ref tauAnneal);
        soft.TauAnneal = tauAnneal;

        double corrRadius = soft.CorrespondenceRadiusFactor;
        da.GetData(ISoftIcpCorrRadiusFactor, ref corrRadius);
        soft.CorrespondenceRadiusFactor = corrRadius;

        double contactWeight = soft.ContactWeight;
        da.GetData(ISoftIcpContactWeight, ref contactWeight);
        soft.ContactWeight = contactWeight;

        double penWeight = soft.PenetrationWeight;
        da.GetData(ISoftIcpPenetrationWeight, ref penWeight);
        soft.PenetrationWeight = penWeight;

        int softIters = soft.MaxIterations;
        da.GetData(ISoftIcpMaxIterations, ref softIters);
        soft.MaxIterations = softIters;

        // --- 2.5D projection bootstrap (WIP, OFF by default) --------------
        bool projBootstrap = opts.ProjectionBootstrap;
        da.GetData(IProjectionBootstrap, ref projBootstrap);
        opts.ProjectionBootstrap = projBootstrap;

        double projSpacing = opts.ProjectionSampleSpacingFactor;
        da.GetData(IProjectionSampleSpacingFactor, ref projSpacing);
        opts.ProjectionSampleSpacingFactor = projSpacing;

        double projPlanarity = opts.ProjectionPlanarityFactor;
        da.GetData(IProjectionPlanarityFactor, ref projPlanarity);
        opts.ProjectionPlanarityFactor = projPlanarity;

        double projVerify = opts.ProjectionVerifyFactor;
        da.GetData(IProjectionVerifyFactor, ref projVerify);
        opts.ProjectionVerifyFactor = projVerify;

        if (projBootstrap)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Projection Bootstrap is WIP/unverified and only affects the " +
                "agglomerative 3D path.");

        da.SetData(0, opts);
    }
}
