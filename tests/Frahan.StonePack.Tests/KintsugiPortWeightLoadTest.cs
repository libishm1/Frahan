#nullable disable
using System;
using System.IO;
using System.Linq;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// Round-trip test: convert_pytorch_checkpoint.py produces kintsugi.bin;
// WeightReader.cs loads it; we sanity-check expected tensors are present.
//
// This is the Phase 7 gate -- if a kintsugi.bin is present at the
// repo-relative reference path, we exercise the load + name lookup.
// If absent, the test silently no-ops (clean clones without the
// upstream PuzzleFusion++ checkpoint should not fail this gate).
// =============================================================================

static class KintsugiPortWeightLoadTest
{
    private static readonly string DefaultBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    public static void WeightReader_LoadsKintsugiBinIfPresent()
    {
        if (!File.Exists(DefaultBinPath))
        {
            Console.WriteLine($"        info: kintsugi.bin not at {DefaultBinPath}; test is a no-op.");
            return;
        }

        var reader = new WeightReader(DefaultBinPath);
        var names = reader.Names.ToList();
        Assert(names.Count > 0, "no tensors loaded");

        // Spot-check expected tensors from each model.
        Assert(names.Any(n => n.StartsWith("ae.")), "no autoencoder tensors (prefix 'ae.')");
        Assert(names.Any(n => n.StartsWith("denoiser.")), "no denoiser tensors");
        Assert(names.Any(n => n.StartsWith("verifier.")), "no verifier tensors");
        Assert(names.Any(n => n.StartsWith("encoder.")), "no encoder-in-denoiser tensors");

        // Spot-check known shapes from the catalog.
        var w0 = reader.GetFloat32("ae.pn2.sa1.mlp_convs.0.weight");
        var s0 = reader.GetShape("ae.pn2.sa1.mlp_convs.0.weight");
        // Original was (64, 3, 1, 1); script squeezed Conv2d 1x1 to (64, 3).
        Assert(s0.Length == 2 && s0[0] == 64 && s0[1] == 3,
            $"ae.pn2.sa1.mlp_convs.0.weight shape {string.Join(",", s0)} != [64,3]");
        Assert(w0.Length == 64 * 3, $"ae.pn2.sa1.mlp_convs.0.weight elem count {w0.Length} != 192");

        var denoiserAttn = reader.GetShape("denoiser.transformer_layers.0.self_attn.to_q.weight");
        Assert(denoiserAttn.Length == 2 && denoiserAttn[0] == 512 && denoiserAttn[1] == 512,
            $"denoiser self_attn to_q shape {string.Join(",", denoiserAttn)} != [512,512]");

        Console.WriteLine($"        info: kintsugi.bin loaded ok ({names.Count} tensors)");
    }

    private static void Assert(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
