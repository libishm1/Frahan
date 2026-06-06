#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// Wall3D — 3D extension of the Kim 2024 polygonal-masonry sequencing
// algorithm. Paper sec. 8 sketches the 3D version as future work
// (z = F(x, y) surfaces); this is a faithful structural extension.
//
// Scope split from the Python reference at
// Template-General/outputs/2026-05-20/polygonal_masonry_sequence/polygonal_masonry/wall3d.py:
// Python owns the geometric tessellation (scipy.spatial.Voronoi),
// C# owns the install-order DAG and reversed Kahn's. The 3D
// arrangement geometry (cell vertices, faces, adjacency) is provided
// by the caller. The C# side is intentionally agnostic to where the
// cells came from (Voronoi, prismatic stack, hand-built array, ...).
//
// The "above" rule generalises paper eq. (5)-(8): for each adjacent
// pair of cells, the cell whose representative point has the higher
// z-coordinate is the upper cell. Adjacent pairs whose z difference
// is below a threshold are treated as side-neighbours and impose no
// ordering (the 3D analogue of purely vertical 2D shared edges).
// =============================================================================

public sealed class Cell3D
{
    public int Id { get; }
    public (double X, double Y, double Z) Representative { get; }

    /// <summary>
    /// Optional. Triangular faces of the cell as flat (M*3, 3) vertex
    /// arrays. Only used by callers that want to render or inspect the
    /// cell; the DAG construction itself reads only `Id`,
    /// `Representative`, and the adjacency list passed to `Wall3D`.
    /// </summary>
    public IReadOnlyList<(double X, double Y, double Z)> Vertices { get; }

    public bool IsBounded { get; }

    public Cell3D(int id,
                  (double X, double Y, double Z) representative,
                  IReadOnlyList<(double X, double Y, double Z)> vertices = null,
                  bool isBounded = true)
    {
        Id = id;
        Representative = representative;
        Vertices = vertices;
        IsBounded = isBounded;
    }
}

public sealed class InstallationPlan3D
{
    public IReadOnlyDictionary<int, int> Depth { get; }
    public IReadOnlyDictionary<int, int> Order { get; }
    public IReadOnlyList<(int Low, int High)> DagEdges { get; }

    public InstallationPlan3D(Dictionary<int, int> depth,
                                Dictionary<int, int> order,
                                List<(int Low, int High)> edges)
    {
        Depth = depth;
        Order = order;
        DagEdges = edges;
    }
}

public sealed class Wall3D
{
    public IReadOnlyList<Cell3D> Cells { get; }
    public IReadOnlyList<(int A, int B)> Adjacency { get; }
    public HashSet<int> Holes { get; } = new();

    private readonly Dictionary<int, Cell3D> _byId;

    public Wall3D(IReadOnlyList<Cell3D> cells,
                  IReadOnlyList<(int A, int B)> adjacency)
    {
        if (cells == null) throw new ArgumentNullException(nameof(cells));
        if (adjacency == null) throw new ArgumentNullException(nameof(adjacency));
        Cells = cells;
        Adjacency = adjacency;
        _byId = new Dictionary<int, Cell3D>(cells.Count);
        foreach (var c in cells)
        {
            if (_byId.ContainsKey(c.Id))
            {
                throw new ArgumentException($"duplicate cell id {c.Id}", nameof(cells));
            }
            _byId[c.Id] = c;
        }
    }

    // ------------------------------------------------------------------
    // DAG construction (paper rule (5)-(8) generalised to z-axis)
    // ------------------------------------------------------------------

    public (Dictionary<int, List<int>> Graph, List<(int Low, int High)> Edges)
        BuildDag(double zThreshold = 1e-3)
    {
        if (zThreshold < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(zThreshold),
                "zThreshold must be non-negative");
        }
        var graph = new Dictionary<int, List<int>>();
        foreach (var c in Cells)
        {
            if (!c.IsBounded) continue;
            if (Holes.Contains(c.Id)) continue;
            graph[c.Id] = new List<int>();
        }
        var edges = new List<(int Low, int High)>();
        foreach (var (a, b) in Adjacency)
        {
            if (!_byId.TryGetValue(a, out var ca) ||
                !_byId.TryGetValue(b, out var cb))
            {
                continue;
            }
            if (!ca.IsBounded || !cb.IsBounded) continue;
            if (Holes.Contains(a) || Holes.Contains(b)) continue;
            double za = ca.Representative.Z;
            double zb = cb.Representative.Z;
            if (za + zThreshold < zb)
            {
                graph[a].Add(b);
                edges.Add((a, b));
            }
            else if (zb + zThreshold < za)
            {
                graph[b].Add(a);
                edges.Add((b, a));
            }
            // else: side neighbour, no ordering constraint.
        }
        return (graph, edges);
    }

    // ------------------------------------------------------------------
    // Top-level entry (reuses Wall's reversed Kahn's)
    // ------------------------------------------------------------------

    public InstallationPlan3D InstallSequence(double zThreshold = 1e-3)
    {
        var (graph, edges) = BuildDag(zThreshold);
        var depth = Wall.ReversedKahnDepths(graph);
        var keys = new List<int>(depth.Keys);
        keys.Sort((u, v) =>
        {
            int byDepth = depth[v].CompareTo(depth[u]);
            return byDepth != 0 ? byDepth : u.CompareTo(v);
        });
        var order = new Dictionary<int, int>();
        for (int i = 0; i < keys.Count; i++) order[keys[i]] = i + 1;
        return new InstallationPlan3D(depth, order, edges);
    }

    // ------------------------------------------------------------------
    // Hole insertion (3D analogue of paper sec. 5.4)
    // ------------------------------------------------------------------

    public void RemoveCells(IEnumerable<int> cellIds)
    {
        if (cellIds == null) return;
        foreach (int id in cellIds)
        {
            if (_byId.ContainsKey(id)) Holes.Add(id);
        }
    }

    // ------------------------------------------------------------------
    // Convenience: build adjacency from a list of pairs deduplicated
    // ------------------------------------------------------------------

    public static List<(int A, int B)> NormaliseAdjacency(
        IEnumerable<(int A, int B)> pairs)
    {
        if (pairs == null) throw new ArgumentNullException(nameof(pairs));
        var seen = new HashSet<(int, int)>();
        var result = new List<(int A, int B)>();
        foreach (var (a, b) in pairs)
        {
            if (a == b) continue;
            var key = a < b ? (a, b) : (b, a);
            if (seen.Add(key)) result.Add((key.Item1, key.Item2));
        }
        return result;
    }
}
