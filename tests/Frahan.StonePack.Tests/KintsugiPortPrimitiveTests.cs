#nullable disable
using System;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Tests;

// =============================================================================
// Headless tests for the Kintsugi port Phase 1 primitives. No Rhino runtime
// needed; pure managed C#. Validates arithmetic correctness against
// analytical / known-good values BEFORE the Phase 7 PyTorch-parity gate.
//
// Catches: matmul / softmax / LayerNorm / attention scaling bugs that
// would only surface much later as garbage inference output.
// =============================================================================

static class KintsugiPortPrimitiveTests
{
    private const double Tol = 1e-5;

    public static void Fps_PicksCornersOfASquare()
    {
        // 4 unit-square corners. Seeded at (0,0,0). FPS picks furthest
        // each step: should visit all 4 corners in some order.
        var pts = new double[]
        {
            0, 0, 0,
            1, 0, 0,
            0, 1, 0,
            1, 1, 0,
        };
        var picked = Fps.Sample(pts, 4, seedIndex: 0);
        Assert(picked.Length == 4, $"got {picked.Length} picks");
        var seen = new bool[4];
        foreach (var idx in picked) seen[idx] = true;
        for (int i = 0; i < 4; i++) Assert(seen[i], $"vertex {i} missing");
    }

    public static void Fps_SecondPickIsOpposite()
    {
        // Seed at 0, second pick must be index 3 (the OPPOSITE corner)
        // -- it's the furthest single point.
        var pts = new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0 };
        var picked = Fps.Sample(pts, 2, seedIndex: 0);
        Assert(picked[0] == 0, $"seed was {picked[0]}");
        Assert(picked[1] == 3, $"second pick was {picked[1]}, expected 3 (opposite)");
    }

    public static void Knn_ReturnsClosestPoints()
    {
        // Cloud: 5 points on a line at x = 0, 1, 2, 3, 4 (z=y=0).
        // Query at x=2.1, k=3 -> indices 2, 3, 1.
        var cloud = new double[] { 0, 0, 0, 1, 0, 0, 2, 0, 0, 3, 0, 0, 4, 0, 0 };
        var query = new double[] { 2.1, 0, 0 };
        var nbrs = Knn.NearestK(cloud, query, 3);
        Assert(nbrs.Length == 3, $"got {nbrs.Length}");
        Assert(nbrs[0] == 2, $"nearest is {nbrs[0]}, expected 2");
        Assert(nbrs[1] == 3, $"second is {nbrs[1]}, expected 3");
        Assert(nbrs[2] == 1, $"third is {nbrs[2]}, expected 1");
    }

    public static void Matmul_IdentityVsKnownProduct()
    {
        // 2x3 @ 3x2: should match hand-computed result.
        var a = new float[] { 1, 2, 3,  4, 5, 6 };
        var b = new float[] { 7, 8,  9, 10,  11, 12 };
        var c = new float[4];
        Matmul.MatMul(a, b, c, 2, 3, 2);
        // c[0,0] = 1*7 + 2*9 + 3*11 = 58
        // c[0,1] = 1*8 + 2*10 + 3*12 = 64
        // c[1,0] = 4*7 + 5*9 + 6*11 = 139
        // c[1,1] = 4*8 + 5*10 + 6*12 = 154
        AssertNear(c[0], 58.0, Tol, "c[0,0]");
        AssertNear(c[1], 64.0, Tol, "c[0,1]");
        AssertNear(c[2], 139.0, Tol, "c[1,0]");
        AssertNear(c[3], 154.0, Tol, "c[1,1]");
    }

    public static void Matmul_LargeShapesProduceSameResultAsNaiveLoop()
    {
        // Random-ish 16x32 @ 32x8. SIMD-on path must match naive scalar.
        var rng = new Random(1234);
        int M = 16, K = 32, N = 8;
        var a = new float[M * K];
        var b = new float[K * N];
        for (int i = 0; i < a.Length; i++) a[i] = (float)(rng.NextDouble() * 2 - 1);
        for (int i = 0; i < b.Length; i++) b[i] = (float)(rng.NextDouble() * 2 - 1);
        var cSimd = new float[M * N];
        Matmul.MatMul(a, b, cSimd, M, K, N);
        // Naive reference
        var cRef = new float[M * N];
        for (int i = 0; i < M; i++)
            for (int j = 0; j < N; j++)
            {
                float s = 0;
                for (int k = 0; k < K; k++) s += a[i * K + k] * b[k * N + j];
                cRef[i * N + j] = s;
            }
        for (int i = 0; i < M * N; i++)
            AssertNear(cSimd[i], cRef[i], 1e-4, $"c[{i}] mismatch");
    }

    public static void MatVec_MatchesMatMulN1()
    {
        var rng = new Random(42);
        int M = 8, K = 16;
        var a = new float[M * K];
        var x = new float[K];
        for (int i = 0; i < a.Length; i++) a[i] = (float)(rng.NextDouble() * 2 - 1);
        for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 2 - 1);
        var yVec = new float[M];
        var yMM = new float[M];
        Matmul.MatVec(a, x, yVec, M, K);
        Matmul.MatMul(a, x, yMM, M, K, 1);
        for (int i = 0; i < M; i++) AssertNear(yVec[i], yMM[i], 1e-5, $"y[{i}]");
    }

    public static void Activations_GeluAtZeroIsZero()
    {
        var x = new float[] { 0f };
        Activations.Gelu(x);
        AssertNear(x[0], 0.0, Tol, "GELU(0)");
    }

    public static void Activations_GeluAtPositiveTendsToInput()
    {
        // For large positive x, GELU(x) ≈ x.
        var x = new float[] { 100f };
        Activations.Gelu(x);
        AssertNear(x[0], 100.0, 1e-2, "GELU(100) ≈ 100");
    }

    public static void Activations_SiluAtZeroIsZero()
    {
        var x = new float[] { 0f };
        Activations.Silu(x);
        AssertNear(x[0], 0.0, Tol, "SiLU(0)");
    }

    public static void Activations_ReluClampsNegatives()
    {
        var x = new float[] { -1f, 0f, 1f, -100f };
        Activations.Relu(x);
        AssertNear(x[0], 0.0, Tol, "ReLU(-1)");
        AssertNear(x[1], 0.0, Tol, "ReLU(0)");
        AssertNear(x[2], 1.0, Tol, "ReLU(1)");
        AssertNear(x[3], 0.0, Tol, "ReLU(-100)");
    }

    public static void Activations_SoftmaxRowSumsToOne()
    {
        var x = new float[] { 1f, 2f, 3f, 4f,  10f, 20f, 30f, 40f };
        Activations.SoftmaxRowwise(x, 2, 4);
        float row0 = x[0] + x[1] + x[2] + x[3];
        float row1 = x[4] + x[5] + x[6] + x[7];
        AssertNear(row0, 1.0, 1e-5, "softmax row 0 sum");
        AssertNear(row1, 1.0, 1e-5, "softmax row 1 sum");
        // Row 1 (10, 20, 30, 40) should be dominated by the largest: ~0,~0,~0,~1.
        Assert(x[7] > 0.99f, $"softmax row 1 final element {x[7]} not ~1");
    }

    public static void LayerNorm_ZeroMeanUnitVar()
    {
        // Input row with mean 5, var 4. After LN: mean ~0, var ~1.
        var x = new float[] { 3f, 5f, 7f, 5f }; // mean 5, var (4+0+4+0)/4=2; std=sqrt(2)
        LayerNorm.Apply(x, null, null, 1, 4);
        float mean = (x[0] + x[1] + x[2] + x[3]) / 4f;
        AssertNear(mean, 0.0, 1e-5, "post-LN mean ≈ 0");
        float var = 0;
        for (int i = 0; i < 4; i++) var += x[i] * x[i];
        var /= 4f;
        AssertNear(var, 1.0, 1e-4, "post-LN var ≈ 1");
    }

    public static void LayerNorm_GammaBetaScaleAndShift()
    {
        // After LN normalisation, gamma + beta affine should be applied.
        var x = new float[] { 0f, 1f, 2f, 3f };
        var gamma = new float[] { 2f, 2f, 2f, 2f };
        var beta = new float[] { 5f, 5f, 5f, 5f };
        LayerNorm.Apply(x, gamma, beta, 1, 4);
        // Mean after scale=2: 2*0 = 0; mean after shift +5: 5. So row mean = 5.
        float mean = (x[0] + x[1] + x[2] + x[3]) / 4f;
        AssertNear(mean, 5.0, 1e-4, "post-affine mean = beta");
    }

    public static void TimeEmbedding_LengthAndSymmetry()
    {
        var emb = TimeEmbedding.Encode(100f, 64);
        Assert(emb.Length == 64, "len=64");
        // For t=0, sin terms = 0, cos terms = 1. Quick sanity check at t=0.
        var emb0 = TimeEmbedding.Encode(0f, 64);
        for (int i = 0; i < 32; i++) AssertNear(emb0[i], 0.0, 1e-5, $"sin term {i} at t=0");
        for (int i = 0; i < 32; i++) AssertNear(emb0[32 + i], 1.0, 1e-5, $"cos term {i} at t=0");
    }

    public static void MultiHeadAttention_IdentityWeightsPassThrough()
    {
        // 1-token sequence: attention degenerates to identity (one query
        // attending to itself with weight 1). With identity output proj,
        // input should pass through unchanged.
        int D = 8, H = 2, M = 1;
        var x = new float[D];
        var rng = new Random(7);
        for (int i = 0; i < D; i++) x[i] = (float)(rng.NextDouble() * 2 - 1);
        var orig = (float[])x.Clone();

        // Identity-ish Wq/Wk/Wv: pre-normalised so the inner product
        // doesn't grossly inflate. Use identity matrices [D, D].
        var I = new float[D * D];
        for (int i = 0; i < D; i++) I[i * D + i] = 1f;
        var zero = new float[D];

        var mha = new MultiHeadAttention(D, H);
        // No biases.
        mha.Apply(x, I, null, I, null, I, null, I, null, M);
        // With identity Wq/Wk/Wv/Wo and M=1, output should equal input
        // (single token attends to itself with weight 1 after softmax).
        for (int i = 0; i < D; i++) AssertNear(x[i], orig[i], 1e-4, $"x[{i}] pass-through");
    }

    public static void VqVae_ReplacesWithNearestCodebookEntry()
    {
        // 3-entry codebook in 2-D: (0,0), (1,0), (0,1).
        var codebook = new float[] { 0f, 0f,   1f, 0f,   0f, 1f };
        var vqvae = new VqVae(codebook, 3, 2);
        // Input row 1: (0.9, 0.1) -> nearest is (1, 0). Indices[0] = 1.
        // Input row 2: (0.1, 0.9) -> nearest is (0, 1). Indices[1] = 2.
        // Input row 3: (0.1, 0.1) -> nearest is (0, 0). Indices[2] = 0.
        var x = new float[] { 0.9f, 0.1f,  0.1f, 0.9f,  0.1f, 0.1f };
        var idx = vqvae.Quantise(x, 3);
        Assert(idx[0] == 1, $"row 0 -> {idx[0]}, expected 1");
        Assert(idx[1] == 2, $"row 1 -> {idx[1]}, expected 2");
        Assert(idx[2] == 0, $"row 2 -> {idx[2]}, expected 0");
        // Values should have been replaced by codebook entries.
        AssertNear(x[0], 1.0, Tol, "x[0,0] = codebook[1,0]");
        AssertNear(x[1], 0.0, Tol, "x[0,1] = codebook[1,1]");
    }

    private static void Assert(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} +- {tol}, got {actual}");
    }
}
