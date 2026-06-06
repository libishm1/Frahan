#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Packing;
using Grasshopper.Kernel;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // WallFrameComponent — bundles 3 numbers (width / height / thickness) into
    // a Frahan.Masonry.Packing.WallFrame DTO that downstream Ashlar Pack
    // components consume via the optional WallFrame input.
    //
    // ComponentGuid: A2B3C4D5-E6F7-489A-BCDE-F01234567890
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Wall Frame.
    /// Bundles wall dimensions into a WallFrame DTO.
    /// </summary>
        [DesignApplication(
        "Bundles wall width / height / thickness into a WallFrame DTO",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original wall-frame DTO for ashlar / rubble packers")]
    public sealed class WallFrameComponent : GH_Component
    {
        public WallFrameComponent()
            : base(
                "Wall Frame", "WallFrame",
                "Bundles wall width / height / thickness into a WallFrame DTO. " +
                "Wire into the optional WallFrame input on Ashlar Pack so the " +
                "envelope can be reused across multiple packers.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("A2B3C4D5-E6F7-489A-BCDE-F01234567890");

        protected override Bitmap Icon => IconProvider.Load("CourseGenerator.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Width", "W",
                "Wall length along +X (Rhino-document units, typically meters). " +
                "Must be > 0. Example: 1.5 for a 1.5 m long wall.",
                GH_ParamAccess.item, 1.5);
            p.AddNumberParameter("Height", "H",
                "Wall height along +Z. Must be > 0. Example: 1.0 for 1 m tall.",
                GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Thickness", "T",
                "Wall thickness along +Y. Must be > 0. Default 0.20 — typical " +
                "single-leaf masonry. Use 0.40 for double-leaf.",
                GH_ParamAccess.item, 0.20);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Wall Frame", "F",
                "WallFrame DTO. Wire into Ashlar Pack's Wall Frame input to " +
                "reuse the same envelope across multiple packers.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            double w = 0.0, h = 0.0, t = 0.0;
            if (!da.GetData(0, ref w)) return;
            if (!da.GetData(1, ref h)) return;
            if (!da.GetData(2, ref t)) return;

            WallFrame frame;
            try
            {
                frame = new WallFrame(w, h, t);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"WallFrame construction failed: {ex.Message}");
                return;
            }
            da.SetData(0, frame);
        }
    }
}
