#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Core.ScanIngest;
using Frahan.Masonry;
using Rhino.Geometry;

namespace Frahan.Tests;

// P5 benchmark: settle v2 (Furrer support/COM + Johns under-void candidate
// ranking) vs the signed-off legacy objective (stable-then-deepest), on real
// ETH1100 stones. Acceptance per EVOLUTION_PLAN_MASONRY.md P5: v2 must not
// lose stability and should improve seating quality (mean support clearance).
// SKIPs without the dataset; skips via the native-Rhino guard if rhcommon
// cannot initialise in this process.

static class RubbleSettleV2BenchmarkTests
{
    private const string EthDir =
        @"D:\code_ws\Template-General\raw\2026-05-25\eth_drystone\closed\1100 Closed Stone Meshes";

    public static void SettleV2_EthStones_NotWorseStability_BetterSeating()
    {
        if (!Directory.Exists(EthDir))
            throw new SkipTest("requires the ETH1100 dataset at " + EthDir);

        // ---- load 24 stones (the signed-off baseline size) in a sane band ----
        var meshes = new List<Mesh>();
        var files = Directory.GetFiles(EthDir, "*.obj");
        Array.Sort(files, StringComparer.Ordinal);
        for (int i = 0; i < files.Length && meshes.Count < 24; i++)
        {
            var objs = ObjMeshReader.ReadFile(files[i]);
            if (objs.Count == 0) continue;
            var om = objs[0];
            double vol = Volume(om.VertexCoordsXyz, om.TriangleIndices);
            if (vol < 0.020 || vol > 0.250) continue;
            var m = new Mesh();
            var c = om.VertexCoordsXyz;
            for (int v = 0; v + 2 < c.Count; v += 3)
                m.Vertices.Add(c[v], c[v + 1], c[v + 2]);
            var t = om.TriangleIndices;
            for (int f = 0; f + 2 < t.Count; f += 3)
                m.Faces.AddFace(t[f], t[f + 1], t[f + 2]);
            meshes.Add(m);
        }
        if (meshes.Count < 20)
            throw new SkipTest($"only {meshes.Count} stones in band among the scanned prefix");

        // ---- legacy vs v2 ----
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var legacy = RubbleWallSettle.Settle(meshes, 7.0, true, 0.0, 20);
        long tLegacy = sw.ElapsedMilliseconds; sw.Restart();
        var v2 = RubbleWallSettle.Settle(meshes, 7.0, true, 0.0, 20, new SettleV2Options());
        long tV2 = sw.ElapsedMilliseconds;

        Stats(legacy, out int stableL, out double clrL);
        Stats(v2, out int stableV, out double clrV);
        Console.WriteLine($"      [bench] settle ETH x{meshes.Count}: " +
                          $"legacy stable {stableL}/{meshes.Count} meanClr {clrL:0.0000} ({tLegacy} ms) | " +
                          $"v2 stable {stableV}/{meshes.Count} meanClr {clrV:0.0000} ({tV2} ms)");

        if (stableV < stableL)
            throw new Exception($"v2 must not lose stability: {stableV} < {stableL}");
        if (clrV < clrL - 1e-9)
            throw new Exception($"v2 must not worsen mean support clearance: {clrV:0.0000} < {clrL:0.0000}");
    }

    private static void Stats(IList<RubbleStonePlacement> placements, out int stable, out double meanClr)
    {
        stable = 0; double sum = 0; int n = 0;
        foreach (var p in placements)
        {
            if (p.Stable) stable++;
            if (p.Clearance > -0.5) { sum += p.Clearance; n++; }
        }
        meanClr = n > 0 ? sum / n : 0;
    }

    private static double Volume(IReadOnlyList<double> coords, IReadOnlyList<int> tris)
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
