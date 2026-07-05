#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // BlockCutOpt inspector / extension components, 2026-05-15.
    //
    // Closes gaps 1, 3, 4, 6 of the 2026-05-15 Quarry-coverage audit, plus a
    // bonus mixed-size packer (gap 8) that answers Libish's question
    // "can we pack multiple sizes instead of one size for the entire quarry?".
    //
    // Components in this file:
    //   FrahanParetoFrontInspectorComponent     gap 1 (BCOPareto)
    //   FrahanFisherRobustComponent             gap 4 (BCORobust)
    //   FrahanDensityWatershedZonesComponent    gap 3 (BCOWatershed)
    //   FrahanVtuExportComponent                gap 6 (VtuOut)
    //   FrahanMixedSizeBlockPackComponent       bonus (BCOMixedPack)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 1. Pareto Front Inspector -- 4-axis Pareto extrema per sub-zone.
    //    Wraps BlockCutOptOmniSolver and surfaces all four optima (recovery,
    //    revenue, kerf-time, BCSdbBV) in parallel; the original BCOOmni only
    //    returns three of the four.
    // -------------------------------------------------------------------------
    [Algorithm("Pareto multi-objective front (BCSdbBV cost axis)", "Jalalian (2023) BCSdbBV cost objective = cutting-surface area / block value", WikiPath = "wiki/index/references.md")]
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Production solver; this inspector visualises the Pareto front of a solver run.")]
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Omni Solve", Reason = "Multi-objective production solver.")]
    [DesignApplication(
        "Run BlockCutOpt with 4-axis Pareto optimisation and emit the  recovery-max, revenue-max, kerf-time-min and ...",
        DesignFlow.TopDown)]
    public sealed class FrahanParetoFrontInspectorComponent : FrahanComponentBase
    {
        public FrahanParetoFrontInspectorComponent()
            : base(
                "Pareto Front Inspector", "BCOPareto",
                "Run BlockCutOpt with 4-axis Pareto optimisation and emit the " +
                "recovery-max, revenue-max, kerf-time-min and BCSdbBV-min " +
                "points side-by-side, per sub-zone. Use when the BCOOmni " +
                "single best-recovery output is not enough and you need to " +
                "compare trade-offs explicitly. " +
                "Implements BCSdbBV cost axis (Jalalian 2023).",
                "Frahan", "Lab")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC10-1234-4F2D-A0B0-7E60CADA15B0");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => IconProvider.Load("YieldEstimator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "F", "Fracture mesh.", GH_ParamAccess.item);
            p.AddIntegerParameter("Mx", "Mx", "Uniform sub-divisions in X.", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("My", "My", "Uniform sub-divisions in Y.", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Block X", "Lx", "Block length (m).", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Block Y", "Ly", "Block width (m).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Block Z", "Lz", "Block height (m).", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf", "K", "Material-lost-by-quarrying (m).", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("RMV per Block", "Rmv", "Jalalian relative money value per block (BCSdbBV denominator factor).", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("BV per Block", "Bv", "Jalalian block value per block.", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Kerf Time / Block (min)", "Kt", "Saw kerf time per block (min).", GH_ParamAccess.item, 24.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Zone Id", "Z", "Sub-zone id per row.", GH_ParamAccess.list);
            p.AddIntegerParameter("Recovery Max -- Count", "Nr", "Best-recovery non-intersected count per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Recovery Max -- Psi (deg)", "Pr", "Best-recovery psi per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Revenue Max -- Pi", "Pi", "Best-revenue Pi per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Revenue Max -- Psi (deg)", "Ppi", "Best-revenue psi per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Kerf Time Min -- tau", "Tau", "Min kerf-time tau per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("Kerf Time Min -- Psi (deg)", "Ptau", "Min-kerf-time psi per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("BCSdbBV Min", "BCS", "Min BCSdbBV cost (Jalalian) per zone.", GH_ParamAccess.list);
            p.AddNumberParameter("BCSdbBV Min -- Psi (deg)", "Pbcs", "Min-BCSdbBV psi per zone.", GH_ParamAccess.list);
            p.AddIntegerParameter("Pareto Front Size", "Fz", "Number of non-dominated points per zone.", GH_ParamAccess.list);
            p.AddIntegerParameter("Total Evaluations", "Ev", "Sum of (psi, dx, dy) samples evaluated.", GH_ParamAccess.item);
            p.AddNumberParameter("Elapsed (ms)", "T", "Wall-clock duration.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            Mesh fxMesh = null;
            int mx = 1, my = 1;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            double rmv = 1.0, bv = 1.0, kt = 24.0;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh required."); return; }
            da.GetData(2, ref mx); da.GetData(3, ref my);
            da.GetData(4, ref Lx); da.GetData(5, ref Ly); da.GetData(6, ref Lz);
            da.GetData(7, ref kerf); da.GetData(8, ref psiDeg);
            da.GetData(9, ref rmv); da.GetData(10, ref bv); da.GetData(11, ref kt);

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var search = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: 1.5, dxStep: 0.5,
                dyMax: 1.5, dyStep: 0.5);
            var omni = new OmniSolverOptions
            {
                Search = search,
                ValueModel = new BlockValueModel(rmv, bv, kt),
                SubdivMode = SubdivisionMode.Uniform,
                Mx = mx, My = my,
            };

            OmniSolveResult result;
            try { result = BlockCutOptOmniSolver.Solve(area, ply, omni); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            int n = result.PerZone.Count;
            var ids = new List<string>(n);
            var nr = new List<int>(n);
            var pr = new List<double>(n);
            var pi = new List<double>(n);
            var ppi = new List<double>(n);
            var tau = new List<double>(n);
            var ptau = new List<double>(n);
            var bcs = new List<double>(n);
            var pbcs = new List<double>(n);
            var fz = new List<int>(n);
            foreach (var zr in result.PerZone)
            {
                var rMax = zr.Front.BestRecovery();
                var pMax = zr.Front.BestRevenue();
                var tMin = zr.Front.BestKerfTime();
                var bMin = zr.Front.BestBcsdbBv();
                ids.Add(zr.Zone.Id);
                nr.Add(rMax.RecoveryCount);    pr.Add(rMax.PsiDeg);
                pi.Add(pMax.Revenue);          ppi.Add(pMax.PsiDeg);
                tau.Add(tMin.KerfTime);        ptau.Add(tMin.PsiDeg);
                bcs.Add(bMin.BcsdbBv);         pbcs.Add(bMin.PsiDeg);
                fz.Add(zr.Front.Count);
            }
            da.SetDataList(0, ids);
            da.SetDataList(1, nr);  da.SetDataList(2, pr);
            da.SetDataList(3, pi);  da.SetDataList(4, ppi);
            da.SetDataList(5, tau); da.SetDataList(6, ptau);
            da.SetDataList(7, bcs); da.SetDataList(8, pbcs);
            da.SetDataList(9, fz);
            da.SetData(10, (int)Math.Min(result.TotalEvaluations, int.MaxValue));
            da.SetData(11, result.Elapsed.TotalMilliseconds);
        }
    }

    // -------------------------------------------------------------------------
    // 2. Fisher-Robust BlockCutOpt -- Monte Carlo robustness over Fisher
    //    fracture-orientation scatter. Wraps FisherRobustSampler.Solve.
    // -------------------------------------------------------------------------
    [Algorithm("Fisher-distribution joint-scatter robustness sampling", "Azarafza et al. (2016) granite block-cut + Fisher-distribution joint scatter", WikiPath = "wiki/index/references.md")]
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Production single-best-fit solver; Fisher-robust extension reports stability of that optimum under fracture-orientation noise.")]
    [RelatedComponent("Frahan > Quarry > Joint Set", Reason = "Source of fracture-orientation distribution used here.")]
    [DesignApplication(
        "Run BlockCutOpt M times against M Fisher-perturbed DFN  realisations of the same joint sets; return p10 / p...",
        DesignFlow.TopDown)]
    public sealed class FrahanFisherRobustComponent : FrahanComponentBase
    {
        public FrahanFisherRobustComponent()
            : base(
                "Fisher-Robust BCO", "BCORobust",
                "Run BlockCutOpt M times against M Fisher-perturbed DFN " +
                "realisations of the same joint sets; return p10 / p50 / p90 " +
                "recovery percent and the median psi. The robust optimum " +
                "direction is the median psi, not the single deterministic " +
                "best (Azarafza 2016 / synthesis I8). Parallel joint-set " +
                "lists must all be the same length. " +
                "Implements Fisher-scatter robustness sampling (Azarafza 2016).",
                "Frahan", "Lab")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC11-1234-4F2D-A0B0-7E60CADA15B1");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddNumberParameter("Dip Directions (deg)", "Dd", "Per joint set, in [0,360).", GH_ParamAccess.list);
            p.AddNumberParameter("Dips (deg)", "Dp", "Per joint set, in [0,90].", GH_ParamAccess.list);
            p.AddNumberParameter("Mean Spacings (m)", "Sp", "Per joint set.", GH_ParamAccess.list);
            p.AddNumberParameter("Scatters (deg)", "Sc", "Fisher scatter per joint set.", GH_ParamAccess.list);
            p.AddNumberParameter("Block X", "Lx", "Block length (m).", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Block Y", "Ly", "Block width (m).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Block Z", "Lz", "Block height (m).", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf", "K", "Material-lost-by-quarrying (m).", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 3.0);
            p.AddIntegerParameter("MC Samples", "M", "Monte Carlo sample count.", GH_ParamAccess.item, 16);
            p.AddIntegerParameter("Base Seed", "S", "Reproducibility seed.", GH_ParamAccess.item, 12345);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Recovery p10 %", "R10", "10th-percentile recovery (robust score).", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery p50 %", "R50", "Median recovery.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery p90 %", "R90", "90th-percentile recovery.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery Mean %", "Rm", "Mean recovery.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery StdDev %", "Rs", "Sample standard deviation.", GH_ParamAccess.item);
            p.AddNumberParameter("Median Psi (deg)", "Psi", "Median psi across MC samples.", GH_ParamAccess.item);
            p.AddNumberParameter("Per-Sample Recovery %", "Rk", "All M recovery values.", GH_ParamAccess.list);
            p.AddNumberParameter("Per-Sample Psi (deg)", "Pk", "All M psi values.", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            var dd = new List<double>();
            var dp = new List<double>();
            var sp = new List<double>();
            var sc = new List<double>();
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            int mc = 16, seed = 12345;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            da.GetDataList(1, dd); da.GetDataList(2, dp);
            da.GetDataList(3, sp); da.GetDataList(4, sc);
            if (dd.Count == 0 || dd.Count != dp.Count || dd.Count != sp.Count || dd.Count != sc.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Joint-set lists must be non-empty and equal length."); return; }
            da.GetData(5, ref Lx); da.GetData(6, ref Ly); da.GetData(7, ref Lz);
            da.GetData(8, ref kerf); da.GetData(9, ref psiDeg);
            da.GetData(10, ref mc); da.GetData(11, ref seed);
            if (mc < 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "MC Samples must be >= 1."); return; }

            var jointSets = new List<JointSet>(dd.Count);
            try
            {
                for (int i = 0; i < dd.Count; i++)
                    jointSets.Add(new JointSet(dd[i], dp[i], sp[i], sc[i]));
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Joint-set #{jointSets.Count + 1}: {ex.Message}"); return; }

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            var opts = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: 1.5, dxStep: 0.5,
                dyMax: 1.5, dyStep: 0.5);

            FisherRobustResult result;
            try { result = FisherRobustSampler.Solve(area, jointSets, opts, mc, seed); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var rk = new List<double>(result.SampleCount);
            var pk = new List<double>(result.SampleCount);
            foreach (var r in result.PerSample)
            {
                rk.Add(r.RecoveryPercent);
                pk.Add(r.BestPsiDeg);
            }
            da.SetData(0, result.RecoveryP10);
            da.SetData(1, result.RecoveryP50);
            da.SetData(2, result.RecoveryP90);
            da.SetData(3, result.RecoveryMean);
            da.SetData(4, result.RecoveryStdDev);
            da.SetData(5, result.MedianPsiDeg);
            da.SetDataList(6, rk);
            da.SetDataList(7, pk);
        }
    }

    // -------------------------------------------------------------------------
    // 3. Density-Watershed Zones -- adaptive (mx, my) replacement.
    //    Emits one Box per watershed basin. Pair with BCOPareto / BCOOmni in
    //    DensityWatershed mode for the actual solve.
    // -------------------------------------------------------------------------
    [Algorithm("Density-watershed partition (BlockCutOpt I5)", "Frahan-original", Note = "BlockCutOpt synthesis I5; Core DensityWatershedPartition.cs verified-original")]
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Density-watershed sub-zones feed the production solver's I10 sub-division pass.")]
    [RelatedComponent("Frahan > Mesh > Bench From Mesh", Reason = "Bench mesh source for the tested area whose density is partitioned here.")]
    [DesignApplication(
        "Adaptive sub-division of the tested area by 2D fracture- density watershed (synthesis I5)",
        DesignFlow.TopDown)]
    public sealed class FrahanDensityWatershedZonesComponent : FrahanComponentBase
    {
        public FrahanDensityWatershedZonesComponent()
            : base(
                "Density-Watershed Zones", "BCOWatershed",
                "Adaptive sub-division of the tested area by 2D fracture-" +
                "density watershed (synthesis I5). Each zone boundary snaps " +
                "to high-density ridges so the unavoidable boundary penalty " +
                "lands on already-broken rock. Feed FracturePlanes from " +
                "Mesh2FxPl or any other planes-producing component. " +
                "Frahan-original method.",
                "Frahan", "Lab")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC12-1234-4F2D-A0B0-7E60CADA15B2");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => IconProvider.Load("Voronoi.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddGenericParameter("Fracture Planes", "F", "List<FracturePlane> (e.g. from Mesh2FxPl).", GH_ParamAccess.list);
            p.AddNumberParameter("Bandwidth (m)", "H", "Gaussian KDE bandwidth.", GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Raster Cell (m)", "Rc", "Density-raster cell size; 0 = bandwidth/4.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBoxParameter("Zone Boxes", "B", "One axis-aligned Box per watershed basin.", GH_ParamAccess.list);
            p.AddTextParameter("Zone Ids", "Z", "Synthetic id per zone.", GH_ParamAccess.list);
            p.AddIntegerParameter("Zone Count", "N", "Total number of zones.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            var raw = new List<IGH_Goo>();
            double h = 5.0, rc = 0.0;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            if (!da.GetDataList(1, raw) || raw.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture planes required."); return; }
            da.GetData(2, ref h); da.GetData(3, ref rc);
            if (!(h > 0)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bandwidth must be > 0."); return; }

            var planes = new List<FracturePlane>(raw.Count);
            foreach (var g in raw)
            {
                if (g is GH_ObjectWrapper w && w.Value is FracturePlane fp) planes.Add(fp);
                else if (g != null && g.ScriptVariable() is FracturePlane fp2) planes.Add(fp2);
            }
            if (planes.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No FracturePlane values found in input."); return; }

            var area = GhBlockCutOptInterop.BoxToBbox(box);

            IReadOnlyList<SubZone> zones;
            try { zones = DensityWatershedPartition.Partition(area, planes, h, rc); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var boxes = new List<Box>(zones.Count);
            var ids = new List<string>(zones.Count);
            foreach (var z in zones)
            {
                var aa = z.Aabb;
                boxes.Add(new Box(Plane.WorldXY,
                    new Interval(aa.MinX, aa.MaxX),
                    new Interval(aa.MinY, aa.MaxY),
                    new Interval(aa.MinZ, aa.MaxZ)));
                ids.Add(z.Id);
            }
            da.SetDataList(0, boxes);
            da.SetDataList(1, ids);
            da.SetData(2, zones.Count);
        }
    }

    // -------------------------------------------------------------------------
    // 4. VTU Export -- ParaView visualisation of the optimal cutting grid.
    //    Writes a .vtu UnstructuredGrid with two cell sets (non-intersected
    //    + intersected), matching the BlockCutOpt 2020 paper Figure 3/6
    //    convention.
    // -------------------------------------------------------------------------
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Source of the optimised cutting grid being exported to VTU for external visualisation.")]
    [DesignApplication(
        "Run BlockCutOpt then dump the optimal cutting grid to a  ParaView .vtu file",
        DesignFlow.TopDown)]
    public sealed class FrahanVtuExportComponent : FrahanComponentBase
    {
        public FrahanVtuExportComponent()
            : base(
                "VTU Export", "VtuExport",
                "Run BlockCutOpt then dump the optimal cutting grid to a " +
                "ParaView .vtu file. Two cell sets: cell_status=1 (non-" +
                "intersected, ready-to-quarry), cell_status=0 (intersected, " +
                "discarded). Matches BlockCutOpt 2020 Figures 3 and 6.",
                "Frahan", "Lab")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC13-1234-4F2D-A0B0-7E60CADA15B3");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => IconProvider.Load("GcodeExport.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddMeshParameter("Fractures", "F", "Fracture mesh.", GH_ParamAccess.item);
            p.AddTextParameter("VTU Path", "Path", "Output .vtu file path.", GH_ParamAccess.item);
            p.AddNumberParameter("Block X", "Lx", "Block length (m).", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Block Y", "Ly", "Block width (m).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Block Z", "Lz", "Block height (m).", GH_ParamAccess.item, 0.8);
            p.AddNumberParameter("Kerf", "K", "Material-lost-by-quarrying (m).", GH_ParamAccess.item, BlockCutOptTolerances.KerfDefaultMetres);
            p.AddNumberParameter("Psi Step (deg)", "Pdeg", "Angular search step.", GH_ParamAccess.item, 3.0);
            p.AddBooleanParameter("Write", "W", "Trigger; set true to write the file.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("Non-Intersected", "NI", "Cell count tagged status=1.", GH_ParamAccess.item);
            p.AddIntegerParameter("Intersected", "I", "Cell count tagged status=0.", GH_ParamAccess.item);
            p.AddNumberParameter("Recovery %", "R", "Recovery percent at the winning (psi, dx, dy).", GH_ParamAccess.item);
            p.AddTextParameter("Written Path", "Out", "Path written, or empty when Write=false.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            Mesh fxMesh = null;
            string path = null;
            double Lx = 3.0, Ly = 2.0, Lz = 0.8;
            double kerf = BlockCutOptTolerances.KerfDefaultMetres;
            double psiDeg = 3.0;
            bool write = false;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            if (!da.GetData(1, ref fxMesh) || fxMesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fracture mesh required."); return; }
            if (!da.GetData(2, ref path) || string.IsNullOrWhiteSpace(path))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path required."); return; }
            da.GetData(3, ref Lx); da.GetData(4, ref Ly); da.GetData(5, ref Lz);
            da.GetData(6, ref kerf); da.GetData(7, ref psiDeg);
            da.GetData(8, ref write);

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            var ply = GhBlockCutOptInterop.RhinoMeshToPly(fxMesh);
            var opts = new BlockCutOptOptions(
                Lx, Ly, Lz, kerf,
                psiStartRad: 0.0, psiStopRad: Math.PI,
                psiStepRad: BlockCutOptTolerances.DegToRad(psiDeg),
                dxMax: 1.5, dxStep: 0.5,
                dyMax: 1.5, dyStep: 0.5);

            BlockCutOptResult result;
            try { result = BlockCutOptSolver.Solve(area, ply, opts); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var grid = CuttingGrid.GenerateTilted(
                area, Lx, Ly, Lz, kerf,
                result.BestPsiRad, result.BestThetaRad, result.BestPhiRad,
                result.BestDx, result.BestDy);
            var bvh = TriangleAabbBvh.Build(ply);

            int niCount = 0, iCount = 0;
            if (write)
            {
                try
                {
                    var (ni, ii) = VtuWriter.WriteFromGridAndBvh(path, grid, bvh);
                    niCount = ni; iCount = ii;
                }
                catch (Exception ex)
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Write failed: {ex.Message}"); return; }
            }
            else
            {
                for (int i = 0; i < grid.Count; i++)
                {
                    var obb = grid[i];
                    if (bvh.AnyTriangleIntersects(in obb)) iCount++;
                    else niCount++;
                }
            }

            da.SetData(0, niCount);
            da.SetData(1, iCount);
            da.SetData(2, result.RecoveryPercent);
            da.SetData(3, write ? path : string.Empty);
        }
    }

    // -------------------------------------------------------------------------
    // 5. Mixed-Size Block Pack -- bonus, answers the question
    //    "can we pack multiple sizes instead of one size for the entire
    //    quarry?". Yes: DLBF (Chehrazad 2025) greedy 2D mixed-size packer
    //    on the tested area rectangle. Forbidden rectangles can encode
    //    fracture-intersected cells from a prior BCOSolve.
    // -------------------------------------------------------------------------
    [Algorithm("Deepest-left-bottom-fill (DLBF) mixed-size packing", "Chehrazad, R., Roose, D., Wauters, T. (2025). A fast and scalable deepest-left-bottom-fill algorithm. Int. J. Production Research 63:6606-6629", Doi = "10.1080/00207543.2025.2478434", WikiPath = "wiki/index/references.md")]
    [RelatedComponent("Frahan > Masonry > Ashlar Pack", Reason = "Production 3D packer; this mixed-size variant is the heterogeneous-block research path.")]
    [RelatedComponent("Frahan > Masonry > Best Fit Pack", Reason = "Production rubble packer for varied-height inputs.")]
    [RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Upstream source of the block inventory this packer consumes.")]
    [DesignApplication(
        "Pack a catalogue of mixed-size blocks (multiple Width x  Depth pairs each with its own revenue) into the te...",
        DesignFlow.TopDown)]
    public sealed class FrahanMixedSizeBlockPackComponent : FrahanComponentBase
    {
        public FrahanMixedSizeBlockPackComponent()
            : base(
                "Mixed-Size Block Pack", "BCOMixedPack",
                "Pack a catalogue of mixed-size blocks (multiple Width x " +
                "Depth pairs each with its own revenue) into the tested " +
                "area using the DLBF greedy heuristic (Chehrazad 2025, " +
                "synthesis I7). Forbidden boxes mark fracture-intersected " +
                "regions that must stay empty. Returns one Box per placed " +
                "piece. " +
                "Implements DLBF (Chehrazad 2025).",
                "Frahan", "Lab")
        { }

        public override Guid ComponentGuid =>
            new Guid("F2D0BC17-1234-4F2D-A0B0-7E60CADA15B7");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("BinPack.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBoxParameter("Tested Area", "A", "Bench bounding box (m).", GH_ParamAccess.item);
            p.AddTextParameter("Piece Ids", "Id", "One id per catalogue entry.", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Widths (m)", "W", "Width per entry (X).", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Depths (m)", "D", "Depth per entry (Y).", GH_ParamAccess.list);
            p.AddNumberParameter("Piece Revenues", "Rev", "RMV per entry.", GH_ParamAccess.list);
            p.AddNumberParameter("Block Height (m)", "Lz", "Common Z extrusion height for output Boxes.", GH_ParamAccess.item, 0.8);
            p.AddBoxParameter("Forbidden Boxes", "X", "Optional forbidden regions (e.g. fracture-intersected cells).", GH_ParamAccess.list);
            p.AddNumberParameter("Grid Cell (m)", "Gc", "Discretisation cell; 0 = min(W,D)/4.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBoxParameter("Placed Boxes", "B", "One Box per placed piece.", GH_ParamAccess.list);
            p.AddTextParameter("Placed Ids", "I", "Id of each placed piece (multiplicity preserved).", GH_ParamAccess.list);
            p.AddNumberParameter("Total Revenue", "Pi", "Sum of placed-piece revenues.", GH_ParamAccess.item);
            p.AddNumberParameter("Covered Area (m^2)", "Ar", "Sum of placed-piece footprint areas.", GH_ParamAccess.item);
            p.AddIntegerParameter("Placed Count", "N", "Number of placements.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var box = Box.Empty;
            var ids = new List<string>();
            var ws = new List<double>();
            var ds = new List<double>();
            var revs = new List<double>();
            double Lz = 0.8;
            var forb = new List<Box>();
            double gc = 0.0;
            if (!da.GetData(0, ref box) || !box.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid tested-area box."); return; }
            da.GetDataList(1, ids); da.GetDataList(2, ws);
            da.GetDataList(3, ds); da.GetDataList(4, revs);
            if (ids.Count == 0 || ids.Count != ws.Count || ids.Count != ds.Count || ids.Count != revs.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Id / W / D / Rev lists must be non-empty and equal length."); return; }
            da.GetData(5, ref Lz);
            da.GetDataList(6, forb);
            da.GetData(7, ref gc);

            var catalog = new List<PieceSize>(ids.Count);
            try
            {
                for (int i = 0; i < ids.Count; i++)
                    catalog.Add(new PieceSize(ids[i], ws[i], ds[i], revs[i]));
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Piece #{catalog.Count + 1}: {ex.Message}"); return; }

            var area = GhBlockCutOptInterop.BoxToBbox(box);
            List<BoundingBox3> forbidden = null;
            if (forb.Count > 0)
            {
                forbidden = new List<BoundingBox3>(forb.Count);
                foreach (var b in forb)
                {
                    if (!b.IsValid) continue;
                    forbidden.Add(GhBlockCutOptInterop.BoxToBbox(b));
                }
            }

            DlbfPackResult result;
            try { result = DlbfMixedSizePacker.Pack(area, catalog, forbidden, gc); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var outBoxes = new List<Box>(result.Placed.Count);
            var outIds = new List<string>(result.Placed.Count);
            foreach (var p in result.Placed)
            {
                outBoxes.Add(new Box(Plane.WorldXY,
                    new Interval(p.XMin, p.XMax),
                    new Interval(p.YMin, p.YMax),
                    new Interval(area.MinZ, area.MinZ + Lz)));
                outIds.Add(p.Size.Id);
            }
            da.SetDataList(0, outBoxes);
            da.SetDataList(1, outIds);
            da.SetData(2, result.TotalRevenue);
            da.SetData(3, result.CoveredAreaMetres2);
            da.SetData(4, result.Placed.Count);
        }
    }
}
