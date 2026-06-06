using System;
using System.Collections.Generic;

namespace Frahan.Core;

/// <summary>
/// One detected void region in a 2D sheet after packing. Reported by
/// <see cref="ResidualVoidsDetector"/>.
/// </summary>
public sealed class ResidualVoid
{
    public ResidualVoid(double minX, double minY, double maxX, double maxY, double approximateArea, int cellCount)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        ApproximateArea = approximateArea;
        CellCount = cellCount;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double ApproximateArea { get; }
    public int CellCount { get; }

    public override string ToString() =>
        $"ResidualVoid({MinX:0.##}..{MaxX:0.##}, {MinY:0.##}..{MaxY:0.##}, area={ApproximateArea:0.##}, cells={CellCount})";
}

/// <summary>
/// Detects residual voids - 2D regions inside a sheet polygon that no placed
/// part covers - via cell-grid sampling and connected-component labelling.
/// Pure managed; allocation-light hot path; no Rhino dependency.
///
/// Spec 5 § 5 calls for residual void detection in the 2D Trencadis solver
/// `PackingResult`. This implementation provides the algorithm; the GH wrapper
/// component that exposes it lives in Frahan.GH (proposed component
/// "Frahan Residual Voids" per runbook § 16.1).
/// </summary>
public sealed class ResidualVoidsDetector
{
    private readonly double _cellSize;
    private readonly double _minVoidArea;

    /// <summary>
    /// Build a detector with a given cell sampling resolution and minimum
    /// reportable void area (regions smaller than this are filtered out).
    /// </summary>
    public ResidualVoidsDetector(double cellSize, double minVoidArea = 0.0)
    {
        if (cellSize <= 0.0) throw new ArgumentOutOfRangeException(nameof(cellSize), "must be > 0");
        if (minVoidArea < 0.0) throw new ArgumentOutOfRangeException(nameof(minVoidArea), "must be >= 0");
        _cellSize = cellSize;
        _minVoidArea = minVoidArea;
    }

    /// <summary>
    /// Detect voids inside <paramref name="sheetPolygon"/> not covered by any
    /// polygon in <paramref name="placedPartPolygons"/>. Polygon coordinates
    /// are flat double arrays (x0, y0, x1, y1, ...). The polygons are assumed
    /// closed (the first and last vertices are joined implicitly).
    /// </summary>
    public IReadOnlyList<ResidualVoid> Detect(
        IReadOnlyList<double> sheetPolygon,
        IReadOnlyList<IReadOnlyList<double>> placedPartPolygons)
    {
        if (sheetPolygon == null) throw new ArgumentNullException(nameof(sheetPolygon));
        if (placedPartPolygons == null) throw new ArgumentNullException(nameof(placedPartPolygons));
        if (sheetPolygon.Count < 6 || sheetPolygon.Count % 2 != 0)
            throw new ArgumentException("sheetPolygon must have an even count of >= 6 doubles (>= 3 vertices)", nameof(sheetPolygon));

        // Sheet AABB.
        double minX = sheetPolygon[0], maxX = sheetPolygon[0];
        double minY = sheetPolygon[1], maxY = sheetPolygon[1];
        for (int i = 2; i < sheetPolygon.Count; i += 2)
        {
            double x = sheetPolygon[i], y = sheetPolygon[i + 1];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        int nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / _cellSize));
        int ny = Math.Max(1, (int)Math.Ceiling((maxY - minY) / _cellSize));

        // Mark cells that are inside the sheet AND outside every placed part.
        // Cell i,j -> cell centre (minX + (i+0.5)*cellSize, minY + (j+0.5)*cellSize).
        // value: 0 = unmarked (covered or outside sheet); 1 = void candidate.
        var grid = new byte[nx * ny];
        for (int j = 0; j < ny; j++)
        {
            double cy = minY + (j + 0.5) * _cellSize;
            for (int i = 0; i < nx; i++)
            {
                double cx = minX + (i + 0.5) * _cellSize;
                if (!PointInPoly(sheetPolygon, cx, cy)) continue;

                bool covered = false;
                for (int p = 0; p < placedPartPolygons.Count; p++)
                {
                    var part = placedPartPolygons[p];
                    if (part == null || part.Count < 6) continue;
                    if (PointInPoly(part, cx, cy)) { covered = true; break; }
                }
                if (!covered) grid[j * nx + i] = 1;
            }
        }

        // Connected-component label (4-neighbourhood, iterative flood fill).
        var labels = new int[nx * ny];   // 0 = unlabelled; >=1 = component id
        var voids = new List<ResidualVoid>();
        var queue = new Queue<int>(); // packed (j*nx+i)
        int nextLabel = 0;
        double cellArea = _cellSize * _cellSize;
        for (int seed = 0; seed < grid.Length; seed++)
        {
            if (grid[seed] != 1 || labels[seed] != 0) continue;
            nextLabel++;
            labels[seed] = nextLabel;
            queue.Enqueue(seed);

            int compMinI = int.MaxValue, compMaxI = int.MinValue;
            int compMinJ = int.MaxValue, compMaxJ = int.MinValue;
            int cellCount = 0;

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int i = idx % nx;
                int j = idx / nx;
                if (i < compMinI) compMinI = i; if (i > compMaxI) compMaxI = i;
                if (j < compMinJ) compMinJ = j; if (j > compMaxJ) compMaxJ = j;
                cellCount++;

                // 4-neighbours
                if (i > 0)        EnqueueIfVoid(idx - 1, grid, labels, nextLabel, queue);
                if (i < nx - 1)   EnqueueIfVoid(idx + 1, grid, labels, nextLabel, queue);
                if (j > 0)        EnqueueIfVoid(idx - nx, grid, labels, nextLabel, queue);
                if (j < ny - 1)   EnqueueIfVoid(idx + nx, grid, labels, nextLabel, queue);
            }

            double approxArea = cellCount * cellArea;
            if (approxArea + 1e-12 < _minVoidArea) continue; // filter

            voids.Add(new ResidualVoid(
                minX: minX + compMinI * _cellSize,
                minY: minY + compMinJ * _cellSize,
                maxX: minX + (compMaxI + 1) * _cellSize,
                maxY: minY + (compMaxJ + 1) * _cellSize,
                approximateArea: approxArea,
                cellCount: cellCount));
        }

        return voids;
    }

    private static void EnqueueIfVoid(int neighborIdx, byte[] grid, int[] labels, int label, Queue<int> queue)
    {
        if (grid[neighborIdx] == 1 && labels[neighborIdx] == 0)
        {
            labels[neighborIdx] = label;
            queue.Enqueue(neighborIdx);
        }
    }

    /// <summary>
    /// Even-odd point-in-polygon test on a flat (x,y,x,y,...) polygon.
    /// </summary>
    internal static bool PointInPoly(IReadOnlyList<double> poly, double x, double y)
    {
        bool inside = false;
        int n = poly.Count / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[2 * i],     yi = poly[2 * i + 1];
            double xj = poly[2 * j],     yj = poly[2 * j + 1];
            bool cross = ((yi > y) != (yj > y)) &&
                (x < (xj - xi) * (y - yi) / (yj - yi + 1e-30) + xi);
            if (cross) inside = !inside;
        }
        return inside;
    }
}
