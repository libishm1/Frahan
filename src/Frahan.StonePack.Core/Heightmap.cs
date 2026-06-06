using System;
using System.Collections.Generic;

namespace Frahan.Core;

public sealed class Heightmap
{
    private readonly double[,] _top;
    private readonly List<Box3> _occupied = new List<Box3>();

    public Heightmap(PackContainer container, double cellSize)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

        Container = container;
        CellSize = cellSize;
        WidthCells = Math.Max(1, (int)Math.Ceiling(container.Width / cellSize));
        DepthCells = Math.Max(1, (int)Math.Ceiling(container.Depth / cellSize));
        _top = new double[WidthCells, DepthCells];
    }

    public PackContainer Container { get; }
    public double CellSize { get; }
    public int WidthCells { get; }
    public int DepthCells { get; }
    public IReadOnlyList<Box3> OccupiedBoxes => _occupied;

    public double this[int x, int y] => _top[x, y];

    public bool TryGetLowestZ(Size3 size, int cellX, int cellY, out double z)
    {
        z = 0;
        var spanX = CellsFor(size.Width);
        var spanY = CellsFor(size.Depth);

        if (cellX < 0 || cellY < 0 || cellX + spanX > WidthCells || cellY + spanY > DepthCells)
        {
            return false;
        }

        for (var x = cellX; x < cellX + spanX; x++)
        {
            for (var y = cellY; y < cellY + spanY; y++)
            {
                z = Math.Max(z, _top[x, y]);
            }
        }

        return z + size.Height <= Container.Height + 1e-9;
    }

    public double ScorePlacement(Size3 size, int cellX, int cellY, double z, PackSettings settings)
    {
        var spanX = CellsFor(size.Width);
        var spanY = CellsFor(size.Depth);
        var newTop = z + size.Height;
        var deltaHeight = 0.0;

        for (var x = cellX; x < cellX + spanX; x++)
        {
            for (var y = cellY; y < cellY + spanY; y++)
            {
                deltaHeight += Math.Max(0, newTop - _top[x, y]);
            }
        }

        var compactness = settings.CompactnessWeight * (cellX + cellY);
        return settings.HeightWeight * deltaHeight + compactness;
    }

    public void Add(Box3 box)
    {
        var cellX = CellIndex(box.Min.X);
        var cellY = CellIndex(box.Min.Y);
        var spanX = CellsFor(box.Size.Width);
        var spanY = CellsFor(box.Size.Depth);
        var top = box.Min.Z + box.Size.Height;

        for (var x = cellX; x < Math.Min(WidthCells, cellX + spanX); x++)
        {
            for (var y = cellY; y < Math.Min(DepthCells, cellY + spanY); y++)
            {
                _top[x, y] = Math.Max(_top[x, y], top);
            }
        }

        _occupied.Add(box);
    }

    public int CellsFor(double length) => Math.Max(1, (int)Math.Ceiling(length / CellSize));

    public int CellIndex(double coordinate) => Math.Max(0, (int)Math.Floor(coordinate / CellSize));
}
