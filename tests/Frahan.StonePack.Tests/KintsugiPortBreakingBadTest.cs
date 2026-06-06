#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using Frahan.Kintsugi.Port.Outer;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Tests;

// =============================================================================
// Run Mode=Port end-to-end on a real Breaking Bad sample from pc_data.zip
// (training-distribution data). Compares against the random-shatter result
// to disambiguate "parity drift" from "distribution mismatch".
//
// SAMPLE INPUT
//   data/pc_data/everyday/val/00697.npz
//     - 2 fragments
//     - 1000 points each, float32
//     - Already aligned at ground-truth canonical positions
//
// EXPECTED IF THE PORT IS PARITY-CORRECT
//   The denoiser should predict poses near IDENTITY for both fragments
//   (since they're already at GT). The verifier should give a HIGH score
//   on the single (0, 1) pair. Per the paper, Part Accuracy ~70% on
//   Breaking Bad / everyday-val, so we expect verifier > 0.5 for a
//   correctly-paired sample.
//
// EXPECTED IF PARITY HAS DRIFTED
//   Predicted poses are arbitrary; verifier scores stay ~0.2 (model
//   guess); no clear assembly signal.
//
// Use this diagnostic to decide whether the issue blocking 0/45 hits on
// random shatters is parity drift (fix the port) or distribution
// mismatch (different test inputs needed).
// =============================================================================

static class KintsugiPortBreakingBadTest
{
    private static readonly string KintsugiBinPath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\kintsugi.bin";
    private static readonly string BbSamplePath =
        @"D:\code_ws\Template-General\outputs\2026-05-22\reference\bb_sample_00697.bin";

    public static void BreakingBad_LoadsSample()
    {
        if (!File.Exists(BbSamplePath))
        { Console.WriteLine($"        info: bb_sample_00697.bin missing -- run extract_breaking_bad_sample.py first"); return; }
        var reader = new WeightReader(BbSamplePath);
        int found = 0;
        foreach (var n in reader.Names) { if (n.StartsWith("bb.input.")) found++; }
        AssertTrue(found > 0, "no bb.input.* tensors in the sample binary");
        var pc = reader.GetFloat32("bb.input.point_clouds");
        var pcShape = reader.GetShape("bb.input.point_clouds");
        Console.WriteLine($"        BB sample 00697: point_clouds shape=[{string.Join(",", pcShape)}], " +
                          $"first values=[{pc[0]:F4},{pc[1]:F4},{pc[2]:F4}]");
    }

    public static void BreakingBad_RunInferenceAndReportScores()
    {
        if (!File.Exists(KintsugiBinPath))
        { Console.WriteLine($"        info: kintsugi.bin missing -- skip"); return; }
        if (!File.Exists(BbSamplePath))
        { Console.WriteLine($"        info: bb_sample_00697.bin missing -- skip"); return; }
        var bb = new WeightReader(BbSamplePath);
        var pcShape = bb.GetShape("bb.input.point_clouds");
        int F = pcShape[0];
        int N = pcShape[1];
        AssertTrue(F >= 2, $"need >= 2 fragments, got {F}");
        AssertTrue(N == 1000, $"upstream convention is N=1000 points, got {N}");
        var pcFlat = bb.GetFloat32("bb.input.point_clouds");
        // Split into per-fragment channels-LAST [N, 3] arrays.
        var clouds = new float[F][];
        for (int f = 0; f < F; f++)
        {
            clouds[f] = new float[N * 3];
            Buffer.BlockCopy(pcFlat, f * N * 3 * sizeof(float),
                             clouds[f], 0, N * 3 * sizeof(float));
        }
        Console.WriteLine($"        BB sample 00697: {F} fragments, {N} points each. Running inference...");

        var weightReader = new WeightReader(KintsugiBinPath);
        // T=10 -- balance between CI time and giving the denoiser
        // enough steps to converge from random init.
        var inf = new KintsugiPortInference(weightReader, numInferenceSteps: 10);
        var sw = Stopwatch.StartNew();
        var result = inf.RunAssembly(clouds, N, anchorIndex: 0, seed: 42);
        sw.Stop();
        Console.WriteLine($"        BB inference took {sw.ElapsedMilliseconds}ms " +
                          $"({F} fragments x 5 steps).");
        // Sanity: poses are finite.
        for (int f = 0; f < F; f++)
        {
            var p = result.Poses[f];
            for (int i = 0; i < 7; i++)
                AssertTrue(!float.IsNaN(p[i]) && !float.IsInfinity(p[i]),
                    $"frag {f} pose[{i}] is NaN/Inf");
        }
        // Anchor (frag 0) should be EXACTLY identity (the orchestrator forces it).
        var anchor = result.Poses[0];
        AssertTrue(anchor[0] == 0f && anchor[1] == 0f && anchor[2] == 0f
                   && anchor[3] == 1f && anchor[4] == 0f && anchor[5] == 0f && anchor[6] == 0f,
            "anchor must be identity after diffusion (forced by orchestrator)");
        // Distance-from-identity per non-anchor fragment. Closer to zero
        // means denoiser thinks the fragment is at its GT position.
        Console.WriteLine($"        Per-fragment pose distance from identity:");
        for (int f = 0; f < F; f++)
        {
            var p = result.Poses[f];
            // identity: trans=(0,0,0), quat=(1,0,0,0)
            float tDist = (float)Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
            // Quaternion angle distance from identity quat (1,0,0,0):
            //   theta = 2 * acos(|<q, q_id>|) = 2 * acos(|q.w|)
            float qw = Math.Abs(p[3]);
            qw = Math.Min(1f, Math.Max(-1f, qw));
            float angleRad = 2f * (float)Math.Acos(qw);
            float angleDeg = angleRad * 180f / (float)Math.PI;
            Console.WriteLine($"          frag {f}: " +
                              $"trans=({p[0]:F3},{p[1]:F3},{p[2]:F3}) |t|={tDist:F3}, " +
                              $"quat=({p[3]:F3},{p[4]:F3},{p[5]:F3},{p[6]:F3}), " +
                              $"angle-from-id={angleDeg:F1} deg");
        }
        Console.WriteLine($"        Verifier scores ({result.VerifierScores.Count} pairs):");
        for (int i = 0; i < result.VerifierScores.Count && i < 6; i++)
            Console.WriteLine($"          pair {i}: score={result.VerifierScores[i]:F4}");
        int strongPairs = 0;
        foreach (var s in result.VerifierScores) if (s > 0.5f) strongPairs++;
        Console.WriteLine($"        Strong pairs (>0.5): {strongPairs} / {result.VerifierScores.Count}");
    }

    private static void AssertTrue(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
