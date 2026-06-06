#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// CutValidationTests — Phase 4 of the robustness pass.
// =============================================================================

static class CutValidationTests
{
    public static void Validate_PerfectSplit_ConservesVolume()
    {
        // Pre: unit cube. Post: two halves split at x = 0.5.
        var pre = new[] { Slab.Box(0, 0, 0, 1, 1, 1) };
        var post = new[]
        {
            Slab.Box(0,   0, 0, 0.5, 1, 1),
            Slab.Box(0.5, 0, 0, 1.0, 1, 1),
        };
        var rep = CutResultValidator.Validate(pre, post);
        Assert(rep.Conserved, $"perfect split should conserve, got {rep}");
        Assert(rep.SliverCount == 0, $"slivers {rep.SliverCount}");
        Assert(rep.Dropouts == 0, $"dropouts {rep.Dropouts}");
        Assert(Math.Abs(rep.PreVolume - 1.0) < 1e-9, $"preVol {rep.PreVolume}");
        Assert(Math.Abs(rep.PostVolumeSum - 1.0) < 1e-9, $"postVol {rep.PostVolumeSum}");
    }

    public static void Validate_LeakyCut_FlagsAsNonConserved()
    {
        // Pre: unit cube. Post: two halves with a small gap → missing volume.
        var pre = new[] { Slab.Box(0, 0, 0, 1, 1, 1) };
        var post = new[]
        {
            Slab.Box(0,    0, 0, 0.45, 1, 1),
            Slab.Box(0.55, 0, 0, 1.0,  1, 1),
        };
        var rep = CutResultValidator.Validate(pre, post, relativeTolerance: 1e-3);
        Assert(!rep.Conserved, $"leaky cut should NOT conserve, got {rep}");
        Assert(rep.RelativeError > 0.05, $"relErr {rep.RelativeError}");
    }

    public static void Validate_SliverDetected_BelowFraction()
    {
        // Pre: unit cube. Post: a 0.99-cube + a 0.01-thick sliver. Volume
        // conserves but the sliver is flagged.
        var pre = new[] { Slab.Box(0, 0, 0, 1, 1, 1) };
        var post = new[]
        {
            Slab.Box(0,    0, 0, 0.99, 1, 1),
            Slab.Box(0.99, 0, 0, 1.0,  1, 1),
        };
        var rep = CutResultValidator.Validate(pre, post,
            relativeTolerance: 1e-9,
            sliverFraction: 0.05);  // anything < 5% of total = sliver
        Assert(rep.Conserved, "volume should still conserve");
        Assert(rep.SliverCount == 1, $"expected 1 sliver, got {rep.SliverCount}");
    }

    public static void EnumerateSlivers_ReturnsCorrectIndices()
    {
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1.0, 1, 1),    // 1.0
            Slab.Box(0, 0, 0, 0.001, 1, 1),  // 0.001 — sliver
            Slab.Box(0, 0, 0, 0.5, 1, 1),    // 0.5
        };
        var idx = CutResultValidator.EnumerateSlivers(pieces, 0.01);
        Assert(idx.Count == 1, $"sliver count {idx.Count}");
        Assert(idx[0] == 1, $"sliver index {idx[0]}");
    }

    public static void DropSlivers_RemovesFlaggedPieces()
    {
        var pieces = new[]
        {
            Slab.Box(0, 0, 0, 1.0, 1, 1),
            Slab.Box(0, 0, 0, 0.0001, 1, 1),
            Slab.Box(0, 0, 0, 0.5, 1, 1),
        };
        var keep = CutResultValidator.DropSlivers(pieces, 0.01);
        Assert(keep.Count == 2, $"kept {keep.Count}");
    }

    public static void Validate_NullInputs_Throw()
    {
        bool t1 = false, t2 = false;
        try { CutResultValidator.Validate(null, new[] { Slab.Box(0, 0, 0, 1, 1, 1) }); }
        catch (ArgumentNullException) { t1 = true; }
        try { CutResultValidator.Validate(new[] { Slab.Box(0, 0, 0, 1, 1, 1) }, null); }
        catch (ArgumentNullException) { t2 = true; }
        Assert(t1 && t2, "null inputs must throw");
    }

    public static void Gh_CutValidationComponent_Metadata()
    {
        var c = new CutValidationComponent();
        Assert(c.ComponentGuid == new Guid("DEF01234-5678-9ABC-DEF0-123456789ABC"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.Params.Input.Count == 6, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 9, $"Output count {c.Params.Output.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
