#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Sequencing;
using Frahan.Core.ScanIngest;

namespace Frahan.Tests;

// P4b real-data benchmark: assign REAL scanned rubble (ETH1100 dry-stone
// dataset, Johns et al., Zenodo 10038881, CC-BY-4.0) to a generated wall's
// cells and measure the imposition index Lambda. Context datum: Clifford &
// McGee's built Cyclopean Cannibalism wall ran at Lambda ~ 0.27 (73% of
// scanned stock used) with the gap carved to zero — a different (carve-back)
// workflow, so the number here is REPORTED for comparison, not gated to it.
// SKIPs cleanly when the dataset is not on this machine (it is gitignored).

/// <summary>Throw from a test body to SKIP (e.g. machine-local dataset missing).</summary>
sealed class SkipTest : Exception
{
    public SkipTest(string reason) : base(reason) { }
}

static class StoneCellAssignmentEthBenchmarkTests
{
    private const string EthDir =
        @"D:\code_ws\Template-General\raw\2026-05-25\eth_drystone\closed\1100 Closed Stone Meshes";

    public static void Lambda_EthRubble_OnGeneratedWall()
    {
        if (!Directory.Exists(EthDir))
            throw new SkipTest("requires the ETH1100 dataset at " + EthDir);

        // ---- inventory: first stones in the Cyclopean-card band (30-200 L) ----
        var stoneCoords = new List<IReadOnlyList<double>>();
        var stoneTris = new List<IReadOnlyList<int>>();
        var files = Directory.GetFiles(EthDir, "*.obj");
        Array.Sort(files, StringComparer.Ordinal);
        double sumVol = 0;
        for (int i = 0; i < files.Length && stoneCoords.Count < 45; i++)
        {
            var meshes = ObjMeshReader.ReadFile(files[i]);
            if (meshes.Count == 0) continue;
            var m = meshes[0];
            double vol = MeshVolume(m.VertexCoordsXyz, m.TriangleIndices);
            if (vol < 0.030 || vol > 0.200) continue; // 30-200 litres
            stoneCoords.Add(m.VertexCoordsXyz);
            stoneTris.Add(m.TriangleIndices);
            sumVol += vol;
        }
        if (stoneCoords.Count < 20)
            throw new SkipTest($"only {stoneCoords.Count} stones in the 30-200 L band among the scanned prefix");

        // ---- targets: a generated wall whose cells sit in the same volume band ----
        // mean stone ~ sumVol/n; wall cells: W*H*depth / (gx*gy) ~ that mean
        double meanVol = sumVol / stoneCoords.Count;
        double depth = 0.30;
        int gx = 5, gy = 3;
        double area = meanVol * gx * gy / depth;       // W*H
        double w = Math.Sqrt(area * 2.0), h = area / w; // ~2:1 panel
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = w, Height = h, GridX = gx, GridY = gy, Coursing = 0.5,
            LloydIterations = 2, SizeGradeCv = 0.30, Seed = 3,
        });
        var cellCoords = new List<IReadOnlyList<double>>();
        var cellTris = new List<IReadOnlyList<int>>();
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
        }

        // ---- assign + report ----
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = StoneCellAssignment.Assign(stoneCoords, stoneTris, cellCoords, cellTris);
        sw.Stop();
        Console.WriteLine($"      [bench] ETH1100 rubble -> {cellCoords.Count}-cell wall: " +
                          $"Lambda={r.ImpositionIndex:0.000} meanGap={r.MeanGapRatio:0.000} " +
                          $"placed={r.Placements.Count} unused={r.UnusedStones.Count} " +
                          $"({stoneCoords.Count} stones, {sw.ElapsedMilliseconds} ms) " +
                          $"[Cyclopean carve-back datum: 0.27]");
        if (r.Placements.Count != cellCoords.Count)
            throw new Exception($"all {cellCoords.Count} cells must be filled, got {r.Placements.Count}");
        if (!(r.ImpositionIndex > 0.0 && r.ImpositionIndex < 0.85))
            throw new Exception($"Lambda out of plausible range for real rubble vs generated cells: " +
                                $"{r.ImpositionIndex:0.000}");
    }

    private static double MeshVolume(IReadOnlyList<double> coords, IReadOnlyList<int> tris)
    {
        double v6 = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
            v6 += coords[a] * (coords[b + 1] * coords[c + 2] - coords[b + 2] * coords[c + 1])
                - coords[a + 1] * (coords[b] * coords[c + 2] - coords[b + 2] * coords[c])
                + coords[a + 2] * (coords[b] * coords[c + 1] - coords[b + 1] * coords[c]);
        }
        return Math.Abs(v6) / 6.0;
    }
}
