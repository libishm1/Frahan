#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Packing;
using Frahan.GH.Attributes;
using Frahan.GH.Quarry;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Packing;

// =============================================================================
// BlockPackTreeComponent — Frahan port of Kim 2025 (Computation 13:211,
// CC BY 4.0) tree/forest guillotine packer. Synthesis in
// wiki/papers/kim2025_tree_packing.md.
//
// Subcategory: Masonry (fabrication-side packing of sculpture cuboids
// into stone-block containers). Picked over a brand-new "Block Cut"
// panel to keep the recommended §7.1 11-panel layout intact.
//
// Going beyond Kim 2025 (closes paper's documented gaps):
//   1. Deterministic Seed input (paper uses internal random()).
//   2. Kerf Width input (paper assumes zero kerf).
//   3. Forbidden Boxes input per container (paper requires clean
//      cuboid containers; this lets fracture-intersected cells from
//      HeteroExt or BCOExtract feed in as forbidden regions).
// =============================================================================

[Algorithm("Tree-forest guillotine pack", "Kim 2025 Computation 13:211", Doi = "10.3390/computation13090211", WikiPath = "wiki/papers/kim2025_tree_packing.md", Note = "CC-BY 4.0; randomised forest growth")]
[Algorithm("Jalalian BCSdbBV cost", "Jalalian et al. 2023 Sci. Reports cutting-surface-area minimisation", Note = "Pareto axis I11")]
[DesignApplication(
    "Pack sculpture/element cuboids into stone-block containers  with axis-aligned guillotine cuts",
    DesignFlow.TopDown,
    Precedent = "Park Han 2024 tree-packing; Chehrazad 2025 DLBF substrate",
    CardSet = "wiki/research/hitl_cards/td_voussoir/")]
public sealed class BlockPackTreeComponent : GH_Component
{
    public BlockPackTreeComponent()
        : base("Block Pack (Tree)", "BlockPackTree",
            "Pack sculpture/element cuboids into stone-block containers " +
            "with axis-aligned guillotine cuts. Frahan port of Kim 2025 " +
            "(Computation 13:211, CC BY 4.0). Picks the cheapest subset " +
            "of containers that fits all elements; falls back to highest " +
            "packed-value when full packing is infeasible. Three " +
            "extensions beyond the paper: deterministic seed, saw kerf " +
            "width, and Forbidden Boxes per container.",
            "Frahan", "Masonry")
    {
    }

    public override Guid ComponentGuid => new Guid("C2D3E4F5-3001-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("TreePack.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBoxParameter("Elements", "E",
            "Element AABBs (sculpture / final-piece bounding boxes). " +
            "Only the box dimensions are used for the fit test; the " +
            "Box.Plane defines the element's source pose.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Element Values", "Pe",
            "Per-element value (e.g. piece price). Must match the " +
            "element count.",
            GH_ParamAccess.list);
        p.AddBoxParameter("Containers", "C",
            "Container AABBs (stone-block bounding boxes).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Container Prices", "Pc",
            "Per-container price (e.g. stone-block material cost). " +
            "Must match the container count.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Rotation Mode", "Rot",
            "0 = None (identity only), 1 = OneAxis (vein-aligned), " +
            "2 = ThreeAxis (six 90° rotations).",
            GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Forests", "F",
            "Number of independent randomised forests to grow. Score " +
            "plateaus by f ≈ 50–1000 on small instances; large jobs " +
            "may need 10⁴–10⁶ forests (see paper §4).",
            GH_ParamAccess.item, 256);
        p.AddIntegerParameter("Seed", "S",
            "Master seed (Frahan extension beyond Kim 2025). Forest k " +
            "uses (seed + k) internally; setting the same seed gives " +
            "the same result. Default 0 is deterministic.",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Kerf Width", "K",
            "Saw kerf width in model units (Frahan extension). Each " +
            "axis-aligned cut consumes this much material along its " +
            "direction. Real values: 5–10 mm for diamond wire saws, " +
            "1–3 mm for thin blades. Default 0.",
            GH_ParamAccess.item, 0.0);
        p.AddBoxParameter("Forbidden Boxes", "X",
            "Optional flat list of forbidden Box regions inside any " +
            "container (Frahan extension; closes Kim §8.2 gap on " +
            "fracture-aware containers). Elements that overlap a " +
            "forbidden region in their target container are rejected. " +
            "A forbidden box outside all containers has no effect.",
            GH_ParamAccess.list);
        p[8].Optional = true;
        p.AddNumberParameter("Cut Surface Weight", "Cw",
            "K2 / Jalalian I11 (BCSdbBV) extension. Score subtracts " +
            "weight × Σ(internal-face area) across placements. Default 0 " +
            "preserves the original Kim 2025 score.",
            GH_ParamAccess.item, 0.0);
        p.AddIntegerParameter("Max Parallelism", "Mp",
            "K2 parallel-forest extension. 0 = auto (Environment." +
            "ProcessorCount). 1 forces serial. Parallel results are " +
            "bitwise identical to serial because each forest's RNG is " +
            "seeded independently.",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Memory Budget MB", "Mb",
            "K2 memory-cap extension. When > 0, Forests is " +
            "automatically reduced so f × ~1.4 KB × element-count ≤ budget. " +
            "0 = unlimited.",
            GH_ParamAccess.item, 0.0);
        // 2026-05-30: optional QuarryBlock input (HITL #2). Indexed at the
        // END so existing build_gh wiring (indices 0-11) is untouched.
        // When wired, Block.UsableVolume's world AABB becomes the single
        // container; takes precedence over Containers / Container Prices if
        // both are wired. GUID unchanged.
        p.AddParameter(new Param_QuarryBlock(), "Block", "QB",
            "Optional QuarryBlock from Scan to Block Inventory. When wired, " +
            "Block.UsableVolume's world bounding box is used as the single " +
            "container (takes precedence over Containers input if both are " +
            "wired).",
            GH_ParamAccess.item);
        p[12].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBoxParameter("Placed Boxes", "Pb",
            "Placed element AABBs in world-frame coordinates.",
            GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "Xf",
            "World-frame transform per placed element (apply to the " +
            "source element Box to recover the placed pose, including " +
            "any rotation).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Placed Element Ids", "Ei",
            "Index into the input element list for each placement, in " +
            "placement order. Compare against the input element count " +
            "to find unpacked elements.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Placed Container Ids", "Ci",
            "Index into the input container list for each placement.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Used Containers", "Uc",
            "Sorted unique indices of containers that hold at least one " +
            "placed element.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Score", "Sc",
            "Score of the winning forest (Kim 2025 §2.4): sum of packed " +
            "element values, plus 1/(1+containerPrice) bonus when all " +
            "elements fit.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("All Packed", "All",
            "True iff every input element landed in a container.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Best Forest", "Bf",
            "Index of the winning forest (0 ≤ index < Forests).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Human-readable summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var elements = new List<Box>();
        var elementValues = new List<double>();
        var containers = new List<Box>();
        var containerPrices = new List<double>();
        int rotationModeInt = 0;
        int forests = 256;
        int seed = 0;
        double kerf = 0.0;
        var forbiddenBoxesFlat = new List<Box>();
        double cutSurfaceWeight = 0.0;
        int maxParallelism = 0;
        double memoryBudgetMb = 0.0;

        if (!da.GetDataList(0, elements) || elements.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one element required.");
            return;
        }
        if (!da.GetDataList(1, elementValues) || elementValues.Count != elements.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Element Values count ({elementValues.Count}) must match Elements ({elements.Count}).");
            return;
        }
        bool gotContainersFromBoxes = da.GetDataList(2, containers) && containers.Count > 0;
        bool gotContainerPricesFromBoxes = da.GetDataList(3, containerPrices) && containerPrices.Count > 0;
        da.GetData(4, ref rotationModeInt);
        da.GetData(5, ref forests);
        da.GetData(6, ref seed);
        da.GetData(7, ref kerf);
        da.GetDataList(8, forbiddenBoxesFlat);
        da.GetData(9, ref cutSurfaceWeight);
        da.GetData(10, ref maxParallelism);
        da.GetData(11, ref memoryBudgetMb);

        // 2026-05-30: optional QuarryBlock input. When wired, Block.UsableVolume's
        // world AABB becomes a single container with price 0. Takes precedence
        // over the Containers input per HITL #2.
        QuarryBlockGoo blockGoo = null;
        if (da.GetData(12, ref blockGoo) && blockGoo != null && blockGoo.Value != null &&
            blockGoo.Value.UsableVolume != null && blockGoo.Value.UsableVolume.IsValid)
        {
            if (gotContainersFromBoxes)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Block input takes precedence over Mesh input.");
            }
            var bb = blockGoo.Value.UsableVolume.GetBoundingBox(true);
            if (bb.IsValid)
            {
                var boxFromBlock = new Box(Plane.WorldXY,
                    new Interval(bb.Min.X, bb.Max.X),
                    new Interval(bb.Min.Y, bb.Max.Y),
                    new Interval(bb.Min.Z, bb.Max.Z));
                containers = new List<Box> { boxFromBlock };
                containerPrices = new List<double> { 0.0 };
                gotContainersFromBoxes = true;
                gotContainerPricesFromBoxes = true;
            }
        }

        if (!gotContainersFromBoxes || containers.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one container required.");
            return;
        }
        if (!gotContainerPricesFromBoxes || containerPrices.Count != containers.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Container Prices count ({containerPrices.Count}) must match Containers ({containers.Count}).");
            return;
        }

        if (rotationModeInt < 0 || rotationModeInt > 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Rotation Mode must be 0 (None), 1 (OneAxis), or 2 (ThreeAxis); got {rotationModeInt}.");
            return;
        }
        if (forests < 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Forests must be ≥ 1; got {forests}.");
            return;
        }

        GuillotinePackOptions opts;
        try
        {
            long memBytes = memoryBudgetMb > 0 ? (long)(memoryBudgetMb * 1024L * 1024L) : 0L;
            opts = new GuillotinePackOptions(
                forestCount: forests,
                seed: seed,
                rotationMode: (GuillotineRotationMode)rotationModeInt,
                kerfWidth: kerf,
                cutSurfaceWeight: cutSurfaceWeight,
                maxDegreeOfParallelism: maxParallelism,
                memoryBudgetBytes: memBytes);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        // Bucket forbidden boxes by which container they live in. A box
        // is "in" a container when its centre lies inside the container's
        // AABB (cheap approximation; users rarely supply boxes that
        // straddle container boundaries).
        var forbiddenPerContainer = new List<List<Box>>(containers.Count);
        for (int c = 0; c < containers.Count; c++)
            forbiddenPerContainer.Add(new List<Box>());
        foreach (var fb in forbiddenBoxesFlat)
        {
            var centre = fb.Plane.PointAt(
                (fb.X.Min + fb.X.Max) / 2,
                (fb.Y.Min + fb.Y.Max) / 2,
                (fb.Z.Min + fb.Z.Max) / 2);
            for (int c = 0; c < containers.Count; c++)
            {
                if (BoxContainsPoint(containers[c], centre))
                {
                    forbiddenPerContainer[c].Add(fb);
                    break;
                }
            }
        }
        var forbiddenAsReadOnly = new List<IReadOnlyList<Box>>(containers.Count);
        foreach (var f in forbiddenPerContainer) forbiddenAsReadOnly.Add(f);

        GuillotinePackResult result;
        try
        {
            result = TreePackForest.Pack(
                elements, elementValues, containers, containerPrices,
                opts, forbiddenAsReadOnly);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Pack failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var placedBoxes = new List<Box>(result.Placements.Count);
        var xforms = new List<Transform>(result.Placements.Count);
        var elementIds = new List<int>(result.Placements.Count);
        var containerIds = new List<int>(result.Placements.Count);
        foreach (var pl in result.Placements)
        {
            placedBoxes.Add(pl.PlacedBox);
            xforms.Add(pl.Transform);
            elementIds.Add(pl.ElementIndex);
            containerIds.Add(pl.ContainerIndex);
        }

        da.SetDataList(0, placedBoxes);
        da.SetDataList(1, xforms);
        da.SetDataList(2, elementIds);
        da.SetDataList(3, containerIds);
        da.SetDataList(4, result.UsedContainerIndices);
        da.SetData(5, result.Score);
        da.SetData(6, result.AllElementsPacked);
        da.SetData(7, result.BestForestIndex);
        da.SetData(8,
            $"Packed {result.Placements.Count}/{elements.Count} elements " +
            $"into {result.UsedContainerIndices.Count} of {containers.Count} containers; " +
            $"score={result.Score:F3}; all-packed={result.AllElementsPacked}; " +
            $"forests={forests}; seed={seed}; kerf={kerf:F4}");

        if (!result.AllElementsPacked)
        {
            int unpacked = elements.Count - result.Placements.Count;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unpacked} of {elements.Count} elements did not fit. " +
                "Try more forests, a different rotation mode, or add inventory.");
        }
    }

    private static bool BoxContainsPoint(Box b, Point3d p)
    {
        // Transform world point into the box's local frame, then test
        // interval containment.
        if (!b.Plane.RemapToPlaneSpace(p, out Point3d local)) return false;
        return local.X >= b.X.Min && local.X <= b.X.Max
            && local.Y >= b.Y.Min && local.Y <= b.Y.Max
            && local.Z >= b.Z.Min && local.Z <= b.Z.Max;
    }
}
