#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// Performance measurements for the denoiser forward path. Calibrates
// realistic timings for the user-visible Mode=Port inference budget.
//
// At inference time, the denoiser runs ONCE per diffusion timestep,
// and the upstream uses num_inference_steps=20. So:
//   total denoiser cost = single-forward-time x 20
// per ONE Mode=Port solve.
//
// These tests print the timings; they don't fail unless the forward
// takes >60s (would mean a regression made it 60x slower).

static class KintsugiPortDenoiserPerfTests
{
    private static readonly string FixturePath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\parity_fixtures.bin";
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";

    public static void Perf_Denoiser_SingleForward_Time()
    {
        if (!File.Exists(FixturePath) || !File.Exists(KintsugiBinPath))
        { Console.WriteLine("        info: parity / kintsugi.bin missing -- skip"); return; }
        var fixtures = new WeightReader(FixturePath);
        var weights = new WeightReader(KintsugiBinPath);
        var cfg = new DenoiserTransformerPort.Config();
        var w = DenoiserWeightLoader.LoadWeights(weights, cfg);
        int N = 20;
        var x = new float[N * 7];
        var latent = new float[N * cfg.NumPoint * cfg.NumDim];
        var xyz = new float[N * cfg.NumPoint * 3];
        var valids = new float[N];
        var scale = new float[N];
        var refPart = new int[N];
        var poses = fixtures.GetFloat32("parity.input.noisy_poses");
        Buffer.BlockCopy(poses, 0, x, 0, Math.Min(8, poses.Length / 7) * 7 * sizeof(float));
        for (int n = 0; n < 8; n++) { valids[n] = 1f; scale[n] = 1f; }
        refPart[0] = 1;
        var port = new DenoiserTransformerPort(cfg);

        // Warm-up.
        var _ = port.Forward(x, latent, xyz, valids, scale, refPart, 500, N, w);

        var sw = Stopwatch.StartNew();
        var result = port.Forward(x, latent, xyz, valids, scale, refPart, 500, N, w);
        sw.Stop();
        long singleMs = sw.ElapsedMilliseconds;
        long projectedT20Ms = singleMs * 20;
        Console.WriteLine(
            $"        Denoiser single forward (N={N}, 6 layers, D=512): {singleMs}ms");
        Console.WriteLine(
            $"        Projected T=20 diffusion loop: {projectedT20Ms}ms (~{projectedT20Ms / 1000.0:F1}s)");
        AssertTrue(result != null, "denoiser returned null");
        AssertTrue(singleMs < 60_000,
            $"single forward took {singleMs}ms -- regression suspected (budget 60s)");
    }

    private static void AssertTrue(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
