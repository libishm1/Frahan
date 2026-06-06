#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Pairwise alignment verifier. A small classifier that, given a
/// pair of fragments and their proposed alignment, predicts a
/// scalar in [0, 1] = probability the alignment is correct.
///
/// PuzzleFusion++ inputs:
///   - Fragment index embeddings (one-hot or learned)
///   - 6-bin histogram of point-match distances
///   - Match count
///
/// Architecture: a few transformer blocks (typically 2-3, smaller
/// embed dim than the denoiser) plus a final Linear → sigmoid
/// classification head. Threshold ≥ 0.9 for accept.
///
/// Skeleton: stub the forward pass.
/// </summary>
public sealed class Verifier
{
    public sealed class Config
    {
        public int NumLayers = 3;
        public int EmbedDim = 128;
        public int NumHeads = 4;
        public int FfnDim = 512;
        public int InputDim = 14;     // sized from paper Sec 3.3 description
        public float Threshold = 0.9f;
    }

    public sealed class Weights
    {
        public float[] InProjW;
        public float[] InProjB;
        public TransformerBlock.Weights[] Blocks;
        public float[] OutHeadW;       // [embedDim, 1]
        public float[] OutHeadB;       // [1]
    }

    private readonly Config _cfg;
    private readonly TransformerBlock[] _blocks;

    public Verifier(Config cfg)
    {
        _cfg = cfg ?? new Config();
        _blocks = new TransformerBlock[_cfg.NumLayers];
        for (int i = 0; i < _cfg.NumLayers; i++)
            _blocks[i] = new TransformerBlock(_cfg.EmbedDim, _cfg.NumHeads, _cfg.FfnDim);
    }

    /// <summary>
    /// Score a pair. Returns p in [0, 1]; accept if p >= Threshold.
    /// </summary>
    public float Score(float[] pairFeatures, Weights w)
    {
        if (pairFeatures == null) throw new ArgumentNullException(nameof(pairFeatures));
        if (pairFeatures.Length < _cfg.InputDim) throw new ArgumentException("pairFeatures too small.");

        // 1. Project input to embed dim.
        var tokens = new float[_cfg.EmbedDim];
        // M=1 (one "pair token")
        Matmul.MatMul(pairFeatures, w.InProjW, tokens, 1, _cfg.InputDim, _cfg.EmbedDim);
        if (w.InProjB != null) Matmul.AddBias(tokens, w.InProjB, 1, _cfg.EmbedDim);

        // 2. Transformer stack (1-token sequence; reduces to identity
        //    attention but the FFN + LayerNorm transforms the feature).
        for (int l = 0; l < _cfg.NumLayers; l++)
            _blocks[l].Forward(tokens, 1, w.Blocks[l]);

        // 3. Linear head + sigmoid.
        float logit = w.OutHeadB != null ? w.OutHeadB[0] : 0f;
        for (int i = 0; i < _cfg.EmbedDim; i++) logit += tokens[i] * w.OutHeadW[i];
        return 1.0f / (1.0f + (float)Math.Exp(-logit));
    }

    public bool Accept(float score) => score >= _cfg.Threshold;
}
