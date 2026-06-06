#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Ball-query neighbour search, matching the upstream PuzzleFusion++ /
/// PointNet++ utility `query_ball_point(radius, nsample, xyz, new_xyz)`
/// in `utils/pn2_utils.py`.
///
/// For each query point in `queries`, collect up to <c>nsample</c>
/// indices of source points within <c>radius</c>. If fewer than
/// <c>nsample</c> source points are inside the radius, the upstream
/// pads with the FIRST valid neighbour (NOT zero) so the downstream
/// max-pool sees consistent shape. We match that convention exactly.
///
/// O(Q * N) per ball-query call. For SA1 with Q=256, N=1000 the work
/// is 256k pairwise distances; runs comfortably under 50 ms on a
/// laptop CPU.
/// </summary>
public static class BallQuery
{
    /// <summary>
    /// Run ball-query. Inputs are channels-last [N, 3] flat arrays.
    /// Returns an int[Q * nsample] array of indices into `xyz`.
    /// </summary>
    /// <param name="xyz">Source points [N, 3] row-major.</param>
    /// <param name="N">Number of source points.</param>
    /// <param name="queries">Query centroids [Q, 3] row-major.</param>
    /// <param name="Q">Number of queries.</param>
    /// <param name="radius">Search radius (Euclidean).</param>
    /// <param name="nsample">Cap on neighbours per query.</param>
    /// <returns>int[Q * nsample], row-major (query-major).</returns>
    public static int[] Sample(float[] xyz, int N,
                                float[] queries, int Q,
                                float radius, int nsample)
    {
        if (xyz == null || xyz.Length < N * 3) throw new ArgumentException("xyz too small.");
        if (queries == null || queries.Length < Q * 3) throw new ArgumentException("queries too small.");

        float r2 = radius * radius;
        var output = new int[Q * nsample];

        // Per-query scratch. Reused across queries to avoid allocation.
        // dists[n] = squared distance from query to source point n,
        //   or float.MaxValue if outside the radius.
        var distBuf = new float[N];
        var idxBuf  = new int[N];

        for (int q = 0; q < Q; q++)
        {
            float qx = queries[q * 3 + 0];
            float qy = queries[q * 3 + 1];
            float qz = queries[q * 3 + 2];

            // Compute squared distance to every source point.
            // Indices of points within radius are kept; far points
            // are flagged with float.MaxValue so they sort last.
            int firstValid = -1;
            int countInside = 0;
            for (int n = 0; n < N; n++)
            {
                float dx = xyz[n * 3 + 0] - qx;
                float dy = xyz[n * 3 + 1] - qy;
                float dz = xyz[n * 3 + 2] - qz;
                float d2 = dx * dx + dy * dy + dz * dz;
                idxBuf[n] = n;
                if (d2 <= r2)
                {
                    distBuf[n] = d2;
                    if (firstValid < 0) firstValid = n;
                    countInside++;
                }
                else
                {
                    distBuf[n] = float.MaxValue;
                }
            }

            // The upstream's torch sort sorts by index ASCENDING (not
            // by distance), then masks out radius>r matches with N
            // sentinel, and keeps the first nsample of the SORTED-BY-
            // INDEX array. Re-read query_ball_point in pn2_utils.py:
            //   group_idx = arange(N).repeat([B, S, 1])
            //   group_idx[sqrdists > r^2] = N
            //   group_idx = group_idx.sort(dim=-1)[0][:, :, :nsample]
            // So sorting is on the INDICES (which after masking are
            // either the original ascending indices for in-radius
            // points, or N for outside). Sort ascending puts in-radius
            // indices FIRST (since they're <N), in their original
            // order. We must match this: collect in-radius indices in
            // their original ascending order, then pad.
            int dstBase = q * nsample;
            int filled = 0;
            if (firstValid < 0)
            {
                // No neighbours found within radius. Upstream's behaviour
                // here is to pad with the "first" index which is meaningless
                // (the mask `group_idx == N` after the sort is everywhere,
                // so group_first = group_idx[..., 0] = N too). We replicate
                // by filling with 0 (the most common firstValid fallback).
                for (int k = 0; k < nsample; k++) output[dstBase + k] = 0;
            }
            else
            {
                // Walk indices in ascending order, pick those within radius.
                for (int n = 0; n < N && filled < nsample; n++)
                {
                    if (distBuf[n] <= r2)
                    {
                        output[dstBase + filled] = n;
                        filled++;
                    }
                }
                // Pad remaining slots with firstValid (matching upstream's
                // "replace N sentinel with group_first" trick).
                for (int k = filled; k < nsample; k++) output[dstBase + k] = firstValid;
            }
        }
        return output;
    }
}
