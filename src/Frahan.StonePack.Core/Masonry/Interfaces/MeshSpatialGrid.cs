#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Interfaces;

// =============================================================================
// MeshSpatialGrid — third layer of spatial indexing in the proximity-detector
// stack. Hash-based uniform 3D grid over the mesh AABBs themselves, used as
// the broad-phase pair-discovery accelerator for very large assemblies.
//
// Indexing layers in MeshContactDetector (high-level overview):
//   1. Broad-phase, small N (<= GridThreshold meshes): sweep-and-prune by
//      min-X. O(N log N) sort + O(K) sweep.
//   2. Broad-phase, large N (> GridThreshold meshes): MeshSpatialGrid.
//      Insert each mesh into the cells its AABB overlaps; pair-discover by
//      iterating each cell's contents. O(N + cells × cell_size²); for
//      densely-packed walls with cells sized to the average block AABB,
//      cell_size² is ~constant → O(N) overall.
//   3. Narrow-phase, per pair: MeshBvh closest-point queries. O(log T).
//
// The grid is parameterised by its cell size, which the caller picks from
// the average mesh AABB extent. Cell counts grow as (extent / cellSize)³;
// for typical brick walls (1-100 m extent, 0.1-0.5 m blocks) the grid stays
// in the millions of cells worst-case which the dictionary-based storage
// handles fine.
// =============================================================================

/// <summary>
/// Hash-based uniform 3D grid over a list of MeshSnapshot AABBs. Used as
/// the broad-phase pair-discovery index for large assemblies where the
/// O(N log N) sweep-and-prune pass becomes the bottleneck.
/// </summary>
public sealed class MeshSpatialGrid
{
    private const int MaxCellsPerAabbAxis = 64;  // safety cap

    private readonly double _cellSize;
    private readonly Dictionary<long, List<int>> _cellToMeshIndices;

    public int MeshCount { get; }
    public double CellSize => _cellSize;
    public int OccupiedCells => _cellToMeshIndices.Count;

    public MeshSpatialGrid(IReadOnlyList<MeshSnapshot> meshes, double cellSize)
    {
        if (meshes == null) throw new ArgumentNullException(nameof(meshes));
        if (!(cellSize > 0.0))
            throw new ArgumentOutOfRangeException(nameof(cellSize), "must be > 0");

        _cellSize = cellSize;
        _cellToMeshIndices = new Dictionary<long, List<int>>();
        MeshCount = meshes.Count;

        for (int i = 0; i < meshes.Count; i++)
        {
            var m = meshes[i];
            int x0 = (int)Math.Floor(m.BBoxMinX / cellSize);
            int y0 = (int)Math.Floor(m.BBoxMinY / cellSize);
            int z0 = (int)Math.Floor(m.BBoxMinZ / cellSize);
            int x1 = (int)Math.Floor(m.BBoxMaxX / cellSize);
            int y1 = (int)Math.Floor(m.BBoxMaxY / cellSize);
            int z1 = (int)Math.Floor(m.BBoxMaxZ / cellSize);

            // Cap span per axis so a pathological huge-AABB / tiny-cell
            // combination can't blow up the dictionary.
            int sx = Math.Min(x1 - x0, MaxCellsPerAabbAxis - 1);
            int sy = Math.Min(y1 - y0, MaxCellsPerAabbAxis - 1);
            int sz = Math.Min(z1 - z0, MaxCellsPerAabbAxis - 1);

            for (int dz = 0; dz <= sz; dz++)
                for (int dy = 0; dy <= sy; dy++)
                    for (int dx = 0; dx <= sx; dx++)
                    {
                        long key = CellKey(x0 + dx, y0 + dy, z0 + dz);
                        if (!_cellToMeshIndices.TryGetValue(key, out var list))
                        {
                            list = new List<int>(4);
                            _cellToMeshIndices[key] = list;
                        }
                        list.Add(i);
                    }
        }
    }

    /// <summary>
    /// Enumerate candidate (i, j) pairs (i &lt; j) — all pairs whose AABBs
    /// share at least one grid cell. Each pair appears EXACTLY ONCE even
    /// if the pair shares multiple cells (caller doesn't have to dedup).
    /// </summary>
    public IEnumerable<(int I, int J)> CandidatePairs()
    {
        // We mark pairs as seen via a hashset. For huge assemblies this
        // can grow large, but it's bounded by the number of touching pairs
        // (= the number we'd test anyway).
        var seen = new HashSet<long>();
        foreach (var kv in _cellToMeshIndices)
        {
            var bucket = kv.Value;
            for (int a = 0; a < bucket.Count; a++)
            {
                for (int b = a + 1; b < bucket.Count; b++)
                {
                    int i = bucket[a], j = bucket[b];
                    if (i == j) continue;
                    if (i > j) (i, j) = (j, i);
                    long pairKey = ((long)i << 32) | (uint)j;
                    if (seen.Add(pairKey))
                        yield return (i, j);
                }
            }
        }
    }

    private static long CellKey(int x, int y, int z)
    {
        // Pack three ~21-bit signed coordinates into one 64-bit key.
        // Range per axis: roughly ±10⁶ cells, ample for any realistic input.
        unchecked
        {
            const long mask = (1L << 21) - 1;
            long ux = (long)(x + (1 << 20)) & mask;
            long uy = (long)(y + (1 << 20)) & mask;
            long uz = (long)(z + (1 << 20)) & mask;
            return (ux << 42) | (uy << 21) | uz;
        }
    }
}
