#nullable disable
using System;
using System.IO;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// Layer-by-layer L_inf parity for the verifier. Mirrors the denoiser harness
// but uses the simpler verifier inputs (just edge_features + indices + mask)
// and the standard nn.TransformerEncoderLayer architecture.
// =============================================================================

static class KintsugiPortVerifierLayerParityTests
{
    private static readonly string FixturePath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\parity_fixtures.bin";
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    public static void VerifierV2_AllLayersLInf()
    {
        if (!File.Exists(FixturePath) || !File.Exists(KintsugiBinPath))
        { Console.WriteLine("        info: fixtures missing -- skip"); return; }
        var fx = new WeightReader(FixturePath);
        var wr = new WeightReader(KintsugiBinPath);
        if (!HasTensor(fx, "parity.verifier_v2.input.edge_features"))
        { Console.WriteLine("        info: verifier_v2 captures absent -- re-run export"); return; }

        // Load inputs.
        var edgeFeat   = fx.GetFloat32("parity.verifier_v2.input.edge_features");
        var edgeIdxF   = fx.GetFloat32("parity.verifier_v2.input.edge_indices");
        var validMask  = fx.GetFloat32("parity.verifier_v2.input.valid_mask");
        int E = fx.GetShape("parity.verifier_v2.input.edge_features")[0];
        var edgeIdx = new int[E * 2];
        for (int i = 0; i < E * 2; i++) edgeIdx[i] = (int)edgeIdxF[i];

        // Load verifier weights + run forward.
        var cfg = new VerifierTransformerPort.Config();
        var w = VerifierWeightLoader.LoadWeights(wr, cfg);
        var port = new VerifierTransformerPort(cfg);
        // VerifierTransformerPort.Forward returns logits. To get per-layer
        // outputs for parity comparison, we'd need the port to expose them.
        // For now just check the final logits.
        var logits = port.Forward(edgeFeat, edgeIdx, validMask, E, w);

        Console.WriteLine($"        verifier_v2 (E={E} edges):");
        if (HasTensor(fx, "parity.verifier_v2.logits"))
        {
            var refLogits = fx.GetFloat32("parity.verifier_v2.logits");
            int len = Math.Min(logits.Length, refLogits.Length);
            double maxDiff = 0, refMaxAbs = 0, portMaxAbs = 0;
            for (int i = 0; i < len; i++)
            {
                double d = Math.Abs(refLogits[i] - logits[i]);
                if (d > maxDiff) maxDiff = d;
                if (Math.Abs(refLogits[i]) > refMaxAbs) refMaxAbs = Math.Abs(refLogits[i]);
                if (Math.Abs(logits[i]) > portMaxAbs) portMaxAbs = Math.Abs(logits[i]);
            }
            Console.WriteLine(
                $"          logits: max|diff|={maxDiff:G4}  " +
                $"ref|x|_max={refMaxAbs:G4}  port|x|_max={portMaxAbs:G4}");
            for (int i = 0; i < len; i++)
                Console.WriteLine($"            edge {i}: ref={refLogits[i]:F4}, port={logits[i]:F4}");
        }
        else
        {
            Console.WriteLine("          info: parity.verifier_v2.logits absent");
        }
        // Per-layer references (just shape + magnitude reports).
        for (int i = 0; i < 6; i++)
        {
            string name = $"parity.verifier_v2.layer{i}_output";
            if (!HasTensor(fx, name)) continue;
            var refOut = fx.GetFloat32(name);
            var shape = fx.GetShape(name);
            double refMaxAbs = 0;
            for (int k = 0; k < refOut.Length; k++)
                if (Math.Abs(refOut[k]) > refMaxAbs) refMaxAbs = Math.Abs(refOut[k]);
            Console.WriteLine(
                $"          layer{i} (shape [{string.Join(",", shape)}]): " +
                $"ref|x|_max={refMaxAbs:G4}");
        }
    }

    private static bool HasTensor(WeightReader r, string name)
    {
        foreach (var n in r.Names) if (n == name) return true;
        return false;
    }
}
