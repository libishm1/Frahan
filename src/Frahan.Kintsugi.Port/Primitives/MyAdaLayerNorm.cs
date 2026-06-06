#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Faithful port of the upstream `MyAdaLayerNorm` in
/// `puzzlefusion_plusplus/denoiser/model/modules/attention.py`.
///
/// Structure (per upstream):
///   self.emb    = nn.Embedding(num_embeddings, embedding_dim)
///   self.silu   = nn.SiLU()
///   self.linear = nn.Linear(embedding_dim, embedding_dim * 2)
///   self.norm   = nn.LayerNorm(embedding_dim, elementwise_affine=False)
///
///   forward(x[B*S, D], timestep[B] int):
///     e = linear(silu(emb[timestep]))      # [B, 2*D]
///     scale, shift = e.chunk(2, dim=1)     # [B, D] each
///     x = norm(x) * (1 + scale[:, None]) + shift[:, None]
///
/// NOTE 1: This is a TWO-chunk (scale + shift) AdaLN, distinct from
/// Frahan.Kintsugi.Port.Primitives.AdaLN which is three-chunk (shift,
/// scale, gate). They are different layers from different code paths.
///
/// NOTE 2: For PuzzleFusion++ inference the typical batch is B=1 with
/// timestep a single integer; we keep that assumption in the API.
/// </summary>
public static class MyAdaLayerNorm
{
    /// <summary>
    /// Apply MyAdaLN in-place on x[M, D]. The same (scale, shift)
    /// vector is broadcast across all M tokens (since one timestep
    /// produces one conditioning vector).
    /// </summary>
    /// <param name="x">Tokens to normalise, shape [M*D]. Mutated.</param>
    /// <param name="M">Number of tokens.</param>
    /// <param name="D">Per-token dimension.</param>
    /// <param name="timestep">Integer timestep index into the embedding table.</param>
    /// <param name="embWeight">nn.Embedding.weight [N_embeds, D] row-major.</param>
    /// <param name="linearW">nn.Linear.weight [2*D, D] row-major.</param>
    /// <param name="linearB">nn.Linear.bias [2*D] or null.</param>
    public static void Apply(float[] x, int M, int D,
                              int timestep,
                              float[] embWeight, int nEmbeds,
                              float[] linearW, float[] linearB)
    {
        if (x == null || x.Length < M * D) throw new ArgumentException("x too small.");
        if (embWeight == null || embWeight.Length < nEmbeds * D) throw new ArgumentException("embWeight too small.");
        if (linearW == null || linearW.Length < 2 * D * D) throw new ArgumentException("linearW too small.");
        if (timestep < 0 || timestep >= nEmbeds)
            throw new ArgumentOutOfRangeException(nameof(timestep),
                $"timestep {timestep} out of range [0, {nEmbeds}).");

        // 1. emb[timestep] = embWeight[timestep, :]  shape [D]
        var emb = new float[D];
        int embRow = timestep * D;
        for (int i = 0; i < D; i++) emb[i] = embWeight[embRow + i];

        // 2. SiLU(emb)
        for (int i = 0; i < D; i++) emb[i] = emb[i] / (1.0f + (float)Math.Exp(-emb[i]));

        // 3. linear(emb): out = emb @ W.T + b   shape [2*D]
        var lin = new float[2 * D];
        Matmul.MatVec(linearW, emb, lin, 2 * D, D);
        if (linearB != null) for (int i = 0; i < 2 * D; i++) lin[i] += linearB[i];

        // 4. scale = lin[0..D], shift = lin[D..2D]
        // Apply LayerNorm (no learnable gamma/beta) and then
        //   x = norm(x) * (1 + scale) + shift
        // broadcast across tokens.
        LayerNorm.Apply(x, null, null, M, D);
        for (int m = 0; m < M; m++)
        {
            int row = m * D;
            for (int c = 0; c < D; c++)
                x[row + c] = x[row + c] * (1.0f + lin[c]) + lin[D + c];
        }
    }
}
