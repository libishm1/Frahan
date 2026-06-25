#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Packing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // PackPreviewComponent — converts MasonryBlocks in an AshlarPackResult into
    // Rhino meshes (one per block) for canvas preview. The only file in this
    // packing subsystem that touches RhinoCommon types directly; lives in the
    // GH project where Rhino types are allowed.
    //
    // ComponentGuid: D5E6F7A8-B9CA-4BCD-EF01-234567890123
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Pack Preview.
    /// Builds one Rhino mesh per placed MasonryBlock for canvas preview.
    /// </summary>
        [DesignApplication(
        "Builds one Rhino mesh per placed block in an AshlarPackResult  for visual preview on the canvas",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original packing-visualisation helper")]
    public sealed class PackPreviewComponent : FrahanComponentBase
    {
        public PackPreviewComponent()
            : base(
                "Pack Preview", "PackPrev",
                "Builds one Rhino mesh per placed block in an AshlarPackResult " +
                "for visual preview on the canvas.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("D5E6F7A8-B9CA-4BCD-EF01-234567890123");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => IconProvider.Load("AssemblyState.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Result", "R",
                "AshlarPackResult from Ashlar Pack.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Meshes", "M",
                "One Rhino mesh per placed MasonryBlock.",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            object raw = null;
            if (!da.GetData(0, ref raw))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No result provided.");
                return;
            }
            AshlarPackResult result = UnwrapResult(raw);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Result is not an AshlarPackResult (got {DescribeType(raw)}).");
                return;
            }

            var meshes = new List<Mesh>(result.PlacedBlocks.Count);
            for (int i = 0; i < result.PlacedBlocks.Count; i++)
            {
                meshes.Add(BuildMesh(result.PlacedBlocks[i]));
            }
            da.SetDataList(0, meshes);
        }

        private static Mesh BuildMesh(MasonryBlock block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            var mesh = new Mesh();
            int n = block.VertexCount;
            for (int i = 0; i < n; i++)
            {
                mesh.Vertices.Add(
                    block.VertexCoordsXyz[3 * i + 0],
                    block.VertexCoordsXyz[3 * i + 1],
                    block.VertexCoordsXyz[3 * i + 2]);
            }
            int t = block.TriangleCount;
            for (int i = 0; i < t; i++)
            {
                mesh.Faces.AddFace(
                    block.TriangleIndices[3 * i + 0],
                    block.TriangleIndices[3 * i + 1],
                    block.TriangleIndices[3 * i + 2]);
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static AshlarPackResult UnwrapResult(object raw)
        {
            if (raw is AshlarPackResult direct) return direct;
            if (raw is GH_ObjectWrapper wrap && wrap.Value is AshlarPackResult fromWrap)
                return fromWrap;
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
