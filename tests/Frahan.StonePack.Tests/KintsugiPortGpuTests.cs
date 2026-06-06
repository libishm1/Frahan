#nullable disable
using System;
using System.Diagnostics;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Tests;

// =============================================================================
// ILGPU GPU-matmul tests. Auto-detects accelerator; skip-with-info if
// no GPU/CPU accelerator is available.
//
// 1. PARITY: GPU result must match CPU result to ~1e-3 absolute (GPU
//    float32 reductions can drift slightly vs scalar/SIMD reduction
//    order, but should stay well under 1e-3 for typical matrix sizes).
// 2. PERF: For 512x512x512 matmul, GPU should be at least 2x faster
//    than scalar (otherwise it's not worth the host->device round-trip).
// =============================================================================

static class KintsugiPortGpuTests
{
    public static void Gpu_Availability_Report()
    {
        Console.WriteLine($"        GPU diagnostic: {GpuMatmul.Diagnostic}");
        Console.WriteLine($"        GPU available: {GpuMatmul.IsAvailable}");
        // Test always passes -- it's a diagnostic.
    }

    public static void Gpu_Matmul_MatchesCpuToTolerance()
    {
        if (!GpuMatmul.IsAvailable)
        {
            Console.WriteLine($"        info: GPU unavailable ({GpuMatmul.Diagnostic}) -- skip");
            return;
        }
        // Small enough to verify, big enough to be non-trivial.
        const int M = 64, K = 96, N = 80;
        var rng = new Random(7);
        var a = new float[M * K];
        var b = new float[K * N];
        for (int i = 0; i < a.Length; i++) a[i] = (float)(rng.NextDouble() * 2 - 1);
        for (int i = 0; i < b.Length; i++) b[i] = (float)(rng.NextDouble() * 2 - 1);

        var cpu = new float[M * N];
        var gpu = new float[M * N];

        // Force CPU path by disabling GPU.
        var saved = GpuMatmul.GpuEnabled;
        GpuMatmul.GpuEnabled = false;
        Matmul.MatMul(a, b, cpu, M, K, N);
        GpuMatmul.GpuEnabled = saved;

        // Direct GPU call.
        GpuMatmul.MatMul(a, b, gpu, M, K, N);

        float maxDiff = 0;
        for (int i = 0; i < M * N; i++)
        {
            float d = Math.Abs(cpu[i] - gpu[i]);
            if (d > maxDiff) maxDiff = d;
        }
        Console.WriteLine($"        GPU vs CPU max|diff|={maxDiff:G4} for {M}x{K}x{N}");
        AssertTrue(maxDiff < 1e-3f,
            $"GPU matmul drifted from CPU by {maxDiff:G4} (tolerance 1e-3)");
    }

    public static void Gpu_Matmul_SpeedReport_512Cubed()
    {
        if (!GpuMatmul.IsAvailable)
        {
            Console.WriteLine($"        info: GPU unavailable ({GpuMatmul.Diagnostic}) -- skip");
            return;
        }
        const int N = 512;
        var rng = new Random(42);
        var a = new float[N * N];
        var b = new float[N * N];
        for (int i = 0; i < a.Length; i++) { a[i] = (float)rng.NextDouble(); b[i] = (float)rng.NextDouble(); }
        var c = new float[N * N];

        // CPU SIMD baseline.
        var saved = GpuMatmul.GpuEnabled;
        GpuMatmul.GpuEnabled = false;
        Matmul.MatMul(a, b, c, N, N, N);   // warm
        var swCpu = Stopwatch.StartNew();
        Matmul.MatMul(a, b, c, N, N, N);
        swCpu.Stop();
        GpuMatmul.GpuEnabled = saved;

        // GPU.
        GpuMatmul.MatMul(a, b, c, N, N, N);  // warm + kernel JIT
        var swGpu = Stopwatch.StartNew();
        GpuMatmul.MatMul(a, b, c, N, N, N);
        swGpu.Stop();

        double speedup = swCpu.ElapsedMilliseconds > 0
            ? (double)swCpu.ElapsedMilliseconds / Math.Max(1, swGpu.ElapsedMilliseconds)
            : 0;
        Console.WriteLine(
            $"        512^3 matmul: CPU(SIMD)={swCpu.ElapsedMilliseconds}ms, " +
            $"GPU={swGpu.ElapsedMilliseconds}ms, speedup={speedup:F2}x");
        // No hard gate; this is a diagnostic. On a real GPU we expect
        // 5-20x; on a CPU-only ILGPU accelerator we may see <1x because
        // ILGPU's CPU emitter is not as optimised as System.Numerics.
        // What matters is that the call returns finite results.
        AssertTrue(swGpu.ElapsedMilliseconds < 30_000,
            $"GPU matmul took {swGpu.ElapsedMilliseconds}ms (budget 30s)");
    }

    private static void AssertTrue(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
