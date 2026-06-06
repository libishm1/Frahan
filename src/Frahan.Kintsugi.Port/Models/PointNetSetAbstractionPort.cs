#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Faithful port of the upstream PuzzleFusion++
/// `utils/pn2_utils.py::PointNetSetAbstraction` for a single-radius
/// (non-MSG) layer.
///
/// Forward signature mirrors upstream:
///   input  xyz [3, N]   (channels-first, row-major)
///   input  points [D, N] or null
///   output new_xyz [3, npoint]
///   output new_points [Cout_last, npoint]
///
/// Internal flow per the upstream code:
///   1. Transpose to channels-last: xyz_l [N, 3], points_l [N, D]
///   2. FPS sample npoint keypoints (deterministic seed for parity).
///   3. Gather new_xyz_l [npoint, 3].
///   4. Ball-query: per keypoint, gather nsample neighbour indices
///      within `radius`, padding with the first valid index.
///   5. Build grouped tensor of shape [npoint, nsample, C+D] where
///      the first 3 channels are RELATIVE xyz (neighbour - keypoint).
///   6. For each (conv, bn) pair: Linear(C_in -> C_out) + BatchNorm
///      (eval / running stats) + ReLU, applied per-(keypoint, neighbour)
///      row independently. The upstream uses Conv2d 1x1 + BatchNorm2d;
///      after the 1x1 kernel-dim squeeze this is mathematically
///      identical to a per-row Linear + per-channel BatchNorm1d.
///   7. Max-pool over the nsample axis.
///   8. Transpose new_xyz back to [3, npoint] channels-first.
/// </summary>
public sealed class PointNetSetAbstractionPort
{
    public sealed class Config
    {
        public int Npoint;       // K
        public float Radius;     // ball-query radius
        public int Nsample;      // max neighbours per ball
        public int InChannel;    // input channels including the 3 xyz coords (per upstream)
        public int[] MlpOutChannels;  // e.g. [64, 64, 128] for SA1
    }

    public sealed class Weights
    {
        // Per-MLP-layer Conv2d 1x1 weight (after squeeze) of shape [Cout, Cin].
        public float[][] ConvWeights;
        // Per-layer Conv2d bias of shape [Cout] or null.
        public float[][] ConvBiases;
        // Per-layer BatchNorm running statistics.
        public float[][] BnGammas;
        public float[][] BnBetas;
        public float[][] BnRunningMeans;
        public float[][] BnRunningVars;
    }

    private readonly Config _cfg;

    public PointNetSetAbstractionPort(Config cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
    }

    /// <summary>
    /// Forward pass. xyz: float[3 * N] in [B=1, C=3, N] channels-first
    /// row-major (X0..X_{N-1}, Y0..Y_{N-1}, Z0..Z_{N-1}). points: same
    /// convention with D channels, or null.
    ///
    /// Returns (newXyz [3 * K], newPoints [Cout * K]) channels-first.
    /// </summary>
    public (float[] newXyz, float[] newPoints) Forward(float[] xyz, int N, float[] points, int D, Weights w)
    {
        if (xyz == null || xyz.Length < 3 * N) throw new ArgumentException("xyz too small.");
        int K = _cfg.Npoint;
        int kNbr = _cfg.Nsample;

        // ---- 1. Convert channels-first [3, N] -> channels-last [N, 3].
        var xyzL = new float[N * 3];
        for (int i = 0; i < N; i++)
        {
            xyzL[i * 3 + 0] = xyz[0 * N + i];
            xyzL[i * 3 + 1] = xyz[1 * N + i];
            xyzL[i * 3 + 2] = xyz[2 * N + i];
        }
        float[] pointsL = null;
        if (points != null)
        {
            if (points.Length < D * N) throw new ArgumentException("points too small.");
            pointsL = new float[N * D];
            for (int i = 0; i < N; i++)
                for (int c = 0; c < D; c++)
                    pointsL[i * D + c] = points[c * N + i];
        }

        // ---- 2. FPS sample K keypoints, deterministic (seedIndex=0).
        // Note: this is the only place where the FPS algorithm choice
        // matters for parity. Upstream uses torch_cluster.fps with
        // random_start=False (which forces the first pick to be index 0).
        // Our Fps.Sample(seedIndex=0) does the same.
        var xyzL_d = new double[N * 3];
        for (int i = 0; i < N * 3; i++) xyzL_d[i] = xyzL[i];
        int[] fpsIdx = Fps.Sample(xyzL_d, K, seedIndex: 0);

        // ---- 3. Gather new_xyz_l [K, 3].
        var newXyzL = new float[K * 3];
        for (int k = 0; k < K; k++)
        {
            int src = fpsIdx[k];
            newXyzL[k * 3 + 0] = xyzL[src * 3 + 0];
            newXyzL[k * 3 + 1] = xyzL[src * 3 + 1];
            newXyzL[k * 3 + 2] = xyzL[src * 3 + 2];
        }

        // ---- 4. Ball-query for nsample neighbours per keypoint.
        int[] groupIdx = BallQuery.Sample(xyzL, N, newXyzL, K, _cfg.Radius, kNbr);

        // ---- 5. Build grouped feature tensor [K, kNbr, Cin].
        // Cin = 3 (relative xyz) + D (passed-through features, or 0).
        int cInRel = 3;
        int cIn = cInRel + (points != null ? D : 0);
        var grouped = new float[K * kNbr * cIn];
        for (int p = 0; p < K; p++)
        {
            float cx = newXyzL[p * 3 + 0];
            float cy = newXyzL[p * 3 + 1];
            float cz = newXyzL[p * 3 + 2];
            for (int n = 0; n < kNbr; n++)
            {
                int srcIdx = groupIdx[p * kNbr + n];
                int dst = (p * kNbr + n) * cIn;
                grouped[dst + 0] = xyzL[srcIdx * 3 + 0] - cx;
                grouped[dst + 1] = xyzL[srcIdx * 3 + 1] - cy;
                grouped[dst + 2] = xyzL[srcIdx * 3 + 2] - cz;
                if (points != null)
                {
                    for (int c = 0; c < D; c++)
                        grouped[dst + 3 + c] = pointsL[srcIdx * D + c];
                }
            }
        }

        // ---- 6. MLP stack: Linear + BN + ReLU per (conv, bn) pair.
        // Treat the grouped tensor as [K*kNbr, Cin] for matmul. Each
        // Conv2d 1x1 weight is [Cout, Cin] (after squeeze); we matmul
        // as (in @ W^T) to get [K*kNbr, Cout].
        int totalRows = K * kNbr;
        float[] mlpIn = grouped;
        int mlpInCh = cIn;

        for (int lyr = 0; lyr < w.ConvWeights.Length; lyr++)
        {
            float[] convW = w.ConvWeights[lyr];
            int cOut = _cfg.MlpOutChannels[lyr];
            // convW is [cOut, cIn] flat row-major. We need to do
            // out[i, o] = sum_k in[i, k] * convW[o, k]
            // = sum_k in[i, k] * convW_T[k, o]
            // Cheapest: transpose convW into [cIn, cOut] once, then MatMul.
            var convWT = new float[mlpInCh * cOut];
            for (int o = 0; o < cOut; o++)
                for (int k = 0; k < mlpInCh; k++)
                    convWT[k * cOut + o] = convW[o * mlpInCh + k];

            var mlpOut = new float[totalRows * cOut];
            Matmul.MatMul(mlpIn, convWT, mlpOut, totalRows, mlpInCh, cOut);
            if (w.ConvBiases != null && w.ConvBiases[lyr] != null)
                Matmul.AddBias(mlpOut, w.ConvBiases[lyr], totalRows, cOut);

            // BatchNorm2d on [B=1, Cout, kNbr, K] is per-channel: each
            // output channel applies its own (gamma, beta, running_mean,
            // running_var). After reshaping to [B*kNbr*K, Cout] = [totalRows, Cout]
            // a BatchNorm1d on dim 1 produces the equivalent answer.
            if (w.BnGammas != null && lyr < w.BnGammas.Length
                && w.BnGammas[lyr] != null && w.BnBetas[lyr] != null
                && w.BnRunningMeans[lyr] != null && w.BnRunningVars[lyr] != null)
            {
                BatchNorm1d.Apply(mlpOut,
                    w.BnGammas[lyr], w.BnBetas[lyr],
                    w.BnRunningMeans[lyr], w.BnRunningVars[lyr],
                    totalRows, cOut);
            }

            Activations.Relu(mlpOut);

            mlpIn = mlpOut;
            mlpInCh = cOut;
        }

        // ---- 7. Max-pool over the nsample axis (axis 1 in [K, kNbr, Cout]).
        // mlpIn is laid out as [K * kNbr, Cout] row-major; iterate.
        var pooled = new float[K * mlpInCh];
        for (int p = 0; p < K; p++)
        {
            int dstRow = p * mlpInCh;
            for (int c = 0; c < mlpInCh; c++) pooled[dstRow + c] = float.NegativeInfinity;
            for (int n = 0; n < kNbr; n++)
            {
                int srcRow = (p * kNbr + n) * mlpInCh;
                for (int c = 0; c < mlpInCh; c++)
                {
                    float v = mlpIn[srcRow + c];
                    if (v > pooled[dstRow + c]) pooled[dstRow + c] = v;
                }
            }
        }

        // ---- 8. Convert outputs back to channels-first [3, K] / [Cout, K].
        var newXyz = new float[3 * K];
        for (int k = 0; k < K; k++)
        {
            newXyz[0 * K + k] = newXyzL[k * 3 + 0];
            newXyz[1 * K + k] = newXyzL[k * 3 + 1];
            newXyz[2 * K + k] = newXyzL[k * 3 + 2];
        }
        var newPoints = new float[mlpInCh * K];
        for (int k = 0; k < K; k++)
            for (int c = 0; c < mlpInCh; c++)
                newPoints[c * K + k] = pooled[k * mlpInCh + c];

        return (newXyz, newPoints);
    }
}
