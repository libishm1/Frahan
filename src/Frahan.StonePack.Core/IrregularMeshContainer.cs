using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Core;

public sealed class IrregularMeshContainer
{
    private readonly bool[,] _allowed;
    private readonly double[,] _floor;
    private readonly double[,] _ceiling;
    private readonly List<ContainerHeightInterval>[,] _intervals;

    private IrregularMeshContainer(
        double cellSize,
        bool[,] allowed,
        double[,] floor,
        double[,] ceiling,
        List<ContainerHeightInterval>[,] intervals)
    {
        CellSize = cellSize;
        WidthCells = allowed.GetLength(0);
        DepthCells = allowed.GetLength(1);
        _allowed = allowed;
        _floor = floor;
        _ceiling = ceiling;
        _intervals = intervals;

        var maxCeiling = 0.0;
        for (var x = 0; x < WidthCells; x++)
        {
            for (var y = 0; y < DepthCells; y++)
            {
                if (!_allowed[x, y]) continue;
                maxCeiling = Math.Max(maxCeiling, _ceiling[x, y]);
            }
        }

        Bounds = new PackContainer(WidthCells * CellSize, DepthCells * CellSize, Math.Max(CellSize, maxCeiling));
    }

    public double CellSize { get; }
    public int WidthCells { get; }
    public int DepthCells { get; }
    public PackContainer Bounds { get; }

    public bool IsAllowed(int x, int y) => _allowed[x, y];
    public double FloorAt(int x, int y) => _floor[x, y];
    public double CeilingAt(int x, int y) => _ceiling[x, y];
    public IReadOnlyList<ContainerHeightInterval> IntervalsAt(int x, int y) => _intervals[x, y];

    public static IrregularMeshContainer FromMesh(MeshPackItem containerMesh, double cellSize)
    {
        if (containerMesh == null) throw new ArgumentNullException(nameof(containerMesh));
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

        var widthCells = Math.Max(1, (int)Math.Ceiling(containerMesh.Bounds.Size.Width / cellSize));
        var depthCells = Math.Max(1, (int)Math.Ceiling(containerMesh.Bounds.Size.Depth / cellSize));
        var allowed = new bool[widthCells, depthCells];
        var floor = new double[widthCells, depthCells];
        var ceiling = new double[widthCells, depthCells];
        var intervals = new List<ContainerHeightInterval>[widthCells, depthCells];
        var allowedCount = 0;

        for (var x = 0; x < widthCells; x++)
        {
            for (var y = 0; y < depthCells; y++)
            {
                var px = Math.Min(containerMesh.Bounds.Size.Width, (x + 0.5) * cellSize);
                var py = Math.Min(containerMesh.Bounds.Size.Depth, (y + 0.5) * cellSize);
                var intersections = GetVerticalIntersections(containerMesh, px, py);
                if (intersections.Count < 2)
                {
                    continue;
                }

                var cellIntervals = PairIntersections(intersections);
                if (cellIntervals.Count == 0)
                {
                    continue;
                }

                allowed[x, y] = true;
                intervals[x, y] = cellIntervals;
                allowedCount++;
                floor[x, y] = cellIntervals[0].Bottom;
                ceiling[x, y] = cellIntervals[cellIntervals.Count - 1].Top;
            }
        }

        if (allowedCount == 0)
        {
            return FromSurfaceSamplesFallback(containerMesh, cellSize);
        }

        return new IrregularMeshContainer(cellSize, allowed, floor, ceiling, intervals);
    }

    private static IrregularMeshContainer FromSurfaceSamplesFallback(MeshPackItem containerMesh, double cellSize)
    {
        var map = OrientedMeshHeightmap.Build(containerMesh, 0, cellSize, 0);
        var allowed = new bool[map.WidthCells, map.DepthCells];
        var floor = new double[map.WidthCells, map.DepthCells];
        var ceiling = new double[map.WidthCells, map.DepthCells];
        var intervals = new List<ContainerHeightInterval>[map.WidthCells, map.DepthCells];
        var allowedCount = 0;

        for (var x = 0; x < map.WidthCells; x++)
        {
            for (var y = 0; y < map.DepthCells; y++)
            {
                allowed[x, y] = map.IsOccupied(x, y);
                if (!allowed[x, y]) continue;

                allowedCount++;
                floor[x, y] = map.BottomAt(x, y);
                ceiling[x, y] = map.TopAt(x, y);
                intervals[x, y] = new List<ContainerHeightInterval>
                {
                    new ContainerHeightInterval(floor[x, y], ceiling[x, y])
                };
            }
        }

        if (allowedCount == 0)
        {
            throw new InvalidOperationException("Container mesh did not produce any allowed heightmap cells.");
        }

        return new IrregularMeshContainer(cellSize, allowed, floor, ceiling, intervals);
    }

    private static List<double> GetVerticalIntersections(MeshPackItem mesh, double x, double y)
    {
        var intersections = new List<double>();
        foreach (var triangle in mesh.Triangles)
        {
            if (!IsValidTriangle(triangle, mesh.Vertices.Count)) continue;
            if (TryIntersectVertical(mesh.Vertices[triangle.A], mesh.Vertices[triangle.B], mesh.Vertices[triangle.C], x, y, out var z))
            {
                intersections.Add(z);
            }
        }

        intersections.Sort();
        return Deduplicate(intersections);
    }

    private static bool IsValidTriangle(MeshTriangle triangle, int vertexCount)
    {
        return triangle.A >= 0 && triangle.B >= 0 && triangle.C >= 0
            && triangle.A < vertexCount && triangle.B < vertexCount && triangle.C < vertexCount;
    }

    private static bool TryIntersectVertical(Vec3 a, Vec3 b, Vec3 c, double x, double y, out double z)
    {
        z = 0;
        var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        var w1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
        var w2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
        var w3 = 1.0 - w1 - w2;

        const double tolerance = 1e-9;
        if (w1 < -tolerance || w2 < -tolerance || w3 < -tolerance)
        {
            return false;
        }

        z = w1 * a.Z + w2 * b.Z + w3 * c.Z;
        return true;
    }

    private static List<double> Deduplicate(List<double> values)
    {
        return values
            .OrderBy(value => value)
            .Aggregate(new List<double>(), (unique, value) =>
            {
                if (unique.Count == 0 || Math.Abs(unique[unique.Count - 1] - value) > 1e-7)
                {
                    unique.Add(value);
                }

                return unique;
            });
    }

    private static List<ContainerHeightInterval> PairIntersections(List<double> intersections)
    {
        var intervals = new List<ContainerHeightInterval>();
        for (var i = 0; i + 1 < intersections.Count; i += 2)
        {
            if (intersections[i + 1] - intersections[i] <= 1e-7)
            {
                continue;
            }

            intervals.Add(new ContainerHeightInterval(intersections[i], intersections[i + 1]));
        }

        return intervals;
    }
}

public readonly struct ContainerHeightInterval
{
    public ContainerHeightInterval(double bottom, double top)
    {
        Bottom = Math.Min(bottom, top);
        Top = Math.Max(bottom, top);
    }

    public double Bottom { get; }
    public double Top { get; }
    public double Height => Top - Bottom;

    public bool Contains(double bottom, double top)
    {
        return bottom >= Bottom - 1e-9 && top <= Top + 1e-9;
    }
}
