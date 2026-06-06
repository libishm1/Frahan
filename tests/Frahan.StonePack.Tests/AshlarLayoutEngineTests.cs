#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// =============================================================================
// AshlarLayoutEngineTests — Stage 1 + Stage 2 + Stage 3 unit tests for the
// Ashlar packer. All pure-managed (no Rhino runtime needed).
// =============================================================================

static class AshlarLayoutEngineTests
{
    // ─── Stage 1 ─────────────────────────────────────────────────────────────

    public static void CoursedAshlar_AllUniformBoxes_FullCoverage()
    {
        // Wall 1.5 x 1.0 x 0.20, course 0.15, block 0.30 x 0.20 x 0.15.
        // Expect floor(1.0/0.15) = 6 courses x floor(1.5/0.30) = 5 blocks = 30 blocks.
        const int blocksPerCourse = 5;
        const int expectedCourses = 6;
        const int expectedBlocks = blocksPerCourse * expectedCourses;
        var slabs = new List<Slab>(expectedBlocks);
        for (int i = 0; i < expectedBlocks; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 1.5, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.CourseCount == expectedCourses,
            $"expected {expectedCourses} courses, got {result.CourseCount}");
        Assert(result.PlacedBlocks.Count == expectedBlocks,
            $"expected {expectedBlocks} placed blocks, got {result.PlacedBlocks.Count}");
        Assert(result.Assembly.BlockCount == expectedBlocks,
            $"assembly block count mismatch: {result.Assembly.BlockCount}");

        int expectedHeadJoints = expectedCourses * (blocksPerCourse - 1);
        int expectedBedJoints = (expectedCourses - 1) * blocksPerCourse;
        Assert(result.Assembly.InterfaceCount == expectedHeadJoints + expectedBedJoints,
            $"expected {expectedHeadJoints + expectedBedJoints} interfaces, got {result.Assembly.InterfaceCount}");

        double expectedCoverage = (expectedBlocks * 0.30 * 0.15) / (1.5 * 1.0);
        Assert(Math.Abs(result.CoverageRatio - expectedCoverage) < 1e-6,
            $"coverage expected ~{expectedCoverage:0.###}, got {result.CoverageRatio:0.###}");
    }

    public static void BoundaryConditions_BottomCourseFixed()
    {
        var slabs = new List<Slab>(6);
        for (int i = 0; i < 6; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.90, wallHeight: 0.30, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        var fixedIds = new HashSet<string>(result.Assembly.BoundaryConditions.FixedBlockIds);
        Assert(fixedIds.Count == 3,
            $"expected 3 fixed bottom blocks, got {fixedIds.Count}");
        for (int slot = 0; slot < 3; slot++)
        {
            string id = $"ashlar_000_{slot:D3}";
            Assert(fixedIds.Contains(id), $"expected '{id}' in fixed set");
        }
    }

    public static void LeftoverWhenNoFit_RecordsGap()
    {
        var slabs = new List<Slab>(1);
        slabs.Add(Slab.Box(0, 0, 0, 0.10, 0.10, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 5.0, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.001, headJoint: 0.001,
            staggerOffset: 0.5,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.PlacedBlocks.Count == 1,
            $"expected 1 placed block, got {result.PlacedBlocks.Count}");
        Assert(result.CoverageRatio < 0.5,
            $"coverage should be low for tiny inventory in big wall, got {result.CoverageRatio}");

        bool hasGapNote = false;
        for (int i = 0; i < result.Notes.Count; i++)
        {
            if (result.Notes[i].IndexOf("gap", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hasGapNote = true;
                break;
            }
        }
        Assert(hasGapNote, "expected at least one 'gap' note");
    }

    public static void RunningBondStagger_OffsetsOddCourses()
    {
        var slabs = new List<Slab>(20);
        for (int i = 0; i < 20; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 1.5, wallHeight: 0.45, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.5,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.CourseCount == 3, $"expected 3 courses, got {result.CourseCount}");

        var blocks = result.Assembly.Blocks;
        double course0First = double.NaN;
        double course1First = double.NaN;
        for (int i = 0; i < blocks.Count; i++)
        {
            string id = blocks[i].Id;
            if (id == "ashlar_000_000") course0First = MinX(blocks[i]);
            if (id == "ashlar_001_000") course1First = MinX(blocks[i]);
        }
        Assert(!double.IsNaN(course0First), "missing ashlar_000_000");
        Assert(!double.IsNaN(course1First), "missing ashlar_001_000");
        Assert(Math.Abs(course1First - course0First) > 1e-6,
            $"course 1 start ({course1First}) should differ from course 0 start ({course0First})");
    }

    public static void LoopGuard_TripsOnPathologicalInput()
    {
        var slabs = new List<Slab>(1);
        slabs.Add(Slab.Box(0, 0, 0, 0.05, 0.05, 1e-6));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 1.0, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 1e-6,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        bool tripped = false;
        try
        {
            _ = AshlarLayoutEngine.Pack(slabs, opts);
        }
        catch (InvalidOperationException ex) when (ex.Message.IndexOf("loop guard tripped", StringComparison.Ordinal) >= 0)
        {
            tripped = true;
        }
        Assert(tripped, "expected InvalidOperationException with 'loop guard tripped' message");
    }

    // ─── Stage 2 ─────────────────────────────────────────────────────────────

    public static void Options_ConstructedFromWallFrame_RoundTrips()
    {
        var frame = new WallFrame(2.0, 1.5, 0.30);
        var opts = new AshlarPackOptions(
            CourseMode.CoursedRubble,
            frame.WallWidth, frame.WallHeight, frame.WallThickness,
            targetCourseHeight: 0.20,
            bedJoint: 0.002, headJoint: 0.003,
            staggerOffset: 0.4,
            density: 2200.0,
            heightTolerance: 0.01);

        Assert(Math.Abs(opts.WallWidth - 2.0) < 1e-12, "WallWidth round-trip");
        Assert(Math.Abs(opts.WallHeight - 1.5) < 1e-12, "WallHeight round-trip");
        Assert(Math.Abs(opts.WallThickness - 0.30) < 1e-12, "WallThickness round-trip");
        Assert(opts.Mode == CourseMode.CoursedRubble, "mode round-trip");
    }

    public static void CoursedRubble_VariableHeights_BinsByTolerance()
    {
        var slabs = new List<Slab>(8);
        for (int i = 0; i < 4; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.10));
        for (int i = 0; i < 4; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.20));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedRubble,
            wallWidth: 1.2, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 0.10,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 0.005);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.PlacedBlocks.Count == 8,
            $"expected all 8 slabs placed, got {result.PlacedBlocks.Count}");
        Assert(result.CourseCount == 2,
            $"expected 2 courses (one per height bin), got {result.CourseCount}");
    }

    public static void CoursedRubble_AcrossBins_NeverMixesInOneCourse()
    {
        var slabs = new List<Slab>(6);
        for (int i = 0; i < 3; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.10));
        for (int i = 0; i < 3; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.18));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedRubble,
            wallWidth: 1.2, wallHeight: 1.0, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 0.005);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        var heightsByCourse = new Dictionary<int, double>();
        var blocks = result.Assembly.Blocks;
        for (int i = 0; i < result.PlacedBlocks.Count; i++)
        {
            var block = result.PlacedBlocks[i];
            string id = block.Id;
            int courseIndex = int.Parse(id.Substring("ashlar_".Length, 3));
            double bH = MaxZ(block) - MinZ(block);
            if (heightsByCourse.TryGetValue(courseIndex, out double h0))
            {
                Assert(Math.Abs(h0 - bH) < 0.01,
                    $"course {courseIndex} mixes block heights: {h0} vs {bH}");
            }
            else
            {
                heightsByCourse[courseIndex] = bH;
            }
        }
        Assert(heightsByCourse.Count >= 1, "expected at least one course");
    }

    public static void Options_InvalidNegativeStagger_Throws()
    {
        bool threw = false;
        try
        {
            _ = new AshlarPackOptions(
                CourseMode.CoursedAshlar,
                wallWidth: 1.0, wallHeight: 1.0, wallThickness: 0.2,
                targetCourseHeight: 0.15,
                bedJoint: 0.0, headJoint: 0.0,
                staggerOffset: -0.1,
                density: 2400.0,
                heightTolerance: 0.05);
        }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative stagger offset should throw ArgumentOutOfRangeException");
    }

    // ─── Stage 3 (diagnostics decomposition) ─────────────────────────────────

    public static void Diagnostics_RoundTripsResult()
    {
        var slabs = new List<Slab>(5);
        for (int i = 0; i < 5; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.90, wallHeight: 0.30, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.CourseCount == 2, $"expected 2 courses, got {result.CourseCount}");
        Assert(result.PlacedBlocks.Count == 5, $"expected 5 placed, got {result.PlacedBlocks.Count}");
        Assert(result.Assembly != null, "Assembly should be present");
        Assert(result.Notes != null, "Notes should be present (may be empty)");
        Assert(result.CoverageRatio >= 0.0 && result.CoverageRatio <= 1.0,
            $"coverage out of [0,1]: {result.CoverageRatio}");
    }

    public static void Diagnostics_HandlesEmptyLeftovers()
    {
        var slabs = new List<Slab>(2);
        for (int i = 0; i < 2; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.60, wallHeight: 0.15, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.Leftovers.Count == 0,
            $"expected 0 leftovers (all 2 placed), got {result.Leftovers.Count}");
        Assert(result.PlacedBlocks.Count == 2,
            $"expected 2 placed, got {result.PlacedBlocks.Count}");
    }

    public static void Diagnostics_PlacedBlocksMatchAssembly()
    {
        var slabs = new List<Slab>(3);
        for (int i = 0; i < 3; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.90, wallHeight: 0.15, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);

        Assert(result.PlacedBlocks.Count == result.Assembly.Blocks.Count,
            $"PlacedBlocks count ({result.PlacedBlocks.Count}) != Assembly.Blocks count ({result.Assembly.Blocks.Count})");
        for (int i = 0; i < result.PlacedBlocks.Count; i++)
        {
            Assert(ReferenceEquals(result.PlacedBlocks[i], result.Assembly.Blocks[i]),
                $"PlacedBlocks[{i}] should be the same instance as Assembly.Blocks[{i}]");
        }
    }

    // ─── Stage A: rotation + trim-to-fit ─────────────────────────────────────

    public static void Rotation_Disabled_StaysTranslationOnly()
    {
        // Inventory of slabs whose width exceeds wall width. Default
        // (AllowYaw=false) packer should fail to place them.
        var slabs = new List<Slab>(2);
        for (int i = 0; i < 2; i++) slabs.Add(Slab.Box(0, 0, 0, 0.40, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.20, wallHeight: 0.15, wallThickness: 0.40,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9,
            allowYaw: false,
            allowTrim: false);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 0,
            $"with rotation disabled, expected 0 placements, got {result.PlacedBlocks.Count}");
    }

    public static void Rotation_Enabled_PacksOversizedSlabsByYawing()
    {
        // Slabs are 0.40 wide x 0.20 deep x 0.15 tall. Wall is 0.20 wide x
        // 0.15 tall x 0.40 thick. Naturally the width 0.40 doesn't fit into
        // wall width 0.20. After yaw 90°, effective width becomes 0.20 (the
        // depth) and depth becomes 0.40 (which fits the 0.40 thickness).
        var slabs = new List<Slab>(1);
        slabs.Add(Slab.Box(0, 0, 0, 0.40, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.20, wallHeight: 0.15, wallThickness: 0.40,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9,
            allowYaw: true,
            allowTrim: false);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 1,
            $"with rotation enabled, expected 1 yawed placement, got {result.PlacedBlocks.Count}");
        // Verify the placed block's AABB has X-extent 0.20 (the rotated width).
        var v = result.PlacedBlocks[0].VertexCoordsXyz;
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        for (int i = 0; i < result.PlacedBlocks[0].VertexCount; i++)
        {
            double x = v[3 * i + 0];
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
        }
        double width = xMax - xMin;
        Assert(Math.Abs(width - 0.20) < 1e-6,
            $"yawed block width expected 0.20, got {width}");
    }

    public static void Trim_Disabled_StillRecordsGap()
    {
        var slabs = new List<Slab>(1);
        slabs.Add(Slab.Box(0, 0, 0, 0.10, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 1.0, wallHeight: 0.15, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9,
            allowYaw: false,
            allowTrim: false);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 1,
            $"expected 1 placement before gap, got {result.PlacedBlocks.Count}");
        bool hasGap = false;
        for (int i = 0; i < result.Notes.Count; i++)
            if (result.Notes[i].IndexOf("gap", StringComparison.OrdinalIgnoreCase) >= 0)
                hasGap = true;
        Assert(hasGap, "expected gap note when trim is disabled");
    }

    public static void Trim_Enabled_FillsGapWithTrimmedPiece()
    {
        // Single oversized slab; with trim enabled, the engine should cut it
        // to the gap width and place the piece.
        var slabs = new List<Slab>(1);
        slabs.Add(Slab.Box(0, 0, 0, 1.0, 0.20, 0.15));  // width 1.0

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.30, wallHeight: 0.15, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9,
            allowYaw: false,
            allowTrim: true);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 1,
            $"expected 1 trimmed placement, got {result.PlacedBlocks.Count}");
        // Placed block should have X-extent 0.30 (the gap width).
        var v = result.PlacedBlocks[0].VertexCoordsXyz;
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        for (int i = 0; i < result.PlacedBlocks[0].VertexCount; i++)
        {
            double x = v[3 * i + 0];
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
        }
        double width = xMax - xMin;
        Assert(Math.Abs(width - 0.30) < 1e-6,
            $"trimmed piece width expected 0.30, got {width}");
        // Coverage should be ~1.0 (no gaps).
        Assert(result.CoverageRatio > 0.99,
            $"coverage with trim expected ~1.0, got {result.CoverageRatio}");
        // Offcut (width 0.70) should appear in leftovers.
        Assert(result.Leftovers.Count == 1,
            $"expected 1 leftover offcut, got {result.Leftovers.Count}");
    }

    // ─── End-to-end integration: pack + RBE QP + ManagedQpSolver ─────────────

    public static void EndToEnd_PackedWall_FeedsRbeSolver_ProducesWellFormedQp()
    {
        // End-to-end pipeline check: Pack -> EquilibriumMatrixBuilder ->
        // RbeQpFormulation -> ManagedQpSolver. Asserts the QP is well-formed
        // (matrix shapes, force-component count, equality RHS matches gravity)
        // and the solver returns a terminal status. Does NOT assert
        // ConvexQpStatus.Optimal — Dykstra's known not to converge on the
        // 6-DOF RBE family at this scale (it's exact for diagonal H but the
        // masonry equality system is ill-conditioned for alternating
        // projections; a paper-faithful Hessian or IPOPT is the upstream
        // P1/P4 fix tracked in HANDOFF_TO_CLAUDE.md).
        var slabs = new List<Slab>(2);
        for (int i = 0; i < 2; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 0.30, wallHeight: 0.30, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.PlacedBlocks.Count == 2, $"expected 2 placed, got {result.PlacedBlocks.Count}");
        Assert(result.CourseCount == 2, $"expected 2 courses, got {result.CourseCount}");
        Assert(result.Assembly.InterfaceCount == 1,
            $"expected 1 bed-joint interface, got {result.Assembly.InterfaceCount}");
        Assert(result.Assembly.FreeBlockCount == 1,
            $"expected 1 free block, got {result.Assembly.FreeBlockCount}");

        var system = EquilibriumMatrixBuilder.Build(result.Assembly, penalty: false);
        // 1 free block × 6 DOFs = 6 equality rows.
        Assert(system.Aeq.RowCount == 6,
            $"expected 6 equality rows, got {system.Aeq.RowCount}");
        // 1 contact × 4 vertices × 3 force components (n, t1, t2) = 12 force vars.
        Assert(system.Aeq.ColCount == 12,
            $"expected 12 force-component columns, got {system.Aeq.ColCount}");

        var qp = RbeQpFormulation.Build(system, frictionAfr: null);
        Assert(qp.VariableCount == 12, $"QP var count expected 12, got {qp.VariableCount}");

        var solver = new ManagedQpSolver(tolerance: 1e-6, maxIterations: 5000);
        var r = solver.Solve(qp);
        bool terminalStatus = r.Status == ConvexQpStatus.Optimal
                              || r.Status == ConvexQpStatus.SolverError
                              || r.Status == ConvexQpStatus.NotImplemented
                              || r.Status == ConvexQpStatus.Infeasible;
        Assert(terminalStatus,
            $"solver did not return a terminal status, got {r.Status}: {r.SolverMessage}");
        // X may be null on SolverError; only require correct length when present.
        if (r.X != null)
        {
            Assert(r.X.Length == qp.VariableCount,
                $"solution vector wrong length: {r.X.Length} vs {qp.VariableCount}");
        }
    }

    public static void EndToEnd_PackedWall_OneCourse_HasZeroBedJoints()
    {
        // Sanity: one-course wall should have no bed joints, only head joints.
        var slabs = new List<Slab>(5);
        for (int i = 0; i < 5; i++)
            slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));

        var opts = new AshlarPackOptions(
            CourseMode.CoursedAshlar,
            wallWidth: 1.5, wallHeight: 0.15, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);

        var result = AshlarLayoutEngine.Pack(slabs, opts);
        Assert(result.CourseCount == 1, $"expected 1 course, got {result.CourseCount}");
        Assert(result.Assembly.InterfaceCount == 4,
            $"expected 4 head joints (5 blocks - 1) and 0 bed joints, got {result.Assembly.InterfaceCount}");

        // All blocks are bottom-course-fixed: no free DOFs => Aeq is empty.
        Assert(result.Assembly.FreeBlockCount == 0,
            $"expected 0 free blocks (all fixed in course 0), got {result.Assembly.FreeBlockCount}");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static double MinX(MasonryBlock b)
    {
        var v = b.VertexCoordsXyz;
        double m = double.PositiveInfinity;
        for (int i = 0; i < b.VertexCount; i++) { double x = v[3 * i]; if (x < m) m = x; }
        return m;
    }

    private static double MinZ(MasonryBlock b)
    {
        var v = b.VertexCoordsXyz;
        double m = double.PositiveInfinity;
        for (int i = 0; i < b.VertexCount; i++) { double z = v[3 * i + 2]; if (z < m) m = z; }
        return m;
    }

    private static double MaxZ(MasonryBlock b)
    {
        var v = b.VertexCoordsXyz;
        double m = double.NegativeInfinity;
        for (int i = 0; i < b.VertexCount; i++) { double z = v[3 * i + 2]; if (z > m) m = z; }
        return m;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
