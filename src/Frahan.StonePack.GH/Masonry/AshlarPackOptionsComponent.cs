#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Packing;
using Grasshopper.Kernel;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // AshlarPackOptionsComponent — bundles all algorithmic knobs into an
    // AshlarPackOptions DTO. Internally it requires a WallFrame to construct
    // the DTO, but the values stored on the DTO are the algorithmic ones; the
    // wall envelope is overwritten when the Ashlar Pack component sees both
    // a WallFrame and an Options input.
    //
    // ComponentGuid: B3C4D5E6-F7A8-49AB-CDEF-012345678901
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Ashlar Pack Options.
    /// Bundles course mode + joint sizes + density + tolerance.
    /// </summary>
        [DesignApplication(
        "Bundles ashlar packer knobs (course mode, joints, stagger,  density, height tolerance) into an AshlarPackOp...",
        DesignFlow.BottomUp,
        Precedent = "DTO bundle for AshlarPackComponent (Gramazio Kohler Eichenhofer 2017)",
        CardSet = "wiki/research/hitl_cards/bu_ashlar/")]
    public sealed class AshlarPackOptionsComponent : GH_Component
    {
        public AshlarPackOptionsComponent()
            : base(
                "Ashlar Pack Options", "AshOpts",
                "Bundles ashlar packer knobs (course mode, joints, stagger, " +
                "density, height tolerance) into an AshlarPackOptions DTO. " +
                "Wire into Ashlar Pack's optional Options input.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("B3C4D5E6-F7A8-49AB-CDEF-012345678901");

        protected override Bitmap Icon => IconProvider.Load("BondPattern.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddIntegerParameter("Course Mode", "M",
                "Layout strategy. 0 = CoursedAshlar (single block-height bin, " +
                "uniform courses). 1 = CoursedRubble (multi-bin, mixes block " +
                "sizes across courses). Default 0.",
                GH_ParamAccess.item, 0);
            p.AddNumberParameter("Course Height", "Ch",
                "Target course height in document units (typically meters). " +
                "Must match the Z-extent of your blocks within Height Tolerance. " +
                "Default 0.15.",
                GH_ParamAccess.item, 0.15);
            p.AddNumberParameter("Bed Joint", "Bj",
                "Vertical mortar gap between courses, in document units. >= 0. " +
                "Default 0.001 (1 mm typical).",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Head Joint", "Hj",
                "Horizontal mortar gap between adjacent blocks, in document " +
                "units. >= 0. Default 0.001.",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Stagger Offset", "So",
                "Running-bond shift on odd courses, as a fraction of the " +
                "average block width. In [0, 1]. Default 0.5 (half-bond, " +
                "the standard).",
                GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Density", "D",
                "Material density in kg/m³ (or consistent mass-per-volume " +
                "unit). > 0. Default 2400 (typical limestone). Used by the " +
                "downstream stability solver.",
                GH_ParamAccess.item, 2400.0);
            p.AddNumberParameter("Height Tolerance", "Tol",
                "Block-height tolerance for inventory filtering and rubble " +
                "binning, in document units. >= 0. Default 0.05 (5 cm — " +
                "accommodates rough-cut quarry blocks).",
                GH_ParamAccess.item, 0.05);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Options", "O",
                "AshlarPackOptions DTO bundling all algorithmic knobs. Wire " +
                "into Ashlar Pack's Options input — when wired, it overrides " +
                "the equivalent primitive inputs on the packer.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            int modeInt = 0;
            double courseHeight = 0.15;
            double bedJoint = 0.001;
            double headJoint = 0.001;
            double stagger = 0.5;
            double density = 2400.0;
            double heightTol = 0.05;

            if (!da.GetData(0, ref modeInt)) modeInt = 0;
            if (!da.GetData(1, ref courseHeight)) courseHeight = 0.15;
            if (!da.GetData(2, ref bedJoint)) bedJoint = 0.001;
            if (!da.GetData(3, ref headJoint)) headJoint = 0.001;
            if (!da.GetData(4, ref stagger)) stagger = 0.5;
            if (!da.GetData(5, ref density)) density = 2400.0;
            if (!da.GetData(6, ref heightTol)) heightTol = 0.05;

            CourseMode mode;
            switch (modeInt)
            {
                case 0: mode = CourseMode.CoursedAshlar; break;
                case 1: mode = CourseMode.CoursedRubble; break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Course Mode must be 0 or 1, got {modeInt}.");
                    return;
            }

            AshlarPackOptions opts;
            try
            {
                // Stub wall dims; Ashlar Pack overwrites them from the WallFrame
                // when it consumes this Options DTO. We use placeholder positives
                // here so the constructor's positivity validation passes.
                opts = new AshlarPackOptions(
                    mode, 1.0, 1.0, 1.0,
                    courseHeight, bedJoint, headJoint, stagger,
                    density, heightTol);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"AshlarPackOptions construction failed: {ex.Message}");
                return;
            }
            da.SetData(0, opts);
        }
    }
}
