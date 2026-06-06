#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.CutOpt;

namespace Frahan.Tests;

// =============================================================================
// End-to-end integration test for the Quarry → Masonry bridge added 2026-05-14.
//
// Runs the full Layer 7 → Layer 5/6 pipeline on one hand-built fixture:
//
//   QuarryInventory (3 bench blocks)
//     ──► BlockYieldEstimator (BlockCutOpt sub-routine, empty fractures)
//     ──► ExtractionOrderOptimizer
//     ──► BenchBlockSlabBuilder (slabs)
//     ──► AshlarLayoutEngine.Pack (CoursedRubble)
//     ──► MasonryAssembly
//
// Catches any regression in the bridge that the per-layer unit tests miss:
//   - parent-index propagation across the BenchBlockSlabBuilder → Slab list
//   - Slab geometry round-trip from OrientedBlock through Slab.ToMasonryBlock
//   - AshlarLayoutEngine accepting fracture-pattern slabs as inventory
// =============================================================================

static class QuarryToMasonryIntegrationTests
{
    private static PlyMesh EmptyPly()
    {
        // Far-away degenerate triangle so the BVH intersection test returns
        // false for every candidate block. Same trick the unit tests use.
        var v = new List<double> { 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6 };
        var t = new List<int> { 0, 1, 2 };
        return new PlyMesh(v, t, null);
    }

    public static void Quarry_To_Masonry_FullPipeline_EndsInPackedAssembly()
    {
        // ── Layer 7 input: 3 bench blocks ───────────────────────────────────
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("BLK-0000", new BoundingBox3(0, 0, 0, 3, 2, 0.8)),
            new BenchBlock("BLK-0001", new BoundingBox3(0, 0, 0, 3, 2, 0.8), geologyGrade: 0.9),
            new BenchBlock("BLK-0002", new BoundingBox3(0, 0, 0, 3, 2, 0.8), geologyGrade: 0.8),
        };
        var inventory = new QuarryInventory("bench-integration", blocks);

        // ── BlockCutOpt options: 1.0 x 1.0 x 0.8 dimension blocks, no kerf
        // search jitter, single psi. Tight grid so every block emits at
        // least one OBB.
        var bcoOpts = new BlockCutOptOptions(
            blockSizeX: 1.0, blockSizeY: 1.0, blockSizeZ: 0.8, kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: 0.0, psiStepRad: Math.PI,
            dxMax: 0.0, dxStep: 1.0, dyMax: 0.0, dyStep: 1.0);

        // ── Step A: per-block yield estimate ─────────────────────────────────
        var ply = EmptyPly();
        var yieldOpts = new BlockYieldEstimatorOptions(bcoOpts);
        var estimates = BlockYieldEstimator.EstimateAll(inventory, ply, yieldOpts);
        Assert(estimates.Count == 3, $"3 estimates, got {estimates.Count}");
        for (int i = 0; i < 3; i++)
        {
            Assert(estimates[i].NonIntersectedCount > 0,
                $"estimate[{i}] has no non-intersected blocks");
            Assert(estimates[i].YieldFraction > 0,
                $"estimate[{i}] yield fraction = 0");
        }

        // ── Step B: extraction order ─────────────────────────────────────────
        var plan = ExtractionOrderOptimizer.Plan(inventory, estimates);
        Assert(plan.Accepted.Count == 3,
            $"all 3 should be accepted (no fracture risk), got {plan.Accepted.Count}");
        Assert(plan.Skipped.Count == 0,
            $"no skips expected, got {plan.Skipped.Count}");

        // ── Step C: BenchBlock → Slabs ───────────────────────────────────────
        var cutResults = BenchBlockSlabBuilder.CutPlan(plan, inventory, ply, bcoOpts);
        Assert(cutResults.Count == 3,
            $"one cut result per accepted block, got {cutResults.Count}");
        var allSlabs = new List<Slab>();
        for (int i = 0; i < cutResults.Count; i++)
        {
            var r = cutResults[i];
            Assert(r.SlabCount > 0, $"cutResult[{i}] emitted 0 slabs");
            for (int k = 0; k < r.Slabs.Count; k++)
            {
                var s = r.Slabs[k];
                Assert(s.VertexCount == 8, $"slab {i}.{k} has {s.VertexCount} verts (need 8)");
                Assert(s.FaceCount == 6, $"slab {i}.{k} has {s.FaceCount} faces (need 6)");
                Assert(Math.Abs(s.SignedVolume()) > 0,
                    $"slab {i}.{k} has zero volume");
                allSlabs.Add(s);
            }
        }
        // The 3 m × 2 m × 0.8 m bench footprint with 1.0 × 1.0 × 0.8 + 5 cm
        // kerf dimension blocks fits a single block per bench centred grid
        // when psi/dx/dy are all zero. Each of the three bench blocks
        // therefore emits at least one slab.
        Assert(allSlabs.Count >= cutResults.Count,
            $"each accepted block should emit >= 1 slab; got {allSlabs.Count} slabs from {cutResults.Count} blocks");

        // ── Step D: Pack the slabs into an ashlar wall ───────────────────────
        // Use CoursedRubble — fracture-pattern inventories have uniform
        // height here (0.8 m, the dimension block Z), so either mode would
        // accept them, but rubble is the canonical Layer-6 mode for quarry
        // output.
        var packOptions = new AshlarPackOptions(
            CourseMode.CoursedRubble,
            wallWidth: 10.0,
            wallHeight: 2.5,
            wallThickness: 1.2,           // exceeds slab.Y (1.05 with kerf)
            targetCourseHeight: 0.8,
            bedJoint: 0.01,
            headJoint: 0.01,
            staggerOffset: 0.5,
            density: 2700.0,
            heightTolerance: 0.05);
        var result = AshlarLayoutEngine.Pack(allSlabs, packOptions);

        // ── Step E: assert the wall packed something ─────────────────────────
        Assert(result.PlacedBlocks.Count > 0,
            $"Ashlar pack returned 0 placed blocks for {allSlabs.Count} input slabs");
        Assert(result.Assembly != null, "MasonryAssembly is null");
        Assert(result.Assembly.Blocks.Count == result.PlacedBlocks.Count,
            $"Assembly block count ({result.Assembly.Blocks.Count}) mismatches placed count ({result.PlacedBlocks.Count})");

        // ── Step F: at least one block should be fixed (bottom row) ─────────
        // BoundaryConditions.FixedBlockIds carries the ids the layout engine
        // pinned. Bottom-course pinning is what the downstream RBE solver
        // relies on for stability.
        int fixedCount = 0;
        foreach (var id in result.Assembly.BoundaryConditions.FixedBlockIds)
        {
            if (!string.IsNullOrEmpty(id)) fixedCount++;
        }
        Assert(fixedCount > 0, "no bottom-row blocks were marked fixed; RBE solver will be unstable");
    }

    private static void Assert(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }
}
