#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.CutOpt;
using Frahan.Masonry.Quarry.GeoCut;
using Frahan.Masonry.Quarry.GeoPack;

namespace Frahan.Tests;

// =============================================================================
// Tests for the 2026-05-14 punchlist (items 1-6 from
// outputs/2026-05-14/connection_map/FRAHAN_PIPELINE_MAP.md):
//   - BenchBlockSlabBuilder           (Item 1)
//   - BlockCutOptSolver.SolveAndExtract (Item 2)
//   - SlabYieldOptimizer + BilletCutter (Item 3)
//   - GeoPack v0 (CrackGraph + BlockGraph + Candidates) (Item 4)
//   - BestFitInventoryPacker rubble bug fix (Item 5)
// =============================================================================

static class QuarryBridgeTests
{
    private static PlyMesh EmptyPly()
    {
        var v = new List<double> { 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6 };
        var t = new List<int> { 0, 1, 2 };
        return new PlyMesh(v, t, null);
    }

    public static void SolveAndExtract_EmptyFractures_ReturnsAllBlocks()
    {
        var area = new BoundingBox3(0, 0, 0, 3, 3, 0.8);
        var opts = new BlockCutOptOptions(
            blockSizeX: 1.0, blockSizeY: 1.0, blockSizeZ: 0.8, kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: 0.0, psiStepRad: Math.PI,
            dxMax: 0.0, dxStep: 1.0, dyMax: 0.0, dyStep: 1.0);
        var (r, grid) = BlockCutOptSolver.SolveAndExtract(area, EmptyPly(), opts);
        Assert(grid.Count == r.NonIntersectedCount, $"grid {grid.Count} != count {r.NonIntersectedCount}");
        Assert(grid.Count > 0, "grid is empty");
    }

    public static void BenchBlockSlabBuilder_OneBlock_ProducesSlabs()
    {
        var block = new BenchBlock("A", new BoundingBox3(0, 0, 0, 3, 3, 0.8));
        var opts = new BlockCutOptOptions(
            blockSizeX: 1.0, blockSizeY: 1.0, blockSizeZ: 0.8, kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: 0.0, psiStepRad: Math.PI,
            dxMax: 0.0, dxStep: 1.0, dyMax: 0.0, dyStep: 1.0);
        var result = BenchBlockSlabBuilder.CutOne(block, EmptyPly(), opts);
        Assert(result.Slabs.Count == result.Grid.Count, "slab count != grid count");
        Assert(result.Slabs.Count > 0, "no slabs emitted");
        Assert(result.Slabs[0].VertexCount == 8, $"slab[0] has {result.Slabs[0].VertexCount} verts, expected 8");
        Assert(result.Slabs[0].FaceCount == 6, $"slab[0] has {result.Slabs[0].FaceCount} faces, expected 6");
    }

    public static void SlabYieldOptimizer_PicksLongestAxis()
    {
        var block = Slab.Box(0, 0, 0, 10, 1, 1);
        var fractures = new List<FracturePlane>();
        var opts = SlabYieldOptimizerOptions.ThreeAxisAt(0.05, 0.005);
        var best = SlabYieldOptimizer.PickBest(block, fractures, opts);
        Assert(best.Plan.Axis == SlabAxis.X,
            $"longest axis (X = 10) should win, got {best.Plan.Axis}");
        Assert(best.SlabCount > 100, $"expected many slabs along X=10, got {best.SlabCount}");
    }

    public static void BilletCutter_TenCm_SplitsOneMeterIntoTen()
    {
        var slab = Slab.Box(0, 0, 0, 1.0, 0.05, 0.5);
        var plan = new BilletPlan(SlabAxis.X, billetWidthMetres: 0.10, kerfMetres: 0.0);
        var result = BilletCutter.Cut(slab, plan);
        // 0 + 9 cuts at 0.1, 0.2, ..., 0.9 → 10 billets
        Assert(result.Slabs.Count == 10, $"expected 10 billets, got {result.Slabs.Count}");
    }

    public static void GeoPack_CrackGraph_FromFracturePlanes()
    {
        var planes = new List<FracturePlane>
        {
            new FracturePlane(0.5, 0, 0, 1, 0, 0),
        };
        var graph = CrackGraphBuilder.FromPlanes(planes);
        Assert(graph.Count == 1, $"count {graph.Count}");
        Assert(graph.Cracks[0].FitPlane != null, "fit plane null");
    }

    public static void GeoPack_BlockGraph_OnePlaneSplitsBenchIntoTwo()
    {
        var bench = Slab.Box(0, 0, 0, 2, 1, 1);
        var crack = new FracturePlane(1.0, 0.5, 0.5, 1, 0, 0);
        var graph = CrackGraphBuilder.FromPlanes(new[] { crack });
        var bg = BlockGraphBuilder.Partition(bench, graph);
        Assert(bg.Count == 2, $"expected 2 cells, got {bg.Count}");
        Assert(Math.Abs(bg.TotalVolume - 2.0) < 1e-6, $"total volume {bg.TotalVolume}");
    }

    public static void GeoPack_Candidates_ToInventoryRoundtrip()
    {
        var bench = Slab.Box(0, 0, 0, 2, 1, 1);
        var crack = new FracturePlane(1.0, 0.5, 0.5, 1, 0, 0);
        var graph = CrackGraphBuilder.FromPlanes(new[] { crack });
        var bg = BlockGraphBuilder.Partition(bench, graph);
        var cands = BlockCandidateGenerator.AabbPerCell(bg);
        var inv = BlockCandidateGenerator.ToInventory("bench", cands);
        Assert(inv.Count == 2, $"inventory count {inv.Count}");
    }

    public static void BestFit_RubbleMode_PlacesVariedHeightSlabs()
    {
        // Three slabs of different heights. With CoursedRubble the packer
        // must bin by height per course (not force everyone to TargetCourseHeight).
        var slabs = new List<Slab>
        {
            Slab.Box(0, 0, 0, 0.5, 0.2, 0.10),   // height 0.10
            Slab.Box(0, 0, 0, 0.5, 0.2, 0.10),   // height 0.10
            Slab.Box(0, 0, 0, 0.5, 0.2, 0.10),   // height 0.10
            Slab.Box(0, 0, 0, 0.5, 0.2, 0.20),   // height 0.20
            Slab.Box(0, 0, 0, 0.5, 0.2, 0.20),   // height 0.20
        };
        var options = new AshlarPackOptions(
            CourseMode.CoursedRubble,
            wallWidth: 1.5, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 0.10, bedJoint: 0.001, headJoint: 0.001,
            staggerOffset: 0.5, density: 2400.0, heightTolerance: 0.005);
        var result = BestFitInventoryPacker.Pack(slabs, options);
        Assert(result.PlacedBlocks.Count >= 3,
            $"BestFit rubble path must place at least 3 of 5 varied-height slabs, got {result.PlacedBlocks.Count}");
    }

    private static void Assert(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
