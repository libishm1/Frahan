#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Furthest-point sampling (FPS) over a 3D point cloud.
///
/// Greedy O(N * K) algorithm: pick a seed (arbitrary or specified),
/// then repeatedly pick the point that is FURTHEST from the
/// already-selected set. Used in PuzzleFusion++'s PointNet++ encoder
/// to reduce each fragment's 1000-point cloud to 25 keypoints.
///
/// Numerically equivalent to PyG's fps() / PointNet++ original
/// reference implementation. Distance metric: Euclidean (L2).
///
/// Reference:
///   Qi, Yi, Su, Guibas. PointNet++: Deep Hierarchical Feature
///   Learning on Point Sets in a Metric Space. NeurIPS 2017.
/// </summary>
public static class Fps
{
    /// <summary>Pick K keypoints by furthest-point sampling.</summary>
    /// <param name="points">Input cloud, length 3*N (xyz, xyz, ...).</param>
    /// <param name="k">Number of keypoints to pick. Must be >= 1 and &lt;= N.</param>
    /// <param name="seedIndex">Optional starting point index. -1 = pick 0 for determinism.</param>
    /// <returns>Indices into the input cloud of the picked keypoints, length k.</returns>
    public static int[] Sample(double[] points, int k, int seedIndex = -1)
    {
        if (points == null) throw new ArgumentNullException(nameof(points));
        if (points.Length % 3 != 0) throw new ArgumentException("points length must be divisible by 3 (xyz triples).");
        int n = points.Length / 3;
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k), "k >= 1");
        if (k > n) throw new ArgumentOutOfRangeException(nameof(k), $"k ({k}) > N ({n}).");
        if (seedIndex < 0) seedIndex = 0;
        if (seedIndex >= n) throw new ArgumentOutOfRangeException(nameof(seedIndex));

        var selected = new int[k];
        // dist[i] = min L2 distance from point i to the selected set.
        var dist = new double[n];
        for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;

        selected[0] = seedIndex;
        for (int step = 1; step < k; step++)
        {
            int prev = selected[step - 1];
            double px = points[prev * 3 + 0];
            double py = points[prev * 3 + 1];
            double pz = points[prev * 3 + 2];

            // Update min-distance to the selected set with the newly-
            // added point. O(N) per step => O(NK) total.
            double maxDist = -1;
            int maxIdx = -1;
            for (int i = 0; i < n; i++)
            {
                double dx = points[i * 3 + 0] - px;
                double dy = points[i * 3 + 1] - py;
                double dz = points[i * 3 + 2] - pz;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < dist[i]) dist[i] = d;
                if (dist[i] > maxDist)
                {
                    maxDist = dist[i];
                    maxIdx = i;
                }
            }
            selected[step] = maxIdx;
        }
        return selected;
    }
}
