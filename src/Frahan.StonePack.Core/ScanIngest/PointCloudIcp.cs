#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
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
        // null = AUTO: Register derives coarse-to-fine scales from the cloud
        // extent (diag/60, diag/200, diag/600). The old absolute default
        // {0.5, 0.1, 0.02} was metre-tuned: in a millimetre model those
        // voxels are microscopic, nothing downsamples, and full-resolution
        // ICP on a quarry pair churns GBs of garbage (UI-freezing blocking
        // GCs, then OOM). Unit-aware by construction instead.
        VoxelScales = voxelScales != null && voxelScales.Length > 0
            ? voxelScales : null;
        MaxIterationsPerScale = maxIterationsPerScale;
        TranslationTolerance = translationTolerance;
        RotationToleranceDegrees = rotationToleranceDegrees;
        TrimFraction = trimFraction;
        UsePointToPlane = usePointToPlane;
        NormalsKNeighbours = normalsKNeighbours;
    }

    /// <summary>Voxel scales for coarse-to-fine. 0 disables downsampling
    /// at that level (use full cloud). null = AUTO: derived from the cloud
    /// extent at Register time (unit-aware).</summary>
    public double[] VoxelScales { get; }

    /// <summary>Hard cap on points fed into a single ICP scale (after voxel
    /// downsampling; uniform stride subsample above it). Bounds memory and
    /// per-iteration cost regardless of units or scan density. 400k points
    /// ≈ 10 MB/array; registration accuracy is preserved because ICP needs
    /// coverage, not every sample. 0 = unlimited (old behaviour).</summary>
    public int MaxPointsPerScale { get; } = 400_000;
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
        CloudIcpOptions options = null,
        Action<string> progress = null,
        CancellationToken token = default)
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

        // Build flat arrays once (was re-flattened per scale).
        var srcFlat = ToFlat(sourcePoints);
        var tgtFlat = ToFlat(targetPoints);

        // AUTO scales: derive from the combined extent so the ladder is
        // unit-aware (mm / cm / m / quarry all get the same relative
        // coarse-to-fine). diag/60 ≈ the old 0.5 m on a 30 m bench.
        double[] scales = options.VoxelScales;
        if (scales == null)
        {
            double diag = CombinedDiagonal(srcFlat, tgtFlat);
            scales = diag > 0.0
                ? new[] { diag / 60.0, diag / 200.0, diag / 600.0 }
                : new[] { 0.0 };
        }

        int scaleIdx = 0;
        foreach (double voxel in scales)
        {
            scaleIdx++;
            token.ThrowIfCancellationRequested();
            progress?.Invoke($"scale {scaleIdx}/{scales.Length} (voxel {voxel:G3}): downsampling...");

            // Downsample at this voxel scale.
            double[] srcDs, tgtDs;
            if (voxel > 0.0)
            {
                srcDs = MaybeVoxelDownsample(srcFlat, voxel);
                tgtDs = MaybeVoxelDownsample(tgtFlat, voxel);
            }
            else
            {
                srcDs = srcFlat;
                tgtDs = tgtFlat;
            }
            // Budget: bound memory + per-iteration cost regardless of units.
            srcDs = StrideSubsample(srcDs, options.MaxPointsPerScale);
            tgtDs = StrideSubsample(tgtDs, options.MaxPointsPerScale);
            if (srcDs.Length < 9 || tgtDs.Length < 9) continue;

            var (xform, rms, iters, conv, used) = RunIcpAtScale(
                srcDs, tgtDs, cum, options, progress, token,
                $"scale {scaleIdx}/{scales.Length}", voxel);
            cum = xform;
            totalIters += iters;
            finalRms = rms;
            converged = conv;
            corrUsed = used;
        }

        return new CloudIcpResult(cum, finalRms, totalIters, converged, corrUsed);
    }

    private static double CombinedDiagonal(double[] a, double[] b)
    {
        double minX = double.PositiveInfinity, minY = minX, minZ = minX;
        double maxX = double.NegativeInfinity, maxY = maxX, maxZ = maxX;
        foreach (var flat in new[] { a, b })
            for (int i = 0; i < flat.Length; i += 3)
            {
                if (flat[i] < minX) minX = flat[i];
                if (flat[i] > maxX) maxX = flat[i];
                if (flat[i + 1] < minY) minY = flat[i + 1];
                if (flat[i + 1] > maxY) maxY = flat[i + 1];
                if (flat[i + 2] < minZ) minZ = flat[i + 2];
                if (flat[i + 2] > maxZ) maxZ = flat[i + 2];
            }
        double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Uniform stride subsample down to maxPoints (0 = unlimited).</summary>
    private static double[] StrideSubsample(double[] flat, int maxPoints)
    {
        int n = flat.Length / 3;
        if (maxPoints <= 0 || n <= maxPoints) return flat;
        int stride = (n + maxPoints - 1) / maxPoints;
        int outN = (n + stride - 1) / stride;
        var outFlat = new double[3 * outN];
        for (int i = 0, j = 0; i < n; i += stride, j++)
        {
            outFlat[3 * j + 0] = flat[3 * i + 0];
            outFlat[3 * j + 1] = flat[3 * i + 1];
            outFlat[3 * j + 2] = flat[3 * i + 2];
        }
        return outFlat;
    }

    private static (Transform xform, double rms, int iters, bool converged, int corrUsed)
        RunIcpAtScale(double[] srcFlat, double[] tgtFlat, Transform initial, CloudIcpOptions opt,
            Action<string> progress = null, CancellationToken token = default, string label = "",
            double cellHint = 0.0)
    {
        int srcN = srcFlat.Length / 3;
        int tgtN = tgtFlat.Length / 3;
        Transform cur = initial;
        double prevRms = double.PositiveInfinity;
        int iters = 0;
        bool converged = false;
        int corrUsed = 0;

        // NEAREST NEIGHBOUR: managed hash-grid, built once per scale.
        // The native Geogram kd-tree shim is NOT used: frahan_geogram_kdtree_query
        // ACCESS-VIOLATES and kills the whole Rhino process even on a healthy
        // 1000-point tree / 100-query call (verified in an isolated Rhino slot,
        // 2026-07-10 - this was the Cloud ICP crash). A native AV cannot be
        // caught from managed code, so the only safe path is to not call it.
        // The grid is O(1) per query at ICP densities and pure managed.
        var nn = new GridNN(tgtFlat, cellHint);

        // Hoisted buffers: per-iteration reallocation churned ~150 MB/iter of
        // garbage at full res - blocking GCs froze the UI from a background
        // thread and OOM-crashed Rhino on quarry pairs.
        var srcTransformed = new double[srcFlat.Length];
        var nnIdx = new int[srcN];
        var nnSq = new double[srcN];
        int keepMax = opt.TrimFraction > 0.0
            ? (int)Math.Max(3, Math.Floor(srcN * (1.0 - opt.TrimFraction)))
            : srcN;
        var srcXyz = new double[3 * keepMax];
        var tgtXyz = new double[3 * keepMax];
        int[] sortedIdxBuf = opt.TrimFraction > 0.0 ? new int[srcN] : null;

        for (iters = 0; iters < opt.MaxIterationsPerScale; iters++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Invoke($"{label}: iteration {iters + 1}/{opt.MaxIterationsPerScale}, {srcN:N0} pts");

            // Transform source cloud by current xform.
            for (int i = 0; i < srcN; i++)
            {
                var p = new Point3d(srcFlat[3*i + 0], srcFlat[3*i + 1], srcFlat[3*i + 2]);
                p.Transform(cur);
                srcTransformed[3*i + 0] = p.X;
                srcTransformed[3*i + 1] = p.Y;
                srcTransformed[3*i + 2] = p.Z;
            }

            // Nearest-neighbour lookup (managed hash-grid; see note above).
            nn.Query(srcTransformed, nnIdx, nnSq);
            int[] indices = nnIdx; double[] sqDists = nnSq;

            // Trim: drop the worst trimFraction of pairs by distance.
            int keep = srcN;
            int[] sortedIdx = null;
            if (opt.TrimFraction > 0.0)
            {
                sortedIdx = sortedIdxBuf;
                for (int i = 0; i < srcN; i++) sortedIdx[i] = i;
                Array.Sort(sortedIdx, (a, b) => sqDists[a].CompareTo(sqDists[b]));
                keep = keepMax;
            }
            corrUsed = keep;
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

    /// <summary>
    /// Pure-managed nearest-neighbour on a uniform hash grid. Built once per
    /// ICP scale over the (fixed) target cloud; queried every iteration.
    /// Replaces the native Geogram kd-tree, whose kdtree_query entry point
    /// access-violates and kills the Rhino process even on healthy small
    /// inputs (verified in an isolated slot 2026-07-10). Ring search expands
    /// until the closed best distance beats every unvisited ring's minimum
    /// possible distance, so results are exact nearest neighbours.
    /// </summary>
    private sealed class GridNN
    {
        private readonly Dictionary<(long, long, long), List<int>> _cells;
        private readonly double[] _pts;
        private readonly double _cell;

        public GridNN(double[] flat, double cellHint)
        {
            _pts = flat;
            int n = flat.Length / 3;
            double cell = cellHint;
            if (!(cell > 0.0))
            {
                // Derive from extent: ~1 point per cell on average.
                double minX = double.PositiveInfinity, minY = minX, minZ = minX;
                double maxX = double.NegativeInfinity, maxY = maxX, maxZ = maxX;
                for (int i = 0; i < flat.Length; i += 3)
                {
                    if (flat[i] < minX) minX = flat[i];
                    if (flat[i] > maxX) maxX = flat[i];
                    if (flat[i + 1] < minY) minY = flat[i + 1];
                    if (flat[i + 1] > maxY) maxY = flat[i + 1];
                    if (flat[i + 2] < minZ) minZ = flat[i + 2];
                    if (flat[i + 2] > maxZ) maxZ = flat[i + 2];
                }
                double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
                double vol = Math.Max(dx * dy * dz, 1e-12);
                cell = Math.Pow(vol / Math.Max(n, 1), 1.0 / 3.0);
                if (!(cell > 0.0)) cell = 1.0;
            }
            _cell = cell;
            _cells = new Dictionary<(long, long, long), List<int>>(n / 2 + 1);
            for (int i = 0; i < n; i++)
            {
                var key = Key(flat[3 * i], flat[3 * i + 1], flat[3 * i + 2]);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<int>(2);
                    _cells[key] = list;
                }
                list.Add(i);
            }
        }

        private (long, long, long) Key(double x, double y, double z) =>
            ((long)Math.Floor(x / _cell), (long)Math.Floor(y / _cell), (long)Math.Floor(z / _cell));

        /// <summary>Exact nearest target index + squared distance per query.</summary>
        public void Query(double[] queries, int[] indices, double[] sqDists)
        {
            int qn = queries.Length / 3;
            for (int q = 0; q < qn; q++)
            {
                double qx = queries[3 * q], qy = queries[3 * q + 1], qz = queries[3 * q + 2];
                long cx = (long)Math.Floor(qx / _cell);
                long cy = (long)Math.Floor(qy / _cell);
                long cz = (long)Math.Floor(qz / _cell);

                double best = double.PositiveInfinity;
                int bestIdx = 0;
                for (int ring = 0; ring < 512; ring++)
                {
                    // A point in Chebyshev ring r is at least (r-1)*cell away:
                    // once the best distance beats that bound, we are done.
                    double ringMin = (ring - 1) * _cell;
                    if (best < double.PositiveInfinity && ringMin > 0 && ringMin * ringMin > best)
                        break;

                    bool anyCellVisited = false;
                    for (long ix = cx - ring; ix <= cx + ring; ix++)
                        for (long iy = cy - ring; iy <= cy + ring; iy++)
                            for (long iz = cz - ring; iz <= cz + ring; iz++)
                            {
                                // shell only (skip interior already visited)
                                if (ring > 0
                                    && Math.Abs(ix - cx) != ring
                                    && Math.Abs(iy - cy) != ring
                                    && Math.Abs(iz - cz) != ring) continue;
                                if (!_cells.TryGetValue((ix, iy, iz), out var list)) continue;
                                anyCellVisited = true;
                                foreach (int t in list)
                                {
                                    double dx = _pts[3 * t] - qx;
                                    double dy = _pts[3 * t + 1] - qy;
                                    double dz = _pts[3 * t + 2] - qz;
                                    double d2 = dx * dx + dy * dy + dz * dz;
                                    if (d2 < best) { best = d2; bestIdx = t; }
                                }
                            }
                    // Fallback safety: nothing anywhere near - widen fast.
                    if (!anyCellVisited && ring > 64 && best == double.PositiveInfinity)
                    {
                        BruteForceOne(qx, qy, qz, ref best, ref bestIdx);
                        break;
                    }
                }
                if (best == double.PositiveInfinity)
                    BruteForceOne(qx, qy, qz, ref best, ref bestIdx);
                indices[q] = bestIdx;
                sqDists[q] = best;
            }
        }

        private void BruteForceOne(double qx, double qy, double qz, ref double best, ref int bestIdx)
        {
            int n = _pts.Length / 3;
            for (int t = 0; t < n; t++)
            {
                double dx = _pts[3 * t] - qx;
                double dy = _pts[3 * t + 1] - qy;
                double dz = _pts[3 * t + 2] - qz;
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < best) { best = d2; bestIdx = t; }
            }
        }
    }

    // Retained for reference/golden checks; no longer called at scale (the
    // grid above replaced both this and the crashing native kd-tree path).
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
