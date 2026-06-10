#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Sequencing;

namespace Frahan.Tests;

// P4 tests: the imposition-index (Λ) assignment engine. The analytic anchor:
// a stone that is its own cell uniformly inflated by factor k has carve ratio
// λ = 1 − 1/k³ exactly (1.1 → 0.2487, 1.4 → 0.6356), so the voxel metrics are
// validated against closed-form ground truth. Pure managed; no Rhino runtime.

static class StoneCellAssignmentTests
{
    private static void WallCellsAsPrisms(out List<IReadOnlyList<double>> coords,
                                          out List<IReadOnlyList<int>> tris)
    {
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = 2.0, Height = 1.0, GridX = 5, GridY = 3, Coursing = 0.4,
            LloydIterations = 2, SizeGradeCv = 0.30, Seed = 11,
        });
        coords = new List<IReadOnlyList<double>>();
        tris = new List<IReadOnlyList<int>>();
        double depth = 0.25;
        foreach (var cell in gen.Cells)
        {
            int m = cell.VertexCount;
            var cs = new List<double>(m * 6);
            for (int k = 0; k < m; k++) { cs.Add(cell.Us[k]); cs.Add(0.0); cs.Add(cell.Vs[k]); }
            for (int k = 0; k < m; k++) { cs.Add(cell.Us[k]); cs.Add(depth); cs.Add(cell.Vs[k]); }
            var ts = new List<int>();
            for (int k = 1; k + 1 < m; k++)
            {
                ts.Add(0); ts.Add(k); ts.Add(k + 1);
                ts.Add(m); ts.Add(m + k + 1); ts.Add(m + k);
            }
            for (int k = 0; k < m; k++)
            {
                int a = k, b = (k + 1) % m;
                ts.Add(a); ts.Add(m + a); ts.Add(m + b);
                ts.Add(a); ts.Add(m + b); ts.Add(b);
            }
            coords.Add(cs); tris.Add(ts);
        }
    }

    private static IReadOnlyList<double> Scaled(IReadOnlyList<double> coords, double k)
    {
        int nv = coords.Count / 3;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < nv; i++) { cx += coords[i * 3]; cy += coords[i * 3 + 1]; cz += coords[i * 3 + 2]; }
        cx /= nv; cy /= nv; cz /= nv;
        var outC = new List<double>(coords.Count);
        for (int i = 0; i < nv; i++)
        {
            outC.Add(cx + (coords[i * 3] - cx) * k);
            outC.Add(cy + (coords[i * 3 + 1] - cy) * k);
            outC.Add(cz + (coords[i * 3 + 2] - cz) * k);
        }
        return outC;
    }

    public static void IdenticalInventory_LambdaNearZero()
    {
        WallCellsAsPrisms(out var cells, out var tris);
        var r = StoneCellAssignment.Assign(cells, tris, cells, tris);
        if (r.Placements.Count != cells.Count)
            throw new Exception($"all {cells.Count} cells must be filled, got {r.Placements.Count}");
        if (r.ImpositionIndex > 0.06)
            throw new Exception($"identical inventory must give Lambda ~ 0, got {r.ImpositionIndex:0.000}");
        if (r.MeanGapRatio > 0.06)
            throw new Exception($"identical inventory must give gap ~ 0, got {r.MeanGapRatio:0.000}");
    }

    public static void InflatedInventory_AnalyticLambda_AndIdentityRecovery()
    {
        WallCellsAsPrisms(out var cells, out var tris);
        int n = cells.Count;
        // stones = the cells inflated 1.10x, in REVERSED order (shuffle)
        var stones = new List<IReadOnlyList<double>>(n);
        var stTris = new List<IReadOnlyList<int>>(n);
        for (int i = n - 1; i >= 0; i--) { stones.Add(Scaled(cells[i], 1.10)); stTris.Add(tris[i]); }

        var r = StoneCellAssignment.Assign(stones, stTris, cells, tris);
        if (r.Placements.Count != n)
            throw new Exception($"all {n} cells must be filled, got {r.Placements.Count}");

        int identity = 0;
        foreach (var pl in r.Placements)
            if (pl.StoneIndex == n - 1 - pl.CellIndex) identity++;
        if (identity < (int)(0.8 * n))
            throw new Exception($"Hungarian must recover the shuffled identity matching: {identity}/{n}");

        double analytic = 1.0 - 1.0 / (1.10 * 1.10 * 1.10); // 0.2487
        if (Math.Abs(r.ImpositionIndex - analytic) > 0.06)
            throw new Exception($"Lambda must match the analytic inflation value {analytic:0.000} " +
                                $"within voxel error; got {r.ImpositionIndex:0.000}");
        if (r.MeanGapRatio > 0.06)
            throw new Exception($"an inflated stone covers its cell; gap must be ~0, got {r.MeanGapRatio:0.000}");
    }

    public static void CoarserInventory_LambdaMonotonic()
    {
        WallCellsAsPrisms(out var cells, out var tris);
        int n = cells.Count;
        var s11 = new List<IReadOnlyList<double>>(); var s14 = new List<IReadOnlyList<double>>();
        for (int i = 0; i < n; i++) { s11.Add(Scaled(cells[i], 1.10)); s14.Add(Scaled(cells[i], 1.40)); }
        var r11 = StoneCellAssignment.Assign(s11, tris, cells, tris);
        var r14 = StoneCellAssignment.Assign(s14, tris, cells, tris);
        if (!(r14.ImpositionIndex > r11.ImpositionIndex + 0.15))
            throw new Exception($"coarser stock must cost more carve: Lambda(1.4)={r14.ImpositionIndex:0.000} " +
                                $"vs Lambda(1.1)={r11.ImpositionIndex:0.000}");
    }

    public static void ExtraInventory_ReportsUnusedStones()
    {
        WallCellsAsPrisms(out var cells, out var tris);
        int n = cells.Count;
        var stones = new List<IReadOnlyList<double>>(); var stTris = new List<IReadOnlyList<int>>();
        for (int i = 0; i < n; i++) { stones.Add(Scaled(cells[i], 1.05)); stTris.Add(tris[i]); }
        // 5 extra oversize stones that should lose the assignment
        for (int i = 0; i < 5 && i < n; i++) { stones.Add(Scaled(cells[i], 2.5)); stTris.Add(tris[i]); }
        var r = StoneCellAssignment.Assign(stones, stTris, cells, tris);
        if (r.Placements.Count != n)
            throw new Exception($"all {n} cells must be filled, got {r.Placements.Count}");
        if (r.UnusedStones.Count != stones.Count - n)
            throw new Exception($"expected {stones.Count - n} unused stones, got {r.UnusedStones.Count}");
    }
}
