using System;
using System.Collections.Generic;

namespace Frahan.Core;

public sealed class OrientedMeshHeightmap
{
    private readonly bool[,] _mask;
    private readonly double[,] _bottom;
    private readonly double[,] _top;

    private OrientedMeshHeightmap(
        bool[,] mask,
        double[,] bottom,
        double[,] top,
        double cellSize,
        int paddingCells,
        Vec3 orientationBoundsMin,
        Size3 geometrySize,
        double yawDegrees,
        int downAxis)
    {
        _mask = mask;
        _bottom = bottom;
        _top = top;
        CellSize = cellSize;
        PaddingCells = paddingCells;
        WidthCells = mask.GetLength(0);
        DepthCells = mask.GetLength(1);
        OrientationBoundsMin = orientationBoundsMin;
        GeometrySize = geometrySize;
        YawDegrees = yawDegrees;
        DownAxis = downAxis;

        var maxTop = double.NegativeInfinity;
        var minBottom = double.PositiveInfinity;
        for (var x = 0; x < WidthCells; x++)
        {
            for (var y = 0; y < DepthCells; y++)
            {
                if (!IsOccupied(x, y)) continue;
                maxTop = Math.Max(maxTop, TopAt(x, y));
                minBottom = Math.Min(minBottom, BottomAt(x, y));
            }
        }

        MaxTop = double.IsNegativeInfinity(maxTop) ? 0 : maxTop;
        MinBottom = double.IsPositiveInfinity(minBottom) ? 0 : minBottom;
    }

    public double CellSize { get; }
    public int PaddingCells { get; }
    public int WidthCells { get; }
    public int DepthCells { get; }
    public Vec3 OrientationBoundsMin { get; }
    public Size3 GeometrySize { get; }
    public double YawDegrees { get; }

    // 0 = None (Z up, legacy), 1 = X down, 2 = Y down. The down rotation is applied
    // to the item vertices BEFORE the yaw rotation when building this heightmap, so
    // OrientationBoundsMin / GeometrySize already reflect the fully rotated geometry.
    public int DownAxis { get; }

    public double MaxTop { get; }
    public double MinBottom { get; }

    public bool IsOccupied(int x, int y) => _mask[x, y];
    public double BottomAt(int x, int y) => _bottom[x, y];
    public double TopAt(int x, int y) => _top[x, y];

    public static OrientedMeshHeightmap Build(MeshPackItem item, double yawDegrees, double cellSize, double clearance)
    {
        return Build(item, 0, yawDegrees, cellSize, clearance);
    }

    public static OrientedMeshHeightmap Build(MeshPackItem item, int downAxis, double yawDegrees, double cellSize, double clearance)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

        var rotated = Rotate(item.Vertices, downAxis, yawDegrees);
        var bounds = BoundsFrom(rotated);
        var normalized = Normalize(rotated, bounds.Min);
        var geometrySize = bounds.Size;
        var paddingCells = Math.Max(0, (int)Math.Ceiling(Math.Max(0, clearance) / cellSize));
        var widthCells = Math.Max(1, (int)Math.Ceiling(geometrySize.Width / cellSize)) + paddingCells * 2;
        var depthCells = Math.Max(1, (int)Math.Ceiling(geometrySize.Depth / cellSize)) + paddingCells * 2;
        var mask = new bool[widthCells, depthCells];
        var bottom = new double[widthCells, depthCells];
        var top = new double[widthCells, depthCells];

        for (var x = 0; x < widthCells; x++)
        {
            for (var y = 0; y < depthCells; y++)
            {
                bottom[x, y] = double.PositiveInfinity;
                top[x, y] = double.NegativeInfinity;
            }
        }

        foreach (var vertex in normalized)
        {
            AddSample(mask, bottom, top, vertex.X, vertex.Y, vertex.Z, cellSize, paddingCells);
        }

        foreach (var triangle in item.Triangles)
        {
            if (!IsValidTriangle(triangle, normalized.Count)) continue;
            RasterizeTriangle(mask, bottom, top, normalized[triangle.A], normalized[triangle.B], normalized[triangle.C], cellSize, paddingCells);
        }

        return new OrientedMeshHeightmap(mask, bottom, top, cellSize, paddingCells, bounds.Min, geometrySize, yawDegrees, downAxis);
    }

    // Apply the down rotation (tilt a local axis to -Z) FIRST, then the in-plane yaw
    // rotation about Z, matching the world transform order yawRot * downRot. For
    // downAxis == 0 (None) the down step is a true no-op, so the yaw output is
    // bit-identical to the legacy yaw-only code.
    private static List<Vec3> Rotate(IReadOnlyList<Vec3> vertices, int downAxis, double yawDegrees)
    {
        var radians = yawDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var rotated = new List<Vec3>(vertices.Count);

        foreach (var vertex in vertices)
        {
            double dx, dy, dz;
            switch (downAxis)
            {
                case 1: // X down: rotate about +Y by +90deg -> (x,y,z) -> (z, y, -x)
                    dx = vertex.Z;
                    dy = vertex.Y;
                    dz = -vertex.X;
                    break;
                case 2: // Y down: rotate about +X by -90deg -> (x,y,z) -> (x, z, -y)
                    dx = vertex.X;
                    dy = vertex.Z;
                    dz = -vertex.Y;
                    break;
                default: // None: identity (byte-stable with legacy code)
                    dx = vertex.X;
                    dy = vertex.Y;
                    dz = vertex.Z;
                    break;
            }

            rotated.Add(new Vec3(dx * cos - dy * sin, dx * sin + dy * cos, dz));
        }

        return rotated;
    }

    private static List<Vec3> Normalize(IReadOnlyList<Vec3> vertices, Vec3 min)
    {
        var normalized = new List<Vec3>(vertices.Count);
        foreach (var vertex in vertices)
        {
            normalized.Add(new Vec3(vertex.X - min.X, vertex.Y - min.Y, vertex.Z - min.Z));
        }

        return normalized;
    }

    private static Box3 BoundsFrom(IReadOnlyList<Vec3> vertices)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var minZ = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var maxZ = double.NegativeInfinity;

        foreach (var vertex in vertices)
        {
            minX = Math.Min(minX, vertex.X);
            minY = Math.Min(minY, vertex.Y);
            minZ = Math.Min(minZ, vertex.Z);
            maxX = Math.Max(maxX, vertex.X);
            maxY = Math.Max(maxY, vertex.Y);
            maxZ = Math.Max(maxZ, vertex.Z);
        }

        return new Box3(
            new Vec3(minX, minY, minZ),
            new Size3(Math.Max(1e-9, maxX - minX), Math.Max(1e-9, maxY - minY), Math.Max(1e-9, maxZ - minZ)));
    }

    private static bool IsValidTriangle(MeshTriangle triangle, int vertexCount)
    {
        return triangle.A >= 0 && triangle.B >= 0 && triangle.C >= 0
            && triangle.A < vertexCount && triangle.B < vertexCount && triangle.C < vertexCount;
    }

    private static void RasterizeTriangle(
        bool[,] mask,
        double[,] bottom,
        double[,] top,
        Vec3 a,
        Vec3 b,
        Vec3 c,
        double cellSize,
        int paddingCells)
    {
        AddSample(mask, bottom, top, a.X, a.Y, a.Z, cellSize, paddingCells);
        AddSample(mask, bottom, top, b.X, b.Y, b.Z, cellSize, paddingCells);
        AddSample(mask, bottom, top, c.X, c.Y, c.Z, cellSize, paddingCells);
        AddSample(mask, bottom, top, (a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0, cellSize, paddingCells);

        var minX = Math.Min(a.X, Math.Min(b.X, c.X));
        var minY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
        var maxX = Math.Max(a.X, Math.Max(b.X, c.X));
        var maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
        var startX = Math.Max(0, (int)Math.Floor(minX / cellSize) + paddingCells);
        var startY = Math.Max(0, (int)Math.Floor(minY / cellSize) + paddingCells);
        var endX = Math.Min(mask.GetLength(0) - 1, (int)Math.Floor(maxX / cellSize) + paddingCells);
        var endY = Math.Min(mask.GetLength(1) - 1, (int)Math.Floor(maxY / cellSize) + paddingCells);
        var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);

        if (Math.Abs(denominator) < 1e-12)
        {
            RasterizeSegment(mask, bottom, top, a, b, cellSize, paddingCells);
            RasterizeSegment(mask, bottom, top, b, c, cellSize, paddingCells);
            RasterizeSegment(mask, bottom, top, c, a, cellSize, paddingCells);
            return;
        }

        for (var x = startX; x <= endX; x++)
        {
            for (var y = startY; y <= endY; y++)
            {
                var px = (x - paddingCells + 0.5) * cellSize;
                var py = (y - paddingCells + 0.5) * cellSize;
                var w1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denominator;
                var w2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denominator;
                var w3 = 1.0 - w1 - w2;

                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;

                var z = w1 * a.Z + w2 * b.Z + w3 * c.Z;
                AddSample(mask, bottom, top, px, py, z, cellSize, paddingCells);
            }
        }
    }

    private static void RasterizeSegment(
        bool[,] mask,
        double[,] bottom,
        double[,] top,
        Vec3 a,
        Vec3 b,
        double cellSize,
        int paddingCells)
    {
        var length = Math.Sqrt(
            (b.X - a.X) * (b.X - a.X)
            + (b.Y - a.Y) * (b.Y - a.Y)
            + (b.Z - a.Z) * (b.Z - a.Z));
        var steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(cellSize * 0.5, 1e-9)));

        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            AddSample(
                mask,
                bottom,
                top,
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                cellSize,
                paddingCells);
        }
    }

    private static void AddSample(
        bool[,] mask,
        double[,] bottom,
        double[,] top,
        double x,
        double y,
        double z,
        double cellSize,
        int paddingCells)
    {
        var cellX = Math.Max(0, Math.Min(mask.GetLength(0) - 1, CoordinateToCell(x, cellSize) + paddingCells));
        var cellY = Math.Max(0, Math.Min(mask.GetLength(1) - 1, CoordinateToCell(y, cellSize) + paddingCells));

        mask[cellX, cellY] = true;
        bottom[cellX, cellY] = Math.Min(bottom[cellX, cellY], z);
        top[cellX, cellY] = Math.Max(top[cellX, cellY], z);
    }

    private static int CoordinateToCell(double coordinate, double cellSize)
    {
        if (coordinate > 0)
        {
            var grid = coordinate / cellSize;
            var nearest = Math.Round(grid);
            if (Math.Abs(grid - nearest) < 1e-9)
            {
                coordinate -= cellSize * 1e-9;
            }
        }

        return (int)Math.Floor(coordinate / cellSize);
    }
}
