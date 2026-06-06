#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// Dlbf3dMixedSizePacker -- Phase 6 extension; 3D generalisation of I7.
//
// Deepest-Left-Bottom-Fill greedy 3D packer over a tested AABB using a
// discrete cubic grid. Multi-size catalogue with per-size revenue and per-
// piece height (the 2D DLBF assumes one common Z extrusion, this one does
// not). Pieces sort by revenue-per-VOLUME descending and place at the
// deepest-bottom-left available cell.
//
// "Deepest-bottom-left" ordering for 3D (matching Chehrazad 2025 conventions):
//   1. Lowest Z first  (bench floor first; matters when stacking is allowed)
//   2. Then lowest Y    (back / north first)
//   3. Then lowest X    (left / west first)
//
// Two operating modes:
//   - FloorOnly = true  : every piece sits on z = bench.MinZ, occupying
//                         [0, height] in Z. This is the natural quarry
//                         extraction mode -- monoliths cut OUT of solid rock,
//                         no stacking.
//   - FloorOnly = false : full 3D, pieces may stack at any free z. Useful
//                         for monument storage, slab racking, container
//                         loading.
//
// Reference: Chehrazad, Roose, Wauters (2025) "A fast and scalable
// deepest-left-bottom-fill algorithm..." Int. J. Production Research 63,
// 6606-6629. doi 10.1080/00207543.2025.2478434. 3D extension follows the
// paper's Section 5 generalisation.
//
// Companion: see Dlbf2D in DlbfMixedSizePacker.cs for the planar variant.
// =============================================================================

public sealed class PieceSize3D
{
    public PieceSize3D(string id, double width, double depth, double height, double revenue)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException(nameof(id));
        if (!(width > 0)) throw new ArgumentOutOfRangeException(nameof(width));
        if (!(depth > 0)) throw new ArgumentOutOfRangeException(nameof(depth));
        if (!(height > 0)) throw new ArgumentOutOfRangeException(nameof(height));
        if (!(revenue >= 0)) throw new ArgumentOutOfRangeException(nameof(revenue));
        Id = id;
        Width = width;
        Depth = depth;
        Height = height;
        Revenue = revenue;
    }

    public string Id { get; }
    public double Width { get; }   // metres along X
    public double Depth { get; }   // metres along Y
    public double Height { get; }  // metres along Z
    public double Revenue { get; } // RMV per piece
    public double Volume => Width * Depth * Height;
    public double RevenuePerVolume =>
        Revenue / Math.Max(Volume, BlockCutOptTolerances.GeometricEps);

    public override string ToString() =>
        $"PieceSize3D({Id}, {Width}x{Depth}x{Height}m, Rev={Revenue:0.000})";
}

public sealed class PlacedPiece3D
{
    public PlacedPiece3D(PieceSize3D size, double xMin, double yMin, double zMin)
    {
        Size = size;
        XMin = xMin;
        YMin = yMin;
        ZMin = zMin;
    }

    public PieceSize3D Size { get; }
    public double XMin { get; }
    public double YMin { get; }
    public double ZMin { get; }
    public double XMax => XMin + Size.Width;
    public double YMax => YMin + Size.Depth;
    public double ZMax => ZMin + Size.Height;
}

public sealed class Dlbf3dPackResult
{
    public Dlbf3dPackResult(
        IReadOnlyList<PlacedPiece3D> placed,
        double totalRevenue,
        double occupiedVolumeMetres3)
    {
        Placed = placed;
        TotalRevenue = totalRevenue;
        OccupiedVolumeMetres3 = occupiedVolumeMetres3;
    }
    public IReadOnlyList<PlacedPiece3D> Placed { get; }
    public double TotalRevenue { get; }
    public double OccupiedVolumeMetres3 { get; }

    public override string ToString() =>
        $"Dlbf3dPackResult(placed={Placed.Count}, revenue={TotalRevenue:0.000}, " +
        $"volume={OccupiedVolumeMetres3:0.0} m^3)";
}

public static class Dlbf3dMixedSizePacker
{
    /// <summary>
    /// 3D DLBF greedy pack. Pieces are processed in revenue-per-volume
    /// descending order; for each piece, the lowest-Z, then lowest-Y, then
    /// lowest-X available cubic cell is chosen.
    /// </summary>
    /// <param name="area">Packing AABB in metres.</param>
    /// <param name="catalog">PieceSize3D entries.</param>
    /// <param name="forbidden">Optional forbidden boxes (e.g. fracture-intersected cells from BlockCutOpt).</param>
    /// <param name="cellSize">Grid discretisation; 0 = min(W,D,H)/4.</param>
    /// <param name="floorOnly">When true, every piece sits at zMin = area.MinZ (no stacking).</param>
    public static Dlbf3dPackResult Pack(
        BoundingBox3 area,
        IReadOnlyList<PieceSize3D> catalog,
        IReadOnlyList<BoundingBox3> forbidden = null,
        double cellSize = 0.0,
        bool floorOnly = true)
        => Pack(area, catalog, forbidden, cellSize, floorOnly, false);

    /// <summary>
    /// 3D DLBF greedy pack with optional best-of-orientation (2026-06-06 evolution).
    /// When <paramref name="tryOrientations"/> is true, each piece is tried in its
    /// (up to 6) distinct axis-permutations and placed in the orientation whose best
    /// free cell is lowest (z, then y, then x); a flatter orientation packs denser
    /// and a piece that does not fit one way may fit rotated. Volume and revenue are
    /// permutation-invariant, so the placed piece simply records its oriented dims.
    /// The default overload delegates here with tryOrientations=false, so existing
    /// behaviour (and the 6 existing tests) is byte-identical.
    /// </summary>
    public static Dlbf3dPackResult Pack(
        BoundingBox3 area,
        IReadOnlyList<PieceSize3D> catalog,
        IReadOnlyList<BoundingBox3> forbidden,
        double cellSize,
        bool floorOnly,
        bool tryOrientations)
    {
        if (area == null) throw new ArgumentNullException(nameof(area));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (catalog.Count == 0) throw new ArgumentException("catalog must be non-empty", nameof(catalog));

        if (cellSize <= 0)
        {
            double minDim = double.PositiveInfinity;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Width  < minDim) minDim = catalog[i].Width;
                if (catalog[i].Depth  < minDim) minDim = catalog[i].Depth;
                if (catalog[i].Height < minDim) minDim = catalog[i].Height;
            }
            cellSize = minDim / 4.0;
        }
        if (!(cellSize > 0))
            throw new ArgumentOutOfRangeException(nameof(cellSize), "must resolve to > 0");

        int nx = Math.Max(1, (int)Math.Ceiling(area.SizeX / cellSize));
        int ny = Math.Max(1, (int)Math.Ceiling(area.SizeY / cellSize));
        int nz = Math.Max(1, (int)Math.Ceiling(area.SizeZ / cellSize));
        var blocked = new bool[nx, ny, nz];

        if (forbidden != null)
        {
            for (int f = 0; f < forbidden.Count; f++)
            {
                var fb = forbidden[f];
                int iMin = Math.Max(0, (int)Math.Floor((fb.MinX - area.MinX) / cellSize));
                int iMax = Math.Min(nx - 1, (int)Math.Ceiling((fb.MaxX - area.MinX) / cellSize) - 1);
                int jMin = Math.Max(0, (int)Math.Floor((fb.MinY - area.MinY) / cellSize));
                int jMax = Math.Min(ny - 1, (int)Math.Ceiling((fb.MaxY - area.MinY) / cellSize) - 1);
                int kMin = Math.Max(0, (int)Math.Floor((fb.MinZ - area.MinZ) / cellSize));
                int kMax = Math.Min(nz - 1, (int)Math.Ceiling((fb.MaxZ - area.MinZ) / cellSize) - 1);
                for (int k = kMin; k <= kMax; k++)
                    for (int j = jMin; j <= jMax; j++)
                        for (int i = iMin; i <= iMax; i++)
                            blocked[i, j, k] = true;
            }
        }

        // sort pieces by revenue-per-volume descending
        var sorted = new List<PieceSize3D>(catalog);
        sorted.Sort((a, b) => b.RevenuePerVolume.CompareTo(a.RevenuePerVolume));

        var placed = new List<PlacedPiece3D>();
        double totalRevenue = 0;
        double totalVolume = 0;

        bool anyPlaced = true;
        while (anyPlaced)
        {
            anyPlaced = false;
            for (int p = 0; p < sorted.Count; p++)
            {
                var piece = sorted[p];
                var oris = tryOrientations
                    ? Orientations(piece)
                    : new List<(double W, double D, double H)> { (piece.Width, piece.Depth, piece.Height) };

                int bestI = -1, bestJ = -1, bestK = -1, bestW = 0, bestD = 0, bestH = 0;
                double bestOw = 0, bestOd = 0, bestOh = 0;
                foreach (var (ow, od, oh) in oris)
                {
                    int wCells = Math.Max(1, (int)Math.Ceiling(ow / cellSize));
                    int dCells = Math.Max(1, (int)Math.Ceiling(od / cellSize));
                    int hCells = Math.Max(1, (int)Math.Ceiling(oh / cellSize));
                    if (wCells > nx || dCells > ny || hCells > nz) continue;

                    int fi = -1, fj = -1, fk = -1;
                    int kStop = floorOnly ? 1 : (nz - hCells + 1);
                    // DLBF 3D: lowest Z, then lowest Y, then lowest X
                    for (int k = 0; k < kStop && fk < 0; k++)
                        for (int j = 0; j + dCells <= ny && fj < 0; j++)
                            for (int i = 0; i + wCells <= nx; i++)
                                if (RegionFree(blocked, i, j, k, wCells, dCells, hCells)) { fi = i; fj = j; fk = k; break; }
                    if (fk < 0) continue;

                    // Keep the orientation whose best free cell is lowest (z, y, x).
                    if (bestK < 0 || fk < bestK ||
                        (fk == bestK && (fj < bestJ || (fj == bestJ && fi < bestI))))
                    {
                        bestI = fi; bestJ = fj; bestK = fk;
                        bestW = wCells; bestD = dCells; bestH = hCells;
                        bestOw = ow; bestOd = od; bestOh = oh;
                    }
                }
                if (bestK < 0) continue;

                double xMin = area.MinX + bestI * cellSize;
                double yMin = area.MinY + bestJ * cellSize;
                double zMin = area.MinZ + bestK * cellSize;
                var oriented = (Math.Abs(bestOw - piece.Width) < 1e-9 && Math.Abs(bestOd - piece.Depth) < 1e-9 && Math.Abs(bestOh - piece.Height) < 1e-9)
                    ? piece : new PieceSize3D(piece.Id, bestOw, bestOd, bestOh, piece.Revenue);
                placed.Add(new PlacedPiece3D(oriented, xMin, yMin, zMin));
                totalRevenue += piece.Revenue;
                totalVolume += piece.Volume;
                MarkBlocked(blocked, bestI, bestJ, bestK, bestW, bestD, bestH);
                anyPlaced = true;
            }
        }

        return new Dlbf3dPackResult(placed, totalRevenue, totalVolume);
    }

    // The (up to 6) distinct axis-permutations of a piece's dimensions.
    private static List<(double W, double D, double H)> Orientations(PieceSize3D p)
    {
        double w = p.Width, d = p.Depth, h = p.Height;
        var all = new (double, double, double)[]
        { (w, d, h), (w, h, d), (d, w, h), (d, h, w), (h, w, d), (h, d, w) };
        var distinct = new List<(double W, double D, double H)>();
        foreach (var o in all)
        {
            bool dup = false;
            foreach (var e in distinct)
                if (Math.Abs(e.W - o.Item1) < 1e-9 && Math.Abs(e.D - o.Item2) < 1e-9 && Math.Abs(e.H - o.Item3) < 1e-9)
                { dup = true; break; }
            if (!dup) distinct.Add((o.Item1, o.Item2, o.Item3));
        }
        return distinct;
    }

    private static bool RegionFree(
        bool[,,] blocked,
        int iStart, int jStart, int kStart,
        int wCells, int dCells, int hCells)
    {
        for (int k = kStart; k < kStart + hCells; k++)
            for (int j = jStart; j < jStart + dCells; j++)
                for (int i = iStart; i < iStart + wCells; i++)
                    if (blocked[i, j, k]) return false;
        return true;
    }

    private static void MarkBlocked(
        bool[,,] blocked,
        int iStart, int jStart, int kStart,
        int wCells, int dCells, int hCells)
    {
        for (int k = kStart; k < kStart + hCells; k++)
            for (int j = jStart; j < jStart + dCells; j++)
                for (int i = iStart; i < iStart + wCells; i++)
                    blocked[i, j, k] = true;
    }
}
