#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// QuarryInventory -- aggregate of all BenchBlocks on a named bench.
//
// Spec: wiki/specs/10_frahan_quarrycutopt_spec.md section 3 (Frahan Quarry
// Inventory component).
// =============================================================================

public sealed class QuarryInventory
{
    public QuarryInventory(string benchId, IReadOnlyList<BenchBlock> blocks)
    {
        if (string.IsNullOrWhiteSpace(benchId)) throw new ArgumentException("benchId required", nameof(benchId));
        if (blocks == null) throw new ArgumentNullException(nameof(blocks));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b == null) throw new ArgumentException($"blocks[{i}] is null", nameof(blocks));
            if (!seen.Add(b.Id))
                throw new ArgumentException($"duplicate block id '{b.Id}'", nameof(blocks));
        }

        BenchId = benchId;
        Blocks = blocks;
    }

    public string BenchId { get; }
    public IReadOnlyList<BenchBlock> Blocks { get; }

    public int Count => Blocks.Count;
    public double TotalGrossVolume => Blocks.Sum(b => b.GrossVolume);
    public double WeightedAverageGrade =>
        TotalGrossVolume > 0
            ? Blocks.Sum(b => b.GrossVolume * b.GeologyGrade) / TotalGrossVolume
            : 0.0;

    public override string ToString() =>
        $"QuarryInventory({BenchId}, N={Count}, V={TotalGrossVolume:0.###} m3, grade={WeightedAverageGrade:0.00})";
}
