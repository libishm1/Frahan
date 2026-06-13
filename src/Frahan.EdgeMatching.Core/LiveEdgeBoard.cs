#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching;

// =============================================================================
// LiveEdgeBoard -- a classified offcut oriented for layup: its two LIVE edges
// sampled as height functions y = f(x) along the board length, plus its width.
// Used by Live Edge Match (D5F10044) and Live Edge Stagger Layup (D5F10046).
//
// Orientation: the board is rotated so the axis between the two sawn-end
// midpoints lies on +x (sawn ends vertical, live edges running along x), then
// translated so its left corner is at x = 0. The lower-mean-y live edge is the
// bottom edge, the other is the top.
//
// Rhino-LIGHT: Point3d used only as a value container.
// =============================================================================

public sealed class LiveEdgeBoard
{
    // Bottom/Top live edge heights sampled at x = i*Dx, i in [0, Length-1].
    public double[] BottomY;
    public double[] TopY;
    public double Width;
    public double Dx;

    public int SampleCount => BottomY == null ? 0 : BottomY.Length;
    public double NominalHeight => SampleCount == 0 ? 0 : (TopY.Average() - BottomY.Average());

    // Build a board from a raw closed outline. Returns null if the outline does
    // not classify to two live + two sawn edges.
    public static LiveEdgeBoard Extract(IReadOnlyList<Point3d> outline, double dx = 2.0, int resampleN = 160)
    {
        var c = LiveEdgeClassifier.Classify(outline, resampleN);
        var loop = c.Loop;
        int n = loop.Length;

        var sawn = Enumerable.Range(0, 4).Where(e => !c.IsLive[e]).ToList();
        if (sawn.Count != 2) return null;
        var liveE = Enumerable.Range(0, 4).Where(e => c.IsLive[e]).ToList();
        if (liveE.Count != 2) return null;

        Point3d Mid(int e)
        {
            var s = c.EdgePoints(e);
            return s[0] + (s[s.Count - 1] - s[0]) * 0.5;
        }
        Vector3d axis = Mid(sawn[1]) - Mid(sawn[0]);
        if (axis.Length < 1e-6) return null;
        axis.Unitize();
        double ang = -Math.Atan2(axis.Y, axis.X), ca = Math.Cos(ang), sa = Math.Sin(ang);
        Point3d Rot(Point3d p) => new Point3d(p.X * ca - p.Y * sa, p.X * sa + p.Y * ca, 0);

        var rr = loop.Select(Rot).ToArray();
        List<Point3d> RotEdge(int e)
        {
            int a = c.Corners[e], b = c.Corners[(e + 1) % 4];
            var seg = new List<Point3d>();
            int j = a;
            while (true) { seg.Add(rr[j]); if (j == b) break; j = (j + 1) % n; }
            return seg;
        }

        var l0 = RotEdge(liveE[0]);
        var l1 = RotEdge(liveE[1]);
        double my0 = l0.Average(p => p.Y), my1 = l1.Average(p => p.Y);
        var botE = my0 < my1 ? l0 : l1;
        var topE = my0 < my1 ? l1 : l0;

        double minx = rr.Min(p => p.X);
        botE = botE.Select(p => new Point3d(p.X - minx, p.Y, 0)).ToList();
        topE = topE.Select(p => new Point3d(p.X - minx, p.Y, 0)).ToList();
        double width = rr.Max(p => p.X) - minx;
        int ln = Math.Max(2, (int)Math.Round(width / dx) + 1);

        double YAt(List<Point3d> pl, double x)
        {
            var sp = pl.OrderBy(p => p.X).ToList();
            if (x <= sp[0].X) return sp[0].Y;
            if (x >= sp[sp.Count - 1].X) return sp[sp.Count - 1].Y;
            for (int i = 1; i < sp.Count; i++)
                if (sp[i].X >= x)
                {
                    double t = (x - sp[i - 1].X) / Math.Max(1e-9, sp[i].X - sp[i - 1].X);
                    return sp[i - 1].Y + (sp[i].Y - sp[i - 1].Y) * t;
                }
            return sp[sp.Count - 1].Y;
        }

        var bY = new double[ln];
        var tY = new double[ln];
        for (int i = 0; i < ln; i++)
        {
            double x = Math.Min(width, i * dx);
            bY[i] = YAt(botE, x);
            tY[i] = YAt(topE, x);
        }
        return new LiveEdgeBoard { BottomY = bY, TopY = tY, Width = width, Dx = dx };
    }
}
