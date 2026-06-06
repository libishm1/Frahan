#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.GeoCut;

// =============================================================================
// SlabYieldOptimizer -- spec 09 section 2: pick the SlabPlan that maximises
// per-block yield, optionally penalised by crack conflicts.
//
// Algorithm:
//   for each candidate SlabPlan:
//     - count integer slabs that fit along the axis (kerf-aware);
//     - compute slab volume = thickness * face area;
//     - subtract crack conflicts (number of FracturePlanes whose normal
//       aligns with the slab axis within tolerance and whose offset lies
//       inside the block extent);
//     - yield = slab_count * slab_volume / block_volume;
//     - score = yield - conflictPenalty * conflictCount.
//   return the SlabPlan with the highest score.
//
// "Slab Yield" here is per-block; the upstream QuarryCutOpt layer aggregates
// across blocks. Compatible with the existing SlabCutter / FracturePlane
// machinery; produces a SlabPlan that the caller can hand to SlabCutter.
// =============================================================================

public sealed class SlabYieldResult
{
    public SlabYieldResult(
        SlabPlan plan, int slabCount, double yieldFraction,
        int conflictCount, double score)
    {
        if (slabCount < 0) throw new ArgumentOutOfRangeException(nameof(slabCount));
        if (yieldFraction < 0) throw new ArgumentOutOfRangeException(nameof(yieldFraction));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        SlabCount = slabCount;
        YieldFraction = yieldFraction;
        ConflictCount = conflictCount;
        Score = score;
    }

    public SlabPlan Plan { get; }
    public int SlabCount { get; }
    public double YieldFraction { get; }
    public int ConflictCount { get; }
    public double Score { get; }

    public override string ToString() =>
        $"SlabYield({Plan}, N={SlabCount}, yield={YieldFraction:0.00}, conflicts={ConflictCount}, score={Score:0.000})";
}

public sealed class SlabYieldOptimizerOptions
{
    public SlabYieldOptimizerOptions(
        IReadOnlyList<SlabPlan> candidates,
        double conflictPenalty = 0.05,
        double alignmentToleranceRad = 0.10)
    {
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        if (candidates.Count == 0) throw new ArgumentException("candidates required", nameof(candidates));
        if (conflictPenalty < 0) throw new ArgumentOutOfRangeException(nameof(conflictPenalty));
        if (alignmentToleranceRad < 0) throw new ArgumentOutOfRangeException(nameof(alignmentToleranceRad));
        ConflictPenalty = conflictPenalty;
        AlignmentToleranceRad = alignmentToleranceRad;
    }

    public IReadOnlyList<SlabPlan> Candidates { get; }
    public double ConflictPenalty { get; }
    public double AlignmentToleranceRad { get; }

    /// <summary>
    /// Convenience: three axis-aligned plans (X, Y, Z) at the same thickness.
    /// </summary>
    public static SlabYieldOptimizerOptions ThreeAxisAt(
        double thicknessMetres, double kerfMetres,
        double conflictPenalty = 0.05)
    {
        return new SlabYieldOptimizerOptions(
            new[]
            {
                new SlabPlan(SlabAxis.X, thicknessMetres, kerfMetres),
                new SlabPlan(SlabAxis.Y, thicknessMetres, kerfMetres),
                new SlabPlan(SlabAxis.Z, thicknessMetres, kerfMetres),
            },
            conflictPenalty);
    }
}

public static class SlabYieldOptimizer
{
    public static SlabYieldResult PickBest(
        Slab block,
        IReadOnlyList<FracturePlane> fractures,
        SlabYieldOptimizerOptions options)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));

        ComputeAabb(block, out double xMin, out double yMin, out double zMin,
                          out double xMax, out double yMax, out double zMax);
        double bx = xMax - xMin, by = yMax - yMin, bz = zMax - zMin;
        double blockVolume = bx * by * bz;
        if (!(blockVolume > 0))
            throw new ArgumentException("block has degenerate AABB", nameof(block));

        SlabYieldResult best = null;
        for (int i = 0; i < options.Candidates.Count; i++)
        {
            var plan = options.Candidates[i];
            double extent, otherA, otherB;
            switch (plan.Axis)
            {
                case SlabAxis.X: extent = bx; otherA = by; otherB = bz; break;
                case SlabAxis.Y: extent = by; otherA = bx; otherB = bz; break;
                case SlabAxis.Z: extent = bz; otherA = bx; otherB = by; break;
                default: throw new NotSupportedException(plan.Axis.ToString());
            }
            double pitch = plan.ThicknessMetres + plan.KerfMetres;
            int slabCount = pitch > 0 ? (int)Math.Floor((extent + plan.KerfMetres) / pitch) : 0;
            if (slabCount < 0) slabCount = 0;
            double slabVolume = plan.ThicknessMetres * otherA * otherB;
            double total = slabCount * slabVolume;
            double yieldFraction = total / blockVolume;
            int conflicts = CountConflicts(plan.Axis, fractures, options.AlignmentToleranceRad,
                xMin, yMin, zMin, xMax, yMax, zMax);
            double score = yieldFraction - options.ConflictPenalty * conflicts;

            var r = new SlabYieldResult(plan, slabCount, yieldFraction, conflicts, score);
            if (best == null || r.Score > best.Score)
            {
                best = r;
            }
        }
        return best;
    }

    private static int CountConflicts(
        SlabAxis axis, IReadOnlyList<FracturePlane> fractures, double tolRad,
        double xMin, double yMin, double zMin,
        double xMax, double yMax, double zMax)
    {
        // axis unit vector
        double ax = 0, ay = 0, az = 0;
        switch (axis) { case SlabAxis.X: ax = 1; break; case SlabAxis.Y: ay = 1; break; case SlabAxis.Z: az = 1; break; }

        double cosLimit = Math.Cos(tolRad);
        int count = 0;
        for (int i = 0; i < fractures.Count; i++)
        {
            var f = fractures[i];
            // alignment: |dot(n, axis)| close to 1
            double dot = Math.Abs(f.NormalX * ax + f.NormalY * ay + f.NormalZ * az);
            if (dot < cosLimit) continue;
            // proximity: plane point inside block AABB
            if (f.PointX < xMin || f.PointX > xMax) continue;
            if (f.PointY < yMin || f.PointY > yMax) continue;
            if (f.PointZ < zMin || f.PointZ > zMax) continue;
            count++;
        }
        return count;
    }

    private static void ComputeAabb(
        Slab s,
        out double xMin, out double yMin, out double zMin,
        out double xMax, out double yMax, out double zMax)
    {
        var v = s.VertexCoordsXyz;
        xMin = double.PositiveInfinity; yMin = double.PositiveInfinity; zMin = double.PositiveInfinity;
        xMax = double.NegativeInfinity; yMax = double.NegativeInfinity; zMax = double.NegativeInfinity;
        int n = s.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double x = v[3 * i + 0], y = v[3 * i + 1], z = v[3 * i + 2];
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            if (z < zMin) zMin = z; if (z > zMax) zMax = z;
        }
    }

    /// <summary>
    /// Convert a SlabPlan + parent Slab AABB into the FracturePlane list that
    /// SlabCutter would consume to actually slice the block. One plane per
    /// slab boundary (kerf-aware spacing).
    /// </summary>
    public static IReadOnlyList<FracturePlane> ToFracturePlanes(SlabPlan plan, Slab block)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (block == null) throw new ArgumentNullException(nameof(block));

        ComputeAabb(block, out double xMin, out double yMin, out double zMin,
                          out double xMax, out double yMax, out double zMax);
        double start, end;
        double nx = 0, ny = 0, nz = 0;
        switch (plan.Axis)
        {
            case SlabAxis.X: start = xMin; end = xMax; nx = 1; break;
            case SlabAxis.Y: start = yMin; end = yMax; ny = 1; break;
            case SlabAxis.Z: start = zMin; end = zMax; nz = 1; break;
            default: throw new NotSupportedException(plan.Axis.ToString());
        }
        double pitch = plan.ThicknessMetres + plan.KerfMetres;
        var output = new List<FracturePlane>();
        double offset = start + plan.ThicknessMetres;
        while (offset + plan.KerfMetres < end)
        {
            double px = (plan.Axis == SlabAxis.X) ? offset : 0.5 * (xMin + xMax);
            double py = (plan.Axis == SlabAxis.Y) ? offset : 0.5 * (yMin + yMax);
            double pz = (plan.Axis == SlabAxis.Z) ? offset : 0.5 * (zMin + zMax);
            output.Add(new FracturePlane(px, py, pz, nx, ny, nz));
            offset += pitch;
        }
        return output;
    }
}
