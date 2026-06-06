#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;   // GeometryNumerics (recenter, T1)

namespace Frahan.Core.Earthworks
{
    // =========================================================================
    // OverburdenVolume -- volume between two surfaces sampled on a COMMON TIN.
    //
    // The cut-and-fill -> rock-face core (see outputs/2026-06-04/cutfill_excavation/
    // math_functions.md sec 3-4 + SLM_cutfill_algorithms.md card A5). Given a ground
    // surface (z_top) and a bedrock/reference surface (z_bottom) sampled at the SAME
    // (x,y) vertices with the SAME triangulation, it integrates the per-facet prism
    //   V_facet = A_xy * (d_a + d_b + d_c) / 3,   d = z_top - z_bottom
    // which is EXACT for piecewise-linear (TIN) surfaces. Where d changes sign within a
    // facet (ground above the reference in part, below in part) the facet is clipped at
    // the d = 0 line so CUT (d>0) and FILL (d<0) are integrated exactly and separately.
    //
    // For the rock-face strip: z_top = ground (LiDAR TIN), z_bottom = bedrock surface
    // (GPR/ERT/seismic picks). d >= 0 everywhere, so CutVolume = the soil/overburden to
    // strip (bank volume). The general grade case (surfaces crossing) uses the same call.
    //
    // CALLER CONTRACT: both surfaces must already be resampled onto ONE common
    // triangulation (the A3 TinMerge / compatible-triangulation step; out of scope here).
    //
    // Rhino-free; reuses GeometryNumerics to recenter the (x,y) before forming coordinate
    // differences (T1: avoids catastrophic cancellation at quarry/UTM scale). z enters
    // only as the difference d = z_top - z_bottom, so any z origin cancels.
    // Method-class: TIN prism integration; difference triangulation (geom.at / Fade2D).
    // =========================================================================
    public sealed class OverburdenResult
    {
        public OverburdenResult(double cut, double fill, double planArea)
        {
            CutVolume = cut; FillVolume = fill; PlanArea = planArea;
        }
        /// <summary>Volume where z_top is ABOVE z_bottom (soil to strip for the rock-face case).</summary>
        public double CutVolume { get; }
        /// <summary>Volume where z_top is BELOW z_bottom.</summary>
        public double FillVolume { get; }
        /// <summary>Cut - Fill (signed net between the surfaces).</summary>
        public double NetVolume => CutVolume - FillVolume;
        /// <summary>Total projected (x,y) area of the triangulation.</summary>
        public double PlanArea { get; }
        /// <summary>Alias: for the rock-face strip, the overburden is the cut volume.</summary>
        public double Overburden => CutVolume;
        public override string ToString() =>
            $"Overburden(cut={CutVolume:0.###}, fill={FillVolume:0.###}, net={NetVolume:0.###}, area={PlanArea:0.###})";
    }

    public static class OverburdenVolume
    {
        /// <summary>
        /// Volume between two surfaces over a common TIN.
        /// </summary>
        /// <param name="groundXyz">Flat ground vertices x,y,z_top (length 3n).</param>
        /// <param name="bedrockZ">z_bottom at the SAME n vertices (length n).</param>
        /// <param name="triangles">Flat vertex-index triples (length 3m).</param>
        public static OverburdenResult Compute(
            IReadOnlyList<double> groundXyz,
            IReadOnlyList<double> bedrockZ,
            IReadOnlyList<int> triangles)
        {
            if (groundXyz == null) throw new ArgumentNullException(nameof(groundXyz));
            if (bedrockZ == null) throw new ArgumentNullException(nameof(bedrockZ));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (groundXyz.Count % 3 != 0 || groundXyz.Count < 9)
                throw new ArgumentException($"groundXyz length must be a multiple of 3 and >= 9, got {groundXyz.Count}", nameof(groundXyz));
            int n = groundXyz.Count / 3;
            if (bedrockZ.Count != n)
                throw new ArgumentException($"bedrockZ length ({bedrockZ.Count}) must equal vertex count ({n})", nameof(bedrockZ));
            if (triangles.Count % 3 != 0)
                throw new ArgumentException($"triangles length must be a multiple of 3, got {triangles.Count}", nameof(triangles));

            // T1: recenter (x,y) to the centroid so coordinate differences keep precision at
            // UTM scale. z only enters as d = z_top - z_bottom, so its origin is irrelevant.
            var c = GeometryNumerics.Centroid(groundXyz);
            var lx = new double[n];
            var ly = new double[n];
            var d = new double[n];
            for (int i = 0; i < n; i++)
            {
                lx[i] = groundXyz[3 * i + 0] - c.X;
                ly[i] = groundXyz[3 * i + 1] - c.Y;
                d[i] = groundXyz[3 * i + 2] - bedrockZ[i];   // signed height difference
            }

            double cut = 0.0, fill = 0.0, area = 0.0;
            int m = triangles.Count / 3;
            for (int t = 0; t < m; t++)
            {
                int a = triangles[3 * t + 0], b = triangles[3 * t + 1], c2 = triangles[3 * t + 2];
                if (a < 0 || b < 0 || c2 < 0 || a >= n || b >= n || c2 >= n) continue;
                area += TriAreaXy(lx[a], ly[a], lx[b], ly[b], lx[c2], ly[c2]);
                AccumulateFacet(
                    lx[a], ly[a], d[a],
                    lx[b], ly[b], d[b],
                    lx[c2], ly[c2], d[c2],
                    ref cut, ref fill);
            }
            return new OverburdenResult(cut, fill, area);
        }

        // Clip the facet at the d = 0 line into a non-negative polygon (-> cut) and a
        // non-positive polygon (-> fill), then integrate each by fan + centroid rule.
        private static void AccumulateFacet(
            double xa, double ya, double da,
            double xb, double yb, double db,
            double xc, double yc, double dc,
            ref double cut, ref double fill)
        {
            // points as (x, y, d)
            double[] px = { xa, xb, xc };
            double[] py = { ya, yb, yc };
            double[] pd = { da, db, dc };

            var posX = new List<double>(5); var posY = new List<double>(5); var posD = new List<double>(5);
            var negX = new List<double>(5); var negY = new List<double>(5); var negD = new List<double>(5);

            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                double di = pd[i], dj = pd[j];
                if (di >= 0.0) { posX.Add(px[i]); posY.Add(py[i]); posD.Add(di); }
                if (di <= 0.0) { negX.Add(px[i]); negY.Add(py[i]); negD.Add(di); }
                // strict sign change on edge i->j -> insert the d=0 crossing into both
                if ((di > 0.0 && dj < 0.0) || (di < 0.0 && dj > 0.0))
                {
                    double s = di / (di - dj);                 // 0..1
                    double cx = px[i] + s * (px[j] - px[i]);
                    double cy = py[i] + s * (py[j] - py[i]);
                    posX.Add(cx); posY.Add(cy); posD.Add(0.0);
                    negX.Add(cx); negY.Add(cy); negD.Add(0.0);
                }
            }
            cut += FanVolume(posX, posY, posD);          // all d >= 0 -> positive
            fill += -FanVolume(negX, negY, negD);        // all d <= 0 -> negative -> magnitude
        }

        // Integral of the linear field d over a convex polygon = sum over a triangle fan
        // of (planar area) * (mean vertex d). Unsigned area; the sign rides in d.
        private static double FanVolume(List<double> x, List<double> y, List<double> dd)
        {
            int k = x.Count;
            if (k < 3) return 0.0;
            double v = 0.0;
            for (int i = 1; i + 1 < k; i++)
            {
                double ar = TriAreaXy(x[0], y[0], x[i], y[i], x[i + 1], y[i + 1]);
                v += ar * (dd[0] + dd[i] + dd[i + 1]) / 3.0;
            }
            return v;
        }

        private static double TriAreaXy(double xa, double ya, double xb, double yb, double xc, double yc)
            => 0.5 * Math.Abs((xb - xa) * (yc - ya) - (xc - xa) * (yb - ya));
    }
}
