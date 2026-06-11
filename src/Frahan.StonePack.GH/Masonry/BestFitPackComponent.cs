#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Packing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BestFitPackComponent — inventory-aware ashlar packer (GKR-style). Picks
    // the highest-scoring stone from the inventory at each placement slot
    // rather than the first that fits. Trade-off: marginally slower, much
    // better material utilization on heterogeneous inventories.
    //
    // Reference: gramaziokohler/ashlar (ETH Zurich Gramazio Kohler Research).
    //
    // ComponentGuid: 01234567-89AB-CDEF-0123-456789ABCDEF
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Best Fit Pack.
    /// Inventory-aware ashlar packing: picks the best-scoring stone per slot.
    /// </summary>
    [Algorithm("Best-fit rubble inventory placement", "Gramazio Kohler Eichenhofer 2017 NCCR Digital Fabrication", Note = "Robotic stone assembly inventory packer")]
        [DesignApplication(
        "Inventory-aware ashlar packing",
        DesignFlow.BottomUp,
        Precedent = "Gramazio Kohler Eichenhofer 2017 NCCR Digital Fabrication ETH (gramaziokohler/ashlar inventory-aware variant)",
        CardSet = "wiki/research/hitl_cards/bu_ashlar/")]
    public sealed class BestFitPackComponent : GH_Component
    {
        public BestFitPackComponent()
            : base(
                "Best Fit Pack", "BestFit",
                "Inventory-aware ashlar packing. For each placement slot, " +
                "scores every remaining stone by width / depth / height / " +
                "aspect-ratio fit and picks the highest-scoring candidate. " +
                "Companion to Ashlar Pack (which uses first-fit). Recommended " +
                "for heterogeneous quarry inventories where stone sizes vary. [Gramazio et al. 2017]",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("01234567-89AB-CDEF-0123-456789ABCDEF");

        protected override Bitmap Icon => IconProvider.Load("CourseGenerator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Wall Width", "W",
                "Wall length along +X. Must be > 0. Recommended: wire a " +
                "Wall Frame instead.",
                GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Wall Height", "H",
                "Wall height along +Z. Must be > 0.",
                GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Wall Thickness", "T",
                "Wall thickness along +Y. Must be > 0.",
                GH_ParamAccess.item, 0.20);
            p.AddMeshParameter("Inventory", "I",
                "Block inventory as Rhino meshes. Each mesh becomes a " +
                "candidate stone for best-fit selection.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Course Height", "Ch",
                "Target course height. Must match the Z-extent of your " +
                "blocks within Height Tolerance. Default 0.15.",
                GH_ParamAccess.item, 0.15);
            p.AddNumberParameter("Bed Joint", "Bj",
                "Vertical mortar gap between courses. Default 0.001.",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Head Joint", "Hj",
                "Horizontal mortar gap between blocks. Default 0.001.",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Stagger Offset", "So",
                "Running-bond shift on odd courses, fraction of average " +
                "block width. Default 0.5.",
                GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Density", "D",
                "Material density (kg/m³). Default 2400.",
                GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Height Tolerance", "Tol",
                "Block-height tolerance for inventory filtering. Default 0.05.",
                GH_ParamAccess.item, 0.05);
            p.AddGenericParameter("Wall Frame", "Wf",
                "Optional WallFrame DTO. Overrides primitive Wall W/H/T.",
                GH_ParamAccess.item);
            p[10].Optional = true;
            p.AddGenericParameter("Options", "Op",
                "Optional AshlarPackOptions DTO. Overrides primitive " +
                "algorithmic inputs.",
                GH_ParamAccess.item);
            p[11].Optional = true;
            p.AddPlaneParameter("Start Plane", "Sp",
                "Optional start plane. Engine stays in world XY (correct for " +
                "the stability solver). Component emits a Display Transform " +
                "that maps world XY into this plane for visual re-orientation.",
                GH_ParamAccess.item, Plane.Unset);
            p[12].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly with bottom-course blocks fixed.",
                GH_ParamAccess.item);
            p.AddGenericParameter("Result", "R",
                "AshlarPackResult — coverage / leftovers / notes / placed blocks.",
                GH_ParamAccess.item);
            p.AddTransformParameter("Display Transform", "T",
                "World-XY → Start Plane transform. Identity when unset.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            double wallWidth = 1.5, wallHeight = 1.0, wallThickness = 0.20;
            double targetCourseHeight = 0.15, bedJoint = 0.001, headJoint = 0.001;
            double staggerOffset = 0.5, density = 2400.0, heightTolerance = 0.05;

            if (!da.GetData(0, ref wallWidth)) return;
            if (!da.GetData(1, ref wallHeight)) return;
            if (!da.GetData(2, ref wallThickness)) return;

            var meshes = new List<Mesh>();
            if (!da.GetDataList(3, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No inventory meshes provided.");
                return;
            }
            var slabs = new List<Slab>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = GhInterop.SlabFromMesh(meshes[i]);
                if (s == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Inventory[{i}] is invalid (need >= 4 vertices and >= 4 faces).");
                    return;
                }
                slabs.Add(s);
            }
            if (slabs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need at least one inventory mesh.");
                return;
            }

            da.GetData(4, ref targetCourseHeight);
            da.GetData(5, ref bedJoint);
            da.GetData(6, ref headJoint);
            da.GetData(7, ref staggerOffset);
            da.GetData(8, ref density);
            da.GetData(9, ref heightTolerance);

            object rawFrame = null;
            object rawOpts = null;
            da.GetData(10, ref rawFrame);
            da.GetData(11, ref rawOpts);
            WallFrame wiredFrame = UnwrapWallFrame(rawFrame);
            AshlarPackOptions wiredOpts = UnwrapOptions(rawOpts);

            if (wiredFrame != null)
            {
                wallWidth = wiredFrame.WallWidth;
                wallHeight = wiredFrame.WallHeight;
                wallThickness = wiredFrame.WallThickness;
            }
            CourseMode mode = CourseMode.CoursedAshlar;
            if (wiredOpts != null)
            {
                mode = wiredOpts.Mode;
                targetCourseHeight = wiredOpts.TargetCourseHeight;
                bedJoint = wiredOpts.BedJoint;
                headJoint = wiredOpts.HeadJoint;
                staggerOffset = wiredOpts.StaggerOffset;
                density = wiredOpts.Density;
                heightTolerance = wiredOpts.HeightTolerance;
            }

            AshlarPackOptions options;
            try
            {
                options = new AshlarPackOptions(
                    mode, wallWidth, wallHeight, wallThickness,
                    targetCourseHeight, bedJoint, headJoint, staggerOffset,
                    density, heightTolerance);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"AshlarPackOptions construction failed: {ex.Message}");
                return;
            }

            AshlarPackResult result;
            try
            {
                result = BestFitInventoryPacker.Pack(slabs, options);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Best-fit packing failed: {ex.Message}");
                return;
            }

            for (int i = 0; i < result.Notes.Count && i < 8; i++)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, result.Notes[i]);

            Plane startPlane = Plane.Unset;
            da.GetData(12, ref startPlane);
            Transform displayXform = (startPlane.IsValid)
                ? Transform.PlaneToPlane(Plane.WorldXY, startPlane)
                : Transform.Identity;

            da.SetData(0, result.Assembly);
            da.SetData(1, result);
            da.SetData(2, displayXform);
        }

        private static WallFrame UnwrapWallFrame(object raw)
        {
            if (raw == null) return null;
            if (raw is WallFrame direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is WallFrame fromWrap) return fromWrap;
            return null;
        }

        private static AshlarPackOptions UnwrapOptions(object raw)
        {
            if (raw == null) return null;
            if (raw is AshlarPackOptions direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is AshlarPackOptions fromWrap) return fromWrap;
            return null;
        }
    }
}
