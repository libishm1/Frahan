#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Quarry;
using Grasshopper.Kernel;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // JointSetComponent — constructs a JointSet DTO from dip direction, dip,
    // mean spacing, and optional scatter. Wire into Quarry DFN.
    //
    // ComponentGuid: ECFDAEBF-CBDC-4345-6789-012345678BCD
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Quarry &gt; Joint Set.
    /// Authors a structural-geology joint set for use in Quarry DFN.
    /// </summary>
    [Algorithm("Joint-set DFN authoring", "ISRM Suggested Methods + Priest 1993 joint-set DFN", WikiPath = "wiki/index/references.md")]
        [DesignApplication(
        "Authors a structural-geology joint set: dip direction (azimuth  of steepest descent, 0 = North), dip angle,...",
        DesignFlow.BottomUp,
        Precedent = "ISRM Suggested Methods + Priest 1993 joint-set DFN")]
    public sealed class JointSetComponent : GH_Component
    {
        public JointSetComponent()
            : base(
                "Joint Set", "Joint",
                "Authors a structural-geology joint set: dip direction (azimuth " +
                "of steepest descent, 0 = North), dip angle, mean spacing along " +
                "the normal, optional orientation scatter. Wire into Quarry DFN. Implements joint-set DFN authoring (ISRM/Priest 1993).",
                "Frahan", "Quarry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("ECFDAEBF-CBDC-4345-6789-012345678BCD");

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override Bitmap Icon => IconProvider.Load("Stratigraphy.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Dip Direction", "DD",
                "Azimuth of the steepest descent line, clockwise from North (+Y), in [0, 360).",
                GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Dip", "D",
                "Dip angle from horizontal, in [0, 90]. 0 = horizontal joint, 90 = vertical.",
                GH_ParamAccess.item, 90.0);
            p.AddNumberParameter("Spacing", "S",
                "Mean spacing along the normal (same units as the quarry block). > 0.",
                GH_ParamAccess.item, 0.30);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Joint Set", "J",
                "JointSet DTO. Wire into Quarry DFN.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            double dipDir = 0.0, dip = 90.0, spacing = 0.30;
            da.GetData(0, ref dipDir);
            da.GetData(1, ref dip);
            da.GetData(2, ref spacing);

            JointSet js;
            try
            {
                js = new JointSet(dipDir, dip, spacing);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Joint set construction failed: {ex.Message}");
                return;
            }
            da.SetData(0, js);
        }
    }
}
