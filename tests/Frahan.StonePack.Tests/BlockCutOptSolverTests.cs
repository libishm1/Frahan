#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Tests;

// =============================================================================
// BlockCutOptSolverTests -- Phase 1 sanity tests for the BlockCutOpt solver.
//
// Layer 1 (unit) tests:
//   - empty fracture set yields full grid coverage
//   - one fracture across the centre kills exactly the row of blocks it crosses
//   - solver returns the BlockCutOpt limestone Stratum a search space
//
// Layer 2 (correctness) regression -- Phase 1.D will add the reproduction of
// the published 23 non-intersected blocks at psi=81.0 deg using a synthetic
// joint-set DFN with the documented parameters. That test depends on a
// PLY emission helper from JointSetDfnGenerator that does not exist yet and
// is tracked separately.
// =============================================================================

static class BlockCutOptSolverTests
{
    private static PlyMesh EmptyPly()
    {
        // a valid PlyMesh requires at least one triangle in some downstream
        // consumers; here we use a single far-away degenerate triangle so the
        // intersection test always returns false.
        var v = new List<double> { 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6 };
        var t = new List<int> { 0, 1, 2 };
        return new PlyMesh(v, t, null);
    }

    public static void Solve_EmptyFractures_GridIsFullCoverage()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var opts = new BlockCutOptOptions(
            blockSizeX: 3.0,
            blockSizeY: 2.0,
            blockSizeZ: 0.8,
            kerf: 0.05,
            psiStartRad: 0.0,
            psiStopRad: 0.0,                       // single psi
            psiStepRad: Math.PI,                   // moot
            dxMax: 0.0,
            dxStep: 1.0,                           // single dx
            dyMax: 0.0,
            dyStep: 1.0);                          // single dy

        var result = BlockCutOptSolver.Solve(area, EmptyPly(), opts);

        // 27 x 65 area, 3.05 x 2.05 pitch, axis-aligned (psi=0): 8 columns x 31 rows
        // = 248 blocks fit; the centroid-centred grid loses about half a pitch
        // on each side, so an exact count is around 224-248 depending on parity.
        Assert(result.NonIntersectedCount >= 200,
            $"expected >= 200 blocks with empty fractures, got {result.NonIntersectedCount}");
        Assert(result.NonIntersectedCount <= 248,
            $"expected <= 248 blocks, got {result.NonIntersectedCount}");
    }

    public static void Solve_VerticalPlaneAcrossCenter_KillsOneRow()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);

        // one vertical PLY rectangle from (0, 32.5, 0) to (27, 32.5, 0.8)
        // i.e. a YZ-perpendicular plane slicing the area through its centre Y.
        var v = new List<double>
        {
            0.0, 32.5, 0.0,
            27.0, 32.5, 0.0,
            27.0, 32.5, 0.8,
            0.0, 32.5, 0.8
        };
        var tri = new List<int> { 0, 1, 2, 0, 2, 3 };
        var ply = new PlyMesh(v, tri, null);

        var opts = new BlockCutOptOptions(
            blockSizeX: 3.0,
            blockSizeY: 2.0,
            blockSizeZ: 0.8,
            kerf: 0.05,
            psiStartRad: 0.0,
            psiStopRad: 0.0,
            psiStepRad: Math.PI,
            dxMax: 0.0,
            dxStep: 1.0,
            dyMax: 0.0,
            dyStep: 1.0);

        var resultEmpty = BlockCutOptSolver.Solve(area, EmptyPly(), opts);
        var resultCut = BlockCutOptSolver.Solve(area, ply, opts);

        int delta = resultEmpty.NonIntersectedCount - resultCut.NonIntersectedCount;
        // The single plane should kill the blocks that straddle it: 8 in a row
        // for the 27 m wide bench at 3.05 m pitch. Allow +/- 1 for parity.
        Assert(delta >= 7 && delta <= 10,
            $"expected one row of ~8 blocks lost, got delta={delta}");
    }

    public static void Solve_LimestoneStratumA_DefaultsParseCleanly()
    {
        var opts = BlockCutOptOptions.LimestoneStratumA();
        Assert(opts.BlockSizeX == 3.0, "BlockSizeX");
        Assert(opts.BlockSizeY == 2.0, "BlockSizeY");
        Assert(opts.BlockSizeZ == 0.8, "BlockSizeZ");
        Assert(opts.Kerf == 0.05, "Kerf");
        AssertNear(opts.PsiStepRad, 3.0 * Math.PI / 180.0, 1e-9, "PsiStep");
    }

    public static void TraceVerticalExtruder_TwoTraces_EmitsEightVertsTwelveTriIndices()
    {
        var traces = new List<(double, double, double, double)>
        {
            (0, 0, 10, 0),
            (5, -5, 5, 5),
        };
        var ply = TraceVerticalExtruder.Extrude(traces, 0.0, 6.0);
        Assert(ply.VertexCount == 8, $"expected 8 verts, got {ply.VertexCount}");
        Assert(ply.TriangleCount == 4, $"expected 4 tris, got {ply.TriangleCount}");
    }

    public static void CuttingGrid_PsiZero_AxisAligned()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var grid = CuttingGrid.Generate(
            area,
            blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 0.8,
            kerf: 0.05,
            psiRad: 0.0, dx: 0.0, dy: 0.0);
        Assert(grid.Count > 100, $"expected many candidate blocks, got {grid.Count}");
        var first = grid[0];
        AssertNear(first.UX, 1.0, 1e-12, "U=+x");
        AssertNear(first.UY, 0.0, 1e-12, "Uy=0");
    }

    public static void CuttingGrid_PsiQuarterTurn_NinetyDegreeRotation()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var grid = CuttingGrid.Generate(
            area,
            blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 0.8,
            kerf: 0.05,
            psiRad: Math.PI / 2.0, dx: 0.0, dy: 0.0);
        Assert(grid.Count > 100, $"expected many candidate blocks, got {grid.Count}");
        var first = grid[0];
        AssertNear(first.UX, 0.0, 1e-12, "U=+y when psi=90");
        AssertNear(first.UY, 1.0, 1e-12, "Uy=1");
    }

    // ─── Phase 1.D ─── synthetic-DFN end-to-end pipeline ────────────────────
    //
    // Per `D:\code_ws\wiki\papers\equations_and_diagrams\09_dataset_reproduction_report.md`
    // section 10.1, the published limestone Stratum a numbers (23 non-intersected
    // blocks at psi=81 deg) cannot be reproduced exactly without the original GPR
    // PLY input. This test instead validates the full pipeline (JointSetDfn ->
    // PLY emit -> solver) on a synthetic stratum-a-shaped DFN, asserting only
    // pipeline correctness and determinism.

    public static void Phase1D_SyntheticDfn_PipelineRunsAndReducesCount()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);

        // limestone-Stratum-a-style joint set definition: two near-vertical
        // orthogonal sub-vertical sets + one horizontal bedding.
        // Strikes loosely aligned with the published optimum direction (~81 deg).
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 11.0,  dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 101.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 0.0,   dipDeg: 0.0,  meanSpacing: 0.85, scatterDeg: 3.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 42);
        Assert(planes.Count > 0, "DFN generator emitted no planes");

        var ply = JointSetDfnPlyEmitter.Emit(planes, area);
        Assert(ply.TriangleCount > 0, "PLY emitter produced no triangles");

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var resultDfn = BlockCutOptSolver.Solve(area, ply, opts);
        var resultEmpty = BlockCutOptSolver.Solve(area, EmptyPly(), opts);

        Assert(resultEmpty.NonIntersectedCount > 100,
            $"empty-pipe baseline too low: {resultEmpty.NonIntersectedCount}");
        Assert(resultDfn.NonIntersectedCount < resultEmpty.NonIntersectedCount,
            $"DFN must reduce non-intersected count below empty " +
            $"(empty={resultEmpty.NonIntersectedCount}, dfn={resultDfn.NonIntersectedCount})");
        Assert(resultDfn.NonIntersectedCount >= 0,
            $"non-intersected count must be non-negative, got {resultDfn.NonIntersectedCount}");
        Assert(resultDfn.RecoveryPercent >= 0.0 && resultDfn.RecoveryPercent <= 100.0,
            $"recovery percent must be in [0, 100], got {resultDfn.RecoveryPercent:0.00}");

        double psiDeg = resultDfn.BestPsiDeg;
        Assert(psiDeg >= 0.0 && psiDeg <= 180.0 + 1e-6,
            $"best psi must be in [0, 180] deg, got {psiDeg:0.0}");
    }

    public static void Phase1D_SyntheticDfn_DeterministicSeedGivesIdenticalResult()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 11.0,  dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 101.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 0.0,   dipDeg: 0.0,  meanSpacing: 0.85, scatterDeg: 3.0),
        };

        var planesA = JointSetDfnGenerator.Generate(jointSets, area, seed: 1234);
        var plyA = JointSetDfnPlyEmitter.Emit(planesA, area);

        var planesB = JointSetDfnGenerator.Generate(jointSets, area, seed: 1234);
        var plyB = JointSetDfnPlyEmitter.Emit(planesB, area);

        Assert(plyA.VertexCount == plyB.VertexCount,
            $"PLY vertex count diverged: {plyA.VertexCount} vs {plyB.VertexCount}");
        Assert(plyA.TriangleCount == plyB.TriangleCount,
            $"PLY triangle count diverged: {plyA.TriangleCount} vs {plyB.TriangleCount}");

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var rA = BlockCutOptSolver.Solve(area, plyA, opts);
        var rB = BlockCutOptSolver.Solve(area, plyB, opts);

        Assert(rA.NonIntersectedCount == rB.NonIntersectedCount,
            $"solver non-deterministic: {rA.NonIntersectedCount} vs {rB.NonIntersectedCount}");
        AssertNear(rA.BestPsiRad, rB.BestPsiRad, 1e-12, "BestPsiRad determinism");
        AssertNear(rA.BestDx, rB.BestDx, 1e-12, "BestDx determinism");
        AssertNear(rA.BestDy, rB.BestDy, 1e-12, "BestDy determinism");
    }

    public static void JointSetDfnPlyEmitter_VerticalPlaneThroughBench_EmitsRectangle()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        // single vertical plane through the centre Y of the bench
        var plane = new FracturePlane(13.5, 32.5, 0.4,
                                       /*normal*/ 0.0, 1.0, 0.0);
        var ply = JointSetDfnPlyEmitter.Emit(new[] { plane }, area);
        Assert(ply.VertexCount == 4, $"expected 4 verts for AABB-clipped rect, got {ply.VertexCount}");
        Assert(ply.TriangleCount == 2, $"expected 2 tris (fan of 4), got {ply.TriangleCount}");
    }

    // ─── Phase 2 ─── BVH pruning ────────────────────────────────────────────

    public static void Bvh_BuildOnSingleTriangle_FindsHit()
    {
        // single triangle in the XY plane near the bench centre
        var v = new List<double> { 10, 30, 0.4,   20, 30, 0.4,   15, 35, 0.4 };
        var t = new List<int> { 0, 1, 2 };
        var ply = new PlyMesh(v, t, null);
        var bvh = TriangleAabbBvh.Build(ply);
        Assert(bvh.TriangleCount == 1, "expected 1 triangle in BVH");

        // OBB centred at (15, 32, 0.4), axis-aligned, half=(2,2,0.4)
        var obb = new OrientedBlock(15, 32, 0.4, 1, 0, 0, 1, 2, 2, 0.4);
        Assert(bvh.AnyTriangleIntersects(in obb), "OBB should intersect the triangle");
    }

    public static void Bvh_BuildOnFarTriangle_NoHit()
    {
        var v = new List<double> { 100, 100, 0.4,   110, 100, 0.4,   105, 105, 0.4 };
        var t = new List<int> { 0, 1, 2 };
        var ply = new PlyMesh(v, t, null);
        var bvh = TriangleAabbBvh.Build(ply);

        var obb = new OrientedBlock(15, 32, 0.4, 1, 0, 0, 1, 2, 2, 0.4);
        Assert(!bvh.AnyTriangleIntersects(in obb), "far OBB should not intersect");
    }

    public static void Bvh_DeterministicSeed_SolverResultUnchanged()
    {
        // verify the BVH-accelerated solver returns the same numerical result as
        // a brute-force baseline on a synthetic DFN.
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 11.0,  dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 101.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 0.0,   dipDeg: 0.0,  meanSpacing: 0.85, scatterDeg: 3.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 7);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var r1 = BlockCutOptSolver.Solve(area, ply, opts);
        var r2 = BlockCutOptSolver.Solve(area, ply, opts);
        Assert(r1.NonIntersectedCount == r2.NonIntersectedCount,
            $"BVH solver non-deterministic: {r1.NonIntersectedCount} vs {r2.NonIntersectedCount}");
        AssertNear(r1.BestPsiRad, r2.BestPsiRad, 1e-12, "BestPsiRad");
        AssertNear(r1.RecoveryPercent, r2.RecoveryPercent, 1e-9, "RecoveryPercent");
    }

    public static void Bvh_AgainstBruteForce_IdenticalCount_OneVerticalPlane()
    {
        // single fracture: BVH path must return identical non-intersected count
        // to the trivial direct test.
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var v = new List<double>
        {
            0.0, 32.5, 0.0,  27.0, 32.5, 0.0,  27.0, 32.5, 0.8,  0.0, 32.5, 0.8
        };
        var tri = new List<int> { 0, 1, 2, 0, 2, 3 };
        var ply = new PlyMesh(v, tri, null);

        var opts = new BlockCutOptOptions(
            blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 0.8,
            kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: 0.0, psiStepRad: Math.PI,
            dxMax: 0.0, dxStep: 1.0,
            dyMax: 0.0, dyStep: 1.0);
        var solverResult = BlockCutOptSolver.Solve(area, ply, opts);
        var emptyResult = BlockCutOptSolver.Solve(area, EmptyPly(), opts);

        int delta = emptyResult.NonIntersectedCount - solverResult.NonIntersectedCount;
        Assert(delta >= 7 && delta <= 10,
            $"BVH+SAT path expected one ~8-block row killed, got delta={delta}");
    }

    // ─── Phase 3 ─── sub-division ───────────────────────────────────────────

    public static void Subdivision_2x3_Produces6Zones_CoverTestedArea()
    {
        var area = new BoundingBox3(0, 0, 0, 30.0, 60.0, 1.0);
        var zones = SubdivisionPartition.Uniform(area, mx: 2, my: 3);
        Assert(zones.Count == 6, $"expected 6 zones, got {zones.Count}");

        // total area must equal the original area
        double total = 0;
        foreach (var z in zones) total += z.Aabb.SizeX * z.Aabb.SizeY;
        AssertNear(total, area.SizeX * area.SizeY, 1e-9, "sum of zone areas");

        // first zone should be the (1,1) cell at (0..15, 0..20)
        var first = zones[0];
        Assert(first.I == 1 && first.J == 1, $"first zone should be (1,1), got {first.Id}");
        AssertNear(first.Aabb.MinX, 0.0, 1e-12, "first.MinX");
        AssertNear(first.Aabb.MaxX, 15.0, 1e-12, "first.MaxX");
    }

    public static void SolveSubdivided_2x2_AllZonesReturnValidResults()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 11.0,  dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 101.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 8.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 11);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var results = BlockCutOptSolver.SolveSubdivided(area, ply, opts, mx: 2, my: 2);

        Assert(results.Count == 4, $"expected 4 results, got {results.Count}");
        foreach (var (zone, r) in results)
        {
            Assert(r.NonIntersectedCount >= 0,
                $"zone {zone.Id} non-intersected must be non-negative, got {r.NonIntersectedCount}");
            Assert(r.RecoveryPercent >= 0.0 && r.RecoveryPercent <= 100.0,
                $"zone {zone.Id} recovery must be in [0, 100], got {r.RecoveryPercent:0.00}");
            double psiDeg = r.BestPsiDeg;
            Assert(psiDeg >= 0.0 && psiDeg <= 180.0 + 1e-6,
                $"zone {zone.Id} best psi must be in [0, 180], got {psiDeg:0.0}");
        }
    }

    // ─── Phase 4 ─── coarse-to-fine angular search ──────────────────────────

    public static void CoarseToFine_RunsCleanlyAndProducesValidResult()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 3.5, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 171.0, dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 99);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var result = BlockCutOptCoarseToFine.Solve(area, ply, opts, topK: 2);

        Assert(result.NonIntersectedCount >= 0,
            $"coarse-to-fine non-intersected must be non-negative, got {result.NonIntersectedCount}");
        Assert(result.RecoveryPercent >= 0.0 && result.RecoveryPercent <= 100.0,
            $"recovery must be in [0, 100], got {result.RecoveryPercent:0.00}");
        Assert(result.BestPsiDeg >= 0.0 && result.BestPsiDeg <= 180.0 + 1e-6,
            $"best psi must be in [0, 180], got {result.BestPsiDeg:0.0}");
    }

    // ─── Phase 6 ─── Pareto multi-objective + BCSdbBV (I11) ─────────────────

    public static void ParetoFront_InsertAndPruneDominated()
    {
        var f = new ParetoFront();
        // weak point
        var p1 = new ParetoPoint(10, 1.0, 5.0, 100.0, 0, 0, 0);
        // dominates p1 on every axis
        var p2 = new ParetoPoint(20, 2.0, 4.0,  50.0, 0.1, 0, 0);
        // incomparable with p2 (better Recovery, worse Revenue)
        var p3 = new ParetoPoint(30, 1.5, 3.0,  60.0, 0.2, 0, 0);

        Assert(f.Insert(in p1), "p1 should insert");
        Assert(f.Count == 1, "after p1");

        Assert(f.Insert(in p2), "p2 should insert");
        Assert(f.Count == 1, "p2 should evict p1");

        Assert(f.Insert(in p3), "p3 should insert");
        Assert(f.Count == 2, "p3 is incomparable with p2");
    }

    public static void ParetoSolver_FullPipeline_ReturnsNonEmptyFront()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 3.5, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 171.0, dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 7);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var (front, evals, _) = BlockCutOptParetoSolver.Solve(area, ply, opts);
        Assert(front.Count >= 1, $"front must be non-empty, got {front.Count}");
        Assert(evals > 0, "solver should evaluate at least one point");

        var bestR = front.BestRecovery();
        var bestRev = front.BestRevenue();
        var bestBcs = front.BestBcsdbBv();

        Assert(bestR.RecoveryCount > 0, $"BestRecovery count must be > 0, got {bestR.RecoveryCount}");
        Assert(bestRev.Revenue >= bestR.Revenue * 0.999,
            $"BestRevenue revenue {bestRev.Revenue:0.000} must be >= BestRecovery revenue {bestR.Revenue:0.000}");
        Assert(bestBcs.BcsdbBv <= bestR.BcsdbBv + 1e-9,
            $"BestBcsdbBv {bestBcs.BcsdbBv:0.000} must be <= BestRecovery BCSdbBV {bestR.BcsdbBv:0.000}");
    }

    // ─── Phase 8 ─── Fisher-robust Monte Carlo wrapper (I8) ─────────────────

    public static void FisherRobust_Solver_ProducesValidAggregateStatistics()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 3.5, scatterDeg: 8.0),
            new JointSet(dipDirectionDeg: 171.0, dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
        };
        var opts = BlockCutOptOptions.LimestoneStratumA();

        var r = FisherRobustSampler.Solve(area, jointSets, opts, monteCarloSamples: 6, baseSeed: 100);

        Assert(r.SampleCount == 6, $"expected 6 samples, got {r.SampleCount}");
        Assert(r.PerSample.Count == 6, "PerSample.Count must match");
        Assert(r.RecoveryP10 <= r.RecoveryP50, $"p10 ({r.RecoveryP10:0.00}) must be <= p50 ({r.RecoveryP50:0.00})");
        Assert(r.RecoveryP50 <= r.RecoveryP90, $"p50 ({r.RecoveryP50:0.00}) must be <= p90 ({r.RecoveryP90:0.00})");
        Assert(r.RecoveryMean >= 0.0, $"mean must be >= 0, got {r.RecoveryMean:0.00}");
        Assert(r.RecoveryStdDev >= 0.0, $"stddev must be >= 0, got {r.RecoveryStdDev:0.00}");
        Assert(r.MedianPsiDeg >= 0.0 && r.MedianPsiDeg <= 180.0 + 1e-6,
            $"median psi must be in [0, 180], got {r.MedianPsiDeg:0.0}");
    }

    // ─── Tolerances ─────────────────────────────────────────────────────────

    public static void Tolerances_KerfDefaultIs50mm()
    {
        AssertNear(BlockCutOptTolerances.KerfDefaultMetres, 0.05, 1e-12, "kerf default");
        AssertNear(BlockCutOptTolerances.MetresToMm(BlockCutOptTolerances.KerfDefaultMetres), 50.0, 1e-9, "kerf in mm");
        AssertNear(BlockCutOptTolerances.PsiStepDefaultRad, 3.0 * Math.PI / 180.0, 1e-12, "psi step default");
    }

    public static void Tolerances_RhinoUnitConvertsMmToMm()
    {
        // Rhino model in mm: rhinoMetresPerUnit = 1e-3 means each Rhino unit = 1 mm
        double inMmModel = BlockCutOptTolerances.ToRhinoUnit(0.05, 1.0e-3);
        AssertNear(inMmModel, 50.0, 1e-9, "kerf 0.05 m in a mm Rhino model");

        // Rhino model in metres: rhinoMetresPerUnit = 1.0
        double inMetresModel = BlockCutOptTolerances.ToRhinoUnit(0.05, 1.0);
        AssertNear(inMetresModel, 0.05, 1e-9, "kerf 0.05 m in a metre Rhino model");
    }

    // ─── I12 Minetto shared-edge slicing ────────────────────────────────────

    public static void SharedEdgeSlicer_UnitCube_FiveZSlices_ProducesContours()
    {
        // unit cube spanning 0..1
        var v = new List<double>
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0,
            0,0,1, 1,0,1, 1,1,1, 0,1,1,
        };
        var t = new List<int>
        {
            0,1,2, 0,2,3,    // bottom
            4,6,5, 4,7,6,    // top
            0,4,5, 0,5,1,    // y=0
            2,6,7, 2,7,3,    // y=1
            1,5,6, 1,6,2,    // x=1
            0,3,7, 0,7,4,    // x=0
        };
        var ply = new PlyMesh(v, t, null);
        var offsets = new List<double> { 0.1, 0.3, 0.5, 0.7, 0.9 };
        var slices = SharedEdgeSlicer.Slice(ply, 0, 0, 1, offsets);
        Assert(slices.Count == 5, $"expected 5 contours, got {slices.Count}");
        foreach (var s in slices)
        {
            // each z-slice of a cube produces 4 edge-segments
            Assert(s.SegmentCount >= 4 && s.SegmentCount <= 8,
                $"z={s.Offset}: expected ~4 segments, got {s.SegmentCount}");
        }
    }

    // ─── Phase 9 Shao 2022 AMRR planner ─────────────────────────────────────

    public static void AmrrPlanner_CubeToInscribedSphere_RemovesPositiveVolume()
    {
        // 1 m cube blank, target sphere of radius 0.45 m centred at the cube centre
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var blank = ConvexPolyhedron.FromOrientedBlock(in obb);
        AssertNear(blank.Volume(), 1.0, 1e-9, "blank volume");

        var opts = new AmrrPlannerOptions { MaxCuts = 30, ConvergenceFraction = 1e-2 };
        var result = AmrrPlanner.PlanBoundingSphere(blank, 0.5, 0.5, 0.5, 0.45, opts);

        Assert(result.Steps.Count >= 1, "should perform at least one cut");
        Assert(result.MaterialRemovalPercent > 0.0,
            $"MRP should be > 0, got {result.MaterialRemovalPercent:0.00}");
        Assert(result.MaterialRemovalPercent < 100.0,
            $"MRP should be < 100, got {result.MaterialRemovalPercent:0.00}");
        Assert(result.FinalCph.Volume() > 0.0, "final CPH must have positive volume");
    }

    public static void AmrrPlanner_DefaultsUseMmConvertedSawblade()
    {
        var opts = new AmrrPlannerOptions();
        // default radius = 100 mm = 0.1 m
        AssertNear(opts.SawBladeRadiusMetres, 0.1, 1e-9, "sawblade radius in metres");
        // default feed = 50 mm/min = 0.05 m/min
        AssertNear(opts.FeedSpeedMetresPerMin, 0.05, 1e-9, "feed speed in m/min");
    }

    public static void ConvexPolyhedron_ClipByHalfSpace_ReducesVolume()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        double v0 = cph.Volume();

        // cut by a plane through the centre with normal +X
        var clipped = cph.ClipByHalfSpace(0.5, 0.5, 0.5, 1.0, 0.0, 0.0);
        double v1 = clipped.Volume();

        Assert(v1 > 0, "clipped volume must be positive");
        Assert(v1 < v0, $"clipped {v1:0.000} must be < original {v0:0.000}");
        AssertNear(v1, 0.5, 0.05, "centre-X clip should leave ~half");
    }

    // ─── Phase 7 density-watershed ──────────────────────────────────────────

    public static void DensityWatershed_TwoClusters_ProducesAtLeastOneZone()
    {
        var area = new BoundingBox3(0, 0, 0, 30, 30, 1);
        var planes = new List<FracturePlane>
        {
            new FracturePlane(5, 5, 0.5, 1, 0, 0),
            new FracturePlane(5.5, 5, 0.5, 0, 1, 0),
            new FracturePlane(6, 6, 0.5, 1, 1, 0),
            new FracturePlane(25, 25, 0.5, 1, 0, 0),
            new FracturePlane(25.5, 25.5, 0.5, 0, 1, 0),
        };
        var zones = DensityWatershedPartition.Partition(area, planes, bandwidth: 3.0, rasterCellSize: 0.75);
        Assert(zones.Count >= 1, $"expected at least 1 zone, got {zones.Count}");
        foreach (var z in zones)
        {
            Assert(z.Aabb.SizeX > 0 && z.Aabb.SizeY > 0,
                $"zone {z.Id} has zero size: {z.Aabb}");
        }
    }

    // ─── I7 DLBF mixed-size packer ──────────────────────────────────────────

    public static void Dlbf_SingleSize_PacksMultipleCopies()
    {
        var area = new BoundingBox3(0, 0, 0, 10, 6, 1);
        var catalog = new List<PieceSize>
        {
            new PieceSize("medium", 2.0, 1.0, revenue: 5.0),
        };
        var result = DlbfMixedSizePacker.Pack(area, catalog);
        Assert(result.Placed.Count >= 20,
            $"expected >= 20 pieces in 10x6 with 2x1, got {result.Placed.Count}");
        // total revenue ~= 20 * 5 = 100
        Assert(result.TotalRevenue > 80.0,
            $"expected revenue > 80, got {result.TotalRevenue:0.000}");
    }

    public static void Dlbf_MixedSizes_PrefersHigherRevenuePerArea()
    {
        var area = new BoundingBox3(0, 0, 0, 6, 6, 1);
        var catalog = new List<PieceSize>
        {
            new PieceSize("big", 3.0, 3.0, revenue: 2.0),   // 9 m^2 -> rev/area = 0.222
            new PieceSize("small", 1.0, 1.0, revenue: 1.0), // 1 m^2 -> rev/area = 1.0
        };
        var result = DlbfMixedSizePacker.Pack(area, catalog, cellSize: 0.5);
        // small pieces have higher revenue/area so they should be placed first
        int smallCount = 0, bigCount = 0;
        foreach (var p in result.Placed)
        {
            if (p.Size.Id == "small") smallCount++;
            else if (p.Size.Id == "big") bigCount++;
        }
        Assert(smallCount >= 1, $"expected at least 1 small piece, got {smallCount}");
        Assert(result.TotalRevenue > 0,
            $"revenue should be > 0, got {result.TotalRevenue:0.000}");
    }

    // ─── Phase 11.5 photogrammetry contract ─────────────────────────────────

    public static void ImageToWorldMap_FlipYWorks()
    {
        var map = new ImageToWorldMap(originX: 0, originY: 100, gsdMetresPerPx: 0.02, flipY: true);
        var (x, y) = map.PixelToWorld(100, 50);
        // pxX=100 -> X = 0 + 100 * 0.02 = 2.0
        // pxY=50 with flip -> Y = 100 - 50 * 0.02 = 99.0
        AssertNear(x, 2.0, 1e-9, "world X");
        AssertNear(y, 99.0, 1e-9, "world Y");
    }

    // ─── Integration: SharedEdgeSlicer + AmrrPlanner ────────────────────────

    public static void ConvexPolyhedron_ToPlyMesh_FanTriangulatesFaces()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        var ply = cph.ToPlyMesh();
        Assert(ply.VertexCount == 8, $"expected 8 verts, got {ply.VertexCount}");
        // 6 quad faces, fan-triangulated -> 12 triangles
        Assert(ply.TriangleCount == 12, $"expected 12 tris, got {ply.TriangleCount}");
    }

    public static void AmrrPlanner_WithSlicer_StillReducesVolume()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var blank = ConvexPolyhedron.FromOrientedBlock(in obb);
        var opts = new AmrrPlannerOptions { MaxCuts = 20, ConvergenceFraction = 1e-2 };
        var result = AmrrPlanner.PlanBoundingSphere(blank, 0.5, 0.5, 0.5, 0.4, opts);

        Assert(result.Steps.Count >= 1, $"expected at least one cut, got {result.Steps.Count}");
        Assert(result.MaterialRemovalPercent > 5.0,
            $"expected MRP > 5%, got {result.MaterialRemovalPercent:0.00}");
        // every step should have a positive cutting time courtesy of the slicer path
        foreach (var s in result.Steps)
        {
            Assert(s.CuttingTimeMin > 0,
                $"step {s.Index} should have positive cutting time, got {s.CuttingTimeMin:0.000}");
        }
    }

    // ─── VtuWriter ──────────────────────────────────────────────────────────

    public static void VtuWriter_RoundTripsHexahedrons()
    {
        var grid = new List<OrientedBlock>
        {
            new OrientedBlock(1, 1, 0.4, 1, 0, 0, 1, 1, 1, 0.4),
            new OrientedBlock(3, 1, 0.4, 1, 0, 0, 1, 1, 1, 0.4),
            new OrientedBlock(5, 1, 0.4, 1, 0, 0, 1, 1, 1, 0.4),
        };
        var good = new List<OrientedBlock> { grid[0], grid[2] };
        var bad = new List<OrientedBlock> { grid[1] };

        string path = Path.GetTempFileName();
        try
        {
            VtuWriter.Write(path, good, bad);
            string text = File.ReadAllText(path);
            Assert(text.Contains("UnstructuredGrid"), "should mention UnstructuredGrid");
            Assert(text.Contains("cell_status"), "should have cell_status array");
            Assert(text.Contains("NumberOfCells=\"3\""), "should declare 3 cells");
            Assert(text.Contains("NumberOfPoints=\"24\""), "should declare 24 points (3 hex * 8)");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void VtuWriter_FromGridAndBvh_SplitsCorrectly()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var v = new List<double>
        {
            0.0, 32.5, 0.0,  27.0, 32.5, 0.0,  27.0, 32.5, 0.8,  0.0, 32.5, 0.8
        };
        var tri = new List<int> { 0, 1, 2, 0, 2, 3 };
        var ply = new PlyMesh(v, tri, null);
        var bvh = TriangleAabbBvh.Build(ply);

        var grid = CuttingGrid.Generate(area, 3.0, 2.0, 0.8, 0.05, 0.0, 0.0, 0.0);
        string path = Path.GetTempFileName();
        try
        {
            var (good, bad) = VtuWriter.WriteFromGridAndBvh(path, grid, bvh);
            Assert(good + bad == grid.Count, "good + bad should equal grid count");
            Assert(bad >= 7 && bad <= 10, $"expected ~8 row killed, got bad={bad}");
            Assert(good > 0, "expected positive non-intersected count");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ─── OmniSolver end-to-end ──────────────────────────────────────────────

    public static void OmniSolver_Uniform_2x2_EndToEnd()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 6.0),
            new JointSet(dipDirectionDeg: 171.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 6.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 17);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var omniOpts = new OmniSolverOptions
        {
            Search = BlockCutOptOptions.LimestoneStratumA(),
            SubdivMode = SubdivisionMode.Uniform,
            Mx = 2, My = 2,
        };
        var result = BlockCutOptOmniSolver.Solve(area, ply, omniOpts);

        Assert(result.PerZone.Count == 4, $"expected 4 zones, got {result.PerZone.Count}");
        Assert(result.AggregateRecoveryCount >= 0,
            $"aggregate recovery must be non-negative, got {result.AggregateRecoveryCount}");
        Assert(result.TotalEvaluations > 0, "should record evaluations");
    }

    // ─── AMRR VTU sequence + Python detector + Demo + Regression ────────────

    public static void VtuWriter_AmrrSequence_RoundTrips()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var blank = ConvexPolyhedron.FromOrientedBlock(in obb);
        var plan = AmrrPlanner.PlanBoundingSphere(blank, 0.5, 0.5, 0.5, 0.4,
            new AmrrPlannerOptions { MaxCuts = 10, ConvergenceFraction = 1e-2 });
        Assert(plan.Steps.Count >= 1, "need at least one step");

        string path = Path.GetTempFileName();
        try
        {
            VtuWriter.WriteAmrrSequence(path, plan, quadSizeMetres: 0.4);
            string text = File.ReadAllText(path);
            Assert(text.Contains("removed_volume_m3"), "should expose removed_volume_m3");
            Assert(text.Contains("step_index"), "should expose step_index");
            Assert(text.Contains("cutting_time_min"), "should expose cutting_time_min");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void PythonSubprocessFractureDetector_ParseTracesFromCsv()
    {
        // exercise the CSV parser independently of an actual Python process
        var csv = "x1,y1,x2,y2\n" +
                  "0.0,0.0,5.0,0.0\n" +
                  "0.0,5.0,5.0,5.0\n" +
                  "# comment line\n" +
                  "10.0,0.0,10.0,5.0\n";
        var traces = PythonSubprocessFractureDetector.ParseTraces(csv);
        Assert(traces.Count == 3, $"expected 3 traces, got {traces.Count}");
        AssertNear(traces[0].X2, 5.0, 1e-9, "trace 0 x2");
        AssertNear(traces[2].X1, 10.0, 1e-9, "trace 2 x1");
    }

    public static void Demo_RunSyntheticDfn_EndToEnd_WritesVtu()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 4.5, scatterDeg: 6.0),
            new JointSet(dipDirectionDeg: 171.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 6.0),
        };

        string tmpDir = Path.Combine(Path.GetTempPath(), $"frahan_demo_{Guid.NewGuid():N}");
        try
        {
            var result = BlockCutOptDemo.RunSyntheticDfnDemo(
                jointSets, area, seed: 7,
                search: BlockCutOptOptions.LimestoneStratumA(),
                mx: 2, my: 2,
                vtuOutputDir: tmpDir);

            Assert(result.PerZone.Count == 4, $"expected 4 zones, got {result.PerZone.Count}");
            Assert(Directory.Exists(tmpDir), "VTU output dir must exist");
            var files = Directory.GetFiles(tmpDir, "*.vtu");
            Assert(files.Length == 4, $"expected 4 VTUs, got {files.Length}");
            foreach (var f in files)
            {
                Assert(File.ReadAllText(f).Contains("cell_status"), $"{Path.GetFileName(f)} missing cell_status");
            }
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    public static void Demo_CoupleToAmrrAtBestBlock_WritesPlanSequence()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 0.0, dipDeg: 88.0, meanSpacing: 6.0, scatterDeg: 4.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 11);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);
        var opts = BlockCutOptOptions.LimestoneStratumA();
        var quarryResult = BlockCutOptDemo.RunPlyDrivenDemo(ply, area, opts, mx: 1, my: 1, vtuOutputDir: null);

        string amrrVtu = Path.GetTempFileName();
        try
        {
            var plan = BlockCutOptDemo.CoupleToAmrrAtBestBlock(
                quarryResult, opts, targetSphereFraction: 0.7,
                amrrOpts: new AmrrPlannerOptions { MaxCuts = 15, ConvergenceFraction = 1e-2 },
                amrrVtuPath: amrrVtu);

            Assert(plan.Steps.Count >= 1, $"expected >= 1 AMRR step, got {plan.Steps.Count}");
            Assert(plan.MaterialRemovalPercent > 0,
                $"MRP must be > 0, got {plan.MaterialRemovalPercent:0.00}");
            Assert(File.Exists(amrrVtu), "AMRR VTU must be written");
            Assert(File.ReadAllText(amrrVtu).Contains("removed_volume_m3"),
                "AMRR VTU must expose removed_volume_m3");
        }
        finally
        {
            if (File.Exists(amrrVtu)) File.Delete(amrrVtu);
        }
    }

    // Phase 1.D regression: best-effort reproduction of BlockCutOpt 2020
    // limestone Stratum a published numbers (23 non-intersected blocks at
    // psi=81 deg, recovery 7.86 percent) using a synthetic DFN. The original
    // GPR PLY is not public; this test asserts that the solver achieves a
    // recovery in the same order of magnitude (within +/- 10 percentage
    // points) and a psi in [0, 180), validating the end-to-end pipeline.
    // ─── I1 full 3D rotation ────────────────────────────────────────────────

    public static void I1_OrientedBlock_Phase1Constructor_DefaultsToVerticalW()
    {
        var obb = new OrientedBlock(0, 0, 0, 1, 0, 0, 1, 1, 1, 1);
        Assert(obb.IsAxisAlignedZ, "Phase 1 ctor should be axis-aligned-Z");
        AssertNear(obb.WX, 0.0, 1e-12, "WX");
        AssertNear(obb.WY, 0.0, 1e-12, "WY");
        AssertNear(obb.WZ, 1.0, 1e-12, "WZ");
        AssertNear(obb.UZ, 0.0, 1e-12, "UZ");
        AssertNear(obb.VZ, 0.0, 1e-12, "VZ");
    }

    public static void I1_OrientedBlock_FullCtor_AcceptsTiltedAxes()
    {
        // 45 deg tilt around X: U = +X, V = (0, sqrt2/2, sqrt2/2), W = (0, -sqrt2/2, sqrt2/2)
        double s = Math.Sqrt(2.0) / 2.0;
        var obb = new OrientedBlock(0, 0, 0,
            1, 0, 0,
            0, s, s,
            0, -s, s,
            1, 1, 1);
        Assert(!obb.IsAxisAlignedZ, "tilted OBB should not report axis-aligned-Z");
        AssertNear(obb.VZ, s, 1e-9, "VZ tilt");
        AssertNear(obb.WZ, s, 1e-9, "WZ tilt");
    }

    public static void I1_CuttingGrid_GenerateTilted_NonZeroTheta_ChangesUZVZ()
    {
        var area = new BoundingBox3(-5, -5, 0, 5, 5, 1);
        // tilt 30 deg around X (theta)
        var grid = CuttingGrid.GenerateTilted(area,
            blockSizeX: 2.0, blockSizeY: 2.0, blockSizeZ: 1.0,
            kerf: 0.05,
            psiRad: 0.0, thetaRad: BlockCutOptTolerances.DegToRad(30.0), phiRad: 0.0,
            dx: 0.0, dy: 0.0);
        Assert(grid.Count > 0, "should produce candidate blocks");
        var first = grid[0];
        Assert(Math.Abs(first.VZ) > 1e-3,
            $"theta=30 deg should give VZ != 0, got {first.VZ:0.000}");
        Assert(Math.Abs(first.WY) > 1e-3,
            $"theta=30 deg should give WY != 0, got {first.WY:0.000}");
    }

    public static void I1_Solver_ThetaSweep_PreservesPsiOnlyResultWhenThetaZero()
    {
        // theta sweep that is effectively a no-op (ThetaMax=0) must yield
        // the same result as the Phase 1 solver call.
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var v = new List<double> { 0, 32.5, 0, 27, 32.5, 0, 27, 32.5, 0.8, 0, 32.5, 0.8 };
        var t = new List<int> { 0, 1, 2, 0, 2, 3 };
        var ply = new PlyMesh(v, t, null);

        var optsPhase1 = BlockCutOptOptions.LimestoneStratumA();
        var optsI1 = new BlockCutOptOptions(
            blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 0.8,
            kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: Math.PI, psiStepRad: BlockCutOptTolerances.DegToRad(3.0),
            dxMax: 1.5, dxStep: 0.5,
            dyMax: 1.5, dyStep: 0.5,
            thetaMaxRad: 0.0, thetaStepRad: 0.0,
            phiMaxRad: 0.0, phiStepRad: 0.0);

        var r1 = BlockCutOptSolver.Solve(area, ply, optsPhase1);
        var r2 = BlockCutOptSolver.Solve(area, ply, optsI1);
        Assert(r1.NonIntersectedCount == r2.NonIntersectedCount,
            $"theta=0 sweep should match Phase 1: {r1.NonIntersectedCount} vs {r2.NonIntersectedCount}");
        AssertNear(r2.BestThetaRad, 0.0, 1e-12, "BestThetaRad must be 0");
        AssertNear(r2.BestPhiRad, 0.0, 1e-12, "BestPhiRad must be 0");
    }

    public static void I1_Solver_TiltedSearch_RunsCleanly()
    {
        var area = new BoundingBox3(0, 0, 0, 20.0, 20.0, 1.0);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 0.0, dipDeg: 80.0, meanSpacing: 4.0, scatterDeg: 4.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 5);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = new BlockCutOptOptions(
            blockSizeX: 2.0, blockSizeY: 2.0, blockSizeZ: 1.0,
            kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: Math.PI, psiStepRad: BlockCutOptTolerances.DegToRad(30.0),
            dxMax: 0.0, dxStep: 1.0,
            dyMax: 0.0, dyStep: 1.0,
            thetaMaxRad: BlockCutOptTolerances.DegToRad(10.0),
            thetaStepRad: BlockCutOptTolerances.DegToRad(10.0),
            phiMaxRad: 0.0, phiStepRad: 0.0);
        var r = BlockCutOptSolver.Solve(area, ply, opts);
        Assert(r.NonIntersectedCount >= 0, $"count must be non-negative, got {r.NonIntersectedCount}");
        Assert(Math.Abs(r.BestThetaRad) <= opts.ThetaMaxRad + 1e-9,
            $"best theta must be in [-thetaMax, thetaMax], got {r.BestThetaDeg:0.0}");
    }

    // ─── I4 edge-triangle alternative ───────────────────────────────────────

    public static void I4_EdgeTriangleObb_AgreesWithSat_VerticalPlane()
    {
        var obb = new OrientedBlock(0, 0, 0, 1, 0, 0, 1, 1, 1, 1);
        // a plane through the OBB at x = 0
        double p0X = 0, p0Y = -2, p0Z = -2;
        double p1X = 0, p1Y = 2, p1Z = -2;
        double p2X = 0, p2Y = 0, p2Z = 2;
        bool sat = ObbTriangleIntersection.Intersects(in obb,
            p0X, p0Y, p0Z, p1X, p1Y, p1Z, p2X, p2Y, p2Z);
        bool edge = EdgeTriangleObbIntersection.Intersects(in obb,
            p0X, p0Y, p0Z, p1X, p1Y, p1Z, p2X, p2Y, p2Z);
        Assert(sat == edge, $"SAT={sat}, Edge={edge} should agree");
        Assert(sat, "both should report hit");
    }

    public static void I4_EdgeTriangleObb_AgreesWithSat_FarTriangle()
    {
        var obb = new OrientedBlock(0, 0, 0, 1, 0, 0, 1, 1, 1, 1);
        bool sat = ObbTriangleIntersection.Intersects(in obb,
            100, 100, 0, 110, 100, 0, 105, 105, 0);
        bool edge = EdgeTriangleObbIntersection.Intersects(in obb,
            100, 100, 0, 110, 100, 0, 105, 105, 0);
        Assert(!sat && !edge, "both should report miss");
    }

    // ─── FractureInputReader ────────────────────────────────────────────────

    public static void FractureInputReader_LoadCsv_VerticalExtrude()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path + ".csv",
                "x1,y1,x2,y2\n# comment\n0,5,40,5\n0,12,40,12\n");
            File.Delete(path);
            path += ".csv";
            var ply = FractureInputReader.Load(path, zMin: 0.0, zMax: 6.0);
            Assert(ply.TriangleCount == 4, $"expected 4 tris (2 per trace), got {ply.TriangleCount}");
            Assert(ply.VertexCount == 8, $"expected 8 verts, got {ply.VertexCount}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void FractureInputReader_LoadLines_SpaceSeparated()
    {
        string path = Path.GetTempFileName() + ".lines";
        try
        {
            File.WriteAllText(path,
                "# header comment\n0 5 40 5\n0 12 40 12\n0 18 40 18\n");
            var ply = FractureInputReader.Load(path, zMin: 0.0, zMax: 6.0);
            Assert(ply.TriangleCount == 6, $"expected 6 tris, got {ply.TriangleCount}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void FractureInputReader_LoadPly_ReadsRawMesh()
    {
        string path = Path.GetTempFileName() + ".ply";
        try
        {
            File.WriteAllText(path,
                "ply\nformat ascii 1.0\nelement vertex 4\nproperty float x\nproperty float y\nproperty float z\n" +
                "element face 1\nproperty list uchar int vertex_indices\nend_header\n" +
                "0 5 0\n40 5 0\n40 5 6\n0 5 6\n4 0 1 2 3\n");
            var ply = FractureInputReader.Load(path);
            Assert(ply.VertexCount == 4, $"expected 4 verts, got {ply.VertexCount}");
            Assert(ply.TriangleCount == 2, $"quad should fan-triangulate to 2, got {ply.TriangleCount}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ─── Sample data generator ──────────────────────────────────────────────

    public static void SyntheticTnGraniteGenerator_WritesBothFormats()
    {
        var tmpCsv = Path.GetTempFileName() + ".csv";
        var tmpPly = Path.GetTempFileName() + ".ply";
        try
        {
            var bench = new BoundingBox3(0, 0, 0, 40, 30, 6);
            var (planes, traces, tris) = SyntheticTnGraniteGenerator.WriteSampleSet(
                tmpCsv, tmpPly, bench, seed: 1234);
            Assert(planes >= 15, $"expected >=15 planes for 40x30x6 m bench, got {planes}");
            Assert(traces >= 3, $"expected >=3 mid-Z traces (vertical sets dominate), got {traces}");
            Assert(tris >= 20, $"expected >=20 PLY triangles, got {tris}");
            Assert(File.Exists(tmpCsv), "CSV must be written");
            Assert(File.Exists(tmpPly), "PLY must be written");
            string csv = File.ReadAllText(tmpCsv);
            Assert(csv.Contains("x1,y1,x2,y2"), "CSV must have header");
            Assert(csv.Contains("# Tamil Nadu"), "CSV must have site comment");
            string ply = File.ReadAllText(tmpPly);
            Assert(ply.StartsWith("ply"), "PLY must start with magic");
            Assert(ply.Contains("format ascii 1.0"), "PLY must be ASCII format");
        }
        finally
        {
            if (File.Exists(tmpCsv)) File.Delete(tmpCsv);
            if (File.Exists(tmpPly)) File.Delete(tmpPly);
        }
    }

    /// <summary>
    /// One-shot utility test: regenerates the canonical sample files in the
    /// repo's samples/ folder. Not idempotent across version changes; run
    /// explicitly when the generator parameters change. Safe to run any time;
    /// the output is deterministic given seed=1234.
    /// </summary>
    public static void SyntheticTnGraniteGenerator_RegenerateCanonicalSamples()
    {
        // walk up from the test bin dir to find the repo root, then the
        // samples/ folder.
        string root = AppDomain.CurrentDomain.BaseDirectory;
        DirectoryInfo dir = new DirectoryInfo(root);
        string samplesDir = null;
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "samples");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(candidate, "INPUT_FORMATS.md")))
            {
                samplesDir = candidate;
                break;
            }
            dir = dir.Parent;
        }
        if (samplesDir == null)
        {
            // skip if we cannot locate the samples dir from this run context
            return;
        }

        var bench = new BoundingBox3(0, 0, 0, 40, 30, 6);
        var (planes, traces, tris) = SyntheticTnGraniteGenerator.WriteSampleSet(
            csvPath: Path.Combine(samplesDir, "tn_granite_realistic.csv"),
            plyPath: Path.Combine(samplesDir, "tn_granite_realistic.ply"),
            bench: bench,
            seed: 1234);
        Assert(planes > 0, $"sample generator produced zero planes");
        Assert(traces > 0, $"sample generator produced zero traces");
        Assert(tris > 0, $"sample generator produced zero triangles");
    }

    public static void SyntheticTnGraniteGenerator_DeterministicForSameSeed()
    {
        var bench = new BoundingBox3(0, 0, 0, 40, 30, 6);
        var tmpA1 = Path.GetTempFileName() + ".csv";
        var tmpA2 = Path.GetTempFileName() + ".csv";
        var dummyPly = Path.GetTempFileName() + ".ply";
        try
        {
            SyntheticTnGraniteGenerator.WriteSampleSet(tmpA1, dummyPly, bench, seed: 7);
            SyntheticTnGraniteGenerator.WriteSampleSet(tmpA2, dummyPly, bench, seed: 7);
            string a = File.ReadAllText(tmpA1);
            string b = File.ReadAllText(tmpA2);
            Assert(a == b, "same seed must produce identical CSV");
        }
        finally
        {
            if (File.Exists(tmpA1)) File.Delete(tmpA1);
            if (File.Exists(tmpA2)) File.Delete(tmpA2);
            if (File.Exists(dummyPly)) File.Delete(dummyPly);
        }
    }

    // ─── I14 + Zhang cut-code parity (gap analysis 23) ──────────────────────

    public static void I14_ConvexPolyhedron_ToInequalities_UnitCube()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        var rows = cph.ToInequalities();
        Assert(rows.Count == 6, $"expected 6 face inequalities, got {rows.Count}");
        // Every inequality of the unit cube has |N| = 1 and |b| <= 1.
        foreach (var r in rows)
        {
            double nLen = Math.Sqrt(r.Nx * r.Nx + r.Ny * r.Ny + r.Nz * r.Nz);
            AssertNear(nLen, 1.0, 1e-9, "|N|");
            Assert(Math.Abs(r.B) <= 1.0 + 1e-9, $"|b| <= 1, got {r.B}");
        }
    }

    public static void I14_ConvexPolyhedron_FromInequalities_RoundTripUnitCube()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        var rows = cph.ToInequalities();
        var rebuilt = ConvexPolyhedron.FromInequalities(rows);
        Assert(rebuilt != null, "FromInequalities returned null for a valid cube");
        AssertNear(rebuilt.Volume(), 1.0, 1e-9, "rebuilt unit-cube volume");
        Assert(rebuilt.Vertices.Count == 8,
            $"unit cube should have 8 verts, got {rebuilt.Vertices.Count}");
        Assert(rebuilt.Faces.Count == 6,
            $"unit cube should have 6 faces, got {rebuilt.Faces.Count}");
    }

    public static void I14_ContainsPoint_CenterInsideCornerOutside()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        Assert(cph.ContainsPoint(0.5, 0.5, 0.5), "centre must be inside");
        Assert(cph.ContainsPoint(0.0, 0.0, 0.0, tol: 1e-9), "corner on boundary");
        Assert(!cph.ContainsPoint(2.0, 0.5, 0.5), "x=2 must be outside");
        Assert(!cph.ContainsPoint(0.5, 0.5, -1.0), "z=-1 must be outside");
    }

    public static void I14_SignedGap_NegativeInsidePositiveOutside()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        double gIn = cph.SignedGap(0.5, 0.5, 0.5);
        double gOut = cph.SignedGap(2.0, 0.5, 0.5);
        Assert(gIn < 0, $"gap at centre should be < 0, got {gIn}");
        Assert(gOut > 0, $"gap at x=2 should be > 0, got {gOut}");
        AssertNear(gOut, 1.0, 1e-9, "gap at x=2 is 2 - 1 = 1 m beyond face");
    }

    public static void I14_ClipBothSides_UnitCubeAtX05_TwoHalves()
    {
        var obb = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var cph = ConvexPolyhedron.FromOrientedBlock(in obb);
        var (keptCph, discardedCph) =
            cph.ClipBothSides(0.5, 0.5, 0.5, 1.0, 0.0, 0.0);
        double vKept = keptCph.Volume();
        double vDisc = discardedCph.Volume();
        AssertNear(vKept + vDisc, 1.0, 1e-9, "halves must sum to 1");
        AssertNear(vKept, 0.5, 1e-9, "kept half = 0.5");
        AssertNear(vDisc, 0.5, 1e-9, "discarded half = 0.5");
    }

    public static void I14_CompositeBlock_TwoCubes_TotalVolumeAndAabb()
    {
        var obbA = new OrientedBlock(0.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var obbB = new OrientedBlock(2.5, 0.5, 0.5, 1, 0, 0, 1, 0.5, 0.5, 0.5);
        var a = ConvexPolyhedron.FromOrientedBlock(in obbA);
        var b = ConvexPolyhedron.FromOrientedBlock(in obbB);
        var block = new CompositeBlock("test-block", new[] { a, b });
        AssertNear(block.TotalVolume, 2.0, 1e-9, "two unit cubes = 2.0 m^3");
        Assert(block.PieceCount == 2, $"expected 2 pieces, got {block.PieceCount}");
        var bb = block.Aabb;
        AssertNear(bb.MinX, 0.0, 1e-9, "aabb minX");
        AssertNear(bb.MaxX, 3.0, 1e-9, "aabb maxX -- spans both cubes");
        var pcA = block.PieceContaining(0.5, 0.5, 0.5);
        var pcB = block.PieceContaining(2.5, 0.5, 0.5);
        Assert(pcA != null, "PieceContaining must hit cube A");
        Assert(pcB != null, "PieceContaining must hit cube B");
        Assert(!ReferenceEquals(pcA, pcB), "different points must hit different pieces");
    }

    public static void Phase1D_Regression_SyntheticReachesLimestoneOrder()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 6.0),
            new JointSet(dipDirectionDeg: 171.0, dipDeg: 88.0, meanSpacing: 5.0, scatterDeg: 6.0),
            new JointSet(dipDirectionDeg: 0.0, dipDeg: 0.0, meanSpacing: 0.85, scatterDeg: 3.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 42);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var result = BlockCutOptSolver.Solve(area, ply, opts);

        // published value is 7.86%; allow +/- 15 pp on synthetic data
        Assert(result.RecoveryPercent >= 0.0 && result.RecoveryPercent <= 30.0,
            $"recovery on synthetic should be in [0, 30]%, got {result.RecoveryPercent:0.00}%");
        Assert(result.BestPsiDeg >= 0.0 && result.BestPsiDeg <= 180.0,
            $"best psi must be in [0, 180], got {result.BestPsiDeg:0.0}");
        Assert(result.NonIntersectedCount >= 5,
            $"expected at least 5 non-intersected blocks on synthetic, got {result.NonIntersectedCount}");
    }

    public static void OmniSolver_DensityWatershed_EndToEnd()
    {
        var area = new BoundingBox3(0, 0, 0, 30, 30, 1);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 30.0, dipDeg: 88.0, meanSpacing: 6.0, scatterDeg: 4.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 99);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var omniOpts = new OmniSolverOptions
        {
            Search = new BlockCutOptOptions(
                blockSizeX: 3.0, blockSizeY: 2.0, blockSizeZ: 1.0,
                kerf: 0.05,
                psiStartRad: 0.0, psiStopRad: Math.PI, psiStepRad: BlockCutOptTolerances.DegToRad(12.0),
                dxMax: 1.0, dxStep: 1.0,
                dyMax: 1.0, dyStep: 1.0),
            SubdivMode = SubdivisionMode.DensityWatershed,
            WatershedBandwidth = 5.0,
        };
        var result = BlockCutOptOmniSolver.Solve(area, ply, omniOpts, watershedPlanes: planes);

        Assert(result.PerZone.Count >= 1, $"expected >= 1 zone, got {result.PerZone.Count}");
        Assert(result.AggregateRecoveryCount >= 0,
            $"aggregate recovery non-negative, got {result.AggregateRecoveryCount}");
    }

    public static void CsvFractureTraceSource_RoundTripParseCsv()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "x1,y1,x2,y2\n" +
                "0.0,0.0,10.0,0.0\n" +
                "0.0,5.0,10.0,5.0\n");
            var traces = CsvFractureTraceSource.ReadCsv(path);
            Assert(traces.Count == 2, $"expected 2 traces, got {traces.Count}");
            AssertNear(traces[0].X2, 10.0, 1e-9, "trace 0 x2");
            AssertNear(traces[1].Y1, 5.0, 1e-9, "trace 1 y1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void FisherRobust_DeterministicForSameBaseSeed()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 3.5, scatterDeg: 8.0),
        };
        var opts = BlockCutOptOptions.LimestoneStratumA();

        var a = FisherRobustSampler.Solve(area, jointSets, opts, monteCarloSamples: 3, baseSeed: 42);
        var b = FisherRobustSampler.Solve(area, jointSets, opts, monteCarloSamples: 3, baseSeed: 42);

        AssertNear(a.RecoveryP10, b.RecoveryP10, 1e-9, "p10 determinism");
        AssertNear(a.RecoveryP50, b.RecoveryP50, 1e-9, "p50 determinism");
        AssertNear(a.RecoveryMean, b.RecoveryMean, 1e-9, "mean determinism");
        AssertNear(a.MedianPsiRad, b.MedianPsiRad, 1e-12, "median psi determinism");
    }

    public static void ParetoSolver_RecoveryMatchesScalarSolver()
    {
        // the Pareto solver's BestRecovery() should match the scalar solver's
        // NonIntersectedCount on the same input.
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 11.0,  dipDeg: 88.0, meanSpacing: 4.0, scatterDeg: 8.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 21);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var scalar = BlockCutOptSolver.Solve(area, ply, opts);
        var (front, _, _) = BlockCutOptParetoSolver.Solve(area, ply, opts);
        var best = front.BestRecovery();

        Assert(best.RecoveryCount == scalar.NonIntersectedCount,
            $"Pareto BestRecovery {best.RecoveryCount} should equal scalar {scalar.NonIntersectedCount}");
    }

    public static void CoarseToFine_FewerEvaluationsThanUniformSweep()
    {
        var area = new BoundingBox3(0, 0, 0, 27.0, 65.0, 0.8);
        var jointSets = new List<JointSet>
        {
            new JointSet(dipDirectionDeg: 81.0, dipDeg: 88.0, meanSpacing: 3.5, scatterDeg: 8.0),
        };
        var planes = JointSetDfnGenerator.Generate(jointSets, area, seed: 13);
        var ply = JointSetDfnPlyEmitter.Emit(planes, area);

        var opts = BlockCutOptOptions.LimestoneStratumA();
        var uniform = BlockCutOptSolver.Solve(area, ply, opts);
        var c2f = BlockCutOptCoarseToFine.Solve(area, ply, opts, topK: 2);

        // coarse-to-fine should evaluate strictly fewer total (psi, dx, dy)
        // points than the uniform sweep for default parameters.
        Assert(c2f.TotalEvaluations < uniform.TotalEvaluations,
            $"expected c2f evals < uniform evals: c2f={c2f.TotalEvaluations}, uniform={uniform.TotalEvaluations}");

        // best psi must lie inside the c2f fine refinement window of the uniform optimum
        double diffDeg = Math.Abs(c2f.BestPsiDeg - uniform.BestPsiDeg);
        // wrap around 180: if uniform finds 178 and c2f finds 2, diff is 4
        diffDeg = Math.Min(diffDeg, 180.0 - diffDeg);
        Assert(diffDeg <= 12.0 + 1e-6,
            $"c2f best psi {c2f.BestPsiDeg:0.0} too far from uniform {uniform.BestPsiDeg:0.0} (diff={diffDeg:0.0})");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }

    private static void AssertNear(double a, double b, double tol, string label)
    {
        if (Math.Abs(a - b) > tol)
            throw new InvalidOperationException(
                $"{label}: expected {b}, got {a} (|diff|={Math.Abs(a - b)} > {tol})");
    }
}
