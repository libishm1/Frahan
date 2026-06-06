#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Primitives;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// End-to-end Kintsugi inference orchestrator. Mirrors upstream's
/// `auto_aggl.py::AutoAgglomerative.test_denoiser_only` step-for-step.
///
/// CRITICAL CORRECTNESS NOTES (vs naive single-encode-then-denoise)
///
/// 1. The encoder RE-RUNS inside the denoising loop. At each timestep,
///    the current noisy quaternions are applied to the point clouds
///    (via quaternion_apply) and the rotated clouds are fed through
///    the encoder. The denoiser sees encoder features of the CURRENTLY-
///    ESTIMATED pose -- not the static fragment.
///
/// 2. The reference (anchor) part is ANCHORED to its ground-truth pose
///    (identity in the Frahan use case) and never noised. Other parts
///    are predicted relative to it. After each scheduler step, the
///    ref part is reset to identity to prevent drift.
///
/// 3. Initial noise is torch.randn(shape) -- standard normal, NOT unit
///    quaternions. The model handles the quaternion normalisation
///    internally.
///
/// 4. Pose layout is [trans (3) | quat (4)] -- translation FIRST, then
///    quaternion (w, x, y, z). Upstream forward outputs in this order
///    and the diffusion runs in this layout.
///
/// COST PER ASSEMBLY
///   F fragments, T timesteps  -> F * T encoder forward passes
///                              + T denoiser forward passes
///                              + 1 verifier forward pass per pair
///   With encoder ~1.8 s and denoiser ~15 s per forward on GPU:
///     F=10, T=5  -> ~90 + 75 = 165 s
///     F=10, T=20 -> ~360 + 300 = 660 s
/// </summary>
public sealed class KintsugiPortInference
{
    private readonly WeightReader _reader;
    private readonly DenoiserTransformerPort _denoiserNet;
    private readonly DenoiserTransformerPort.Weights _denoiserW;
    private readonly DenoiserTransformerPort.Config _denoiserCfg;
    private readonly VerifierTransformerPort _verifierNet;
    private readonly VerifierTransformerPort.Weights _verifierW;
    private readonly VerifierTransformerPort.Config _verifierCfg;
    private readonly DiffusionScheduler _scheduler;
    private readonly bool _useTorchSharpDenoiser;
    private TorchSharpDenoiserPath _torchDenoiser;

    /// <summary>True iff the TorchSharp/libtorch denoiser is actually in
    /// use after the constructor's init attempt. When the caller requested
    /// TorchSharp but libtorch failed to initialise, this is false and the
    /// run silently used the manual C# port. Read this (not the input
    /// toggle) to report the REAL denoiser path.</summary>
    public bool TorchSharpActive => _useTorchSharpDenoiser;

    /// <summary>Non-null iff TorchSharp was requested but init failed.
    /// Carries the exception type + message so the GH report can surface
    /// WHY it fell back to the manual port (e.g. missing LibTorchSharp.dll).</summary>
    public string TorchSharpInitError { get; private set; }

    /// <summary>Non-null iff a CUDA denoiser forward failed mid-run and we
    /// rebuilt on CPU. Reported so the user sees the GPU was tried but the
    /// real-sized GEMM failed (toy self-test passed).</summary>
    private string _forwardFallbackReason;

    /// <summary>
    /// Construct with optional TorchSharp-denoiser path. When
    /// useTorchSharpDenoiser=true, the denoiser forward uses libtorch
    /// kernels (paper-exact), eliminating the ~3-5% drift in the
    /// manual port. Encoder + verifier still use the manual port
    /// (encoder is parity-validated at 5e-6, verifier at 1e-4 -- both
    /// are paper-equivalent already).
    /// </summary>
    public KintsugiPortInference(WeightReader reader, int numInferenceSteps = 20,
                                  bool useTorchSharpDenoiser = false)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _denoiserCfg = new DenoiserTransformerPort.Config();
        _denoiserNet = new DenoiserTransformerPort(_denoiserCfg);
        _denoiserW = DenoiserWeightLoader.LoadWeights(reader, _denoiserCfg);
        _verifierCfg = new VerifierTransformerPort.Config();
        _verifierNet = new VerifierTransformerPort(_verifierCfg);
        _verifierW = VerifierWeightLoader.LoadWeights(reader, _verifierCfg);
        _scheduler = new DiffusionScheduler();
        _scheduler.SetTimesteps(numInferenceSteps);
        _useTorchSharpDenoiser = useTorchSharpDenoiser;
        if (useTorchSharpDenoiser)
        {
            try { _torchDenoiser = new TorchSharpDenoiserPath(reader); }
            catch (Exception e)
            {
                // Walk the full inner-exception chain. The interesting cause
                // (e.g. "DllNotFoundException: Unable to load cudnn_cnn64_9")
                // lives in InnerException -- a bare TypeInitializationException
                // message tells us nothing.
                var sb = new System.Text.StringBuilder();
                for (var ex = e; ex != null; ex = ex.InnerException)
                {
                    if (sb.Length > 0) sb.Append("  <<  ");
                    sb.Append($"{ex.GetType().Name}: {ex.Message}");
                }
                TorchSharpInitError = sb.ToString();
                Console.WriteLine($"WARN: TorchSharp denoiser init failed: {TorchSharpInitError}");
                Console.WriteLine("       Falling back to manual port denoiser.");
                _useTorchSharpDenoiser = false;
            }
        }
    }

    public static KintsugiPortInference LoadFrom(string kintsugiBinPath, int numInferenceSteps = 20)
    {
        return new KintsugiPortInference(new WeightReader(kintsugiBinPath), numInferenceSteps);
    }

    public sealed class AssemblyResult
    {
        /// <summary>Per-fragment final pose: layout is [trans(3) | quat(4)].</summary>
        public List<float[]> Poses;
        public List<float> VerifierScores;
        public string Report;
        public int InferenceSteps;
        /// <summary>True iff the libtorch denoiser actually ran. False = manual C# port.</summary>
        public bool UsedTorchSharp;
        /// <summary>Non-null iff TorchSharp was requested but fell back to manual port.</summary>
        public string TorchSharpInitError;
        /// <summary>Compute device the libtorch denoiser ran on ("cuda"/"cpu"), or null if manual port.</summary>
        public string DenoiserDevice;
        /// <summary>Non-null iff CUDA was available but failed its self-test, so it ran on CPU.</summary>
        public string DeviceFallbackReason;
    }

    /// <summary>
    /// Run the full assembly. Each fragment's point cloud is N=1000
    /// channels-LAST [N, 3] -- supply the normalised, pre-sampled
    /// surface points.
    /// </summary>
    /// <param name="pointClouds">[F][N*3] per-fragment surface samples, channels-LAST row-major.</param>
    /// <param name="N">Number of points per fragment (1000 = upstream default).</param>
    /// <param name="anchorIndex">Which fragment is the reference/anchor (0 by default).</param>
    /// <param name="seed">RNG seed for initial Gaussian noise.</param>
    /// <param name="progress">Optional callback fired after every diffusion step
    ///   with (currentStep, totalSteps, label). Thread-safe; caller marshals to UI thread.</param>
    /// <param name="cancel">Cancellation token. Inference checks at each diffusion step
    ///   and after each fragment encode; throws OperationCanceledException on cancel.</param>
    public AssemblyResult RunAssembly(
        float[][] pointClouds, int N, int anchorIndex = 0, int seed = 42,
        Action<int, int, string> progress = null,
        CancellationToken cancel = default)
    {
        int F = pointClouds.Length;
        int NMax = _denoiserCfg.MaxLen;          // 20
        int L = _denoiserCfg.NumPoint;           // 25
        int Dn = _denoiserCfg.NumDim;            // 64
        if (F > NMax) throw new ArgumentException($"Fragment count {F} > N_MAX {NMax}");

        // ---- Initialise noisy poses from standard normal. Layout [trans|quat].
        var rng = new Random(seed);
        var noisyPoses = new float[NMax * 7];
        for (int i = 0; i < NMax * 7; i++) noisyPoses[i] = NextGaussian(rng);

        // ---- Anchor: ref part has identity pose: trans=0, quat=(1,0,0,0).
        var refPose = new float[7] { 0f, 0f, 0f, 1f, 0f, 0f, 0f };
        Array.Copy(refPose, 0, noisyPoses, anchorIndex * 7, 7);

        var partValids = new float[NMax];
        var scale = new float[NMax];
        var refPart = new int[NMax];
        for (int f = 0; f < F; f++) { partValids[f] = 1f; scale[f] = 1f; }
        refPart[anchorIndex] = 1;

        // ---- Pre-allocate the per-step latent + xyz tensors.
        var latent = new float[NMax * L * Dn];
        var xyzKp  = new float[NMax * L * 3];

        // ---- Build the encoder configs once.
        var encConfigs = EncoderWeightLoader.BuildConfigs();
        var encWeights = EncoderWeightLoader.LoadSaWeights(_reader);
        var sa1 = new PointNetSetAbstractionPort(encConfigs[0]);
        var sa2 = new PointNetSetAbstractionPort(encConfigs[1]);
        var sa3 = new PointNetSetAbstractionPort(encConfigs[2]);

        // ---- Per-step: rotate clouds by current quat, encode, denoise, step.
        int steps = _scheduler.InferenceTimesteps?.Length ?? 0;
        progress?.Invoke(0, steps, "starting");
        for (int step = 0; step < steps; step++)
        {
            cancel.ThrowIfCancellationRequested();
            int t = _scheduler.InferenceTimesteps[step];
            progress?.Invoke(step, steps, $"step {step + 1}/{steps} (t={t}): encoding {F} fragments");
            // For each fragment: rotate its point cloud by the current
            // estimated quaternion, then run the encoder chain.
            for (int f = 0; f < F; f++)
            {
                cancel.ThrowIfCancellationRequested();
                int poseOff = f * 7;
                float qw = noisyPoses[poseOff + 3];
                float qx = noisyPoses[poseOff + 4];
                float qy = noisyPoses[poseOff + 5];
                float qz = noisyPoses[poseOff + 6];
                // Normalise quat defensively.
                float qn = (float)Math.Sqrt(qw * qw + qx * qx + qy * qy + qz * qz);
                if (qn < 1e-8f) { qw = 1; qx = qy = qz = 0; qn = 1; }
                qw /= qn; qx /= qn; qy /= qn; qz /= qn;
                // Apply quaternion to each point in the cloud.
                var rotatedCl = new float[N * 3];
                var src = pointClouds[f];
                for (int i = 0; i < N; i++)
                {
                    float px = src[i * 3 + 0];
                    float py = src[i * 3 + 1];
                    float pz = src[i * 3 + 2];
                    // Quaternion rotation via matrix form:
                    // R = quaternion_to_matrix(w, x, y, z)
                    float r00 = 1 - 2 * (qy * qy + qz * qz);
                    float r01 = 2 * (qx * qy - qz * qw);
                    float r02 = 2 * (qx * qz + qy * qw);
                    float r10 = 2 * (qx * qy + qz * qw);
                    float r11 = 1 - 2 * (qx * qx + qz * qz);
                    float r12 = 2 * (qy * qz - qx * qw);
                    float r20 = 2 * (qx * qz - qy * qw);
                    float r21 = 2 * (qy * qz + qx * qw);
                    float r22 = 1 - 2 * (qx * qx + qy * qy);
                    rotatedCl[i * 3 + 0] = r00 * px + r01 * py + r02 * pz;
                    rotatedCl[i * 3 + 1] = r10 * px + r11 * py + r12 * pz;
                    rotatedCl[i * 3 + 2] = r20 * px + r21 * py + r22 * pz;
                }
                // Channels-first [3, N] for the SA layers.
                var rotatedCf = new float[3 * N];
                for (int i = 0; i < N; i++)
                {
                    rotatedCf[0 * N + i] = rotatedCl[i * 3 + 0];
                    rotatedCf[1 * N + i] = rotatedCl[i * 3 + 1];
                    rotatedCf[2 * N + i] = rotatedCl[i * 3 + 2];
                }
                // Encode.
                var (sa1Xyz, sa1Pts) = sa1.Forward(rotatedCf, N, null, 0, encWeights[0]);
                var (sa2Xyz, sa2Pts) = sa2.Forward(sa1Xyz, encConfigs[0].Npoint, sa1Pts, 128, encWeights[1]);
                var (sa3Xyz, sa3Pts) = sa3.Forward(sa2Xyz, encConfigs[1].Npoint, sa2Pts, 256, encWeights[2]);
                // Apply conv6 then VQ-quantize (matches upstream
                // VQVAE.encode -- the denoiser was trained on z_q, not z_e).
                float[] feat64 = ApplyConv6AndVq(sa3Pts, L);
                // Tile into latent [NMax, L, 64] channels-LAST.
                int latentOff = f * L * Dn;
                Buffer.BlockCopy(feat64, 0, latent, latentOff * sizeof(float), L * Dn * sizeof(float));
                // xyzKp [NMax, L, 3] channels-LAST from sa3Xyz [3, L] channels-first.
                int xyzOff = f * L * 3;
                for (int k = 0; k < L; k++)
                {
                    xyzKp[xyzOff + k * 3 + 0] = sa3Xyz[0 * L + k];
                    xyzKp[xyzOff + k * 3 + 1] = sa3Xyz[1 * L + k];
                    xyzKp[xyzOff + k * 3 + 2] = sa3Xyz[2 * L + k];
                }
            }

            progress?.Invoke(step, steps, $"step {step + 1}/{steps}: denoising " +
                (_useTorchSharpDenoiser ? "(TorchSharp / libtorch)" : "(manual port)"));
            // ---- Denoise.
            float[] residuals;
            if (_useTorchSharpDenoiser && _torchDenoiser != null)
            {
                try
                {
                    residuals = _torchDenoiser.Forward(
                        noisyPoses, latent, xyzKp, partValids, scale, refPart,
                        timestep: t, N: NMax);
                }
                catch (Exception ex) when (_torchDenoiser.Device == "cuda")
                {
                    // CUDA forward failed mid-run (e.g. CUBLAS_STATUS_EXECUTION_FAILED
                    // on a real-sized GEMM that the constructor's toy self-test
                    // didn't trigger -- cuBLAS failures are shape/config dependent).
                    // Rebuild the denoiser on CPU and retry THIS step; the encoder
                    // for this step already ran, so no recompute there. All later
                    // steps use the CPU denoiser. Still paper-exact, just slower.
                    string m = ex.Message;
                    if (m != null && m.Length > 240) m = m.Substring(0, 240) + "...";
                    _forwardFallbackReason = $"CUDA forward failed ({ex.GetType().Name}: {m}); switched to CPU mid-run.";
                    try { _torchDenoiser.Dispose(); } catch { }
                    _torchDenoiser = new TorchSharpDenoiserPath(_reader, forceCpu: true);
                    residuals = _torchDenoiser.Forward(
                        noisyPoses, latent, xyzKp, partValids, scale, refPart,
                        timestep: t, N: NMax);
                }
            }
            else
            {
                var manualResult = _denoiserNet.Forward(
                    noisyPoses, latent, xyzKp, partValids, scale, refPart,
                    timestep: t, N: NMax, w: _denoiserW);
                residuals = manualResult.Residuals;
            }

            // Scheduler step: predict x_{t-1} from current x_t and predicted noise.
            var newPoses = _scheduler.Step(residuals, noisyPoses, t);
            // Anchor: reset ref part to identity pose every step.
            Array.Copy(refPose, 0, newPoses, anchorIndex * 7, 7);
            // Re-normalise quaternions for every part.
            for (int f = 0; f < F; f++)
            {
                if (f == anchorIndex) continue;
                int qOff = f * 7 + 3;
                float w = newPoses[qOff + 0];
                float x = newPoses[qOff + 1];
                float y = newPoses[qOff + 2];
                float z = newPoses[qOff + 3];
                float n = (float)Math.Sqrt(w * w + x * x + y * y + z * z);
                if (n > 1e-8f)
                {
                    newPoses[qOff + 0] = w / n;
                    newPoses[qOff + 1] = x / n;
                    newPoses[qOff + 2] = y / n;
                    newPoses[qOff + 3] = z / n;
                }
            }
            noisyPoses = newPoses;
        }

        // ---- Verifier on all (i, j) pairs.
        int numEdges = F * (F - 1) / 2;
        var edgeFeatures = new float[numEdges * 7];
        var edgeIndices = new int[numEdges * 2];
        var edgeValid = new float[numEdges];
        int e = 0;
        for (int i = 0; i < F; i++)
        {
            for (int j = i + 1; j < F; j++)
            {
                // Pass fragment j's final pose [trans|quat] as the edge feature.
                Buffer.BlockCopy(noisyPoses, j * 7 * sizeof(float),
                                 edgeFeatures, e * 7 * sizeof(float), 7 * sizeof(float));
                edgeIndices[e * 2 + 0] = i;
                edgeIndices[e * 2 + 1] = j;
                edgeValid[e] = 1f;
                e++;
            }
        }
        var verifierLogits = _verifierNet.Forward(edgeFeatures, edgeIndices, edgeValid,
            numEdges, _verifierW);
        var verifierScores = new List<float>(numEdges);
        foreach (var lg in verifierLogits)
            verifierScores.Add(1.0f / (1.0f + (float)Math.Exp(-lg)));

        // ---- Build per-fragment pose result list. Each entry is the
        // 7-D [trans | quat] vector for that fragment.
        var poses = new List<float[]>(F);
        for (int f = 0; f < F; f++)
        {
            var p = new float[7];
            Array.Copy(noisyPoses, f * 7, p, 0, 7);
            poses.Add(p);
        }
        return new AssemblyResult
        {
            Poses = poses,
            VerifierScores = verifierScores,
            InferenceSteps = steps,
            UsedTorchSharp = _useTorchSharpDenoiser,
            TorchSharpInitError = TorchSharpInitError,
            DenoiserDevice = _useTorchSharpDenoiser ? (_torchDenoiser?.Device ?? "cpu") : null,
            DeviceFallbackReason = _forwardFallbackReason ?? _torchDenoiser?.DeviceFallbackReason,
            Report = $"Mode=Port end-to-end: {F} fragments, {steps} diffusion steps, " +
                     $"{numEdges} pairwise verifications.",
        };
    }

    /// <summary>
    /// Apply pn2.conv6 to compress SA3 features [512, L=25] to [64, L=25],
    /// then VQ-quantize each 16-D chunk against the 1024-entry codebook.
    /// This matches upstream VQVAE.encode() which produces z_q, NOT raw
    /// z_e. The denoiser was trained on quantized inputs.
    /// Returns CHANNELS-LAST [L, 64] for direct use as latent input.
    /// </summary>
    private float[] ApplyConv6AndVq(float[] sa3Pts, int L)
    {
        var w = _reader.GetFloat32("ae.pn2.conv6.weight");      // [64, 512]
        float[] b = null;
        foreach (var n in _reader.Names) if (n == "ae.pn2.conv6.bias") { b = _reader.GetFloat32(n); break; }
        // sa3Pts is [512, L] channels-first. Transpose to [L, 512].
        var sa3PtsCl = new float[L * 512];
        for (int k = 0; k < L; k++)
            for (int c = 0; c < 512; c++)
                sa3PtsCl[k * 512 + c] = sa3Pts[c * L + k];
        // Transpose w [64, 512] -> [512, 64].
        var wT = new float[512 * 64];
        for (int o = 0; o < 64; o++)
            for (int k = 0; k < 512; k++)
                wT[k * 64 + o] = w[o * 512 + k];
        var feat = new float[L * 64];
        Matmul.MatMul(sa3PtsCl, wT, feat, L, 512, 64);
        if (b != null) Matmul.AddBias(feat, b, L, 64);

        // ---- VQ QUANTIZATION (critical, was missing previously).
        // Upstream pipeline: reshape [B=1, L, 64] -> [B*L*4, 16], quantize
        // each 16-D row to its nearest of 1024 codebook entries, reshape
        // back. The codebook is `ae.vector_quantization.embedding.weight`
        // of shape [1024, 16].
        EnsureVqLoaded();
        // Reshape feat as L*4 rows of 16 cols (already in channels-LAST so
        // index math works directly: each L-row of 64 channels is 4 chunks
        // of 16 consecutive channels).
        int chunks = L * 4;
        _vq.Quantise(feat, chunks);
        return feat;
    }

    private VqVae _vq;
    private void EnsureVqLoaded()
    {
        if (_vq != null) return;
        var codebook = _reader.GetFloat32("ae.vector_quantization.embedding.weight");
        _vq = new VqVae(codebook, 1024, 16);
    }

    /// <summary>Marsaglia polar method for one standard normal sample.</summary>
    private static float NextGaussian(Random rng)
    {
        // Box-Muller with cached spare.
        double u1, u2;
        do
        {
            u1 = 1.0 - rng.NextDouble();
            u2 = 1.0 - rng.NextDouble();
        } while (u1 <= 1e-12);
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
