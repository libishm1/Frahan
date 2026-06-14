#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.DataModel;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MasonryBlockComponent — wraps a Rhino Mesh into a Frahan.Masonry.DataModel
    // .MasonryBlock DTO. Keeps the geometry-runtime-agnostic flat double[] /
    // int[] convention that the Core uses (see MasonryBlock.cs).
    //
    // ComponentGuid: D4F8A1B2-2C3D-4E5F-9A1B-3C4D5E6F7A8B
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Masonry Block.
    /// Wraps a Rhino mesh into a <see cref="MasonryBlock"/> DTO suitable for
    /// downstream <see cref="MasonryAssembly"/> construction. Triangulates
    /// quad faces on the fly so callers don't have to.
    /// </summary>
        [DesignApplication(
        "Wraps a Rhino mesh into a MasonryBlock DTO",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original masonry-block DTO")]
    public sealed class MasonryBlockComponent : FrahanComponentBase
    {
        public MasonryBlockComponent()
            : base(
                "Masonry Block", "MasBlk",
                "Wraps a Rhino mesh into a MasonryBlock DTO. Quads are triangulated; " +
                "the mesh must have at least 3 vertices and at least one face.",
                "Frahan", "Masonry")
        {
        }

        // GUID is also written here as a comment so the smoke test can
        // cross-reference the literal: D4F8A1B2-2C3D-4E5F-9A1B-3C4D5E6F7A8B
        public override Guid ComponentGuid =>
            new Guid("D4F8A1B2-2C3D-4E5F-9A1B-3C4D5E6F7A8B");

        protected override Bitmap Icon => IconProvider.Load("MeshBvh.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Rhino mesh defining the block geometry. Quads are auto-triangulated.",
                GH_ParamAccess.item);
            p.AddTextParameter("Id", "I",
                "Optional stable identifier. If blank, a fresh GUID is assigned.",
                GH_ParamAccess.item, string.Empty);
            p[1].Optional = true;
            p.AddNumberParameter("Density", "D",
                "Material density (kg/m^3 or any consistent unit). Must be > 0.",
                GH_ParamAccess.item, 2400.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Block", "B",
                "MasonryBlock DTO. Wire into Masonry Assembly.",
                GH_ParamAccess.item);
            p.AddTextParameter("Id", "Id",
                "Block identifier (the value passed in, or the auto-generated " +
                "GUID if Id was blank). Wire into Auto Interfaces' Block Ids " +
                "input or Masonry Assembly's Fixed Block Ids input — keeps " +
                "block identity consistent across the canvas.",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            string id = string.Empty;
            double density = 2400.0;

            if (!da.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }
            da.GetData(1, ref id);
            da.GetData(2, ref density);

            if (mesh.Vertices.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh must have at least 3 vertices, got {mesh.Vertices.Count}.");
                return;
            }
            if (mesh.Faces.Count < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Mesh has no faces.");
                return;
            }
            if (density <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Density must be > 0, got {density}.");
                return;
            }

            // Use a fresh GUID when the user leaves Id blank (or wires whitespace).
            if (string.IsNullOrWhiteSpace(id))
                id = Guid.NewGuid().ToString("N");

            // Flatten vertex coords to [x0,y0,z0,x1,y1,z1,...].
            int vCount = mesh.Vertices.Count;
            var verts = new double[vCount * 3];
            for (int i = 0; i < vCount; i++)
            {
                var p = mesh.Vertices[i];
                verts[i * 3 + 0] = p.X;
                verts[i * 3 + 1] = p.Y;
                verts[i * 3 + 2] = p.Z;
            }

            // Triangulate faces into a flat int[] [i0,j0,k0,i1,j1,k1,...].
            // A quad becomes two triangles: (A,B,C) + (A,C,D), matching the
            // standard Rhino diagonal split in MeshFace.
            var triList = new List<int>(mesh.Faces.Count * 3);
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                triList.Add(face.A);
                triList.Add(face.B);
                triList.Add(face.C);
                if (face.IsQuad)
                {
                    triList.Add(face.A);
                    triList.Add(face.C);
                    triList.Add(face.D);
                }
            }
            var tris = triList.ToArray();

            MasonryBlock block;
            try
            {
                block = new MasonryBlock(id, verts, tris, density);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"MasonryBlock construction failed: {ex.Message}");
                return;
            }

            da.SetData(0, block);
            da.SetData(1, id);
        }
    }
}
