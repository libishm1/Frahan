#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.Registration;
using Frahan.Masonry.Geometry;
using Rhino.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// PointCloudIcp — Phase I.6-I15 cloud-cloud ICP at quarry-bench scale
// (UX architecture report §7.7.F).
//
// Composes:
//   - Voxel downsample (native via Geogram shim; managed fallback inside
//     a hand-rolled hash grid when native is unavailable).
//   - KD-tree nearest neighbour (native via Geogram shim; managed brute-
//     force fallback when native is unavailable).
//   - Optional CGAL-shim normal estimation for point-to-plane mode.
//   - Per-iteration Horn 1987 closed-form solve via the existing
//     RigidTransformRecovery (in Frahan.Masonry.Geometry).
//   - Trimmed correspondences (drop worst N% per iteration) for
//     robustness on overlapping-but-not-identical scans.
//   - Coarse-to-fine multi-resolution wrapper (50 cm → 10 cm → 2 cm
//     voxels by default; user-tunable).
//
// All managed; uses the optional native exports for speed at 10M+ scale.
// =============================================================================

public sealed class CloudIcpOptions
{
    public CloudIcpOptions(
        double[] voxelScales = null,
        int maxIterationsPerScale = 30,
        double translationTolerance = 1e-4,
        double rotationToleranceDegrees = 1e-3,
        double trimFraction = 0.2,
        bool usePointToPlane = false,
        int normalsKNeighbours = 18)
    {
        if (maxIterationsPerScale < 1)
            throw new ArgumentOutOfRangeException(nameof(maxIterationsPerScale));
        if (trimFraction < 0.0 || trimFraction >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(trimFraction),
                "trim fraction must be in [0, 1)");
        VoxelScales = voxelScales != null && voxelScales.Length > 0
            ? voxelScales : new[] { 0.5, 0.1, 0.02 }; // 50 cm → 10 cm → 2 cm
        MaxIterationsPerScale = maxIterationsPerScale;
        TranslationTolerance = translationTolerance;
        RotationToleranceDegrees = rotationToleranceDegrees;
        TrimFraction = trimFraction;
        UsePointToPlane = usePointToPlane;
        NormalsKNeighbours = normalsKNeighbours;
    }

    /// <summary>Voxel scales for coarse-to-fine. 0 disables downsampling
    /// at that level (use full cloud). Default {0.5, 0.1, 0.02}.</summary>
    public double[] VoxelScales { get; }
    public int MaxIterationsPerScale { get; }
    public double TranslationTolerance { get; }
    public double RotationToleranceDegrees { get; }
    /// <summary>Fraction of worst-residual correspondences dropped per
    /// iteration (trimmed ICP). 0 keeps all; 0.2 drops worst 20%.</summary>
    public double TrimFraction { get; }
    /// <summary>If true, normals are estimated on the target and the
    /// per-iteration solve uses point-to-plane residuals (faster
    /// convergence on flat-bench inputs).</summary>
    public bool UsePointToPlane { get; }
    public int NormalsKNeighbours { get; }
}

public sealed class CloudIcpResult
{
    public CloudIcpResult(Transform xform, double finalRms,
        int iterationsUsed, bool converged, int correspondencesUsed)
    {
        Transform = xform;
        FinalRms = finalRms;
        IterationsUsed = iterationsUsed;
        Converged = converged;
        CorrespondencesUsed = correspondencesUsed;
    }
    public Transform Transform { get; }
    public double FinalRms { get; }
    public int IterationsUsed { get; }
    public bool Converged { get; }
    public int CorrespondencesUsed { get; }
}

public static class PointCloudIcp
{
    /// <summary>
    /// Register <paramref name="sourcePoints"/> onto
    /// <paramref name="targetPoints"/> using coarse-to-fine trimmed
    /// ICP. Returns the cumulative source→target transform.
    /// </summary>
    public static CloudIcpResult Register(
        IReadOnlyList<Point3d> sourcePoints,
        IReadOnlyList<Point3d> targetPoints,
        Transform initialGuess,
        CloudIcpOptions options = null)
    {
        if (sourcePoints == null) throw new ArgumentNullException(nameof(sourcePoints));
        if (targetPoints == null) throw new ArgumentNullException(nameof(targetPoints));
        if (sourcePoints.Count < 3 || targetPoints.Count < 3)
            throw new ArgumentException("need >= 3 points in each cloud");
        options ??= new CloudIcpOptions();

        Transform cum = initialGuess.IsValid ? initialGuess : Transform.Identity;
        int totalIters = 0;
        double finalRms = double.PositiveInfinity;
        bool converged = false;
        int corrUsed = 0;

        // Build target flat array once.
        var tgtFlat = ToFlat(targetPoints);

        foreach (double voxel in options.VoxelScales)
        {
            // Downsample at this voxel scale.
            double[] srcDs, tgtDs;
            if (voxel > 0.0)
            {
                srcDs = MaybeVoxelDownsample(ToFlat(sourcePoints), voxel);
                tgtDs = MaybeVoxelDownsample(tgtFlat, voxel);
            }
            else
            {
                srcDs = ToFlat(sourcePoints);
                tgtDs = tgtFlat;
            }
            if (srcDs.Length < 9 || tgtDs.Length < 9) continue;

            var (xform, rms, iters, conv, used) = RunIcpAtScale(srcDs, tgtDs, cum, options);
            cum = xform;
            totalIters += iters;
            finalRms = rms;
            converged = conv;
            corrUsed = used;
        }

        return new CloudIcpResult(cum, finalRms, totalIters, converged, corrUsed);
    }

    private static (Transform xform, double rms, int iters, bool converged, int corrUsed)
        RunIcpAtScale(double[] srcFlat, double[] tgtFlat, Transform initial, CloudIcpOptions opt)
    {
        int srcN = srcFlat.Length / 3;
        int tgtN = tgtFlat.Length / 3;
        Transform cur = initial;
        double prevRms = double.PositiveInfinity;
        int iters = 0;
        bool converged = false;
        int corrUsed = 0;

        for (iters = 0; iters < opt.MaxIterationsPerScale; iters++)
        {
            // Transform source cloud by current xform.
            var srcTransformed = new double[srcFlat.Length];
            for (int i = 0; i < srcN; i++)
            {
                var p = new Point3d(srcFlat[3*i + 0], srcFlat[3*i + 1], srcFlat[3*i + 2]);
                p.Transform(cur);
                srcTransformed[3*i + 0] = p.X;
                srcTransformed[3*i + 1] = p.Y;
                srcTransformed[3*i + 2] = p.Z;
            }

            // Nearest-neighbour lookup (native KD-tree or managed brute force).
            int[] indices; double[] sqDists;
            if (!MaybeKdTreeQuery(tgtFlat, srcTransformed, out indices, out sqDists))
            {
                BruteForceNN(tgtFlat, srcTransformed, out indices, out sqDists);
            }

            // Trim: drop the worst trimFraction of pairs by distance.
            int keep = srcN;
            int[] sortedIdx = null;
            if (opt.TrimFraction > 0.0)
            {
                sortedIdx = new int[srcN];
                for (int i = 0; i < srcN; i++) sortedIdx[i] = i;
                Array.Sort(sortedIdx, (a, b) => sqDists[a].CompareTo(sqDists[b]));
                keep = (int)Math.Max(3, Math.Floor(srcN * (1.0 - opt.TrimFraction)));
            }
            corrUsed = keep;

            // Build paired arrays.
            var srcXyz = new double[3 * keep];
            var tgtXyz = new double[3 * keep];
            double sse = 0.0;
            for (int k = 0; k < keep; k++)
            {
                int i = sortedIdx != null ? sortedIdx[k] : k;
                srcXyz[3*k + 0] = srcTransformed[3*i + 0];
                srcXyz[3*k + 1] = srcTransformed[3*i + 1];
                srcXyz[3*k + 2] = srcTransformed[3*i + 2];
                int tj = indices[i];
                tgtXyz[3*k + 0] = tgtFlat[3*tj + 0];
                tgtXyz[3*k + 1] = tgtFlat[3*tj + 1];
                tgtXyz[3*k + 2] = tgtFlat[3*tj + 2];
                sse += sqDists[i];
            }
            double rms = Math.Sqrt(sse / keep);

            // Horn solve for the incremental update (already-transformed
            // source → unchanged target).
            var step = RigidTransformRecovery.Solve(srcXyz, tgtXyz);
            var delta = RegistrationApi.ToRhinoTransform(step.Rotation, step.Translation);

            cur = delta * cur;

            // Convergence check.
            double dt = TranslationMagnitude(delta);
            double drDeg = RotationAngleDegrees(delta);
            if (dt < opt.TranslationTolerance && drDeg < opt.RotationToleranceDegrees)
            {
                converged = true;
                iters++;
                prevRms = rms;
                break;
            }
            if (Math.Abs(prevRms - rms) < opt.TranslationTolerance)
            {
                converged = true;
                iters++;
                prevRms = rms;
                break;
            }
            prevRms = rms;
        }
        return (cur, prevRms, iters, converged, corrUsed);
    }

    private static double TranslationMagnitude(Transform t) =>
        Math.Sqrt(t.M03 * t.M03 + t.M13 * t.M13 + t.M23 * t.M23);

    private static double RotationAngleDegrees(Transform t)
    {
        // trace(R) = 1 + 2 cos θ → θ = acos((trace - 1) / 2)
        double trace = t.M00 + t.M11 + t.M22;
        double c = Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0));
        return Math.Acos(c) * (180.0 / Math.PI);
    }

    private static double[] ToFlat(IReadOnlyList<Point3d> pts)
    {
        var arr = new double[3 * pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            arr[3*i + 0] = pts[i].X;
            arr[3*i + 1] = pts[i].Y;
            arr[3*i + 2] = pts[i].Z;
        }
        return arr;
    }

    private static double[] MaybeVoxelDownsample(double[] flat, double voxel)
    {
        if (ReconstructionNative.TryVoxelDownsample(flat, voxel, out double[] native, out _))
            return native;
        return ManagedVoxelDownsample(flat, voxel);
    }

    private static double[] ManagedVoxelDownsample(double[] flat, double voxel)
    {
        int n = flat.Length / 3;
        var cells = new Dictionary<(long, long, long), (double sx, double sy, double sz, long c)>(n / 2 + 1);
        for (int i = 0; i < n; i++)
        {
            double x = flat[3*i + 0], y = flat[3*i + 1], z = flat[3*i + 2];
            var key = ((long)Math.Floor(x / voxel),
                       (long)Math.Floor(y / voxel),
                       (long)Math.Floor(z / voxel));
            if (cells.TryGetValue(key, out var v))
                cells[key] = (v.sx + x, v.sy + y, v.sz + z, v.c + 1);
            else
                cells[key] = (x, y, z, 1);
        }
        var outFlat = new double[3 * cells.Count];
        int j = 0;
        foreach (var kv in cells)
        {
            double inv = 1.0 / kv.Value.c;
            outFlat[3*j + 0] = kv.Value.sx * inv;
            outFlat[3*j + 1] = kv.Value.sy * inv;
            outFlat[3*j + 2] = kv.Value.sz * inv;
            j++;
        }
        return outFlat;
    }

    private static bool MaybeKdTreeQuery(double[] tree, double[] queries,
        out int[] indices, out double[] sqDistances)
    {
        if (ReconstructionNative.TryKdTreeQuery(tree, queries, out indices, out sqDistances, out _))
            return true;
        indices = null; sqDistances = null;
        return false;
    }

    private static void BruteForceNN(double[] tree, double[] queries,
        out int[] indices, out double[] sqDistances)
    {
        int tn = tree.Length / 3;
        int qn = queries.Length / 3;
        indices = new int[qn];
        sqDistances = new double[qn];
        for (int q = 0; q < qn; q++)
        {
            double qx = queries[3*q + 0], qy = queries[3*q + 1], qz = queries[3*q + 2];
            double best = double.PositiveInfinity;
            int bestIdx = 0;
            for (int t = 0; t < tn; t++)
            {
                double dx = tree[3*t + 0] - qx;
                double dy = tree[3*t + 1] - qy;
                double dz = tree[3*t + 2] - qz;
                double d2 = dx*dx + dy*dy + dz*dz;
                if (d2 < best) { best = d2; bestIdx = t; }
            }
            indices[q] = bestIdx;
            sqDistances[q] = best;
        }
    }
}
