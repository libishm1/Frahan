#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// PolygonalWallGenerator — Rhino-free polygonal-masonry wall pattern generator
// (evolution P3 of EVOLUTION_PLAN_MASONRY.md, 2026-06-10).
//
// Math:
//   * Cells are a POWER DIAGRAM (additively weighted Voronoi) of jittered-grid
//     seeds, computed exactly by half-plane clipping: cell(i) = rect ∩
//     { x : 2(s_j - s_i)·x <= |s_j|^2 - |s_i|^2 + w_i - w_j  for all j != i }.
//     Per-seed weights w_i give genuine SIZE GRADING (bigger weight => bigger
//     stone). O(n^2 v) — exact and robust for the n <= a few hundred stones a
//     wall needs, and naturally bounded (no reflection trick required).
//   * LLOYD RELAXATION (seeds <- cell centroids, k iterations) evens cell size
//     and roundness before the coursing morph.
//   * COURSING MORPH (validated 2026-06-10): v' = (1-c)·v + c·nearestCourse(v),
//     c = 0 irregular (Inca) .. c = 1 coursed rubble.
//   * SLIVER CULL: cells whose inradius proxy rho = 2A/P falls below
//     sliverMinInradiusFrac · sqrt(W·H/n) lose their seed and the diagram is
//     recomputed (their area is absorbed by neighbours) — bounded passes.
//   * INTERLOCK SCORE J in [0,1] (the Inca reading from Clifford & McGee 2018's
//     shape-grammar analysis): J = 1 - (aligned head-joint length / total
//     head-joint length) - 0.5 · (4-valent "+" vertices / cell count), clamped.
//     Head joints = interior more-vertical-than-horizontal cell edges; two head
//     joints in consecutive courses are "aligned" (a running joint) when their
//     u-midpoints coincide within alignTol. Higher J = better staggering.
//
// The generator works in the (u, v) parameter rectangle [0,W]x[0,H]; mapping
// onto a 3D surface, extrusion, and meshing live in the GH layer.
// =============================================================================

/// <summary>Options for <see cref="PolygonalWallGenerator.Generate"/>.</summary>
public sealed class WallGenOptions
{
    /// <summary>Panel width in model units (u-extent of the parameter rectangle).</summary>
    public double Width = 3.0;
    /// <summary>Panel height in model units (v-extent of the parameter rectangle).</summary>
    public double Height = 1.8;
    /// <summary>0 = irregular (Inca) .. 1 = coursed rubble.</summary>
    public double Coursing = 0.4;
    /// <summary>Number of courses the coursing morph pulls toward (&gt;= 1).</summary>
    public int Courses = 5;
    /// <summary>Seed columns (stones along the width, &gt;= 2).</summary>
    public int GridX = 8;
    /// <summary>Seed rows (stones along the height, &gt;= 1).</summary>
    public int GridY = 5;
    /// <summary>Random seed (deterministic output for a fixed option set).</summary>
    public int Seed = 7;
    /// <summary>Lloyd relaxation iterations before the coursing morph (0 disables).</summary>
    public int LloydIterations = 2;
    /// <summary>Coefficient of size grading: per-seed power weights are drawn
    /// uniformly from ±(SizeGradeCv · meanArea). 0 = equal-size tendency.</summary>
    public double SizeGradeCv = 0.30;
    /// <summary>Cells with inradius proxy 2A/P below this fraction of
    /// sqrt(meanArea) are culled as slivers (0 disables).</summary>
    public double SliverMinInradiusFrac = 0.18;
    /// <summary>Max sliver-cull passes.</summary>
    public int MaxSliverPasses = 3;
}

/// <summary>One stone cell of the generated wall pattern, in (u,v) space.</summary>
public sealed class WallCell
{
    public WallCell(IReadOnlyList<double> us, IReadOnlyList<double> vs,
                    double area, double centroidU, double centroidV, double inradiusProxy)
    {
        Us = us; Vs = vs; Area = area;
        CentroidU = centroidU; CentroidV = centroidV; InradiusProxy = inradiusProxy;
    }
    /// <summary>Polygon u coordinates (CCW, not closed).</summary>
    public IReadOnlyList<double> Us { get; }
    /// <summary>Polygon v coordinates (CCW, not closed).</summary>
    public IReadOnlyList<double> Vs { get; }
    public double Area { get; }
    public double CentroidU { get; }
    public double CentroidV { get; }
    /// <summary>Inradius proxy rho = 2A/P (exact for regular polygons, lower bound family).</summary>
    public double InradiusProxy { get; }
    public int VertexCount => Us.Count;
}

/// <summary>Result of <see cref="PolygonalWallGenerator.Generate"/> with quality metrics.</summary>
public sealed class WallGenResult
{
    public WallGenResult(IReadOnlyList<WallCell> cells, double interlockScore,
                         double areaCoverage, int culledSlivers, double meanArea,
                         double areaCv, int crossVertexCount, double alignedHeadJointLength,
                         double totalHeadJointLength)
    {
        Cells = cells; InterlockScore = interlockScore; AreaCoverage = areaCoverage;
        CulledSlivers = culledSlivers; MeanArea = meanArea; AreaCv = areaCv;
        CrossVertexCount = crossVertexCount;
        AlignedHeadJointLength = alignedHeadJointLength;
        TotalHeadJointLength = totalHeadJointLength;
    }
    public IReadOnlyList<WallCell> Cells { get; }
    /// <summary>Interlock score J in [0,1]; higher = better staggering (fewer running joints, fewer "+" vertices).</summary>
    public double InterlockScore { get; }
    /// <summary>Sum of cell areas / (W·H). Should be ~1 (the diagram tiles the rectangle).</summary>
    public double AreaCoverage { get; }
    public int CulledSlivers { get; }
    public double MeanArea { get; }
    /// <summary>Coefficient of variation of cell areas (size-grading indicator).</summary>
    public double AreaCv { get; }
    /// <summary>Number of diagram vertices where 4+ cells meet ("+" junctions, weak interlock).</summary>
    public int CrossVertexCount { get; }
    public double AlignedHeadJointLength { get; }
    public double TotalHeadJointLength { get; }
}

/// <summary>
/// Generates an architectural polygonal-masonry wall pattern (power-diagram
/// cells, Lloyd-relaxed, coursing-morphed, sliver-culled) with an interlock
/// quality score. Pure managed math; no Rhino dependency.
/// </summary>
public static class PolygonalWallGenerator
{
    /// <summary>Generate the wall pattern for <paramref name="options"/>.</summary>
    public static WallGenResult Generate(WallGenOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        double w = options.Width, h = options.Height;
        if (!(w > 0) || !(h > 0))
            throw new ArgumentOutOfRangeException(nameof(options), "Width and Height must be > 0");
        int gx = Math.Max(2, options.GridX), gy = Math.Max(1, options.GridY);
        int courses = Math.Max(1, options.Courses);
        double coursing = Clamp(options.Coursing, 0.0, 1.0);
        var rnd = new Random(options.Seed);

        // ---- 1. Jittered-grid seeds + size-grade power weights. ----
        int n = gx * gy;
        var su = new List<double>(n); var sv = new List<double>(n); var sw = new List<double>(n);
        double meanArea = w * h / n;
        for (int j = 0; j < gy; j++)
        {
            for (int i = 0; i < gx; i++)
            {
                double u = (i + 0.5) / gx * w + (rnd.NextDouble() * 2 - 1) * (0.35 * w / gx);
                double v = (j + 0.5) / gy * h + (rnd.NextDouble() * 2 - 1) * (0.30 * h / gy);
                su.Add(Clamp(u, 0.02 * w, 0.98 * w));
                sv.Add(Clamp(v, 0.02 * h, 0.98 * h));
                sw.Add(options.SizeGradeCv * meanArea * (rnd.NextDouble() * 2 - 1));
            }
        }

        // ---- 2. Lloyd relaxation (uniformity / roundness) before coursing. ----
        for (int it = 0; it < Math.Max(0, options.LloydIterations); it++)
        {
            var cellsIt = PowerCells(su, sv, sw, w, h);
            for (int i = 0; i < su.Count; i++)
            {
                if (cellsIt[i] == null || cellsIt[i].Count < 3) continue;
                Centroid(cellsIt[i], out double cu, out double cv, out _);
                su[i] = Clamp(cu, 0.02 * w, 0.98 * w);
                sv[i] = Clamp(cv, 0.02 * h, 0.98 * h);
            }
        }

        // ---- 3. Coursing morph: pull v toward the nearest course centre. ----
        for (int i = 0; i < sv.Count; i++)
        {
            double v = sv[i], best = double.MaxValue, cv = v;
            for (int k = 0; k < courses; k++)
            {
                double cc = (k + 0.5) / courses * h;
                if (Math.Abs(cc - v) < best) { best = Math.Abs(cc - v); cv = cc; }
            }
            sv[i] = v * (1 - coursing) + cv * coursing;
        }

        // ---- 4. Final diagram + bounded sliver-cull passes. ----
        int culled = 0;
        List<List<double[]>> cells = PowerCells(su, sv, sw, w, h);
        double rhoMin = options.SliverMinInradiusFrac * Math.Sqrt(meanArea);
        for (int pass = 0; pass < Math.Max(0, options.MaxSliverPasses) && options.SliverMinInradiusFrac > 0; pass++)
        {
            var keep = new List<int>();
            for (int i = 0; i < su.Count; i++)
            {
                var c = cells[i];
                if (c == null || c.Count < 3) { culled++; continue; } // dominated/empty
                Centroid(c, out _, out _, out double area);
                double rho = 2.0 * area / Perimeter(c);
                if (rho < rhoMin && su.Count - (su.Count - keep.Count - (su.Count - i - 1)) > 3)
                { culled++; continue; }
                keep.Add(i);
            }
            if (keep.Count == su.Count) break;
            if (keep.Count < 3) break;
            var nu = new List<double>(); var nv = new List<double>(); var nw = new List<double>();
            for (int k = 0; k < keep.Count; k++)
            { nu.Add(su[keep[k]]); nv.Add(sv[keep[k]]); nw.Add(sw[keep[k]]); }
            su = nu; sv = nv; sw = nw;
            cells = PowerCells(su, sv, sw, w, h);
        }

        // ---- 5. Package cells + metrics. ----
        var outCells = new List<WallCell>();
        double areaSum = 0;
        var areas = new List<double>();
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c == null || c.Count < 3) continue;
            Centroid(c, out double cu, out double cv, out double area);
            if (area <= 0) continue;
            double rho = 2.0 * area / Perimeter(c);
            var us = new double[c.Count]; var vs = new double[c.Count];
            for (int k = 0; k < c.Count; k++) { us[k] = c[k][0]; vs[k] = c[k][1]; }
            outCells.Add(new WallCell(us, vs, area, cu, cv, rho));
            areaSum += area; areas.Add(area);
        }
        double mean = areas.Count > 0 ? areaSum / areas.Count : 0;
        double var2 = 0;
        for (int i = 0; i < areas.Count; i++) { double d = areas[i] - mean; var2 += d * d; }
        double areaCv = (areas.Count > 1 && mean > 0) ? Math.Sqrt(var2 / areas.Count) / mean : 0;

        double interlock = InterlockScore(outCells, w, h, courses, gx,
                                          out int crossCount, out double alignedLen, out double headLen);

        return new WallGenResult(outCells, interlock, areaSum / (w * h), culled, mean, areaCv,
                                 crossCount, alignedLen, headLen);
    }

    // =========================================================================
    // Power diagram by half-plane clipping (exact, bounded).
    // =========================================================================
    private static List<List<double[]>> PowerCells(
        List<double> su, List<double> sv, List<double> sw, double w, double h)
    {
        int n = su.Count;
        var result = new List<List<double[]>>(n);
        for (int i = 0; i < n; i++)
        {
            // start from the full rectangle (CCW)
            var poly = new List<double[]>
            {
                new[] { 0.0, 0.0 }, new[] { w, 0.0 }, new[] { w, h }, new[] { 0.0, h }
            };
            double si2 = su[i] * su[i] + sv[i] * sv[i];
            for (int q = 0; q < n && poly.Count >= 3; q++)
            {
                if (q == i) continue;
                // keep x with 2(s_q - s_i)·x <= |s_q|^2 - |s_i|^2 + w_i - w_q
                double ax = 2 * (su[q] - su[i]);
                double ay = 2 * (sv[q] - sv[i]);
                double b = su[q] * su[q] + sv[q] * sv[q] - si2 + sw[i] - sw[q];
                poly = ClipHalfPlane(poly, ax, ay, b);
            }
            result.Add(poly.Count >= 3 ? poly : null);
        }
        return result;
    }

    /// <summary>Sutherland–Hodgman clip of a convex polygon against a·x + b·y &lt;= c.</summary>
    private static List<double[]> ClipHalfPlane(List<double[]> poly, double a, double b, double c)
    {
        var outPoly = new List<double[]>(poly.Count + 1);
        int m = poly.Count;
        for (int k = 0; k < m; k++)
        {
            double[] p = poly[k];
            double[] q = poly[(k + 1) % m];
            double fp = a * p[0] + b * p[1] - c;
            double fq = a * q[0] + b * q[1] - c;
            bool inP = fp <= 0, inQ = fq <= 0;
            if (inP) outPoly.Add(p);
            if (inP != inQ)
            {
                double t = fp / (fp - fq); // fp != fq when signs differ
                outPoly.Add(new[] { p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1]) });
            }
        }
        return outPoly;
    }

    private static void Centroid(List<double[]> poly, out double cu, out double cv, out double area)
    {
        double a2 = 0, cx = 0, cy = 0;
        int m = poly.Count;
        for (int k = 0; k < m; k++)
        {
            double[] p = poly[k]; double[] q = poly[(k + 1) % m];
            double cross = p[0] * q[1] - q[0] * p[1];
            a2 += cross; cx += (p[0] + q[0]) * cross; cy += (p[1] + q[1]) * cross;
        }
        area = Math.Abs(a2) / 2.0;
        if (Math.Abs(a2) < 1e-30) { cu = poly[0][0]; cv = poly[0][1]; area = 0; return; }
        cu = cx / (3 * a2); cv = cy / (3 * a2);
    }

    private static double Perimeter(List<double[]> poly)
    {
        double s = 0; int m = poly.Count;
        for (int k = 0; k < m; k++)
        {
            double[] p = poly[k]; double[] q = poly[(k + 1) % m];
            s += Math.Sqrt((q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]));
        }
        return s > 1e-30 ? s : 1e-30;
    }

    // =========================================================================
    // Interlock score J (running head joints + "+" junctions).
    // =========================================================================
    private static double InterlockScore(
        List<WallCell> cells, double w, double h, int courses, int gridX,
        out int crossVertexCount, out double alignedLen, out double headLen)
    {
        // Collect interior "head joint" edges (more vertical than horizontal,
        // not lying on the rectangle border) and weld diagram vertices to count
        // cell valence.
        var heads = new List<double[]>(); // {umid, vmin, vmax, len}
        var vertexCells = new Dictionary<long, HashSet<int>>();
        double weld = 1e-6 * Math.Max(w, h) * 1000; // quantisation cell ~1e-3 of domain
        double qs = Math.Max(w, h) * 1e-3;

        for (int ci = 0; ci < cells.Count; ci++)
        {
            var c = cells[ci];
            int m = c.VertexCount;
            for (int k = 0; k < m; k++)
            {
                double u1 = c.Us[k], v1 = c.Vs[k];
                double u2 = c.Us[(k + 1) % m], v2 = c.Vs[(k + 1) % m];
                // vertex valence accounting
                long key1 = (long)Math.Round(u1 / qs) * 1000003L + (long)Math.Round(v1 / qs);
                if (!vertexCells.TryGetValue(key1, out var set)) { set = new HashSet<int>(); vertexCells[key1] = set; }
                set.Add(ci);
                // border edges are not joints
                bool onBorder =
                    (Math.Abs(u1) < 1e-9 && Math.Abs(u2) < 1e-9) ||
                    (Math.Abs(u1 - w) < 1e-9 && Math.Abs(u2 - w) < 1e-9) ||
                    (Math.Abs(v1) < 1e-9 && Math.Abs(v2) < 1e-9) ||
                    (Math.Abs(v1 - h) < 1e-9 && Math.Abs(v2 - h) < 1e-9);
                if (onBorder) continue;
                double du = Math.Abs(u2 - u1), dv = Math.Abs(v2 - v1);
                if (dv <= du) continue; // bed joint, not head joint
                double len = Math.Sqrt(du * du + dv * dv);
                if (len < 1e-9) continue;
                heads.Add(new[] { (u1 + u2) / 2.0, Math.Min(v1, v2), Math.Max(v1, v2), len });
            }
        }
        _ = weld;

        // Each interior edge is seen twice (once per neighbouring cell) — halve the totals.
        headLen = 0;
        for (int i = 0; i < heads.Count; i++) headLen += heads[i][3];
        headLen /= 2.0;

        // Running joints: pairs of head edges in consecutive courses whose
        // u-midpoints coincide within alignTol.
        double courseH = h / Math.Max(1, courses);
        double alignTol = 0.10 * (w / Math.Max(2, gridX));
        double gapTol = 0.45 * courseH;
        double aligned = 0;
        for (int a = 0; a < heads.Count; a++)
        {
            for (int b = a + 1; b < heads.Count; b++)
            {
                double[] ea = heads[a], eb = heads[b];
                double[] lo = ea[2] <= eb[2] ? ea : eb;
                double[] hi = ea[2] <= eb[2] ? eb : ea;
                double gap = hi[1] - lo[2];               // vertical gap top-of-lower .. bottom-of-upper
                if (gap < -1e-9 || gap > gapTol) continue; // not consecutive courses
                if (Math.Abs(ea[0] - eb[0]) > alignTol) continue;
                aligned += Math.Min(ea[3], eb[3]);
            }
        }
        aligned /= 2.0; // double-seen edges => pairs counted ~4x relative to unique; halve like totals
        alignedLen = Math.Min(aligned, headLen);

        crossVertexCount = 0;
        foreach (var kv in vertexCells)
            if (kv.Value.Count >= 4) crossVertexCount++;

        double j = headLen > 1e-12 ? 1.0 - alignedLen / headLen : 1.0;
        j -= 0.5 * crossVertexCount / Math.Max(1, cells.Count);
        return Clamp(j, 0.0, 1.0);
    }

    private static double Clamp(double t, double lo, double hi) => Math.Max(lo, Math.Min(hi, t));
}
