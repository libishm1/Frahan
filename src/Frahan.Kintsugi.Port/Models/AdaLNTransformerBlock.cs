#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Transformer block with Adaptive Layer Norm (AdaLN). This is the
/// architecture PuzzleFusion++'s SE(3) denoiser uses: gamma/beta of
/// each LN are predicted from the time embedding via a learned linear
/// projection, and the residual stream is multiplicatively gated.
///
///   gate1, x_norm1 = AdaLN(x, t_emb, W_norm1_emb)
///   y = x + gate1 * Attn(x_norm1)
///   gate2, y_norm2 = AdaLN(y, t_emb, W_norm2_emb)
///   z = y + gate2 * FFN(y_norm2)
///
/// Plain <see cref="TransformerBlock"/> (with norm_first LN) remains
/// in use by the verifier, which does not condition on time.
/// </summary>
public sealed class AdaLNTransformerBlock
{
    public struct Weights
    {
        // AdaLN 1 emb projection (norm_first style; gamma/beta predicted)
        public float[] Ln1EmbW;   // shape [T_dim, 3*D]
        public float[] Ln1EmbB;   // optional [3*D]

        // Attention (split Q/K/V to match upstream's to_q/to_k/to_v)
        public float[] Wq, Bq;
        public float[] Wk, Bk;
        public float[] Wv, Bv;
        public float[] Wo, Bo;

        // AdaLN 2 emb projection
        public float[] Ln2EmbW;
        public float[] Ln2EmbB;

        // FFN: Linear(D, FfnDim) -> GELU -> Linear(FfnDim, D)
        public float[] FfnW1, FfnB1;
        public float[] FfnW2, FfnB2;
    }

    private readonly int _embedDim;
    private readonly int _ffnDim;
    private readonly MultiHeadAttention _mha;

    public AdaLNTransformerBlock(int embedDim, int numHeads, int ffnDim = 0)
    {
        _embedDim = embedDim;
        _ffnDim = ffnDim > 0 ? ffnDim : 4 * embedDim;
        _mha = new MultiHeadAttention(embedDim, numHeads);
    }

    /// <summary>Forward; x[M, D] modified in place. tEmb[T_dim] is the
    /// shared conditioning vector for all M tokens.</summary>
    public void Forward(float[] x, int M, float[] tEmb, int tDim, Weights w)
    {
        int D = _embedDim;

        // residual1 = x.clone()
        var residual = new float[M * D];
        Buffer.BlockCopy(x, 0, residual, 0, M * D * sizeof(float));

        // AdaLN1 -> MHA -> gated residual
        var gate1 = AdaLN.Apply(x, M, D, tEmb, tDim, w.Ln1EmbW, w.Ln1EmbB);
        _mha.Apply(x, w.Wq, w.Bq, w.Wk, w.Bk, w.Wv, w.Bv, w.Wo, w.Bo, M);
        for (int i = 0; i < M * D; i++) x[i] = residual[i] + gate1[i] * x[i];

        // residual2 = x.clone()
        Buffer.BlockCopy(x, 0, residual, 0, M * D * sizeof(float));

        // AdaLN2 -> FFN -> gated residual
        var gate2 = AdaLN.Apply(x, M, D, tEmb, tDim, w.Ln2EmbW, w.Ln2EmbB);
        var hidden = new float[M * _ffnDim];
        Matmul.MatMul(x, w.FfnW1, hidden, M, D, _ffnDim);
        if (w.FfnB1 != null) Matmul.AddBias(hidden, w.FfnB1, M, _ffnDim);
        Activations.Gelu(hidden);
        Matmul.MatMul(hidden, w.FfnW2, x, M, _ffnDim, D);
        if (w.FfnB2 != null) Matmul.AddBias(x, w.FfnB2, M, D);
        for (int i = 0; i < M * D; i++) x[i] = residual[i] + gate2[i] * x[i];
    }
}
