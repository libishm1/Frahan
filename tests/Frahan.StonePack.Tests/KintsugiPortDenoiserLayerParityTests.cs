#nullable disable
using System;
using System.IO;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// Layer-by-layer L_inf parity between the C# DenoiserTransformerPort and the
// upstream PyTorch DenoiserTransformer, using REAL encoder output as the
// denoiser's latent input (via the parity.denoiser_v2.* tensors in
// parity_fixtures.bin).
//
// This is the bisection harness: report max|diff| per transformer layer to
// identify which layer first exceeds the parity threshold. The first
// drifting layer reveals which primitive (MyAdaLN, attention, GEGLU,
// LayerNorm) has the discrepancy that needs fixing.
// =============================================================================

static class KintsugiPortDenoiserLayerParityTests
{
    private static readonly string FixturePath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\parity_fixtures.bin";
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    private static WeightReader _fixtures;
    private static WeightReader _weights;

    private static bool Ready(out string reason)
    {
        if (!File.Exists(FixturePath))   { reason = $"fixture missing at {FixturePath}";   return false; }
        if (!File.Exists(KintsugiBinPath)) { reason = $"kintsugi.bin missing at {KintsugiBinPath}"; return false; }
        if (_fixtures == null) _fixtures = new WeightReader(FixturePath);
        if (_weights  == null) _weights  = new WeightReader(KintsugiBinPath);
        reason = null;
        return true;
    }

    private static bool V2Available(out string reason)
    {
        if (!Ready(out reason)) return false;
        foreach (var n in _fixtures.Names)
            if (n == "parity.denoiser_v2.input.noisy_poses")
            {
                reason = null;
                return true;
            }
        reason = "parity.denoiser_v2.* tensors not in fixture -- re-run export_parity_fixtures.py";
        return false;
    }

    public static void DenoiserV2_AllLayersLInfDeviationReport()
    {
        if (!V2Available(out var skip)) { Console.WriteLine($"        info: {skip}"); return; }

        // Load v2 inputs.
        var noisyPoses = _fixtures.GetFloat32("parity.denoiser_v2.input.noisy_poses");  // [N_MAX, 7]
        var latent     = _fixtures.GetFloat32("parity.denoiser_v2.input.latent");        // [N_MAX, L, D]
        var xyz        = _fixtures.GetFloat32("parity.denoiser_v2.input.xyz");           // [N_MAX, L, 3]
        var partValids = _fixtures.GetFloat32("parity.denoiser_v2.input.part_valids");   // [N_MAX]
        var scale      = _fixtures.GetFloat32("parity.denoiser_v2.input.scale");         // [N_MAX, 1]
        var refPartF   = _fixtures.GetFloat32("parity.denoiser_v2.input.ref_part");      // [N_MAX]
        var tFloat     = _fixtures.GetFloat32("parity.denoiser_v2.input.timestep")[0];

        int N_MAX = _fixtures.GetShape("parity.denoiser_v2.input.noisy_poses")[0];
        int L = _fixtures.GetShape("parity.denoiser_v2.input.latent")[1];
        int D_latent = _fixtures.GetShape("parity.denoiser_v2.input.latent")[2];
        int timestep = (int)tFloat;

        // Reshape latent [N_MAX*L*D] is what C# expects (channels-last per part).
        // The captured tensor already has this exact layout for B=1.
        // Convert ref_part float[N_MAX] -> int[N_MAX].
        var refPart = new int[N_MAX];
        for (int i = 0; i < N_MAX; i++) refPart[i] = refPartF[i] > 0.5f ? 1 : 0;
        // scale shape is [N_MAX, 1] flat == [N_MAX] for our port API.
        var scaleFlat = new float[N_MAX];
        for (int i = 0; i < N_MAX; i++) scaleFlat[i] = scale[i];

        // Run the C# denoiser forward with these exact inputs.
        var cfg = new DenoiserTransformerPort.Config();
        var w = DenoiserWeightLoader.LoadWeights(_weights, cfg);
        var port = new DenoiserTransformerPort(cfg);
        var result = port.Forward(noisyPoses, latent, xyz, partValids, scaleFlat, refPart,
            timestep: timestep, N: N_MAX, w: w);

        // Pre-layer-0 input comparison: this isolates the pre-layer
        // compute (NeRF + shape_emb + param_fc + PE + ref_part_emb).
        // If max|diff| here is small but layer outputs differ, drift
        // is INSIDE the transformer layers. If max|diff| is large,
        // the pre-layer compute itself is broken.
        if (HasTensor("parity.denoiser_v2.pre_layer0_input"))
        {
            var refIn = _fixtures.GetFloat32("parity.denoiser_v2.pre_layer0_input");
            var portIn = result.PreLayer0Input;
            if (refIn.Length == portIn.Length)
            {
                double maxDiff = 0;
                double refMaxAbs = 0, portMaxAbs = 0;
                for (int k = 0; k < refIn.Length; k++)
                {
                    double d = Math.Abs(refIn[k] - portIn[k]);
                    if (d > maxDiff) maxDiff = d;
                    if (Math.Abs(refIn[k]) > refMaxAbs) refMaxAbs = Math.Abs(refIn[k]);
                    if (Math.Abs(portIn[k]) > portMaxAbs) portMaxAbs = Math.Abs(portIn[k]);
                }
                Console.WriteLine(
                    $"        denoiser_v2 PRE-LAYER0 input: max|diff|={maxDiff:G4} " +
                    $"ref|x|_max={refMaxAbs:G4} port|x|_max={portMaxAbs:G4}");
            }
            else
            {
                Console.WriteLine(
                    $"        PRE-LAYER0 size mismatch: ref={refIn.Length}, port={portIn.Length}");
            }
        }
        // Compare each layer.
        Console.WriteLine($"        denoiser_v2 layer-by-layer L_inf parity (t={timestep}):");
        for (int i = 0; i < 6; i++)
        {
            string name = $"parity.denoiser_v2.layer{i}_output";
            bool present = false;
            foreach (var n in _fixtures.Names) if (n == name) { present = true; break; }
            if (!present)
            { Console.WriteLine($"          layer{i}: MISSING reference (skipping)"); continue; }
            var refOut = _fixtures.GetFloat32(name);
            var portOut = result.LayerOutputs[i];
            if (refOut.Length != portOut.Length)
            {
                Console.WriteLine($"          layer{i}: SHAPE MISMATCH ref={refOut.Length}, port={portOut.Length}");
                continue;
            }
            double maxDiff = 0;
            int maxIdx = -1;
            double refMaxAbs = 0;
            double portMaxAbs = 0;
            double sumSqDiff = 0;
            for (int k = 0; k < refOut.Length; k++)
            {
                double d = Math.Abs(refOut[k] - portOut[k]);
                if (d > maxDiff) { maxDiff = d; maxIdx = k; }
                if (Math.Abs(refOut[k]) > refMaxAbs) refMaxAbs = Math.Abs(refOut[k]);
                if (Math.Abs(portOut[k]) > portMaxAbs) portMaxAbs = Math.Abs(portOut[k]);
                sumSqDiff += d * d;
            }
            double rmsd = Math.Sqrt(sumSqDiff / refOut.Length);
            Console.WriteLine(
                $"          layer{i}: max|diff|={maxDiff:G4} @ idx {maxIdx}  " +
                $"rmsd={rmsd:G4}  ref|x|_max={refMaxAbs:G4}  port|x|_max={portMaxAbs:G4}");
        }
        // Final residual.
        if (HasTensor("parity.denoiser_v2.final_residual"))
        {
            var refOut = _fixtures.GetFloat32("parity.denoiser_v2.final_residual");
            var portOut = result.Residuals;
            double maxDiff = 0;
            for (int k = 0; k < Math.Min(refOut.Length, portOut.Length); k++)
            {
                double d = Math.Abs(refOut[k] - portOut[k]);
                if (d > maxDiff) maxDiff = d;
            }
            Console.WriteLine($"          final_residual: max|diff|={maxDiff:G4}");
        }
    }

    private static bool HasTensor(string name)
    {
        foreach (var n in _fixtures.Names) if (n == name) return true;
        return false;
    }
}
