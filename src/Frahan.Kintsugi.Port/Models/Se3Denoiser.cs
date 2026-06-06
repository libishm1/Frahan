#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// SE(3) diffusion denoiser. Translates per-fragment noisy quaternion+
/// translation into a residual that, summed with the noisy input,
/// approaches the clean ground-truth pose.
///
/// PuzzleFusion++ architecture (from paper Sec 3.2):
///   - 6 stacked TransformerBlocks
///   - 8 heads × 512-D
///   - Intra-fragment self-attention + inter-fragment self-attention
///     interleaved (the paper alternates per block)
///   - Time embedding (sinusoidal, dim=256) added to the token feature
///     via a small MLP projection
///
/// Input per fragment: [feature_dim (encoder output, e.g. 256) + 7
/// (quat + trans)] = roughly 263-D, then projected to 512-D.
/// Output per fragment: 7-D residual (4 quat + 3 trans).
///
/// Skeleton: stub the forward pass; layer plumbing in place; weight
/// loading is Phase 7.
/// </summary>
public sealed class Se3Denoiser
{
    public sealed class Config
    {
        public int NumLayers = 6;
        public int EmbedDim = 512;
        public int NumHeads = 8;
        public int FfnDim = 2048;
        public int FeatureDim = 256;     // PointNet++ output channels
        public int TimeEmbedDim = 256;
        public int MaxDiffusionT = 1000;
    }

    public sealed class Weights
    {
        // Input projection: Linear(featureDim + 7 + timeEmbedDim, embedDim)
        public float[] InProjW;
        public float[] InProjB;
        // 6 transformer blocks (AdaLN-conditioned on time embedding)
        public AdaLNTransformerBlock.Weights[] Blocks;
        // Output projection: Linear(embedDim, 7)
        public float[] OutProjW;
        public float[] OutProjB;
    }

    private readonly Config _cfg;
    private readonly AdaLNTransformerBlock[] _blocks;

    public Se3Denoiser(Config cfg)
    {
        _cfg = cfg ?? new Config();
        _blocks = new AdaLNTransformerBlock[_cfg.NumLayers];
        for (int i = 0; i < _cfg.NumLayers; i++)
            _blocks[i] = new AdaLNTransformerBlock(_cfg.EmbedDim, _cfg.NumHeads, _cfg.FfnDim);
    }

    /// <summary>
    /// One denoising step. Inputs:
    ///   - features [N, FeatureDim]   PointNet++ encoder output per fragment
    ///   - poses    [N, 7]            noisy (quat 4 + trans 3) per fragment
    ///   - t        scalar in [0, T)  diffusion timestep
    /// Output:
    ///   - residuals [N, 7]           subtract from poses to get cleaner poses
    /// </summary>
    public float[] Forward(float[] features, float[] poses, int N, float t, Weights w)
    {
        int featDim = _cfg.FeatureDim;
        int embedDim = _cfg.EmbedDim;
        int tDim = _cfg.TimeEmbedDim;

        // 1. Time embedding broadcast to all fragments.
        var tEmb = TimeEmbedding.Encode(t, tDim);

        // 2. Per-fragment input: concat[features[i], poses[i], tEmb] -> [featDim + 7 + tDim]
        int inDim = featDim + 7 + tDim;
        var tokens = new float[N * inDim];
        for (int i = 0; i < N; i++)
        {
            int dstOff = i * inDim;
            Buffer.BlockCopy(features, i * featDim * sizeof(float), tokens, dstOff * sizeof(float), featDim * sizeof(float));
            Buffer.BlockCopy(poses, i * 7 * sizeof(float), tokens, (dstOff + featDim) * sizeof(float), 7 * sizeof(float));
            Buffer.BlockCopy(tEmb, 0, tokens, (dstOff + featDim + 7) * sizeof(float), tDim * sizeof(float));
        }

        // 3. In-projection to embedDim.
        var x = new float[N * embedDim];
        Matmul.MatMul(tokens, w.InProjW, x, N, inDim, embedDim);
        if (w.InProjB != null) Matmul.AddBias(x, w.InProjB, N, embedDim);

        // 4. Stacked AdaLN-conditioned transformer blocks. The time
        //    embedding is supplied to each block's two AdaLN
        //    normalisation layers, which predict gamma/beta from it.
        //    ALL inter-fragment attention for now (treating the N
        //    fragments as a single sequence of N tokens). The paper
        //    alternates intra/inter; this port treats all N as one
        //    sequence and lets the attention see everything. Phase 7
        //    will validate against the paper.
        for (int l = 0; l < _cfg.NumLayers; l++)
            _blocks[l].Forward(x, N, tEmb, tDim, w.Blocks[l]);

        // 5. Out-projection to 7-D residual.
        var residual = new float[N * 7];
        Matmul.MatMul(x, w.OutProjW, residual, N, embedDim, 7);
        if (w.OutProjB != null) Matmul.AddBias(residual, w.OutProjB, N, 7);
        return residual;
    }
}
