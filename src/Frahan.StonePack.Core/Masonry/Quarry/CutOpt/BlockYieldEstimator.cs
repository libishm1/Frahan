#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// BlockYieldEstimator -- run BlockCutOpt per BenchBlock and produce a
// BlockYieldEstimate.
//
// Spec: wiki/specs/10_frahan_quarrycutopt_spec.md section 4 ("per-block
// GeoCut estimate (calls spec 09 internally)").
//
// Fracture risk is a deterministic proxy: the count of fracture triangles
// whose AABB overlaps the bench-block AABB, normalised by a reference count
// (FractureRiskNormalizer). Saturating at 1.0. Cheap, no Pareto search.
//
// Cutting-time estimate uses the perimeter-of-AABB-footprint proxy at the
// Shao 2022 feed-speed default (BlockCutOptTolerances).
// =============================================================================

public sealed class BlockYieldEstimatorOptions
{
    public BlockYieldEstimatorOptions(
        BlockCutOptOptions blockCutOpt,
        double fractureRiskNormalizer = 50.0,
        double feedSpeedMetresPerMin = -1.0)
    {
        BlockCutOpt = blockCutOpt ?? throw new ArgumentNullException(nameof(blockCutOpt));
        if (fractureRiskNormalizer <= 0)
            throw new ArgumentOutOfRangeException(nameof(fractureRiskNormalizer), "> 0");
        FractureRiskNormalizer = fractureRiskNormalizer;
        FeedSpeedMetresPerMin = feedSpeedMetresPerMin > 0
            ? feedSpeedMetresPerMin
            : BlockCutOptTolerances.MmToMetres(BlockCutOptTolerances.FeedSpeedMmPerMinDefault);
    }

    public BlockCutOptOptions BlockCutOpt { get; }
    public double FractureRiskNormalizer { get; }
    public double FeedSpeedMetresPerMin { get; }
}

public static class BlockYieldEstimator
{
    public static BlockYieldEstimate EstimateOne(
        BenchBlock block,
        PlyMesh fractures,
        BlockYieldEstimatorOptions options)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var result = BlockCutOptSolver.Solve(block.Footprint, fractures, options.BlockCutOpt);

        double blockVolume = options.BlockCutOpt.BlockSizeX * options.BlockCutOpt.BlockSizeY * options.BlockCutOpt.BlockSizeZ;
        double gross = block.GrossVolume;
        double recoverable = result.NonIntersectedCount * blockVolume * block.GeologyGrade;
        if (recoverable > gross) recoverable = gross;
        double waste = Math.Max(0.0, gross - recoverable);

        double risk = ComputeFractureRisk(block.Footprint, fractures, options.FractureRiskNormalizer);

        double timeMin = EstimateCuttingTimeMin(
            block.Footprint, result.NonIntersectedCount, options.FeedSpeedMetresPerMin);

        return new BlockYieldEstimate(
            block.Id,
            result.NonIntersectedCount,
            result.RecoveryPercent,
            risk,
            timeMin,
            recoverable,
            waste,
            result.BestPsiDeg);
    }

    public static IReadOnlyList<BlockYieldEstimate> EstimateAll(
        QuarryInventory inventory,
        PlyMesh fractures,
        BlockYieldEstimatorOptions options)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));

        var output = new List<BlockYieldEstimate>(inventory.Count);
        foreach (var b in inventory.Blocks)
        {
            output.Add(EstimateOne(b, fractures, options));
        }
        return output;
    }

    private static double ComputeFractureRisk(
        BoundingBox3 footprint,
        PlyMesh fractures,
        double normalizer)
    {
        int overlapCount = 0;
        int n = fractures.TriangleCount;
        var coords = fractures.VertexCoordsXyz;
        var tris = fractures.TriangleIndices;
        for (int t = 0; t < n; t++)
        {
            int a = tris[3 * t + 0], b = tris[3 * t + 1], c = tris[3 * t + 2];
            double ax = coords[3 * a], ay = coords[3 * a + 1], az = coords[3 * a + 2];
            double bx = coords[3 * b], by = coords[3 * b + 1], bz = coords[3 * b + 2];
            double cx = coords[3 * c], cy = coords[3 * c + 1], cz = coords[3 * c + 2];
            double xMin = Math.Min(ax, Math.Min(bx, cx));
            double xMax = Math.Max(ax, Math.Max(bx, cx));
            double yMin = Math.Min(ay, Math.Min(by, cy));
            double yMax = Math.Max(ay, Math.Max(by, cy));
            double zMin = Math.Min(az, Math.Min(bz, cz));
            double zMax = Math.Max(az, Math.Max(bz, cz));
            if (xMax < footprint.MinX || xMin > footprint.MaxX) continue;
            if (yMax < footprint.MinY || yMin > footprint.MaxY) continue;
            if (zMax < footprint.MinZ || zMin > footprint.MaxZ) continue;
            overlapCount++;
        }
        double risk = overlapCount / normalizer;
        if (risk > 1.0) risk = 1.0;
        if (risk < 0.0) risk = 0.0;
        return risk;
    }

    private static double EstimateCuttingTimeMin(
        BoundingBox3 footprint, int nonIntersectedCount, double feedSpeed)
    {
        if (nonIntersectedCount <= 0) return 0.0;
        double perimeter = 2.0 * (footprint.SizeX + footprint.SizeY);
        double totalPath = perimeter * nonIntersectedCount;
        return totalPath / Math.Max(feedSpeed, 1e-9);
    }
}
