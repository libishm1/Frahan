#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// BlockBuildOrderer — produce a physically valid build order for a masonry
// assembly. A block is buildable once every block it sits on has already
// been placed.
//
// Algorithm:
//   1. Walk MasonryAssembly.Interfaces. For each contact, decide whether
//      it's a "bed joint" (normal aligned with the up vector within a
//      tolerance). A head joint or a vertical face contributes no
//      ordering constraint.
//   2. For each bed joint, the block on the +up side depends on the block
//      on the −up side. Build a DAG.
//   3. Kahn-style toposort. Among ready-to-place blocks, prefer the
//      lowest centroid·up; tiebreak by id (deterministic).
//   4. Layer (course number) = longest path from any source. Computed in
//      the same pass via DP on the toposort: layer[v] = max(layer[u]+1).
//   5. A cycle in the DAG means two blocks each support the other. That's
//      physically impossible; throw with the offending block ids.
//
// Convention: the interface normal points A → B (Frahan convention). So
// if (normal · up) > tol, B is above A → B depends on A → edge A → B.
// If (normal · up) < -tol, A is above B → A depends on B → edge B → A.
// =============================================================================

/// <summary>
/// One step in a build order: which block to place, in what order, on
/// what course (layer) of the assembly.
/// </summary>
public sealed class BuildStep
{
    public BuildStep(string blockId, int orderIndex, int layer)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            throw new ArgumentException("blockId must be non-blank", nameof(blockId));
        if (orderIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(orderIndex), "must be >= 0");
        if (layer < 0)
            throw new ArgumentOutOfRangeException(nameof(layer), "must be >= 0");

        BlockId = blockId;
        OrderIndex = orderIndex;
        Layer = layer;
    }

    public string BlockId { get; }
    public int OrderIndex { get; }
    public int Layer { get; }

    public override string ToString() =>
        $"BuildStep(id={BlockId}, order={OrderIndex}, layer={Layer})";
}

public static class BlockBuildOrderer
{
    private const double DefaultUpToleranceCos = 0.866;  // cos(30°)

    /// <summary>
    /// Produce a build order for the given assembly. Up vector defaults
    /// to world Z. <paramref name="upToleranceCos"/> is the minimum
    /// |normal · up| for an interface to count as a bed joint;
    /// 0.866 ≈ cos(30°), i.e. bed joints within 30° of horizontal.
    /// </summary>
    public static IReadOnlyList<BuildStep> Solve(
        MasonryAssembly assembly,
        double upX = 0.0, double upY = 0.0, double upZ = 1.0,
        double upToleranceCos = DefaultUpToleranceCos)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        double um = Math.Sqrt(upX * upX + upY * upY + upZ * upZ);
        if (um < 1e-12)
            throw new ArgumentException("up vector is degenerate", nameof(upX));
        if (!(upToleranceCos >= 0.0 && upToleranceCos <= 1.0))
            throw new ArgumentOutOfRangeException(
                nameof(upToleranceCos), "must be in [0, 1]");
        upX /= um; upY /= um; upZ /= um;

        int n = assembly.BlockCount;
        // Map id → index for graph storage.
        var idToIdx = new Dictionary<string, int>(n, StringComparer.Ordinal);
        for (int i = 0; i < n; i++) idToIdx[assembly.Blocks[i].Id] = i;

        var inDeg = new int[n];
        var adj = new List<List<int>>(n);
        for (int i = 0; i < n; i++) adj.Add(new List<int>(2));

        for (int e = 0; e < assembly.Interfaces.Count; e++)
        {
            var iface = assembly.Interfaces[e];
            double dot = iface.NormalX * upX + iface.NormalY * upY + iface.NormalZ * upZ;
            if (Math.Abs(dot) < upToleranceCos) continue;  // head joint / vertical

            int ia = idToIdx[iface.BlockAId];
            int ib = idToIdx[iface.BlockBId];
            int lower, upper;
            if (dot > 0) { lower = ia; upper = ib; }
            else         { lower = ib; upper = ia; }

            // Avoid duplicate edges (multiple bed joints between the same
            // pair would only inflate in-degree without changing the order).
            if (!adj[lower].Contains(upper))
            {
                adj[lower].Add(upper);
                inDeg[upper]++;
            }
        }

        // Centroid·up per block, used for the priority tiebreak.
        var elev = new double[n];
        for (int i = 0; i < n; i++)
        {
            var b = assembly.Blocks[i];
            int v = b.VertexCount;
            if (v == 0) { elev[i] = 0.0; continue; }
            double sx = 0, sy = 0, sz = 0;
            var coords = b.VertexCoordsXyz;
            for (int j = 0; j < v; j++)
            {
                sx += coords[3 * j + 0];
                sy += coords[3 * j + 1];
                sz += coords[3 * j + 2];
            }
            elev[i] = (sx * upX + sy * upY + sz * upZ) / v;
        }

        // Kahn with priority. We use a sorted set keyed by (elev, id) for
        // deterministic ordering.
        var ready = new SortedSet<(double E, string Id, int Idx)>(ReadyComparer.Instance);
        for (int i = 0; i < n; i++)
            if (inDeg[i] == 0)
                ready.Add((elev[i], assembly.Blocks[i].Id, i));

        var layer = new int[n];        // initialised 0
        var result = new List<BuildStep>(n);
        while (ready.Count > 0)
        {
            var top = ready.Min;
            ready.Remove(top);
            int v = top.Idx;
            result.Add(new BuildStep(assembly.Blocks[v].Id, result.Count, layer[v]));
            for (int k = 0; k < adj[v].Count; k++)
            {
                int u = adj[v][k];
                int newLayer = layer[v] + 1;
                if (newLayer > layer[u]) layer[u] = newLayer;
                if (--inDeg[u] == 0)
                    ready.Add((elev[u], assembly.Blocks[u].Id, u));
            }
        }

        if (result.Count != n)
        {
            // Cycle. Report the still-blocked block ids.
            var stuck = new List<string>();
            for (int i = 0; i < n; i++)
                if (inDeg[i] > 0) stuck.Add(assembly.Blocks[i].Id);
            throw new InvalidOperationException(
                "Build-order DAG has a cycle (mutual support). Stuck block ids: " +
                string.Join(", ", stuck));
        }

        return result;
    }

    private sealed class ReadyComparer : IComparer<(double E, string Id, int Idx)>
    {
        public static readonly ReadyComparer Instance = new ReadyComparer();
        public int Compare((double E, string Id, int Idx) a, (double E, string Id, int Idx) b)
        {
            int c = a.E.CompareTo(b.E);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Id, b.Id);
            if (c != 0) return c;
            return a.Idx.CompareTo(b.Idx);
        }
    }
}
