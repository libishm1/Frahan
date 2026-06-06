#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Layer normalization, PyTorch-style. Normalises ALONG the last
/// axis: each row of a row-major [M, N] matrix is independently
/// re-centred (mean 0) and re-scaled (variance 1), then optionally
/// re-scaled+shifted by learned gamma/beta vectors of length N.
///
/// Matches torch.nn.LayerNorm with normalized_shape = (N,),
/// elementwise_affine = True/False per the gamma/beta args.
/// </summary>
public static class LayerNorm
{
    private const float Epsilon = 1e-5f;

    /// <summary>
    /// In-place LayerNorm over the last axis of x[M, N].
    /// </summary>
    /// <param name="x">Tensor [M,N] row-major.</param>
    /// <param name="gamma">Optional learned scale [N]. Null => no scale.</param>
    /// <param name="beta">Optional learned shift [N]. Null => no shift.</param>
    /// <param name="M">Number of rows / tokens.</param>
    /// <param name="N">Feature dim.</param>
    public static void Apply(float[] x, float[] gamma, float[] beta, int M, int N)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        if (x.Length < M * N) throw new ArgumentException("x too small.");
        if (gamma != null && gamma.Length < N) throw new ArgumentException("gamma too small.");
        if (beta != null && beta.Length < N) throw new ArgumentException("beta too small.");
        for (int i = 0; i < M; i++)
        {
            int row = i * N;
            // Compute mean.
            float mean = 0;
            for (int j = 0; j < N; j++) mean += x[row + j];
            mean /= N;
            // Compute variance.
            float var = 0;
            for (int j = 0; j < N; j++)
            {
                float d = x[row + j] - mean;
                var += d * d;
            }
            var /= N;
            float invStd = 1.0f / (float)Math.Sqrt(var + Epsilon);
            // Normalise + affine.
            for (int j = 0; j < N; j++)
            {
                float n = (x[row + j] - mean) * invStd;
                if (gamma != null) n *= gamma[j];
                if (beta != null) n += beta[j];
                x[row + j] = n;
            }
        }
    }
}
