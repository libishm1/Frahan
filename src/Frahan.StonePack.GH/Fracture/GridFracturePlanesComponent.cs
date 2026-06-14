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
    // GridFracturePlanesComponent — produces an orthogonal grid of fracture
    // planes inside a Slab's bounding box (or a user-supplied box). Wires
    // into Slab Cut By Fractures (Frahan Cut / Slab subcategory) or directly
    // into Quarry Decompose.
    //
    // ComponentGuid: E6F7A8B9-CADB-4CDE-F012-345678901234
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Fracture &gt; Grid Fracture Planes.
    /// </summary>
        [DesignApplication(
        "Produces an orthogonal grid of FracturePlanes inside the  bounding box of the input Slab",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original orthogonal grid fracture set")]
    [Algorithm("Orthogonal grid fracture set", "Frahan-original",
        Note = "axis-aligned grid generator; no canonical published source")]
    public sealed class GridFracturePlanesComponent : FrahanComponentBase
    {
        public GridFracturePlanesComponent()
            : base(
                "Grid Fracture Planes", "GridFx",
                "Produces an orthogonal grid of FracturePlanes inside the " +
                "bounding box of the input Slab. nX/nY/nZ control how many " +
                "evenly-spaced planes are emitted along each axis. Frahan-original method.",
                "Frahan", "Fracture")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("E6F7A8B9-CADB-4CDE-F012-345678901234");

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Slab DTO whose bounding box seeds the grid.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("nX", "nX",
                "Number of planes perpendicular to +X. Must be >= 0.",
                GH_ParamAccess.item, 1);
            p.AddIntegerParameter("nY", "nY",
                "Number of planes perpendicular to +Y. Must be >= 0.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("nZ", "nZ",
                "Number of planes perpendicular to +Z. Must be >= 0.",
                GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Planes", "P",
                "FracturePlane DTOs. Wire into Slab Cut By Fractures or Quarry Decompose.",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            object rawSlab = null;
            int nX = 1, nY = 0, nZ = 1;
            if (!da.GetData(0, ref rawSlab))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slab provided.");
                return;
            }
            da.GetData(1, ref nX); da.GetData(2, ref nY); da.GetData(3, ref nZ);

            Slab slab = UnwrapSlab(rawSlab);
            if (slab == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Slab is not a Slab (got {DescribeType(rawSlab)}).");
                return;
            }

            try
            {
                var box = BoundingBox3.FromSlab(slab);
                var planes = FracturePlaneGenerators.Grid(box, nX, nY, nZ);
                da.SetDataList(0, planes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Grid generation failed: {ex.Message}");
            }
        }

        private static Slab UnwrapSlab(object raw)
        {
            if (raw is Slab direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is Slab fromWrap) return fromWrap;
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
