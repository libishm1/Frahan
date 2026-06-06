#nullable disable
using System;
using System.IO;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Primitives;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// Sub-block parity within transformer layer 0 of the denoiser.
//
// The pre-layer-0 input already matches PyTorch to ~7.6e-6 (bit-equivalent).
// The drift therefore lives INSIDE one EncoderLayer. This test replays
// each sub-step manually using the loaded layer-0 weights, comparing
// against the captured intra-layer references:
//
//   L0_pre              -- input  (== pre_layer0_input)
//   L0_after_norm1      -- after MyAdaLayerNorm
//   L0_after_self_attn  -- after self_attn
//   L0_after_resid1     -- after first residual add
//   L0_after_norm2      -- after MyAdaLayerNorm
//   L0_after_global_attn-- after global_attn
//   L0_after_resid2     -- after second residual add
//   L0_after_norm3      -- after regular LayerNorm
//   L0_after_ff         -- after GEGLU FFN + output linear
//
// First step with max|diff| > 1e-2 (or whatever) reveals the broken
// primitive.
// =============================================================================

static class KintsugiPortDenoiserSubBlockParityTests
{
    private static readonly string FixturePath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\parity_fixtures.bin";
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    public static void DenoiserLayer0_SubBlockLInf()
    {
        if (!File.Exists(FixturePath) || !File.Exists(KintsugiBinPath))
        { Console.WriteLine("        info: fixtures missing -- skip"); return; }
        var fx = new WeightReader(FixturePath);
        var wr = new WeightReader(KintsugiBinPath);
        if (!HasTensor(fx, "parity.denoiser_v2.intra_L0_pre"))
        { Console.WriteLine("        info: intra_L0_* captures absent -- re-run export"); return; }

        // Load layer-0 weights via the same loader the port uses.
        var cfg = new DenoiserTransformerPort.Config();
        var allW = DenoiserWeightLoader.LoadWeights(wr, cfg);
        var l0 = allW.Layers[0];
        int D = cfg.EmbedDim;
        int N_MAX = 20, L = cfg.NumPoint;
        int M = N_MAX * L;

        // Load input. Shape per capture: [1, M=500, D=512].
        var h = (float[])fx.GetFloat32("parity.denoiser_v2.intra_L0_pre").Clone();
        int hLen = h.Length;
        // Sanity vs the pre_layer0 capture.
        Console.WriteLine($"        L0 input length={hLen}, expected={M * D}");

        // Build masks identical to the port's.
        var partValids = fx.GetFloat32("parity.denoiser_v2.input.part_valids");
        var refPartF = fx.GetFloat32("parity.denoiser_v2.input.ref_part");
        var refPart = new int[N_MAX];
        for (int i = 0; i < N_MAX; i++) refPart[i] = refPartF[i] > 0.5f ? 1 : 0;
        var vt = new float[M];
        for (int n = 0; n < N_MAX; n++)
            for (int li = 0; li < L; li++) vt[n * L + li] = partValids[n];
        var selfMask = new float[M * M];
        var genMask = new float[M * M];
        for (int n = 0; n < N_MAX; n++)
        {
            for (int li = 0; li < L; li++)
            {
                int i = n * L + li;
                int iRow = i * M;
                for (int nb = 0; nb < N_MAX; nb++)
                {
                    for (int lj = 0; lj < L; lj++)
                    {
                        int j = nb * L + lj;
                        selfMask[iRow + j] = (n == nb) ? 1f : 0f;
                        genMask[iRow + j] = vt[i] * vt[j];
                    }
                }
            }
        }

        int timestep = (int)fx.GetFloat32("parity.denoiser_v2.input.timestep")[0];
        int numEmbedsAdaNorm = cfg.NumEmbedsAdaNorm;

        // Walk each sub-step, comparing to captured reference.
        void Compare(string label, float[] actual, string refName)
        {
            if (!HasTensor(fx, refName))
            { Console.WriteLine($"          {label}: REF MISSING ({refName})"); return; }
            var refOut = fx.GetFloat32(refName);
            int len = Math.Min(actual.Length, refOut.Length);
            double maxDiff = 0, refMaxAbs = 0, actMaxAbs = 0;
            for (int k = 0; k < len; k++)
            {
                double d = Math.Abs(actual[k] - refOut[k]);
                if (d > maxDiff) maxDiff = d;
                if (Math.Abs(refOut[k]) > refMaxAbs) refMaxAbs = Math.Abs(refOut[k]);
                if (Math.Abs(actual[k]) > actMaxAbs) actMaxAbs = Math.Abs(actual[k]);
            }
            Console.WriteLine($"          {label}: max|diff|={maxDiff:G4}  " +
                              $"ref|x|_max={refMaxAbs:G4}  port|x|_max={actMaxAbs:G4}");
        }

        Console.WriteLine($"        Layer 0 sub-block L_inf (t={timestep}):");
        Compare("L0_pre              ", h, "parity.denoiser_v2.intra_L0_pre");

        // Step 1: norm1
        var residual = new float[hLen];
        Buffer.BlockCopy(h, 0, residual, 0, hLen * sizeof(float));
        MyAdaLayerNorm.Apply(h, M, D, timestep,
            l0.N1EmbWeight, numEmbedsAdaNorm, l0.N1LinearW, l0.N1LinearB);
        Compare("after_norm1         ", h, "parity.denoiser_v2.intra_L0_after_norm1");

        // Step 2: self_attn -- try mask AND no-mask, report both.
        var hNoMask = (float[])h.Clone();
        var mhaNoMask = new MultiHeadAttention(D, cfg.NumHeads);
        mhaNoMask.Apply(hNoMask,
            l0.SelfWq, l0.SelfBq, l0.SelfWk, l0.SelfBk,
            l0.SelfWv, l0.SelfBv, l0.SelfWo, l0.SelfBo, M, attendMask: null);
        Compare("after_self_attn NO_MSK", hNoMask, "parity.denoiser_v2.intra_L0_after_self_attn");
        var mha = new MultiHeadAttention(D, cfg.NumHeads);
        mha.Apply(h,
            l0.SelfWq, l0.SelfBq, l0.SelfWk, l0.SelfBk,
            l0.SelfWv, l0.SelfBv, l0.SelfWo, l0.SelfBo, M, selfMask);
        Compare("after_self_attn MASK ", h, "parity.denoiser_v2.intra_L0_after_self_attn");

        // Step 3: residual1
        for (int i = 0; i < hLen; i++) h[i] += residual[i];
        Compare("after_resid1        ", h, "parity.denoiser_v2.intra_L0_after_resid1");

        // Step 4: norm2
        Buffer.BlockCopy(h, 0, residual, 0, hLen * sizeof(float));
        MyAdaLayerNorm.Apply(h, M, D, timestep,
            l0.N2EmbWeight, numEmbedsAdaNorm, l0.N2LinearW, l0.N2LinearB);
        Compare("after_norm2         ", h, "parity.denoiser_v2.intra_L0_after_norm2");

        // Step 5: global_attn
        var mha2 = new MultiHeadAttention(D, cfg.NumHeads);
        mha2.Apply(h,
            l0.GlobalWq, l0.GlobalBq, l0.GlobalWk, l0.GlobalBk,
            l0.GlobalWv, l0.GlobalBv, l0.GlobalWo, l0.GlobalBo, M, genMask);
        Compare("after_global_attn   ", h, "parity.denoiser_v2.intra_L0_after_global_attn");

        // Step 6: residual2
        for (int i = 0; i < hLen; i++) h[i] += residual[i];
        Compare("after_resid2        ", h, "parity.denoiser_v2.intra_L0_after_resid2");

        // Step 7: norm3 (regular LN with affine)
        Buffer.BlockCopy(h, 0, residual, 0, hLen * sizeof(float));
        LayerNorm.Apply(h, l0.N3Gamma, l0.N3Beta, M, D);
        Compare("after_norm3         ", h, "parity.denoiser_v2.intra_L0_after_norm3");

        // Step 8: GEGLU + linear (FF)
        int ffnDim = 4 * D;
        var hidden = Geglu.Apply(h, M, D, ffnDim, l0.FfGegluW, l0.FfGegluB);
        var wOutT = new float[ffnDim * D];
        for (int o = 0; o < D; o++)
            for (int k = 0; k < ffnDim; k++)
                wOutT[k * D + o] = l0.FfOutW[o * ffnDim + k];
        var ffOut = new float[M * D];
        Matmul.MatMul(hidden, wOutT, ffOut, M, ffnDim, D);
        if (l0.FfOutB != null) Matmul.AddBias(ffOut, l0.FfOutB, M, D);
        Compare("after_ff            ", ffOut, "parity.denoiser_v2.intra_L0_after_ff");
    }

    private static bool HasTensor(WeightReader r, string name)
    {
        foreach (var n in r.Names) if (n == name) return true;
        return false;
    }
}
