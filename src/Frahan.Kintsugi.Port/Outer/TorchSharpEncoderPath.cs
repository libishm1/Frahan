#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Kintsugi.Port.Weights;
using static TorchSharp.torch;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// Alternative encoder path using TorchSharp instead of the manual
/// C# port. Loads weights from the same FRKINTSU `kintsugi.bin` (the
/// converted PyTorch checkpoint) into a TorchSharp `nn.Module`,
/// runs forward in libtorch.
///
/// WHY KEEP BOTH PATHS?
/// - Manual port (PointNetSetAbstractionPort): fully managed, ~50 KB
///   total, no native deps. Required for net48 / restricted Rhino
///   loader contexts where bundling 200MB+ of libtorch DLLs is
///   prohibitive.
/// - TorchSharp path: drops libtorch (~200MB CPU / 2GB CUDA) into
///   the deploy folder; trades binary size for raw inference speed
///   and access to PyTorch's optimised CPU/CUDA kernels.
///
/// USAGE
///   var enc = new TorchSharpEncoderPath(reader);
///   var sa1, sa2, sa3 = enc.RunEncoder(pointCloud, N);
///
/// The manual port stays the default; this is opt-in.
///
/// LICENSE NOTE
/// TorchSharp itself is MIT-licensed. Bundling libtorch (BSD-3) is
/// compatible with the GPL-3.0 Kintsugi port path -- LGPL/BSD-3 +
/// GPL-3.0 combination is permitted for binary linking under the
/// GPL FAQ. The combined .gha would ship as GPL-3.0 in this case.
/// </summary>
public sealed class TorchSharpEncoderPath : IDisposable
{
    private readonly Dictionary<string, Tensor> _params = new Dictionary<string, Tensor>();
    private bool _disposed;

    /// <summary>
    /// Load the encoder's autoencoder weights (ae.pn2.*) into TorchSharp
    /// tensors. The manual EncoderWeightLoader's architecture constants
    /// apply: 3 SA layers with mlp=[64,64,128] / [128,128,256] / [256,256,512].
    /// </summary>
    public TorchSharpEncoderPath(WeightReader reader)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        foreach (var name in reader.Names)
        {
            if (!name.StartsWith("ae.")) continue;
            var raw = reader.GetFloat32(name);
            var shape = reader.GetShape(name);
            // Build a TorchSharp tensor with the same shape + dtype.
            long[] longShape = new long[shape.Length];
            for (int i = 0; i < shape.Length; i++) longShape[i] = shape[i];
            var t = tensor(raw, longShape, ScalarType.Float32);
            _params[name] = t;
        }
    }

    /// <summary>List of loaded parameter names (diagnostic).</summary>
    public IEnumerable<string> ParameterNames => _params.Keys;

    /// <summary>
    /// Run a single set-abstraction layer using TorchSharp's native
    /// operators (FPS via torch.cdist + argmax loop, ball-query via
    /// pairwise distance, Conv2d 1x1 + BatchNorm2d + ReLU + max-pool).
    /// </summary>
    /// <param name="xyz">Channels-first [3, N] flat row-major.</param>
    /// <param name="N">Number of points.</param>
    /// <param name="points">[D, N] or null.</param>
    /// <param name="D">Channels in `points`.</param>
    /// <param name="saIndex">Set-abstraction layer index 1, 2, or 3.</param>
    public (float[] newXyz, float[] newPoints, int K, int Cout) RunSA(
        float[] xyz, int N, float[] points, int D, int saIndex)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TorchSharpEncoderPath));
        if (saIndex < 1 || saIndex > 3) throw new ArgumentOutOfRangeException(nameof(saIndex));

        // Config per upstream pn2.py.
        var configs = new[]
        {
            (npoint: 256, radius: 0.2f, nsample: 32, mlp: new[] { 64, 64, 128 }),
            (npoint: 128, radius: 0.4f, nsample: 64, mlp: new[] { 128, 128, 256 }),
            (npoint: 25,  radius: 0.8f, nsample: 64, mlp: new[] { 256, 256, 512 }),
        };
        var (npoint, radius, nsample, mlp) = configs[saIndex - 1];

        // Build [B=1, 3, N] xyz tensor (channels-first).
        var xyzT = tensor(xyz, new long[] { 1, 3, N }, ScalarType.Float32);
        Tensor pointsT = null;
        if ((object)points != null)
            pointsT = tensor(points, new long[] { 1, D, N }, ScalarType.Float32);

        // Compute distance matrix and sample npoint via simple FPS.
        var xyzL = xyzT.permute(0, 2, 1).contiguous();  // [1, N, 3]
        var fpsIdx = FpsTorch(xyzL, npoint);            // [1, npoint] long
        var newXyzL = GatherXyz(xyzL, fpsIdx);          // [1, npoint, 3]

        // Ball query: pairwise distance, mask outside radius, take first nsample by index.
        var grouped = BallQueryAndGroup(xyzL, pointsT, newXyzL, radius, nsample, D);
        // grouped: [1, npoint, nsample, C_in] where C_in = 3 + (D if points else 0)

        // Apply MLP layers: Conv2d 1x1 + BatchNorm2d + ReLU.
        // Reshape to [1, C_in, nsample, npoint].
        var x = grouped.permute(0, 3, 2, 1).contiguous();
        int cIn = (int)x.shape[1];
        for (int lyr = 0; lyr < mlp.Length; lyr++)
        {
            int cOut = mlp[lyr];
            string baseName = $"ae.pn2.sa{saIndex}.mlp_convs.{lyr}";
            var w = _params[$"{baseName}.weight"];     // [Cout, Cin] (after squeeze)
            // Reshape to Conv2d weight [Cout, Cin, 1, 1].
            var w4 = w.reshape(cOut, cIn, 1, 1);
            var b = _params.ContainsKey($"{baseName}.bias")
                ? _params[$"{baseName}.bias"] : null;
            x = nn.functional.conv2d(x, w4, bias: b);
            // BatchNorm2d (running stats, eval mode).
            string bnName = $"ae.pn2.sa{saIndex}.mlp_bns.{lyr}";
            var gamma = _params[$"{bnName}.weight"];
            var beta  = _params[$"{bnName}.bias"];
            var rm    = _params[$"{bnName}.running_mean"];
            var rv    = _params[$"{bnName}.running_var"];
            x = nn.functional.batch_norm(x, rm, rv, gamma, beta, training: false, eps: 1e-5);
            x = nn.functional.relu(x);
            cIn = cOut;
        }
        // Max-pool over nsample axis (dim 2). Output [1, Cout, npoint].
        x = x.max(dim: 2).values;

        // newXyzL [1, npoint, 3] -> channels-first [1, 3, npoint].
        var newXyzCf = newXyzL.permute(0, 2, 1).contiguous();
        var newXyzArr = newXyzCf.data<float>().ToArray();
        var newPointsArr = x.data<float>().ToArray();
        return (newXyzArr, newPointsArr, npoint, cIn);
    }

    // Pure TorchSharp FPS: O(K*N). Matches our manual Fps.Sample with
    // seedIndex=0 (deterministic).
    private static Tensor FpsTorch(Tensor xyz, int K)
    {
        long N = xyz.shape[1];
        long B = xyz.shape[0];
        var selected = zeros(new long[] { B, K }, ScalarType.Int64);
        var dists = full(new long[] { B, N }, float.PositiveInfinity, ScalarType.Float32);
        var farthest = zeros(new long[] { B }, ScalarType.Int64);
        for (int i = 0; i < K; i++)
        {
            selected[TensorIndex.Colon, i] = farthest;
            // centroid: gather xyz[:, farthest, :] -> [B, 1, 3]
            var batchIdx = arange(B, ScalarType.Int64);
            var centroid = xyz[batchIdx, farthest].unsqueeze(1);   // [B, 1, 3]
            var diff = xyz - centroid;
            var d = (diff * diff).sum(-1);
            dists = minimum(dists, d);
            farthest = dists.argmax(-1);
        }
        return selected;
    }

    private static Tensor GatherXyz(Tensor xyz, Tensor idx)
    {
        // xyz [B, N, 3], idx [B, K] -> [B, K, 3] via index_select per batch.
        // For B=1 single-batch inference, this simplifies.
        return xyz.gather(1, idx.unsqueeze(-1).expand(idx.shape[0], idx.shape[1], 3));
    }

    private static Tensor BallQueryAndGroup(Tensor xyz, Tensor points,
                                              Tensor newXyz, float radius, int nsample, int D)
    {
        long B = xyz.shape[0];
        long N = xyz.shape[1];
        long S = newXyz.shape[1];
        // Pairwise squared distance [B, S, N].
        var sqrdists = cdist(newXyz, xyz).pow(2);
        // Build group_idx with the sort-by-index then mask trick.
        var groupIdx = arange(N, ScalarType.Int64).view(1, 1, N).expand(B, S, N).clone();
        var mask = sqrdists > radius * radius;
        groupIdx[mask] = N;  // sentinel
        // Sort ASCending by index value; in-radius (idx < N) come first.
        var (sorted, _) = groupIdx.sort(-1);
        var topK = sorted[TensorIndex.Colon, TensorIndex.Colon, TensorIndex.Slice(0, nsample)];
        // Replace sentinel N with first valid (topK[..., 0]) where mask hit.
        var groupFirst = topK[TensorIndex.Colon, TensorIndex.Colon, 0].unsqueeze(-1).expand(B, S, nsample).clone();
        var sentinelMask = topK == N;
        topK[sentinelMask] = groupFirst[sentinelMask];
        // Gather xyz at topK -> [B, S, nsample, 3].
        var groupedXyz = GatherForGroup(xyz, topK);
        // Subtract new_xyz: [B, S, 1, 3].
        groupedXyz = groupedXyz - newXyz.unsqueeze(2);
        if ((object)points != null)
        {
            // Permute points [B, D, N] -> [B, N, D] then gather.
            var pointsBND = points.permute(0, 2, 1).contiguous();
            var groupedPoints = GatherForGroup(pointsBND, topK);
            return cat(new[] { groupedXyz, groupedPoints }, dim: -1);
        }
        return groupedXyz;
    }

    private static Tensor GatherForGroup(Tensor src, Tensor idx)
    {
        // src [B, N, C], idx [B, S, K] -> [B, S, K, C] gathered.
        long B = src.shape[0], N = src.shape[1], C = src.shape[2];
        long S = idx.shape[1], K = idx.shape[2];
        var idxExp = idx.unsqueeze(-1).expand(B, S, K, C);
        var srcExp = src.unsqueeze(1).expand(B, S, N, C);
        return srcExp.gather(2, idxExp);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var t in _params.Values)
        {
            try { t?.Dispose(); } catch { }
        }
        _params.Clear();
        _disposed = true;
    }
}
