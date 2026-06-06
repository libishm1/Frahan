#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Faithful port of the upstream `EncoderLayer` in
/// `puzzlefusion_plusplus/denoiser/model/modules/attention.py`.
///
/// Structure (matches upstream verbatim):
///   norm1       = MyAdaLayerNorm(dim, num_embeds_ada_norm)
///   self_attn   = diffusers Attention(query_dim=dim, heads, dim_head)
///   norm2       = MyAdaLayerNorm(dim, num_embeds_ada_norm)
///   global_attn = diffusers Attention(query_dim=dim, heads, dim_head)
///   norm3       = nn.LayerNorm(dim, elementwise_affine=True)
///   ff          = FeedForward(dim, activation_fn='geglu') = GEGLU + Linear
///
/// Forward (per upstream attention.py::EncoderLayer.forward):
///   1. norm_h = norm1(h, timestep)          (MyAdaLN)
///   2. attn   = self_attn(norm_h, self_mask)
///   3. h      = h + attn
///   4. norm_h = norm2(h, timestep)          (MyAdaLN)
///   5. attn   = global_attn(norm_h, gen_mask)
///   6. h      = h + attn
///   7. norm_h = norm3(h)                    (regular LN, learnable affine)
///   8. ff     = FeedForward(norm_h)         (GEGLU + Linear)
///   9. h      = h + ff
/// </summary>
public sealed class DenoiserEncoderLayerPort
{
    public struct Weights
    {
        // norm1 (MyAdaLayerNorm) parameters
        public float[] N1EmbWeight;   // [num_embeds_ada_norm, dim]
        public float[] N1LinearW;     // [2*dim, dim]
        public float[] N1LinearB;     // [2*dim]
        // self_attn (diffusers Attention)
        public float[] SelfWq, SelfBq;
        public float[] SelfWk, SelfBk;
        public float[] SelfWv, SelfBv;
        public float[] SelfWo, SelfBo;
        // norm2 (MyAdaLayerNorm)
        public float[] N2EmbWeight, N2LinearW, N2LinearB;
        // global_attn
        public float[] GlobalWq, GlobalBq;
        public float[] GlobalWk, GlobalBk;
        public float[] GlobalWv, GlobalBv;
        public float[] GlobalWo, GlobalBo;
        // norm3 (regular LayerNorm with learnable affine)
        public float[] N3Gamma, N3Beta;
        // FFN: ff.net.0 (GEGLU) + ff.net.2 (Linear back to dim)
        public float[] FfGegluW, FfGegluB;  // [2*4D, D], [2*4D]
        public float[] FfOutW, FfOutB;       // [D, 4D], [D]
    }

    private readonly int _dim;
    private readonly int _ffnDim;          // = 4 * dim per diffusers default
    private readonly int _numEmbedsAdaNorm;
    private readonly MultiHeadAttention _selfAttn;
    private readonly MultiHeadAttention _globalAttn;

    public DenoiserEncoderLayerPort(int dim, int numAttentionHeads, int numEmbedsAdaNorm)
    {
        _dim = dim;
        _ffnDim = 4 * dim;
        _numEmbedsAdaNorm = numEmbedsAdaNorm;
        _selfAttn = new MultiHeadAttention(dim, numAttentionHeads);
        _globalAttn = new MultiHeadAttention(dim, numAttentionHeads);
    }

    /// <summary>
    /// Forward pass; hidden_states[M, D] is mutated in place. Timestep
    /// is a single integer index into the AdaLN embedding table.
    /// selfMask: optional [M*M] 1=attend / 0=mask, applied to self-attn.
    /// genMask: optional [M*M] applied to global-attn. Per upstream
    /// _gen_mask, selfMask is block-diagonal (each fragment's L tokens
    /// attend only within themselves) and genMask is the outer product
    /// of part_valids broadcast to L tokens.
    /// </summary>
    public void Forward(float[] h, int M, int timestep, Weights w,
                         float[] selfMask = null, float[] genMask = null)
    {
        int D = _dim;

        // residual1 = clone
        var residual = new float[M * D];
        Buffer.BlockCopy(h, 0, residual, 0, M * D * sizeof(float));

        // 1. norm1 -> self_attn -> add residual
        MyAdaLayerNorm.Apply(h, M, D, timestep,
            w.N1EmbWeight, _numEmbedsAdaNorm, w.N1LinearW, w.N1LinearB);
        _selfAttn.Apply(h,
            w.SelfWq, w.SelfBq, w.SelfWk, w.SelfBk,
            w.SelfWv, w.SelfBv, w.SelfWo, w.SelfBo, M, selfMask);
        for (int i = 0; i < M * D; i++) h[i] += residual[i];

        // residual2 = clone
        Buffer.BlockCopy(h, 0, residual, 0, M * D * sizeof(float));

        // 2. norm2 -> global_attn -> add residual
        MyAdaLayerNorm.Apply(h, M, D, timestep,
            w.N2EmbWeight, _numEmbedsAdaNorm, w.N2LinearW, w.N2LinearB);
        _globalAttn.Apply(h,
            w.GlobalWq, w.GlobalBq, w.GlobalWk, w.GlobalBk,
            w.GlobalWv, w.GlobalBv, w.GlobalWo, w.GlobalBo, M, genMask);
        for (int i = 0; i < M * D; i++) h[i] += residual[i];

        // residual3 = clone
        Buffer.BlockCopy(h, 0, residual, 0, M * D * sizeof(float));

        // 3. norm3 (regular LN with affine) -> GEGLU FFN -> Linear back -> residual
        LayerNorm.Apply(h, w.N3Gamma, w.N3Beta, M, D);
        var hidden = Geglu.Apply(h, M, D, _ffnDim, w.FfGegluW, w.FfGegluB);
        // Linear(4D -> D): out = hidden @ W.T + b   where W is [D, 4D]
        var wOutT = new float[_ffnDim * D];
        for (int o = 0; o < D; o++)
            for (int k = 0; k < _ffnDim; k++)
                wOutT[k * D + o] = w.FfOutW[o * _ffnDim + k];
        var ffOut = new float[M * D];
        Matmul.MatMul(hidden, wOutT, ffOut, M, _ffnDim, D);
        if (w.FfOutB != null) Matmul.AddBias(ffOut, w.FfOutB, M, D);
        for (int i = 0; i < M * D; i++) h[i] = ffOut[i] + residual[i];
    }
}
