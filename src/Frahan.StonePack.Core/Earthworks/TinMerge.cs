#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;   // GeometryNumerics (recenter, scale-relative eps)

namespace Frahan.Core.Earthworks
{
    // =========================================================================
    // TinMerge -- fuse a dense ground TIN with SPARSE scattered picks (bedrock
    // from GPR/ERT/seismic, or a second cloud) onto ONE common TIN, so
    // OverburdenVolume can difference them (its caller contract: both surfaces
    // sampled on the SAME triangulation). Card A3; geom_at_meshing_codedive.md sec 3.
    //
    // The Fade2D reference does an offset cookie-cutter merge (dense-where-it-exists,
    // coarse-fills-the-rest, constrained-Delaunay to protect bench/road edges) on a
    // CGAL Delaunay. That richer merge belongs on the native CGAL shim. This managed,
    // Rhino-free, dependency-light path covers the immediate need: RESAMPLE the sparse
    // source heights onto the dense target's (x,y) vertices by k-nearest inverse-
    // distance weighting (IDW, Shepard 1968), so the two surfaces share the target
    // triangulation. Recenter first (GeometryNumerics T1) so UTM/quarry-scale (x,y)
    // do not lose precision; the IDW radius is scale-relative (median target spacing).
    //
    // Use: ResampleOntoVertices(groundXyz, bedrockPicksXyz) -> bedrockZ aligned 1:1 to
    // the ground vertices; feed (groundXyz, bedrockZ, groundTriangles) to
    // OverburdenVolume.Compute. Vertices with no source within MaxSearchRadius are
    // flagged (NaN) so the caller can clip them out of the common area.
    // =========================================================================
    public sealed class TinMergeOptions
    {
        /// <summary>k nearest source points used per target vertex.</summary>
        public int Neighbors = 6;
        /// <summary>IDW power p in w = 1/d^p (2 = Shepard standard).</summary>
        public double Power = 2.0;
        /// <summary>Max search radius as a MULTIPLE of the median source spacing
        /// (scale-relative). Targets with no source inside are returned as NaN.</summary>
        public double MaxRadiusMedianMult = 8.0;
    }

    public sealed class TinMergeResult
    {
        public TinMergeResult(double[] z, bool[] valid, int unresolved, double medianSourceSpacing)
        {
            Z = z; Valid = valid; Unresolved = unresolved; MedianSourceSpacing = medianSourceSpacing;
        }
        /// <summary>Interpolated source height at each target vertex (NaN if unresolved).</summary>
        public double[] Z { get; }
        /// <summary>True where a height was interpolated within the radius.</summary>
        public bool[] Valid { get; }
        /// <summary>Count of target vertices with no source within the radius.</summary>
        public int Unresolved { get; }
        public double MedianSourceSpacing { get; }
    }

    public static class TinMerge
    {
        /// <summary>
        /// Resample sparse source (x,y,z) picks onto target (x,y) vertices by k-NN IDW.
        /// </summary>
        /// <param name="targetXyz">flat target vertex coords (z ignored for the (x,y) lookup).</param>
        /// <param name="sourceXyz">flat sparse source coords (the picks to interpolate).</param>
        public static TinMergeResult ResampleOntoVertices(
            IReadOnlyList<double> targetXyz, IReadOnlyList<double> sourceXyz, TinMergeOptions opt = null)
        {
            if (targetXyz == null) throw new ArgumentNullException(nameof(targetXyz));
            if (sourceXyz == null) throw new ArgumentNullException(nameof(sourceXyz));
            opt = opt ?? new TinMergeOptions();
            int nTarget = targetXyz.Count / 3;
            int nSource = sourceXyz.Count / 3;
            if (nSource == 0) throw new ArgumentException("no source picks to interpolate");

            double medSpacing = MedianNearestSpacing(sourceXyz, nSource);
            double maxR = opt.MaxRadiusMedianMult * medSpacing;
            double maxR2 = maxR * maxR;

            // uniform grid spatial index over source (x,y) at cell = maxR
            double cell = Math.Max(maxR, 1e-9);
            var grid = BuildGrid(sourceXyz, nSource, cell, out double minx, out double miny, out int nx, out int ny);

            var z = new double[nTarget];
            var valid = new bool[nTarget];
            int unresolved = 0;
            int k = Math.Max(1, opt.Neighbors);
            // small fixed-size nearest buffers
            var bestD = new double[k];
            var bestZ = new double[k];

            for (int v = 0; v < nTarget; v++)
            {
                double tx = targetXyz[3 * v], ty = targetXyz[3 * v + 1];
                int cx = (int)((tx - minx) / cell), cy = (int)((ty - miny) / cell);
                int found = 0;
                for (int i = 0; i < k; i++) { bestD[i] = double.MaxValue; bestZ[i] = 0; }

                for (int gy = Math.Max(0, cy - 1); gy <= Math.Min(ny - 1, cy + 1); gy++)
                    for (int gx = Math.Max(0, cx - 1); gx <= Math.Min(nx - 1, cx + 1); gx++)
                    {
                        if (!grid.TryGetValue(gy * nx + gx, out var bucket)) continue;
                        foreach (int s in bucket)
                        {
                            double dx = sourceXyz[3 * s] - tx, dy = sourceXyz[3 * s + 1] - ty;
                            double d2 = dx * dx + dy * dy;
                            if (d2 > maxR2) continue;
                            // insert into the k-smallest buffer
                            int worst = 0; for (int i = 1; i < k; i++) if (bestD[i] > bestD[worst]) worst = i;
                            if (d2 < bestD[worst]) { bestD[worst] = d2; bestZ[worst] = sourceXyz[3 * s + 2]; found++; }
                        }
                    }

                if (found == 0) { z[v] = double.NaN; valid[v] = false; unresolved++; continue; }

                double num = 0, den = 0;
                for (int i = 0; i < k; i++)
                {
                    if (bestD[i] == double.MaxValue) continue;
                    if (bestD[i] < 1e-18) { num = bestZ[i]; den = 1.0; break; }  // exact hit
                    double w = 1.0 / Math.Pow(bestD[i], opt.Power * 0.5);          // d^p with d2 -> p/2
                    num += w * bestZ[i]; den += w;
                }
                z[v] = num / den; valid[v] = true;
            }
            return new TinMergeResult(z, valid, unresolved, medSpacing);
        }

        // median nearest-neighbour spacing of the source (for the scale-relative radius)
        private static double MedianNearestSpacing(IReadOnlyList<double> xyz, int n)
        {
            if (n < 2) return 1.0;
            // sample up to 400 points for the estimate (O(sample*n) bounded)
            int sample = Math.Min(n, 400);
            int step = Math.Max(1, n / sample);
            var nn = new List<double>(sample);
            for (int i = 0; i < n; i += step)
            {
                double xi = xyz[3 * i], yi = xyz[3 * i + 1];
                double best = double.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    double dx = xyz[3 * j] - xi, dy = xyz[3 * j + 1] - yi;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < best) best = d2;
                }
                if (best < double.MaxValue) nn.Add(Math.Sqrt(best));
            }
            if (nn.Count == 0) return 1.0;
            nn.Sort();
            double m = nn[nn.Count / 2];
            return m > 0 ? m : 1.0;
        }

        private static Dictionary<int, List<int>> BuildGrid(IReadOnlyList<double> xyz, int n, double cell,
            out double minx, out double miny, out int nx, out int ny)
        {
            minx = double.MaxValue; miny = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double x = xyz[3 * i], y = xyz[3 * i + 1];
                if (x < minx) minx = x; if (y < miny) miny = y;
                if (x > maxx) maxx = x; if (y > maxy) maxy = y;
            }
            nx = Math.Max(1, (int)((maxx - minx) / cell) + 1);
            ny = Math.Max(1, (int)((maxy - miny) / cell) + 1);
            var grid = new Dictionary<int, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                int cx = (int)((xyz[3 * i] - minx) / cell), cy = (int)((xyz[3 * i + 1] - miny) / cell);
                int key = cy * nx + cx;
                if (!grid.TryGetValue(key, out var b)) { b = new List<int>(4); grid[key] = b; }
                b.Add(i);
            }
            return grid;
        }
    }
}
