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
    // RandomFracturePlanesComponent — N planes whose points lie inside the
    // input slab's bounding box and whose normals are uniform on the sphere.
    // Deterministic given a seed.
    //
    // ComponentGuid: F7A8B9CA-DBEC-4DEF-0123-456789012345
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Random Fracture Planes.
    /// </summary>
        [DesignApplication(
        "Produces N FracturePlanes with points inside the slab's  bounding box and normals uniform on the sphere",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original random / Poisson fracture set")]
    [Algorithm("Random plane placement", "Frahan-original",
        Note = "uniform sphere-pick (Marsaglia 1972) for normals; no Poisson-disk rejection")]
    public sealed class RandomFracturePlanesComponent : GH_Component
    {
        public RandomFracturePlanesComponent()
            : base(
                "Random Fracture Planes", "RandFx",
                "Produces N FracturePlanes with points inside the slab's " +
                "bounding box and normals uniform on the sphere. Deterministic " +
                "for a given Seed. Frahan-original method.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F7A8B9CA-DBEC-4DEF-0123-456789012345");

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Slab DTO whose bounding box seeds the random plane points.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Count", "N",
                "Number of random planes. Must be >= 0.",
                GH_ParamAccess.item, 4);
            p.AddIntegerParameter("Seed", "Seed",
                "Random seed for reproducibility.",
                GH_ParamAccess.item, 12345);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            object rawSlab = null;
            int count = 4, seed = 12345;
            if (!da.GetData(0, ref rawSlab))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slab provided.");
                return;
            }
            da.GetData(1, ref count); da.GetData(2, ref seed);

            Slab slab = UnwrapSlab(rawSlab);
            if (slab == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Slab is not a Slab.");
                return;
            }

            try
            {
                var box = BoundingBox3.FromSlab(slab);
                var planes = FracturePlaneGenerators.Random(box, count, seed);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Random generation failed: {ex.Message}");
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
