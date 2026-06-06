#nullable disable
using System;
using Frahan.EdgeMatching;

namespace Frahan.Tests;

// EdgeMatch Options DTO surfacing (2026-05-25). The "EdgeMatch Options" GH
// component bundles the advanced AssemblyOptions flags into a DTO that
// EdgeMatch Solve consumes on its optional "Opt" input. Two guarantees the
// suite must lock, both PURE MANAGED (no Rhino native): the DTO defaults are
// the Core defaults (an untouched Options component emits unchanged behaviour),
// and the Solve component's advanced-field merge copies the advanced fields
// while preserving the basic fields owned by the simple inputs.
//
// The GH component is instantiated only in EdgeMatchingComponentGuidTests
// (which SKIPs gracefully on hosts without Grasshopper.dll). These DTO-level
// tests cover the value semantics that drive what the component emits/consumes.
static class EdgeMatchOptionsTests
{
    // ---- (1) An untouched Options component emits the Core defaults --------
    // EdgeMatchOptionsComponent seeds every input default from a fresh
    // AssemblyOptions / SoftIcpOptions and writes back the same value when the
    // input is unconnected, so an empty component yields default options. This
    // test locks those defaults so the empty path stays today's behaviour.
    public static void EmptyOptions_EqualCoreDefaults()
    {
        var o = new AssemblyOptions();

        Assert(o.Mode == AssemblyMode.FrameAnchored, "default Mode must be FrameAnchored");
        Assert(o.NonCrossingMaxGap == 0, "default NonCrossingMaxGap must be 0");
        Assert(o.PhaseScoreThreshold == 0.5, $"default PhaseScoreThreshold must be 0.5, got {o.PhaseScoreThreshold}");
        Assert(o.ResidualThresholdFactor == 0.0, "default ResidualThresholdFactor must be 0");
        Assert(o.EmitPartials == false, "default EmitPartials must be false");
        Assert(o.PartialFractions != null && o.PartialFractions.Length == 2
            && o.PartialFractions[0] == 0.5 && o.PartialFractions[1] == 0.25,
            "default PartialFractions must be {0.5, 0.25}");
        Assert(o.PartialStrideFraction == 1.0, "default PartialStrideFraction must be 1.0");
        Assert(o.OverlapPenalty == 0.0, "default OverlapPenalty must be 0");
        Assert(o.EdgeExclusivity == false, "default EdgeExclusivity must be false");
        Assert(o.ResolveOverlap == false, "default ResolveOverlap must be false");
        Assert(o.ResolveOverlapTolerance == 0.001, $"default ResolveOverlapTolerance must be 0.001, got {o.ResolveOverlapTolerance}");
        Assert(o.ResolveOverlapIterations == 50, "default ResolveOverlapIterations must be 50");
        Assert(o.ResolveOverlapRelaxation == 0.5, "default ResolveOverlapRelaxation must be 0.5");
        Assert(o.SoftIcpRefine == false, "default SoftIcpRefine must be false");
        Assert(o.SoftIcp != null, "default SoftIcp must not be null");
        Assert(o.SoftIcp.Tau0Factor == 4.0, "default SoftIcp.Tau0Factor must be 4.0");
        Assert(o.SoftIcp.TauAnneal == 0.8, "default SoftIcp.TauAnneal must be 0.8");
        Assert(o.SoftIcp.CorrespondenceRadiusFactor == 3.0, "default SoftIcp.CorrespondenceRadiusFactor must be 3.0");
        Assert(o.SoftIcp.ContactWeight == 1.0, "default SoftIcp.ContactWeight must be 1.0");
        Assert(o.SoftIcp.PenetrationWeight == 1.0, "default SoftIcp.PenetrationWeight must be 1.0");
        Assert(o.SoftIcp.MaxIterations == 40, "default SoftIcp.MaxIterations must be 40");
        Assert(o.ProjectionBootstrap == false, "default ProjectionBootstrap must be false (WIP off)");
        Assert(o.ProjectionSampleSpacingFactor == 0.02, "default ProjectionSampleSpacingFactor must be 0.02");
        Assert(o.ProjectionPlanarityFactor == 0.05, "default ProjectionPlanarityFactor must be 0.05");
        Assert(o.ProjectionVerifyFactor == 0.12, "default ProjectionVerifyFactor must be 0.12");
    }

    // ---- (2) Solve's advanced-field merge round-trips ----------------------
    // Replicates EdgeMatchSolveComponent's merge: build the solver options from
    // the simple inputs, then copy the ADVANCED fields from the supplied DTO.
    // Asserts the advanced flags arrive AND the basic fields (BeamWidth,
    // MaxIterations, ResidualThreshold, NonCrossingCorrespondence) are preserved.
    public static void Merge_AdvancedFields_RoundTrips_BasicFieldsPreserved()
    {
        // Simple-input-derived options (what Solve builds today).
        var asmOpt = new AssemblyOptions
        {
            BeamWidth = 13,
            MaxIterations = 777,
            ResidualThreshold = 2.5,
            NonCrossingCorrespondence = true,
        };

        // A non-default advanced DTO from EdgeMatch Options.
        var custom = new SoftIcpOptions { Tau0Factor = 9.0, MaxIterations = 7 };
        var supplied = new AssemblyOptions
        {
            Mode = AssemblyMode.Agglomerative,
            NonCrossingMaxGap = 4,
            PhaseScoreThreshold = 0.7,
            ResidualThresholdFactor = 0.01,
            EmitPartials = true,
            PartialFractions = new[] { 0.75 },
            PartialStrideFraction = 0.5,
            OverlapPenalty = 2.0,
            EdgeExclusivity = true,
            ResolveOverlap = true,
            ResolveOverlapTolerance = 0.002,
            ResolveOverlapIterations = 25,
            ResolveOverlapRelaxation = 0.3,
            SoftIcpRefine = true,
            SoftIcp = custom,
            ProjectionBootstrap = true,
            ProjectionSampleSpacingFactor = 0.03,
            ProjectionPlanarityFactor = 0.06,
            ProjectionVerifyFactor = 0.2,
        };

        MergeAdvanced(asmOpt, supplied);

        // Advanced fields arrived.
        Assert(asmOpt.Mode == AssemblyMode.Agglomerative, "Mode must be overridden");
        Assert(asmOpt.NonCrossingMaxGap == 4, "NonCrossingMaxGap must be overridden");
        Assert(asmOpt.PhaseScoreThreshold == 0.7, "PhaseScoreThreshold must be overridden");
        Assert(asmOpt.ResidualThresholdFactor == 0.01, "ResidualThresholdFactor must be overridden");
        Assert(asmOpt.EmitPartials, "EmitPartials must be overridden");
        Assert(asmOpt.PartialFractions.Length == 1 && asmOpt.PartialFractions[0] == 0.75,
            "PartialFractions must be overridden");
        Assert(asmOpt.PartialStrideFraction == 0.5, "PartialStrideFraction must be overridden");
        Assert(asmOpt.OverlapPenalty == 2.0, "OverlapPenalty must be overridden");
        Assert(asmOpt.EdgeExclusivity, "EdgeExclusivity must be overridden");
        Assert(asmOpt.ResolveOverlap, "ResolveOverlap must be overridden");
        Assert(asmOpt.ResolveOverlapTolerance == 0.002, "ResolveOverlapTolerance must be overridden");
        Assert(asmOpt.ResolveOverlapIterations == 25, "ResolveOverlapIterations must be overridden");
        Assert(asmOpt.ResolveOverlapRelaxation == 0.3, "ResolveOverlapRelaxation must be overridden");
        Assert(asmOpt.SoftIcpRefine, "SoftIcpRefine must be overridden");
        Assert(ReferenceEquals(asmOpt.SoftIcp, custom), "SoftIcp DTO must be carried through");
        Assert(asmOpt.SoftIcp.Tau0Factor == 9.0, "SoftIcp.Tau0Factor must be overridden");
        Assert(asmOpt.SoftIcp.MaxIterations == 7, "SoftIcp.MaxIterations must be overridden");
        Assert(asmOpt.ProjectionBootstrap, "ProjectionBootstrap must be overridden");
        Assert(asmOpt.ProjectionSampleSpacingFactor == 0.03, "ProjectionSampleSpacingFactor must be overridden");
        Assert(asmOpt.ProjectionPlanarityFactor == 0.06, "ProjectionPlanarityFactor must be overridden");
        Assert(asmOpt.ProjectionVerifyFactor == 0.2, "ProjectionVerifyFactor must be overridden");

        // Basic fields, owned by the simple inputs, untouched by the merge.
        Assert(asmOpt.BeamWidth == 13, "BeamWidth must be preserved");
        Assert(asmOpt.MaxIterations == 777, "MaxIterations must be preserved");
        Assert(asmOpt.ResidualThreshold == 2.5, "ResidualThreshold must be preserved");
        Assert(asmOpt.NonCrossingCorrespondence, "NonCrossingCorrespondence must be preserved");
    }

    // ---- (3) No DTO connected -> options unchanged -------------------------
    // When EdgeMatch Solve has no Options wired the merge never runs, so the
    // options are exactly what the simple inputs produced (byte-identical path).
    public static void NoMerge_LeavesSimpleOptionsUntouched()
    {
        var asmOpt = new AssemblyOptions
        {
            BeamWidth = 8,
            MaxIterations = 1000,
            ResidualThreshold = 1.0,
            NonCrossingCorrespondence = false,
        };
        var reference = new AssemblyOptions(); // Core defaults for the advanced fields

        // No merge performed.
        Assert(asmOpt.Mode == reference.Mode, "Mode must remain default");
        Assert(asmOpt.OverlapPenalty == reference.OverlapPenalty, "OverlapPenalty must remain default");
        Assert(asmOpt.SoftIcpRefine == reference.SoftIcpRefine, "SoftIcpRefine must remain default");
        Assert(asmOpt.ProjectionBootstrap == reference.ProjectionBootstrap, "ProjectionBootstrap must remain default");
        Assert(asmOpt.EmitPartials == reference.EmitPartials, "EmitPartials must remain default");
    }

    // Mirror of EdgeMatchSolveComponent.SolveInstance advanced-field copy.
    // Kept in lockstep with the component so the test guards the real semantics.
    private static void MergeAdvanced(AssemblyOptions asmOpt, AssemblyOptions supplied)
    {
        asmOpt.Mode = supplied.Mode;
        asmOpt.NonCrossingMaxGap = supplied.NonCrossingMaxGap;
        asmOpt.PhaseScoreThreshold = supplied.PhaseScoreThreshold;
        asmOpt.ResidualThresholdFactor = supplied.ResidualThresholdFactor;
        asmOpt.EmitPartials = supplied.EmitPartials;
        asmOpt.PartialFractions = supplied.PartialFractions;
        asmOpt.PartialStrideFraction = supplied.PartialStrideFraction;
        asmOpt.OverlapPenalty = supplied.OverlapPenalty;
        asmOpt.EdgeExclusivity = supplied.EdgeExclusivity;
        asmOpt.ResolveOverlap = supplied.ResolveOverlap;
        asmOpt.ResolveOverlapTolerance = supplied.ResolveOverlapTolerance;
        asmOpt.ResolveOverlapIterations = supplied.ResolveOverlapIterations;
        asmOpt.ResolveOverlapRelaxation = supplied.ResolveOverlapRelaxation;
        asmOpt.SoftIcpRefine = supplied.SoftIcpRefine;
        if (supplied.SoftIcp != null) asmOpt.SoftIcp = supplied.SoftIcp;
        asmOpt.ProjectionBootstrap = supplied.ProjectionBootstrap;
        asmOpt.ProjectionSampleSpacingFactor = supplied.ProjectionSampleSpacingFactor;
        asmOpt.ProjectionPlanarityFactor = supplied.ProjectionPlanarityFactor;
        asmOpt.ProjectionVerifyFactor = supplied.ProjectionVerifyFactor;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
