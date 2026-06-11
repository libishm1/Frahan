#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Sequencing;
using Frahan.Core.ScanIngest;

namespace Frahan.Tests;

// The Lambda flagship study (EVOLUTION_PLAN item 1): the imposition-gap
// landscape on REAL ETH1100 rubble across the Coursing continuum, with
// assignment baselines. Hungarian = StoneCellAssignment (the shipped engine);
// volume-greedy and random are honest weaker baselines (labelled as such —
// volume-greedy matches |vol_stone/vol_cell - 1| without the shape term).
// REPORTED, not gated (beyond basic sanity): the numbers feed
// docs/benchmarks/LAMBDA_STUDY.md. SKIPs without the dataset.

static class LambdaStudyBenchmarkTests
{
    private const string EthDir =
        @"D:\code_ws\Template-General\raw\2026-05-25\eth_drystone\closed\1100 Closed Stone Meshes";

    public static void LambdaStudy_CoursingByAssigner_Table()
    {
        if (!Directory.Exists(EthDir))
            throw new SkipTest("requires the ETH1100 dataset at " + EthDir);

        // inventory: 60 stones in the 30-200 L band
        var stoneCoords = new List<IReadOnlyList<double>>();
        var stoneTris = new List<IReadOnlyList<int>>();
        var stoneVols = new List<double>();
        var files = Directory.GetFiles(EthDir, "*.obj");
        Array.Sort(files, StringComparer.Ordinal);
        for (int i = 0; i < files.Length && stoneCoords.Count < 60; i++)
        {
            var meshes = ObjMeshReader.ReadFile(files[i]);
            if (meshes.Count == 0) continue;
            var m = meshes[0];
            double vol = Volume(m.VertexCoordsXyz, m.TriangleIndices);
            if (vol < 0.030 || vol > 0.200) continue;
            stoneCoords.Add(m.VertexCoordsXyz);
            stoneTris.Add(m.TriangleIndices);
            stoneVols.Add(vol);
        }
        if (stoneCoords.Count < 30)
            throw new SkipTest($"only {stoneCoords.Count} stones in band");
        double meanVol = stoneVols.Average();

        Console.WriteLine("      [bench] LAMBDA STUDY — 60 ETH stones, walls scaled to mean stone volume");
        Console.WriteLine("      [bench] coursing grid | Hungarian L/gap | vol-greedy L/gap | random L/gap | ms(H)");
        var rng = new Random(11);
        foreach (var coursing in new[] { 0.0, 0.5, 1.0 })
            foreach (var (gx, gy) in new[] { (4, 3), (6, 4) })
            {
                double depth = 0.30;
                double area = meanVol * gx * gy / depth;
                double w = Math.Sqrt(area * 2.0), h = area / w;
                var gen = PolygonalWallGenerator.Generate(new WallGenOptions
                {
                    Width = w, Height = h, GridX = gx, GridY = gy, Coursing = coursing,
                    LloydIterations = 2, SizeGradeCv = 0.30, Seed = 3,
                });
                var (cellCoords, cellTris, cellVols) = CellsAsPrisms(gen, depth);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var hung = StoneCellAssignment.Assign(stoneCoords, stoneTris, cellCoords, cellTris);
                long ms = sw.ElapsedMilliseconds;

                // volume-greedy: per cell (desc volume) pick the unused stone with
                // the closest volume; lambda proxy via the SAME exact carve-back
                var (gL, gGap) = GreedyByVolume(stoneCoords, stoneTris, stoneVols, cellCoords, cellTris, cellVols);
                // random (3 draws, averaged)
                double rL = 0, rGap = 0;
                for (int d = 0; d < 3; d++)
                {
                    var (l1, g1) = RandomAssign(stoneCoords, stoneTris, cellCoords, cellTris, rng);
                    rL += l1; rGap += g1;
                }
                rL /= 3; rGap /= 3;

                Console.WriteLine($"      [bench]  {coursing,3:0.0} {gx}x{gy}   | " +
                    $"{hung.ImpositionIndex:0.000}/{hung.MeanGapRatio:0.000}      | " +
                    $"{gL:0.000}/{gGap:0.000}       | {rL:0.000}/{rGap:0.000}  | {ms}");
                if (hung.ImpositionIndex < 0 || hung.ImpositionIndex > 1)
                    throw new Exception("Lambda out of [0,1]");
            }
    }

    // greedy + random both score via the EXACT carve-back so the comparison is
    // apples-to-apples on the final metric (the engines differ only in matching)
    private static (double lambda, double gap) GreedyByVolume(
        List<IReadOnlyList<double>> sc, List<IReadOnlyList<int>> st, List<double> sv,
        List<IReadOnlyList<double>> cc, List<IReadOnlyList<int>> ct, List<double> cv)
    {
        int n = cc.Count;
        var order = Enumerable.Range(0, n).OrderByDescending(i => cv[i]).ToList();
        var used = new bool[sc.Count];
        var placements = new List<StonePlacement>();
        var identity = new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        foreach (var ci in order)
        {
            int best = -1; double bestD = double.MaxValue;
            for (int s = 0; s < sc.Count; s++)
            {
                if (used[s]) continue;
                double d = Math.Abs(sv[s] / cv[ci] - 1.0);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best < 0) break;
            used[best] = true;
            placements.Add(CenteredPlacement(sc[best], best, cc[ci], ci));
        }
        var carve = StoneCarveBack.Carve(sc, st, cc, ct, placements);
        return (carve.ImpositionIndex, carve.MeanGapRatio);
    }

    private static (double lambda, double gap) RandomAssign(
        List<IReadOnlyList<double>> sc, List<IReadOnlyList<int>> st,
        List<IReadOnlyList<double>> cc, List<IReadOnlyList<int>> ct, Random rng)
    {
        var stones = Enumerable.Range(0, sc.Count).OrderBy(_ => rng.Next()).Take(cc.Count).ToList();
        var placements = new List<StonePlacement>();
        for (int ci = 0; ci < cc.Count && ci < stones.Count; ci++)
            placements.Add(CenteredPlacement(sc[stones[ci]], stones[ci], cc[ci], ci));
        var carve = StoneCarveBack.Carve(sc, st, cc, ct, placements);
        return (carve.ImpositionIndex, carve.MeanGapRatio);
    }

    // translate the stone's centroid onto the cell's centroid (no rotation):
    // the simplest placement both baselines share
    private static StonePlacement CenteredPlacement(
        IReadOnlyList<double> stone, int si, IReadOnlyList<double> cell, int ci)
    {
        var (sx, sy, sz) = Centroid(stone);
        var (cx, cy, cz) = Centroid(cell);
        var t = new double[] { 1, 0, 0, cx - sx, 0, 1, 0, cy - sy, 0, 0, 1, cz - sz, 0, 0, 0, 1 };
        return new StonePlacement(si, ci, 0, 0, 0, t);
    }

    private static (double, double, double) Centroid(IReadOnlyList<double> c)
    {
        double x = 0, y = 0, z = 0; int n = c.Count / 3;
        for (int i = 0; i < n; i++) { x += c[i * 3]; y += c[i * 3 + 1]; z += c[i * 3 + 2]; }
        return (x / n, y / n, z / n);
    }

    private static (List<IReadOnlyList<double>>, List<IReadOnlyList<int>>, List<double>) CellsAsPrisms(
        WallGenResult gen, double depth)
    {
        var cellCoords = new List<IReadOnlyList<double>>();
        var cellTris = new List<IReadOnlyList<int>>();
        var vols = new List<double>();
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
            cellCoords.Add(cs); cellTris.Add(ts);
            vols.Add(Volume(cs, ts));
        }
        return (cellCoords, cellTris, vols);
    }

    private static double Volume(IReadOnlyList<double> c, IReadOnlyList<int> t)
    {
        double v6 = 0;
        for (int i = 0; i < t.Count; i += 3)
        {
            int a = t[i] * 3, b = t[i + 1] * 3, d = t[i + 2] * 3;
            v6 += c[a] * (c[b + 1] * c[d + 2] - c[b + 2] * c[d + 1])
                - c[a + 1] * (c[b] * c[d + 2] - c[b + 2] * c[d])
                + c[a + 2] * (c[b] * c[d + 1] - c[b + 1] * c[d]);
        }
        return Math.Abs(v6) / 6.0;
    }
}
