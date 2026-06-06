#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// K-nearest-neighbours over a 3D point cloud. Brute-force O(N^2)
/// implementation -- sufficient for Phase 1 sanity checks at the
/// PointNet++ scales used by PuzzleFusion++ (typically 1000 query
/// points × 1000 cloud points × k≈32). KD-tree acceleration is
/// future work if the inference path bottlenecks here.
/// </summary>
public static class Knn
{
    /// <summary>
    /// For each of <paramref name="queries"/>, find the indices of
    /// the <paramref name="k"/> nearest neighbours in
    /// <paramref name="cloud"/> by Euclidean distance.
    /// </summary>
    /// <param name="cloud">Point cloud, length 3*N (xyz).</param>
    /// <param name="queries">Query points, length 3*Q (xyz).</param>
    /// <param name="k">Number of neighbours per query.</param>
    /// <returns>Flat int[Q*k] of neighbour indices into cloud, row-major.</returns>
    public static int[] NearestK(double[] cloud, double[] queries, int k)
    {
        if (cloud == null) throw new ArgumentNullException(nameof(cloud));
        if (queries == null) throw new ArgumentNullException(nameof(queries));
        if (cloud.Length % 3 != 0) throw new ArgumentException("cloud length % 3 != 0.");
        if (queries.Length % 3 != 0) throw new ArgumentException("queries length % 3 != 0.");
        int n = cloud.Length / 3;
        int q = queries.Length / 3;
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k));
        if (k > n) throw new ArgumentOutOfRangeException(nameof(k), $"k ({k}) > cloud N ({n}).");

        var out_ = new int[q * k];

        // Per query: maintain a top-K heap of (distance, index).
        // For Phase 1 we use a simple bounded-insert array (O(k) per
        // candidate); total cost O(Q * N * k). Replace with a binary
        // heap or KD-tree in Phase 7+ if profiling demands.
        var topDist = new double[k];
        var topIdx = new int[k];
        for (int qi = 0; qi < q; qi++)
        {
            double qx = queries[qi * 3 + 0];
            double qy = queries[qi * 3 + 1];
            double qz = queries[qi * 3 + 2];

            for (int i = 0; i < k; i++) { topDist[i] = double.PositiveInfinity; topIdx[i] = -1; }

            for (int i = 0; i < n; i++)
            {
                double dx = cloud[i * 3 + 0] - qx;
                double dy = cloud[i * 3 + 1] - qy;
                double dz = cloud[i * 3 + 2] - qz;
                double d = dx * dx + dy * dy + dz * dz;
                // Find worst entry in the top-K. If d beats it, insert.
                int worst = 0;
                for (int t = 1; t < k; t++)
                    if (topDist[t] > topDist[worst]) worst = t;
                if (d < topDist[worst])
                {
                    topDist[worst] = d;
                    topIdx[worst] = i;
                }
            }

            // Sort top-K by ascending distance for determinism. Tiny
            // selection sort -- k is small (typically <= 32).
            for (int a = 0; a < k - 1; a++)
            {
                int min = a;
                for (int b = a + 1; b < k; b++)
                    if (topDist[b] < topDist[min]) min = b;
                if (min != a)
                {
                    (topDist[a], topDist[min]) = (topDist[min], topDist[a]);
                    (topIdx[a], topIdx[min]) = (topIdx[min], topIdx[a]);
                }
            }
            for (int t = 0; t < k; t++) out_[qi * k + t] = topIdx[t];
        }
        return out_;
    }
}
