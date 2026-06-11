#nullable disable
using System;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Sequencing;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// =============================================================================
// KB-10 regression tests — exact-joint ADMM conditioning at ~50+ interfaces
// (handoffs/KNOWN_BUGS.md KB-10, 2026-06-11).
//
// Repro: the 6x4 generated wall (Width 6, Height 3.2, Coursing 0.4, Depth 0.4,
// SizeGrade 0.3, Seed 7 — the card 27_09 parameters; the GH-level Mortar 0.05
// cell shrink does not exist on the exact-joint Core path) assembled via
// PolygonalWallAssembler (exact 4-vertex quad joints) returned SolverError
// from MasonryStabilityChecker.Check ("ADMM did not converge in 8000
// iterations") while the 5x3 wall (30 ifaces) certified in 1 iteration.
//
// Fix under test: LS-first warm start in MasonryStabilityChecker — the
// equality-constrained KKT solve (dense Cholesky on the 6*freeBlocks dual
// system, exploiting the diagonal penalty Hessian) is checked against the
// friction cone / bounds; when feasible and tension-free the verdict is
// decoded directly (static lower-bound theorem), else the point warm-starts
// the ADMM. The acceptance gate here is NO SolverError — verdicts and timings
// are REPORTED ([bench] lines), not pinned.
// =============================================================================

static class Kb10ExactJointConditioningTests
{
    private static MasonryAssembly BuildWall(int gridX, int gridY)
    {
        // Mirrors MasonryStabilityCheckerTests.GeneratedWall_AdjacencyAssembler_StableAndLean
        // but at the KB-10 card 27_09 scale (6.0 x 3.2 m, depth 0.4).
        var gen = PolygonalWallGenerator.Generate(new WallGenOptions
        {
            Width = 6.0, Height = 3.2, GridX = gridX, GridY = gridY, Coursing = 0.4,
            LloydIterations = 2, SizeGradeCv = 0.30, Seed = 7,
        });
        var wall = PolygonalWallAssembler.Build(
            gen,
            (u, v) => new[] { u, 0.0, v },          // flat XZ panel
            (u, v) => new[] { 0.0, 1.0, 0.0 },      // +Y normal
            depth: 0.4);
        return wall.Assembly;
    }

    public static void Wall6x4_ExactJoints_CertifiesWithoutSolverError()
    {
        var asm = BuildWall(6, 4);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = MasonryStabilityChecker.Check(asm);
        sw.Stop();
        Console.WriteLine($"      [bench] kb10 6x4 exact-joint wall: " +
                          $"{(r.IsStable ? "STABLE" : "NOT STABLE")} (status={r.Status}) " +
                          $"in {sw.ElapsedMilliseconds} ms " +
                          $"({asm.Interfaces.Count} ifaces, {r.ContactVertexCount} verts, {r.FreeBlockCount} free)");
        if (r.Status == ConvexQpStatus.SolverError)
            throw new Exception(
                $"KB-10: 6x4 exact-joint wall must yield a real verdict, got SolverError: {r.Message}");
    }

    public static void WallSweep_ExactJoints_NoSolverError()
    {
        foreach (var (gx, gy) in new[] { (6, 4), (8, 5), (10, 6) })
        {
            var asm = BuildWall(gx, gy);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = MasonryStabilityChecker.Check(asm);
            sw.Stop();
            Console.WriteLine($"      [bench] kb10 sweep {gx}x{gy}: " +
                              $"{(r.IsStable ? "STABLE" : "NOT STABLE")} (status={r.Status}) " +
                              $"in {sw.ElapsedMilliseconds} ms " +
                              $"({asm.Interfaces.Count} ifaces, {r.ContactVertexCount} verts, {r.FreeBlockCount} free)");
            if (r.Status == ConvexQpStatus.SolverError)
                throw new Exception(
                    $"KB-10 sweep {gx}x{gy} returned SolverError: {r.Message}");
        }
    }
}
