#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Interfaces;

// =============================================================================
// BlockGraphColorer — assigns a colour to every block in a MasonryAssembly
// such that no two blocks sharing an interface (= adjacent in the contact
// graph) share a colour. Uses at most Δ+1 colours (Δ = maximum contact
// degree): the greedy Welsh-Powell algorithm always finds a free colour
// within Δ+1, machine-checked as `greedy_coloring_exists` in
// frahan_proofs/FrahanProofs/Coloring.lean. (A previous fixed cap of 8 was
// wrong: non-planar 3D masonry contact graphs can need more — a clique of
// 9 mutually-touching blocks needs 9 colours — and the code threw on them.
// The Appel-Haken 4-colour theorem covers PLANAR graphs only.)
//
// Use cases:
//   - Visualization: 4 distinct colours make wall structure scannable.
//   - Material distribution: alternate stone types across the wall so no
//     two adjacent stones share material (common in dimension-stone
//     aesthetics).
//   - Robotic assembly sequencing: blocks of the same colour are
//     non-adjacent → can be placed in parallel without collision.
//
// Algorithm: Welsh-Powell — sort vertices by descending degree, then
// greedy-colour, assigning the lowest colour not used by any already-
// coloured neighbour. For typical wall sizes (< 1000 blocks) this is
// instant. Reference: okaydemir/4-color-theorem (graph-coloring patterns).
// =============================================================================

public static class BlockGraphColorer
{
    /// <summary>
    /// Color the contact graph. Returns a dictionary
    /// <c>blockId → colourIndex</c> where colourIndex is in [0, NumColoursUsed).
    /// </summary>
    public static IReadOnlyDictionary<string, int> Color(MasonryAssembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));

        // Build adjacency: for each block, the set of blocks it shares an interface with.
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (int i = 0; i < assembly.Blocks.Count; i++)
        {
            adj[assembly.Blocks[i].Id] = new HashSet<string>(StringComparer.Ordinal);
        }
        for (int i = 0; i < assembly.Interfaces.Count; i++)
        {
            var iface = assembly.Interfaces[i];
            if (adj.ContainsKey(iface.BlockAId) && adj.ContainsKey(iface.BlockBId))
            {
                adj[iface.BlockAId].Add(iface.BlockBId);
                adj[iface.BlockBId].Add(iface.BlockAId);
            }
        }

        // Welsh-Powell vertex order: sort by descending degree.
        var order = new List<string>(adj.Keys);
        order.Sort((a, b) => adj[b].Count.CompareTo(adj[a].Count));

        // Palette sized to Δ+1 (max contact degree + 1). Greedy Welsh-Powell
        // always finds a free colour within Δ+1, so it never fails on a valid
        // contact graph (frahan_proofs Coloring.lean, greedy_coloring_exists).
        // For graphs of degree ≤ 7 this yields the exact same colouring as the
        // old fixed cap of 8 (greedy picks the lowest free colour, always < Δ+1).
        int palette = 1;
        foreach (var kv in adj)
            if (kv.Value.Count + 1 > palette) palette = kv.Value.Count + 1;

        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < order.Count; i++)
        {
            string id = order[i];
            var used = new bool[palette];
            foreach (var neighbour in adj[id])
            {
                if (color.TryGetValue(neighbour, out int nc) && nc < palette)
                    used[nc] = true;
            }
            int chosen = -1;
            for (int c = 0; c < palette; c++)
            {
                if (!used[c]) { chosen = c; break; }
            }
            if (chosen < 0)
                throw new InvalidOperationException(
                    "Block graph colouring failed unexpectedly (Δ+1 palette " +
                    "should always suffice).");
            color[id] = chosen;
        }
        return color;
    }
}
