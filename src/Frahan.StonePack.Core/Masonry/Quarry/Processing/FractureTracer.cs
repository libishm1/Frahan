#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// FractureTracer -- discrete fracture picks -> continuous fracture LINES.
//
// Groups the per-column local-maxima picks (FractureExtractor) into connected
// reflectors and traces one continuous polyline per reflector (median depth per
// trace column -> Douglas-Peucker simplify), tagging span / dip / mean energy and
// a fracture id per pick. Two grouping modes:
//
//   ConnectedComponents -- 8-connectivity flood fill on the pick set. Simple and
//     robust; MERGES fractures that physically cross/touch.
//   OrientationGated    -- connect two neighbouring picks only if their LOCAL dip
//     agrees within a tolerance (the "neighbour-angle" rule, via a small-window
//     PCA orientation). SEPARATES crossing fractures of different dip; needs no
//     cluster count k (unlike k-means, which also splits long curved reflectors).
//
// Rhino-free, dependency-free (arrays + graph BFS + PCA + Douglas-Peucker). The
// resulting polylines feed the GH drape (GprFractureOverlayComponent) and the 3D
// surface build. Heavy 3D geometry (loft to surfaces, cavity isosurface, drape)
// is routed through geogram (preferred) / RhinoCommon, NOT reimplemented here.
//
// Math: gpr_math_derivations.md sec 4c (ridge -> polyline -> Douglas-Peucker);
// the orientation gate is the robust form of the local-angle continuity idea.
// =============================================================================

public enum FractureTraceMode
{
    ConnectedComponents,
    OrientationGated,
}

public sealed class FractureLine
{
    public int Id;
    public List<double> X = new List<double>();      // metres along the section
    public List<double> Depth = new List<double>();  // metres (positive down)
    public int SpanTraces;
    public double DipDeg;        // overall dip of the trace (deg from horizontal)
    public double MeanEnergy;    // mean normalised energy of the member picks
    public int PointCount => X.Count;
}

public sealed class FractureTraceResult
{
    public FractureTraceResult(List<FractureLine> lines, int[] labelPerPick)
    {
        Lines = lines; LabelPerPick = labelPerPick;
    }
    /// <summary>Traced continuous fracture lines (only components meeting MinSpanTraces).</summary>
    public List<FractureLine> Lines { get; }
    /// <summary>Fracture id per input pick (matches Lines[k].Id; 0 = unassigned / too short).</summary>
    public int[] LabelPerPick { get; }
    public int LineCount => Lines.Count;
}

public sealed class FractureTracer
{
    public FractureTraceMode Mode { get; set; } = FractureTraceMode.ConnectedComponents;
    /// <summary>Keep only reflectors spanning at least this many traces (USGS >= 40).</summary>
    public int MinSpanTraces { get; set; } = 40;
    /// <summary>OrientationGated: max local-dip disagreement (deg) to connect two picks.</summary>
    public double OrientationToleranceDeg { get; set; } = 30.0;
    /// <summary>OrientationGated: neighbourhood radius (traces) for the PCA orientation.</summary>
    public int OrientationRadius { get; set; } = 6;
    /// <summary>Douglas-Peucker tolerance (m). &lt;= 0 = auto (~half wavelength from dt/v).</summary>
    public double SimplifyToleranceM { get; set; } = 0.0;

    public FractureTraceResult Trace(
        IReadOnlyList<FractureExtractor.FracturePick> picks, int ns, int ntr,
        double dt, double dx, double v)
    {
        if (picks == null) throw new ArgumentNullException(nameof(picks));
        int n = picks.Count;
        var label = new int[n];                          // 0 = unassigned
        if (n == 0) return new FractureTraceResult(new List<FractureLine>(), label);

        double depthPerSample = v * dt / 2.0;
        // index lookup: (sample,trace) -> pick index
        var key2pick = new Dictionary<long, int>(n);
        for (int k = 0; k < n; k++)
            key2pick[Key(picks[k].SampleIndex, picks[k].TraceIndex)] = k;

        double[] ori = Mode == FractureTraceMode.OrientationGated
            ? LocalOrientations(picks, dt, dx, v) : null;

        // BFS grouping over the 8-neighbourhood of the pick set
        var comp = new int[n];                           // raw component id (1-based)
        int nComp = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < n; s++)
        {
            if (comp[s] != 0) continue;
            nComp++; comp[s] = nComp; stack.Push(s);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int si = picks[k].SampleIndex, ti = picks[k].TraceIndex;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        if (!key2pick.TryGetValue(Key(si + di, ti + dj), out int m)) continue;
                        if (comp[m] != 0) continue;
                        if (Mode == FractureTraceMode.OrientationGated &&
                            AngDiff(ori[k], ori[m]) >= OrientationToleranceDeg) continue;
                        comp[m] = nComp; stack.Push(m);
                    }
            }
        }

        // group pick indices by component
        var members = new Dictionary<int, List<int>>();
        for (int k = 0; k < n; k++)
        {
            if (!members.TryGetValue(comp[k], out var lst)) { lst = new List<int>(); members[comp[k]] = lst; }
            lst.Add(k);
        }

        double eps = SimplifyToleranceM > 0 ? SimplifyToleranceM : Math.Max(0.05, 4.0 * depthPerSample);
        var lines = new List<FractureLine>();
        int nextId = 0;
        foreach (var kv in members)
        {
            var idxs = kv.Value;
            // span = trace extent
            int tmin = int.MaxValue, tmax = int.MinValue;
            foreach (int k in idxs) { int t = picks[k].TraceIndex; if (t < tmin) tmin = t; if (t > tmax) tmax = t; }
            int span = tmax - tmin + 1;
            if (span < MinSpanTraces) continue;

            // median sample per trace column -> ordered polyline
            var colSamples = new Dictionary<int, List<int>>();
            double eSum = 0;
            foreach (int k in idxs)
            {
                int t = picks[k].TraceIndex;
                if (!colSamples.TryGetValue(t, out var l)) { l = new List<int>(); colSamples[t] = l; }
                l.Add(picks[k].SampleIndex);
                eSum += picks[k].Energy;
            }
            var ts = new List<int>(colSamples.Keys); ts.Sort();
            var poly = new List<double[]>(ts.Count);
            foreach (int t in ts)
            {
                var col = colSamples[t]; col.Sort();
                double medSample = col[col.Count / 2];
                poly.Add(new[] { t * dx, medSample * depthPerSample });
            }
            var simp = DouglasPeucker(poly, eps);

            nextId++;
            var fl = new FractureLine { Id = nextId, SpanTraces = span, MeanEnergy = eSum / idxs.Count };
            foreach (var p in simp) { fl.X.Add(p[0]); fl.Depth.Add(p[1]); }
            double x0 = simp[0][0], z0 = simp[0][1], x1 = simp[simp.Count - 1][0], z1 = simp[simp.Count - 1][1];
            fl.DipDeg = Math.Atan2(Math.Abs(z1 - z0), Math.Abs(x1 - x0) + 1e-9) * 180.0 / Math.PI;
            lines.Add(fl);
            foreach (int k in idxs) label[k] = nextId;
        }
        lines.Sort((a, b) => b.SpanTraces.CompareTo(a.SpanTraces));
        return new FractureTraceResult(lines, label);
    }

    // local reflector dip (deg, folded to [-90,90]) via PCA of nearby picks in metric (x,depth)
    private double[] LocalOrientations(IReadOnlyList<FractureExtractor.FracturePick> picks,
        double dt, double dx, double v)
    {
        int n = picks.Count;
        double dps = v * dt / 2.0;
        var x = new double[n]; var z = new double[n];
        for (int k = 0; k < n; k++) { x[k] = picks[k].TraceIndex * dx; z[k] = picks[k].SampleIndex * dps; }
        double cell = Math.Max(OrientationRadius * dx, 1e-6);
        var grid = new Dictionary<long, List<int>>();
        for (int k = 0; k < n; k++)
        {
            long gk = Key((int)Math.Floor(x[k] / cell), (int)Math.Floor(z[k] / cell));
            if (!grid.TryGetValue(gk, out var l)) { l = new List<int>(); grid[gk] = l; }
            l.Add(k);
        }
        var ang = new double[n];
        for (int k = 0; k < n; k++)
        {
            int cx = (int)Math.Floor(x[k] / cell), cz = (int)Math.Floor(z[k] / cell);
            double mx = 0, mz = 0; int cnt = 0;
            var nb = new List<int>();
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gz = cz - 1; gz <= cz + 1; gz++)
                    if (grid.TryGetValue(Key(gx, gz), out var l)) nb.AddRange(l);
            if (nb.Count < 3) { ang[k] = 0; continue; }
            foreach (int m in nb) { mx += x[m]; mz += z[m]; cnt++; }
            mx /= cnt; mz /= cnt;
            double sxx = 0, szz = 0, sxz = 0;
            foreach (int m in nb) { double ddx = x[m] - mx, ddz = z[m] - mz; sxx += ddx * ddx; szz += ddz * ddz; sxz += ddx * ddz; }
            // principal-axis angle of the 2x2 covariance [[sxx,sxz],[sxz,szz]]
            double theta = 0.5 * Math.Atan2(2 * sxz, sxx - szz);
            double deg = theta * 180.0 / Math.PI;
            ang[k] = ((deg + 90) % 180) - 90;
        }
        return ang;
    }

    /// <summary>Package a HAND-DRAWN section polyline (operator HITL pick) as a FractureLine,
    /// so manual fractures merge with the auto-traced set on equal footing. x / depth in
    /// metres (section frame); dx (trace spacing) is only used to express the span in traces.
    /// MeanEnergy is set to 1 (operator-asserted = full confidence).</summary>
    public static FractureLine FractureLineFromSection(
        IReadOnlyList<double> x, IReadOnlyList<double> depth, int id, double dx)
    {
        if (x == null || depth == null || x.Count != depth.Count || x.Count < 2)
            throw new ArgumentException("need >= 2 matching (x,depth) points");
        var fl = new FractureLine { Id = id, MeanEnergy = 1.0 };
        double xmin = double.MaxValue, xmax = double.MinValue;
        for (int k = 0; k < x.Count; k++)
        {
            fl.X.Add(x[k]); fl.Depth.Add(depth[k]);
            if (x[k] < xmin) xmin = x[k]; if (x[k] > xmax) xmax = x[k];
        }
        fl.SpanTraces = dx > 0 ? (int)Math.Round((xmax - xmin) / dx) : x.Count;
        double x0 = x[0], z0 = depth[0], x1 = x[x.Count - 1], z1 = depth[depth.Count - 1];
        fl.DipDeg = Math.Atan2(Math.Abs(z1 - z0), Math.Abs(x1 - x0) + 1e-9) * 180.0 / Math.PI;
        return fl;
    }

    private static double AngDiff(double a, double b)
    {
        double d = Math.Abs(a - b) % 180.0;
        return Math.Min(d, 180.0 - d);
    }

    private static long Key(int a, int b) => ((long)a << 32) ^ (uint)b;

    // Douglas-Peucker on a polyline of [x,y] points.
    private static List<double[]> DouglasPeucker(List<double[]> pts, double eps)
    {
        if (pts.Count < 3) return new List<double[]>(pts);
        double ax = pts[0][0], ay = pts[0][1];
        double bx = pts[pts.Count - 1][0], by = pts[pts.Count - 1][1];
        double abx = bx - ax, aby = by - ay;
        double L = Math.Sqrt(abx * abx + aby * aby) + 1e-12;
        double dmax = 0; int idx = 0;
        for (int k = 1; k < pts.Count - 1; k++)
        {
            double px = pts[k][0] - ax, py = pts[k][1] - ay;
            double d = Math.Abs(abx * py - aby * px) / L;
            if (d > dmax) { dmax = d; idx = k; }
        }
        if (dmax > eps)
        {
            var left = DouglasPeucker(pts.GetRange(0, idx + 1), eps);
            var right = DouglasPeucker(pts.GetRange(idx, pts.Count - idx), eps);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }
        return new List<double[]> { pts[0], pts[pts.Count - 1] };
    }
}
