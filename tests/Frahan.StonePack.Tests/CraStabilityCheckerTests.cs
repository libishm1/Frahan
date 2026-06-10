#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Sequencing;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// P2 regression tests for CraStabilityChecker (Kao 2022 coupled rigid-block
// analysis, alternating convex certificate). The H-model is THE counterexample
// from the paper: force-only RBE accepts it via self-equilibrated squeeze;
// the kinematic coupling must reject it. Pure managed; no Rhino runtime.

static class CraStabilityCheckerTests
{
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
            0,2,1, 0,3,2,   4,5,6, 4,6,7,
            0,1,5, 0,5,4,   2,3,7, 2,7,6,
            0,4,7, 0,7,3,   1,2,6, 1,6,5,
        };
    }

    private static MasonryAssembly BuildBoxAssembly(
        IReadOnlyList<(List<double> coords, List<int> tris)> boxes, double fixBelowZ = 1e-3)
    {
        var snaps = new List<MeshSnapshot>();
        var ids = new List<string>();
        var minZ = new List<double>();
        double globalMin = double.MaxValue;
        for (int i = 0; i < boxes.Count; i++)
        {
            snaps.Add(new MeshSnapshot(boxes[i].coords, boxes[i].tris));
            ids.Add("blk_" + i.ToString("00"));
            double mz = double.MaxValue;
            for (int k = 2; k < boxes[i].coords.Count; k += 3)
                if (boxes[i].coords[k] < mz) mz = boxes[i].coords[k];
            minZ.Add(mz);
            if (mz < globalMin) globalMin = mz;
        }
        var ifaces = MeshContactDetector.Detect(snaps, ids);
        var blocks = new List<MasonryBlock>();
        var fixedIds = new List<string>();
        for (int i = 0; i < boxes.Count; i++)
        {
            blocks.Add(new MasonryBlock(ids[i], boxes[i].coords, boxes[i].tris, 2400));
            if (minZ[i] <= globalMin + fixBelowZ) fixedIds.Add(ids[i]);
        }
        return new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
    }

    public static void Cra_TwoBoxStack_Certified()
    {
        Box(0, 0, 0.0, 1, 1, 0.5, out var c0, out var t0);
        Box(0, 0, 0.5, 1, 1, 1.0, out var c1, out var t1);
        var asm = BuildBoxAssembly(new[] { (c0, t0), (c1, t1) });
        var r = CraStabilityChecker.Check(asm);
        if (!r.IsStable || !r.Certified)
            throw new Exception($"a two-box stack must be CRA-certified; got stable={r.IsStable} " +
                                $"certified={r.Certified} residual={r.CertificateResidual:0.00}: {r.Message}");
    }

    public static void Cra_Cantilever_Unstable()
    {
        Box(0.0, 0, 0.0, 1.0, 1, 0.5, out var c0, out var t0);
        Box(0.8, 0, 0.5, 1.8, 1, 1.0, out var c1, out var t1);
        var asm = BuildBoxAssembly(new[] { (c0, t0), (c1, t1) });
        var r = CraStabilityChecker.Check(asm);
        if (r.IsStable)
            throw new Exception("a cantilever with COM beyond the support must be CRA-unstable");
    }

    public static void Cra_HModel_RbeAcceptsButCraRejects()
    {
        // Kao's counterexample family: a beam bridging two columns, touching them
        // ONLY through vertical faces (no support underneath). RBE finds a
        // self-equilibrated horizontal squeeze whose friction carries the beam —
        // physically there is nothing to produce that squeeze. CRA must reject:
        // engaging BOTH vertical joints needs the beam to virtually penetrate
        // both columns at once, which no rigid-body motion provides.
        Box(0.0, 0, 0.0, 0.4, 0.4, 1.2, out var colL, out var tL);
        Box(1.0, 0, 0.0, 1.4, 0.4, 1.2, out var colR, out var tR);
        Box(0.4, 0, 0.6, 1.0, 0.4, 0.9, out var beam, out var tB);
        var asm = BuildBoxAssembly(new[] { (colL, tL), (colR, tR), (beam, tB) });

        var rbe = MasonryStabilityChecker.Check(asm);
        if (!rbe.IsStable)
            throw new Exception("PIN FAILED: force-only RBE is expected to (wrongly) accept the " +
                                $"H-model via self-stress, but it reported unstable: {rbe.Message}");

        var cra = CraStabilityChecker.Check(asm);
        if (cra.IsStable)
            throw new Exception("CRA must reject the H-model (the squeeze is kinematically " +
                                $"impossible); got stable, residual={cra.CertificateResidual:0.00}: {cra.Message}");
    }

    public static void Cra_GeneratedWall_Certified()
    {
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = 2.0, Height = 1.0, GridX = 5, GridY = 3, Coursing = 1.0,
            LloydIterations = 2, SizeGradeCv = 0.0, Seed = 4,
        });
        var wall = PolygonalWallAssembler.Build(
            gen, (u, v) => new[] { u, 0.0, v }, (u, v) => new[] { 0.0, 1.0, 0.0 }, depth: 0.25);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = CraStabilityChecker.Check(wall.Assembly);
        sw.Stop();
        Console.WriteLine($"      [bench] CRA wall ({wall.Assembly.Blocks.Count} stones): " +
                          $"certified={r.Certified} residual={r.CertificateResidual:0.00}e " +
                          $"iters={r.Iterations} in {sw.ElapsedMilliseconds} ms");
        if (!r.IsStable)
            throw new Exception($"the coursed generated wall must be CRA-stable; " +
                                $"residual={r.CertificateResidual:0.00}: {r.Message}");
    }
}
