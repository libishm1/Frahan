#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.EdgeMatching
{
    // =========================================================================
    // SpatialHash3D -- uniform spatial hash for radius neighbour queries over a
    // FIXED point set. Rhino-free (flat double xyz array), so it is headless
    // unit-testable. Cell size = query radius, so every point within the radius
    // lies in the 27 cells around the query cell. QueryRadius returns SORTED
    // indices, which keeps the downstream float reduction order identical to a
    // linear scan (gotcha P1 determinism).
    //
    // V3 evolution batch 2: turns the soft-ICP / CPD q-bar O(N P^2) all-points
    // scan (SoftIcpRefiner.cs:316-330) into O(N P k), k = in-radius count << P.
    // Proven bit-identical + 2.95x on real ETH1100 geometry
    // (outputs/2026-06-04/algo_review_v3/evo_softicp/bench.py).
    // =========================================================================
    public sealed class SpatialHash3D
    {
        private readonly double[] _xyz;
        private readonly double _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly int _count;

        /// <summary>Build a hash over a flat [x0,y0,z0,x1,...] coordinate array. cellSize should
        /// be the query radius you will use (so neighbour scan touches only the 27 adjacent cells).</summary>
        public SpatialHash3D(double[] xyz, double cellSize)
        {
            _xyz = xyz ?? throw new ArgumentNullException(nameof(xyz));
            if (xyz.Length % 3 != 0) throw new ArgumentException("xyz length must be a multiple of 3");
            _cell = Math.Max(cellSize, 1e-12);
            _count = xyz.Length / 3;
            for (int i = 0; i < _count; i++)
            {
                long key = Key(C(xyz[3 * i]), C(xyz[3 * i + 1]), C(xyz[3 * i + 2]));
                if (!_cells.TryGetValue(key, out var lst)) { lst = new List<int>(4); _cells[key] = lst; }
                lst.Add(i);
            }
        }

        public int Count => _count;

        private int C(double v) => (int)Math.Floor(v / _cell);
        private static long Key(int x, int y, int z)
            => (((long)(x + 1_000_000) * 2_000_003L) + (y + 1_000_000)) * 2_000_003L + (z + 1_000_000);

        /// <summary>Indices of stored points within <paramref name="radius"/> of the query point,
        /// returned in ascending index order (deterministic). Use radius == cellSize for the
        /// 27-cell fast path.</summary>
        public List<int> QueryRadius(double px, double py, double pz, double radius)
        {
            double r2 = radius * radius;
            int cx = C(px), cy = C(py), cz = C(pz);
            int span = Math.Max(1, (int)Math.Ceiling(radius / _cell));
            var outl = new List<int>();
            for (int dx = -span; dx <= span; dx++)
            for (int dy = -span; dy <= span; dy++)
            for (int dz = -span; dz <= span; dz++)
            {
                if (!_cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var lst)) continue;
                for (int t = 0; t < lst.Count; t++)
                {
                    int i = lst[t];
                    double ex = _xyz[3 * i] - px, ey = _xyz[3 * i + 1] - py, ez = _xyz[3 * i + 2] - pz;
                    if (ex * ex + ey * ey + ez * ez <= r2) outl.Add(i);
                }
            }
            outl.Sort();
            return outl;
        }
    }
}
