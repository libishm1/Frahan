#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // QuarryDecomposeComponent — combines fracture-plane generation with
    // SlabCutter into a one-component pipeline: input convex quarry slab,
    // grid resolution, output cut-down slab inventory ready for Ashlar Pack.
    //
    // For non-grid DFNs, wire Grid / Random / Voronoi Fracture Planes
    // directly into Slab Cut By Fractures instead.
    //
    // ComponentGuid: B9CADBEC-FDAE-4F01-2345-678901234567
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Quarry &gt; Quarry Decompose (Grid).
    /// </summary>
    [Algorithm("Orthogonal-grid slab decomposition", "Frahan-original", Note = "Axis-aligned grid-of-planes cut via SlabCutter; no published algorithm.")]
        [DesignApplication(
        "Cuts a convex quarry Slab into a list of smaller convex Slabs  by an orthogonal grid of fracture planes",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original quarry decompose (DFN -> block candidates)")]
    public sealed class QuarryDecomposeComponent : GH_Component
    {
        public QuarryDecomposeComponent()
            : base(
                "Quarry Decompose", "QuarryDc",
                "Cuts a convex quarry Slab into a list of smaller convex Slabs " +
                "by an orthogonal grid of fracture planes. Output flows into " +
                "Ashlar Pack. Frahan-original method.",
                "Frahan", "Quarry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("B9CADBEC-FDAE-4F01-2345-678901234567");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Quarry", "Q",
                "Convex quarry. Accepts a Frahan Slab DTO (from Slab From Mesh) " +
                "OR a Rhino Mesh (auto-converted).",
                GH_ParamAccess.item);
            p.AddIntegerParameter("nX", "nX", "Grid count along +X (>= 0).",
                GH_ParamAccess.item, 4);
            p.AddIntegerParameter("nY", "nY", "Grid count along +Y (>= 0).",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("nZ", "nZ", "Grid count along +Z (>= 0).",
                GH_ParamAccess.item, 2);
            p.AddNumberParameter("Eps", "eps",
                "Cutter floating-point tolerance. Must be >= 0.",
                GH_ParamAccess.item, 1e-9);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Slabs", "S",
                "Output Slab DTOs. Wire into Ashlar Pack.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Parents", "Pi",
                "Per-output index back into the input list (always 0 for a " +
                "single-quarry call).",
                GH_ParamAccess.list);
            p.AddMeshParameter("Mesh", "M",
                "Output Slabs as Rhino Meshes (parallel to the Slab list).",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            object rawQuarry = null;
            int nX = 4, nY = 0, nZ = 2;
            double eps = 1e-9;
            if (!da.GetData(0, ref rawQuarry))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No quarry slab provided.");
                return;
            }
            da.GetData(1, ref nX); da.GetData(2, ref nY); da.GetData(3, ref nZ);
            da.GetData(4, ref eps);

            Slab quarry = GhInterop.UnwrapSlab(rawQuarry);
            if (quarry == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Quarry is not a Slab or Mesh (got {GhInterop.DescribeType(rawQuarry)}).");
                return;
            }

            try
            {
                var result = QuarryDecomposer.DecomposeByGrid(quarry, nX, nY, nZ, eps);
                da.SetDataList(0, result.Slabs);
                da.SetDataList(1, result.ParentIndices);
                da.SetDataList(2, GhInterop.SlabsToMeshes(result.Slabs));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Quarry decompose failed: {ex.Message}");
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
