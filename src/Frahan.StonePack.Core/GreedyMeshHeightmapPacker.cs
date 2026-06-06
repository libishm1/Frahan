using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Core;

public sealed class GreedyMeshHeightmapPacker
{
    public MeshPackResult Pack(IEnumerable<MeshPackItem> inputItems, PackContainer container, MeshPackSettings settings)
    {
        if (inputItems == null) throw new ArgumentNullException(nameof(inputItems));
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (settings.CellSize <= 0) throw new ArgumentOutOfRangeException(nameof(settings.CellSize));

        var random = CreateRandom(settings);
        var items = inputItems.ToList();
        if (settings.SortByVolumeDescending)
        {
            items = OrderItems(items, settings, random);
        }

        var pile = new MeshPileHeightmap(container, settings.CellSize);
        var placements = new List<MeshPackPlacement>();
        var failures = new List<MeshPackFailure>();
        var sequence = 0;

        foreach (var item in items)
        {
            var candidate = FindBestPlacement(item, pile, settings, random);
            if (candidate == null)
            {
                failures.Add(new MeshPackFailure(item, "No mesh-heightmap candidate fit inside the container without vertical-column collision."));
                continue;
            }

            var placement = new MeshPackPlacement(
                item,
                candidate.Value.GeometryOrigin,
                candidate.Value.Map.OrientationBoundsMin,
                candidate.Value.Map.GeometrySize,
                candidate.Value.Map.YawDegrees,
                candidate.Value.Map.DownAxis,
                candidate.Value.Score,
                sequence++);

            placements.Add(placement);
            pile.Add(candidate.Value.Map, candidate.Value.CellX, candidate.Value.CellY, candidate.Value.Z);
        }

        return new MeshPackResult(placements, failures, pile, container);
    }

    public MeshPackResult Pack(IEnumerable<MeshPackItem> inputItems, IrregularMeshContainer container, MeshPackSettings settings)
    {
        if (inputItems == null) throw new ArgumentNullException(nameof(inputItems));
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (settings.CellSize <= 0) throw new ArgumentOutOfRangeException(nameof(settings.CellSize));

        var random = CreateRandom(settings);
        var items = inputItems.ToList();
        if (settings.SortByVolumeDescending)
        {
            items = OrderItems(items, settings, random);
        }

        var pile = new MeshPileHeightmap(container);
        var placements = new List<MeshPackPlacement>();
        var failures = new List<MeshPackFailure>();
        var sequence = 0;

        foreach (var item in items)
        {
            var candidate = FindBestPlacement(item, pile, settings, random);
            if (candidate == null)
            {
                failures.Add(new MeshPackFailure(item, "No mesh-heightmap candidate fit inside the irregular container footprint/heightmap."));
                continue;
            }

            var placement = new MeshPackPlacement(
                item,
                candidate.Value.GeometryOrigin,
                candidate.Value.Map.OrientationBoundsMin,
                candidate.Value.Map.GeometrySize,
                candidate.Value.Map.YawDegrees,
                candidate.Value.Map.DownAxis,
                candidate.Value.Score,
                sequence++);

            placements.Add(placement);
            pile.Add(candidate.Value.Map, candidate.Value.CellX, candidate.Value.CellY, candidate.Value.Z);
        }

        return new MeshPackResult(placements, failures, pile, container.Bounds);
    }

    private static Candidate? FindBestPlacement(MeshPackItem item, MeshPileHeightmap pile, MeshPackSettings settings, Random? random)
    {
        Candidate? best = null;
        var evaluated = 0;

        foreach (var map in MaybeShuffle(GetOrientations(item, settings).ToList(), random))
        {
            var maxX = pile.WidthCells - map.WidthCells;
            var maxY = pile.DepthCells - map.DepthCells;

            foreach (var cell in GetCandidateCells(maxX, maxY, random))
            {
                var x = cell.X;
                var y = cell.Y;
                if (++evaluated > settings.MaxCandidatesPerItem && best != null)
                {
                    return best;
                }

                if (!pile.TryGetLowestZ(map, x, y, out var z))
                {
                    continue;
                }

                var score = pile.ScorePlacement(map, x, y, z, settings);
                if (random != null && settings.RandomTieBreakWeight > 0)
                {
                    score += random.NextDouble() * settings.RandomTieBreakWeight;
                }

                var origin = new Vec3((x + map.PaddingCells) * pile.CellSize, (y + map.PaddingCells) * pile.CellSize, z);
                var candidate = new Candidate(map, x, y, z, origin, score);

                if (best == null || candidate.Score < best.Value.Score)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static IEnumerable<OrientedMeshHeightmap> GetOrientations(MeshPackItem item, MeshPackSettings settings)
    {
        if (settings.AllowAxisDownOrientations)
        {
            // 6-orientation set: each of the 3 local axes tilted down (None / X / Y)
            // crossed with yaw {0, 90}. Yaw 90 is skipped when the rotated footprint
            // is square (width == depth) to avoid evaluating a duplicate heightmap.
            foreach (var downAxis in new[] { 0, 1, 2 })
            {
                var map0 = OrientedMeshHeightmap.Build(item, downAxis, 0, settings.CellSize, settings.Clearance);
                yield return map0;

                if (Math.Abs(map0.GeometrySize.Width - map0.GeometrySize.Depth) > 1e-9)
                {
                    yield return OrientedMeshHeightmap.Build(item, downAxis, 90, settings.CellSize, settings.Clearance);
                }
            }

            yield break;
        }

        yield return OrientedMeshHeightmap.Build(item, 0, settings.CellSize, settings.Clearance);

        if (settings.AllowYaw90 && Math.Abs(item.Bounds.Size.Width - item.Bounds.Size.Depth) > 1e-9)
        {
            yield return OrientedMeshHeightmap.Build(item, 90, settings.CellSize, settings.Clearance);
        }
    }

    private static Random? CreateRandom(MeshPackSettings settings)
    {
        return settings.Seed == 0 ? null : new Random(settings.Seed);
    }

    private static List<MeshPackItem> OrderItems(List<MeshPackItem> items, MeshPackSettings settings, Random? random)
    {
        return random == null
            ? items.OrderByDescending(i => i.VolumeEstimate).ToList()
            : items.OrderByDescending(i => i.VolumeEstimate).ThenBy(_ => random.Next()).ToList();
    }

    private static IEnumerable<Cell> GetCandidateCells(int maxX, int maxY, Random? random)
    {
        if (random == null)
        {
            return EnumerateCandidateCells(maxX, maxY);
        }

        var cells = new List<Cell>();
        for (var x = 0; x <= maxX; x++)
        {
            for (var y = 0; y <= maxY; y++)
            {
                cells.Add(new Cell(x, y));
            }
        }

        return MaybeShuffle(cells, random);
    }

    private static IEnumerable<Cell> EnumerateCandidateCells(int maxX, int maxY)
    {
        for (var x = 0; x <= maxX; x++)
        {
            for (var y = 0; y <= maxY; y++)
            {
                yield return new Cell(x, y);
            }
        }
    }

    private static List<T> MaybeShuffle<T>(List<T> values, Random? random)
    {
        if (random == null)
        {
            return values;
        }

        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }

        return values;
    }

    private readonly struct Cell
    {
        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
    }

    private readonly struct Candidate
    {
        public Candidate(OrientedMeshHeightmap map, int cellX, int cellY, double z, Vec3 geometryOrigin, double score)
        {
            Map = map;
            CellX = cellX;
            CellY = cellY;
            Z = z;
            GeometryOrigin = geometryOrigin;
            Score = score;
        }

        public OrientedMeshHeightmap Map { get; }
        public int CellX { get; }
        public int CellY { get; }
        public double Z { get; }
        public Vec3 GeometryOrigin { get; }
        public double Score { get; }
    }
}
