#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Faithful port of the upstream `DenoiserTransformer` in
/// `puzzlefusion_plusplus/denoiser/model/modules/denoiser_transformer.py`.
///
/// Forward signature (matches upstream verbatim):
///   x:           [B, N, 7]    noisy quat+translation per part
///   timesteps:   [B] long      diffusion step index
///   latent:      [B, N, L, D]  encoder features per part (D=64, L=25)
///   xyz:         [B, N, L, 3]  encoder keypoint positions
///   part_valids: [B, N]        per-part validity mask (1=valid)
///   scale:       [B, N, 1]     per-part scale
///   ref_part:    [B, N] bool   one-hot mark of the anchor fragment
///
///   returns:     [B, N, 7]    pose residuals
///
/// This port assumes B=1 (single-batch inference). The forward
/// processes one batch at a time; chain in a loop if you need
/// multi-batch.
/// </summary>
public sealed class DenoiserTransformerPort
{
    public sealed class Config
    {
        public int NumLayers = 6;
        public int EmbedDim = 512;
        public int NumHeads = 8;
        public int NumDim = 64;          // encoder feature channels (latent last dim)
        public int NumPoint = 25;        // L
        public int Multires = 10;
        public int OutChannels = 7;
        public int NumEmbedsAdaNorm = 6 * 512;  // upstream: 6 * model_channels
        public int MaxLen = 20;          // pos_encoding max parts
    }

    public sealed class Weights
    {
        // shape_embedding: nn.Linear(num_dim + pos_emb_out + scale_emb_out, embed_dim)
        public float[] ShapeEmbW;
        public float[] ShapeEmbB;
        // param_fc: nn.Linear(param_emb_out, embed_dim)
        public float[] ParamFcW;
        public float[] ParamFcB;
        // ref_part_emb: nn.Embedding(2, embed_dim) -- weight [2, embed_dim]
        public float[] RefPartEmbWeight;
        // pos_encoding buffer: precomputed [max_len, embed_dim]
        // (computed at runtime from sin/cos, so we don't store).
        // 6 transformer layers
        public DenoiserEncoderLayerPort.Weights[] Layers;
        // mlp_out_trans: 3 Linears with SiLU between
        public float[] TransL1W, TransL1B;  // [D, D]
        public float[] TransL2W, TransL2B;  // [D/2, D]
        public float[] TransL3W, TransL3B;  // [3, D/2]
        // mlp_out_rot: 3 Linears with SiLU between
        public float[] RotL1W, RotL1B;
        public float[] RotL2W, RotL2B;
        public float[] RotL3W, RotL3B;
    }

    private readonly Config _cfg;
    private readonly DenoiserEncoderLayerPort[] _layers;
    private readonly NerfEmbedder _paramEmb;
    private readonly NerfEmbedder _posEmb;
    private readonly NerfEmbedder _scaleEmb;

    public DenoiserTransformerPort(Config cfg)
    {
        _cfg = cfg ?? new Config();
        _layers = new DenoiserEncoderLayerPort[_cfg.NumLayers];
        for (int i = 0; i < _cfg.NumLayers; i++)
            _layers[i] = new DenoiserEncoderLayerPort(_cfg.EmbedDim, _cfg.NumHeads, _cfg.NumEmbedsAdaNorm);
        _paramEmb = new NerfEmbedder(7, _cfg.Multires);
        _posEmb   = new NerfEmbedder(3, _cfg.Multires);
        _scaleEmb = new NerfEmbedder(1, _cfg.Multires);
    }

    /// <summary>
    /// Build the standard sinusoidal positional encoding buffer used
    /// by upstream's PositionalEncoding(max_len=20, d_model). The
    /// upstream adds `pe.unsqueeze(2)` which broadcasts over the L
    /// dimension -- one PE per part, replicated across the part's
    /// L=25 tokens.
    /// </summary>
    private float[] BuildPositionalEncoding(int maxLen, int dModel)
    {
        var pe = new float[maxLen * dModel];
        for (int pos = 0; pos < maxLen; pos++)
        {
            for (int i = 0; i < dModel; i += 2)
            {
                double divTerm = Math.Exp(-Math.Log(10000.0) * i / dModel);
                pe[pos * dModel + i] = (float)Math.Sin(pos * divTerm);
                if (i + 1 < dModel)
                    pe[pos * dModel + i + 1] = (float)Math.Cos(pos * divTerm);
            }
        }
        return pe;
    }

    /// <summary>
    /// Result of a forward pass with per-layer intermediates exposed
    /// for parity validation. Layer outputs are the post-block
    /// hidden states at each of the 6 transformer layers.
    /// </summary>
    public sealed class ForwardResult
    {
        public float[][] LayerOutputs;     // [NumLayers] x [N*L, D]
        public float[] Residuals;          // final output [N, 7]
        public float[] PreLayer0Input;     // data_emb [N*L, D] right before transformer_layers[0]
    }

    /// <summary>
    /// Run one full forward pass. B is assumed 1 for inference.
    ///
    /// Inputs (channels-LAST, row-major):
    ///   x:           [N * 7]
    ///   latent:      [N * L * D_num]
    ///   xyz:         [N * L * 3]
    ///   part_valids: [N]
    ///   scale:       [N]              (per-part scalar; upstream's [N, 1])
    ///   ref_part:    [N] (0/1)        boolean as int per part
    ///   timestep:    scalar int
    /// </summary>
    public ForwardResult Forward(float[] x, float[] latent, float[] xyz,
                                  float[] partValids, float[] scale, int[] refPart,
                                  int timestep, int N, Weights w)
    {
        int L = _cfg.NumPoint;
        int Dn = _cfg.NumDim;
        int D = _cfg.EmbedDim;

        // ---- 1. Compute condition embeddings _gen_cond.
        // scale_emb: NerfEmbed each scale[n] -> [N, scaleOutDim]
        // Then broadcast to [N, L, scaleOutDim] (upstream uses unsqueeze + repeat).
        var scaleEmb = _scaleEmb.Embed(scale, N);           // [N, scaleOutDim]
        // xyz_pos_emb: NerfEmbed xyz [N*L, 3] -> [N*L, posOutDim]
        var xyzPosEmb = _posEmb.Embed(xyz, N * L);          // [N*L, posOutDim]
        // Concat latent [N*L, Dn] || xyzPosEmb [N*L, posOutDim] || scaleEmb broadcast [N*L, scaleOutDim].
        int posOutDim = _posEmb.OutDim;
        int scaleOutDim = _scaleEmb.OutDim;
        int catDim = Dn + posOutDim + scaleOutDim;
        var concatLatent = new float[N * L * catDim];
        for (int n = 0; n < N; n++)
        {
            for (int l = 0; l < L; l++)
            {
                int rowIdx = n * L + l;
                int dstOff = rowIdx * catDim;
                // copy latent
                Buffer.BlockCopy(latent, rowIdx * Dn * sizeof(float),
                                 concatLatent, dstOff * sizeof(float), Dn * sizeof(float));
                // copy xyz pos emb
                Buffer.BlockCopy(xyzPosEmb, rowIdx * posOutDim * sizeof(float),
                                 concatLatent, (dstOff + Dn) * sizeof(float), posOutDim * sizeof(float));
                // broadcast scale emb (same for all L of part n)
                Buffer.BlockCopy(scaleEmb, n * scaleOutDim * sizeof(float),
                                 concatLatent, (dstOff + Dn + posOutDim) * sizeof(float),
                                 scaleOutDim * sizeof(float));
            }
        }
        // shape_emb = ShapeEmbW @ concatLatent + b -> [N*L, D]
        var shapeEmb = new float[N * L * D];
        var shapeWT = Transpose(w.ShapeEmbW, D, catDim);    // [catDim, D]
        Matmul.MatMul(concatLatent, shapeWT, shapeEmb, N * L, catDim, D);
        if (w.ShapeEmbB != null) Matmul.AddBias(shapeEmb, w.ShapeEmbB, N * L, D);

        // param_emb: NerfEmbed x [N, 7] -> [N, paramOutDim], then ParamFcW + b -> [N, D]
        var paramEmb = _paramEmb.Embed(x, N);
        int paramOutDim = _paramEmb.OutDim;
        var xEmb = new float[N * D];
        var paramWT = Transpose(w.ParamFcW, D, paramOutDim);
        Matmul.MatMul(paramEmb, paramWT, xEmb, N, paramOutDim, D);
        if (w.ParamFcB != null) Matmul.AddBias(xEmb, w.ParamFcB, N, D);

        // ---- 2. Add ref_part_emb. RefPartEmbWeight is [2, D].
        // x_emb[n] += ref_part_emb_weight[refPart[n]]
        for (int n = 0; n < N; n++)
        {
            int idx = (refPart != null && refPart[n] != 0) ? 1 : 0;
            for (int c = 0; c < D; c++)
                xEmb[n * D + c] += w.RefPartEmbWeight[idx * D + c];
        }

        // ---- 3. Reshape x_emb [N, D] -> [N, L, D] by broadcast (per upstream).
        //   data_emb[B=1, N*L, D]
        var dataEmb = new float[N * L * D];
        for (int n = 0; n < N; n++)
        {
            for (int l = 0; l < L; l++)
                Buffer.BlockCopy(xEmb, n * D * sizeof(float),
                                 dataEmb, (n * L + l) * D * sizeof(float),
                                 D * sizeof(float));
        }
        // ---- 4. data_emb += shape_emb (already shaped [N*L, D]).
        for (int i = 0; i < N * L * D; i++) dataEmb[i] += shapeEmb[i];

        // ---- 5. Add positional encoding: data_emb[B, N, L, D] + pe[B, N, 1, D].
        // We add pe[n, :] to every token (n, l, :) for l in [0..L).
        var pe = BuildPositionalEncoding(_cfg.MaxLen, D);
        for (int n = 0; n < N; n++)
        {
            for (int l = 0; l < L; l++)
            {
                int dstOff = (n * L + l) * D;
                for (int c = 0; c < D; c++)
                    dataEmb[dstOff + c] += pe[n * D + c];
            }
        }

        // ---- 6. Build attention masks per upstream _gen_mask.
        //   self_mask: block-diagonal [N*L, N*L]. Token (n, l) attends
        //              only to tokens (n, *). Used for INTRA-fragment
        //              self-attention.
        //   gen_mask:  outer product of part_valids (broadcast to L)
        //              with itself: [N*L, N*L]. Used for INTER-fragment
        //              global attention.
        int M = N * L;
        var selfMask = new float[M * M];
        var genMask  = new float[M * M];
        // Flattened per-token validity: vt[n*L + l] = part_valids[n].
        var vt = new float[M];
        for (int n = 0; n < N; n++)
            for (int l = 0; l < L; l++) vt[n * L + l] = partValids[n];
        for (int n = 0; n < N; n++)
        {
            for (int li = 0; li < L; li++)
            {
                int i = n * L + li;
                int iRow = i * M;
                for (int nb = 0; nb < N; nb++)
                {
                    for (int lj = 0; lj < L; lj++)
                    {
                        int j = nb * L + lj;
                        selfMask[iRow + j] = (n == nb) ? 1f : 0f;
                        genMask[iRow + j]  = vt[i] * vt[j];
                    }
                }
            }
        }

        // ---- 7. Run through 6 transformer layers, capturing each output.
        var result = new ForwardResult { LayerOutputs = new float[_cfg.NumLayers][] };
        // Snapshot the pre-layer0 input so a parity test can isolate
        // whether the divergence is in the pre-layer compute (NeRF +
        // shape_emb + param_fc + ref_part_emb + PE) or inside the
        // transformer layers themselves.
        result.PreLayer0Input = new float[N * L * D];
        Buffer.BlockCopy(dataEmb, 0, result.PreLayer0Input, 0, N * L * D * sizeof(float));

        for (int lyr = 0; lyr < _cfg.NumLayers; lyr++)
        {
            _layers[lyr].Forward(dataEmb, M, timestep, w.Layers[lyr],
                                  selfMask: selfMask, genMask: genMask);
            // Snapshot for parity testing.
            result.LayerOutputs[lyr] = new float[N * L * D];
            Buffer.BlockCopy(dataEmb, 0, result.LayerOutputs[lyr], 0,
                             N * L * D * sizeof(float));
        }

        // ---- 7. Average pool over L axis: [N, L, D] -> [N, D].
        var pooled = new float[N * D];
        float invL = 1.0f / L;
        for (int n = 0; n < N; n++)
        {
            for (int l = 0; l < L; l++)
            {
                int srcOff = (n * L + l) * D;
                for (int c = 0; c < D; c++)
                    pooled[n * D + c] += dataEmb[srcOff + c];
            }
            for (int c = 0; c < D; c++) pooled[n * D + c] *= invL;
        }

        // ---- 8. mlp_out_trans + mlp_out_rot, both 3-layer with SiLU between.
        var trans = RunSiluMlp(pooled, N, D, w.TransL1W, w.TransL1B, D,
                                                w.TransL2W, w.TransL2B, D / 2,
                                                w.TransL3W, w.TransL3B, 3);
        var rots  = RunSiluMlp(pooled, N, D, w.RotL1W,   w.RotL1B,   D,
                                                w.RotL2W,   w.RotL2B,   D / 2,
                                                w.RotL3W,   w.RotL3B,   4);

        // ---- 9. Concat trans (3) || rots (4) -> [N, 7].
        var residuals = new float[N * 7];
        for (int n = 0; n < N; n++)
        {
            Buffer.BlockCopy(trans, n * 3 * sizeof(float),
                             residuals, n * 7 * sizeof(float), 3 * sizeof(float));
            Buffer.BlockCopy(rots, n * 4 * sizeof(float),
                             residuals, (n * 7 + 3) * sizeof(float), 4 * sizeof(float));
        }
        result.Residuals = residuals;
        return result;
    }

    // SiLU MLP: Linear -> SiLU -> Linear -> SiLU -> Linear (no final activation).
    private static float[] RunSiluMlp(float[] input, int M, int Din,
        float[] w1, float[] b1, int Dh1,
        float[] w2, float[] b2, int Dh2,
        float[] w3, float[] b3, int Dout)
    {
        var h1 = new float[M * Dh1];
        Matmul.MatMul(input, Transpose(w1, Dh1, Din), h1, M, Din, Dh1);
        if (b1 != null) Matmul.AddBias(h1, b1, M, Dh1);
        Silu(h1);
        var h2 = new float[M * Dh2];
        Matmul.MatMul(h1, Transpose(w2, Dh2, Dh1), h2, M, Dh1, Dh2);
        if (b2 != null) Matmul.AddBias(h2, b2, M, Dh2);
        Silu(h2);
        var h3 = new float[M * Dout];
        Matmul.MatMul(h2, Transpose(w3, Dout, Dh2), h3, M, Dh2, Dout);
        if (b3 != null) Matmul.AddBias(h3, b3, M, Dout);
        return h3;
    }

    private static void Silu(float[] x)
    {
        for (int i = 0; i < x.Length; i++) x[i] = x[i] / (1.0f + (float)Math.Exp(-x[i]));
    }

    /// <summary>Transpose a [Rows, Cols] row-major matrix to [Cols, Rows].</summary>
    private static float[] Transpose(float[] mat, int rows, int cols)
    {
        var t = new float[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                t[c * rows + r] = mat[r * cols + c];
        return t;
    }
}
