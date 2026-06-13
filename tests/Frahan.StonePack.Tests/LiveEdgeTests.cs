#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Frahan.EdgeMatching;

namespace Frahan.Tests;

// =============================================================================
// LiveEdgeTests -- unit tests for the live-edge flooring 2D edge-matching Core
// (LiveEdgeClassifier / LiveEdgeBoard / LiveEdgeScribeMatcher / LiveEdgeLayup).
//
// Rhino-LIGHT: only Point3d value math is exercised, so these run headless
// without rhcommon_c.dll (no native P/Invoke).
// =============================================================================

static class LiveEdgeTests
{
    // Deterministic LCG so fixtures reproduce.
    private sealed class Lcg
    {
        private ulong _s;
        public Lcg(ulong seed) { _s = seed; }
        public double Next() { unchecked { _s = _s * 6364136223846793005UL + 1442695040888963407UL; } return ((_s >> 33) & 0x7fffffff) / 2147483647.0; }
    }

    // Synthesise one irregular offcut: skewed quad, two curvy live edges + two straight sawn ends.
    private static List<Point3d> MakeBoard(Lcg r)
    {
        double wb = 95 + r.Next() * 60, hb = 54 + r.Next() * 12;
        var bl = new Point3d(0, (r.Next() - 0.5) * 4, 0);
        var br = new Point3d(wb, (r.Next() - 0.5) * 4, 0);
        var tr = new Point3d(wb + (r.Next() - 0.5) * 6, hb + (r.Next() - 0.5) * 4, 0);
        var tl = new Point3d((r.Next() - 0.5) * 6, hb + (r.Next() - 0.5) * 4, 0);
        int m = 40;
        Point3d[] Live(Point3d a, Point3d b, double amp)
        {
            var dir = b - a; double len = dir.Length; dir.Unitize();
            var nrm = new Vector3d(-dir.Y, dir.X, 0);
            double p1 = r.Next() * 6.28, p2 = r.Next() * 6.28, p3 = r.Next() * 6.28;
            double k1 = 1 + Math.Floor(r.Next() * 2), k2 = 2 + Math.Floor(r.Next() * 2), k3 = 3 + Math.Floor(r.Next() * 3);
            var pts = new Point3d[m];
            for (int i = 0; i < m; i++)
            {
                double t = (double)i / (m - 1);
                double off = amp * (Math.Sin(Math.PI * k1 * t + p1) + 0.5 * Math.Sin(Math.PI * k2 * t + p2) + 0.22 * Math.Sin(Math.PI * k3 * t + p3));
                off += (r.Next() - 0.5) * amp * 0.22;
                off *= Math.Sin(Math.PI * t) * 0.55 + 0.45;
                pts[i] = a + dir * (len * t) + nrm * off;
            }
            pts[0] = a; pts[m - 1] = b;
            return pts;
        }
        double ampv = 3 + r.Next() * 3;
        var bottom = Live(bl, br, ampv);
        var top = Live(tr, tl, ampv);
        Point3d[] Str(Point3d a, Point3d b, int n) { var arr = new Point3d[n]; for (int i = 0; i < n; i++) arr[i] = a + (b - a) * ((double)i / (n - 1)); return arr; }
        var right = Str(br, tr, 6);
        var left = Str(tl, bl, 6);
        var loop = new List<Point3d>();
        loop.AddRange(bottom);
        for (int i = 1; i < right.Length; i++) loop.Add(right[i]);
        for (int i = 1; i < top.Length; i++) loop.Add(top[i]);
        for (int i = 1; i < left.Length; i++) loop.Add(left[i]);
        return loop;
    }

    public static void Classify_IrregularOutlines_TwoLiveTwoSawnAlternating()
    {
        var r = new Lcg(77777);
        int ok = 0, total = 16;
        for (int i = 0; i < total; i++)
        {
            var outline = MakeBoard(r);
            var c = LiveEdgeClassifier.Classify(outline);
            int nLive = c.IsLive.Count(f => f);
            bool opposite = c.IsLive[0] == c.IsLive[2] && c.IsLive[1] == c.IsLive[3] && c.IsLive[0] != c.IsLive[1];
            // the live edges (curvy) must be less straight than the sawn ends
            double maxLive = Enumerable.Range(0, 4).Where(e => c.IsLive[e]).Max(e => c.Straightness[e]);
            double minSawn = Enumerable.Range(0, 4).Where(e => !c.IsLive[e]).Min(e => c.Straightness[e]);
            if (nLive == 2 && opposite && minSawn > maxLive) ok++;
        }
        Assert(ok == total, $"classify expected {total}/{total}, got {ok}/{total}");
    }

    public static void Extract_IrregularOutline_ProducesValidBoard()
    {
        var r = new Lcg(123);
        var board = LiveEdgeBoard.Extract(MakeBoard(r));
        Assert(board != null, "extract returned null on a valid offcut");
        Assert(board.SampleCount >= 2, "board has too few samples");
        Assert(board.Width > 50 && board.Width < 200, $"board width out of range: {board.Width}");
        Assert(board.NominalHeight > 30 && board.NominalHeight < 90, $"board height out of range: {board.NominalHeight}");
    }

    private static List<LiveEdgeBoard> Pool(int n, ulong seed)
    {
        var r = new Lcg(seed);
        var pool = new List<LiveEdgeBoard>();
        for (int i = 0; i < n; i++) { var b = LiveEdgeBoard.Extract(MakeBoard(r)); if (b != null) pool.Add(b); }
        return pool;
    }

    public static void Layup_Greedy_DeterministicAndTrimBounded()
    {
        var opt = new LiveEdgeLayupOptions { Mode = LiveEdgeLayupMode.Greedy };
        var a = LiveEdgeLayup.Solve(Pool(80, 4242), opt);
        var b = LiveEdgeLayup.Solve(Pool(80, 4242), opt);
        Assert(a.Placed > 10, $"expected >10 boards placed, got {a.Placed}");
        Assert(a.Placed == b.Placed, "layup not deterministic (placed count differs)");
        Assert(Math.Abs(a.MeanTrim - b.MeanTrim) < 1e-9, "layup not deterministic (mean trim differs)");
        // scribe trim should be a few mm, well under the course height.
        Assert(a.MeanTrim < opt.CourseHeight / 8.0, $"mean trim too high: {a.MeanTrim}");
    }

    public static void Layup_Hungarian_TotalTrimNotWorseThanGreedy()
    {
        var pool = Pool(80, 909090);
        var greedy = LiveEdgeLayup.Solve(pool, new LiveEdgeLayupOptions { Mode = LiveEdgeLayupMode.Greedy });
        var hung = LiveEdgeLayup.Solve(pool, new LiveEdgeLayupOptions { Mode = LiveEdgeLayupMode.Hungarian });
        Assert(hung.Placed == greedy.Placed, $"hungarian placed {hung.Placed} != greedy {greedy.Placed}");
        // Hungarian assigns optimally over the same (greedy-fixed) slots, so total trim cannot be worse.
        Assert(hung.MeanTrim <= greedy.MeanTrim + 1e-6, $"hungarian mean trim {hung.MeanTrim} worse than greedy {greedy.MeanTrim}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
