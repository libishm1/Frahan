#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Sequencing;

namespace Frahan.Tests;

// Headless tests for the PolygonalWallGenerator (evolution P3, 2026-06-10):
// power-diagram tiling, determinism, Lloyd uniformity, sliver cull, interlock
// score range. Pure managed; no Rhino runtime needed.

static class PolygonalWallGeneratorTests
{
    public static void Generate_TilesTheRectangle()
    {
        var r = PolygonalWallGenerator.Generate(new WallGenOptions());
        if (r.Cells.Count < 20)
            throw new Exception($"expected >= 20 cells for an 8x5 grid, got {r.Cells.Count}");
        if (Math.Abs(r.AreaCoverage - 1.0) > 1e-6)
            throw new Exception($"power diagram must tile the rectangle; coverage = {r.AreaCoverage:0.000000}");
        for (int i = 0; i < r.Cells.Count; i++)
        {
            if (r.Cells[i].Area <= 0) throw new Exception($"cell {i} has non-positive area");
            if (r.Cells[i].VertexCount < 3) throw new Exception($"cell {i} has < 3 vertices");
        }
    }

    public static void Generate_IsDeterministic()
    {
        var a = PolygonalWallGenerator.Generate(new WallGenOptions { Seed = 11 });
        var b = PolygonalWallGenerator.Generate(new WallGenOptions { Seed = 11 });
        if (a.Cells.Count != b.Cells.Count)
            throw new Exception("same seed must give the same cell count");
        if (Math.Abs(a.InterlockScore - b.InterlockScore) > 1e-12)
            throw new Exception("same seed must give the same interlock score");
        if (Math.Abs(a.Cells[0].CentroidU - b.Cells[0].CentroidU) > 1e-12)
            throw new Exception("same seed must give identical geometry");
    }

    public static void Generate_LloydReducesAreaSpread()
    {
        var o0 = new WallGenOptions { LloydIterations = 0, SizeGradeCv = 0.0, SliverMinInradiusFrac = 0.0, Coursing = 0.0, Seed = 5 };
        var o3 = new WallGenOptions { LloydIterations = 3, SizeGradeCv = 0.0, SliverMinInradiusFrac = 0.0, Coursing = 0.0, Seed = 5 };
        var r0 = PolygonalWallGenerator.Generate(o0);
        var r3 = PolygonalWallGenerator.Generate(o3);
        if (!(r3.AreaCv < r0.AreaCv))
            throw new Exception(
                $"Lloyd relaxation must reduce the cell-area CV: cv(0 it)={r0.AreaCv:0.0000}, cv(3 it)={r3.AreaCv:0.0000}");
    }

    public static void Generate_SliverCullOffReportsZero()
    {
        var r = PolygonalWallGenerator.Generate(new WallGenOptions { SliverMinInradiusFrac = 0.0 });
        if (r.CulledSlivers != 0)
            throw new Exception($"with the cull disabled CulledSlivers must be 0, got {r.CulledSlivers}");
    }

    public static void Generate_InterlockScoreInRange_AndCoursingExtremesValid()
    {
        foreach (double c in new[] { 0.0, 0.5, 1.0 })
        {
            var r = PolygonalWallGenerator.Generate(new WallGenOptions { Coursing = c, Seed = 3 });
            if (r.InterlockScore < 0.0 || r.InterlockScore > 1.0)
                throw new Exception($"interlock score must be in [0,1], got {r.InterlockScore} at coursing {c}");
            if (Math.Abs(r.AreaCoverage - 1.0) > 1e-6)
                throw new Exception($"coverage broke at coursing {c}: {r.AreaCoverage:0.000000}");
        }
    }

    public static void Generate_SizeGradingIncreasesAreaSpread()
    {
        var flat = PolygonalWallGenerator.Generate(new WallGenOptions { SizeGradeCv = 0.0, SliverMinInradiusFrac = 0.0, LloydIterations = 2, Seed = 9 });
        var graded = PolygonalWallGenerator.Generate(new WallGenOptions { SizeGradeCv = 0.6, SliverMinInradiusFrac = 0.0, LloydIterations = 2, Seed = 9 });
        if (!(graded.AreaCv > flat.AreaCv))
            throw new Exception(
                $"size grading must widen the area distribution: cv(0)={flat.AreaCv:0.0000}, cv(0.6)={graded.AreaCv:0.0000}");
    }
}

// End-to-end tests for MasonryStabilityChecker (evolution P1, 2026-06-10):
// stones -> contacts -> assembly -> RBE QP -> verdict, plus the inscribed
// friction-pyramid fix. Pure managed; no Rhino runtime needed.

static class MasonryStabilityCheckerTests
{
    // Axis-aligned box mesh with outward-facing triangles.
    private static void Box(double x0, double y0, double z0, double x1, double y1, double z1,
                            out List<double> coords, out List<int> tris)
    {
        coords = new List<double>
        {
            x0,y0,z0,  x1,y0,z0,  x1,y1,z0,  x0,y1,z0,
            x0,y0,z1,  x1,y0,z1,  x1,y1,z1,  x0,y1,z1,
        };
        tris = new List<int>
        {
            0,2,1, 0,3,2,   // bottom (-Z)
            4,5,6, 4,6,7,   // top (+Z)
            0,1,5, 0,5,4,   // front (-Y)
            2,3,7, 2,7,6,   // back (+Y)
            0,4,7, 0,7,3,   // left (-X)
            1,2,6, 1,6,5,   // right (+X)
        };
    }

    public static void TwoBoxStack_IsStable()
    {
        Box(0, 0, 0.0, 1, 1, 0.5, out var c0, out var t0);
        Box(0, 0, 0.5, 1, 1, 1.0, out var c1, out var t1);
        var r = Frahan.Masonry.Solvers.MasonryStabilityChecker.CheckMeshes(
            new[] { (IReadOnlyList<double>)c0, c1 },
            new[] { (IReadOnlyList<int>)t0, t1 });
        if (!r.IsStable)
            throw new Exception($"a plain two-box stack must be RBE-stable; got {r.Status}: {r.Message}");
        if (r.InterfaceCount < 1)
            throw new Exception("contact detector found no interface in the stack");
    }

    public static void FloatingBlock_IsUnstable()
    {
        Box(0, 0, 0.0, 1, 1, 0.5, out var c0, out var t0);
        Box(0, 0, 2.0, 1, 1, 2.5, out var c1, out var t1); // air gap, no contact
        var r = Frahan.Masonry.Solvers.MasonryStabilityChecker.CheckMeshes(
            new[] { (IReadOnlyList<double>)c0, c1 },
            new[] { (IReadOnlyList<int>)t0, t1 });
        if (r.IsStable)
            throw new Exception("a floating block must not be reported stable");
    }

    public static void CantileverBeyondSupport_IsUnstable()
    {
        // Support occupies x in [0,1]; the top box sits on the strip x in
        // [0.8, 1.0] but its COM is at x = 1.3 — outside the contact patch.
        // No-tension RBE must be infeasible (it would need a tensile pull-down
        // at the inner contact edge).
        Box(0.0, 0, 0.0, 1.0, 1, 0.5, out var c0, out var t0);
        Box(0.8, 0, 0.5, 1.8, 1, 1.0, out var c1, out var t1);
        var r = Frahan.Masonry.Solvers.MasonryStabilityChecker.CheckMeshes(
            new[] { (IReadOnlyList<double>)c0, c1 },
            new[] { (IReadOnlyList<int>)t0, t1 });
        if (r.IsStable)
            throw new Exception("a cantilever with COM beyond the support polygon must be RBE-unstable");
    }

    public static void InscribedFriction_ShrinksMuByCosPiOverK()
    {
        Box(0, 0, 0.0, 1, 1, 0.5, out var c0, out var t0);
        Box(0, 0, 0.5, 1, 1, 1.0, out var c1, out var t1);
        var snaps = new List<Frahan.Masonry.Interfaces.MeshSnapshot>
        {
            new Frahan.Masonry.Interfaces.MeshSnapshot(c0, t0),
            new Frahan.Masonry.Interfaces.MeshSnapshot(c1, t1),
        };
        var ids = new List<string> { "a", "b" };
        var ifaces = Frahan.Masonry.Interfaces.MeshContactDetector.Detect(snaps, ids);
        var blocks = new List<Frahan.Masonry.DataModel.MasonryBlock>
        {
            new Frahan.Masonry.DataModel.MasonryBlock("a", c0, t0, 2400),
            new Frahan.Masonry.DataModel.MasonryBlock("b", c1, t1, 2400),
        };
        var asm = new Frahan.Masonry.DataModel.MasonryAssembly(
            blocks, ifaces, new Frahan.Masonry.DataModel.BoundaryConditions(new[] { "a" }));
        var eq = Frahan.Masonry.Equilibrium.EquilibriumMatrixBuilder.Build(asm);

        double mu = 0.84;
        var circ = Frahan.Masonry.Equilibrium.FrictionConeBuilder.Build(eq, mu, 4, inscribed: false);
        var insc = Frahan.Masonry.Equilibrium.FrictionConeBuilder.Build(eq, mu, 4, inscribed: true);
        if (Math.Abs(circ.Mu - mu) > 1e-12)
            throw new Exception($"circumscribed (default) must keep mu = {mu}, got {circ.Mu}");
        double expected = mu * Math.Cos(Math.PI / 4);
        if (Math.Abs(insc.Mu - expected) > 1e-12)
            throw new Exception($"inscribed K=4 must give mu*cos(pi/4) = {expected:0.000000}, got {insc.Mu:0.000000}");
    }

    public static void GeneratedWall_40Stones_StableAndFast()
    {
        // P1.1 sparse-ADMM scale benchmark: the 8x5 wall (the live HITL case)
        // took 284 s with the dense solver; the CSR rewrite must keep it in
        // interactive territory. Also guards correctness at wall scale.
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = 3.0, Height = 1.8, GridX = 8, GridY = 5, Coursing = 0.4,
            LloydIterations = 2, SizeGradeCv = 0.30, Seed = 7,
        });
        BuildPrisms(gen, 0.25, out var coordsList, out var trisList);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Frahan.Masonry.Solvers.MasonryStabilityChecker.CheckMeshes(
            coordsList, trisList,
            contactDistanceTol: 5e-3, contactAngleTolDeg: 8.0, fixBelowZ: 0.02);
        sw.Stop();
        Console.WriteLine($"      [bench] 40-stone wall: {(r.IsStable ? "STABLE" : "NOT STABLE")} " +
                          $"in {sw.ElapsedMilliseconds} ms ({r.InterfaceCount} ifaces, {r.ContactVertexCount} verts)");
        if (!r.IsStable)
            throw new Exception($"the 40-stone coursed wall must be RBE-stable; got {r.Status}: {r.Message}");
        if (sw.ElapsedMilliseconds > 120_000)
            throw new Exception($"40-stone stability took {sw.ElapsedMilliseconds} ms — sparse path regressed");
    }

    public static void GeneratedWall_AdjacencyAssembler_StableAndLean()
    {
        // P1.2: exact generator-adjacency joints (one planar quad per adjacent
        // stone pair) instead of detector-splintered contacts. Must be stable,
        // leaner than the detector path, and tolerance-free.
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = 3.0, Height = 1.8, GridX = 8, GridY = 5, Coursing = 0.4,
            LloydIterations = 2, SizeGradeCv = 0.30, Seed = 7,
        });
        var wall = PolygonalWallAssembler.Build(
            gen,
            (u, v) => new[] { u, 0.0, v },          // flat XZ panel
            (u, v) => new[] { 0.0, 1.0, 0.0 },      // +Y normal
            depth: 0.25);
        int ifaces = wall.Assembly.Interfaces.Count;
        int verts = 0;
        foreach (var i in wall.Assembly.Interfaces) verts += i.ContactPolygon.Count;
        if (ifaces < gen.Cells.Count - 1)
            throw new Exception($"adjacency joints look under-extracted: {ifaces} interfaces for {gen.Cells.Count} stones");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Frahan.Masonry.Solvers.MasonryStabilityChecker.Check(wall.Assembly);
        sw.Stop();
        Console.WriteLine($"      [bench] 40-stone wall (adjacency joints): {(r.IsStable ? "STABLE" : "NOT STABLE")} " +
                          $"in {sw.ElapsedMilliseconds} ms ({ifaces} ifaces, {verts} verts vs detector 125/612)");
        if (!r.IsStable)
            throw new Exception($"the adjacency-assembled wall must be RBE-stable; got {r.Status}: {r.Message}");
        if (verts >= 612)
            throw new Exception($"adjacency joints must be leaner than the detector ({verts} vs 612 contact vertices)");
    }

    private static void BuildPrisms(WallGenResult gen, double depth,
        out List<IReadOnlyList<double>> coordsList, out List<IReadOnlyList<int>> trisList)
    {
        coordsList = new List<IReadOnlyList<double>>();
        trisList = new List<IReadOnlyList<int>>();
        foreach (var cell in gen.Cells)
        {
            int m = cell.VertexCount;
            var coords = new List<double>(m * 6);
            for (int k = 0; k < m; k++) { coords.Add(cell.Us[k]); coords.Add(0.0); coords.Add(cell.Vs[k]); }
            for (int k = 0; k < m; k++) { coords.Add(cell.Us[k]); coords.Add(depth); coords.Add(cell.Vs[k]); }
            var tris = new List<int>();
            for (int k = 1; k + 1 < m; k++)
            {
                tris.Add(0); tris.Add(k); tris.Add(k + 1);
                tris.Add(m); tris.Add(m + k + 1); tris.Add(m + k);
            }
            for (int k = 0; k < m; k++)
            {
                int a = k, b = (k + 1) % m;
                tris.Add(a); tris.Add(m + a); tris.Add(m + b);
                tris.Add(a); tris.Add(m + b); tris.Add(b);
            }
            coordsList.Add(coords); trisList.Add(tris);
        }
    }

    public static void GeneratedWall_PrismStones_AreStable()
    {
        // End-to-end: generate a small coursed wall pattern, extrude each cell
        // to a flat-backed prism in XZ (depth in Y), and verify the assembly.
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = 2.0, Height = 1.0, GridX = 4, GridY = 3, Coursing = 1.0,
            LloydIterations = 2, SizeGradeCv = 0.0, Seed = 4,
        });
        var coordsList = new List<IReadOnlyList<double>>();
        var trisList = new List<IReadOnlyList<int>>();
        double depth = 0.3;
        for (int ci = 0; ci < gen.Cells.Count; ci++)
        {
            var cell = gen.Cells[ci];
            int m = cell.VertexCount;
            var coords = new List<double>(m * 6);
            // front ring (y = 0), back ring (y = depth); cell (u,v) -> (x,z)
            for (int k = 0; k < m; k++) { coords.Add(cell.Us[k]); coords.Add(0.0); coords.Add(cell.Vs[k]); }
            for (int k = 0; k < m; k++) { coords.Add(cell.Us[k]); coords.Add(depth); coords.Add(cell.Vs[k]); }
            var tris = new List<int>();
            for (int k = 1; k + 1 < m; k++)
            {
                // CCW cell in (u,v) -> (x,z): cell-order fan faces -Y (front outward);
                // the back cap reverses to face +Y.
                tris.Add(0); tris.Add(k); tris.Add(k + 1);             // front fan (-Y outward)
                tris.Add(m); tris.Add(m + k + 1); tris.Add(m + k);     // back fan (+Y outward)
            }
            for (int k = 0; k < m; k++)
            {
                int a = k, b = (k + 1) % m;
                tris.Add(a); tris.Add(m + a); tris.Add(m + b);         // side quads, outward
                tris.Add(a); tris.Add(m + b); tris.Add(b);
            }
            coordsList.Add(coords); trisList.Add(tris);
        }
        var r = Frahan.Masonry.Solvers.MasonryStabilityChecker.CheckMeshes(
            coordsList, trisList,
            contactDistanceTol: 5e-3, contactAngleTolDeg: 8.0, fixBelowZ: 0.02);
        if (!r.IsStable)
            throw new Exception(
                $"the fully-coursed generated wall must be RBE-stable; got {r.Status}: {r.Message} " +
                $"(interfaces={r.InterfaceCount}, free={r.FreeBlockCount})");
    }
}
