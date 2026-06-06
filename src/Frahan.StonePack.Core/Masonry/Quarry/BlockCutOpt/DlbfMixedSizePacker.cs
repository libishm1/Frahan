#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// DlbfMixedSizePacker -- Phase 6 extension; improvement I7.
//
// Deepest-Left-Bottom-Fill greedy 2D packer over a rectangular tested area
// using a discrete grid resolution. Multi-size catalogue with per-size revenue.
// Pieces are sorted by revenue-per-area in descending order and placed at the
// deepest (Y minimal), leftmost (X minimal) available position.
//
// Reference: Chehrazad, Roose, Wauters (2025) "A fast and scalable
// deepest-left-bottom-fill algorithm..." Int. J. Production Research 63,
// 6606-6629. doi 10.1080/00207543.2025.2478434.
//
// v1 scope:
//   - 2D packing (the rotated cutting grid is intrinsically planar).
//   - Axis-aligned placement (rotation handled at the BlockCutOpt psi level).
//   - Discrete grid resolution at min(blockSize)/4.
//   - No-rotation NFP check via discrete grid mask.
// =============================================================================

public sealed class PieceSize
{
    public PieceSize(string id, double width, double depth, double revenue)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException(nameof(id));
        if (!(width > 0)) throw new ArgumentOutOfRangeException(nameof(width));
        if (!(depth > 0)) throw new ArgumentOutOfRangeException(nameof(depth));
        if (!(revenue >= 0)) throw new ArgumentOutOfRangeException(nameof(revenue));
        Id = id;
        Width = width;
        Depth = depth;
        Revenue = revenue;
    }

    public string Id { get; }
    public double Width { get; }   // metres along X
    public double Depth { get; }   // metres along Y
    public double Revenue { get; } // RMV per piece
    public double Area => Width * Depth;
    public double RevenuePerArea => Revenue / Math.Max(Area, BlockCutOptTolerances.GeometricEps);

    public override string ToString() =>
        $"PieceSize({Id}, {Width}x{Depth}m, Rev={Revenue:0.000})";
}

public sealed class PlacedPiece
{
    public PlacedPiece(PieceSize size, double xMin, double yMin)
    {
        Size = size;
        XMin = xMin;
        YMin = yMin;
    }

    public PieceSize Size { get; }
    public double XMin { get; }
    public double YMin { get; }
    public double XMax => XMin + Size.Width;
    public double YMax => YMin + Size.Depth;
}

public sealed class DlbfPackResult
{
    public DlbfPackResult(IReadOnlyList<PlacedPiece> placed, double totalRevenue, double coveredAreaMetres2)
    {
        Placed = placed;
        TotalRevenue = totalRevenue;
        CoveredAreaMetres2 = coveredAreaMetres2;
    }
    public IReadOnlyList<PlacedPiece> Placed { get; }
    public double TotalRevenue { get; }
    public double CoveredAreaMetres2 { get; }

    public override string ToString() =>
        $"DlbfPackResult(placed={Placed.Count}, revenue={TotalRevenue:0.000}, area={CoveredAreaMetres2:0.0} m2)";
}

public static class DlbfMixedSizePacker
{
    /// <summary>
    /// DLBF greedy pack. Each candidate piece is tried at the (xMin, yMin) of
    /// every available cell in the discretised grid; the lowest-y, then
    /// lowest-x available cell is chosen. Pieces are processed in
    /// revenue-per-area-descending order.
    /// </summary>
    /// <param name="area">Packing rectangle in metres (Z range ignored).</param>
    /// <param name="catalog">List of PieceSize entries.</param>
    /// <param name="forbidden">Optional list of forbidden rectangles (e.g. fracture-intersected cells).</param>
    /// <param name="cellSize">Discretisation cell size; defaults to min piece dimension / 4.</param>
    public static DlbfPackResult Pack(
        BoundingBox3 area,
        IReadOnlyList<PieceSize> catalog,
        IReadOnlyList<BoundingBox3> forbidden = null,
        double cellSize = 0.0)
    {
        if (area == null) throw new ArgumentNullException(nameof(area));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (catalog.Count == 0) throw new ArgumentException("catalog must be non-empty", nameof(catalog));

        if (cellSize <= 0)
        {
            double minDim = double.PositiveInfinity;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Width < minDim) minDim = catalog[i].Width;
                if (catalog[i].Depth < minDim) minDim = catalog[i].Depth;
            }
            cellSize = minDim / 4.0;
        }

        int nx = Math.Max(1, (int)Math.Ceiling(area.SizeX / cellSize));
        int ny = Math.Max(1, (int)Math.Ceiling(area.SizeY / cellSize));
        var blocked = new bool[nx, ny];

        if (forbidden != null)
        {
            for (int f = 0; f < forbidden.Count; f++)
            {
                var fb = forbidden[f];
                int iMin = Math.Max(0, (int)Math.Floor((fb.MinX - area.MinX) / cellSize));
                int iMax = Math.Min(nx - 1, (int)Math.Ceiling((fb.MaxX - area.MinX) / cellSize) - 1);
                int jMin = Math.Max(0, (int)Math.Floor((fb.MinY - area.MinY) / cellSize));
                int jMax = Math.Min(ny - 1, (int)Math.Ceiling((fb.MaxY - area.MinY) / cellSize) - 1);
                for (int j = jMin; j <= jMax; j++)
                    for (int i = iMin; i <= iMax; i++)
                        blocked[i, j] = true;
            }
        }

        // sort pieces by revenue-per-area descending
        var sorted = new List<PieceSize>(catalog);
        sorted.Sort((a, b) => b.RevenuePerArea.CompareTo(a.RevenuePerArea));

        var placed = new List<PlacedPiece>();
        double totalRevenue = 0;
        double totalCoveredArea = 0;

        // greedy multi-pass: keep placing each piece until no more fit
        bool anyPlaced = true;
        while (anyPlaced)
        {
            anyPlaced = false;
            for (int p = 0; p < sorted.Count; p++)
            {
                var piece = sorted[p];
                int wCells = Math.Max(1, (int)Math.Ceiling(piece.Width / cellSize));
                int dCells = Math.Max(1, (int)Math.Ceiling(piece.Depth / cellSize));
                if (wCells > nx || dCells > ny) continue;

                int bestI = -1, bestJ = -1;
                // DLBF: lowest-y first, then lowest-x
                for (int j = 0; j + dCells <= ny && bestJ < 0; j++)
                {
                    for (int i = 0; i + wCells <= nx; i++)
                    {
                        if (RegionFree(blocked, i, j, wCells, dCells))
                        {
                            bestI = i; bestJ = j;
                            break;
                        }
                    }
                }
                if (bestI < 0) continue;

                double xMin = area.MinX + bestI * cellSize;
                double yMin = area.MinY + bestJ * cellSize;
                placed.Add(new PlacedPiece(piece, xMin, yMin));
                totalRevenue += piece.Revenue;
                totalCoveredArea += piece.Area;
                MarkBlocked(blocked, bestI, bestJ, wCells, dCells);
                anyPlaced = true;
            }
        }

        return new DlbfPackResult(placed, totalRevenue, totalCoveredArea);
    }

    private static bool RegionFree(bool[,] blocked, int iStart, int jStart, int wCells, int dCells)
    {
        for (int j = jStart; j < jStart + dCells; j++)
        {
            for (int i = iStart; i < iStart + wCells; i++)
            {
                if (blocked[i, j]) return false;
            }
        }
        return true;
    }

    private static void MarkBlocked(bool[,] blocked, int iStart, int jStart, int wCells, int dCells)
    {
        for (int j = jStart; j < jStart + dCells; j++)
            for (int i = iStart; i < iStart + wCells; i++)
                blocked[i, j] = true;
    }
}
