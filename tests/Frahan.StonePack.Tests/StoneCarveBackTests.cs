#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Packing;

namespace Frahan.Tests;

// P4c tests: the exact Cyclopean carve-back. Anchor: a stone that is its own
// cell inflated by k, placed identically, intersects to exactly the cell, so
// exact lambda = 1 - 1/k^3 to boolean precision (much tighter than the voxel
// estimate). Booleans go through CgalMeshBoolean (native CGAL inside Rhino,
// managed BSP fallback headless — both volume-validated in the battery).

static class StoneCarveBackTests
{
    public static void CarveBack_InflatedInventory_ExactAnalytic()
    {
        StoneCellAssignmentTestsHelpers.WallCellsAsPrisms(out var cells, out var tris);
        int n = cells.Count;
        var stones = new List<IReadOnlyList<double>>(n);
        for (int i = 0; i < n; i++) stones.Add(StoneCellAssignmentTestsHelpers.Scaled(cells[i], 1.10));

        var assign = StoneCellAssignment.Assign(stones, tris, cells, tris);
        if (assign.Placements.Count != n)
            throw new Exception($"need all {n} placements, got {assign.Placements.Count}");

        var carve = StoneCarveBack.Carve(stones, tris, cells, tris, assign.Placements);
        double analytic = 1.0 - 1.0 / (1.10 * 1.10 * 1.10); // 0.24868...
        if (Math.Abs(carve.ImpositionIndex - analytic) > 0.012)
            throw new Exception($"exact carve-back Lambda must hit the analytic {analytic:0.0000} " +
                                $"to boolean precision; got {carve.ImpositionIndex:0.0000} " +
                                $"(backend {carve.Backend})");
        if (carve.MeanGapRatio > 0.012)
            throw new Exception($"a containing stone leaves no gap; got {carve.MeanGapRatio:0.0000}");
        for (int i = 0; i < carve.CarvedStones.Count; i++)
            if (carve.CarvedStones[i] == null)
                throw new Exception($"boolean failed on placement {i}");
    }

    public static void CarveBack_ExactRefinesVoxelEstimate()
    {
        // exact-vs-voxel agreement on whatever placements the assignment chose
        // (at 1.25x inflation similar-size cells may legitimately swap, so the
        // analytic anchor only applies to IDENTITY placements — next test).
        StoneCellAssignmentTestsHelpers.WallCellsAsPrisms(out var cells, out var tris);
        int n = cells.Count;
        var stones = new List<IReadOnlyList<double>>(n);
        for (int i = 0; i < n; i++) stones.Add(StoneCellAssignmentTestsHelpers.Scaled(cells[i], 1.25));

        var assign = StoneCellAssignment.Assign(stones, tris, cells, tris);
        var carve = StoneCarveBack.Carve(stones, tris, cells, tris, assign.Placements);
        if (Math.Abs(carve.ImpositionIndex - assign.ImpositionIndex) > 0.08)
            throw new Exception($"exact Lambda {carve.ImpositionIndex:0.000} must agree with the voxel " +
                                $"estimate {assign.ImpositionIndex:0.000} within discretisation error");
    }

    public static void CarveBack_IdentityPlacements_ExactAnalytic125()
    {
        // isolates the carve-back boolean: identity placements (a 1.25x scaled-
        // in-place stone contains its own cell) must give lambda = 1 - 1/1.25^3
        // per pair to boolean precision.
        StoneCellAssignmentTestsHelpers.WallCellsAsPrisms(out var cells, out var tris);
        int n = cells.Count;
        var stones = new List<IReadOnlyList<double>>(n);
        for (int i = 0; i < n; i++) stones.Add(StoneCellAssignmentTestsHelpers.Scaled(cells[i], 1.25));

        var identity = new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        var placements = new List<StonePlacement>(n);
        for (int i = 0; i < n; i++)
            placements.Add(new StonePlacement(i, i, 0, 0, 0, identity));

        var carve = StoneCarveBack.Carve(stones, tris, cells, tris, placements);
        double analytic = 1.0 - 1.0 / (1.25 * 1.25 * 1.25); // 0.488
        if (Math.Abs(carve.ImpositionIndex - analytic) > 0.012)
            throw new Exception($"identity carve-back must hit the analytic {analytic:0.000}; " +
                                $"got {carve.ImpositionIndex:0.000} (backend {carve.Backend})");
        if (carve.MeanGapRatio > 0.012)
            throw new Exception($"containing stones leave no gap; got {carve.MeanGapRatio:0.000}");
    }
}

// shared geometry helpers (extracted from StoneCellAssignmentTests for reuse)
static class StoneCellAssignmentTestsHelpers
{
    public static void WallCellsAsPrisms(out List<IReadOnlyList<double>> coords,
                                         out List<IReadOnlyList<int>> tris)
    {
        var gen = Frahan.Masonry.Sequencing.PolygonalWallGenerator.Generate(
            new Frahan.Masonry.Sequencing.WallGenOptions
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
            { ts.Add(0); ts.Add(k); ts.Add(k + 1); ts.Add(m); ts.Add(m + k + 1); ts.Add(m + k); }
            for (int k = 0; k < m; k++)
            {
                int a = k, b = (k + 1) % m;
                ts.Add(a); ts.Add(m + a); ts.Add(m + b);
                ts.Add(a); ts.Add(m + b); ts.Add(b);
            }
            coords.Add(cs); tris.Add(ts);
        }
    }

    public static IReadOnlyList<double> Scaled(IReadOnlyList<double> coords, double k)
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
}
