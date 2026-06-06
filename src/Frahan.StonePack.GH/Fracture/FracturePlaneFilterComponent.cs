#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // FracturePlaneFilterComponent — drops fracture planes that don't intersect
    // the target slab's bounding box. SlabCutter handles missing planes
    // gracefully so this component is purely an optimisation: useful when
    // composing large fracture sets from multiple generators.
    //
    // ComponentGuid: DCFDAEBF-CADB-4789-ABCD-EF0123456789
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Fracture Plane Filter.
    /// </summary>
        [DesignApplication(
        "Drops planes that miss the target slab's bounding box",
        DesignFlow.TopDown,
        Precedent = "Frahan-original fracture-plane spatial / orientation filter")]
    public sealed class FracturePlaneFilterComponent : GH_Component
    {
        public FracturePlaneFilterComponent()
            : base(
                "Fracture Plane Filter", "FxFilter",
                "Drops planes that miss the target slab's bounding box. " +
                "Pre-cutting optimisation; SlabCutter handles non-intersecting " +
                "planes gracefully on its own.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("DCFDAEBF-CADB-4789-ABCD-EF0123456789");

        protected override Bitmap Icon => IconProvider.Load("DefectMap.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "Input FracturePlane DTOs (any source).",
                GH_ParamAccess.list);
            p.AddGenericParameter("Slab", "S",
                "Target Slab whose bounding box drives the filter.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "Filtered FracturePlane DTOs (only those intersecting the slab AABB).",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var rawPlanes = new List<object>();
            object rawSlab = null;
            if (!da.GetDataList(0, rawPlanes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No planes provided.");
                return;
            }
            if (!da.GetData(1, ref rawSlab))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slab provided.");
                return;
            }

            Slab slab = UnwrapSlab(rawSlab);
            if (slab == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Slab is not a Slab.");
                return;
            }

            var planes = new List<FracturePlane>(rawPlanes.Count);
            for (int i = 0; i < rawPlanes.Count; i++)
            {
                var p = UnwrapPlane(rawPlanes[i]);
                if (p == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Planes[{i}] is not a FracturePlane.");
                    return;
                }
                planes.Add(p);
            }

            try
            {
                var box = BoundingBox3.FromSlab(slab);
                var filtered = FracturePlaneGenerators.FilterToBox(planes, box);
                da.SetDataList(0, filtered);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Filter failed: {ex.Message}");
            }
        }

        private static Slab UnwrapSlab(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap) return fromWrap;
            return null;
        }

        private static FracturePlane UnwrapPlane(object raw)
        {
            if (raw is FracturePlane direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is FracturePlane fromWrap) return fromWrap;
            return null;
        }
    }
}
