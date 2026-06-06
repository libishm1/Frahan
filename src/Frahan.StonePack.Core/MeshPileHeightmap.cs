using System;
using System.Collections.Generic;

namespace Frahan.Core;

public sealed class MeshPileHeightmap
{
    private readonly double[,] _top;
    private readonly bool[,] _allowed;
    private readonly double[,] _ceiling;
    private readonly List<ContainerHeightInterval>[,] _containerIntervals;
    private readonly List<HeightInterval>[,] _intervals;

    public MeshPileHeightmap(PackContainer container, double cellSize)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

        Container = container;
        CellSize = cellSize;
        WidthCells = Math.Max(1, (int)Math.Ceiling(container.Width / cellSize));
        DepthCells = Math.Max(1, (int)Math.Ceiling(container.Depth / cellSize));
        _top = new double[WidthCells, DepthCells];
        _allowed = new bool[WidthCells, DepthCells];
        _ceiling = new double[WidthCells, DepthCells];
        _containerIntervals = new List<ContainerHeightInterval>[WidthCells, DepthCells];
        _intervals = new List<HeightInterval>[WidthCells, DepthCells];

        for (var x = 0; x < WidthCells; x++)
        {
            for (var y = 0; y < DepthCells; y++)
            {
                _allowed[x, y] = true;
                _ceiling[x, y] = container.Height;
                _containerIntervals[x, y] = new List<ContainerHeightInterval>
                {
                    new ContainerHeightInterval(0, container.Height)
                };
            }
        }
    }

    public MeshPileHeightmap(IrregularMeshContainer container)
    {
        if (container == null) throw new ArgumentNullException(nameof(container));

        Container = container.Bounds;
        CellSize = container.CellSize;
        WidthCells = container.WidthCells;
        DepthCells = container.DepthCells;
        _top = new double[WidthCells, DepthCells];
        _allowed = new bool[WidthCells, DepthCells];
        _ceiling = new double[WidthCells, DepthCells];
        _containerIntervals = new List<ContainerHeightInterval>[WidthCells, DepthCells];
        _intervals = new List<HeightInterval>[WidthCells, DepthCells];

        for (var x = 0; x < WidthCells; x++)
        {
            for (var y = 0; y < DepthCells; y++)
            {
                _allowed[x, y] = container.IsAllowed(x, y);
                _top[x, y] = container.FloorAt(x, y);
                _ceiling[x, y] = container.CeilingAt(x, y);
                _containerIntervals[x, y] = container.IsAllowed(x, y)
                    ? new List<ContainerHeightInterval>(container.IntervalsAt(x, y))
                    : new List<ContainerHeightInterval>();
            }
        }
    }

    public PackContainer Container { get; }
    public double CellSize { get; }
    public int WidthCells { get; }
    public int DepthCells { get; }

    public double this[int x, int y] => _top[x, y];
    public bool IsAllowed(int x, int y) => _allowed[x, y];
    public double CeilingAt(int x, int y) => _ceiling[x, y];

    public bool TryGetLowestZ(OrientedMeshHeightmap map, int cellX, int cellY, out double z)
    {
        z = 0;
        if (!Contains(map, cellX, cellY))
        {
            return false;
        }

        for (var mx = 0; mx < map.WidthCells; mx++)
        {
            for (var my = 0; my < map.DepthCells; my++)
            {
                if (!map.IsOccupied(mx, my)) continue;

                var targetX = cellX + mx;
                var targetY = cellY + my;
                if (!_allowed[targetX, targetY])
                {
                    return false;
                }

                z = Math.Max(z, _top[targetX, targetY] - map.BottomAt(mx, my));
            }
        }

        if (!TryFitContainerIntervals(map, cellX, cellY, ref z)) return false;
        return !WouldCollide(map, cellX, cellY, z);
    }

    public double ScorePlacement(OrientedMeshHeightmap map, int cellX, int cellY, double z, MeshPackSettings settings)
    {
        var deltaHeight = 0.0;

        for (var mx = 0; mx < map.WidthCells; mx++)
        {
            for (var my = 0; my < map.DepthCells; my++)
            {
                if (!map.IsOccupied(mx, my)) continue;

                var targetX = cellX + mx;
                var targetY = cellY + my;
                var candidateTop = z + map.TopAt(mx, my);
                deltaHeight += Math.Max(0, candidateTop - _top[targetX, targetY]);
            }
        }

        return settings.HeightWeight * deltaHeight + settings.CompactnessWeight * (cellX + cellY);
    }

    public void Add(OrientedMeshHeightmap map, int cellX, int cellY, double z)
    {
        for (var mx = 0; mx < map.WidthCells; mx++)
        {
            for (var my = 0; my < map.DepthCells; my++)
            {
                if (!map.IsOccupied(mx, my)) continue;

                var targetX = cellX + mx;
                var targetY = cellY + my;
                var bottom = z + map.BottomAt(mx, my);
                var top = z + map.TopAt(mx, my);
                _top[targetX, targetY] = Math.Max(_top[targetX, targetY], top);

                if (_intervals[targetX, targetY] == null)
                {
                    _intervals[targetX, targetY] = new List<HeightInterval>();
                }

                _intervals[targetX, targetY].Add(new HeightInterval(bottom, top));
            }
        }
    }

    private bool Contains(OrientedMeshHeightmap map, int cellX, int cellY)
    {
        return cellX >= 0
            && cellY >= 0
            && cellX + map.WidthCells <= WidthCells
            && cellY + map.DepthCells <= DepthCells;
    }

    private bool TryFitContainerIntervals(OrientedMeshHeightmap map, int cellX, int cellY, ref double z)
    {
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var allFit = true;
            var raisedZ = z;

            for (var mx = 0; mx < map.WidthCells; mx++)
            {
                for (var my = 0; my < map.DepthCells; my++)
                {
                    if (!map.IsOccupied(mx, my)) continue;

                    var targetX = cellX + mx;
                    var targetY = cellY + my;
                    var candidateBottom = z + map.BottomAt(mx, my);
                    var candidateTop = z + map.TopAt(mx, my);
                    var intervals = _containerIntervals[targetX, targetY];

                    if (FitsAnyInterval(intervals, candidateBottom, candidateTop))
                    {
                        continue;
                    }

                    var raiseTo = FindRaiseToFit(intervals, map.BottomAt(mx, my), map.TopAt(mx, my), z);
                    if (raiseTo == null)
                    {
                        return false;
                    }

                    allFit = false;
                    raisedZ = Math.Max(raisedZ, raiseTo.Value);
                }
            }

            if (allFit)
            {
                return true;
            }

            if (raisedZ <= z + 1e-9)
            {
                return false;
            }

            z = raisedZ;
        }

        return false;
    }

    private static bool FitsAnyInterval(IReadOnlyList<ContainerHeightInterval> intervals, double bottom, double top)
    {
        foreach (var interval in intervals)
        {
            if (interval.Contains(bottom, top))
            {
                return true;
            }
        }

        return false;
    }

    private static double? FindRaiseToFit(
        IReadOnlyList<ContainerHeightInterval> intervals,
        double objectBottom,
        double objectTop,
        double currentZ)
    {
        var objectHeight = objectTop - objectBottom;
        foreach (var interval in intervals)
        {
            if (objectHeight > interval.Height + 1e-9)
            {
                continue;
            }

            var candidateZ = interval.Bottom - objectBottom;
            if (candidateZ > currentZ + 1e-9 && candidateZ + objectTop <= interval.Top + 1e-9)
            {
                return candidateZ;
            }
        }

        return null;
    }

    private bool WouldCollide(OrientedMeshHeightmap map, int cellX, int cellY, double z)
    {
        for (var mx = 0; mx < map.WidthCells; mx++)
        {
            for (var my = 0; my < map.DepthCells; my++)
            {
                if (!map.IsOccupied(mx, my)) continue;

                var intervals = _intervals[cellX + mx, cellY + my];
                if (intervals == null) continue;

                var candidate = new HeightInterval(z + map.BottomAt(mx, my), z + map.TopAt(mx, my));
                foreach (var existing in intervals)
                {
                    if (candidate.Overlaps(existing))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private readonly struct HeightInterval
    {
        public HeightInterval(double bottom, double top)
        {
            Bottom = Math.Min(bottom, top);
            Top = Math.Max(bottom, top);
        }

        public double Bottom { get; }
        public double Top { get; }

        public bool Overlaps(HeightInterval other)
        {
            return Bottom < other.Top - 1e-9 && other.Bottom < Top - 1e-9;
        }
    }
}
