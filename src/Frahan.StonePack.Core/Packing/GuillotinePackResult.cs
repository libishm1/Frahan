#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Packing;

// =============================================================================
// GuillotinePackResult — output of TreePackForest. One result per Pack()
// call. Carries the winning forest's placements, the score, the index of
// the best-scoring forest, and per-element rotation transforms.
// =============================================================================

/// <summary>
/// One element placement: which container it landed in, the world-frame
/// transform to apply to the source element, and the placed AABB.
/// </summary>
public readonly struct GuillotinePlacement
{
    public GuillotinePlacement(int elementIndex, int containerIndex,
        Box placedBox, Transform transform)
    {
        ElementIndex = elementIndex;
        ContainerIndex = containerIndex;
        PlacedBox = placedBox;
        Transform = transform;
    }

    /// <summary>Index into the original input element list.</summary>
    public int ElementIndex { get; }
    /// <summary>Index into the original input container list.</summary>
    public int ContainerIndex { get; }
    /// <summary>World-frame axis-aligned bounding box of the placed
    /// element (already rotated and translated).</summary>
    public Box PlacedBox { get; }
    /// <summary>Transform mapping the source element (placed at world
    /// origin in default orientation) to its final pose.</summary>
    public Transform Transform { get; }
}

public sealed class GuillotinePackResult
{
    public GuillotinePackResult(
        int bestForestIndex,
        double score,
        IReadOnlyList<GuillotinePlacement> placements,
        IReadOnlyList<int> usedContainerIndices,
        bool allElementsPacked,
        int forestsRun,
        long seedUsed)
    {
        BestForestIndex = bestForestIndex;
        Score = score;
        Placements = placements ?? Array.Empty<GuillotinePlacement>();
        UsedContainerIndices = usedContainerIndices ?? Array.Empty<int>();
        AllElementsPacked = allElementsPacked;
        ForestsRun = forestsRun;
        SeedUsed = seedUsed;
    }

    /// <summary>Index of the forest that produced this result
    /// (0 ≤ index &lt; ForestsRun). Useful for reproducing the run.</summary>
    public int BestForestIndex { get; }

    /// <summary>Score formula from Kim 2025 §2.4: sum of packed element
    /// values, plus a φ(containerPrice) = 1/(1+price) bonus when all
    /// elements fit.</summary>
    public double Score { get; }

    public IReadOnlyList<GuillotinePlacement> Placements { get; }

    /// <summary>Sorted unique container indices that hold at least one
    /// placed element.</summary>
    public IReadOnlyList<int> UsedContainerIndices { get; }

    public bool AllElementsPacked { get; }
    public int ForestsRun { get; }
    public long SeedUsed { get; }
}
