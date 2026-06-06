#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.GH.TwoD;

/// <summary>
/// Centroidal Voronoi Diagram (CVD) seed generator via Lloyd's algorithm.
/// Pure-managed; no Rhino dependency. Used by the Trencadís solver
/// (F-2D-002.2 wiki recommendation) to produce a blue-noise-distributed
/// initial seed pattern over the sheet domain instead of placing the
/// first piece at a bbox corner.
///
/// Algorithm (uniform-density CVD; matches wiki primitives/cvd_lloyd.md):
///   1. Initialize K seeds at random positions strictly inside the
///      domain (outer polygon minus holes).
///   2. Discretise the domain on an N×N grid; assign each grid cell
///      to its nearest seed → discrete Voronoi diagram.
///   3. Compute the centroid of each Voronoi region.
///   4. Move each seed to its centroid.
///   5. Repeat until centroids stabilise (typ. 10–30 iterations).
/// </summary>
public static class CvdLloyd2d
{
    /// <summary>
    /// Generate K seed points inside the domain bounded by `outer`
    /// minus `holes`. Returns &lt;= K points (some may be rejected if
    /// the domain is small / heavily perforated).
    /// </summary>
    public static List<(double x, double y)> GenerateSeeds(
        double[] outerVx, double[] outerVy,
        IList<(double[] vx, double[] vy)> holes,
        double bboxMinX, double bboxMinY, double bboxMaxX, double bboxMaxY,
        int K, int iterations = 20, int gridRes = 64, int seed = 0)
    {
        if (K <= 0 || outerVx == null || outerVy == null || outerVx.Length < 3) return new List<(double, double)>();
        var nOuter = outerVx.Length;
        holes ??= Array.Empty<(double[], double[])>();

        var rnd = new Random(seed == 0 ? 1 : seed);

        // Initialize: random points inside domain.
        var seeds = new List<(double x, double y)>(K);
        var attempts = 0;
        while (seeds.Count < K && attempts < K * 200)
        {
            attempts++;
            var x = bboxMinX + rnd.NextDouble() * (bboxMaxX - bboxMinX);
            var y = bboxMinY + rnd.NextDouble() * (bboxMaxY - bboxMinY);
            if (!PointInPoly(x, y, outerVx, outerVy, nOuter)) continue;
            if (PointInAnyHole(x, y, holes)) continue;
            seeds.Add((x, y));
        }
        if (seeds.Count == 0) return seeds;

        // Lloyd iterations: build Voronoi via grid assignment, recentre.
        var dx = (bboxMaxX - bboxMinX) / gridRes;
        var dy = (bboxMaxY - bboxMinY) / gridRes;
        if (dx <= 0 || dy <= 0) return seeds;

        for (int iter = 0; iter < iterations; iter++)
        {
            var n = seeds.Count;
            var sumX = new double[n];
            var sumY = new double[n];
            var count = new int[n];

            for (int i = 0; i < gridRes; i++)
            {
                var gx = bboxMinX + (i + 0.5) * dx;
                for (int j = 0; j < gridRes; j++)
                {
                    var gy = bboxMinY + (j + 0.5) * dy;
                    if (!PointInPoly(gx, gy, outerVx, outerVy, nOuter)) continue;
                    if (PointInAnyHole(gx, gy, holes)) continue;

                    int nearest = 0;
                    double bestSq = double.MaxValue;
                    for (int s = 0; s < n; s++)
                    {
                        var ddx = gx - seeds[s].x;
                        var ddy = gy - seeds[s].y;
                        var dsq = ddx * ddx + ddy * ddy;
                        if (dsq < bestSq) { bestSq = dsq; nearest = s; }
                    }
                    sumX[nearest] += gx;
                    sumY[nearest] += gy;
                    count[nearest]++;
                }
            }

            double maxMove = 0;
            for (int s = 0; s < n; s++)
            {
                if (count[s] == 0) continue;
                var nx = sumX[s] / count[s];
                var ny = sumY[s] / count[s];
                var mx = nx - seeds[s].x;
                var my = ny - seeds[s].y;
                var move = Math.Sqrt(mx * mx + my * my);
                if (move > maxMove) maxMove = move;
                seeds[s] = (nx, ny);
            }
            if (maxMove < Math.Min(dx, dy) * 0.5) break;
        }

        return seeds;
    }

    private static bool PointInAnyHole(double x, double y, IList<(double[] vx, double[] vy)> holes)
    {
        if (holes == null) return false;
        for (int h = 0; h < holes.Count; h++)
        {
            var hv = holes[h];
            if (PointInPoly(x, y, hv.vx, hv.vy, hv.vx.Length)) return true;
        }
        return false;
    }

    private static bool PointInPoly(double px, double py, double[] vx, double[] vy, int n)
    {
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if ((vy[i] > py) != (vy[j] > py) &&
                px < (vx[j] - vx[i]) * (py - vy[i]) / (vy[j] - vy[i]) + vx[i])
                inside = !inside;
        return inside;
    }
}
