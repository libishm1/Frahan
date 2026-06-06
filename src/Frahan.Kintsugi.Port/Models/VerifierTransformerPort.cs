#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Faithful port of upstream's `VerifierTransformer`
/// (puzzlefusion_plusplus/verifier/model/modules/verifier_transformer.py).
///
/// Architecture per upstream:
///   self.edge_feature_emb = nn.Linear(7, embed_dim)
///   self.edge_indices_pe  = PositionalEncoding(embed_dim // 2, max_len=20)
///   self.transformer_encoder = nn.TransformerEncoder(
///       TransformerEncoderLayer(d_model=embed_dim, nhead=num_heads,
///                               dim_feedforward=2048, dropout=0.1,
///                               batch_first=True, activation='gelu'),
///       num_layers=num_layers, enable_nested_tensor=False
///   )
///   self.mlp_out = nn.Linear(embed_dim, 1)
///
///   forward(edge_features [B, E, 7], edge_indices [B, E, 2], mask [B, E]):
///     edge_features = edge_feature_emb(edge_features)
///     pe = edge_indices_pe.pe[0]                # [max_len, embed_dim//2]
///     edge_indices_pe = pe[edge_indices].reshape(B, E, -1)  # [B, E, embed_dim]
///     data_emb = edge_indices_pe + edge_features
///     edge_features = transformer_encoder(data_emb, src_key_padding_mask=~mask)
///     return mlp_out(edge_features)              # [B, E, 1] logits
///
/// nn.TransformerEncoderLayer uses CONCAT in_proj (Wq+Wk+Wv stacked
/// as a single [3*D, D] weight named in_proj_weight). This is
/// DIFFERENT from the denoiser's split to_q/to_k/to_v -- we split
/// it back into Wq/Wk/Wv at load time.
/// </summary>
public sealed class VerifierTransformerPort
{
    public sealed class Config
    {
        public int EmbedDim = 256;
        public int NumLayers = 6;
        public int NumHeads = 8;
        public int FfnDim = 2048;
        public int MaxLen = 20;
        public int EdgeFeatureDim = 7;
    }

    public sealed class Weights
    {
        // edge_feature_emb: nn.Linear(7, embed_dim)
        public float[] EdgeFeatureEmbW;   // [D, 7]
        public float[] EdgeFeatureEmbB;   // [D]
        // 6 transformer encoder layers (PyTorch's nn.TransformerEncoderLayer)
        public LayerWeights[] Layers;
        // mlp_out: nn.Linear(D, 1)
        public float[] MlpOutW;   // [1, D]
        public float[] MlpOutB;   // [1]
    }

    public struct LayerWeights
    {
        // Self-attention: in_proj is the CONCAT layout
        public float[] InProjW;    // [3*D, D]  rows = [Wq | Wk | Wv]
        public float[] InProjB;    // [3*D]
        public float[] OutProjW;   // [D, D]
        public float[] OutProjB;   // [D]
        // FFN: Linear(D, FfnDim) -> activation -> Linear(FfnDim, D)
        public float[] FfnL1W, FfnL1B;
        public float[] FfnL2W, FfnL2B;
        // norm1 + norm2 (regular LN, affine, post-norm in PyTorch default)
        public float[] N1Gamma, N1Beta;
        public float[] N2Gamma, N2Beta;
    }

    private readonly Config _cfg;
    private readonly MultiHeadAttention _mha;

    public VerifierTransformerPort(Config cfg)
    {
        _cfg = cfg ?? new Config();
        _mha = new MultiHeadAttention(_cfg.EmbedDim, _cfg.NumHeads);
    }

    /// <summary>
    /// Build the upstream PositionalEncoding buffer [max_len, embed_dim//2].
    /// Used to look up per-edge positional codes for both endpoints.
    /// </summary>
    private float[] BuildPe(int maxLen, int dHalf)
    {
        var pe = new float[maxLen * dHalf];
        for (int pos = 0; pos < maxLen; pos++)
        {
            for (int i = 0; i < dHalf; i += 2)
            {
                double divTerm = Math.Exp(-Math.Log(10000.0) * i / dHalf);
                pe[pos * dHalf + i] = (float)Math.Sin(pos * divTerm);
                if (i + 1 < dHalf)
                    pe[pos * dHalf + i + 1] = (float)Math.Cos(pos * divTerm);
            }
        }
        return pe;
    }

    /// <summary>
    /// Forward pass. Returns the per-edge logit (pre-sigmoid). Apply
    /// sigmoid + threshold > 0.5 (or 0.9 for high-confidence accept)
    /// downstream.
    /// </summary>
    /// <param name="edgeFeatures">[E, 7] row-major edge feature vectors (4 quat + 3 trans).</param>
    /// <param name="edgeIndices">[E, 2] row-major part-index pairs.</param>
    /// <param name="validMask">[E] 1 = valid edge, 0 = padded.</param>
    /// <param name="E">Number of edges.</param>
    public float[] Forward(float[] edgeFeatures, int[] edgeIndices, float[] validMask, int E, Weights w)
    {
        int D = _cfg.EmbedDim;
        int dHalf = D / 2;
        // ---- 1. edge_feature_emb: [E, 7] @ W.T (W = [D, 7]) + b -> [E, D]
        var eEmb = new float[E * D];
        var wT = Transpose(w.EdgeFeatureEmbW, D, 7);
        Matmul.MatMul(edgeFeatures, wT, eEmb, E, 7, D);
        if (w.EdgeFeatureEmbB != null) Matmul.AddBias(eEmb, w.EdgeFeatureEmbB, E, D);

        // ---- 2. edge_indices_pe: pe[edge_indices].reshape(E, -1)
        // pe is [maxLen, D//2]. For each edge, look up pe[idx0] + pe[idx1]
        // and concat to get a [D] vector.
        var pe = BuildPe(_cfg.MaxLen, dHalf);
        var idxPe = new float[E * D];
        for (int e = 0; e < E; e++)
        {
            int i0 = edgeIndices[e * 2 + 0];
            int i1 = edgeIndices[e * 2 + 1];
            i0 = Math.Max(0, Math.Min(_cfg.MaxLen - 1, i0));
            i1 = Math.Max(0, Math.Min(_cfg.MaxLen - 1, i1));
            // First half: pe[i0], second half: pe[i1].
            Buffer.BlockCopy(pe, i0 * dHalf * sizeof(float),
                             idxPe, e * D * sizeof(float), dHalf * sizeof(float));
            Buffer.BlockCopy(pe, i1 * dHalf * sizeof(float),
                             idxPe, (e * D + dHalf) * sizeof(float), dHalf * sizeof(float));
        }
        // ---- 3. data_emb = idxPe + eEmb
        for (int i = 0; i < E * D; i++) eEmb[i] += idxPe[i];

        // ---- 4. 6 transformer encoder layers. PyTorch's default LayerNorm
        // placement is POST-norm: attn(x) -> add residual -> LN -> FFN -> add
        // -> LN. But pytorch nn.TransformerEncoderLayer with norm_first=False
        // (the default) does ATTENDS first, residual, NORMS, FFN, residual,
        // NORMS. Let's match that.
        for (int lyr = 0; lyr < _cfg.NumLayers; lyr++)
        {
            var lw = w.Layers[lyr];
            // Save residual.
            var residual = new float[E * D];
            Buffer.BlockCopy(eEmb, 0, residual, 0, E * D * sizeof(float));
            // Self-attention via concat QKV: split InProjW [3*D, D] into
            // Wq/Wk/Wv each [D, D], and InProjB [3*D] into Bq/Bk/Bv each [D].
            var wq = new float[D * D];
            var wk = new float[D * D];
            var wv = new float[D * D];
            Array.Copy(lw.InProjW, 0 * D * D, wq, 0, D * D);
            Array.Copy(lw.InProjW, 1 * D * D, wk, 0, D * D);
            Array.Copy(lw.InProjW, 2 * D * D, wv, 0, D * D);
            float[] bq = null, bk = null, bv = null;
            if (lw.InProjB != null)
            {
                bq = new float[D]; Array.Copy(lw.InProjB, 0, bq, 0, D);
                bk = new float[D]; Array.Copy(lw.InProjB, D, bk, 0, D);
                bv = new float[D]; Array.Copy(lw.InProjB, 2 * D, bv, 0, D);
            }
            _mha.Apply(eEmb, wq, bq, wk, bk, wv, bv, lw.OutProjW, lw.OutProjB, E);
            for (int i = 0; i < E * D; i++) eEmb[i] += residual[i];
            // Norm1 (post-norm).
            LayerNorm.Apply(eEmb, lw.N1Gamma, lw.N1Beta, E, D);

            // FFN: Linear(D, Ffn) -> GeLU -> Linear(Ffn, D)
            Buffer.BlockCopy(eEmb, 0, residual, 0, E * D * sizeof(float));
            var ffn1WT = Transpose(lw.FfnL1W, _cfg.FfnDim, D);
            var hidden = new float[E * _cfg.FfnDim];
            Matmul.MatMul(eEmb, ffn1WT, hidden, E, D, _cfg.FfnDim);
            if (lw.FfnL1B != null) Matmul.AddBias(hidden, lw.FfnL1B, E, _cfg.FfnDim);
            Activations.Gelu(hidden);
            var ffn2WT = Transpose(lw.FfnL2W, D, _cfg.FfnDim);
            Matmul.MatMul(hidden, ffn2WT, eEmb, E, _cfg.FfnDim, D);
            if (lw.FfnL2B != null) Matmul.AddBias(eEmb, lw.FfnL2B, E, D);
            for (int i = 0; i < E * D; i++) eEmb[i] += residual[i];
            LayerNorm.Apply(eEmb, lw.N2Gamma, lw.N2Beta, E, D);
        }

        // ---- 5. mlp_out: Linear(D, 1) -> [E, 1]
        var logits = new float[E];
        var mlpOutWT = Transpose(w.MlpOutW, 1, D);
        Matmul.MatMul(eEmb, mlpOutWT, logits, E, D, 1);
        if (w.MlpOutB != null) for (int i = 0; i < E; i++) logits[i] += w.MlpOutB[0];
        return logits;
    }

    private static float[] Transpose(float[] mat, int rows, int cols)
    {
        var t = new float[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                t[c * rows + r] = mat[r * cols + c];
        return t;
    }
}
