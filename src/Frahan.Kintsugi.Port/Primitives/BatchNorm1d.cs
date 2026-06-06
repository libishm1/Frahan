#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// 1-D BatchNorm at INFERENCE time. Matches torch.nn.BatchNorm1d in eval
/// mode: uses the running mean / running var captured during training,
/// not batch statistics.
///
///   x_hat[i, c] = (x[i, c] - running_mean[c]) / sqrt(running_var[c] + eps)
///   y[i, c]     = x_hat[i, c] * gamma[c] + beta[c]
///
/// Used by PuzzleFusion++'s PointNet++ encoder between every Conv2d 1x1
/// MLP layer.
/// </summary>
public static class BatchNorm1d
{
    private const float Epsilon = 1e-5f;

    /// <summary>In-place BN over x[M, C] where M is samples and C is channels.</summary>
    public static void Apply(float[] x, float[] gamma, float[] beta,
                              float[] runningMean, float[] runningVar,
                              int M, int C, float eps = Epsilon)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        if (x.Length < M * C) throw new ArgumentException("x too small.");
        if (gamma == null || gamma.Length < C) throw new ArgumentException("gamma missing/short.");
        if (beta == null || beta.Length < C) throw new ArgumentException("beta missing/short.");
        if (runningMean == null || runningMean.Length < C) throw new ArgumentException("running_mean missing/short.");
        if (runningVar == null || runningVar.Length < C) throw new ArgumentException("running_var missing/short.");

        // Pre-compute per-channel scale + bias.
        var scale = new float[C];
        var shift = new float[C];
        for (int c = 0; c < C; c++)
        {
            float inv = 1.0f / (float)Math.Sqrt(runningVar[c] + eps);
            scale[c] = gamma[c] * inv;
            shift[c] = beta[c] - runningMean[c] * gamma[c] * inv;
        }

        for (int i = 0; i < M; i++)
        {
            int row = i * C;
            for (int c = 0; c < C; c++)
                x[row + c] = x[row + c] * scale[c] + shift[c];
        }
    }
}
