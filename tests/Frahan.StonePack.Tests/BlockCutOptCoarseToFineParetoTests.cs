#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Tests;

// Phase 4 evolution: the Omni solver's Pareto-aware coarse-to-fine angular search
// (12 -> 3 -> 0.5 deg, top-K seeded) replaces the former no-op that ran a full uniform
// fine sweep. It must (1) evaluate strictly fewer poses than the exhaustive uniform fine
// sweep, (2) be deterministic, and (3) stay near-optimal on recovery (it keeps every
// coarse-non-dominated point and refines around the top-K, so it is a quality heuristic,
// not exhaustive). Same synthetic-DFN fixture as the parallel determinism test.
static class BlockCutOptCoarseToFineParetoTests
{
    private static PlyMesh Dfn(int nFacets, BoundingBox3 area, int seed)
    {
        var rng = new Random(seed);
        var v = new List<double>(nFacets * 9);
        var t = new List<int>(nFacets * 3);
        for (int k = 0; k < nFacets; k++)
        {
            double cx = area.MinX + rng.NextDouble() * area.SizeX;
            double cy = area.MinY + rng.NextDouble() * area.SizeY;
            double cz = area.MinZ + rng.NextDouble() * area.SizeZ;
            double r = 0.5 + rng.NextDouble() * 2.5;
            int b = v.Count / 3;
            for (int j = 0; j < 3; j++)
            {
                double a = 2 * Math.PI * (j + rng.NextDouble()) / 3.0;
                v.Add(cx + r * Math.Cos(a)); v.Add(cy + r * Math.Sin(a)); v.Add(cz + (rng.NextDouble() - 0.5) * r);
            }
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
        }
        return new PlyMesh(v, t, null);
    }

    public static void CoarseToFine_FewerEvals_Deterministic_NearOptimal()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var dfn = Dfn(180, area, 11);
        var search = new BlockCutOptOptions(
            blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 0.8, kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: Math.PI, psiStepRad: 0.5 * Math.PI / 180.0, // exhaustive: 0.5 deg
            dxMax: 1.0, dxStep: 0.5, dyMax: 1.0, dyStep: 0.5);

        var exhaustive = new OmniSolverOptions { Search = search, UseCoarseToFine = false };
        var c2fOpts = new OmniSolverOptions { Search = search, UseCoarseToFine = true }; // 12 -> 3 -> 0.5

        var rEx = BlockCutOptOmniSolver.Solve(area, dfn, exhaustive);
        var rC2f = BlockCutOptOmniSolver.Solve(area, dfn, c2fOpts);
        var rC2fb = BlockCutOptOmniSolver.Solve(area, dfn, c2fOpts);

        // (1) fewer evaluations than the exhaustive uniform fine sweep
        Assert(rC2f.TotalEvaluations < rEx.TotalEvaluations,
            $"coarse-to-fine did not reduce evals: c2f {rC2f.TotalEvaluations} vs exhaustive {rEx.TotalEvaluations}");

        // (2) deterministic
        Assert(rC2f.AggregateRecoveryCount == rC2fb.AggregateRecoveryCount &&
               rC2f.TotalEvaluations == rC2fb.TotalEvaluations,
            "coarse-to-fine is non-deterministic across runs");

        // (3) near-optimal recovery vs exhaustive (heuristic: keeps all coarse points + refines top-K)
        int ex = rEx.AggregateRecoveryCount;
        int c2f = rC2f.AggregateRecoveryCount;
        Assert(c2f >= ex - Math.Max(1, ex / 10),
            $"coarse-to-fine recovery too far below exhaustive: c2f {c2f} vs exhaustive {ex}");

        Console.WriteLine($"        BlockCutOpt c2f: evals {rC2f.TotalEvaluations} vs exhaustive {rEx.TotalEvaluations} " +
            $"({(double)rEx.TotalEvaluations / Math.Max(rC2f.TotalEvaluations, 1):F1}x fewer); " +
            $"recovery c2f={c2f} exhaustive={ex}");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException(m); }
}
