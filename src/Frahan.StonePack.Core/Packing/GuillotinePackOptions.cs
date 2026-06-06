#nullable disable
using System;

namespace Frahan.Core.Packing;

// =============================================================================
// GuillotinePackOptions — knobs for TreePackForest (the Kim 2025
// tree/forest stone-block packer). Documented in
// wiki/papers/kim2025_tree_packing.md.
//
// Going beyond the paper (K1):
//   - Deterministic Seed (paper uses random(); we expose it so canvas
//     re-runs produce the same forest).
//   - KerfWidth (real saws lose material per cut; paper ignores this).
//
// K2 additions (memory `project_jalalian_bcsdbbv`):
//   - CutSurfaceWeight: optional Jalalian I11 cutting-surface-area cost
//     subtracted from the score. Defaults to 0 (Kim original behaviour).
//   - MaxDegreeOfParallelism: parallel forest growth via Parallel.For.
//     Defaults to processor count.
//   - MemoryBudgetBytes: optional cap on aggregate forest memory. When
//     non-zero, ForestCount is automatically reduced if the budget would
//     be exceeded, with a warning surfaced via the result.
// =============================================================================

/// <summary>Rotation policy for elements during placement attempts.</summary>
public enum GuillotineRotationMode
{
    /// <summary>Identity only; element orientation is fixed.</summary>
    None = 0,
    /// <summary>0° or 90° about one of x/y/z axes (chosen per element per
    /// forest). Useful when stone has a horizontal vein direction —
    /// 1-axis rotation keeps the vein aligned.</summary>
    OneAxis = 1,
    /// <summary>Six 90°-rotations from the proper subgroup of SO(3)
    /// (identity + 90° each axis + two two-axis compositions). Use when
    /// vein direction does not matter.</summary>
    ThreeAxis = 2,
}

public sealed class GuillotinePackOptions
{
    public GuillotinePackOptions(
        int forestCount = 256,
        long seed = 0,
        GuillotineRotationMode rotationMode = GuillotineRotationMode.None,
        double kerfWidth = 0.0,
        double cutSurfaceWeight = 0.0,
        int maxDegreeOfParallelism = 0,
        long memoryBudgetBytes = 0)
    {
        if (forestCount < 1)
            throw new ArgumentOutOfRangeException(nameof(forestCount),
                $"forest count must be >= 1, got {forestCount}");
        if (kerfWidth < 0.0 || double.IsNaN(kerfWidth) || double.IsInfinity(kerfWidth))
            throw new ArgumentOutOfRangeException(nameof(kerfWidth),
                $"kerf width must be a non-negative finite number, got {kerfWidth}");
        if (cutSurfaceWeight < 0.0 || double.IsNaN(cutSurfaceWeight) || double.IsInfinity(cutSurfaceWeight))
            throw new ArgumentOutOfRangeException(nameof(cutSurfaceWeight),
                $"cut surface weight must be a non-negative finite number, got {cutSurfaceWeight}");
        if (maxDegreeOfParallelism < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism),
                $"max degree of parallelism must be >= 0 (0 = auto), got {maxDegreeOfParallelism}");
        if (memoryBudgetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes),
                $"memory budget must be >= 0 (0 = unlimited), got {memoryBudgetBytes}");

        ForestCount = forestCount;
        Seed = seed;
        RotationMode = rotationMode;
        KerfWidth = kerfWidth;
        CutSurfaceWeight = cutSurfaceWeight;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;
        MemoryBudgetBytes = memoryBudgetBytes;
    }

    /// <summary>Number of independent randomised forests to grow. Paper
    /// shows score quality plateaus by f ≈ 50-1000 on small instances
    /// (45 elements / 4 containers). Default 256 is a reasonable
    /// canvas-time budget.</summary>
    public int ForestCount { get; }

    /// <summary>Master seed. Forest k uses seed + k for its RNG so
    /// individual forests are reproducible. Setting Seed = 0 is fine and
    /// gives a deterministic run.</summary>
    public long Seed { get; }

    public GuillotineRotationMode RotationMode { get; }

    /// <summary>Saw kerf width in model units. Each axis-aligned slab
    /// cut consumes this much material along the cut direction. Real
    /// values: 5-10 mm for diamond wire saws; 1-3 mm for thin blades.
    /// Paper assumes zero kerf, which silently over-estimates yield.</summary>
    public double KerfWidth { get; }

    /// <summary>K2 / Jalalian I11 (BCSdbBV) extension: weight applied to
    /// the total cut-surface area of placed elements when computing the
    /// score. The score subtracts <c>weight * Σ(internal-face area)</c>
    /// across all placements, where internal faces are the three element
    /// faces opposite to the placement corner (i.e. the faces that
    /// touch freshly cut slab boundaries, not the original block
    /// exterior). Default 0 keeps the original Kim 2025 behaviour.</summary>
    public double CutSurfaceWeight { get; }

    /// <summary>K2 parallel-forest extension. 0 = auto (use
    /// <see cref="Environment.ProcessorCount"/>). 1 forces serial
    /// execution (matches Kim §4 single-thread baseline). Each forest is
    /// independent and uses its own SplitMix64 RNG seeded by
    /// (Seed + forestIndex), so parallel results are bitwise identical
    /// to serial results.</summary>
    public int MaxDegreeOfParallelism { get; }

    /// <summary>K2 memory-budget cap. When non-zero, the
    /// <see cref="ForestCount"/> is automatically reduced so that
    /// <c>ForestCount * estimatedBytesPerForest</c> stays within the
    /// budget. Paper §5 reports ~1.76 GB for f = 10⁵ and 400 elements;
    /// inside Rhino's process that competes with the viewport, so
    /// canvas use typically wants a few hundred MB budget. 0 = unlimited.</summary>
    public long MemoryBudgetBytes { get; }
}
