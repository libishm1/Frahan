#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Core.Earthworks;

namespace Frahan.Tests;

// Headless tests for the deferred-roadmap Core: TinPeelFilter (A2), TinMerge (A3),
// BedrockSurface (A9). All synthetic / self-contained.
public static class EarthworksTinTests
{
    // --- TinPeelFilter removes a long "cap" spike triangle on a dense grid ---
    public static void TinPeel_RemovesCapSpike()
    {
        // dense 6x6 unit grid (spacing 1) -> many short triangles
        int n = 6;
        var xyz = new List<double>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) { xyz.Add(i); xyz.Add(j); xyz.Add(0.0); }
        int Idx(int i, int j) => j * n + i;
        var tris = new List<int>();
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                tris.Add(Idx(i, j)); tris.Add(Idx(i + 1, j)); tris.Add(Idx(i + 1, j + 1));
                tris.Add(Idx(i, j)); tris.Add(Idx(i + 1, j + 1)); tris.Add(Idx(i, j + 1));
            }
        int dense = tris.Count / 3;
        // add a far spike vertex + a long thin cap triangle off the border
        int spike = xyz.Count / 3;
        xyz.Add(40.0); xyz.Add(2.5); xyz.Add(0.0);   // far away -> long edges
        tris.Add(Idx(n - 1, 2)); tris.Add(Idx(n - 1, 3)); tris.Add(spike);

        var opt = new TinPeelOptions { MinComponentTriangles = 1 };  // keep the grid component
        var r = TinPeelFilter.Filter(xyz, tris, opt);
        Assert(r.RemovedByPeel >= 1, "cap spike not peeled");
        Assert(r.KeptCount == dense, $"expected {dense} dense tris kept, got {r.KeptCount}");
        // the spike vertex must not survive in any kept triangle
        Assert(!r.KeptTriangles.Contains(spike), "spike vertex still referenced after peel");
    }

    // --- TinPeelFilter drops a tiny disconnected component ---
    public static void TinPeel_DropsTinyComponent()
    {
        // big component: 4x4 grid; tiny component: a single far triangle island
        int n = 4;
        var xyz = new List<double>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) { xyz.Add(i); xyz.Add(j); xyz.Add(0.0); }
        int Idx(int i, int j) => j * n + i;
        var tris = new List<int>();
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                tris.Add(Idx(i, j)); tris.Add(Idx(i + 1, j)); tris.Add(Idx(i + 1, j + 1));
                tris.Add(Idx(i, j)); tris.Add(Idx(i + 1, j + 1)); tris.Add(Idx(i, j + 1));
            }
        int big = tris.Count / 3;
        int b = xyz.Count / 3;
        xyz.Add(10); xyz.Add(10); xyz.Add(0); xyz.Add(10.5); xyz.Add(10); xyz.Add(0); xyz.Add(10); xyz.Add(10.5); xyz.Add(0);
        tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);   // tiny island
        var opt = new TinPeelOptions { MinComponentTriangles = 4, UseLongEdge = false, UseVerticality = false, UseCapAngle = false };
        var r = TinPeelFilter.Filter(xyz, tris, opt);
        Assert(r.RemovedBySize == 1, $"tiny island not size-dropped (got {r.RemovedBySize})");
        Assert(r.KeptCount == big, $"expected {big} kept, got {r.KeptCount}");
    }

    // --- TinMerge IDW recovers a known planar bedrock surface at target vertices ---
    public static void TinMerge_IdwRecoversPlane()
    {
        // source picks sample a tilted plane z = 2 + 0.5x + 0.25y on a coarse grid
        double Plane(double x, double y) => 2.0 + 0.5 * x + 0.25 * y;
        var src = new List<double>();
        for (int j = 0; j <= 10; j += 2)
            for (int i = 0; i <= 10; i += 2) { src.Add(i); src.Add(j); src.Add(Plane(i, j)); }
        // target vertices at off-grid points inside the hull
        var tgt = new List<double>();
        var probes = new (double, double)[] { (3, 3), (5, 5), (7, 2), (4.5, 6.5) };
        foreach (var (x, y) in probes) { tgt.Add(x); tgt.Add(y); tgt.Add(0); }
        var r = TinMerge.ResampleOntoVertices(tgt, src, new TinMergeOptions { Neighbors = 8 });
        Assert(r.Unresolved == 0, "some target vertices unresolved inside the hull");
        for (int v = 0; v < probes.Length; v++)
        {
            double expect = Plane(probes[v].Item1, probes[v].Item2);
            Assert(Math.Abs(r.Z[v] - expect) < 0.25,
                $"IDW off at probe {v}: got {r.Z[v]:F3}, expect {expect:F3}");
        }
    }

    // --- TinMerge flags targets with no source nearby ---
    public static void TinMerge_FlagsOutOfRange()
    {
        var src = new List<double> { 0, 0, 1, 1, 0, 1, 0, 1, 1, 1, 1, 1 };  // 4 picks near origin
        var tgt = new List<double> { 0.5, 0.5, 0, 1000, 1000, 0 };          // one near, one far
        var r = TinMerge.ResampleOntoVertices(tgt, src, new TinMergeOptions());
        Assert(r.Valid[0], "near target should resolve");
        Assert(!r.Valid[1], "far target should be flagged unresolved");
        Assert(double.IsNaN(r.Z[1]), "far target z should be NaN");
    }

    // --- BedrockSurface keeps the deepest reflector per column + datum shift ---
    public static void Bedrock_DeepestPerColumn_DatumShift()
    {
        // two picks at x=5: shallow (1 m) and deep (8 m); deep one is bedrock.
        var picks = new List<BedrockPick>
        {
            new BedrockPick(5, 0, 1.0, 0.9),
            new BedrockPick(5, 0, 8.0, 0.8),
            new BedrockPick(6, 0, 7.0, 0.7),
        };
        // ground at z = 100 everywhere -> z_r = 100 - depth
        var pts = BedrockSurface.DeepestReflectorPoints(picks, (x, y) => 100.0,
            new BedrockSurfaceOptions { ColumnCell = 0.5 });
        // expect 2 columns (x=5 and x=6). x=5 must use depth 8 -> z_r = 92.
        Assert(pts.Length == 6, $"expected 2 bedrock points, got {pts.Length / 3}");
        // find the x=5 point
        bool found = false;
        for (int i = 0; i < pts.Length; i += 3)
            if (Math.Abs(pts[i] - 5.0) < 1e-6) { found = true; Assert(Math.Abs(pts[i + 2] - 92.0) < 1e-6, $"x=5 z_r should be 92, got {pts[i + 2]}"); }
        Assert(found, "x=5 column missing");
    }

    // --- BedrockSurface -> TinMerge end-to-end onto a ground TIN ---
    public static void Bedrock_ToCommonTin_EndToEnd()
    {
        // ground = flat 4x4 grid at z=50; bedrock picks 5 m down across the grid
        int n = 4;
        var ground = new List<double>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) { ground.Add(i); ground.Add(j); ground.Add(50.0); }
        var picks = new List<BedrockPick>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) picks.Add(new BedrockPick(i, j, 5.0, 0.9));
        var r = BedrockSurface.ToCommonTin(picks, ground);
        Assert(r.Unresolved == 0, "ground vertices should all resolve a bedrock depth");
        // ToCommonTin resamples DEPTH (~5 m everywhere)
        for (int v = 0; v < r.Z.Length; v++)
            Assert(Math.Abs(r.Z[v] - 5.0) < 0.5, $"depth at vertex {v} = {r.Z[v]:F3}, expect ~5");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("ASSERT FAILED: " + msg);
    }
}
