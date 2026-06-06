#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Kintsugi.Port.Weights;
using TorchSharp;
using static TorchSharp.torch;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// Paper-exact denoiser inference via TorchSharp / libtorch.
///
/// The manual C# port (DenoiserTransformerPort) has ~3-5% per-layer
/// drift after our parity tightening. That residual drift comes from
/// minor primitive-level differences (GeLU tanh-approx vs erf-exact,
/// softmax reduction order, etc.) that compound across 6 transformer
/// layers x T diffusion steps.
///
/// This path bypasses ALL manual primitives and runs the denoiser
/// using TorchSharp's `nn.functional` ops -- which are identical to
/// PyTorch's CUDA/CPU kernels. Result: paper-equivalent inference.
///
/// API mirrors DenoiserTransformerPort.Forward so the
/// KintsugiPortInference orchestrator can swap between them via a
/// flag without changing the surrounding pipeline.
///
/// Same FRKINTSU weight binary; same input shapes; same output shape.
/// Drop-in alternative.
/// </summary>
public sealed class TorchSharpDenoiserPath : IDisposable
{
    private readonly Dictionary<string, Tensor> _w = new Dictionary<string, Tensor>();
    private readonly int _D;          // 512
    private readonly int _H;          // 8
    private readonly int _L;          // 25
    private readonly int _NumDim;     // 64
    private readonly int _NumLayers;  // 6
    private readonly int _NumEmbedsAdaNorm;  // 6 * 512 = 3072
    private readonly int _Multires;   // 10
    private readonly int _OutCh;      // 7
    private readonly Device _device;  // CUDA if the cuda libtorch package is present, else CPU
    private bool _disposed;

    /// <summary>Compute device the denoiser runs on: "cuda" or "cpu".
    /// Reports "cuda" only when the CUDA libtorch package
    /// (TorchSharp-cuda-windows) is deployed AND a CUDA device is visible
    /// AND the GPU passed the construction-time matmul self-test.
    /// With the default TorchSharp-cpu package this is always "cpu".</summary>
    public string Device => _device.type == DeviceType.CUDA ? "cuda" : "cpu";

    /// <summary>Non-null iff CUDA was available but the GPU self-test failed,
    /// so the denoiser fell back to CPU. Carries the CUDA error (e.g.
    /// CUBLAS_STATUS_EXECUTION_FAILED) so the GH report can explain why a
    /// "cuda available" machine still ran on CPU.</summary>
    public string DeviceFallbackReason { get; private set; }

    public TorchSharpDenoiserPath(WeightReader reader,
                                   bool forceCpu = false,
                                   int embedDim = 512,
                                   int numHeads = 8,
                                   int numLayers = 6,
                                   int numPoint = 25,
                                   int numDim = 64,
                                   int multires = 10,
                                   int outChannels = 7)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        _D = embedDim; _H = numHeads; _NumLayers = numLayers;
        _L = numPoint; _NumDim = numDim;
        _Multires = multires; _OutCh = outChannels;
        _NumEmbedsAdaNorm = 6 * embedDim;
        // Device selection: use CUDA when the CUDA-enabled libtorch is
        // deployed and a GPU is visible. cuda.is_available() returns false
        // under the CPU-only package, so this safely stays CPU there.
        // forceCpu=true: caller is rebuilding us on CPU after a CUDA forward
        // failed mid-run (see KintsugiPortInference), so skip CUDA entirely.
        _device = (!forceCpu && cuda.is_available()) ? CUDA : CPU;
        // GPU self-test: some Windows + Turing + libtorch combos init the
        // CUDA context fine but throw CUBLAS_STATUS_EXECUTION_FAILED on the
        // first real GEMM (lazy cuBLAS init / WDDM display-GPU quirk; seen on
        // Quadro RTX 4000, 2026-05-24). Do a tiny matmul and force a sync; if
        // it throws, fall back to CPU (still TorchSharp / paper-exact) instead
        // of failing the whole inference. Record why for the report.
        if (_device.type == DeviceType.CUDA)
        {
            try
            {
                using var a = ones(new long[] { 8, 8 }).to(_device);
                using var b = ones(new long[] { 8, 8 }).to(_device);
                using var c = a.matmul(b);
                // .cpu() forces a device sync, surfacing any async CUDA error
                // here (CUDA errors are deferred to the next blocking call).
                var probe = c.cpu().data<float>().ToArray();
                if (probe.Length == 0 || probe[0] != 8f)
                    throw new Exception($"self-test matmul gave {(probe.Length > 0 ? probe[0].ToString() : "empty")}, expected 8");
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg != null && msg.Length > 240) msg = msg.Substring(0, 240) + "...";
                DeviceFallbackReason = $"{ex.GetType().Name}: {msg}";
                _device = CPU;
            }
        }
        // Load all denoiser.* tensors into libtorch, on the chosen device.
        foreach (var name in reader.Names)
        {
            if (!name.StartsWith("denoiser.")) continue;
            var raw = reader.GetFloat32(name);
            var shape = reader.GetShape(name);
            long[] longShape = new long[shape.Length];
            for (int i = 0; i < shape.Length; i++) longShape[i] = shape[i];
            _w[name] = tensor(raw, longShape, ScalarType.Float32).to(_device);
        }
    }

    /// <summary>Number of denoiser tensors successfully loaded.</summary>
    public int LoadedTensorCount => _w.Count;

    /// <summary>Diagnostic: list of loaded tensor names.</summary>
    public IEnumerable<string> LoadedNames => _w.Keys;

    /// <summary>
    /// Forward at the given diffusion timestep. Matches
    /// DenoiserTransformerPort.Forward signature byte-for-byte so the
    /// orchestrator code in KintsugiPortInference can swap between
    /// manual + TorchSharp paths via a flag.
    /// </summary>
    /// <param name="x">[N, 7] noisy [trans|quat] poses (channels-LAST).</param>
    /// <param name="latent">[N, L, D_num] encoder features (channels-LAST).</param>
    /// <param name="xyz">[N, L, 3] encoder keypoints.</param>
    /// <param name="partValids">[N] validity mask (1=valid).</param>
    /// <param name="scale">[N] per-part scale.</param>
    /// <param name="refPart">[N] anchor mask (1=anchor).</param>
    /// <param name="timestep">Diffusion timestep index.</param>
    /// <param name="N">Number of fragment slots (typically 20).</param>
    /// <returns>[N * 7] residuals (channels-LAST).</returns>
    public float[] Forward(float[] x, float[] latent, float[] xyz,
                            float[] partValids, float[] scale, int[] refPart,
                            int timestep, int N)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TorchSharpDenoiserPath));
        using var noGrad = no_grad();

        // ---- Build input tensors (all on the compute device).
        var xT      = tensor(x,           new long[] { 1, N, _OutCh },     ScalarType.Float32).to(_device);
        var latentT = tensor(latent,      new long[] { 1, N, _L, _NumDim }, ScalarType.Float32).to(_device);
        var xyzT    = tensor(xyz,         new long[] { 1, N, _L, 3 },       ScalarType.Float32).to(_device);
        var validsT = tensor(partValids,  new long[] { 1, N },              ScalarType.Float32).to(_device);
        var scaleF  = new float[N];
        for (int i = 0; i < N; i++) scaleF[i] = scale[i];
        var scaleT  = tensor(scaleF,      new long[] { 1, N, 1 },           ScalarType.Float32).to(_device);
        var refF    = new float[N];
        for (int i = 0; i < N; i++) refF[i] = refPart[i] > 0 ? 1f : 0f;
        var refT    = tensor(refF,        new long[] { 1, N },              ScalarType.Float32).to(_device).to(ScalarType.Bool);
        var tsT     = tensor(new long[] { timestep }, new long[] { 1 }, ScalarType.Int64).to(_device);

        // ---- _gen_cond: NeRF embed + shape_embedding + param_fc.
        var (xEmb, shapeEmb) = GenCond(xT, xyzT, latentT, scaleT, N);

        // ---- add_ref_part_emb: lookup ref_part_emb_weight + add.
        // Original code: rebuild x_emb [B, N, D], add per-part ref emb.
        var refPartEmb = _w["denoiser.ref_part_emb.weight"];  // [2, D]
        // xEmb shape [B*N, D]; reshape to [B, N, D].
        xEmb = xEmb.reshape(1, N, _D);
        // Default = row 0; anchor positions get row 1.
        var refIdx = refT.to(ScalarType.Int64);  // [B, N]
        var refEmbForN = refPartEmb[refIdx];  // [B, N, D]
        xEmb = xEmb + refEmbForN;
        xEmb = xEmb.reshape(N, _D);

        // ---- Broadcast x_emb [B, N, D] -> [B, N, L, D] then reshape to [B, N*L, D].
        var dataEmb = xEmb.reshape(1, N, 1, _D).expand(1, N, _L, _D).contiguous();
        // ---- + shape_emb [B*N*L, D] reshaped to [B, N, L, D]
        var shapeEmbT = shapeEmb.reshape(1, N, _L, _D);
        dataEmb = dataEmb + shapeEmbT;

        // ---- pos_encoding: add per-N position code.
        var pe = BuildPositionalEncoding(20, _D).to(_device);  // [max_len, D]
        // Broadcast pe[None, :N, None, :] to [B, N, L, D]
        var peSlice = pe.slice(0, 0, N, 1).reshape(1, N, 1, _D);
        dataEmb = dataEmb + peSlice;
        // Flatten to [B, N*L, D]
        dataEmb = dataEmb.reshape(1, N * _L, _D);

        // ---- Build masks: self_mask block-diag, gen_mask outer product.
        var selfMask = BuildSelfMask(N).to(_device).reshape(1, N * _L, N * _L);
        var genMask  = BuildGenMask(validsT, N).reshape(1, N * _L, N * _L);

        // ---- Run through 6 transformer layers.
        for (int lyr = 0; lyr < _NumLayers; lyr++)
        {
            dataEmb = EncoderLayer(dataEmb, lyr, tsT, selfMask, genMask);
        }

        // ---- _out: reshape to [B, N, L, D], mean over L, then MLP heads.
        dataEmb = dataEmb.reshape(1, N, _L, _D);
        var pooled = dataEmb.mean(new long[] { 2 }, keepdim: false);  // [B, N, D]
        var pooledFlat = pooled.reshape(N, _D);

        var trans = SiluMlp(pooledFlat, "denoiser.mlp_out_trans", _D, _D, _D / 2, 3);  // [N, 3]
        var rots  = SiluMlp(pooledFlat, "denoiser.mlp_out_rot",   _D, _D, _D / 2, 4);  // [N, 4]
        var outDec = cat(new[] { trans, rots }, dim: -1);  // [N, 7]

        // Copy back to a float[] (move to CPU first if we ran on CUDA).
        var result = outDec.cpu().data<float>().ToArray();
        return result;
    }

    private (Tensor xEmb, Tensor shapeEmb) GenCond(Tensor x, Tensor xyz, Tensor latent, Tensor scale, int N)
    {
        // Match upstream verbatim:
        //   x [B, N, 7]     -> [B*N, 7]
        //   xyz [B, N, L, 3]-> [B*N, L, 3]
        //   latent [B, N, L, D_num] -> [B*N, L, D_num]
        //   scale [B, N, 1] -> [B*N, 1]
        var xFlat      = x.reshape(N, _OutCh);
        var xyzFlat    = xyz.reshape(N, _L, 3);
        var latentFlat = latent.reshape(N, _L, _NumDim);
        var scaleFlat  = scale.reshape(N, 1);

        var scaleEmb = NerfEmbed(scaleFlat, 1, _Multires);   // [N, 21]
        scaleEmb = scaleEmb.unsqueeze(1).expand(N, _L, scaleEmb.shape[1]);  // [N, L, 21]
        var xyzPosEmb = NerfEmbed(xyzFlat.reshape(-1, 3), 3, _Multires);    // [N*L, 63]
        xyzPosEmb = xyzPosEmb.reshape(N, _L, -1);
        var concat = cat(new[] { latentFlat, xyzPosEmb, scaleEmb }, dim: -1);  // [N, L, num_dim+63+21]
        var shapeEmb = Linear(concat, "denoiser.shape_embedding");  // [N, L, D]

        var paramEmb = NerfEmbed(xFlat, 7, _Multires);  // [N, 147]
        var xEmb = Linear(paramEmb, "denoiser.param_fc");  // [N, D]
        return (xEmb, shapeEmb);
    }

    private Tensor NerfEmbed(Tensor input, int inputDims, int numFreqs)
    {
        // input is [..., inputDims]. Compute include_input + sin/cos at freq bands.
        var parts = new List<Tensor> { input };
        for (int f = 0; f < numFreqs; f++)
        {
            float freq = (float)Math.Pow(2.0, f);  // log_sampling: 1, 2, 4, ..., 2^(N-1)
            parts.Add(sin(input * freq));
            parts.Add(cos(input * freq));
        }
        return cat(parts.ToArray(), dim: -1);
    }

    private Tensor Linear(Tensor x, string baseName)
    {
        var w = _w[baseName + ".weight"];
        Tensor b = null;
        if (_w.ContainsKey(baseName + ".bias")) b = _w[baseName + ".bias"];
        return nn.functional.linear(x, w, b);
    }

    private Tensor SiluMlp(Tensor x, string baseName, int Din, int Dh1, int Dh2, int Dout)
    {
        // 3 Linears with SiLU between.
        // Upstream Sequential indices: 0 = Linear, 1 = SiLU, 2 = Linear, 3 = SiLU, 4 = Linear.
        var h = nn.functional.linear(x, _w[baseName + ".0.weight"],
                                     _w.ContainsKey(baseName + ".0.bias") ? _w[baseName + ".0.bias"] : null);
        h = nn.functional.silu(h);
        h = nn.functional.linear(h, _w[baseName + ".2.weight"],
                                 _w.ContainsKey(baseName + ".2.bias") ? _w[baseName + ".2.bias"] : null);
        h = nn.functional.silu(h);
        h = nn.functional.linear(h, _w[baseName + ".4.weight"],
                                 _w.ContainsKey(baseName + ".4.bias") ? _w[baseName + ".4.bias"] : null);
        return h;
    }

    private Tensor MyAdaLayerNorm(Tensor x, string baseName, Tensor timestep)
    {
        // x [B, M, D]; timestep [B] long.
        var embW = _w[baseName + ".emb.weight"];     // [num_embeds, D]
        var linW = _w[baseName + ".linear.weight"];  // [2*D, D]
        Tensor linB = null;
        if (_w.ContainsKey(baseName + ".linear.bias")) linB = _w[baseName + ".linear.bias"];
        // emb lookup
        var emb = embW[timestep];  // [B, D]
        emb = nn.functional.silu(emb);
        // linear
        var lin = nn.functional.linear(emb, linW, linB);  // [B, 2*D]
        var chunks = lin.chunk(2, dim: 1);
        var scale = chunks[0];  // [B, D]
        var shift = chunks[1];  // [B, D]
        // norm: layer_norm without learnable affine.
        var xNorm = nn.functional.layer_norm(x, new long[] { _D });
        // apply: x_norm * (1 + scale[:, None]) + shift[:, None]
        return xNorm * (1 + scale.unsqueeze(1)) + shift.unsqueeze(1);
    }

    private Tensor EncoderLayer(Tensor h, int layerIdx, Tensor timestep, Tensor selfMask, Tensor genMask)
    {
        string baseN = $"denoiser.transformer_layers.{layerIdx}";
        // 1. norm1 -> self_attn -> residual
        var normH = MyAdaLayerNorm(h, baseN + ".norm1", timestep);
        var attnOut = Attention(normH, baseN + ".self_attn", selfMask);
        h = h + attnOut;
        // 2. norm2 -> global_attn -> residual
        normH = MyAdaLayerNorm(h, baseN + ".norm2", timestep);
        var globOut = Attention(normH, baseN + ".global_attn", genMask);
        h = h + globOut;
        // 3. norm3 -> ff -> residual
        normH = nn.functional.layer_norm(h, new long[] { _D },
            _w[baseN + ".norm3.weight"], _w[baseN + ".norm3.bias"]);
        var ffOut = Geglu(normH, baseN);
        h = h + ffOut;
        return h;
    }

    private Tensor Attention(Tensor x, string baseN, Tensor attendMask)
    {
        // x [B, M, D]; W_q/k/v have shape [D, D] in PyTorch convention.
        var wq = _w[baseN + ".to_q.weight"];
        var wk = _w[baseN + ".to_k.weight"];
        var wv = _w[baseN + ".to_v.weight"];
        var wo = _w[baseN + ".to_out.0.weight"];
        Tensor bo = null;
        if (_w.ContainsKey(baseN + ".to_out.0.bias")) bo = _w[baseN + ".to_out.0.bias"];
        long B = x.shape[0], M = x.shape[1];
        var q = nn.functional.linear(x, wq);  // [B, M, D]
        var k = nn.functional.linear(x, wk);
        var v = nn.functional.linear(x, wv);
        long headDim = _D / _H;
        q = q.reshape(B, M, _H, headDim).transpose(1, 2);  // [B, H, M, headDim]
        k = k.reshape(B, M, _H, headDim).transpose(1, 2);
        v = v.reshape(B, M, _H, headDim).transpose(1, 2);
        double scale = 1.0 / Math.Sqrt(headDim);
        var scores = q.matmul(k.transpose(-2, -1)) * scale;  // [B, H, M, M]
        if (attendMask is not null)
        {
            // Match diffusers convention: scores += mask (broadcast over H).
            scores = scores + attendMask.unsqueeze(1);
        }
        var probs = nn.functional.softmax(scores, -1);
        var attn = probs.matmul(v);  // [B, H, M, headDim]
        attn = attn.transpose(1, 2).contiguous().reshape(B, M, _D);  // [B, M, D]
        attn = nn.functional.linear(attn, wo, bo);
        return attn;
    }

    private Tensor Geglu(Tensor x, string baseN)
    {
        // ff.net.0 = GEGLU(D, 4D) -> Linear(D, 8D) inside
        // ff.net.2 = Linear(4D, D)
        var wIn = _w[baseN + ".ff.net.0.proj.weight"];  // [8D, D]
        Tensor bIn = null;
        if (_w.ContainsKey(baseN + ".ff.net.0.proj.bias")) bIn = _w[baseN + ".ff.net.0.proj.bias"];
        var proj = nn.functional.linear(x, wIn, bIn);  // [..., 8D]
        var chunks = proj.chunk(2, dim: -1);
        var val = chunks[0];
        var gate = chunks[1];
        var hidden = val * nn.functional.gelu(gate);  // [..., 4D]
        var wOut = _w[baseN + ".ff.net.2.weight"];  // [D, 4D]
        Tensor bOut = null;
        if (_w.ContainsKey(baseN + ".ff.net.2.bias")) bOut = _w[baseN + ".ff.net.2.bias"];
        return nn.functional.linear(hidden, wOut, bOut);
    }

    private Tensor BuildPositionalEncoding(int maxLen, int dModel)
    {
        var positions = arange(maxLen, ScalarType.Float32).unsqueeze(1);  // [maxLen, 1]
        var divTerm = exp(arange(0, dModel, 2, ScalarType.Float32) * (-Math.Log(10000.0) / dModel));  // [dModel/2]
        var angles = positions * divTerm;  // [maxLen, dModel/2]
        var pe = zeros(maxLen, dModel);
        pe.index_put_(sin(angles), TensorIndex.Colon, TensorIndex.Slice(0, null, 2));
        pe.index_put_(cos(angles), TensorIndex.Colon, TensorIndex.Slice(1, null, 2));
        return pe;
    }

    private Tensor BuildSelfMask(int N)
    {
        // Block-diagonal [N*L, N*L]: 1 within a fragment's L tokens, 0 across.
        var block = ones(_L, _L);
        var blocks = new Tensor[N];
        for (int i = 0; i < N; i++) blocks[i] = block;
        return block_diag(blocks);
    }

    private Tensor BuildGenMask(Tensor partValids, int N)
    {
        // Outer product of [B, N*L] valids with itself.
        var vt = partValids.unsqueeze(-1).repeat(1, 1, _L).reshape(1, N * _L);
        return vt.unsqueeze(2) * vt.unsqueeze(1);  // [B, N*L, N*L]
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var t in _w.Values) try { t?.Dispose(); } catch { }
        _w.Clear();
        _disposed = true;
    }
}
