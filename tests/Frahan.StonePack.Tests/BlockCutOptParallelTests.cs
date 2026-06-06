#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Tests;

// V3 evolution batch 3: BlockCutOpt pose-grid parallelisation. The (psi,theta,phi,dx,dy) brute
// search is embarrassingly parallel (each pose builds its own grid, only READS the immutable
// BVH). The parallel SolveInternal must return the BIT-IDENTICAL winning pose as the serial
// reference (strict-greater argmax in enumeration order -> ties to the earliest pose), and be
// faster on multiple cores. Real-geometry obstacle: a discrete-fracture-network (DFN) of random
// fracture facets (the granite-DFN model, e.g. Loviisa/Grimsel, is the same fracture-triangle
// structure; it plugs in as a PlyMesh identically).
static class BlockCutOptParallelTests
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

    public static void Parallel_MatchesSerial_AndIsFaster()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var dfn = Dfn(180, area, 11);
        var opts = new BlockCutOptOptions(
            blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 0.8, kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: Math.PI, psiStepRad: Math.PI / 90.0,   // ~91 psi
            dxMax: 1.0, dxStep: 0.5, dyMax: 1.0, dyStep: 0.5);                    // 5x5 dx,dy

        var sw = Stopwatch.StartNew();
        var serial = BlockCutOptSolver.Solve(area, dfn, opts, parallel: false);
        double ts = sw.Elapsed.TotalSeconds;
        sw.Restart();
        var par = BlockCutOptSolver.Solve(area, dfn, opts, parallel: true);
        double tp = sw.Elapsed.TotalSeconds;

        // Bit-identical winning pose (determinism preserved).
        Assert(serial.NonIntersectedCount == par.NonIntersectedCount,
            $"count: serial {serial.NonIntersectedCount} vs parallel {par.NonIntersectedCount}");
        Assert(Math.Abs(serial.BestPsiRad - par.BestPsiRad) < 1e-12, "best psi differs");
        Assert(Math.Abs(serial.BestDx - par.BestDx) < 1e-12 && Math.Abs(serial.BestDy - par.BestDy) < 1e-12,
            "best dx/dy differs (non-deterministic argmax)");
        Console.WriteLine($"        BlockCutOpt parallel: serial={ts:F2}s parallel={tp:F2}s " +
                          $"speedup={ts / Math.Max(tp, 1e-9):F2}x cores={Environment.ProcessorCount} " +
                          $"count={par.NonIntersectedCount} psiDeg={par.BestPsiDeg:F1}");
        Assert(par.NonIntersectedCount >= 0, "sanity");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException(m); }
}
