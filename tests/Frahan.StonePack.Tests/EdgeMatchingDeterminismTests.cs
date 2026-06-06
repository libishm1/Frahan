#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// End-to-end determinism canary: run the full pipeline twice on a
// fixed input and assert byte-identical output. Spec §9 + addendum
// §10 (D7, D8). Needs Rhino runtime.
static class EdgeMatchingDeterminismTests
{
    public static void TwoRuns_SameInput_SameOutput()
    {
        var (state1, state2) = RunPipelineTwice();

        Assert(state1.PlacedPanels.Count == state2.PlacedPanels.Count,
            $"placed-panel count mismatch: {state1.PlacedPanels.Count} vs {state2.PlacedPanels.Count}");

        Assert(state1.History.Count == state2.History.Count,
            $"history count mismatch: {state1.History.Count} vs {state2.History.Count}");

        // Bit-exact: residuals are accumulated in a fixed order so the
        // double values must compare equal, not just within tolerance.
        Assert(state1.TotalResidual.Equals(state2.TotalResidual),
            $"residual drift: {state1.TotalResidual} vs {state2.TotalResidual}");

        for (int i = 0; i < state1.PlacedPanels.Count; i++)
        {
            var p1 = state1.PlacedPanels[i];
            var p2 = state2.PlacedPanels[i];
            Assert(p1.Id == p2.Id,
                $"placed-panel order mismatch at {i}: {p1.Id} vs {p2.Id}");

            var t1 = state1.AppliedTransforms[p1.Id];
            var t2 = state2.AppliedTransforms[p2.Id];
            Assert(TransformsEqual(t1, t2),
                $"transform drift for panel {p1.Id}");
        }
    }

    public static void TwoRuns_HashIdentical()
    {
        var (state1, state2) = RunPipelineTwice();
        string h1 = HashState(state1);
        string h2 = HashState(state2);
        Assert(h1 == h2, $"hash mismatch: {h1} vs {h2}");
    }

    private static (AssemblyState, AssemblyState) RunPipelineTwice()
    {
        var frame = MakeFramePanel();
        var shards = new List<Panel>
        {
            MakeShard("s001", 100, 100, 30),
            MakeShard("s002", 200, 100, 25),
            MakeShard("s003", 150, 200, 35),
        };

        AssemblyState Run()
        {
            var index = new SegmentHashIndex();
            foreach (var s in shards)
                foreach (var seg in BoundarySegmenter.Segment(s, new SegmenterOptions()))
                    index.Add(seg);
            foreach (var seg in BoundarySegmenter.Segment(frame, new SegmenterOptions()))
                index.Add(seg);

            var solver = new AssemblySolver(index);
            return solver.Solve(new[] { frame }, shards);
        }

        // Reset anchored flag between runs since Solve mutates it.
        var s1 = Run();
        frame.IsAnchored = false;
        var s2 = Run();
        return (s1, s2);
    }

    private static Panel MakeFramePanel()
    {
        var poly = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(500, 0, 0),
            new Point3d(500, 400, 0),
            new Point3d(0, 400, 0),
            new Point3d(0, 0, 0),
        };
        return new Panel("frame", poly.ToPolylineCurve(), PanelKind.Frame);
    }

    private static Panel MakeShard(string id, double cx, double cy, double size)
    {
        var poly = new Polyline
        {
            new Point3d(cx - size, cy - size, 0),
            new Point3d(cx + size, cy - size, 0),
            new Point3d(cx + size * 0.8, cy + size, 0),
            new Point3d(cx - size * 1.1, cy + size * 0.9, 0),
            new Point3d(cx - size, cy - size, 0),
        };
        return new Panel(id, poly.ToPolylineCurve(), PanelKind.Shard);
    }

    private static bool TransformsEqual(Transform a, Transform b)
    {
        return a.M00.Equals(b.M00) && a.M01.Equals(b.M01) && a.M02.Equals(b.M02) && a.M03.Equals(b.M03)
            && a.M10.Equals(b.M10) && a.M11.Equals(b.M11) && a.M12.Equals(b.M12) && a.M13.Equals(b.M13)
            && a.M20.Equals(b.M20) && a.M21.Equals(b.M21) && a.M22.Equals(b.M22) && a.M23.Equals(b.M23)
            && a.M30.Equals(b.M30) && a.M31.Equals(b.M31) && a.M32.Equals(b.M32) && a.M33.Equals(b.M33);
    }

    private static string HashState(AssemblyState s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"R={s.TotalResidual:R}|");
        foreach (var p in s.PlacedPanels)
        {
            var t = s.AppliedTransforms[p.Id];
            sb.Append($"{p.Id}:{t.M00:R},{t.M01:R},{t.M03:R},{t.M10:R},{t.M11:R},{t.M13:R};");
        }
        return sb.ToString();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
