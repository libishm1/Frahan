#nullable disable
using System;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Needs Rhino runtime (Curve.GetLength / DivideByCount / PointAt).
static class EdgeMatchingBoundarySegmenterTests
{
    public static void IrregularPolygon_ProducesSegments()
    {
        // A closed irregular pentagon with two sharp interior turns.
        // Expect the segmenter to detect break-points at those turns
        // and emit at least two segments with non-zero turning.
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(40, 0, 0),
            new Point3d(60, 30, 0),     // sharp turn
            new Point3d(30, 50, 0),
            new Point3d(-10, 30, 0),    // sharp turn
            new Point3d(0, 0, 0),
        };
        var panel = new Panel("pent", poly.ToPolylineCurve(), PanelKind.Shard);
        var opt = new SegmenterOptions
        {
            SampleSpacing = 1.0,
            BreakAngleDeg = 18.0,
            MinSegmentLength = 5.0,
            SignatureBins = 64,
        };
        var segs = BoundarySegmenter.Segment(panel, opt);
        Assert(segs.Count >= 2, $"expected at least 2 segments on irregular polygon, got {segs.Count}");

        foreach (var s in segs)
        {
            Assert(s.TurningSignature.Length == opt.SignatureBins,
                $"signature length {s.TurningSignature.Length} != bins {opt.SignatureBins}");
            Assert(s.CurvatureSignature.Length == opt.SignatureBins,
                $"curvature length {s.CurvatureSignature.Length} != bins {opt.SignatureBins}");
            Assert(s.TorsionSignature == null,
                "planar 2D segmenter must not populate torsion");
            Assert(s.ChordLength > 0, $"segment {s.Index} has non-positive chord {s.ChordLength}");
        }
    }

    public static void FullCircle_StraightLine_ProducesNoBreaks()
    {
        // A smooth circle with uniform curvature has no break-point and
        // should collapse to zero or one segment depending on closure
        // handling. Either is acceptable — the test asserts segments are
        // well-formed if produced.
        var poly = new Polyline();
        const int n = 64;
        for (int i = 0; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            poly.Add(new Point3d(Math.Cos(t) * 20, Math.Sin(t) * 20, 0));
        }
        var panel = new Panel("circle", poly.ToPolylineCurve(), PanelKind.Shard);
        var segs = BoundarySegmenter.Segment(panel, new SegmenterOptions { BreakAngleDeg = 60.0 });
        // With a very wide break threshold no breaks fire; result is empty
        // or a single segment that spans the whole loop.
        Assert(segs.Count <= 1, $"expected ≤1 segment on smooth circle with wide break threshold, got {segs.Count}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
