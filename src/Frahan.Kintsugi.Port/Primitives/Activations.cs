#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Element-wise activation functions used in PuzzleFusion++. The
/// PyTorch original uses GELU in the transformer feed-forwards
/// (default tanh-approximation) and SiLU in the PointNet++ feature
/// blocks.
///
/// All operate in-place on float arrays; SIMD acceleration is left
/// to a later pass — these are cheap relative to matmul.
/// </summary>
public static class Activations
{
    private static readonly float SqrtTwoOverPi = (float)Math.Sqrt(2.0 / Math.PI);

    /// <summary>
    /// GELU (tanh approximation). Matches PyTorch's
    /// nn.functional.gelu(approximate='tanh').
    /// GELU(x) = 0.5 * x * (1 + tanh(sqrt(2/pi) * (x + 0.044715 * x^3)))
    /// </summary>
    public static void Gelu(float[] x)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        for (int i = 0; i < x.Length; i++)
        {
            float v = x[i];
            float inner = SqrtTwoOverPi * (v + 0.044715f * v * v * v);
            x[i] = 0.5f * v * (1.0f + (float)Math.Tanh(inner));
        }
    }

    /// <summary>
    /// SiLU (Swish) activation. SiLU(x) = x * sigmoid(x) = x / (1 + e^-x).
    /// </summary>
    public static void Silu(float[] x)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        for (int i = 0; i < x.Length; i++)
        {
            float v = x[i];
            x[i] = v / (1.0f + (float)Math.Exp(-v));
        }
    }

    /// <summary>ReLU(x) = max(0, x).</summary>
    public static void Relu(float[] x)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        for (int i = 0; i < x.Length; i++)
            if (x[i] < 0) x[i] = 0;
        }

    /// <summary>Softmax along the LAST axis of a row-major [M, N] matrix.</summary>
    public static void SoftmaxRowwise(float[] x, int M, int N)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        if (x.Length < M * N) throw new ArgumentException("x too small.");
        for (int i = 0; i < M; i++)
        {
            int row = i * N;
            // Numeric stability: subtract row max.
            float max = x[row];
            for (int j = 1; j < N; j++) if (x[row + j] > max) max = x[row + j];
            float sum = 0;
            for (int j = 0; j < N; j++)
            {
                float e = (float)Math.Exp(x[row + j] - max);
                x[row + j] = e;
                sum += e;
            }
            float inv = 1.0f / sum;
            for (int j = 0; j < N; j++) x[row + j] *= inv;
        }
    }
}
