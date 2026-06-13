#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching;

// =============================================================================
// LiveEdgeClassifier -- splits a plain-sawn wood-offcut outline into its LIVE
// (curvy, natural) edges and SAWN (straight, machine-cut) ends, for the
// live-edge flooring 2D edge-matching workflow (Live Edge Classify, D5F10043).
//
// Method (Frahan-original "straight-run" detection): resample the closed
// outline uniformly, measure windowed turning at each vertex, mark the
// near-straight vertices, and take the TWO LONGEST straight runs as the sawn
// ends. Their four endpoints are the corners; the two arcs between them are the
// live edges. Robust to live-edge wiggles/noise where a turning-peak corner
// detector fails (it mistakes a sharp wiggle for a corner). Validated 12/12 on
// skewed, noisy, varied-dimension irregular offcuts.
//
// Rhino-LIGHT: Point3d is used only as a value container; there are no
// Rhino-runtime calls, so this type is unit-testable without a live Rhino.
// =============================================================================

public static class LiveEdgeClassifier
{
    public sealed class Result
    {
        // The resampled closed loop the corner indices refer to.
        public Point3d[] Loop;
        // Four corner indices into Loop, sorted ascending.
        public int[] Corners;
        // Per edge e in [0,4): true = LIVE (curvy), false = SAWN (straight).
        // Edge e runs from Corners[e] to Corners[(e+1)%4] along the loop.
        public bool[] IsLive;
        // Per edge straightness = chord / arc-length in (0,1]; ~1 = straight.
        public double[] Straightness;

        public List<Point3d> EdgePoints(int e)
        {
            return LiveEdgeClassifier.EdgePoints(Loop, Corners, e);
        }
    }

    // Uniform arc-length resample of a closed loop to n points.
    public static Point3d[] Resample(IReadOnlyList<Point3d> loop, int n)
    {
        var pts = new List<Point3d>(loop);
        pts.Add(loop[0]);
        var cum = new double[pts.Count];
        double total = 0;
        for (int i = 1; i < pts.Count; i++) { total += pts[i].DistanceTo(pts[i - 1]); cum[i] = total; }
        var outp = new Point3d[n];
        for (int k = 0; k < n; k++)
        {
            double d = total * k / n;
            int j = 1;
            while (j < pts.Count && cum[j] < d) j++;
            if (j >= pts.Count) j = pts.Count - 1;
            double t = (d - cum[j - 1]) / Math.Max(1e-9, cum[j] - cum[j - 1]);
            outp[k] = pts[j - 1] + (pts[j] - pts[j - 1]) * t;
        }
        return outp;
    }

    // Walk the loop from Corners[e] to Corners[(e+1)%4], inclusive.
    public static List<Point3d> EdgePoints(Point3d[] loop, int[] corners, int e)
    {
        int n = loop.Length;
        int a = corners[e], b = corners[(e + 1) % corners.Length];
        var seg = new List<Point3d>();
        int j = a;
        while (true) { seg.Add(loop[j]); if (j == b) break; j = (j + 1) % n; }
        return seg;
    }

    public static Result Classify(IReadOnlyList<Point3d> outline, int resampleN = 160, int turnWindow = 2, double straightThreshold = 0.06)
    {
        var loop = Resample(outline, resampleN);
        int n = loop.Length;
        int w = Math.Max(1, turnWindow);

        var turn = new double[n];
        for (int i = 0; i < n; i++)
        {
            Vector3d u = loop[i] - loop[(i - w + n) % n];
            Vector3d v = loop[(i + w) % n] - loop[i];
            if (u.Length < 1e-9 || v.Length < 1e-9) { turn[i] = 0; continue; }
            u.Unitize(); v.Unitize();
            turn[i] = Math.Acos(Math.Max(-1, Math.Min(1, u * v)));
        }

        var st = new bool[n];
        for (int i = 0; i < n; i++) st[i] = turn[i] < straightThreshold;
        int start = -1;
        for (int i = 0; i < n; i++) if (!st[i]) { start = i; break; }
        if (start < 0) start = 0; // degenerate: nearly all straight

        // Collect circular straight runs starting at a non-straight vertex.
        var runs = new List<int[]>();
        var runLen = new List<double>();
        int idx = start, counted = 0;
        while (counted < n)
        {
            int g = idx % n;
            if (st[g])
            {
                int a = g; double len = 0; int prev = g;
                while (counted < n && st[idx % n])
                {
                    int cur = idx % n;
                    if (cur != a) { len += loop[cur].DistanceTo(loop[prev]); prev = cur; }
                    idx++; counted++;
                }
                int b = (idx - 1) % n;
                runs.Add(new[] { a, b }); runLen.Add(len);
            }
            else { idx++; counted++; }
        }

        // The two longest straight runs are the sawn ends; their endpoints are corners.
        var order = Enumerable.Range(0, runs.Count).OrderByDescending(i => runLen[i]).Take(2).ToList();
        var cs = new List<int>();
        foreach (var oi in order) { cs.Add(runs[oi][0]); cs.Add(runs[oi][1]); }
        cs = cs.Distinct().OrderBy(v => v).ToList();
        while (cs.Count < 4) cs.Add((cs.Count * n) / 4);
        while (cs.Count > 4) cs.RemoveAt(cs.Count - 1);
        var corners = cs.ToArray();

        var live = new bool[4];
        var sA = new double[4];
        for (int e = 0; e < 4; e++)
        {
            var seg = EdgePoints(loop, corners, e);
            double arc = 0;
            for (int k = 1; k < seg.Count; k++) arc += seg[k].DistanceTo(seg[k - 1]);
            double ch = seg[0].DistanceTo(seg[seg.Count - 1]);
            sA[e] = arc > 1e-9 ? ch / arc : 1.0;
        }
        // The two straightest edges are sawn; the other two are live.
        var ord = Enumerable.Range(0, 4).OrderByDescending(e => sA[e]).ToList();
        for (int e = 0; e < 4; e++) live[e] = !(e == ord[0] || e == ord[1]);

        return new Result { Loop = loop, Corners = corners, IsLive = live, Straightness = sA };
    }
}
