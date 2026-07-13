#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Multi-head self-attention. Input: tokens [M, D]. Output: same shape.
/// Matches PyTorch nn.MultiheadAttention(embed_dim=D, num_heads=H,
/// batch_first=True) for the un-masked, self-attention path used by
/// PuzzleFusion++'s transformer denoiser and verifier.
///
/// Math:
///   Q = x @ Wq + bq   shape [M, D]
///   K = x @ Wk + bk
///   V = x @ Wv + bv
///   reshape to [H, M, d] where d = D / H
///   scores = Q @ K^T / sqrt(d)   shape [H, M, M]
///   weights = softmax(scores, axis=-1)
///   attn_out = weights @ V       shape [H, M, d]
///   merge heads back to [M, D]
///   out = attn_out @ Wo + bo
///
/// Weights are passed in as raw float arrays in PyTorch's layout:
/// nn.Linear(D, D).weight is [D, D] row-major (out, in). Caller is
/// responsible for the in_proj/out_proj split.
/// </summary>
public sealed class MultiHeadAttention
{
    private readonly int _embedDim;
    private readonly int _numHeads;
    private readonly int _headDim;

    public MultiHeadAttention(int embedDim, int numHeads)
    {
        if (embedDim % numHeads != 0)
            throw new ArgumentException("embedDim must be divisible by numHeads.");
        _embedDim = embedDim;
        _numHeads = numHeads;
        _headDim = embedDim / numHeads;
    }

    /// <summary>Apply self-attention to x[M, D] in place.</summary>
    /// <param name="x">Input tokens, modified in place to hold the output.</param>
    /// <param name="wq"><c>nn.Linear(D, D)</c> weight, shape [D, D].</param>
    /// <param name="bq">Bias [D] or null.</param>
    /// <param name="wk"><c>nn.Linear(D, D)</c> weight.</param>
    /// <param name="bk">Bias [D] or null.</param>
    /// <param name="wv"><c>nn.Linear(D, D)</c> weight.</param>
    /// <param name="bv">Bias [D] or null.</param>
    /// <param name="wo">Output projection weight [D, D].</param>
    /// <param name="bo">Bias [D] or null.</param>
    /// <param name="M">Number of tokens.</param>
    public void Apply(float[] x,
        float[] wq, float[] bq,
        float[] wk, float[] bk,
        float[] wv, float[] bv,
        float[] wo, float[] bo,
        int M)
    {
        Apply(x, wq, bq, wk, bk, wv, bv, wo, bo, M, attendMask: null);
    }

    /// <summary>
    /// Mask-aware overload. attendMask: optional [M*M] row-major boolean
    /// (1.0 = attend, 0.0 = don't-attend). Don't-attend positions get
    /// their attention scores set to -1e9 before softmax, effectively
    /// zeroing their weight. Pass null to disable masking.
    /// </summary>
    public void Apply(float[] x,
        float[] wq, float[] bq,
        float[] wk, float[] bk,
        float[] wv, float[] bv,
        float[] wo, float[] bo,
        int M,
        float[] attendMask)
    {
        int D = _embedDim;
        int H = _numHeads;
        int d = _headDim;

        // 1. Project Q, K, V: each [M, D]
        // CRITICAL: PyTorch nn.Linear stores weight as [out_features, in_features]
        // and computes y = x @ W.T + b. The kintsugi.bin tensor `self_attn.to_q.weight`
        // is shape [D, D] in PyTorch's [out, in] convention. To get the SAME math
        // (x @ W.T) using our row-major Matmul(A[M,K] @ B[K,N] = C[M,N]), we must
        // TRANSPOSE the loaded weight first: T(W) has shape [in, out] = [D, D],
        // and x @ T(W) = x @ W.T. Prior to this fix, MHA was computing x @ W
        // (without transpose), which produced wildly off attention outputs
        // (~5-15x magnitude error per the v2 parity sub-block bisection).
        var q = new float[M * D];
        var k = new float[M * D];
        var v = new float[M * D];
        var wqT = TransposeSquare(wq, D);
        var wkT = TransposeSquare(wk, D);
        var wvT = TransposeSquare(wv, D);
        Matmul.MatMul(x, wqT, q, M, D, D);
        Matmul.MatMul(x, wkT, k, M, D, D);
        Matmul.MatMul(x, wvT, v, M, D, D);
        if (bq != null) Matmul.AddBias(q, bq, M, D);
        if (bk != null) Matmul.AddBias(k, bk, M, D);
        if (bv != null) Matmul.AddBias(v, bv, M, D);

        // 2. For each head, compute attention separately.
        var scores = new float[M * M];
        var headOut = new float[M * d];
        var concatOut = new float[M * D];
        float scale = 1.0f / (float)Math.Sqrt(d);

        for (int h = 0; h < H; h++)
        {
            int hOff = h * d;
            // 2a. Build per-head Qh [M, d] and Kh [M, d] views by stride.
            // Compute scores[i, j] = (Qh[i] dot Kh[j]) * scale.
            for (int i = 0; i < M; i++)
            {
                int qRow = i * D + hOff;
                for (int j = 0; j < M; j++)
                {
                    int kRow = j * D + hOff;
                    float s = 0;
                    for (int t = 0; t < d; t++) s += q[qRow + t] * k[kRow + t];
                    scores[i * M + j] = s * scale;
                }
            }
            // 2a'. Apply attention mask (HARD). The reference runs on torch 2.x,
            // so diffusers uses AttnProcessor2_0 -> F.scaled_dot_product_attention
            // with a BOOL attn_mask, whose semantics are: True = attend,
            // False = -inf (fully blocked). PuzzleFusion++ passes bool masks
            // (self_mask block-diagonal, gen_mask key-validity). So mask==0 must
            // become -inf, NOT a soft +1 bias. The prior +1-bias code let
            // cross-fragment self-attention leak, diverging from the reference
            // (isolated: after-self_attn 11.66% off with +1 bias).
            if (attendMask != null)
            {
                for (int idx = 0; idx < M * M; idx++)
                    if (attendMask[idx] <= 0.5f) scores[idx] = -1e9f;
            }
            // 2b. Softmax along last axis (each row).
            Activations.SoftmaxRowwise(scores, M, M);
            // 2c. headOut[i, :] = sum_j weights[i, j] * V[j, hOff..hOff+d]
            Array.Clear(headOut, 0, M * d);
            for (int i = 0; i < M; i++)
            {
                int outRow = i * d;
                for (int j = 0; j < M; j++)
                {
                    float w = scores[i * M + j];
                    int vRow = j * D + hOff;
                    for (int t = 0; t < d; t++) headOut[outRow + t] += w * v[vRow + t];
                }
            }
            // 2d. Write back into concatOut at columns [hOff, hOff + d).
            for (int i = 0; i < M; i++)
            {
                int srcRow = i * d;
                int dstRow = i * D + hOff;
                for (int t = 0; t < d; t++) concatOut[dstRow + t] = headOut[srcRow + t];
            }
        }

        // 3. Output projection: x[M, D] = concatOut @ Wo.T + bo
        // (Same nn.Linear transpose-convention fix as Q/K/V above.)
        var woT = TransposeSquare(wo, D);
        Matmul.MatMul(concatOut, woT, x, M, D, D);
        if (bo != null) Matmul.AddBias(x, bo, M, D);
    }

    /// <summary>Transpose a square [D, D] matrix in row-major layout.</summary>
    private static float[] TransposeSquare(float[] m, int D)
    {
        var t = new float[D * D];
        for (int i = 0; i < D; i++)
            for (int j = 0; j < D; j++)
                t[j * D + i] = m[i * D + j];
        return t;
    }
}
