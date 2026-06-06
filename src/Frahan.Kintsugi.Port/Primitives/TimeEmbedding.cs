#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Sinusoidal time embedding used by the diffusion denoiser. Maps a
/// scalar diffusion timestep t to a fixed D-dimensional embedding
/// where the first D/2 components are sin(t * w_i) and the next D/2
/// are cos(t * w_i), with logarithmically-spaced frequencies w_i.
///
/// Standard "Attention Is All You Need" formula; matches the upstream
/// PyTorch reference.
/// </summary>
public static class TimeEmbedding
{
    /// <summary>Compute the sinusoidal time embedding for one scalar t.</summary>
    /// <param name="t">Diffusion timestep, integer in [0, T-1] cast to float.</param>
    /// <param name="dim">Embedding dimension. Must be even.</param>
    /// <param name="maxPeriod">Maximum frequency period; PuzzleFusion++ uses 10000.</param>
    /// <returns>Float array of length <paramref name="dim"/>.</returns>
    public static float[] Encode(float t, int dim, float maxPeriod = 10000f)
    {
        if (dim % 2 != 0) throw new ArgumentException("dim must be even.");
        int half = dim / 2;
        var emb = new float[dim];
        // freqs[i] = exp(-ln(maxPeriod) * i / half), i in [0, half)
        // arguments[i] = t * freqs[i]
        float lnPeriod = (float)Math.Log(maxPeriod);
        for (int i = 0; i < half; i++)
        {
            float freq = (float)Math.Exp(-lnPeriod * i / half);
            float arg = t * freq;
            emb[i] = (float)Math.Sin(arg);
            emb[half + i] = (float)Math.Cos(arg);
        }
        return emb;
    }
}
