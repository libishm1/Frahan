#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Frahan.Masonry.Quarry.CutOpt;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // Layer 7 (QuarryCutOpt) Grasshopper components.
    //
    // Spec: wiki/specs/10_frahan_quarrycutopt_spec.md.
    //
    // Pipeline:
    //   FrahanQuarryInventory
    //     -> FrahanQuarryYieldEstimator (uses BlockCutOpt as sub-routine)
    //     -> FrahanExtractionOrderOptimizer
    //     -> FrahanSawBedSchedule
    //     -> FrahanQuarryReport
    //
    // Component GUIDs are fresh and stable. Never reuse, never modify.
    // =========================================================================

    [DesignApplication(
        "Aggregate a list of bench-block AABBs into a QuarryInventory",
        DesignFlow.TopDown)]
    public sealed class FrahanQuarryInventoryComponent : FrahanComponentBase
    {
        public FrahanQuarryInventoryComponent()
            : base(
                "Quarry Inventory", "QInv",
                "Aggregate a list of bench-block AABBs into a QuarryInventory. " +
                "All units in metres.",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A11001-0001-4F2D-A0B0-7E60CADA17A1");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("StockpileManager.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Bench Id", "B", "Bench identifier.", GH_ParamAccess.item, "bench-1");
            p.AddBoxParameter("Block Boxes", "X", "Axis-aligned bench-block footprints (m).", GH_ParamAccess.list);
            p.AddTextParameter("Block Ids", "I", "Optional block ids. If empty, auto-generated.", GH_ParamAccess.list);
            p.AddNumberParameter("Geology Grade", "G", "Per-block geology grade 0..1 (default 1.0).", GH_ParamAccess.list);
            p.AddNumberParameter("Access Cost", "A", "Per-block access cost (default 0).", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "QuarryInventory object.", GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N", "Number of blocks.", GH_ParamAccess.item);
            p.AddNumberParameter("Total Volume (m^3)", "V", "Sum of gross volumes.", GH_ParamAccess.item);
            p.AddNumberParameter("Avg Grade", "G", "Volume-weighted average geology grade.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string benchId = "bench-1";
            var boxes = new List<Box>();
            var ids = new List<string>();
            var grades = new List<double>();
            var costs = new List<double>();

            da.GetData(0, ref benchId);
            if (!da.GetDataList(1, boxes) || boxes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one block box is required.");
                return;
            }
            da.GetDataList(2, ids);
            da.GetDataList(3, grades);
            da.GetDataList(4, costs);

            var blocks = new List<BenchBlock>(boxes.Count);
            for (int i = 0; i < boxes.Count; i++)
            {
                if (!boxes[i].IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"box {i} is invalid");
                    return;
                }
                string id = (i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]))
                    ? ids[i]
                    : $"BLK-{i:D4}";
                double grade = i < grades.Count ? grades[i] : 1.0;
                if (grade < 0) grade = 0; if (grade > 1) grade = 1;
                double cost = i < costs.Count ? costs[i] : 0.0;
                if (cost < 0) cost = 0;
                var bb = GhBlockCutOptInterop.BoxToBbox(boxes[i]);
                blocks.Add(new BenchBlock(id, bb, grade, cost));
            }

            QuarryInventory inv;
            try { inv = new QuarryInventory(benchId, blocks); }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            da.SetData(0, new GH_ObjectWrapper(inv));
            da.SetData(1, inv.Count);
            da.SetData(2, inv.TotalGrossVolume);
            da.SetData(3, inv.WeightedAverageGrade);
        }
    }

    [DesignApplication(
        "Per-block yield estimate via BlockCutOpt as a sub-routine",
        DesignFlow.TopDown)]
    public sealed class FrahanQuarryYieldEstimatorComponent : FrahanComponentBase
    {
        public FrahanQuarryYieldEstimatorComponent()
            : base(
                "Quarry Yield Estimator", "QYield",
                "Per-block yield estimate via BlockCutOpt as a sub-routine. " +
                "Returns one BlockYieldEstimate per BenchBlock.",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A11002-0001-4F2D-A0B0-7E60CADA17A2");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("YieldEstimator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "QuarryInventory from Frahan Quarry Inventory.", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "F", "Fracture mesh (BlockCutOpt format).", GH_ParamAccess.item);
            p.AddNumberParameter("Product X (m)", "Lx", "Dimension-block target X.", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Product Y (m)", "Ly", "Dimension-block target Y.", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Product Z (m)", "Lz", "Dimension-block target Z.", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf (m)", "K", "Saw kerf.", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Dx Max", "Dx", "Half-range of dx (m).", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Dx Step", "DxS", "Dx step (m).", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Dy Max", "Dy", "Half-range of dy (m).", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Dy Step", "DyS", "Dy step (m).", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Risk Normaliser", "Rn", "Fracture-triangle count divisor for risk 0..1.", GH_ParamAccess.item, 50.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Estimates", "E", "BlockYieldEstimate per BenchBlock.", GH_ParamAccess.list);
            p.AddTextParameter("Block Ids", "I", "Block ids matching the estimates list.", GH_ParamAccess.list);
            p.AddNumberParameter("Recovery %", "R", "Per-block recovery percent.", GH_ParamAccess.list);
            p.AddNumberParameter("Fracture Risk", "Rf", "Per-block fracture risk 0..1.", GH_ParamAccess.list);
            p.AddNumberParameter("Cutting Time (min)", "T", "Per-block estimated cutting time.", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var wrapper = new GH_ObjectWrapper();
            Mesh fxMesh = null;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 5.0;
            double dxMax = 1.0, dxStep = 0.5, dyMax = 1.0, dyStep = 0.5;
            double normaliser = 50.0;
            if (!da.GetData(0, ref wrapper) || !(wrapper.Value is QuarryInventory inv))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inventory input is not a QuarryInventory.");
                return;
            }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh is required.");
                return;
            }
            da.GetData(2, ref Lx); da.GetData(3, ref Ly); da.GetData(4, ref Lz);
            da.GetData(5, ref kerf);
            da.GetData(6, ref psiDeg);
            da.GetData(7, ref dxMax); da.GetData(8, ref dxStep);
            da.GetData(9, ref dyMax); da.GetData(10, ref dyStep);
            da.GetData(11, ref normaliser);

            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var bcoOpts = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: dxMax, dxStep: dxStep,
                dyMax: dyMax, dyStep: dyStep);
            var opts = new BlockYieldEstimatorOptions(bcoOpts, normaliser);
            var estimates = BlockYieldEstimator.EstimateAll(inv, ply, opts);

            var wrapList = new List<GH_ObjectWrapper>(estimates.Count);
            var ids = new List<string>(estimates.Count);
            var recovery = new List<double>(estimates.Count);
            var risks = new List<double>(estimates.Count);
            var times = new List<double>(estimates.Count);
            for (int i = 0; i < estimates.Count; i++)
            {
                var e = estimates[i];
                wrapList.Add(new GH_ObjectWrapper(e));
                ids.Add(e.BlockId);
                recovery.Add(e.RecoveryPercent);
                risks.Add(e.FractureRisk);
                times.Add(e.EstimatedCuttingTimeMin);
            }
            da.SetDataList(0, wrapList);
            da.SetDataList(1, ids);
            da.SetDataList(2, recovery);
            da.SetDataList(3, risks);
            da.SetDataList(4, times);
        }
    }

    [Algorithm("Weighted-sum greedy extraction-order sort", "Frahan-original", Note = "Weighted-sum greedy sort with a min-yield skip threshold; no published scheduling algorithm matched.")]
    [DesignApplication(
        "Order BenchBlocks by score = w_yield*yield - w_risk*risk -  w_access*access",
        DesignFlow.TopDown)]
    public sealed class FrahanExtractionOrderOptimizerComponent : FrahanComponentBase
    {
        public FrahanExtractionOrderOptimizerComponent()
            : base(
                "Extraction Order Optimizer", "QOrder",
                "Order BenchBlocks by score = w_yield*yield - w_risk*risk - " +
                "w_access*access. Blocks under min yield are skipped. Frahan-original method.",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A11003-0001-4F2D-A0B0-7E60CADA17A3");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "QuarryInventory.", GH_ParamAccess.item);
            p.AddGenericParameter("Estimates", "E", "BlockYieldEstimate list.", GH_ParamAccess.list);
            p.AddNumberParameter("Yield Weight", "Wy", "Score weight on yield fraction.", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Risk Weight", "Wr", "Score weight on fracture risk.", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Access Weight", "Wa", "Score weight on access cost.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Min Yield", "My", "Yield fraction 0..1 below which a block is skipped.", GH_ParamAccess.item, 0.10);
            p.AddNumberParameter("Access Normaliser", "An", "Divisor for access cost.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Plan", "P", "ExtractionPlan object.", GH_ParamAccess.item);
            p.AddTextParameter("Order Ids", "I", "Block ids in extraction order.", GH_ParamAccess.list);
            p.AddNumberParameter("Scores", "S", "Score for each accepted block.", GH_ParamAccess.list);
            p.AddTextParameter("Skipped Ids", "Sk", "Block ids skipped (low yield).", GH_ParamAccess.list);
            p.AddNumberParameter("Total Recoverable (m^3)", "Vr", "Sum of recoverable volumes.", GH_ParamAccess.item);
            p.AddNumberParameter("Total Waste (m^3)", "Vw", "Sum of waste volumes.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var invWrapper = new GH_ObjectWrapper();
            var estWrappers = new List<GH_ObjectWrapper>();
            double wy = 1.0, wr = 1.0, wa = 0.0;
            double minY = 0.10, accNorm = 1.0;
            if (!da.GetData(0, ref invWrapper) || !(invWrapper.Value is QuarryInventory inv))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inventory input is not a QuarryInventory.");
                return;
            }
            if (!da.GetDataList(1, estWrappers) || estWrappers.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Estimates list is empty.");
                return;
            }
            da.GetData(2, ref wy); da.GetData(3, ref wr); da.GetData(4, ref wa);
            da.GetData(5, ref minY); da.GetData(6, ref accNorm);

            var estimates = new List<BlockYieldEstimate>(estWrappers.Count);
            for (int i = 0; i < estWrappers.Count; i++)
            {
                if (!(estWrappers[i].Value is BlockYieldEstimate e))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"estimates[{i}] is not a BlockYieldEstimate");
                    return;
                }
                estimates.Add(e);
            }

            ExtractionPlan plan;
            try
            {
                var opts = new ExtractionOrderOptions(wy, wr, wa, minY, accNorm);
                plan = ExtractionOrderOptimizer.Plan(inv, estimates, opts);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var orderIds = new List<string>(plan.Accepted.Count);
            var scores = new List<double>(plan.Accepted.Count);
            foreach (var e in plan.Accepted) { orderIds.Add(e.Block.Id); scores.Add(e.Score); }
            var skippedIds = new List<string>(plan.Skipped.Count);
            foreach (var e in plan.Skipped) skippedIds.Add(e.Block.Id);

            da.SetData(0, new GH_ObjectWrapper(plan));
            da.SetDataList(1, orderIds);
            da.SetDataList(2, scores);
            da.SetDataList(3, skippedIds);
            da.SetData(4, plan.TotalRecoverableVolume);
            da.SetData(5, plan.TotalWasteVolume);
        }
    }

    [Algorithm("Greedy LPT list scheduling", "Graham 1969, Bounds on multiprocessing timing anomalies, SIAM J. Appl. Math. 17(2):416-429", Doi = "10.1137/0117039", WikiPath = "wiki/index/references.md#Graham1969LPT")]
    [DesignApplication(
        "Greedy LPT schedule of accepted blocks onto N saw beds",
        DesignFlow.TopDown)]
    public sealed class FrahanSawBedScheduleComponent : FrahanComponentBase
    {
        public FrahanSawBedScheduleComponent()
            : base(
                "Saw-Bed Schedule", "QSched",
                "Greedy LPT schedule of accepted blocks onto N saw beds. " +
                "Returns per-bed timelines and the total makespan. Implements greedy LPT scheduling (Graham 1969).",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A11004-0001-4F2D-A0B0-7E60CADA17A4");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("CncRoughing.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Plan", "P", "ExtractionPlan.", GH_ParamAccess.item);
            p.AddIntegerParameter("Bed Count", "N", "Number of saw beds (>= 1).", GH_ParamAccess.item, 2);
            p.AddNumberParameter("Setup (min)", "S", "Fixed inter-block setup time per bed.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Schedule", "Sc", "SawBedSchedule object.", GH_ParamAccess.item);
            p.AddTextParameter("Bed Summary", "BS", "One line per bed.", GH_ParamAccess.list);
            p.AddNumberParameter("Makespan (min)", "M", "Schedule makespan.", GH_ParamAccess.item);
            p.AddIntegerParameter("Slot Count", "K", "Total scheduled slots.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var wrapper = new GH_ObjectWrapper();
            int bedCount = 2;
            double setup = 0.0;
            if (!da.GetData(0, ref wrapper) || !(wrapper.Value is ExtractionPlan plan))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Plan input is not an ExtractionPlan.");
                return;
            }
            da.GetData(1, ref bedCount);
            da.GetData(2, ref setup);
            if (bedCount < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bed count must be >= 1.");
                return;
            }

            SawBedSchedule sched;
            try
            {
                sched = SawBedScheduler.Schedule(plan, new SawBedSchedulerOptions(bedCount, setup));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var summaries = new List<string>(sched.Timelines.Count);
            foreach (var t in sched.Timelines)
                summaries.Add($"Bed#{t.BedIndex}: N={t.Slots.Count}, end={t.LoadEndMin:0.0} min");

            da.SetData(0, new GH_ObjectWrapper(sched));
            da.SetDataList(1, summaries);
            da.SetData(2, sched.MakespanMin);
            da.SetData(3, sched.TotalSlotCount);
        }
    }

    [DesignApplication(
        "Aggregate Inventory + ExtractionPlan + SawBedSchedule into  a Markdown summary plus headline numbers",
        DesignFlow.TopDown)]
    public sealed class FrahanQuarryReportComponent : FrahanComponentBase
    {
        public FrahanQuarryReportComponent()
            : base(
                "Quarry Report", "QRep",
                "Aggregate Inventory + ExtractionPlan + SawBedSchedule into " +
                "a Markdown summary plus headline numbers.",
                "Frahan", "Quarry")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A11005-0001-4F2D-A0B0-7E60CADA17A5");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("PackDiagnostics.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "QuarryInventory.", GH_ParamAccess.item);
            p.AddGenericParameter("Plan", "P", "ExtractionPlan.", GH_ParamAccess.item);
            p.AddGenericParameter("Schedule", "Sc", "SawBedSchedule.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Report", "R", "QuarryReport object.", GH_ParamAccess.item);
            p.AddTextParameter("Markdown", "MD", "Report rendered as Markdown.", GH_ParamAccess.item);
            p.AddNumberParameter("Yield (m^3)", "Vy", "Total recoverable yield.", GH_ParamAccess.item);
            p.AddNumberParameter("Waste (m^3)", "Vw", "Total waste.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery %", "R%", "Overall recovery percent.", GH_ParamAccess.item);
            p.AddNumberParameter("Makespan (min)", "M", "Schedule makespan.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var invW = new GH_ObjectWrapper();
            var planW = new GH_ObjectWrapper();
            var schedW = new GH_ObjectWrapper();
            if (!da.GetData(0, ref invW) || !(invW.Value is QuarryInventory inv))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inventory is not a QuarryInventory"); return; }
            if (!da.GetData(1, ref planW) || !(planW.Value is ExtractionPlan plan))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Plan is not an ExtractionPlan"); return; }
            if (!da.GetData(2, ref schedW) || !(schedW.Value is SawBedSchedule sched))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Schedule is not a SawBedSchedule"); return; }

            var report = QuarryReportBuilder.Build(inv, plan, sched);
            var md = QuarryReportBuilder.ToMarkdown(report);
            da.SetData(0, new GH_ObjectWrapper(report));
            da.SetData(1, md);
            da.SetData(2, report.TotalYieldVolume);
            da.SetData(3, report.TotalWasteVolume);
            da.SetData(4, report.OverallRecoveryPercent);
            da.SetData(5, report.MakespanMin);
        }
    }
}
