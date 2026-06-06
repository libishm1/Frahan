#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry.CutOpt;

namespace Frahan.Masonry.Quarry.GeoPack;

// =============================================================================
// BlockGraph -- partition of a bench volume into BlockCells, separated by the
// crack surfaces of a CrackGraph.
//
// In v0 each cell is a convex polyhedron (Slab). Contact tracking between
// cells is left as a stub (one Contact per shared crack id); a full
// face-adjacency map is deferred to v1.
// =============================================================================

public sealed class BlockCell
{
    public BlockCell(string id, Slab geometry, double uncertaintyBuffer = 0.0)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
        if (uncertaintyBuffer < 0) throw new ArgumentOutOfRangeException(nameof(uncertaintyBuffer));
        Id = id;
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        UncertaintyBuffer = uncertaintyBuffer;
    }

    public string Id { get; }
    public Slab Geometry { get; }
    public double UncertaintyBuffer { get; }

    public double Volume => Math.Abs(Geometry.SignedVolume());

    public override string ToString() => $"BlockCell({Id}, V={Volume:0.###} m^3)";
}

public sealed class BlockGraph
{
    public BlockGraph(IReadOnlyList<BlockCell> cells)
    {
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == null) throw new ArgumentException($"cells[{i}] is null", nameof(cells));
            if (!seen.Add(cells[i].Id))
                throw new ArgumentException($"duplicate cell id '{cells[i].Id}'", nameof(cells));
        }
    }

    public IReadOnlyList<BlockCell> Cells { get; }
    public int Count => Cells.Count;
    public double TotalVolume
    {
        get
        {
            double t = 0.0;
            for (int i = 0; i < Cells.Count; i++) t += Cells[i].Volume;
            return t;
        }
    }

    public override string ToString() => $"BlockGraph(N={Count}, V={TotalVolume:0.###} m^3)";
}

public sealed class BlockCandidate
{
    public BlockCandidate(BlockCell parent, BenchBlock orientedBox, double uncertaintyBuffer = 0.0)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        OrientedBox = orientedBox ?? throw new ArgumentNullException(nameof(orientedBox));
        if (uncertaintyBuffer < 0) throw new ArgumentOutOfRangeException(nameof(uncertaintyBuffer));
        UncertaintyBuffer = uncertaintyBuffer;
    }

    public BlockCell Parent { get; }
    public BenchBlock OrientedBox { get; }
    public double UncertaintyBuffer { get; }

    public override string ToString() =>
        $"BlockCandidate({OrientedBox.Id} from cell {Parent.Id}, V={OrientedBox.GrossVolume:0.###} m^3)";
}
