#nullable disable
using System;
using System.Drawing;
using Frahan.Core.Quarry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry;

// =============================================================================
// BenchFromMeshComponent — Phase G helper (UX architecture report §7.8.B).
//
// Input: Mesh of the actual bench geometry (any non-rectangular shape).
// Output: Box (AABB of the mesh, ready to feed any existing BCO
// component's `Tested Area` / `Bench` input) + the original Mesh
// (ready to feed the matching ClipBoxesByMesh component for output-
// side half-space filtering) + a BenchBoundary opaque output for
// future BCO components that consume the wrapper directly.
//
// Wiring pattern (composition, no edits to existing BCO components):
//
//   Mesh ─→ BenchFromMesh ─┬─→ Box  ─→ BCOSolve.Tested Area
//                          ├─→ Mesh ─→ ClipBoxesByMesh.Mesh
//                          └─→ Bench (Generic) ─→ future BCO v2
//
//   BCOSolve.Boxes ─→ ClipBoxesByMesh.Boxes ─→ filtered Box[]
//
// Subcategory: Mesh (semantically a mesh-prep tool that produces a
// bench-input bundle).
// =============================================================================

[DesignApplication(
    "Derive an axis-aligned Box bench + carry the original Mesh  for use with the existing 11 BCO components (wh...",
    DesignFlow.Bridges,
    Precedent = "Frahan-original quarry-bench-from-scan utility")]
public sealed class BenchFromMeshComponent : FrahanComponentBase
{
    public BenchFromMeshComponent()
        : base("Bench From Mesh", "BenchFromMesh",
            "Derive an axis-aligned Box bench + carry the original Mesh " +
            "for use with the existing 11 BCO components (which take Box " +
            "inputs) and with ClipBoxesByMesh (which filters their Box[] " +
            "outputs). Closes the §7.8 mesh-bench gap without editing any " +
            "existing BCO component. Designed for non-rectangular quarry " +
            "benches: trapezoidal, stepped, polygonal, surveyed from a " +
            "DXF + bench-height extrusion, or produced by a Phase H " +
            "scan reconstruction.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("D3E4F5A6-3002-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M",
            "Bench mesh (any closed or open shape). The AABB is derived " +
            "from this for the Box output; the mesh itself is preserved " +
            "for downstream clipping.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Bench Height", "H",
            "Optional override: if > 0, the Box output's Z height is " +
            "extended to this value (e.g. when the user wants the bench " +
            "AABB to span the full block height even though the mesh " +
            "only covers the working face). Default 0 = use mesh AABB Z " +
            "as-is.",
            GH_ParamAccess.item, 0.0);
        p[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBoxParameter("Box", "B",
            "Axis-aligned bounding Box of the mesh (with optional " +
            "Bench Height override applied to Z). Wire to the existing " +
            "BCO components' `Tested Area` or `Bench` input.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Mesh", "M",
            "Pass-through of the input mesh, for downstream clipping.",
            GH_ParamAccess.item);
        p.AddGenericParameter("Bench Boundary", "BB",
            "Opaque BenchBoundary value (Box + Mesh combined). Future " +
            "BCO-v2 components consume this directly.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Vertex Count", "V",
            "Mesh vertex count (sanity check).", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh mesh = null;
        double benchHeight = 0.0;
        if (!da.GetData(0, ref mesh) || mesh == null || !mesh.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A valid Mesh input is required.");
            return;
        }
        da.GetData(1, ref benchHeight);

        BenchBoundary bb;
        try
        {
            bb = BenchBoundary.FromMesh(mesh);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var box = bb.Aabb;
        if (benchHeight > 0.0)
        {
            // Extend Z to the requested height (anchor at the AABB's Z min).
            var zNew = new Interval(box.Z.Min, box.Z.Min + benchHeight);
            box = new Box(box.Plane, box.X, box.Y, zNew);
            // Rebuild BenchBoundary with the new Box.
            bb = BenchBoundary.FromBoxAndMesh(box, mesh);
        }

        if (!mesh.IsClosed)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Mesh is not closed; ClipBoxesByMesh will fall back to AABB checks. " +
                "Run Mesh Repair upstream if you need true non-rectangular clipping.");
        }

        da.SetData(0, box);
        da.SetData(1, mesh);
        da.SetData(2, new GH_ObjectWrapper(bb));
        da.SetData(3, mesh.Vertices.Count);
    }
}
