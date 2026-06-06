#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// Maps the PuzzleFusion++ autoencoder weight tensors out of a loaded
/// kintsugi.bin into PointNetSetAbstractionPort.Weights structures
/// ready to feed into Forward(). Single-purpose helper -- isolates the
/// state-dict naming convention from the model code itself.
///
/// Architecture per upstream `vqvae/model/modules/pn2.py`:
///   SA1: npoint=256, radius=0.2, nsample=32, in=3,       mlp=[64, 64, 128]
///   SA2: npoint=128, radius=0.4, nsample=64, in=128+3,   mlp=[128, 128, 256]
///   SA3: npoint=25,  radius=0.8, nsample=64, in=256+3,   mlp=[256, 256, 512]
///   conv6: Conv1d(512, 64, kernel=1)
///
/// Tensor names produced by convert_pytorch_checkpoint.py:
///   ae.pn2.sa{L}.mlp_convs.{K}.weight   -> shape [Cout, Cin] (after 1x1 squeeze)
///   ae.pn2.sa{L}.mlp_convs.{K}.bias     -> [Cout]
///   ae.pn2.sa{L}.mlp_bns.{K}.weight     -> [Cout]  (gamma)
///   ae.pn2.sa{L}.mlp_bns.{K}.bias       -> [Cout]  (beta)
///   ae.pn2.sa{L}.mlp_bns.{K}.running_mean -> [Cout]
///   ae.pn2.sa{L}.mlp_bns.{K}.running_var  -> [Cout]
///   ae.pn2.conv6.weight                 -> [64, 512]  (after Conv1d 1x1 squeeze)
///   ae.pn2.conv6.bias                   -> [64]
/// </summary>
public static class EncoderWeightLoader
{
    /// <summary>
    /// Build the three SA layer configs to match the upstream's
    /// pn2.py constructor verbatim.
    /// </summary>
    public static PointNetSetAbstractionPort.Config[] BuildConfigs()
    {
        return new[]
        {
            new PointNetSetAbstractionPort.Config
            {
                Npoint = 256, Radius = 0.2f, Nsample = 32,
                InChannel = 3, MlpOutChannels = new[] { 64, 64, 128 },
            },
            new PointNetSetAbstractionPort.Config
            {
                Npoint = 128, Radius = 0.4f, Nsample = 64,
                InChannel = 128 + 3, MlpOutChannels = new[] { 128, 128, 256 },
            },
            new PointNetSetAbstractionPort.Config
            {
                Npoint = 25, Radius = 0.8f, Nsample = 64,
                InChannel = 256 + 3, MlpOutChannels = new[] { 256, 256, 512 },
            },
        };
    }

    /// <summary>
    /// Load the per-layer weight bundles for SA1, SA2, SA3 out of the
    /// provided WeightReader (loaded from kintsugi.bin). Throws if any
    /// expected tensor is missing.
    /// </summary>
    public static PointNetSetAbstractionPort.Weights[] LoadSaWeights(WeightReader reader)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        var configs = BuildConfigs();
        var weights = new PointNetSetAbstractionPort.Weights[configs.Length];
        for (int L = 0; L < configs.Length; L++)
        {
            int saNum = L + 1;
            int numMlp = configs[L].MlpOutChannels.Length;
            var w = new PointNetSetAbstractionPort.Weights
            {
                ConvWeights = new float[numMlp][],
                ConvBiases = new float[numMlp][],
                BnGammas = new float[numMlp][],
                BnBetas = new float[numMlp][],
                BnRunningMeans = new float[numMlp][],
                BnRunningVars = new float[numMlp][],
            };
            for (int K = 0; K < numMlp; K++)
            {
                w.ConvWeights[K] = reader.GetFloat32($"ae.pn2.sa{saNum}.mlp_convs.{K}.weight");
                w.ConvBiases[K] = TryGet(reader, $"ae.pn2.sa{saNum}.mlp_convs.{K}.bias");
                w.BnGammas[K] = reader.GetFloat32($"ae.pn2.sa{saNum}.mlp_bns.{K}.weight");
                w.BnBetas[K] = reader.GetFloat32($"ae.pn2.sa{saNum}.mlp_bns.{K}.bias");
                w.BnRunningMeans[K] = reader.GetFloat32($"ae.pn2.sa{saNum}.mlp_bns.{K}.running_mean");
                w.BnRunningVars[K] = reader.GetFloat32($"ae.pn2.sa{saNum}.mlp_bns.{K}.running_var");
            }
            weights[L] = w;
        }
        return weights;
    }

    /// <summary>
    /// Run the SA1->SA2->SA3 chain on a single fragment's point cloud.
    /// Inputs xyz [N, 3] row-major (channels-LAST -- this is what the
    /// parity script feeds; we transpose to channels-first internally).
    /// Returns per-layer outputs for parity validation.
    /// </summary>
    public sealed class EncoderForwardResult
    {
        public float[] Sa1Xyz, Sa1Points;     // shapes [3, 256] and [128, 256]
        public float[] Sa2Xyz, Sa2Points;     // [3, 128] and [256, 128]
        public float[] Sa3Xyz, Sa3Points;     // [3, 25]  and [512, 25]
        public float[] FinalFeatures;          // [64, 25] post-conv6 (optional, null if conv6 weights absent)
    }

    public static EncoderForwardResult RunEncoder(
        WeightReader reader,
        float[] pointCloudCl, int N)
    {
        if (pointCloudCl == null || pointCloudCl.Length < N * 3)
            throw new ArgumentException("point cloud too small");

        var configs = BuildConfigs();
        var weights = LoadSaWeights(reader);

        // Convert channels-last [N, 3] -> channels-first [3, N] for the
        // port's Forward signature.
        var xyzCf = new float[3 * N];
        for (int i = 0; i < N; i++)
        {
            xyzCf[0 * N + i] = pointCloudCl[i * 3 + 0];
            xyzCf[1 * N + i] = pointCloudCl[i * 3 + 1];
            xyzCf[2 * N + i] = pointCloudCl[i * 3 + 2];
        }

        var sa1 = new PointNetSetAbstractionPort(configs[0]);
        var (sa1Xyz, sa1Pts) = sa1.Forward(xyzCf, N, points: null, D: 0, weights[0]);

        var sa2 = new PointNetSetAbstractionPort(configs[1]);
        var (sa2Xyz, sa2Pts) = sa2.Forward(sa1Xyz, configs[0].Npoint, sa1Pts, 128, weights[1]);

        var sa3 = new PointNetSetAbstractionPort(configs[2]);
        var (sa3Xyz, sa3Pts) = sa3.Forward(sa2Xyz, configs[1].Npoint, sa2Pts, 256, weights[2]);

        var result = new EncoderForwardResult
        {
            Sa1Xyz = sa1Xyz, Sa1Points = sa1Pts,
            Sa2Xyz = sa2Xyz, Sa2Points = sa2Pts,
            Sa3Xyz = sa3Xyz, Sa3Points = sa3Pts,
        };

        // Optional: pn2.conv6 is a Conv1d(512, 64, kernel=1). Same as a
        // Linear(512 -> 64) applied per spatial position. Weight shape
        // after Conv1d 1x1 squeeze is [64, 512]; bias is [64].
        var conv6W = TryGet(reader, "ae.pn2.conv6.weight");
        var conv6B = TryGet(reader, "ae.pn2.conv6.bias");
        if (conv6W != null)
        {
            int K = configs[2].Npoint;  // 25
            // sa3Pts is [512, K] channels-first. Reshape to [K, 512].
            var sa3PtsCl = new float[K * 512];
            for (int k = 0; k < K; k++)
                for (int c = 0; c < 512; c++)
                    sa3PtsCl[k * 512 + c] = sa3Pts[c * K + k];

            // conv6W is [64, 512]; transpose to [512, 64] for MatMul.
            var conv6WT = new float[512 * 64];
            for (int o = 0; o < 64; o++)
                for (int k = 0; k < 512; k++)
                    conv6WT[k * 64 + o] = conv6W[o * 512 + k];

            var feat = new float[K * 64];
            Primitives.Matmul.MatMul(sa3PtsCl, conv6WT, feat, K, 512, 64);
            if (conv6B != null) Primitives.Matmul.AddBias(feat, conv6B, K, 64);
            // Channels-first [64, K] for parity comparison.
            var finalCf = new float[64 * K];
            for (int k = 0; k < K; k++)
                for (int c = 0; c < 64; c++)
                    finalCf[c * K + k] = feat[k * 64 + c];
            result.FinalFeatures = finalCf;
        }
        return result;
    }

    private static float[] TryGet(WeightReader r, string name)
    {
        foreach (var n in r.Names) if (n == name) return r.GetFloat32(name);
        return null;
    }
}
