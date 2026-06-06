#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// BuildOrderPartialAssembler — given a full MasonryAssembly and a build
// order, produce the sequence of partial assemblies that exist after
// step 1, step 2, ..., step N. Each partial assembly contains the first k
// blocks plus only those interfaces whose A and B sides are BOTH present.
//
// Used by stability streaming: feed each partial through the RBE solver to
// find the first step at which the in-progress assembly becomes unstable.
// That's the actionable answer — not whether the FINISHED assembly is
// stable, which is much weaker.
//
// Boundary conditions are inherited (the floor / fixed blocks are in every
// partial), so the very first step already has the ground-anchor it needs.
// =============================================================================

public static class BuildOrderPartialAssembler
{
    /// <summary>
    /// Yield one MasonryAssembly per build step. Step k contains the first
    /// (k+1) blocks from <paramref name="orderedBlockIds"/> plus any
    /// fixed-boundary blocks that aren't already in that prefix.
    /// </summary>
    public static IEnumerable<MasonryAssembly> EnumeratePartials(
        MasonryAssembly full, IReadOnlyList<string> orderedBlockIds)
    {
        if (full == null) throw new ArgumentNullException(nameof(full));
        if (orderedBlockIds == null) throw new ArgumentNullException(nameof(orderedBlockIds));
        for (int i = 0; i < orderedBlockIds.Count; i++)
            if (string.IsNullOrWhiteSpace(orderedBlockIds[i]))
                throw new ArgumentException($"orderedBlockIds[{i}] is blank", nameof(orderedBlockIds));

        // Validate the order references real blocks.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < orderedBlockIds.Count; i++)
        {
            if (!full.TryGetBlock(orderedBlockIds[i], out _))
                throw new ArgumentException(
                    $"orderedBlockIds[{i}]='{orderedBlockIds[i]}' not in assembly",
                    nameof(orderedBlockIds));
            if (!seen.Add(orderedBlockIds[i]))
                throw new ArgumentException(
                    $"orderedBlockIds has duplicate id '{orderedBlockIds[i]}'",
                    nameof(orderedBlockIds));
        }

        // Fixed blocks always present: anchor the partial against the ground.
        var fixedSet = new HashSet<string>(full.BoundaryConditions.FixedBlockIds, StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fixedSet) present.Add(f);

        for (int step = 0; step < orderedBlockIds.Count; step++)
        {
            present.Add(orderedBlockIds[step]);
            yield return BuildSubset(full, present, fixedSet);
        }
    }

    /// <summary>
    /// Single-shot construction of the partial at a specific step (0-based).
    /// </summary>
    public static MasonryAssembly BuildPartial(
        MasonryAssembly full, IReadOnlyList<string> orderedBlockIds, int step)
    {
        if (full == null) throw new ArgumentNullException(nameof(full));
        if (orderedBlockIds == null) throw new ArgumentNullException(nameof(orderedBlockIds));
        if (step < 0 || step >= orderedBlockIds.Count)
            throw new ArgumentOutOfRangeException(nameof(step),
                $"must be in [0, {orderedBlockIds.Count})");

        var fixedSet = new HashSet<string>(full.BoundaryConditions.FixedBlockIds, StringComparer.Ordinal);
        var present = new HashSet<string>(fixedSet, StringComparer.Ordinal);
        for (int i = 0; i <= step; i++)
        {
            if (!full.TryGetBlock(orderedBlockIds[i], out _))
                throw new ArgumentException(
                    $"orderedBlockIds[{i}]='{orderedBlockIds[i]}' not in assembly");
            present.Add(orderedBlockIds[i]);
        }
        return BuildSubset(full, present, fixedSet);
    }

    private static MasonryAssembly BuildSubset(
        MasonryAssembly full, HashSet<string> present, HashSet<string> fixedSet)
    {
        var blocks = new List<MasonryBlock>(present.Count);
        for (int i = 0; i < full.Blocks.Count; i++)
        {
            var b = full.Blocks[i];
            if (present.Contains(b.Id)) blocks.Add(b);
        }
        var ifaces = new List<MasonryInterface>(full.InterfaceCount);
        for (int i = 0; i < full.Interfaces.Count; i++)
        {
            var ifx = full.Interfaces[i];
            if (present.Contains(ifx.BlockAId) && present.Contains(ifx.BlockBId))
                ifaces.Add(ifx);
        }
        // Inherit only the fixed ids that are actually present in the partial.
        var partialFixed = new List<string>();
        foreach (var f in fixedSet)
            if (present.Contains(f)) partialFixed.Add(f);
        return new MasonryAssembly(blocks, ifaces, new BoundaryConditions(partialFixed));
    }
}
