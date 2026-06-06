#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Cutting;

/// <summary>
/// Output of one or more <see cref="SlabCutter"/> operations: the resulting
/// slabs plus optional provenance (which input slab each output piece came
/// from, in input-list order). Useful for downstream callers that want to
/// preserve a "this fragment came from quarry block #5" relationship.
/// </summary>
public sealed class SlabCutResult
{
    public SlabCutResult(IReadOnlyList<Slab> slabs, IReadOnlyList<int> parentIndices)
    {
        Slabs = slabs ?? throw new ArgumentNullException(nameof(slabs));
        ParentIndices = parentIndices ?? Array.Empty<int>();
        if (parentIndices != null && parentIndices.Count != slabs.Count)
            throw new ArgumentException(
                $"parentIndices count {parentIndices.Count} != slabs count {slabs.Count}",
                nameof(parentIndices));
    }

    public IReadOnlyList<Slab> Slabs { get; }
    public IReadOnlyList<int> ParentIndices { get; }

    public int Count => Slabs.Count;

    /// <summary>Sum of signed volumes of all output slabs (sanity-check by comparing to input).</summary>
    public double TotalVolume()
    {
        double t = 0.0;
        for (int i = 0; i < Slabs.Count; i++) t += Slabs[i].SignedVolume();
        return t;
    }

    public override string ToString() =>
        $"SlabCutResult(n={Count}, totalVol={TotalVolume():0.###})";
}
