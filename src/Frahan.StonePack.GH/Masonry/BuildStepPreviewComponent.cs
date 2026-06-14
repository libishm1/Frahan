#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BuildStepPreviewComponent — animation helper for the masonry build
    // sequence. Wire Ordered Meshes from Block Build Order plus a Step
    // slider; the component splits the meshes into "already built" and
    // "still pending" lists. Drag the slider to play back the assembly.
    //
    // ComponentGuid: 56789ABC-DEF0-1234-5678-9ABCDEF01234
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Build Step Preview.
    /// Splits an ordered mesh list at the given step. Built / Pending /
    /// Current outputs let the user animate or filter by build progress.
    /// </summary>
        [DesignApplication(
        "Slider-driven animation of a masonry build sequence",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original step-preview slider")]
    public sealed class BuildStepPreviewComponent : FrahanComponentBase
    {
        public BuildStepPreviewComponent()
            : base(
                "Build Step Preview", "BuildStep",
                "Slider-driven animation of a masonry build sequence. Wire " +
                "the ordered meshes from Block Build Order and a Step " +
                "integer slider. Returns Built (0..step), Pending " +
                "(step..N), and Current (mesh at step).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("56789ABC-DEF0-1234-5678-9ABCDEF01234");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => IconProvider.Load("AssemblyState.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Ordered Meshes", "M",
                "Block meshes in build order. Pipe in the Ordered Meshes " +
                "output from Block Build Order.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Step", "S",
                "Current step. 0 = nothing built yet; N = everything " +
                "built. Values outside [0, N] are clamped.",
                GH_ParamAccess.item, 0);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Built", "Mb",
                "Meshes placed at or before Step (indices 0..step-1).",
                GH_ParamAccess.list);
            p.AddMeshParameter("Pending", "Mp",
                "Meshes still to place (indices step..N-1).",
                GH_ParamAccess.list);
            p.AddMeshParameter("Current", "Mc",
                "The mesh placed at this step (index step-1). Empty when " +
                "Step == 0 (nothing built yet).",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Total", "N",
                "Total mesh count, for sizing the slider.",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            if (!da.GetDataList(0, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No ordered meshes provided.");
                return;
            }

            int step = 0;
            da.GetData(1, ref step);

            int n = meshes.Count;
            // Clamp to [0, N] without erroring — sliders pass arbitrary values.
            int s = step;
            if (s < 0) s = 0;
            if (s > n) s = n;

            var built = new List<Mesh>(s);
            var pending = new List<Mesh>(n - s);
            for (int i = 0; i < s; i++) built.Add(meshes[i]);
            for (int i = s; i < n; i++) pending.Add(meshes[i]);

            Mesh current = (s > 0 && s <= n) ? meshes[s - 1] : null;

            da.SetDataList(0, built);
            da.SetDataList(1, pending);
            if (current != null) da.SetData(2, current);
            da.SetData(3, n);
        }
    }
}
