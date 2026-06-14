#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.GeoPack;
using Frahan.Masonry.Quarry.Monuments;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry
{
    // =========================================================================
    // Monument packing components, 2026-05-15.
    //
    // Track: pack a heterogeneous monument inventory inside a fractured bench
    // such that no monument crosses a fracture. Uses 24-rotation SO(3)
    // sampling and greedy AABB packing per BlockCell.
    //
    // Components:
    //   FrahanMonumentInventoryComponent   (Mesh[] → MonumentInventory)
    //   FrahanPackMonumentsInCellComponent (BlockCell + Inventory → Placements)
    //   FrahanBenchMonumentPackComponent   (BlockGraph + Inventory → BenchMonumentPlan)
    // =========================================================================

    [DesignApplication(
        "Bundle Rhino meshes as a MonumentInventory consumable by  the Frahan Bench Monument Pack components",
        DesignFlow.TopDown)]
    public sealed class FrahanMonumentInventoryComponent : FrahanComponentBase
    {
        public FrahanMonumentInventoryComponent()
            : base(
                "Frahan Monument Inventory", "MonInv",
                "Bundle Rhino meshes as a MonumentInventory consumable by " +
                "the Frahan Bench Monument Pack components. Each mesh becomes " +
                "one Monument; ids are optional and auto-generated when blank.",
                "Frahan", "Fabricate")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A16001-0001-4F2D-A0B0-7E60CADA17F1");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("StockpileManager.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Meshes", "M", "One mesh per monument.", GH_ParamAccess.list);
            p.AddTextParameter("Ids", "I", "Optional ids; auto-generated when blank.", GH_ParamAccess.list);
            p[1].Optional = true;
            p.AddNumberParameter("Density (kg/m^3)", "D", "Material density.", GH_ParamAccess.item, 2700.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Inventory", "Inv", "MonumentInventory.", GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N", "Number of monuments.", GH_ParamAccess.item);
            p.AddNumberParameter("Total AABB Volume (m^3)", "V", "Sum of monument AABB volumes.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            var ids = new List<string>();
            double density = 2700.0;
            if (!da.GetDataList(0, meshes) || meshes.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Provide at least one monument mesh."); return; }
            da.GetDataList(1, ids);
            da.GetData(2, ref density);

            var monuments = new List<Monument>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var m = meshes[i];
                if (m == null || m.Faces.Count == 0 || m.Vertices.Count == 0)
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"meshes[{i}] is empty."); return; }
                string id = (i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]))
                    ? ids[i]
                    : $"MON-{i:D4}";
                var ply = GhBlockCutOptInterop.RhinoMeshToPly(m);
                try { monuments.Add(new Monument(id, ply, density)); }
                catch (Exception ex)
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"meshes[{i}]: {ex.Message}"); return; }
            }

            MonumentInventory inv;
            try { inv = new MonumentInventory(monuments); }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            da.SetData(0, new GH_ObjectWrapper(inv));
            da.SetData(1, inv.Count);
            da.SetData(2, inv.TotalAabbVolume);
        }
    }

    [DesignApplication(
        "Pack a MonumentInventory inside a fractured bench (BlockGraph)  using 24-rotation SO(3) sampling and greedy...",
        DesignFlow.TopDown)]
    [Algorithm("24-orientation SO(3) sampling + greedy AABB packing", "Frahan-original",
        Note = "24-rotation cube-symmetry sampling is a known recipe; the bench/fracture-cell packing logic is Frahan-original")]
    public sealed class FrahanBenchMonumentPackComponent : FrahanComponentBase
    {
        public FrahanBenchMonumentPackComponent()
            : base(
                "Frahan Bench Monument Pack", "MonPack",
                "Pack a MonumentInventory inside a fractured bench (BlockGraph) " +
                "using 24-rotation SO(3) sampling and greedy AABB placement " +
                "per cell. Monuments stay inside one cell — no fracture crossings. Frahan-original method.",
                "Frahan", "Fabricate")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A16002-0001-4F2D-A0B0-7E60CADA17F2");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("BinPack.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Block Graph", "Bg", "BlockGraph from Frahan Block Graph.", GH_ParamAccess.item);
            p.AddGenericParameter("Inventory", "Inv", "MonumentInventory.", GH_ParamAccess.item);
            p.AddNumberParameter("Grid Stride (m)", "Gs", "Candidate-origin sweep step.", GH_ParamAccess.item, 0.05);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Plan", "P", "BenchMonumentPlan.", GH_ParamAccess.item);
            p.AddBoxParameter("Placed Boxes", "B", "Axis-aligned box per placement (visual).", GH_ParamAccess.list);
            p.AddTextParameter("Placed Ids", "I", "Monument ids in placement order.", GH_ParamAccess.list);
            p.AddIntegerParameter("Orientation Index", "R", "Rotation index (0..23) per placement.", GH_ParamAccess.list);
            p.AddTextParameter("Cell Ids", "C", "Parent cell id per placement.", GH_ParamAccess.list);
            p.AddTextParameter("Unplaced Ids", "U", "Monuments that did not fit.", GH_ParamAccess.list);
            p.AddNumberParameter("Fill Ratio", "Fr", "TotalPlacedVolume / BenchAabbVolume.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var bgW = new GH_ObjectWrapper();
            var invW = new GH_ObjectWrapper();
            double stride = 0.05;
            if (!da.GetData(0, ref bgW) || !(bgW.Value is BlockGraph bg))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block Graph required."); return; }
            if (!da.GetData(1, ref invW) || !(invW.Value is MonumentInventory inv))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "MonumentInventory required."); return; }
            da.GetData(2, ref stride);

            BenchMonumentPlan plan;
            try
            {
                var opts = new BenchMonumentPackerOptions(gridStride: stride);
                plan = BenchMonumentPacker.PackBlockGraph(bg, inv, opts);
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var boxes = new List<Box>(plan.PlacedCount);
            var ids = new List<string>(plan.PlacedCount);
            var rots = new List<int>(plan.PlacedCount);
            var cellIds = new List<string>(plan.PlacedCount);
            foreach (var p in plan.Placements)
            {
                boxes.Add(new Box(Plane.WorldXY,
                    new Interval(p.OriginX, p.OriginX + p.Dx),
                    new Interval(p.OriginY, p.OriginY + p.Dy),
                    new Interval(p.OriginZ, p.OriginZ + p.Dz)));
                ids.Add(p.MonumentId);
                rots.Add(p.OrientationIndex);
                cellIds.Add(p.CellId);
            }

            da.SetData(0, new GH_ObjectWrapper(plan));
            da.SetDataList(1, boxes);
            da.SetDataList(2, ids);
            da.SetDataList(3, rots);
            da.SetDataList(4, cellIds);
            da.SetDataList(5, plan.UnplacedMonumentIds);
            da.SetData(6, plan.FillRatio);
        }
    }

    [DesignApplication(
        "Pack a MonumentInventory inside ONE BlockCell",
        DesignFlow.TopDown)]
    [Algorithm("24-orientation SO(3) sampling + greedy AABB packing", "Frahan-original",
        Note = "single-cell entry to the same BenchMonumentPacker engine as MonPack; Frahan-original recipe")]
    public sealed class FrahanPackMonumentsInCellComponent : FrahanComponentBase
    {
        public FrahanPackMonumentsInCellComponent()
            : base(
                "Frahan Pack Monuments In Cell", "MonInCell",
                "Pack a MonumentInventory inside ONE BlockCell. Useful when " +
                "you want to assign specific monuments to specific cells " +
                "rather than letting the bench-wide packer order them. Frahan-original method.",
                "Frahan", "Fabricate")
        { }

        public override Guid ComponentGuid =>
            new Guid("F7A16003-0001-4F2D-A0B0-7E60CADA17F3");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("PackIntoBlock.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Block Graph", "Bg", "BlockGraph (the cell is selected by index).", GH_ParamAccess.item);
            p.AddIntegerParameter("Cell Index", "Ci", "Index of the cell within Bg.Cells.", GH_ParamAccess.item, 0);
            p.AddGenericParameter("Inventory", "Inv", "MonumentInventory.", GH_ParamAccess.item);
            p.AddNumberParameter("Grid Stride (m)", "Gs", "Candidate-origin sweep step.", GH_ParamAccess.item, 0.05);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBoxParameter("Placed Boxes", "B", "Axis-aligned box per placement.", GH_ParamAccess.list);
            p.AddTextParameter("Placed Ids", "I", "Monument ids placed.", GH_ParamAccess.list);
            p.AddIntegerParameter("Orientation Index", "R", "Rotation index per placement.", GH_ParamAccess.list);
            p.AddIntegerParameter("Placed Count", "N", "Total placements in this cell.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var bgW = new GH_ObjectWrapper();
            int cellIndex = 0;
            var invW = new GH_ObjectWrapper();
            double stride = 0.05;
            if (!da.GetData(0, ref bgW) || !(bgW.Value is BlockGraph bg))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Block Graph required."); return; }
            da.GetData(1, ref cellIndex);
            if (cellIndex < 0 || cellIndex >= bg.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cell Index {cellIndex} out of range [0, {bg.Count - 1}]."); return; }
            if (!da.GetData(2, ref invW) || !(invW.Value is MonumentInventory inv))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "MonumentInventory required."); return; }
            da.GetData(3, ref stride);

            IReadOnlyList<MonumentPlacement> placements;
            try
            {
                var opts = new BenchMonumentPackerOptions(gridStride: stride);
                placements = BenchMonumentPacker.PackInCell(bg.Cells[cellIndex], inv.Monuments, opts);
            }
            catch (Exception ex)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            var boxes = new List<Box>(placements.Count);
            var ids = new List<string>(placements.Count);
            var rots = new List<int>(placements.Count);
            foreach (var p in placements)
            {
                boxes.Add(new Box(Plane.WorldXY,
                    new Interval(p.OriginX, p.OriginX + p.Dx),
                    new Interval(p.OriginY, p.OriginY + p.Dy),
                    new Interval(p.OriginZ, p.OriginZ + p.Dz)));
                ids.Add(p.MonumentId);
                rots.Add(p.OrientationIndex);
            }
            da.SetDataList(0, boxes);
            da.SetDataList(1, ids);
            da.SetDataList(2, rots);
            da.SetData(3, placements.Count);
        }
    }
}
