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
    // JitteredGridFracturePlanesComponent — orthogonal grid with per-plane
    // random offset along its normal. Adds variation to a regular grid
    // without losing the "axis-aligned" feel.
    //
    // ComponentGuid: CBECFDAE-BFCA-4678-9ABC-DEF012345678
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Jittered Grid Fracture Planes.
    /// </summary>
        [DesignApplication(
        "Orthogonal grid of FracturePlanes with each plane jittered  by up to (jitter * cellStep) along its normal",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original grid + per-cell jitter perturbation")]
    [Algorithm("Grid with per-plane offset jitter", "Frahan-original",
        Note = "grid plus uniform per-plane normal offset; not a published sampler")]
    public sealed class JitteredGridFracturePlanesComponent : FrahanComponentBase
    {
        public JitteredGridFracturePlanesComponent()
            : base(
                "Jittered Grid Fracture Planes", "JitGridFx",
                "Orthogonal grid of FracturePlanes with each plane jittered " +
                "by up to (jitter * cellStep) along its normal. Deterministic " +
                "for a given Seed. Frahan-original method.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("CBECFDAE-BFCA-4678-9ABC-DEF012345678");

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Slab DTO whose bounding box seeds the grid.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("nX", "nX", "Planes perpendicular to +X (>= 0).", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("nY", "nY", "Planes perpendicular to +Y (>= 0).", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("nZ", "nZ", "Planes perpendicular to +Z (>= 0).", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Jitter", "J",
                "Per-plane offset jitter as a fraction of the cell step. In [0, 0.5).",
                GH_ParamAccess.item, 0.25);
            p.AddIntegerParameter("Seed", "Seed", "Random seed.", GH_ParamAccess.item, 12345);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs.",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            object rawSlab = null;
            int nX = 1, nY = 0, nZ = 1, seed = 12345;
            double jitter = 0.25;
            if (!da.GetData(0, ref rawSlab))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slab provided.");
                return;
            }
            da.GetData(1, ref nX); da.GetData(2, ref nY); da.GetData(3, ref nZ);
            da.GetData(4, ref jitter); da.GetData(5, ref seed);

            Slab slab = UnwrapSlab(rawSlab);
            if (slab == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Slab is not a Slab.");
                return;
            }

            try
            {
                var box = BoundingBox3.FromSlab(slab);
                var planes = FracturePlaneGenerators.JitteredGrid(box, nX, nY, nZ, jitter, seed);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Jittered grid generation failed: {ex.Message}");
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
