#nullable disable
using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry;

// =============================================================================
// BoxToMeshComponent — adapter to bridge BlockCutOpt's Box[] outputs
// into mesh-consuming components like SlabFromMesh.
//
// Input: a single Box.
// Output: a closed Mesh of the box (8 vertices, 12 triangle faces).
//
// Why this exists: BlockCutOpt produces axis-aligned Box[] outputs
// (candidate bench-block footprints). Downstream components in the
// quarry chain — SlabFromMesh, SlabCutByFractures, BenchMonumentPacker,
// AshlarPack — consume Meshes. Without this adapter the canvas
// flow has to use Grasshopper's stock Mesh Box component, which
// produces a sub-divided mesh that BlockCutOpt downstreams choke on
// (extra interior vertices break planar-polygon extraction).
//
// This adapter always emits exactly the box-corner mesh: 8 vertices,
// 6 quad faces split into 12 triangles. Deterministic and minimal.
//
// ComponentGuid: D3E4F5A6-3004-4F5E-A6B7-C8D9E0F12345
//
// Frahan > Quarry > Mesh-prep helpers.
// =============================================================================

/// <summary>
/// Frahan &gt; Quarry &gt; Box To Mesh.
/// Convert a Box into a closed Mesh suitable for piping into
/// SlabFromMesh, SlabCutByFractures, and AshlarPack-style consumers.
/// </summary>
[DesignApplication(
    "Convert a Box (e.g",
    DesignFlow.Bridges,
    Precedent = "Frahan-original box-to-mesh utility")]
public sealed class BoxToMeshComponent : FrahanComponentBase
{
    public BoxToMeshComponent()
        : base("Box To Mesh", "Box2Mesh",
            "Convert a Box (e.g. a BlockCutOpt output) into a closed " +
            "Mesh (8 vertices, 12 triangles). Bridges the Box->Mesh " +
            "adapter gap between BlockCutOpt and SlabFromMesh / " +
            "SlabCutByFractures / AshlarPack.",
            "Frahan", "Block Cutting")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D3E4F5A6-3004-4F5E-A6B7-C8D9E0F12345");

    protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBoxParameter("Box", "B",
            "Input Box. Typically a single Box from BlockCutOpt's " +
            "Boxes output (graft, list-item, or as a single item).",
            GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M",
            "Closed mesh of the box. 8 vertices, 12 triangles.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Box box = Box.Unset;
        if (!da.GetData(0, ref box))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "No Box provided.");
            return;
        }
        if (!box.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Box is not valid.");
            return;
        }

        // Build the 8 corners in canonical order: bottom CCW then top CCW.
        var corners = box.GetCorners();
        // Box.GetCorners returns the 8 corners in a well-defined order
        // (counter-clockwise bottom face starting at the box's min, then
        // counter-clockwise top face). We map them to a closed triangular
        // mesh with outward-facing normals.

        var mesh = new Mesh();
        for (int i = 0; i < 8; i++)
        {
            mesh.Vertices.Add(corners[i]);
        }

        // Box.GetCorners convention:
        // 0 = (min.x, min.y, min.z) bottom-near-left
        // 1 = (max.x, min.y, min.z) bottom-near-right
        // 2 = (max.x, max.y, min.z) bottom-far-right
        // 3 = (min.x, max.y, min.z) bottom-far-left
        // 4 = (min.x, min.y, max.z) top-near-left
        // 5 = (max.x, min.y, max.z) top-near-right
        // 6 = (max.x, max.y, max.z) top-far-right
        // 7 = (min.x, max.y, max.z) top-far-left

        // Six faces, each as two triangles. Winding chosen so the normal
        // points OUTWARD.
        // Bottom (z = min) faces -Z.
        mesh.Faces.AddFace(0, 3, 2);
        mesh.Faces.AddFace(0, 2, 1);
        // Top (z = max) faces +Z.
        mesh.Faces.AddFace(4, 5, 6);
        mesh.Faces.AddFace(4, 6, 7);
        // Front (y = min) faces -Y.
        mesh.Faces.AddFace(0, 1, 5);
        mesh.Faces.AddFace(0, 5, 4);
        // Right (x = max) faces +X.
        mesh.Faces.AddFace(1, 2, 6);
        mesh.Faces.AddFace(1, 6, 5);
        // Back (y = max) faces +Y.
        mesh.Faces.AddFace(2, 3, 7);
        mesh.Faces.AddFace(2, 7, 6);
        // Left (x = min) faces -X.
        mesh.Faces.AddFace(3, 0, 4);
        mesh.Faces.AddFace(3, 4, 7);

        mesh.RebuildNormals();
        mesh.Compact();

        if (!mesh.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Resulting mesh is not valid.");
        }

        da.SetData(0, mesh);
    }
}
