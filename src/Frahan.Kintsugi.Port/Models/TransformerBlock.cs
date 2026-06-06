#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// One transformer block: pre-LayerNorm → MultiHeadAttention →
/// residual → pre-LayerNorm → FFN (2 linear with GELU between) →
/// residual. PyTorch nn.TransformerEncoderLayer with
/// norm_first=True. Used by PuzzleFusion++'s denoiser (6 stacked
/// blocks) and verifier (smaller stack).
///
/// Weights are stored in a flat <see cref="Weights"/> struct so the
/// caller can load them from a state-dict in one pass.
/// </summary>
public sealed class TransformerBlock
{
    public struct Weights
    {
        // LayerNorm 1 (pre-attention)
        public float[] Ln1Gamma;
        public float[] Ln1Beta;
        // Attention: in_proj is [3*D, D] in PyTorch's concat layout;
        // we split into separate Wq/Wk/Wv for clarity.
        public float[] Wq, Bq;
        public float[] Wk, Bk;
        public float[] Wv, Bv;
        public float[] Wo, Bo;
        // LayerNorm 2 (pre-FFN)
        public float[] Ln2Gamma;
        public float[] Ln2Beta;
        // FFN: Linear(D, 4D) then Linear(4D, D)
        public float[] FfnW1, FfnB1;
        public float[] FfnW2, FfnB2;
    }

    private readonly int _embedDim;
    private readonly int _ffnDim;
    private readonly MultiHeadAttention _mha;

    public TransformerBlock(int embedDim, int numHeads, int ffnDim = 0)
    {
        _embedDim = embedDim;
        _ffnDim = ffnDim > 0 ? ffnDim : 4 * embedDim;
        _mha = new MultiHeadAttention(embedDim, numHeads);
    }

    /// <summary>Forward pass; x[M, D] is modified in place.</summary>
    public void Forward(float[] x, int M, Weights w)
    {
        int D = _embedDim;
        // residual = x.clone()
        var residual = new float[M * D];
        Buffer.BlockCopy(x, 0, residual, 0, M * D * sizeof(float));

        // ---- LN1 + MHA + residual
        LayerNorm.Apply(x, w.Ln1Gamma, w.Ln1Beta, M, D);
        _mha.Apply(x, w.Wq, w.Bq, w.Wk, w.Bk, w.Wv, w.Bv, w.Wo, w.Bo, M);
        for (int i = 0; i < M * D; i++) x[i] += residual[i];

        // ---- residual = x.clone()
        Buffer.BlockCopy(x, 0, residual, 0, M * D * sizeof(float));

        // ---- LN2 + FFN + residual
        LayerNorm.Apply(x, w.Ln2Gamma, w.Ln2Beta, M, D);
        // FFN: Linear(D, 4D) -> GELU -> Linear(4D, D)
        var hidden = new float[M * _ffnDim];
        Matmul.MatMul(x, w.FfnW1, hidden, M, D, _ffnDim);
        if (w.FfnB1 != null) Matmul.AddBias(hidden, w.FfnB1, M, _ffnDim);
        Activations.Gelu(hidden);
        Matmul.MatMul(hidden, w.FfnW2, x, M, _ffnDim, D);
        if (w.FfnB2 != null) Matmul.AddBias(x, w.FfnB2, M, D);
        for (int i = 0; i < M * D; i++) x[i] += residual[i];
    }
}
