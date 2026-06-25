#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.GH.Quarry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Quarry.CutOpt;
using Frahan.Masonry.Quarry.GeoCut;
using Frahan.Masonry.Quarry.GeoPack;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // GeoCut (spec 09) + GeoPack v0 (spec 08) Grasshopper components.
    //
    // GeoCut wrappers:
    //   FrahanSlabYieldOptimizerComponent  (per-block slab-plan picker)
    //   FrahanBilletCutterComponent        (sub-divide Slabs into billets)
    //
    // GeoPack wrappers (v0, manual crack input):
    //   FrahanCrackGraphBuilderComponent   (FracturePlanes -> CrackGraph)
    //   FrahanBlockGraphBuilderComponent   (Slab + CrackGraph -> BlockGraph)
    //   FrahanBlockCandidateGeneratorComponent (BlockGraph -> QuarryInventory)
    //
    // Added 2026-05-14. New GUIDs F7A14001..F7A15003.
    // =========================================================================

    // ---------- GeoCut ----------

    [Algorithm("Per-block slab-plan yield maximisation", "Frahan-original spec 09 section 2 conflict-penalised yield score", Note = "Score = slab_count * slab_volume / block_volume - conflictPenalty * crackConflictCount", WikiPath = "wiki/specs/09_frahan_geocut_spec.md")]
    [Algorithm("Dimension-stone optimisation review context", "Marvie Reed and Bondua 2025 PRISMA review of dimension-stone optimisation", Note = "Context anchor for the yield/conflict score family")]
        [DesignApplication(
        "Pick the best SlabPlan (axis + thickness) for one block",
        DesignFlow.TopDown,
        Precedent = "Frahan-original SlabCutOpt (spec 09 SS2); Marvie Reed Bondua 2025 review context (DOI 10.2478/minrv-2025-0015)",
        Tolerance = "slab count within 95 % of fracture-free theoretical max",
        CardSet = "wiki/research/hitl_cards/td_slabcut/")]
    public sealed class FrahanSlabYieldOptimizerComponent : FrahanComponentBase
    {
        public FrahanSlabYieldOptimizerComponent()
            : base(
                "Frahan Slab Yield Optimizer", "SlabYield",
                "Pick the best SlabPlan (axis + thickness) for one block. " +
                "Enumerates three axis-aligned candidates at the given " +
                "thickness; score = yield - conflictPenalty * crackConflicts.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A14001-0001-4F2D-A0B0-7E60CADA17D1");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("YieldEstimator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Block", "B", "Convex block mesh.", GH_ParamAccess.item);
            p.AddGenericParameter("Fracture Planes", "F",
                "Optional List<FracturePlane>. Wire from Frahan Mesh → Fracture Planes.",
                GH_ParamAccess.list);
            p[1].Optional = true;
            p.AddNumberParameter("Thickness (m)", "T", "Target slab thickness.", GH_ParamAccess.item, 0.05);
            p.AddNumberParameter("Kerf (m)", "K", "Saw kerf.", GH_ParamAccess.item, 0.005);
            p.AddNumberParameter("Conflict Penalty", "Cp", "Score penalty per aligned fracture inside the block.", GH_ParamAccess.item, 0.05);
            p.AddNumberParameter("Alignment Tol (deg)", "At", "Normal-axis alignment tolerance for conflict detection.", GH_ParamAccess.item, 6.0);
            // 2026-05-30: optional QuarryBlock input (HITL #2). Indexed at the
            // END so existing build_gh wiring (indices 0-5) is untouched.
            // When wired, Block.UsableVolume mesh is used instead of the Block
            // (Mesh) input; takes precedence if both are wired. GUID unchanged.
            p.AddParameter(new Param_QuarryBlock(), "Quarry Block", "QB",
                "Optional QuarryBlock from Scan to Block Inventory. When wired, " +
                "Block.UsableVolume is the block mesh (takes precedence over " +
                "the Block (Mesh) input if both are wired).",
                GH_ParamAccess.item);
            p[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Best Plan", "P", "SlabPlan with the highest score.", GH_ParamAccess.item);
            p.AddIntegerParameter("Axis", "A", "0=X, 1=Y, 2=Z.", GH_ParamAccess.item);
            p.AddIntegerParameter("Slab Count", "N", "Slabs the block produces under this plan.", GH_ParamAccess.item);
            p.AddNumberParameter("Yield Fraction", "Y", "slab_total_volume / block_volume.", GH_ParamAccess.item);
            p.AddIntegerParameter("Conflicts", "C", "Crack conflicts counted.", GH_ParamAccess.item);
            p.AddNumberParameter("Score", "S", "Yield − penalty × conflicts.", GH_ParamAccess.item);
            p.AddGenericParameter("Cut Planes", "Cp", "FracturePlanes that materialise the winning plan (feed Slab Cut By Fractures).", GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            var fxWrappers = new List<GH_ObjectWrapper>();
            double thickness = 0.05, kerf = 0.005, penalty = 0.05, alignDeg = 6.0;

            bool gotMesh = da.GetData(0, ref mesh) && mesh != null;
            da.GetDataList(1, fxWrappers);
            da.GetData(2, ref thickness);
            da.GetData(3, ref kerf);
            da.GetData(4, ref penalty);
            da.GetData(5, ref alignDeg);

            // 2026-05-30: optional QuarryBlock input. When wired,
            // Block.UsableVolume replaces the Block (Mesh) input. Takes
            // precedence per HITL #2.
            QuarryBlockGoo blockGoo = null;
            if (da.GetData(6, ref blockGoo) && blockGoo != null && blockGoo.Value != null &&
                blockGoo.Value.UsableVolume != null && blockGoo.Value.UsableVolume.IsValid)
            {
                if (gotMesh)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Block input takes precedence over Mesh input.");
                }
                mesh = blockGoo.Value.UsableVolume;
                gotMesh = true;
            }
            if (!gotMesh || mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block mesh required."); return; }

            var slab = Frahan.GH.Masonry.GhInterop.SlabFromMesh(mesh);
            if (slab == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block mesh is invalid (need >= 4 vertices and >= 4 faces)."); return; }

            var fractures = new List<FracturePlane>(fxWrappers.Count);
            for (int i = 0; i < fxWrappers.Count; i++)
            {
                if (fxWrappers[i].Value is FracturePlane fp) fractures.Add(fp);
            }

            var opts = new SlabYieldOptimizerOptions(
                new[]
                {
                    new SlabPlan(SlabAxis.X, thickness, kerf),
                    new SlabPlan(SlabAxis.Y, thickness, kerf),
                    new SlabPlan(SlabAxis.Z, thickness, kerf),
                },
                penalty,
                alignmentToleranceRad: alignDeg * Math.PI / 180.0);

            SlabYieldResult best;
            try { best = SlabYieldOptimizer.PickBest(slab, fractures, opts); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var cutPlanes = SlabYieldOptimizer.ToFracturePlanes(best.Plan, slab);
            var cutPlaneWrappers = new List<GH_ObjectWrapper>(cutPlanes.Count);
            for (int i = 0; i < cutPlanes.Count; i++) cutPlaneWrappers.Add(new GH_ObjectWrapper(cutPlanes[i]));

            da.SetData(0, new GH_ObjectWrapper(best.Plan));
            da.SetData(1, (int)best.Plan.Axis);
            da.SetData(2, best.SlabCount);
            da.SetData(3, best.YieldFraction);
            da.SetData(4, best.ConflictCount);
            da.SetData(5, best.Score);
            da.SetDataList(6, cutPlaneWrappers);
        }
    }

    [Algorithm("Axis-parallel kerf-aware slab sub-division", "Frahan-original", Note = "Spec-09 billet sub-divider; no prior art.")]
        [DesignApplication(
        "Sub-divide slabs into billets along an axis at a target  billet width",
        DesignFlow.TopDown,
        Precedent = "Frahan-original slab-to-billet sub-divider (spec 09)")]
    public sealed class FrahanBilletCutterComponent : FrahanComponentBase
    {
        public FrahanBilletCutterComponent()
            : base(
                "Frahan Billet Cutter", "Billets",
                "Sub-divide slabs into billets along an axis at a target " +
                "billet width. Kerf-aware. Frahan-original method.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A14002-0001-4F2D-A0B0-7E60CADA17D2");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Slabs", "S", "Slab inventory (one mesh per slab).", GH_ParamAccess.list);
            p.AddIntegerParameter("Axis", "A", "Billet axis: 0=X, 1=Y, 2=Z.", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Billet Width (m)", "W", "Target billet width.", GH_ParamAccess.item, 0.10);
            p.AddNumberParameter("Kerf (m)", "K", "Saw kerf.", GH_ParamAccess.item, 0.003);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Billets", "B", "Billet meshes (one per cut piece).", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Total billet count.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            int axisInt = 0;
            double width = 0.10, kerf = 0.003;

            if (!da.GetDataList(0, meshes) || meshes.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Provide at least one slab."); return; }
            da.GetData(1, ref axisInt);
            da.GetData(2, ref width);
            da.GetData(3, ref kerf);
            if (axisInt < 0 || axisInt > 2)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Axis must be 0..2."); return; }

            var slabs = new List<Slab>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = Frahan.GH.Masonry.GhInterop.SlabFromMesh(meshes[i]);
                if (s == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Slabs[{i}] is invalid."); return; }
                slabs.Add(s);
            }

            IReadOnlyList<Slab> billets;
            try
            {
                var plan = new BilletPlan((SlabAxis)axisInt, width, kerf);
                billets = BilletCutter.CutAll(slabs, plan);
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var outMeshes = new List<Mesh>(billets.Count);
            for (int i = 0; i < billets.Count; i++)
                outMeshes.Add(FrahanBenchBlockToSlabsComponent.SlabToRhinoMesh(billets[i]));
            da.SetDataList(0, outMeshes);
            da.SetData(1, billets.Count);
        }
    }

    // ---------- GeoPack v0 ----------

    [Algorithm("Crack-graph DTO builder", "Frahan-original", Note = "Spec-08 v0 manual crack-input DTO wrapper; no published algorithm.")]
        [DesignApplication(
        "Wrap a user-supplied list of FracturePlanes (and optional  confidences) as a CrackGraph for spec-08 downstr...",
        DesignFlow.TopDown,
        Precedent = "Frahan-original fracture-plane crack-graph builder (spec 09)")]
    public sealed class FrahanCrackGraphBuilderComponent : FrahanComponentBase
    {
        public FrahanCrackGraphBuilderComponent()
            : base(
                "Frahan Crack Graph (manual)", "CrkGraph",
                "Wrap a user-supplied list of FracturePlanes (and optional " +
                "confidences) as a CrackGraph for spec-08 downstream consumers. Frahan-original method.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A15001-0001-4F2D-A0B0-7E60CADA17E1");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Fracture Planes", "F", "List<FracturePlane>.", GH_ParamAccess.list);
            p.AddNumberParameter("Confidences", "C", "Per-plane confidence 0..1 (optional).", GH_ParamAccess.list);
            p[1].Optional = true;
            p.AddTextParameter("Ids", "I", "Per-plane ids (optional).", GH_ParamAccess.list);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Crack Graph", "G", "CrackGraph object.", GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N", "Number of cracks.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var fxWrappers = new List<GH_ObjectWrapper>();
            var confs = new List<double>();
            var ids = new List<string>();
            if (!da.GetDataList(0, fxWrappers) || fxWrappers.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "FracturePlanes required."); return; }
            da.GetDataList(1, confs);
            da.GetDataList(2, ids);

            var planes = new List<FracturePlane>(fxWrappers.Count);
            for (int i = 0; i < fxWrappers.Count; i++)
            {
                if (!(fxWrappers[i].Value is FracturePlane fp))
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"F[{i}] is not a FracturePlane."); return; }
                planes.Add(fp);
            }

            CrackGraph graph;
            try
            {
                graph = CrackGraphBuilder.FromPlanes(
                    planes,
                    confs.Count > 0 ? confs : null,
                    ids.Count > 0 ? ids : null);
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            da.SetData(0, new GH_ObjectWrapper(graph));
            da.SetData(1, graph.Count);
        }
    }

    [Algorithm("CrackGraph to BlockGraph partition", "Frahan-original", Note = "Spec-08/09 CrackGraph reduction with sub-volume cell dropping; no faithful published algorithm.")]
        [DesignApplication(
        "Partition a bench (Box or Mesh) into BlockCells using a  CrackGraph",
        DesignFlow.TopDown,
        Precedent = "Frahan-original CrackGraph -> BlockGraph reduction (spec 09)")]
    public sealed class FrahanBlockGraphBuilderComponent : FrahanComponentBase
    {
        public FrahanBlockGraphBuilderComponent()
            : base(
                "Frahan Block Graph", "BlkGraph",
                "Partition a bench (Box or Mesh) into BlockCells using a " +
                "CrackGraph. Each cell is a convex Slab; small cells are " +
                "dropped under Min Cell Volume. Frahan-original method.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A15002-0001-4F2D-A0B0-7E60CADA17E2");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("Voronoi.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Bench Mesh", "B", "Bench geometry (convex mesh).", GH_ParamAccess.item);
            p.AddGenericParameter("Crack Graph", "G", "CrackGraph from Frahan Crack Graph.", GH_ParamAccess.item);
            p.AddNumberParameter("Min Cell Volume (m^3)", "Mv", "Cells below this volume are dropped.", GH_ParamAccess.item, 1e-6);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Block Graph", "Bg", "BlockGraph object.", GH_ParamAccess.item);
            p.AddMeshParameter("Cells", "C", "One mesh per BlockCell.", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of cells.", GH_ParamAccess.item);
            p.AddNumberParameter("Total Volume (m^3)", "V", "Sum of cell volumes.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh benchMesh = null;
            var graphW = new GH_ObjectWrapper();
            double minVol = 1e-6;
            if (!da.GetData(0, ref benchMesh) || benchMesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bench mesh required."); return; }
            if (!da.GetData(1, ref graphW) || !(graphW.Value is CrackGraph graph))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Crack Graph required."); return; }
            da.GetData(2, ref minVol);

            var bench = Frahan.GH.Masonry.GhInterop.SlabFromMesh(benchMesh);
            if (bench == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bench mesh invalid."); return; }

            BlockGraph bg;
            try { bg = BlockGraphBuilder.Partition(bench, graph, minVol); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var meshes = new List<Mesh>(bg.Count);
            for (int i = 0; i < bg.Count; i++)
                meshes.Add(FrahanBenchBlockToSlabsComponent.SlabToRhinoMesh(bg.Cells[i].Geometry));

            da.SetData(0, new GH_ObjectWrapper(bg));
            da.SetDataList(1, meshes);
            da.SetData(2, bg.Count);
            da.SetData(3, bg.TotalVolume);
        }
    }

    [Algorithm("Per-cell AABB block-candidate generator", "Frahan-original", Note = "Spec-09 SS2 candidate generator; no prior art.")]
        [DesignApplication(
        "Emit one BlockCandidate per BlockCell using the cell's AABB  as the BenchBlock footprint",
        DesignFlow.TopDown,
        Precedent = "Frahan-original block-candidate generator (spec 09 SS2)")]
    public sealed class FrahanBlockCandidateGeneratorComponent : FrahanComponentBase
    {
        public FrahanBlockCandidateGeneratorComponent()
            : base(
                "Frahan Block Candidate Generator", "BCand",
                "Emit one BlockCandidate per BlockCell using the cell's AABB " +
                "as the BenchBlock footprint. Also returns a QuarryInventory " +
                "ready for the Layer 7 Quarry Yield Estimator. Frahan-original method.",
                "Frahan", "Block")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A15003-0001-4F2D-A0B0-7E60CADA17E3");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("BlockCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Block Graph", "Bg", "BlockGraph from Frahan Block Graph.", GH_ParamAccess.item);
            p[0].Optional = true;
            p.AddTextParameter("Bench Id", "B", "Bench identifier for the QuarryInventory.", GH_ParamAccess.item, "bench-1");
            p.AddNumberParameter("Geology Grade", "G", "Per-cell geology grade 0..1.", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Uncertainty Buffer (m)", "U", "Buffer applied to each candidate.", GH_ParamAccess.item, 0.0);
            // 2026-05-30: optional QuarryBlock input (HITL #2). Indexed at the
            // END so existing build_gh wiring (indices 0-3) is untouched.
            // When wired and Block Graph is NOT wired, a single-cell BlockGraph
            // is synthesised from Block.UsableVolume. When both are wired the
            // existing Block Graph wins and a remark is emitted. GUID unchanged.
            p.AddParameter(new Param_QuarryBlock(), "Quarry Block", "QB",
                "Optional QuarryBlock from Scan to Block Inventory. When wired " +
                "without a Block Graph input, a single-cell BlockGraph is " +
                "synthesised from Block.UsableVolume. Block Graph takes " +
                "precedence if both are wired.",
                GH_ParamAccess.item);
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "QuarryInventory for the Layer 7 pipeline.", GH_ParamAccess.item);
            p.AddGenericParameter("Candidates", "C", "List<BlockCandidate>.", GH_ParamAccess.list);
            p.AddBoxParameter("Candidate Boxes", "Bx", "Footprint of each candidate as a Rhino Box.", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of candidates.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var bgW = new GH_ObjectWrapper();
            string benchId = "bench-1";
            double grade = 1.0, buf = 0.0;
            bool gotBlockGraph = da.GetData(0, ref bgW) && bgW != null && bgW.Value is BlockGraph;
            BlockGraph bg = gotBlockGraph ? (BlockGraph)bgW.Value : null;
            da.GetData(1, ref benchId);
            da.GetData(2, ref grade);
            da.GetData(3, ref buf);
            if (grade < 0) grade = 0; if (grade > 1) grade = 1;
            if (buf < 0) buf = 0;

            // 2026-05-30: optional QuarryBlock input. When wired without
            // Block Graph, synthesise a single-cell BlockGraph from
            // Block.UsableVolume. Block Graph wins per HITL #2 (existing wire
            // path stays authoritative).
            QuarryBlockGoo blockGoo = null;
            if (da.GetData(4, ref blockGoo) && blockGoo != null && blockGoo.Value != null &&
                blockGoo.Value.UsableVolume != null && blockGoo.Value.UsableVolume.IsValid)
            {
                if (gotBlockGraph)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Block input takes precedence over Mesh input.");
                    // Existing semantics: keep the wired Block Graph, ignore the
                    // QuarryBlock for graph construction. Block Graph is the
                    // authoritative input when wired.
                }
                else
                {
                    var slab = Frahan.GH.Masonry.GhInterop.SlabFromMesh(blockGoo.Value.UsableVolume);
                    if (slab == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "QuarryBlock.UsableVolume could not be converted to a Slab.");
                        return;
                    }
                    try
                    {
                        bg = BlockGraphBuilder.Partition(slab, new CrackGraph(new List<CrackSurface>()), 1e-9);
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"Failed to synthesise single-cell BlockGraph from QuarryBlock: {ex.Message}");
                        return;
                    }
                    gotBlockGraph = true;
                }
            }

            if (!gotBlockGraph || bg == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block Graph required."); return; }

            IReadOnlyList<BlockCandidate> cands;
            QuarryInventory inv;
            try
            {
                cands = BlockCandidateGenerator.AabbPerCell(bg, grade, buf);
                inv = BlockCandidateGenerator.ToInventory(benchId, cands);
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var wraps = new List<GH_ObjectWrapper>(cands.Count);
            var boxes = new List<Box>(cands.Count);
            for (int i = 0; i < cands.Count; i++)
            {
                wraps.Add(new GH_ObjectWrapper(cands[i]));
                var bb = cands[i].OrientedBox.Footprint;
                var box = new Box(Plane.WorldXY,
                    new Interval(bb.MinX, bb.MaxX),
                    new Interval(bb.MinY, bb.MaxY),
                    new Interval(bb.MinZ, bb.MaxZ));
                boxes.Add(box);
            }

            da.SetData(0, new GH_ObjectWrapper(inv));
            da.SetDataList(1, wraps);
            da.SetDataList(2, boxes);
            da.SetData(3, cands.Count);
        }
    }
}
