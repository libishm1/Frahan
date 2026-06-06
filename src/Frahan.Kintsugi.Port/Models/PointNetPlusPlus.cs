#nullable disable
using System;
using Frahan.Kintsugi.Port.Primitives;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// PointNet++ piece encoder, structural skeleton.
///
/// Translated from PuzzleFusion++'s encoder.py
/// (puzzlefusion_pp/models/encoder.py). Approach:
///   1. Sample K keypoints from each fragment's N-point cloud via FPS.
///   2. For each keypoint, gather its k-NN ball-neighbourhood.
///   3. Per-neighbourhood MLP (Conv1D collapses to per-point linear)
///      then max-pool over neighbours to produce one feature per
///      keypoint.
///   4. Stack a few set-abstraction layers (radius / k / MLP grow).
///   5. Optional feature-propagation back up (only used by some
///      configs; the denoiser uses keypoint features directly).
///
/// Output: [K, F] feature tensor per fragment.
///
/// THIS FILE IS A STRUCTURAL SKELETON. Weight loading + numerical
/// equivalence against PyTorch state-dict is Phase 7 work.
/// </summary>
public sealed class PointNetPlusPlus
{
    public sealed class SetAbstractionLayer
    {
        public int NumKeypoints;  // K_l
        public int NumNeighbours; // k_l (k-NN, not radius ball)
        public int InChannels;    // C_in
        public int OutChannels;   // C_out

        // MLP weights: a sequence of Linear layers + activations.
        // Shape: each layer is [in_features, out_features] row-major.
        public float[][] LinearWeights;
        public float[][] LinearBiases;

        // BatchNorm running stats per MLP layer (PuzzleFusion++'s
        // PointNet++ encoder applies BN between every Conv2d 1x1 and
        // the ReLU). All four arrays must have the same outer length
        // as LinearWeights; null entries skip BN for that layer.
        public float[][] BnGammas;
        public float[][] BnBetas;
        public float[][] BnRunningMeans;
        public float[][] BnRunningVars;

        public SetAbstractionLayer(int numKeypoints, int numNeighbours, int inChannels, int outChannels)
        {
            NumKeypoints = numKeypoints;
            NumNeighbours = numNeighbours;
            InChannels = inChannels;
            OutChannels = outChannels;
        }
    }

    private readonly SetAbstractionLayer[] _layers;

    /// <summary>
    /// Construct with a stack of set-abstraction layers. The
    /// PuzzleFusion++ encoder uses 3 layers, narrowing keypoints
    /// from 1024 → 256 → 64 → 25 and growing channels from 3 → 64
    /// → 128 → 256.
    /// </summary>
    public PointNetPlusPlus(SetAbstractionLayer[] layers)
    {
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
    }

    /// <summary>
    /// Forward pass. Input: pointCloud[3 * N] xyz triples. Optionally
    /// per-point features [C_in * N]; null means xyz-only (C_in = 3).
    /// Returns: keypoint positions [3 * K_last] + features [C_out_last * K_last].
    /// </summary>
    public (float[] keypoints, float[] features) Forward(float[] pointCloud, float[] perPointFeatures = null)
    {
        if (pointCloud == null) throw new ArgumentNullException(nameof(pointCloud));
        if (pointCloud.Length % 3 != 0) throw new ArgumentException("pointCloud length % 3 != 0.");
        // Convert float[] xyz to double[] for FPS (FPS uses double; could
        // be promoted to float in Phase 7 perf pass).
        double[] xyz = new double[pointCloud.Length];
        for (int i = 0; i < pointCloud.Length; i++) xyz[i] = pointCloud[i];
        float[] currentFeatures = perPointFeatures;
        int currentN = pointCloud.Length / 3;

        foreach (var layer in _layers)
        {
            // 1. FPS to layer.NumKeypoints.
            int[] kpIdx = Fps.Sample(xyz, layer.NumKeypoints, seedIndex: 0);
            double[] kpXyz = GatherXyz(xyz, kpIdx);

            // 2. k-NN: for each keypoint, find layer.NumNeighbours
            //    nearest points in the CURRENT cloud.
            int[] knn = Knn.NearestK(xyz, kpXyz, layer.NumNeighbours);

            // 3. Build a per-(keypoint, neighbour) feature tensor:
            //    relative xyz (xyz_neighbour - xyz_keypoint) concat
            //    with per-neighbour input features.
            //    Shape: [K, k_neighbours, C_in]
            //    C_in here = 3 (relative xyz) + (currentFeatures channels)
            int kNbr = layer.NumNeighbours;
            int kpCount = layer.NumKeypoints;
            int featCh = currentFeatures != null ? currentFeatures.Length / currentN : 0;
            int cIn = 3 + featCh;
            var nbrFeats = new float[kpCount * kNbr * cIn];
            for (int p = 0; p < kpCount; p++)
            {
                double cx = kpXyz[p * 3 + 0];
                double cy = kpXyz[p * 3 + 1];
                double cz = kpXyz[p * 3 + 2];
                for (int n = 0; n < kNbr; n++)
                {
                    int nIdx = knn[p * kNbr + n];
                    int off = (p * kNbr + n) * cIn;
                    nbrFeats[off + 0] = (float)(xyz[nIdx * 3 + 0] - cx);
                    nbrFeats[off + 1] = (float)(xyz[nIdx * 3 + 1] - cy);
                    nbrFeats[off + 2] = (float)(xyz[nIdx * 3 + 2] - cz);
                    for (int c = 0; c < featCh; c++)
                        nbrFeats[off + 3 + c] = currentFeatures[c * currentN + nIdx];
                }
            }

            // 4. MLP on each (keypoint, neighbour) row independently.
            //    Then max-pool over the k_neighbours axis to produce
            //    one feature vector per keypoint.
            //    nbrFeats viewed as [K * k_neighbours, cIn] for matmul.
            int totalRows = kpCount * kNbr;
            float[] mlpIn = nbrFeats;
            int mlpInCh = cIn;
            if (layer.LinearWeights != null)
            {
                for (int lyr = 0; lyr < layer.LinearWeights.Length; lyr++)
                {
                    int outCh = (lyr < layer.LinearWeights.Length - 1)
                        ? layer.LinearWeights[lyr].Length / mlpInCh
                        : layer.OutChannels;
                    var mlpOut = new float[totalRows * outCh];
                    Matmul.MatMul(mlpIn, layer.LinearWeights[lyr], mlpOut, totalRows, mlpInCh, outCh);
                    if (layer.LinearBiases != null && lyr < layer.LinearBiases.Length && layer.LinearBiases[lyr] != null)
                        Matmul.AddBias(mlpOut, layer.LinearBiases[lyr], totalRows, outCh);
                    // BatchNorm1d (eval mode) between Conv2d-1x1 and
                    // ReLU. Skipped if no running stats are loaded
                    // (this lets the skeleton run without BN weights
                    // during early integration).
                    if (layer.BnGammas != null && lyr < layer.BnGammas.Length
                        && layer.BnGammas[lyr] != null && layer.BnBetas != null && layer.BnBetas[lyr] != null
                        && layer.BnRunningMeans != null && layer.BnRunningMeans[lyr] != null
                        && layer.BnRunningVars != null && layer.BnRunningVars[lyr] != null)
                    {
                        BatchNorm1d.Apply(mlpOut, layer.BnGammas[lyr], layer.BnBetas[lyr],
                            layer.BnRunningMeans[lyr], layer.BnRunningVars[lyr], totalRows, outCh);
                    }
                    // ReLU between MLP layers (PuzzleFusion++ pattern;
                    // last layer is post-activation too in their config).
                    Activations.Relu(mlpOut);
                    mlpIn = mlpOut;
                    mlpInCh = outCh;
                }
            }

            // 5. Max-pool over k_neighbours axis. Output [K, C_out].
            var pooled = new float[kpCount * mlpInCh];
            for (int p = 0; p < kpCount; p++)
            {
                int dstRow = p * mlpInCh;
                for (int c = 0; c < mlpInCh; c++) pooled[dstRow + c] = float.NegativeInfinity;
                for (int n = 0; n < kNbr; n++)
                {
                    int srcRow = (p * kNbr + n) * mlpInCh;
                    for (int c = 0; c < mlpInCh; c++)
                    {
                        var v = mlpIn[srcRow + c];
                        if (v > pooled[dstRow + c]) pooled[dstRow + c] = v;
                    }
                }
            }

            // 6. Loop back: current cloud becomes the keypoints, current
            //    features become the pooled features.
            xyz = kpXyz;
            currentN = kpCount;
            currentFeatures = pooled;
        }

        // Final: return keypoint coords + features.
        var keypointsOut = new float[xyz.Length];
        for (int i = 0; i < xyz.Length; i++) keypointsOut[i] = (float)xyz[i];
        return (keypointsOut, currentFeatures);
    }

    private static double[] GatherXyz(double[] cloud, int[] indices)
    {
        var output = new double[indices.Length * 3];
        for (int i = 0; i < indices.Length; i++)
        {
            int src = indices[i] * 3;
            output[i * 3 + 0] = cloud[src + 0];
            output[i * 3 + 1] = cloud[src + 1];
            output[i * 3 + 2] = cloud[src + 2];
        }
        return output;
    }
}
