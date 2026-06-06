#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // LayeredFracturePlanesComponent — parallel planes equally spaced along
    // a chosen axis. Useful for sedimentary stones whose natural splitting
    // direction is layered.
    //
    // ComponentGuid: F8A9CADB-ECFD-4345-6789-012345678ABC
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Layered Fracture Planes.
    /// </summary>
        [DesignApplication(
        "Parallel planes equally spaced along the chosen axis",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original layered (sedimentary-like) fracture set")]
    [Algorithm("Parallel layered fracture set", "Frahan-original",
        Note = "evenly-spaced parallel planes; no canonical published source")]
    public sealed class LayeredFracturePlanesComponent : GH_Component
    {
        public LayeredFracturePlanesComponent()
            : base(
                "Layered Fracture Planes", "LayerFx",
                "Parallel planes equally spaced along the chosen axis. " +
                "Use Axis = 0 (X), 1 (Y), or 2 (Z). Common pattern for " +
                "sedimentary rocks. Frahan-original method.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F8A9CADB-ECFD-4345-6789-012345678ABC");

        protected override Bitmap Icon => IconProvider.Load("Stratigraphy.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Slab DTO whose bounding box seeds the layers.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Axis", "A",
                "Layer normal direction: 0 = X, 1 = Y, 2 = Z. Default 2 (Z).",
                GH_ParamAccess.item, 2);
            p.AddIntegerParameter("Count", "N",
                "Number of layers (interior cuts). Must be >= 0.",
                GH_ParamAccess.item, 3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs (parallel, evenly spaced).",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            object rawSlab = null;
            int axis = 2, count = 3;
            if (!da.GetData(0, ref rawSlab))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slab provided.");
                return;
            }
            da.GetData(1, ref axis);
            da.GetData(2, ref count);

            Slab slab = UnwrapSlab(rawSlab);
            if (slab == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Slab is not a Slab.");
                return;
            }

            try
            {
                var box = BoundingBox3.FromSlab(slab);
                var planes = FracturePlaneGenerators.Layered(box, axis, count);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Layered generation failed: {ex.Message}");
            }
        }

        private static Slab UnwrapSlab(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap) return fromWrap;
            return null;
        }
    }
}
