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
    // AshlarPackComponent — lay convex Slabs into a coursed-ashlar wall and
    // emit a MasonryAssembly + AshlarPackResult downstream. Wraps
    // Frahan.Masonry.Packing.AshlarLayoutEngine.
    //
    // ComponentGuid: F1A2B3C4-D5E6-4789-9ABC-DEF012345678
    //
    // Stage 1 of the masonry packer rollout — single component, primitives
    // only. Stage 2 adds optional WallFrame/Options inputs at the END of the
    // input list (GUIDs unchanged so existing documents continue to load).
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Ashlar Pack.
    /// Lays convex Slabs into a coursed-ashlar wall.
    /// </summary>
    [Algorithm("Ashlar coursed wall layout", "Frahan-original 3D grid stacking with running bond", Note = "AABB-first placement, translation only")]
    [Algorithm("Running-bond ashlar pipeline reference", "Gramazio Kohler Eichenhofer 2017 NCCR Digital Fabrication ETH Zurich", Note = "Robotic stone assembly canonical running-bond / bedded-ashlar pipeline; gramaziokohler/ashlar repo")]
    [Algorithm("Power-of-10 hardening", "Holzmann NASA/JPL 2006 ten coding rules for safety-critical software", Note = "Loop bounds, no recursion, two-assertion-per-function")]
    [DesignApplication(
        "Lay convex Slabs into a coursed-ashlar wall with running-bond joints.",
        DesignFlow.BottomUp,
        Precedent = "Ashlar masonry vernacular; Gramazio Kohler Eichenhofer 2017 NCCR Digital Fabrication ETH",
        Tolerance = "course horizontality <= 1 deg/row; vertical joints offset >= 30 % between rows",
        CardSet = "wiki/research/hitl_cards/bu_ashlar/ (proposed)")]
    public sealed class AshlarPackComponent : FrahanComponentBase
    {
        public AshlarPackComponent()
            : base(
                "Ashlar Pack", "AshlarPack",
                "Lays convex Slabs into a coursed-ashlar wall (running bond). " +
                "Emits a MasonryAssembly with bottom-row blocks fixed and an " +
                "AshlarPackResult carrying coverage / leftovers / notes.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F1A2B3C4-D5E6-4789-9ABC-DEF012345678");

        protected override Bitmap Icon => IconProvider.Load("CourseGenerator.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            // Honeybee-style input descriptions: each one explains purpose +
            // unit + typical range + default rationale. The user can wire only
            // {Meshes, Wall Frame, Options} for a clean canvas; all primitive
            // numeric inputs have working defaults.
            p.AddNumberParameter("Wall Width", "W",
                "Wall length along +X (units of the active Rhino document, " +
                "typically meters). Must be > 0. Recommended: wire a Wall " +
                "Frame instead and leave this at default.",
                GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Wall Height", "H",
                "Wall height along +Z. Must be > 0. Default 1.0.",
                GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Wall Thickness", "T",
                "Wall thickness along +Y. Must be > 0. Default 0.20 (typical " +
                "single-leaf masonry).",
                GH_ParamAccess.item, 0.20);
            p.AddMeshParameter("Meshes", "M",
                "Block inventory as Rhino meshes (e.g. from Quarry DFN, Slab " +
                "Cut By Fractures, or hand-authored). Each mesh must be convex " +
                "with at least 4 vertices and 4 faces. The packer auto-converts " +
                "to its internal Slab DTO.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Course Mode", "M",
                "Layout strategy. 0 = CoursedAshlar (uniform course height; " +
                "one block size). 1 = CoursedRubble (multi-bin: each course " +
                "uses one block-height bin; mixes block sizes across courses).",
                GH_ParamAccess.item, 0);
            p.AddNumberParameter("Target Course Height", "Ch",
                "Course height for CoursedAshlar. For CoursedRubble it seeds " +
                "the first bin height. Should match the Z-extent of your " +
                "blocks within Height Tolerance. Default 0.15.",
                GH_ParamAccess.item, 0.15);
            p.AddNumberParameter("Bed Joint", "Bj",
                "Vertical mortar gap between courses (units of the document). " +
                ">= 0. Default 0.001 (1 mm typical lime-mortar gap).",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Head Joint", "Hj",
                "Horizontal mortar gap between adjacent blocks in a course. " +
                ">= 0. Default 0.001 (1 mm).",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Stagger Offset", "So",
                "Running-bond shift on odd courses, as a fraction of the " +
                "average block width. In [0, 1]. Default 0.5 (half-bond).",
                GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Density", "D",
                "Material density in kg/m³ (or any consistent mass-per-volume " +
                "unit). > 0. Default 2400 (typical limestone). Used by the " +
                "downstream stability solver to compute self-weight.",
                GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Height Tolerance", "Tol",
                "Block-height tolerance for inventory filtering. >= 0. Default " +
                "0.05 (5 cm — accommodates rough-cut quarry blocks).",
                GH_ParamAccess.item, 0.05);
            p.AddGenericParameter("Wall Frame", "Wf",
                "Optional WallFrame DTO from Wall Frame component. When wired, " +
                "this overrides the primitive Wall Width / Height / Thickness " +
                "inputs above. Recommended for clean canvas wiring.",
                GH_ParamAccess.item);
            p[11].Optional = true;
            p.AddGenericParameter("Options", "Op",
                "Optional AshlarPackOptions DTO from Ashlar Pack Options " +
                "component. When wired, overrides Course Mode / Course Height " +
                "/ joints / stagger / density / tolerance. Recommended for " +
                "clean canvas wiring.",
                GH_ParamAccess.item);
            p[12].Optional = true;
            // 2026-05-14: optional start-plane for display orientation. The
            // packer engine stays in world XY (gravity = -Z, stability solver
            // expects this). When the plane is wired, the component emits a
            // Display Transform that maps world XY into the user's plane so
            // downstream preview components can re-orient the wall visually.
            p.AddPlaneParameter("Start Plane", "Sp",
                "Optional start plane. Engine stays in world XY (correct for " +
                "the stability solver, which assumes gravity = -Z). The " +
                "component emits a Display Transform that maps world XY into " +
                "this plane; wire it into AssemblyPreview / mesh Transform " +
                "to re-orient the wall visually.",
                GH_ParamAccess.item, Plane.Unset);
            p[13].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Assembly", "A",
                "MasonryAssembly with bottom-course blocks fixed. Wire into " +
                "Masonry Stability (RBE).",
                GH_ParamAccess.item);
            p.AddGenericParameter("Result", "R",
                "AshlarPackResult carrying coverage / leftovers / notes / placed " +
                "blocks. Wire into Pack Diagnostics (Stage 3).",
                GH_ParamAccess.item);
            p.AddTransformParameter("Display Transform", "T",
                "World-XY → Start Plane transform. Identity when Start Plane " +
                "is not wired. Wire into mesh / preview transforms.",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveSafe(IGH_DataAccess da)
        {
            double wallWidth = 0.0;
            double wallHeight = 0.0;
            double wallThickness = 0.0;
            int courseModeInt = 0;
            double targetCourseHeight = 0.0;
            double bedJoint = 0.0;
            double headJoint = 0.0;
            double staggerOffset = 0.5;
            double density = 2400.0;
            double heightTolerance = 0.05;

            if (!da.GetData(0, ref wallWidth)) return;
            if (!da.GetData(1, ref wallHeight)) return;
            if (!da.GetData(2, ref wallThickness)) return;

            var meshes = new List<Mesh>();
            if (!da.GetDataList(3, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No meshes provided.");
                return;
            }
            var slabs = new List<Slab>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = GhInterop.SlabFromMesh(meshes[i]);
                if (s == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Meshes[{i}] is invalid (need >= 4 vertices and >= 4 faces).");
                    return;
                }
                slabs.Add(s);
            }
            if (slabs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need at least one block mesh.");
                return;
            }

            if (!da.GetData(4, ref courseModeInt)) courseModeInt = 0;
            if (!da.GetData(5, ref targetCourseHeight)) return;
            if (!da.GetData(6, ref bedJoint)) bedJoint = 0.001;
            if (!da.GetData(7, ref headJoint)) headJoint = 0.001;
            if (!da.GetData(8, ref staggerOffset)) staggerOffset = 0.5;
            if (!da.GetData(9, ref density)) density = 2400.0;
            if (!da.GetData(10, ref heightTolerance)) heightTolerance = 0.05;

            // ---- Optional Stage 2 inputs ----------------------------------
            object rawFrame = null;
            object rawOptions = null;
            da.GetData(11, ref rawFrame);
            da.GetData(12, ref rawOptions);
            WallFrame wiredFrame = UnwrapWallFrame(rawFrame);
            AshlarPackOptions wiredOptions = UnwrapOptions(rawOptions);

            if (wiredFrame != null)
            {
                wallWidth = wiredFrame.WallWidth;
                wallHeight = wiredFrame.WallHeight;
                wallThickness = wiredFrame.WallThickness;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "WallFrame override active: primitive Wall Width/Height/Thickness inputs ignored.");
            }

            CourseMode mode;
            if (wiredOptions != null)
            {
                mode = wiredOptions.Mode;
                targetCourseHeight = wiredOptions.TargetCourseHeight;
                bedJoint = wiredOptions.BedJoint;
                headJoint = wiredOptions.HeadJoint;
                staggerOffset = wiredOptions.StaggerOffset;
                density = wiredOptions.Density;
                heightTolerance = wiredOptions.HeightTolerance;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Options override active: primitive algorithmic inputs ignored.");
            }
            else
            {
                switch (courseModeInt)
                {
                    case 0: mode = CourseMode.CoursedAshlar; break;
                    case 1: mode = CourseMode.CoursedRubble; break;
                    default:
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"Course Mode must be 0 or 1, got {courseModeInt}.");
                        return;
                }
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
                result = AshlarLayoutEngine.Pack(slabs, options);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Ashlar packing failed: {ex.Message}");
                return;
            }

            if (result.PlacedBlocks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No blocks were placed; check inventory and wall dimensions.");
            }
            for (int i = 0; i < result.Notes.Count && i < 8; i++)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, result.Notes[i]);
            }

            // Start-plane: emit a Display Transform output for downstream
            // visual re-orientation. Engine output stays in world XY.
            Plane startPlane = Plane.Unset;
            da.GetData(13, ref startPlane);
            Transform displayXform = (startPlane.IsValid)
                ? Transform.PlaneToPlane(Plane.WorldXY, startPlane)
                : Transform.Identity;

            da.SetData(0, result.Assembly);
            da.SetData(1, result);
            da.SetData(2, displayXform);
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static Slab UnwrapSlab(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap)
                return fromWrap;
            return null;
        }

        private static WallFrame UnwrapWallFrame(object raw)
        {
            if (raw == null) return null;
            if (raw is WallFrame direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is WallFrame fromWrap)
                return fromWrap;
            return null;
        }

        private static AshlarPackOptions UnwrapOptions(object raw)
        {
            if (raw == null) return null;
            if (raw is AshlarPackOptions direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is AshlarPackOptions fromWrap)
                return fromWrap;
            return null;
        }

        private static string DescribeType(object raw)
        {
            if (raw == null) return "null";
            if (raw is GH_ObjectWrapper wrap)
            {
                var inner = wrap.Value;
                return inner == null
                    ? "GH_ObjectWrapper(null)"
                    : $"GH_ObjectWrapper({inner.GetType().FullName})";
            }
            return raw.GetType().FullName;
        }
    }
}
