#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Whole-side best-first reassembler. Builds the validated 3x3 wavy-square jigsaw
// (deterministic, seed 7 -- the same configuration the live harness solved 0/9 -> 7/9
// -> 9/9), scatters+rotates every piece, anchors the centre at its ground-truth slot,
// and asserts the solver reassembles all 9 pieces onto their true slots,
// deterministically. Needs Rhino runtime (SKIPs standalone, runs in-process).
static class WholeSideAssemblerTests
{
    private const int Centre = 4; // 3x3 centre index

    public static void WholeSide_WavyJigsaw_AllPlaced()
    {
        var (truth, scattered) = BuildJigsaw();
        int n = truth.Count; // 9

        var state = Solve(truth, scattered);

        Assert(state.PlacedPanels.Count == n,
            $"placed {state.PlacedPanels.Count}/{n}");

        foreach (var p in state.PlacedPanels)
            Assert(state.AppliedTransforms.ContainsKey(p.Id),
                $"AppliedTransforms missing key for placed panel {p.Id}");

        // Anchor is at its truth slot, so every correct mate lands on its truth slot.
        // A wrong (false-adjacency) placement would be a whole piece-width off.
        foreach (var p in state.PlacedPanels)
        {
            int i = int.Parse(p.Id.Substring(1));
            var src = i == Centre ? truth[i] : scattered[i];
            var c = (PolylineCurve)src.DuplicateCurve();
            c.Transform(state.AppliedTransforms[p.Id]);
            double err = c.GetBoundingBox(true).Center.DistanceTo(truth[i].GetBoundingBox(true).Center);
            Assert(err < 10.0, $"piece {p.Id} placed {err:F1}mm from its ground-truth slot (piece ~107x73mm)");
        }
    }

    public static void WholeSide_Deterministic()
    {
        var (truth, scattered) = BuildJigsaw();
        var s1 = Solve(truth, scattered);
        var s2 = Solve(truth, scattered);

        Assert(s1.PlacedPanels.Count == s2.PlacedPanels.Count,
            $"placed count differs across runs: {s1.PlacedPanels.Count} vs {s2.PlacedPanels.Count}");
        Assert(s1.TotalResidual.Equals(s2.TotalResidual),
            $"residual drift: {s1.TotalResidual} vs {s2.TotalResidual}");
        for (int i = 0; i < s1.PlacedPanels.Count; i++)
            Assert(s1.PlacedPanels[i].Id == s2.PlacedPanels[i].Id,
                $"placement order differs at {i}: {s1.PlacedPanels[i].Id} vs {s2.PlacedPanels[i].Id}");
    }

    private static AssemblyState Solve(List<PolylineCurve> truth, List<PolylineCurve> scattered)
    {
        var anchor = new Panel("p" + Centre, truth[Centre], PanelKind.Shard);
        var pool = new List<Panel>();
        for (int i = 0; i < truth.Count; i++)
            if (i != Centre) pool.Add(new Panel("p" + i, scattered[i], PanelKind.Shard));
        return new BestFirstAssembler(new AssemblyOptions { WholeSideFitGate = 2.5 }).Solve(new[] { anchor }, pool);
    }

    // The validated 3x3 wavy-square jigsaw: interior edges carry a deterministic seam
    // wave shared (reversed) between the two pieces meeting there; border edges straight.
    private static (List<PolylineCurve> truth, List<PolylineCurve> scattered) BuildJigsaw()
    {
        const double W = 320, Hh = 220;
        const int R = 3, C = 3, nEdge = 20;
        const double ampFrac = 0.16;
        var rng = new Random(7);

        Func<int, int, Point3d> node = (r, c) => new Point3d(c * W / C, r * Hh / R, 0);
        Func<int, int, int, int, string> key = (rA, cA, rB, cB) =>
        {
            int aa = rA * 100 + cA, bb = rB * 100 + cB;
            if (aa > bb) { int t = aa; aa = bb; bb = t; }
            return aa + "_" + bb;
        };
        var cache = new Dictionary<string, List<Point3d>>();
        Func<int, int, int, int, List<Point3d>> edge = (rA, cA, rB, cB) =>
        {
            string k = key(rA, cA, rB, cB);
            bool fwd = (rA * 100 + cA) < (rB * 100 + cB);
            if (cache.TryGetValue(k, out var cc)) return fwd ? cc : Enumerable.Reverse(cc).ToList();
            var p0 = node(rA, cA); var p1 = node(rB, cB);
            bool horiz = rA == rB;
            bool inner = horiz ? (rA > 0 && rA < R) : (cA > 0 && cA < C);
            var dir = p1 - p0; double len = dir.Length; dir.Unitize();
            var perp = new Vector3d(-dir.Y, dir.X, 0);
            double amp = ampFrac * (horiz ? (Hh / R) : (W / C));
            double f1 = 1.5 + rng.NextDouble() * 2.5, ph1 = rng.NextDouble() * Math.PI * 2, a1 = 0.6 + rng.NextDouble() * 0.4;
            double f2 = 2.5 + rng.NextDouble() * 3.0, ph2 = rng.NextDouble() * Math.PI * 2, a2 = 0.3 + rng.NextDouble() * 0.4;
            double sgn = rng.Next(2) == 0 ? 1 : -1;
            var seg = new List<Point3d>();
            for (int i = 0; i <= nEdge; i++)
            {
                double t = (double)i / nEdge;
                var b = p0 + dir * (len * t);
                double off = 0;
                if (inner)
                {
                    double env = Math.Sin(Math.PI * t);
                    double wig = a1 * Math.Sin(2 * Math.PI * f1 * t + ph1) + a2 * Math.Sin(2 * Math.PI * f2 * t + ph2);
                    off = sgn * amp * env * wig / (a1 + a2);
                }
                seg.Add(b + perp * off);
            }
            cache[k] = fwd ? seg : Enumerable.Reverse(seg).ToList();
            return seg;
        };

        var truth = new List<PolylineCurve>();
        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++)
            {
                var loop = new List<Point3d>();
                Action<List<Point3d>, bool> add = (e, first) =>
                {
                    for (int i = first ? 0 : 1; i < e.Count; i++) loop.Add(e[i]);
                };
                add(edge(r, c, r, c + 1), true);
                add(edge(r, c + 1, r + 1, c + 1), false);
                add(edge(r + 1, c + 1, r + 1, c), false);
                add(edge(r + 1, c, r, c), false);
                if (loop[0].DistanceTo(loop[loop.Count - 1]) > 1e-9) loop.Add(loop[0]);
                truth.Add(new Polyline(loop).ToPolylineCurve());
            }

        double pitch = Math.Max(W / C, Hh / R) * 1.6;
        var scattered = new List<PolylineCurve>();
        for (int i = 0; i < truth.Count; i++)
        {
            var ctr = truth[i].GetBoundingBox(true).Center;
            var c = (PolylineCurve)truth[i].DuplicateCurve();
            c.Transform(Transform.Translation((i % C) * pitch - ctr.X, (i / C) * pitch - ctr.Y, 0));
            c.Rotate((i * 1.7) % 6.28, Vector3d.ZAxis, c.GetBoundingBox(true).Center);
            scattered.Add(c);
        }
        return (truth, scattered);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
