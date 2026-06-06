#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.DataModel;

// =============================================================================
// MasonryAssembly — root data structure for the C# port of compas_cra.
//
// In compas_cra's CRA_Assembly the blocks live as nodes in a graph and the
// interfaces live as a per-edge "interfaces" list. Here we keep the two as
// flat lists; the equilibrium-matrix builder (Phase A.2) walks them in order
// to build the f-index table.
// =============================================================================

/// <summary>
/// Root data structure for a masonry stability problem: blocks +
/// interfaces + boundary conditions. Immutable after construction.
/// </summary>
public sealed class MasonryAssembly
{
    private readonly Dictionary<string, MasonryBlock> _blockById;

    public MasonryAssembly(
        IReadOnlyList<MasonryBlock> blocks,
        IReadOnlyList<MasonryInterface> interfaces,
        BoundaryConditions boundaryConditions)
    {
        if (blocks == null) throw new ArgumentNullException(nameof(blocks));
        if (interfaces == null) throw new ArgumentNullException(nameof(interfaces));
        if (boundaryConditions == null) throw new ArgumentNullException(nameof(boundaryConditions));

        _blockById = new Dictionary<string, MasonryBlock>(StringComparer.Ordinal);
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b == null)
                throw new ArgumentException($"blocks[{i}] is null", nameof(blocks));
            if (_blockById.ContainsKey(b.Id))
                throw new ArgumentException(
                    $"duplicate block id '{b.Id}' at index {i}",
                    nameof(blocks));
            _blockById.Add(b.Id, b);
        }

        for (int i = 0; i < interfaces.Count; i++)
        {
            var iface = interfaces[i];
            if (iface == null)
                throw new ArgumentException($"interfaces[{i}] is null", nameof(interfaces));
            if (!_blockById.ContainsKey(iface.BlockAId))
                throw new ArgumentException(
                    $"interfaces[{i}].BlockAId='{iface.BlockAId}' references an unknown block",
                    nameof(interfaces));
            if (!_blockById.ContainsKey(iface.BlockBId))
                throw new ArgumentException(
                    $"interfaces[{i}].BlockBId='{iface.BlockBId}' references an unknown block",
                    nameof(interfaces));
        }

        foreach (string fixedId in boundaryConditions.FixedBlockIds)
        {
            if (!_blockById.ContainsKey(fixedId))
                throw new ArgumentException(
                    $"boundaryConditions references unknown block id '{fixedId}'",
                    nameof(boundaryConditions));
        }

        Blocks = blocks;
        Interfaces = interfaces;
        BoundaryConditions = boundaryConditions;
    }

    public IReadOnlyList<MasonryBlock> Blocks { get; }
    public IReadOnlyList<MasonryInterface> Interfaces { get; }
    public BoundaryConditions BoundaryConditions { get; }

    public int BlockCount => Blocks.Count;
    public int InterfaceCount => Interfaces.Count;

    /// <summary>Blocks NOT marked as boundary conditions, in the order they appear in <see cref="Blocks"/>.</summary>
    public IEnumerable<MasonryBlock> FreeBlocks
    {
        get
        {
            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                if (!BoundaryConditions.IsFixed(b.Id))
                    yield return b;
            }
        }
    }

    public int FreeBlockCount => Blocks.Count - BoundaryConditions.FixedCount;

    public MasonryBlock GetBlock(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id must be non-blank", nameof(id));
        if (!_blockById.TryGetValue(id, out var b))
            throw new KeyNotFoundException($"no block with id '{id}'");
        return b;
    }

    public bool TryGetBlock(string id, out MasonryBlock block)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            block = null;
            return false;
        }
        return _blockById.TryGetValue(id, out block);
    }

    public override string ToString() =>
        $"MasonryAssembly(blocks={BlockCount}, free={FreeBlockCount}, interfaces={InterfaceCount})";
}
