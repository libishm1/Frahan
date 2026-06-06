#nullable disable
using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MeshAabbComponent — emit a mesh's axis-aligned bounding box dimensions
    // and a world-aligned plane at its min corner. Useful for verifying block
    // dimensions match a wall's expected course height, or as a sanity check
    // before feeding a quarry mesh into Quarry DFN.
    //
    // ComponentGuid: ABCDEF01-2345-6789-ABCD-EF0123456789
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Mesh &gt; Mesh AABB.
    /// Emits the axis-aligned bounding box of a mesh.
    /// </summary>
        [DesignApplication(
        "Axis-aligned bounding box of a mesh",
        DesignFlow.Bridges,
        Precedent = "Standard mesh AABB via Rhino BoundingBox")]
    public sealed class MeshAabbComponent : GH_Component
    {
        public MeshAabbComponent()
            : base(
                "Mesh AABB", "AABB",
                "Axis-aligned bounding box of a mesh. Outputs the box, its " +
                "X/Y/Z extents, and the centre point. Useful for verifying " +
                "block dimensions match a wall's expected course height.",
                "Frahan", "Mesh")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("ABCDEF01-2345-6789-ABCD-EF0123456789");

        protected override Bitmap Icon => IconProvider.Load("MeshBvh.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Input mesh.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBoxParameter("Box", "B",
                "Axis-aligned bounding box.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Width", "W",
                "Extent along +X (document units).",
                GH_ParamAccess.item);
            p.AddNumberParameter("Depth", "D",
                "Extent along +Y (document units).",
                GH_ParamAccess.item);
            p.AddNumberParameter("Height", "H",
                "Extent along +Z (document units).",
                GH_ParamAccess.item);
            p.AddPointParameter("Centre", "C",
                "Centre of the bounding box.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Mesh mesh = null;
            if (!da.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }

            var bbox = mesh.GetBoundingBox(true);
            var box = new Box(bbox);
            var size = bbox.Diagonal;

            da.SetData(0, box);
            da.SetData(1, size.X);
            da.SetData(2, size.Y);
            da.SetData(3, size.Z);
            da.SetData(4, bbox.Center);
        }
    }
}
