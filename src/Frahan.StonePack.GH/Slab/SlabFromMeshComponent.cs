#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // SlabFromMeshComponent — wraps a Rhino Mesh into a Frahan.Masonry.Cutting
    // .Slab DTO. Mirrors the MasonryBlockComponent flat-coords + per-face index
    // convention but keeps quads as quads (Slab does not require pre-
    // triangulation; SlabCutter prefers the natural polygonal face).
    //
    // Sharp-tool note: Slab assumes CONVEX input. This component does NOT
    // verify convexity; that responsibility lives upstream (or with a future
    // dedicated convexity-check component).
    //
    // ComponentGuid: B1A2C3D4-5E6F-4789-9ABC-1D2E3F4A5B6C
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Slab From Mesh.
    /// Wraps a Rhino mesh into a <see cref="Slab"/> DTO suitable for
    /// downstream <see cref="SlabCutter"/> operations. Quads are preserved
    /// (Slab faces are polygons, not triangles).
    /// </summary>
        [DesignApplication(
        "Wraps a Rhino mesh into a Slab DTO",
        DesignFlow.Bridges,
        Precedent = "Frahan-original slab-extractor from input mesh")]
    public sealed class SlabFromMeshComponent : GH_Component
    {
        public SlabFromMeshComponent()
            : base(
                "Slab From Mesh", "Slab",
                "Wraps a Rhino mesh into a Slab DTO. Quads stay as quads. " +
                "Mesh must have at least 4 vertices and 4 faces. Slab assumes " +
                "the input is CONVEX; convexity is not verified here.",
                "Frahan", "Slab")
        {
        }

        // GUID literal: B1A2C3D4-5E6F-4789-9ABC-1D2E3F4A5B6C
        public override Guid ComponentGuid =>
            new Guid("B1A2C3D4-5E6F-4789-9ABC-1D2E3F4A5B6C");

        protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

        // ─── Params ─────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Rhino mesh defining a CONVEX polyhedral slab. Quads are " +
                "preserved as quads; triangles stay as triangles.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Slab", "S",
                "Slab DTO. Wire into Slab Cut By Fractures or downstream masonry.",
                GH_ParamAccess.item);
            p.AddMeshParameter("Mesh", "M",
                "Slab as a Rhino Mesh (re-emitted). Identical geometry to the " +
                "input Mesh but fan-triangulated from each polygonal face.",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Mesh mesh = null;

            if (!da.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }

            if (mesh.Vertices.Count < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh must have at least 4 vertices, got {mesh.Vertices.Count}.");
                return;
            }
            if (mesh.Faces.Count < 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh must have at least 4 faces, got {mesh.Faces.Count}.");
                return;
            }

            // Flatten vertex coords to [x0,y0,z0,x1,y1,z1,...].
            int vCount = mesh.Vertices.Count;
            var verts = new double[vCount * 3];
            for (int i = 0; i < vCount; i++)
            {
                var pt = mesh.Vertices[i];
                verts[i * 3 + 0] = pt.X;
                verts[i * 3 + 1] = pt.Y;
                verts[i * 3 + 2] = pt.Z;
            }

            // Build per-face index arrays. Quads stay as 4-vertex polygons,
            // triangles stay as 3-vertex polygons (Rhino MeshFace only ever
            // carries triangles or quads).
            var faces = new int[mesh.Faces.Count][];
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                if (face.IsQuad)
                {
                    faces[f] = new[] { face.A, face.B, face.C, face.D };
                }
                else
                {
                    faces[f] = new[] { face.A, face.B, face.C };
                }
            }

            Slab slab;
            try
            {
                slab = new Slab(verts, faces);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Slab construction failed: {ex.Message}");
                return;
            }

            da.SetData(0, slab);
            da.SetData(1, GhInterop.SlabToMesh(slab));
        }
    }
}
