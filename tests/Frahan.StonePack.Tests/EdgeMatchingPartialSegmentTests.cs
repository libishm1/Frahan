#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// R1 partial sub-segment emission. Needs the Rhino runtime (the segmenter
// calls Curve.GetLength / DivideByCount / PointAt), so these SKIP if
// RhinoCommon fails to load, exactly like EdgeMatchingBoundarySegmenterTests.
static class EdgeMatchingPartialSegmentTests
{
    // A closed irregular pentagon with two sharp interior turns — same shape
    // the existing BoundarySegmenter test uses, so its base segmentation is
    // already known to produce >= 2 segments.
    private static Panel MakePentagonPanel()
    {
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(40, 0, 0),
            new Point3d(60, 30, 0),     // sharp turn
            new Point3d(30, 50, 0),
            new Point3d(-10, 30, 0),    // sharp turn
            new Point3d(0, 0, 0),
        };
        return new Panel("pent", poly.ToPolylineCurve(), PanelKind.Shard);
    }

    private static SegmenterOptions BaseOpt() => new SegmenterOptions
    {
        SampleSpacing = 1.0,
        BreakAngleDeg = 18.0,
        MinSegmentLength = 5.0,
        SignatureBins = 64,
    };

    public static void PartialsOff_IdenticalToBase()
    {
        var panel = MakePentagonPanel();

        // Explicitly-off and brand-new options must produce the SAME segments.
        var baseOpt = BaseOpt();
        var offOpt = BaseOpt();
        offOpt.EmitPartials = false;
        offOpt.PartialFractions = new[] { 0.5, 0.25 }; // present but ignored when off

        var baseSegs = BoundarySegmenter.Segment(panel, baseOpt);
        var offSegs = BoundarySegmenter.Segment(panel, offOpt);

        Assert(baseSegs.Count == offSegs.Count,
            $"EmitPartials=false must not change segment count: base={baseSegs.Count} off={offSegs.Count}");
        for (int i = 0; i < baseSegs.Count; i++)
        {
            Assert(Math.Abs(baseSegs[i].ChordLength - offSegs[i].ChordLength) < 1e-9,
                $"segment {i} chord drifted with partials off");
            Assert(baseSegs[i].Index == offSegs[i].Index,
                $"segment {i} index drifted with partials off");
        }
    }

    public static void PartialsOn_AddsSegments()
    {
        var panel = MakePentagonPanel();
        var baseSegs = BoundarySegmenter.Segment(panel, BaseOpt());

        var onOpt = BaseOpt();
        onOpt.EmitPartials = true;
        onOpt.PartialFractions = new[] { 0.5, 0.25 };
        var onSegs = BoundarySegmenter.Segment(panel, onOpt);

        Assert(onSegs.Count > baseSegs.Count,
            $"EmitPartials=true must add sub-segments: base={baseSegs.Count} on={onSegs.Count}");

        // Every emitted segment (base or partial) must be well-formed: correct
        // signature length, positive chord, null torsion (2D path).
        foreach (var s in onSegs)
        {
            Assert(s.TurningSignature.Length == 64,
                $"signature length {s.TurningSignature.Length} != 64");
            Assert(s.CurvatureSignature.Length == 64,
                $"curvature length {s.CurvatureSignature.Length} != 64");
            Assert(s.TorsionSignature == null, "2D partial must not carry torsion");
            Assert(s.ChordLength > 0, $"segment {s.Index} non-positive chord {s.ChordLength}");
        }

        // At least one partial must be strictly shorter than the longest base
        // segment (that is the whole point: short windows reach short edges).
        double longestBase = 0.0;
        foreach (var b in baseSegs) if (b.ChordLength > longestBase) longestBase = b.ChordLength;
        bool anyShorter = false;
        foreach (var s in onSegs) if (s.ChordLength < longestBase - 1e-6) { anyShorter = true; break; }
        Assert(anyShorter, "expected at least one partial shorter than the longest base segment");
    }

    public static void PartialsOn_IsDeterministic()
    {
        var panel = MakePentagonPanel();
        var onOpt = BaseOpt();
        onOpt.EmitPartials = true;
        onOpt.PartialFractions = new[] { 0.5, 0.25 };

        var run1 = BoundarySegmenter.Segment(panel, onOpt);
        var run2 = BoundarySegmenter.Segment(panel, onOpt);

        Assert(run1.Count == run2.Count,
            $"two runs differ in count: {run1.Count} vs {run2.Count}");
        for (int i = 0; i < run1.Count; i++)
        {
            Assert(run1[i].Index == run2[i].Index, $"index mismatch at {i}");
            Assert(Math.Abs(run1[i].ChordLength - run2[i].ChordLength) < 1e-12,
                $"chord mismatch at {i}");
            Assert(run1[i].Sign == run2[i].Sign, $"sign mismatch at {i}");
            for (int k = 0; k < run1[i].TurningSignature.Length; k++)
                Assert(Math.Abs(run1[i].TurningSignature[k] - run2[i].TurningSignature[k]) < 1e-12,
                    $"signature mismatch at segment {i} bin {k}");
        }
    }

    public static void EmptyFractions_NoPartialsEvenWhenOn()
    {
        var panel = MakePentagonPanel();
        var baseSegs = BoundarySegmenter.Segment(panel, BaseOpt());

        var onOpt = BaseOpt();
        onOpt.EmitPartials = true;
        onOpt.PartialFractions = new double[0]; // nothing to emit
        var segs = BoundarySegmenter.Segment(panel, onOpt);

        Assert(segs.Count == baseSegs.Count,
            $"empty PartialFractions must add nothing: base={baseSegs.Count} got={segs.Count}");
    }

    public static void PartialWindows_DeterministicRangesAndOrder()
    {
        // Pure-managed: PartialWindows is a plain index-range generator, no
        // Rhino types involved. Fractions are emitted in declared order
        // (0.5 windows before 0.25 windows); positions ascending within each.
        var opt = new SegmenterOptions
        {
            EmitPartials = true,
            PartialFractions = new[] { 0.5, 0.25 },
            PartialStrideFraction = 1.0,
        };
        // Base window spanning indices 0..40 (span = 40 intervals).
        var w = BoundarySegmenter.PartialWindows(0, 40, opt);

        // frac 0.5 -> winLen 20, stride 20 -> windows [0,20],[20,40].
        // frac 0.25 -> winLen 10, stride 10 -> windows [0,10],[10,20],[20,30],[30,40].
        Assert(w.Count == 6, $"expected 6 partial windows, got {w.Count}");
        Assert(w[0] == (0, 20), $"window0 expected (0,20) got {w[0]}");
        Assert(w[1] == (20, 40), $"window1 expected (20,40) got {w[1]}");
        Assert(w[2] == (0, 10), $"window2 expected (0,10) got {w[2]}");
        Assert(w[3] == (10, 20), $"window3 expected (10,20) got {w[3]}");
        Assert(w[4] == (20, 30), $"window4 expected (20,30) got {w[4]}");
        Assert(w[5] == (30, 40), $"window5 expected (30,40) got {w[5]}");

        // Off => empty.
        var offOpt = new SegmenterOptions { EmitPartials = false, PartialFractions = new[] { 0.5 } };
        Assert(BoundarySegmenter.PartialWindows(0, 40, offOpt).Count == 0,
            "PartialWindows must be empty when EmitPartials is false");

        // Fraction >= 1.0 (would duplicate the base) is skipped.
        var fullOpt = new SegmenterOptions { EmitPartials = true, PartialFractions = new[] { 1.0 } };
        Assert(BoundarySegmenter.PartialWindows(0, 40, fullOpt).Count == 0,
            "fraction >= 1.0 must be skipped");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
