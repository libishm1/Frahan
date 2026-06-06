#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptParetoSolver -- 4-axis Pareto multi-objective solver.
//
// Replaces the single-scalar Solve() with a brute-force search that scores
// every (psi, dx, dy) sample on four objectives:
//
//   - Recovery     (count of non-intersected blocks)               maximise
//   - Revenue      (sum of per-block RMV)                          maximise
//   - KerfTime     (sum of per-block saw-cut time)                 minimise
//   - BCSdbBV      (cutting surface area / block value, Jalalian)  minimise
//
// Phase 6 of the synthesis roadmap. The Pareto front returned by Solve()
// generally contains 1-50 points for the limestone Stratum-a-sized problems
// used in the regression tests.
// =============================================================================

public static class BlockCutOptParetoSolver
{
    /// <summary>
    /// Run the brute-force search and return the full 4-axis Pareto front.
    /// </summary>
    public static (ParetoFront Front, long Evaluations, TimeSpan Elapsed) Solve(
        BoundingBox3 testedArea,
        PlyMesh fractures,
        BlockCutOptOptions opts,
        BlockValueModel valueModel = null)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (opts == null) throw new ArgumentNullException(nameof(opts));
        valueModel = valueModel ?? BlockValueModel.Default;

        var sw = Stopwatch.StartNew();
        var bvh = TriangleAabbBvh.Build(fractures);
        var front = new ParetoFront();
        long evals = 0;

        for (double psi = opts.PsiStartRad; psi <= opts.PsiStopRad + 1e-9; psi += opts.PsiStepRad)
        {
            for (double dx = -opts.DxMax; dx <= opts.DxMax + 1e-9; dx += opts.DxStep)
            {
                for (double dy = -opts.DyMax; dy <= opts.DyMax + 1e-9; dy += opts.DyStep)
                {
                    evals++;
                    var grid = CuttingGrid.Generate(
                        testedArea,
                        opts.BlockSizeX, opts.BlockSizeY, opts.BlockSizeZ,
                        opts.Kerf, psi, dx, dy);

                    var pt = EvaluatePoint(grid, bvh, opts, valueModel, psi, dx, dy);
                    front.Insert(in pt);
                }
            }
        }

        sw.Stop();
        return (front, evals, sw.Elapsed);
    }

    private static ParetoPoint EvaluatePoint(
        IReadOnlyList<OrientedBlock> grid,
        TriangleAabbBvh bvh,
        BlockCutOptOptions opts,
        BlockValueModel model,
        double psi, double dx, double dy)
    {
        int count = 0;
        double revenue = 0;
        double kerfTime = 0;
        double cutArea = 0;
        double totalBv = 0;

        for (int b = 0; b < grid.Count; b++)
        {
            var obb = grid[b];
            if (bvh.AnyTriangleIntersects(in obb)) continue;
            count++;
            revenue += model.RmvPerBlock;
            kerfTime += model.KerfTimeMinPerBlock;
            cutArea += BlockValueModel.SurfaceArea(in obb);
            totalBv += model.BvPerBlock;
        }

        double bcsdbBv = totalBv > 1e-12
            ? cutArea / totalBv
            : double.PositiveInfinity;

        return new ParetoPoint(count, revenue, kerfTime, bcsdbBv, psi, dx, dy);
    }
}
