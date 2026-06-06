#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// FragmentMerger — agglomerate small fragments into their largest adjacent
// neighbour, returning a merge mapping. The geometry is NOT re-meshed
// (3D union is its own can of worms); the mapping describes which sliver
// merges into which host so downstream consumers can either
//
//   • discard the slivers (drop, lossy),
//   • lump the sliver's volume into the host's stability mass (cheap), or
//   • run a real CSG union if available (later, when a CSG kernel lands).
//
// Adjacency is supplied as a list of (i, j) index pairs from upstream
// contact detection. The merger walks fragments smallest-first and
// reassigns each one to its largest current host.
// =============================================================================

public sealed class FragmentMergeResult
{
    public FragmentMergeResult(
        int[] hostOf, double[] mergedVolume, int hostCount, int mergedCount)
    {
        HostOf = hostOf ?? throw new ArgumentNullException(nameof(hostOf));
        MergedVolume = mergedVolume ?? throw new ArgumentNullException(nameof(mergedVolume));
        HostCount = hostCount;
        MergedCount = mergedCount;
    }

    /// <summary>Per-input-piece, the index it ultimately merged into (self if it's a host).</summary>
    public int[] HostOf { get; }

    /// <summary>Per-input-piece: total volume after merge (host accumulates; non-hosts return 0).</summary>
    public double[] MergedVolume { get; }

    /// <summary>Distinct hosts in the result.</summary>
    public int HostCount { get; }

    /// <summary>Number of fragments that got merged into something else.</summary>
    public int MergedCount { get; }
}

public static class FragmentMerger
{
    public const double DefaultThresholdFraction = 1e-3;

    /// <summary>
    /// Merge fragments below <paramref name="thresholdFraction"/> · mean
    /// volume into their largest adjacent host. Walks smallest-first so a
    /// chain of slivers doesn't all collapse onto the very first host.
    /// </summary>
    public static FragmentMergeResult Merge(
        IReadOnlyList<Slab> pieces,
        IEnumerable<(int I, int J)> adjacency,
        double thresholdFraction = DefaultThresholdFraction)
    {
        if (pieces == null) throw new ArgumentNullException(nameof(pieces));
        if (adjacency == null) throw new ArgumentNullException(nameof(adjacency));
        if (thresholdFraction < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdFraction));

        int n = pieces.Count;
        var vols = new double[n];
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            vols[i] = Math.Abs(pieces[i].SignedVolume());
            total += vols[i];
        }
        double mean = n > 0 ? total / n : 0.0;
        double cutoff = thresholdFraction * mean;

        // Build adjacency lists.
        var nbrs = new List<List<int>>(n);
        for (int i = 0; i < n; i++) nbrs.Add(new List<int>(2));
        foreach (var (i, j) in adjacency)
        {
            if (i < 0 || i >= n) throw new ArgumentException($"adjacency index {i} out of range");
            if (j < 0 || j >= n) throw new ArgumentException($"adjacency index {j} out of range");
            if (i == j) continue;
            nbrs[i].Add(j);
            nbrs[j].Add(i);
        }

        var hostOf = new int[n];
        for (int i = 0; i < n; i++) hostOf[i] = i;
        var current = (double[])vols.Clone();

        // Smallest-first iteration. We rebuild the order each pass so freshly
        // grown hosts can absorb additional neighbours later.
        bool changed = true;
        while (changed)
        {
            changed = false;
            // Find the smallest fragment that's still its own host AND below cutoff.
            int target = -1;
            double targetVol = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (hostOf[i] != i) continue;
                if (current[i] < cutoff && current[i] < targetVol)
                {
                    target = i;
                    targetVol = current[i];
                }
            }
            if (target < 0) break;

            // Find the largest CURRENT host among neighbours (resolve via hostOf).
            int bestHost = -1;
            double bestVol = -1.0;
            for (int k = 0; k < nbrs[target].Count; k++)
            {
                int h = Resolve(hostOf, nbrs[target][k]);
                if (h == target) continue;
                if (current[h] > bestVol)
                {
                    bestVol = current[h];
                    bestHost = h;
                }
            }
            if (bestHost < 0)
            {
                // Isolated sliver. Mark "host of itself" but flag by setting
                // current to 0 so future iterations don't keep selecting it.
                current[target] = double.PositiveInfinity;
                continue;
            }

            // Merge target into bestHost.
            hostOf[target] = bestHost;
            current[bestHost] += current[target];
            current[target] = 0.0;
            // Inherit target's neighbours (so chains can keep accreting).
            for (int k = 0; k < nbrs[target].Count; k++)
            {
                int nb = nbrs[target][k];
                if (nb == bestHost) continue;
                if (!nbrs[bestHost].Contains(nb))
                    nbrs[bestHost].Add(nb);
            }
            changed = true;
        }

        // Final pass: collapse hostOf transitively.
        for (int i = 0; i < n; i++) hostOf[i] = Resolve(hostOf, i);

        var mergedVolume = new double[n];
        var seenHost = new HashSet<int>();
        int mergedCount = 0;
        for (int i = 0; i < n; i++)
        {
            int h = hostOf[i];
            if (h == i)
            {
                mergedVolume[i] = current[i] == double.PositiveInfinity ? vols[i] : current[i];
                seenHost.Add(i);
            }
            else
            {
                mergedCount++;
            }
        }
        return new FragmentMergeResult(hostOf, mergedVolume, seenHost.Count, mergedCount);
    }

    private static int Resolve(int[] hostOf, int i)
    {
        while (hostOf[i] != i) i = hostOf[i];
        return i;
    }
}
