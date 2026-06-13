#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching;

// =============================================================================
// LiveEdgeDemo -- a deterministic synthetic pool of irregular plain-sawn wood
// offcuts (skewed quad, two curvy live edges + two straight sawn ends) so the
// live-edge components are self-presenting with no input and the example
// reproduces cold. Demo-only generator; real workflows feed scanned outlines.
// =============================================================================

public static class LiveEdgeDemo
{
    public static List<Point3d[]> SyntheticOutlines(int count, int seed)
    {
        ulong s = (ulong)seed;
        double Rnd() { unchecked { s = s * 6364136223846793005UL + 1442695040888963407UL; } return ((s >> 33) & 0x7fffffff) / 2147483647.0; }

        var outlines = new List<Point3d[]>();
        for (int n = 0; n < count; n++)
        {
            double wb = 95 + Rnd() * 60, hb = 54 + Rnd() * 12;
            var bl = new Point3d(0, (Rnd() - 0.5) * 4, 0);
            var br = new Point3d(wb, (Rnd() - 0.5) * 4, 0);
            var tr = new Point3d(wb + (Rnd() - 0.5) * 6, hb + (Rnd() - 0.5) * 4, 0);
            var tl = new Point3d((Rnd() - 0.5) * 6, hb + (Rnd() - 0.5) * 4, 0);
            int m = 40;

            Point3d[] Live(Point3d a, Point3d b, double amp)
            {
                var dir = b - a; double len = dir.Length; dir.Unitize();
                var nrm = new Vector3d(-dir.Y, dir.X, 0);
                double p1 = Rnd() * 6.28, p2 = Rnd() * 6.28, p3 = Rnd() * 6.28;
                double k1 = 1 + Math.Floor(Rnd() * 2), k2 = 2 + Math.Floor(Rnd() * 2), k3 = 3 + Math.Floor(Rnd() * 3);
                var pts = new Point3d[m];
                for (int i = 0; i < m; i++)
                {
                    double t = (double)i / (m - 1);
                    double off = amp * (Math.Sin(Math.PI * k1 * t + p1) + 0.5 * Math.Sin(Math.PI * k2 * t + p2) + 0.22 * Math.Sin(Math.PI * k3 * t + p3));
                    off += (Rnd() - 0.5) * amp * 0.22;
                    off *= Math.Sin(Math.PI * t) * 0.55 + 0.45;
                    pts[i] = a + dir * (len * t) + nrm * off;
                }
                pts[0] = a; pts[m - 1] = b;
                return pts;
            }
            double ampv = 3 + Rnd() * 3;
            var bottom = Live(bl, br, ampv);
            var top = Live(tr, tl, ampv);
            Point3d[] Str(Point3d a, Point3d b, int cnt) { var arr = new Point3d[cnt]; for (int i = 0; i < cnt; i++) arr[i] = a + (b - a) * ((double)i / (cnt - 1)); return arr; }
            var right = Str(br, tr, 6);
            var left = Str(tl, bl, 6);

            var loop = new List<Point3d>();
            loop.AddRange(bottom);
            for (int i = 1; i < right.Length; i++) loop.Add(right[i]);
            for (int i = 1; i < top.Length; i++) loop.Add(top[i]);
            for (int i = 1; i < left.Length; i++) loop.Add(left[i]);
            loop.Add(loop[0]);
            outlines.Add(loop.ToArray());
        }
        return outlines;
    }
}
