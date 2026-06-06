#nullable disable
using System;
using System.IO;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Primitives;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// Verifier port + DiffusionScheduler + end-to-end Mode=Port smoke tests.
// =============================================================================

static class KintsugiPortVerifierTests
{
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";
    private static readonly string FixturePath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\parity_fixtures.bin";

    private static bool BinAvail(out string reason)
    {
        if (!File.Exists(KintsugiBinPath))
        { reason = $"kintsugi.bin missing at {KintsugiBinPath}"; return false; }
        reason = null;
        return true;
    }

    public static void Verifier_AuditWeightCoverage()
    {
        if (!BinAvail(out var skip)) { Console.WriteLine($"        info: {skip}"); return; }
        var reader = new WeightReader(KintsugiBinPath);
        var (present, missing, missingNames) = VerifierWeightLoader.Audit(reader);
        Console.WriteLine($"        verifier weight audit: present={present}, missing={missing}");
        if (missing > 0 && missing <= 10)
            foreach (var n in missingNames) Console.WriteLine($"          missing: {n}");
        AssertTrue(missing == 0,
            $"{missing} required verifier tensors absent");
    }

    public static void Verifier_LoadsAllWeights()
    {
        if (!BinAvail(out var skip)) { Console.WriteLine($"        info: {skip}"); return; }
        var reader = new WeightReader(KintsugiBinPath);
        var w = VerifierWeightLoader.LoadWeights(reader);
        AssertTrue(w.Layers != null && w.Layers.Length == 6, "verifier: expected 6 layers loaded");
        AssertTrue(w.EdgeFeatureEmbW != null, "verifier: edge_feature_emb not loaded");
        AssertTrue(w.MlpOutW != null, "verifier: mlp_out not loaded");
        Console.WriteLine("        verifier load: 6 transformer layers + emb + head OK");
    }

    public static void Verifier_ForwardSmoke()
    {
        if (!BinAvail(out var skip)) { Console.WriteLine($"        info: {skip}"); return; }
        var reader = new WeightReader(KintsugiBinPath);
        var cfg = new VerifierTransformerPort.Config();
        var w = VerifierWeightLoader.LoadWeights(reader, cfg);
        var port = new VerifierTransformerPort(cfg);
        // Synthesise: 3 edges (4 fragments paired) with random 7-D features.
        int E = 3;
        var feats = new float[E * 7];
        var idx = new int[E * 2];
        var valid = new float[E];
        var rng = new Random(11);
        for (int i = 0; i < feats.Length; i++) feats[i] = (float)(rng.NextDouble() * 2 - 1);
        idx[0] = 0; idx[1] = 1;
        idx[2] = 0; idx[3] = 2;
        idx[4] = 1; idx[5] = 2;
        for (int i = 0; i < E; i++) valid[i] = 1f;
        var logits = port.Forward(feats, idx, valid, E, w);
        int nan = 0;
        foreach (var v in logits) if (float.IsNaN(v) || float.IsInfinity(v)) nan++;
        Console.WriteLine($"        verifier smoke: {E} edges -> logits [{logits[0]:F4},{logits[1]:F4},{logits[2]:F4}]; nan={nan}");
        AssertTrue(nan == 0, $"verifier produced {nan} NaN/Inf in logits");
    }

    public static void DiffusionScheduler_AlphaBarMonotonicallyDecreasing()
    {
        var sched = new DiffusionScheduler(numTrainSteps: 1000);
        // alpha_bar should start near 1.0 (small t) and decrease toward 0 (large t).
        AssertTrue(sched.AlphaBars[0] > 0.99f, $"alpha_bar[0] should be ~1, got {sched.AlphaBars[0]}");
        AssertTrue(sched.AlphaBars[999] < 0.1f, $"alpha_bar[999] should be small, got {sched.AlphaBars[999]}");
        for (int t = 1; t < 1000; t++)
            AssertTrue(sched.AlphaBars[t] <= sched.AlphaBars[t - 1] + 1e-5f,
                $"alpha_bar not monotone non-increasing at t={t}");
        Console.WriteLine($"        scheduler: alpha_bar(0)={sched.AlphaBars[0]:F4}, alpha_bar(699)={sched.AlphaBars[699]:F4}, alpha_bar(999)={sched.AlphaBars[999]:F4}");
    }

    public static void DiffusionScheduler_SetTimesteps20IsDescending()
    {
        var sched = new DiffusionScheduler();
        sched.SetTimesteps(20);
        AssertTrue(sched.InferenceTimesteps.Length == 20, "expected 20 timesteps");
        for (int i = 1; i < 20; i++)
            AssertTrue(sched.InferenceTimesteps[i] < sched.InferenceTimesteps[i - 1],
                $"timesteps not strictly descending at i={i}");
    }

    public static void DiffusionScheduler_StepProducesFiniteOutput()
    {
        var sched = new DiffusionScheduler();
        sched.SetTimesteps(20);
        var x = new float[7] { 0.5f, 0.5f, 0.5f, 0.5f, 0.1f, 0.2f, 0.3f };
        var eps = new float[7] { 0.01f, -0.01f, 0.02f, -0.02f, 0.005f, -0.005f, 0.01f };
        var t = sched.InferenceTimesteps[0];
        var xPrev = sched.Step(eps, x, t);
        for (int i = 0; i < xPrev.Length; i++)
        {
            AssertTrue(!float.IsNaN(xPrev[i]), $"step produced NaN at {i}");
            AssertTrue(!float.IsInfinity(xPrev[i]), $"step produced Inf at {i}");
        }
    }

    public static void Inference_EndToEndRunsWithoutError()
    {
        if (!BinAvail(out var skip)) { Console.WriteLine($"        info: {skip}"); return; }
        if (!File.Exists(FixturePath)) { Console.WriteLine("        info: parity_fixtures.bin missing -- skip"); return; }
        var reader = new WeightReader(KintsugiBinPath);
        // Use 3 fragments minimum so we have edges to verify.
        int F = 3;
        int N = 1000;
        var clouds = new float[F][];
        var rng = new Random(7);
        for (int f = 0; f < F; f++)
        {
            clouds[f] = new float[N * 3];
            // Random points roughly inside [-1, 1]^3.
            for (int i = 0; i < clouds[f].Length; i++) clouds[f][i] = (float)(rng.NextDouble() * 2 - 1);
        }
        // T=1 for the fastest smoke test (the re-encode-per-step loop is
        // F*T*encoder_time -- T=1, F=3 costs ~5s).
        var inf = new KintsugiPortInference(reader, numInferenceSteps: 1);
        var result = inf.RunAssembly(clouds, N, anchorIndex: 0, seed: 42);
        AssertTrue(result.Poses.Count == F, $"expected {F} poses, got {result.Poses.Count}");
        AssertTrue(result.VerifierScores.Count == F * (F - 1) / 2,
            $"expected {F * (F - 1) / 2} verifier scores");
        // Sanity: no NaN/Inf in poses; anchor is identity.
        var anchor = result.Poses[0];
        AssertTrue(anchor[0] == 0 && anchor[1] == 0 && anchor[2] == 0 && anchor[3] == 1
                   && anchor[4] == 0 && anchor[5] == 0 && anchor[6] == 0,
            $"anchor pose must be identity, got [{string.Join(",", anchor)}]");
        foreach (var p in result.Poses)
            foreach (var v in p) AssertTrue(!float.IsNaN(v) && !float.IsInfinity(v), "NaN/Inf in pose");
        Console.WriteLine($"        end-to-end inference: {F} fragments, {result.InferenceSteps} steps, " +
                          $"verifier scores: [{string.Join(",", result.VerifierScores.ConvertAll(s => s.ToString("F2")))}]");
    }

    private static void AssertTrue(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
