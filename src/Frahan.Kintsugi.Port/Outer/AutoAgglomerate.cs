#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Kintsugi.Port.Models;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// Auto-agglomerative outer loop (PuzzleFusion++ Sec 3 main schedule).
///
/// Procedure:
///   1. For each fragment, encode the point cloud (PointNet++).
///   2. Run the SE(3) denoiser starting from a random / zero-pose
///      seed; iterate diffusion steps t = T → 0 with the denoiser
///      predicting residuals.
///   3. For each pair of fragments, run the Verifier on
///      (post-denoise transform candidate, point-match histogram).
///   4. Merge verified pairs (acceptance prob ≥ Threshold) into one
///      rigid group; opposing-surface point matches (dist &lt; 0.001,
///      antiparallel normals) get deleted; resample 1000 points via FPS.
///   5. Iterate steps 1-4 until everything is in one group OR
///      MaxRounds is reached (paper caps at 6).
///
/// Outputs per fragment: SE(3) transform (quaternion + translation).
///
/// Skeleton wires the orchestration; per-step calls dispatch to
/// PointNetPlusPlus, Se3Denoiser, Verifier. Weight loading is Phase 7.
/// </summary>
public sealed class AutoAgglomerate
{
    public sealed class Config
    {
        public int MaxRounds = 6;
        public int DiffusionTotalT = 1000;
        public int DiffusionStepsPerRound = 700;  // PuzzleFusion++'s "m"
    }

    public sealed class FragmentInput
    {
        public float[] PointCloud;  // [3 * 1000] xyz, normalised per paper
        public string Id;
    }

    public sealed class Result
    {
        public float[][] Transforms;  // per-fragment [4 quat + 3 trans]
        public bool[] Placed;
        public int RoundsUsed;
        public List<string> History = new List<string>();
    }

    private readonly Config _cfg;
    private readonly PointNetPlusPlus _encoder;
    private readonly Se3Denoiser _denoiser;
    private readonly Verifier _verifier;

    public AutoAgglomerate(
        Config cfg,
        PointNetPlusPlus encoder,
        Se3Denoiser denoiser,
        Verifier verifier)
    {
        _cfg = cfg ?? new Config();
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _denoiser = denoiser ?? throw new ArgumentNullException(nameof(denoiser));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    /// <summary>
    /// Run the agglomerative assembly. Output is per-fragment 7-D
    /// (quat + trans). First fragment anchored at identity per the
    /// paper convention.
    /// </summary>
    public Result Solve(
        IReadOnlyList<FragmentInput> fragments,
        Se3Denoiser.Weights denoiserW,
        Verifier.Weights verifierW)
    {
        if (fragments == null) throw new ArgumentNullException(nameof(fragments));
        int N = fragments.Count;
        var result = new Result
        {
            Transforms = new float[N][],
            Placed = new bool[N],
        };
        for (int i = 0; i < N; i++)
        {
            result.Transforms[i] = new float[7];
            result.Transforms[i][0] = 1f; // identity quaternion (w=1)
        }
        result.Placed[0] = true;

        // Cluster tracking: clusterOf[i] = id of group i currently belongs to.
        var clusterOf = new int[N];
        for (int i = 0; i < N; i++) clusterOf[i] = i;

        for (int round = 0; round < _cfg.MaxRounds; round++)
        {
            result.RoundsUsed = round + 1;
            // 1. Encode all fragments to keypoint features.
            //    Skeleton: assumes _encoder has weights loaded externally.
            var allFeatures = new float[N][];
            for (int i = 0; i < N; i++)
            {
                var (_, feats) = _encoder.Forward(fragments[i].PointCloud);
                allFeatures[i] = feats;
            }

            // 2. Denoiser: starting from current poses (or noise for
            //    unplaced), iterate t = T → 0.
            float[] poses = FlattenPoses(result.Transforms);
            for (int t = _cfg.DiffusionStepsPerRound; t >= 0; t--)
            {
                var flatFeats = FlattenFeatures(allFeatures);
                var residual = _denoiser.Forward(flatFeats, poses, N, t, denoiserW);
                for (int i = 0; i < poses.Length; i++) poses[i] -= residual[i];
            }
            UnpackPoses(poses, result.Transforms);

            // 3. Verify pairs. Accepted pairs flip placed flag.
            //    Skeleton stub: full pair-feature construction (point-
            //    match histogram) is Phase 5 work.
            int progress = 0;
            for (int i = 0; i < N; i++)
            {
                if (result.Placed[i]) continue;
                // Score against any already-placed fragment.
                for (int j = 0; j < N; j++)
                {
                    if (!result.Placed[j]) continue;
                    var pairFeats = BuildPairFeatures(i, j, result.Transforms);
                    float score = _verifier.Score(pairFeats, verifierW);
                    if (_verifier.Accept(score))
                    {
                        result.Placed[i] = true;
                        result.History.Add($"Round {round}: fragment {i} accepted to group via {j} (score {score:F3})");
                        progress++;
                        break;
                    }
                }
            }

            if (progress == 0) break;
            // Step 4 (point-match deletion + resample) is a per-pair
            // operation that requires the encoder to re-run on the
            // merged cluster -- Phase 6 detail. Skeleton omits.
        }
        return result;
    }

    // -------------------------------------------------------------------------

    private static float[] FlattenPoses(float[][] poses)
    {
        int N = poses.Length;
        var output = new float[N * 7];
        for (int i = 0; i < N; i++) Buffer.BlockCopy(poses[i], 0, output, i * 7 * sizeof(float), 7 * sizeof(float));
        return output;
    }

    private static void UnpackPoses(float[] flat, float[][] target)
    {
        for (int i = 0; i < target.Length; i++)
            Buffer.BlockCopy(flat, i * 7 * sizeof(float), target[i], 0, 7 * sizeof(float));
    }

    private static float[] FlattenFeatures(float[][] features)
    {
        if (features.Length == 0) return new float[0];
        int featDim = features[0].Length;
        var output = new float[features.Length * featDim];
        for (int i = 0; i < features.Length; i++)
            Buffer.BlockCopy(features[i], 0, output, i * featDim * sizeof(float), featDim * sizeof(float));
        return output;
    }

    private static float[] BuildPairFeatures(int i, int j, float[][] transforms)
    {
        // Stub. Real construction would compute point-correspondence
        // histogram + match count between fragment i's surface points
        // (under transforms[i]) and fragment j's (under transforms[j]).
        // Phase 5 work.
        var feats = new float[14];
        // Use transform deltas as a crude placeholder so the verifier
        // sees SOMETHING varying with the geometry.
        for (int k = 0; k < 7; k++)
        {
            feats[k] = transforms[i][k];
            feats[7 + k] = transforms[j][k];
        }
        return feats;
    }
}
