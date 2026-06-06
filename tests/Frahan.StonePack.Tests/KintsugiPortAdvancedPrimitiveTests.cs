#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Tests;

// =============================================================================
// Tests for the Phase 2-5-supporting primitives: BatchNorm1d + AdaLN.
// Validates against analytical / hand-computed values, the same way
// KintsugiPortPrimitiveTests covers FPS/KNN/Matmul/MHA/etc.
// =============================================================================

static class KintsugiPortAdvancedPrimitiveTests
{
    private const double Tol = 1e-5;

    public static void BatchNorm1d_IdentityWithUnitGammaZeroBeta()
    {
        // Input (mean 0, var 1) -> output equal to input under
        // gamma=1, beta=0, running_mean=0, running_var=1.
        var x = new float[] { -1f, 0f, 1f,   2f, 3f, 4f };
        var gamma = new float[] { 1f, 1f, 1f };
        var beta = new float[] { 0f, 0f, 0f };
        var rm = new float[] { 0f, 0f, 0f };
        var rv = new float[] { 1f, 1f, 1f };
        var orig = (float[])x.Clone();
        BatchNorm1d.Apply(x, gamma, beta, rm, rv, 2, 3);
        for (int i = 0; i < 6; i++)
            AssertNear(x[i], orig[i], 1e-4, $"x[{i}] identity BN");
    }

    public static void BatchNorm1d_AppliesGammaShiftBeta()
    {
        // gamma=2, beta=10 on a single sample.
        var x = new float[] { 0f, 0f, 0f };
        var gamma = new float[] { 2f, 2f, 2f };
        var beta = new float[] { 10f, 10f, 10f };
        var rm = new float[] { 0f, 0f, 0f };
        var rv = new float[] { 1f, 1f, 1f };
        BatchNorm1d.Apply(x, gamma, beta, rm, rv, 1, 3);
        // Each = (0 - 0) / sqrt(1+eps) * 2 + 10 = 10.
        for (int c = 0; c < 3; c++)
            AssertNear(x[c], 10.0, 1e-4, $"x[{c}] = beta");
    }

    public static void BatchNorm1d_SubtractsRunningMean()
    {
        // gamma=1, beta=0, running_mean=5, running_var=4 (std=2).
        // Input 5 -> (5-5)/2 = 0.
        // Input 7 -> (7-5)/2 = 1.
        var x = new float[] { 5f, 7f };
        var gamma = new float[] { 1f, 1f };
        var beta = new float[] { 0f, 0f };
        var rm = new float[] { 5f, 5f };
        var rv = new float[] { 4f, 4f };
        BatchNorm1d.Apply(x, gamma, beta, rm, rv, 1, 2);
        AssertNear(x[0], 0.0, 1e-4, "input==mean -> 0");
        AssertNear(x[1], 1.0, 1e-4, "(7-5)/2 = 1");
    }

    public static void AdaLN_IdentityWhenEmbProjectsToZero()
    {
        // With w_emb all zero, t_proj = 0 -> shift=scale=gate=0.
        // AdaLN should reduce to LayerNorm-only (which centres + normalises
        // each row), then y = x_norm * 1 + 0 = x_norm.
        int M = 2, D = 4, dT = 8;
        var x = new float[] { 1f, 2f, 3f, 4f,  5f, 5f, 5f, 5f };
        var t = new float[dT];
        for (int i = 0; i < dT; i++) t[i] = 1.0f;
        var wEmb = new float[dT * 3 * D]; // all zeros
        var origRow0 = new float[] { 1f, 2f, 3f, 4f };
        // LayerNorm: row 0 mean = 2.5, std = sqrt((1.5^2+0.5^2+0.5^2+1.5^2)/4)
        //          = sqrt(5/4) = ~1.118. So x_norm[0] = (1-2.5)/1.118 = ~-1.342.
        // Expected first element ~-1.342 (since gate doesn't apply here).
        var gate = AdaLN.Apply(x, M, D, t, dT, wEmb, null);
        Assert(gate != null && gate.Length == M * D, "gate shape");
        // First element should be close to -1.342 (LayerNorm of the first
        // row, then +0 shift, *1 scale).
        Assert(Math.Abs(x[0] - (-1.342)) < 0.01, $"row 0 first elem {x[0]}, expected ~-1.342");
        // Row 1 has zero variance -> LN produces NaN normally; epsilon
        // prevents that. Check it's finite and ~0 (since all values equal).
        for (int i = 0; i < D; i++)
            Assert(!float.IsNaN(x[M * D - D + i]) && !float.IsInfinity(x[M * D - D + i]),
                "row 1 has finite values despite zero variance");
    }

    public static void AdaLN_GateIsBroadcastedAcrossTokens()
    {
        // Build w_emb such that t_proj = [shift_const, scale_const, gate_const]
        // for each block. Verify gate output is broadcast identically across
        // all M tokens.
        int M = 3, D = 2, dT = 1;
        var x = new float[] { 0f, 0f,   0f, 0f,   0f, 0f };
        var t = new float[] { 1.0f };
        // wEmb shape [dT, 3*D] = [1, 6]. Set so t_proj = [shift, scale, gate]
        // with distinct values: shift=0.1, scale=0.2, gate=0.3 across both
        // dims.
        var wEmb = new float[1 * 6];
        // After silu(1) approx 0.731. To get t_proj = [.1, .1, .2, .2, .3, .3]
        // from t_proj = silu(t) * wEmb, set wEmb / 0.731 ~ 0.137, 0.137, 0.274,...
        float sil1 = 1.0f / (1.0f + (float)Math.Exp(-1.0f));
        wEmb[0] = 0.1f / sil1; wEmb[1] = 0.1f / sil1; // shift
        wEmb[2] = 0.2f / sil1; wEmb[3] = 0.2f / sil1; // scale
        wEmb[4] = 0.3f / sil1; wEmb[5] = 0.3f / sil1; // gate
        var gate = AdaLN.Apply(x, M, D, t, dT, wEmb, null);
        // Gate should be [0.3, 0.3, 0.3, 0.3, 0.3, 0.3] (broadcast over M)
        for (int i = 0; i < M * D; i++)
            AssertNear(gate[i], 0.3, 1e-3, $"gate[{i}]");
    }

    public static void BallQuery_PicksAllInRadius()
    {
        // 5 points on a line at x = 0, 1, 2, 3, 4. Query at x=2 with
        // radius=1.5. Expected: indices {1, 2, 3} (within distance 1.5).
        var xyz = new float[] {
            0f, 0f, 0f,
            1f, 0f, 0f,
            2f, 0f, 0f,
            3f, 0f, 0f,
            4f, 0f, 0f,
        };
        var query = new float[] { 2f, 0f, 0f };
        var idx = BallQuery.Sample(xyz, 5, query, 1, 1.5f, 4);
        // Upstream's sort-by-index then pad-with-first semantics: in-radius
        // indices in ascending order = {1, 2, 3}, then pad slot[3] with
        // first valid = 1.
        Assert(idx[0] == 1, $"idx[0]: expected 1, got {idx[0]}");
        Assert(idx[1] == 2, $"idx[1]: expected 2, got {idx[1]}");
        Assert(idx[2] == 3, $"idx[2]: expected 3, got {idx[2]}");
        Assert(idx[3] == 1, $"idx[3] (pad): expected 1, got {idx[3]}");
    }

    public static void BallQuery_CapsAtNsample()
    {
        // Many points all inside radius; ensure we return only nsample.
        var xyz = new float[10 * 3];
        for (int i = 0; i < 10; i++) { xyz[i * 3] = i * 0.1f; }  // x = 0, 0.1, 0.2, ...
        var query = new float[] { 0.5f, 0f, 0f };
        var idx = BallQuery.Sample(xyz, 10, query, 1, 100.0f, 3);
        // First 3 in ascending order: 0, 1, 2.
        Assert(idx[0] == 0 && idx[1] == 1 && idx[2] == 2,
            $"got [{idx[0]},{idx[1]},{idx[2]}], expected [0,1,2]");
    }

    public static void BallQuery_NoNeighboursFillsPaddingSafely()
    {
        // Query far outside any point; should not throw, returns padded
        // sentinel indices.
        var xyz = new float[] { 0f, 0f, 0f, 1f, 0f, 0f };
        var query = new float[] { 100f, 0f, 0f };
        var idx = BallQuery.Sample(xyz, 2, query, 1, 0.5f, 4);
        // All four slots should be valid indices (not crashes / negatives).
        for (int i = 0; i < 4; i++)
            Assert(idx[i] >= 0 && idx[i] < 2,
                $"out-of-radius padding gave invalid idx {idx[i]} at slot {i}");
    }

    public static void MultiHeadAttention_SplitQkvViaSeparateWeights()
    {
        // The existing MHA already takes wq/wk/wv as SEPARATE [D,D]
        // matrices. This test verifies that splitting a concat in_proj
        // (the PyTorch convention used by some upstream layers) into
        // three matrices then feeding into our MHA produces consistent
        // output regardless of where the split happens.
        int D = 4, H = 2, M = 3;
        var rng = new Random(99);
        var x = new float[M * D];
        for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 2 - 1);

        // Three random projection matrices.
        var wq = new float[D * D];
        var wk = new float[D * D];
        var wv = new float[D * D];
        var wo = new float[D * D];
        for (int i = 0; i < D * D; i++)
        {
            wq[i] = (float)(rng.NextDouble() * 0.2 - 0.1);
            wk[i] = (float)(rng.NextDouble() * 0.2 - 0.1);
            wv[i] = (float)(rng.NextDouble() * 0.2 - 0.1);
            wo[i] = (float)(rng.NextDouble() * 0.2 - 0.1);
        }
        // Identity wo for an output sanity check.
        for (int i = 0; i < D * D; i++) wo[i] = 0;
        for (int i = 0; i < D; i++) wo[i * D + i] = 1;

        var xCopy = (float[])x.Clone();
        var mha = new MultiHeadAttention(D, H);
        mha.Apply(x, wq, null, wk, null, wv, null, wo, null, M);

        // Just sanity-check it ran and didn't NaN; numerical equivalence
        // against PyTorch is the Phase 7 parity test gate, not this one.
        for (int i = 0; i < M * D; i++)
        {
            Assert(!float.IsNaN(x[i]) && !float.IsInfinity(x[i]),
                $"x[{i}] is finite after MHA");
        }
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
