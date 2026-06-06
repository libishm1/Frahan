#nullable disable
using System;
using System.Diagnostics;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Tests;

// =============================================================================
// Performance + numerical stability gates for Frahan.Kintsugi.Port primitives.
//
// These are REGRESSION tests, not benchmarks. Each test sets a generous
// upper bound that today's implementation comfortably meets; failures mean
// the primitive got materially slower in a refactor.
//
// The thresholds are calibrated for an i7-12700H @ 2.7 GHz baseline; expect
// ~2x faster on M3 Pro / 5800X3D, ~2x slower on a 2018 budget laptop. CI
// runs on shared cloud hardware so we give a 4x safety margin.
//
// Stability tests run the SAME deterministic input twice and assert
// BIT-IDENTICAL outputs -- this catches accidental dependence on
// thread-local state, unseeded RNG, or environment-leaked memory.
// =============================================================================

static class KintsugiPortPerformanceTests
{
    // -------------------------------------------------------------------------
    // FPS: O(K*N). For N=1000 points sampling K=256 keypoints, target
    // < 50 ms (i.e. 50,000 us). The PointNet++ encoder calls this once
    // per fragment per pass.
    // -------------------------------------------------------------------------

    public static void Perf_Fps_N1000K256_Under50ms()
    {
        var rng = new Random(42);
        var pts = new double[1000 * 3];
        for (int i = 0; i < pts.Length; i++) pts[i] = rng.NextDouble() * 2 - 1;

        var sw = Stopwatch.StartNew();
        var idx = Fps.Sample(pts, 256, seedIndex: 0);
        sw.Stop();

        AssertNonNull(idx, "Fps returned null");
        AssertTrue(idx.Length == 256, $"Fps returned {idx.Length} samples, expected 256");
        AssertTrue(sw.ElapsedMilliseconds < 200,
            $"Fps N=1000 K=256 took {sw.ElapsedMilliseconds}ms (budget 200ms)");
    }

    // -------------------------------------------------------------------------
    // Matmul: 512x512 @ 512x512. Denoiser block does ~4 of these per step.
    // Target < 100 ms.
    // -------------------------------------------------------------------------

    public static void Perf_Matmul_512x512x512_Under100ms()
    {
        var rng = new Random(123);
        var a = new float[512 * 512];
        var b = new float[512 * 512];
        var c = new float[512 * 512];
        for (int i = 0; i < a.Length; i++) { a[i] = (float)(rng.NextDouble() - 0.5); b[i] = (float)(rng.NextDouble() - 0.5); }

        var sw = Stopwatch.StartNew();
        Matmul.MatMul(a, b, c, 512, 512, 512);
        sw.Stop();

        AssertTrue(sw.ElapsedMilliseconds < 500,
            $"Matmul 512x512x512 took {sw.ElapsedMilliseconds}ms (budget 500ms)");
        // Sanity: output should not be all zeros (would mean broken matmul).
        double sum = 0;
        for (int i = 0; i < c.Length; i++) sum += Math.Abs(c[i]);
        AssertTrue(sum > 1e-3, "Matmul output is all-zero -- likely broken");
    }

    // -------------------------------------------------------------------------
    // LayerNorm: in-place over [500, 512]. Denoiser does 12 LN ops per
    // block (2 per layer * 6 layers). Target < 20 ms.
    // -------------------------------------------------------------------------

    public static void Perf_LayerNorm_500x512_Under20ms()
    {
        var rng = new Random(99);
        var x = new float[500 * 512];
        for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 4 - 2);

        var sw = Stopwatch.StartNew();
        LayerNorm.Apply(x, null, null, 500, 512);
        sw.Stop();

        AssertTrue(sw.ElapsedMilliseconds < 100,
            $"LayerNorm 500x512 took {sw.ElapsedMilliseconds}ms (budget 100ms)");
    }

    // -------------------------------------------------------------------------
    // Numerical stability: same input -> bit-identical output across runs.
    // Catches accidental thread-local state, unseeded RNG, or any
    // non-deterministic behaviour that would invalidate parity tests.
    // -------------------------------------------------------------------------

    public static void Stability_Matmul_DeterministicAcrossRuns()
    {
        var rng = new Random(7);
        var a = new float[64 * 64];
        var b = new float[64 * 64];
        for (int i = 0; i < a.Length; i++) { a[i] = (float)(rng.NextDouble()); b[i] = (float)(rng.NextDouble()); }
        var c1 = new float[64 * 64];
        var c2 = new float[64 * 64];
        Matmul.MatMul(a, b, c1, 64, 64, 64);
        Matmul.MatMul(a, b, c2, 64, 64, 64);
        for (int i = 0; i < c1.Length; i++)
            AssertTrue(c1[i] == c2[i],
                $"Matmul output drift at index {i}: {c1[i]} vs {c2[i]} (not bit-identical)");
    }

    public static void Stability_Fps_DeterministicWithFixedSeed()
    {
        var rng = new Random(11);
        var pts = new double[256 * 3];
        for (int i = 0; i < pts.Length; i++) pts[i] = rng.NextDouble();
        var idx1 = Fps.Sample(pts, 64, seedIndex: 0);
        var idx2 = Fps.Sample(pts, 64, seedIndex: 0);
        AssertTrue(idx1.Length == idx2.Length, "Fps length differs across runs");
        for (int i = 0; i < idx1.Length; i++)
            AssertTrue(idx1[i] == idx2[i],
                $"Fps index drift at {i}: {idx1[i]} vs {idx2[i]} -- non-deterministic!");
    }

    public static void Stability_LayerNorm_DeterministicAcrossRuns()
    {
        var rng = new Random(13);
        var x1 = new float[8 * 16];
        for (int i = 0; i < x1.Length; i++) x1[i] = (float)(rng.NextDouble() * 2 - 1);
        var x2 = (float[])x1.Clone();
        LayerNorm.Apply(x1, null, null, 8, 16);
        LayerNorm.Apply(x2, null, null, 8, 16);
        for (int i = 0; i < x1.Length; i++)
            AssertTrue(x1[i] == x2[i],
                $"LayerNorm drift at {i}: {x1[i]} vs {x2[i]}");
    }

    // -------------------------------------------------------------------------
    // Numerical sanity: outputs are NaN/Inf-free for typical inputs.
    // -------------------------------------------------------------------------

    public static void Stability_LayerNorm_NoNanOnTypicalInput()
    {
        var rng = new Random(15);
        var x = new float[100];
        for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 4 - 2);
        LayerNorm.Apply(x, null, null, 1, 100);
        for (int i = 0; i < x.Length; i++)
        {
            AssertTrue(!float.IsNaN(x[i]), $"LayerNorm NaN at index {i}");
            AssertTrue(!float.IsInfinity(x[i]), $"LayerNorm Inf at index {i}");
        }
    }

    public static void Stability_LayerNorm_HandlesZeroVarianceInput()
    {
        // All identical values -> mean=value, var=0. With epsilon
        // protection the LN output should be finite (not NaN/Inf).
        var x = new float[16];
        for (int i = 0; i < 16; i++) x[i] = 3.14f;
        LayerNorm.Apply(x, null, null, 1, 16);
        for (int i = 0; i < x.Length; i++)
        {
            AssertTrue(!float.IsNaN(x[i]), $"zero-variance LN produced NaN at {i}");
            AssertTrue(!float.IsInfinity(x[i]), $"zero-variance LN produced Inf at {i}");
        }
    }

    public static void Stability_Gelu_NoNanOnLargeMagnitudes()
    {
        // Gelu approximation involves tanh which can overflow if not
        // implemented carefully. Test with values from -50 to 50.
        var x = new float[101];
        for (int i = 0; i < 101; i++) x[i] = i - 50.0f;
        Activations.Gelu(x);
        for (int i = 0; i < x.Length; i++)
        {
            AssertTrue(!float.IsNaN(x[i]), $"Gelu NaN at x[{i}] (input was {i - 50})");
            AssertTrue(!float.IsInfinity(x[i]), $"Gelu Inf at x[{i}]");
        }
    }

    // -------------------------------------------------------------------------
    // Round-trip stability: chained primitives (matmul + layernorm + gelu)
    // produce deterministic, bounded output. Mimics one transformer block's
    // FFN sub-stage.
    // -------------------------------------------------------------------------

    public static void Stability_TransformerFfnLikeChain_DeterministicAndBounded()
    {
        const int M = 8, D = 16, F = 64;
        var rng = new Random(17);
        var x = new float[M * D];
        var w1 = new float[D * F];
        var w2 = new float[F * D];
        for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() - 0.5);
        for (int i = 0; i < w1.Length; i++) w1[i] = (float)(rng.NextDouble() * 0.2 - 0.1);
        for (int i = 0; i < w2.Length; i++) w2[i] = (float)(rng.NextDouble() * 0.2 - 0.1);

        float[] Run()
        {
            var xx = (float[])x.Clone();
            LayerNorm.Apply(xx, null, null, M, D);
            var hidden = new float[M * F];
            Matmul.MatMul(xx, w1, hidden, M, D, F);
            Activations.Gelu(hidden);
            var output = new float[M * D];
            Matmul.MatMul(hidden, w2, output, M, F, D);
            return output;
        }

        var o1 = Run();
        var o2 = Run();
        for (int i = 0; i < o1.Length; i++)
        {
            AssertTrue(o1[i] == o2[i],
                $"FFN chain drift at {i}: {o1[i]} vs {o2[i]} -- non-deterministic");
            AssertTrue(!float.IsNaN(o1[i]) && !float.IsInfinity(o1[i]),
                $"FFN chain produced NaN/Inf at {i}");
            AssertTrue(Math.Abs(o1[i]) < 100,
                $"FFN chain output exploded at {i}: |{o1[i]}| > 100");
        }
    }

    private static void AssertTrue(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }

    private static void AssertNonNull(object o, string msg)
    {
        if (o == null) throw new Exception(msg);
    }
}
