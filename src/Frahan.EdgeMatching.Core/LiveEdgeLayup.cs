#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching;

// =============================================================================
// LiveEdgeLayup -- lays a pool of classified offcuts into a staggered live-edge
// floor (Case A: live edges run along the course as continuous wavy "river"
// seams; the short sawn butt joints are staggered brick-bond). Drives Live Edge
// Stagger Layup (D5F10046).
//
// Two assignment modes:
//   Greedy    -- fast, order-dependent local optimum: each slot takes the
//                remaining offcut with least scribe trim plus a stagger penalty.
//   Hungarian -- the greedy pass fixes the (already-staggered) slot geometry,
//                then HungarianAssigner re-assigns the pool to those slots for
//                the globally minimal total scribe trim. Same assigner that
//                drives Template Panel Match (D5F10007) / cyclopean voussoir
//                matching; the predefined rivers make the slot costs static, so
//                the bipartite-assignment (Kuhn 1955) form applies. The stagger
//                lives in the slot x-positions, which Hungarian preserves.
//
// Rhino-LIGHT: Point3d used only as a value container.
// =============================================================================

public enum LiveEdgeLayupMode { Greedy, Hungarian }

public sealed class LiveEdgeLayupOptions
{
    public double FloorWidth = 520;
    public int Courses = 5;
    public double CourseHeight = 60;
    public double RiverAmplitude = 5;
    public double Dx = 2.0;
    public int Seed = 313131;
    public double StaggerMinOffset = 24;
    public LiveEdgeLayupMode Mode = LiveEdgeLayupMode.Greedy;
}

public sealed class PlacedBoard
{
    public LiveEdgeScribeMatcher.ScribeResult Scribe;
    public int Course;
    public int PoolIndex;
    public int[] ColorRgb;
}

public sealed class LiveEdgeLayupResult
{
    public List<PlacedBoard> Boards = new List<PlacedBoard>();
    public List<Point3d[]> Rivers = new List<Point3d[]>();        // R+1 seams, river[0]=base, river[R]=top
    public List<Point3d[]> ButtJoints = new List<Point3d[]>();    // 2-point lines
    public double MeanTrim;
    public double MaxTrim;
    public int Placed;
}

public static class LiveEdgeLayup
{
    private static readonly int[][] Palette =
    {
        new[]{201,166,107}, new[]{185,140,82}, new[]{216,190,142}, new[]{169,116,63},
        new[]{193,154,91},  new[]{227,207,163}, new[]{181,133,74}, new[]{205,166,112},
        new[]{156,107,58},  new[]{221,197,150}, new[]{203,176,121}, new[]{191,160,106}
    };

    public static LiveEdgeLayupResult Solve(IReadOnlyList<LiveEdgeBoard> pool, LiveEdgeLayupOptions opt)
    {
        var result = new LiveEdgeLayupResult();
        if (pool == null || pool.Count == 0) return result;

        ulong s = (ulong)opt.Seed;
        double Rnd() { unchecked { s = s * 6364136223846793005UL + 1442695040888963407UL; } return ((s >> 33) & 0x7fffffff) / 2147483647.0; }

        double fw = opt.FloorWidth, hc = opt.CourseHeight, dx = opt.Dx, av = opt.RiverAmplitude;
        int r = opt.Courses;
        int nx = (int)(fw / dx) + 1;
        int Gi(double x) { int g = (int)Math.Round(x / dx); return g < 0 ? 0 : (g >= nx ? nx - 1 : g); }

        var river = new double[r + 1][];
        for (int k = 0; k <= r; k++)
        {
            double p1 = Rnd() * 6.28, p2 = Rnd() * 6.28;
            double w1 = 2 * Math.PI / (150 + Rnd() * 80), w2 = 2 * Math.PI / (80 + Rnd() * 40);
            double a2 = 0.45 + Rnd() * 0.2;
            river[k] = new double[nx];
            for (int i = 0; i < nx; i++) river[k][i] = k * hc + av * (Math.Sin(w1 * i * dx + p1) + a2 * Math.Sin(w2 * i * dx + p2));
        }
        foreach (var rv in river)
            result.Rivers.Add(Enumerable.Range(0, nx).Select(i => new Point3d(i * dx, rv[i], 0)).ToArray());

        // ---- greedy pass: fix the (staggered) slot geometry + a greedy assignment ----
        var slots = new List<(int course, double x0, double span, int greedy)>();
        var used = new bool[pool.Count];
        var prevJoints = new List<double>();
        for (int c = 0; c < r; c++)
        {
            double cursor = 0; var joints = new List<double>(); int guard = 0;
            while (cursor < fw - 12 && guard++ < 80)
            {
                int best = -1; double bestScore = 1e18;
                for (int b = 0; b < pool.Count; b++)
                {
                    if (used[b]) continue;
                    double span = pool[b].Width;
                    if (cursor + span > fw + 15) continue;
                    double cost = LiveEdgeScribeMatcher.SlotCost(pool[b], span, river[c], river[c + 1], dx, cursor);
                    double jx = cursor + span; double pen = 0;
                    foreach (var pj in prevJoints) { double d = Math.Abs(jx - pj); if (d < opt.StaggerMinOffset) pen += (opt.StaggerMinOffset - d) * 0.5; }
                    double score = cost + pen;
                    if (score < bestScore) { bestScore = score; best = b; }
                }
                if (best < 0) break;
                used[best] = true;
                slots.Add((c, cursor, pool[best].Width, best));
                joints.Add(cursor + pool[best].Width);
                cursor += pool[best].Width;
            }
            prevJoints = joints;
        }

        // ---- assignment ----
        int[] assign;
        if (opt.Mode == LiveEdgeLayupMode.Hungarian && slots.Count > 0)
        {
            int ns = slots.Count, np = pool.Count;
            var cost = new double[ns * np];
            for (int si = 0; si < ns; si++)
            {
                var sl = slots[si];
                for (int bi = 0; bi < np; bi++)
                    cost[si * np + bi] = LiveEdgeScribeMatcher.SlotCost(pool[bi], sl.span, river[sl.course], river[sl.course + 1], dx, sl.x0);
            }
            assign = HungarianAssigner.Solve(cost, ns, np); // slot -> pool index
        }
        else
        {
            assign = slots.Select(sl => sl.greedy).ToArray();
        }

        // ---- build placed boards ----
        double trimSum = 0; int trimN = 0;
        int curCourse = -1, idxInCourse = 0;
        for (int si = 0; si < slots.Count; si++)
        {
            var sl = slots[si];
            if (sl.course != curCourse) { curCourse = sl.course; idxInCourse = 0; }
            int bi = (si < assign.Length) ? assign[si] : HungarianAssigner.Unassigned;
            if (bi < 0 || bi >= pool.Count) { idxInCourse++; continue; }
            var sc = LiveEdgeScribeMatcher.Scribe(pool[bi], sl.span, river[sl.course], river[sl.course + 1], dx, sl.x0);
            result.Boards.Add(new PlacedBoard
            {
                Scribe = sc,
                Course = sl.course,
                PoolIndex = bi,
                ColorRgb = Palette[(sl.course * 7 + idxInCourse) % Palette.Length]
            });
            trimSum += sc.MeanTrim; trimN++;
            if (sc.MaxTrim > result.MaxTrim) result.MaxTrim = sc.MaxTrim;
            if (sl.x0 > 1)
                result.ButtJoints.Add(new[] { new Point3d(sl.x0, river[sl.course][Gi(sl.x0)], 0), new Point3d(sl.x0, river[sl.course + 1][Gi(sl.x0)], 0) });
            idxInCourse++;
        }
        result.MeanTrim = trimN > 0 ? trimSum / trimN : 0;
        result.Placed = result.Boards.Count;
        return result;
    }
}
