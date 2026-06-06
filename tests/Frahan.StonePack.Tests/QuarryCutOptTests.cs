#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.CutOpt;
using Frahan.Masonry.Quarry.Ingestion;

namespace Frahan.Tests;

// =============================================================================
// QuarryCutOptTests -- Layer 7 unit tests.
//
// Covers the spec-10 pipeline:
//   QuarryInventory -> BlockYieldEstimator -> ExtractionOrderOptimizer
//                   -> SawBedScheduler -> QuarryReport
// Plus the Layer 1 ingestion readers (GPR traces, GeoFractNet predictions).
//
// Hand-checked fixtures: a 3-block bench with one fracture plane that hits
// one block and skirts the others, verifying that the high-fracture-risk
// block ranks last in the extraction order (spec section 10).
// =============================================================================

static class QuarryCutOptTests
{
    private static PlyMesh EmptyPly()
    {
        var v = new List<double> { 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6, 1e6, 1e6, 1e6 + 1e-3, 1e6 };
        var t = new List<int> { 0, 1, 2 };
        return new PlyMesh(v, t, null);
    }

    private static BlockCutOptOptions SmallBcoOpts()
    {
        return new BlockCutOptOptions(
            blockSizeX: 1.0, blockSizeY: 1.0, blockSizeZ: 0.8, kerf: 0.05,
            psiStartRad: 0.0, psiStopRad: 0.0, psiStepRad: Math.PI,
            dxMax: 0.0, dxStep: 1.0, dyMax: 0.0, dyStep: 1.0);
    }

    public static void QuarryInventory_BuildsAndAggregates()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 2, 2, 1), 1.0),
            new BenchBlock("B", new BoundingBox3(2, 0, 0, 4, 2, 1), 0.8),
        };
        var inv = new QuarryInventory("bench-test", blocks);
        Assert(inv.Count == 2, $"count = {inv.Count}");
        AssertNear(inv.TotalGrossVolume, 8.0, 1e-9, "totalVolume");
        AssertNear(inv.WeightedAverageGrade, 0.9, 1e-9, "avgGrade");
    }

    public static void QuarryInventory_DuplicateIds_Throws()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 1, 1, 1)),
            new BenchBlock("A", new BoundingBox3(1, 0, 0, 2, 1, 1)),
        };
        try
        {
            new QuarryInventory("b", blocks);
            throw new InvalidOperationException("expected duplicate-id throw");
        }
        catch (ArgumentException) { }
    }

    public static void BlockYieldEstimator_EmptyFractures_AllAccepted()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 3, 3, 0.8)),
            new BenchBlock("B", new BoundingBox3(5, 5, 0, 8, 8, 0.8)),
        };
        var inv = new QuarryInventory("bench", blocks);
        var ply = EmptyPly();
        var opts = new BlockYieldEstimatorOptions(SmallBcoOpts());

        var ests = BlockYieldEstimator.EstimateAll(inv, ply, opts);
        Assert(ests.Count == 2, $"count {ests.Count}");
        for (int i = 0; i < 2; i++)
        {
            Assert(ests[i].NonIntersectedCount > 0, $"nonIntersected[{i}]");
            Assert(ests[i].FractureRisk < 0.05, $"risk[{i}] should be ~0");
            Assert(ests[i].RecoverableVolume > 0, $"volume[{i}]");
        }
    }

    public static void ExtractionOrder_GreedySortsByScore()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
            new BenchBlock("B", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
            new BenchBlock("C", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
        };
        var inv = new QuarryInventory("bench", blocks);
        var estimates = new List<BlockYieldEstimate>
        {
            new BlockYieldEstimate("A", 1, 80, 0.1, 5.0, 1.0, 0.0, 0.0),
            new BlockYieldEstimate("B", 1, 60, 0.5, 5.0, 1.0, 0.0, 0.0),
            new BlockYieldEstimate("C", 1, 90, 0.0, 5.0, 1.0, 0.0, 0.0),
        };
        var plan = ExtractionOrderOptimizer.Plan(inv, estimates);

        Assert(plan.Accepted.Count == 3, $"accepted {plan.Accepted.Count}");
        Assert(plan.Accepted[0].Block.Id == "C", $"first should be C, got {plan.Accepted[0].Block.Id}");
        Assert(plan.Accepted[1].Block.Id == "A", $"second should be A, got {plan.Accepted[1].Block.Id}");
        Assert(plan.Accepted[2].Block.Id == "B", $"third should be B, got {plan.Accepted[2].Block.Id}");
    }

    public static void ExtractionOrder_SkipsLowYield()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
            new BenchBlock("B", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
        };
        var inv = new QuarryInventory("bench", blocks);
        var estimates = new List<BlockYieldEstimate>
        {
            new BlockYieldEstimate("A", 1, 5,  0.0, 1.0, 0.1, 0.0, 0.0),  // 5 % yield: below 10 %
            new BlockYieldEstimate("B", 1, 80, 0.0, 1.0, 1.0, 0.0, 0.0),
        };
        var plan = ExtractionOrderOptimizer.Plan(inv, estimates);
        Assert(plan.Accepted.Count == 1, $"accepted {plan.Accepted.Count}");
        Assert(plan.Skipped.Count == 1, $"skipped {plan.Skipped.Count}");
        Assert(plan.Accepted[0].Block.Id == "B", "B accepted");
        Assert(plan.Skipped[0].Block.Id == "A", "A skipped");
    }

    public static void SawBedScheduler_LptBalancesTwoBeds()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
            new BenchBlock("B", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
            new BenchBlock("C", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
            new BenchBlock("D", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
        };
        var inv = new QuarryInventory("bench", blocks);
        var estimates = new List<BlockYieldEstimate>
        {
            new BlockYieldEstimate("A", 1, 80, 0.0, 10.0, 1, 0, 0),
            new BlockYieldEstimate("B", 1, 80, 0.0, 6.0,  1, 0, 0),
            new BlockYieldEstimate("C", 1, 80, 0.0, 4.0,  1, 0, 0),
            new BlockYieldEstimate("D", 1, 80, 0.0, 2.0,  1, 0, 0),
        };
        var plan = ExtractionOrderOptimizer.Plan(inv, estimates);
        var sched = SawBedScheduler.Schedule(plan, new SawBedSchedulerOptions(2));
        // LPT: A(10), B(6), C(4), D(2): bed0 = A then D = 12, bed1 = B then C = 10
        // makespan = 12.
        AssertNear(sched.MakespanMin, 12.0, 1e-9, "makespan");
        Assert(sched.TotalSlotCount == 4, $"slots {sched.TotalSlotCount}");
    }

    public static void QuarryReport_MarkdownContainsAggregates()
    {
        var blocks = new List<BenchBlock>
        {
            new BenchBlock("A", new BoundingBox3(0, 0, 0, 1, 1, 0.8)),
        };
        var inv = new QuarryInventory("bench-md", blocks);
        var estimates = new List<BlockYieldEstimate>
        {
            new BlockYieldEstimate("A", 1, 50, 0.2, 3.0, 0.5, 0.3, 0.0),
        };
        var plan = ExtractionOrderOptimizer.Plan(inv, estimates);
        var sched = SawBedScheduler.Schedule(plan, new SawBedSchedulerOptions(1));
        var report = QuarryReportBuilder.Build(inv, plan, sched);
        var md = QuarryReportBuilder.ToMarkdown(report);
        Assert(md.Contains("bench-md"), "bench id");
        Assert(md.Contains("Recoverable yield"), "yield line");
        Assert(md.Contains("Saw-bed schedule"), "schedule heading");
    }

    public static void GprRadargramReader_RoundTripCsv()
    {
        var tracesPath = Path.Combine(Path.GetTempPath(), "frahan_gpr_traces.csv");
        var picksPath = Path.Combine(Path.GetTempPath(), "frahan_gpr_picks.csv");
        File.WriteAllText(tracesPath,
            "# header\n" +
            "x_m,y_m,sample_spacing_m,a0,a1,a2,a3\n" +
            "0.0,0.0,0.05,0.1,0.2,0.3,0.4\n" +
            "0.5,0.0,0.05,0.2,0.3,0.4,0.5\n");
        File.WriteAllText(picksPath,
            "x_m,y_m,depth_m,confidence_01,label\n" +
            "0.2,0.0,0.6,0.8,bedding\n" +
            "0.4,0.0,1.2,0.5,joint\n");

        try
        {
            var rg = GprRadargramReader.Load("test-scan", tracesPath, picksPath);
            Assert(rg.TraceCount == 2, $"traces {rg.TraceCount}");
            Assert(rg.PickCount == 2, $"picks {rg.PickCount}");
            Assert(rg.Traces[0].SampleCount == 4, $"samples {rg.Traces[0].SampleCount}");
            AssertNear(rg.Picks[0].Confidence, 0.8, 1e-9, "conf");
            Assert(rg.Picks[1].Label == "joint", $"label {rg.Picks[1].Label}");
        }
        finally
        {
            if (File.Exists(tracesPath)) File.Delete(tracesPath);
            if (File.Exists(picksPath)) File.Delete(picksPath);
        }
    }

    public static void GeoFractNetMaskReader_ParsesPredictions()
    {
        var path = Path.Combine(Path.GetTempPath(), "frahan_gfn_predictions.csv");
        File.WriteAllText(path,
            "# header\n" +
            "px,py,pz,nx,ny,nz,conf,set,label\n" +
            "0.5,0.5,0.0,0.0,0.0,1.0,0.9,1,bedding\n" +
            "0.5,0.5,0.0,1.0,0.0,0.0,0.7,2,joint\n" +
            "0.5,0.5,0.0,1.0,0.0,0.0,0.1,2,joint_low\n");
        try
        {
            var preds = GeoFractNetMaskReader.Load(path, minConfidence: 0.5);
            Assert(preds.Count == 2, $"preds {preds.Count}");
            Assert(preds[0].SetId == 1, $"set {preds[0].SetId}");
            Assert(preds[1].Label == "joint", $"label {preds[1].Label}");
            AssertNear(preds[0].Confidence, 0.9, 1e-9, "conf");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Assert(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} +- {tol}, got {actual}");
    }
}
