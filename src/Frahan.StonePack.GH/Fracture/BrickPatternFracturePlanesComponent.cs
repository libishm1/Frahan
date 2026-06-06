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
    // BrickPatternFracturePlanesComponent — orthogonal fracture set that
    // emulates a running-bond brick layout. Emits horizontal course
    // separators (Z-orthogonal) plus vertical X-orthogonal planes; alternate
    // courses are shifted by half a brick width.
    //
    // ComponentGuid: BADBECFD-AEBF-4567-89AB-CDEF01234567
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Brick-Pattern Fracture Planes.
    /// </summary>
        [DesignApplication(
        "Orthogonal fracture set emulating a running-bond brick  layout",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original running-bond brick-pattern emulation")]
    [Algorithm("Running-bond brick-pattern fracture set", "Frahan-original",
        Note = "encodes a running-bond masonry convention; vernacular craft, not a citable algorithm")]
    public sealed class BrickPatternFracturePlanesComponent : GH_Component
    {
        public BrickPatternFracturePlanesComponent()
            : base(
                "Brick-Pattern Fracture Planes", "BrickFx",
                "Orthogonal fracture set emulating a running-bond brick " +
                "layout. nX = vertical planes per course, nZ = horizontal " +
                "course separators. Frahan-original method.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("BADBECFD-AEBF-4567-89AB-CDEF01234567");

        protected override Bitmap Icon => IconProvider.Load("BondPattern.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Slab DTO whose bounding box seeds the brick grid.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("nX", "nX",
                "Vertical fracture planes per course. >= 0.",
                GH_ParamAccess.item, 3);
            p.AddIntegerParameter("nZ", "nZ",
                "Horizontal course separators. >= 0.",
                GH_ParamAccess.item, 3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs in a running-bond pattern.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            object rawSlab = null;
            int nX = 3, nZ = 3;
            if (!da.GetData(0, ref rawSlab))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slab provided.");
                return;
            }
            da.GetData(1, ref nX);
            da.GetData(2, ref nZ);

            Slab slab = UnwrapSlab(rawSlab);
            if (slab == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Slab is not a Slab.");
                return;
            }

            try
            {
                var box = BoundingBox3.FromSlab(slab);
                var planes = FracturePlaneGenerators.BrickPattern(box, nX, nZ);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Brick-pattern generation failed: {ex.Message}");
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
