#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Masonry.Quarry.Processing;

namespace Frahan.Tests;

// Tests for FractureTracer (picks -> continuous fracture lines), both modes.
public static class FractureTracerTests
{
    private const double Dt = 0.4, Dx = 0.5, V = 0.12;   // ns, m, m/ns

    private static FractureExtractor.FracturePick Pk(int sample, int trace) =>
        new FractureExtractor.FracturePick
        { SampleIndex = sample, TraceIndex = trace, Energy = 1.0, DepthMetres = sample * V * Dt / 2.0 };

    // a straight line of picks from (t0..t1) at sample = s0 + slope*(t-t0)
    private static IEnumerable<FractureExtractor.FracturePick> Line(int t0, int t1, int s0, double slope)
    {
        for (int t = t0; t <= t1; t++)
            yield return Pk((int)Math.Round(s0 + slope * (t - t0)), t);
    }

    // --- connected-components traces one planted sub-horizontal line ---
    public static void Trace_SinglePlantedLine_OneLine()
    {
        var picks = Line(0, 59, 50, 0.0).ToList();        // 60-trace horizontal reflector
        var tr = new FractureTracer { Mode = FractureTraceMode.ConnectedComponents, MinSpanTraces = 40 };
        var r = tr.Trace(picks, 256, 60, Dt, Dx, V);
        Assert(r.LineCount == 1, $"expected 1 line, got {r.LineCount}");
        Assert(r.Lines[0].SpanTraces == 60, $"span should be 60, got {r.Lines[0].SpanTraces}");
        Assert(r.Lines[0].DipDeg < 5.0, $"horizontal line dip should be ~0, got {r.Lines[0].DipDeg:F1}");
        Assert(r.LabelPerPick.All(l => l == r.Lines[0].Id), "all picks should carry the line id");
    }

    // --- a too-short reflector is dropped (USGS span gate) ---
    public static void Trace_ShortReflector_Dropped()
    {
        var picks = Line(0, 19, 40, 0.0).ToList();        // 20 traces < 40
        var tr = new FractureTracer { MinSpanTraces = 40 };
        var r = tr.Trace(picks, 256, 20, Dt, Dx, V);
        Assert(r.LineCount == 0, $"short reflector should be dropped, got {r.LineCount} lines");
        Assert(r.LabelPerPick.All(l => l == 0), "short-reflector picks should be unassigned (0)");
    }

    // --- two well-separated lines -> two lines (both modes) ---
    public static void Trace_TwoSeparatedLines_Two()
    {
        var picks = Line(0, 59, 40, 0.0).Concat(Line(0, 59, 120, 0.0)).ToList();  // 80 samples apart
        foreach (var mode in new[] { FractureTraceMode.ConnectedComponents, FractureTraceMode.OrientationGated })
        {
            var r = new FractureTracer { Mode = mode, MinSpanTraces = 40 }.Trace(picks, 256, 60, Dt, Dx, V);
            Assert(r.LineCount == 2, $"{mode}: expected 2 lines, got {r.LineCount}");
        }
    }

    // --- dip is recovered for a sloping reflector ---
    public static void Trace_DipRecovered()
    {
        // slope 1 sample/trace: dip = atan( (1*V*Dt/2) / Dx ) = atan(0.024/0.5)=2.75 deg
        var picks = Line(0, 59, 20, 1.0).ToList();
        var r = new FractureTracer { MinSpanTraces = 40 }.Trace(picks, 256, 60, Dt, Dx, V);
        Assert(r.LineCount == 1, "expected 1 sloping line");
        double expect = Math.Atan2(1.0 * V * Dt / 2.0, Dx) * 180.0 / Math.PI;
        Assert(Math.Abs(r.Lines[0].DipDeg - expect) < 2.0,
            $"dip {r.Lines[0].DipDeg:F2} vs expected {expect:F2}");
    }

    // --- crossing reflectors: connected MERGES (1), orientation-gated does NOT merge fewer ---
    public static void Trace_Crossing_ConnectedMergesOrientationSeparates()
    {
        // horizontal line at sample 50 + steep line crossing it; share ~1 pixel near trace 30
        var h = Line(0, 60, 50, 0.0);
        var steep = Line(15, 45, 20, 2.0);               // slope 2 -> ~steep; passes through ~ (30,50)
        var picks = h.Concat(steep).ToList();
        var conn = new FractureTracer { Mode = FractureTraceMode.ConnectedComponents, MinSpanTraces = 25 }
            .Trace(picks, 256, 61, Dt, Dx, V);
        var ori = new FractureTracer { Mode = FractureTraceMode.OrientationGated, MinSpanTraces = 25,
            OrientationToleranceDeg = 25 }.Trace(picks, 256, 61, Dt, Dx, V);
        Assert(conn.LineCount == 1, $"connected should MERGE the crossing into 1, got {conn.LineCount}");
        Assert(ori.LineCount >= conn.LineCount,
            $"orientation-gated should not merge MORE than connected ({ori.LineCount} vs {conn.LineCount})");
    }

    // --- FractureSurface loft: a traced line -> a ribbon surface following its depth ---
    public static void Loft_HorizontalLine_FlatSurface()
    {
        var picks = Line(0, 49, 60, 0.0).ToList();
        var r = new FractureTracer { MinSpanTraces = 40 }.Trace(picks, 256, 50, Dt, Dx, V);
        Assert(r.LineCount == 1, "expected 1 line to loft");
        var m = FractureSurface.Loft(r.Lines[0], 3.0, 2);
        Assert(m.TriangleCount > 0 && m.VertexCount > 0, "loft produced empty mesh");
        // all vertices should sit at the reflector depth (z = -depth, ~constant for horizontal)
        double z0 = m.Vertices[2];
        for (int i = 2; i < m.Vertices.Length; i += 3)
            Assert(Math.Abs(m.Vertices[i] - z0) < 0.2, "horizontal loft is not flat in z");
        // Y must span 0..3 (the strike extent)
        double ymax = 0; for (int i = 1; i < m.Vertices.Length; i += 3) ymax = Math.Max(ymax, m.Vertices[i]);
        Assert(Math.Abs(ymax - 3.0) < 1e-6, $"strike extent should be 3, got {ymax}");
    }

    // --- loft across a grid of parallel section-lines -> one 3D surface ---
    public static void LoftAcrossLines_Grid_Surface()
    {
        var tr = new FractureTracer { MinSpanTraces = 40 };
        var l0 = tr.Trace(Line(0, 49, 60, 0.0).ToList(), 256, 50, Dt, Dx, V).Lines[0];
        var l1 = tr.Trace(Line(0, 49, 62, 0.0).ToList(), 256, 50, Dt, Dx, V).Lines[0];
        var l2 = tr.Trace(Line(0, 49, 58, 0.0).ToList(), 256, 50, Dt, Dx, V).Lines[0];
        var grid = new List<(double, FractureLine)> { (0.0, l0), (1.0, l1), (2.0, l2) };
        var m = FractureSurface.LoftAcrossLines(grid, 16);
        Assert(m.VertexCount == 3 * 16, $"expected 48 verts, got {m.VertexCount}");
        Assert(m.TriangleCount == 2 * 2 * 15, $"expected {2 * 2 * 15} tris, got {m.TriangleCount}");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("ASSERT FAILED: " + msg);
    }
}
