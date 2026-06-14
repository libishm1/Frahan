using System;
using System.Drawing;
using Frahan.Surface;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Exposes Frahan.Surface.MeshDiagnostics as a Grasshopper component. Reads a
/// Rhino Mesh and surfaces basic counts + manifold / closed flags + bounding
/// box volume + average edge length. Useful as a precondition check before
/// surface-pack BFF unwrap or 3D mesh-heightmap pack.
///
/// Runbook section 16.6 component family "Frahan Mesh Diagnostics".
/// </summary>
[Algorithm("Mesh quality diagnostics", "Frahan-original", Note = "Inspector; Botsch et al. 2010 Polygon Mesh Processing as the diagnostic-suite reference")]
[DesignApplication(
    "Read a Rhino Mesh and report vertex/face/triangle/quad counts,  IsClosed, IsManifold, HasConsistentWinding,...",
    DesignFlow.Bridges,
    Precedent = "Botsch et al. 2010 Polygon Mesh Processing diagnostic suite")]
public sealed class MeshDiagnosticsComponent : FrahanComponentBase
{
    public MeshDiagnosticsComponent()
        : base("Frahan Mesh Diagnostics", "MeshDiag",
            "Read a Rhino Mesh and report vertex/face/triangle/quad counts, " +
            "IsClosed, IsManifold, HasConsistentWinding, AverageEdgeLength, " +
            "BoundingBoxVolume. " +
            "Frahan-original method.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C005-1A2B-4C3D-9E4F-5A6B7C8D9E05");
    protected override Bitmap? Icon => IconProvider.Load("PackDiagnostics.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Mesh to diagnose.", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Vertex Count", "V", "Number of vertices.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Face Count", "F", "Number of faces.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Triangle Count", "T", "Number of triangular faces.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Quad Count", "Q", "Number of quad faces.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Is Closed", "Ic", "True if the mesh is closed (watertight).", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Is Manifold", "Im", "True if the mesh is manifold.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Has Consistent Winding", "Cw", "True if face windings are consistent.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Average Edge Length", "Ae", "Mean of all unique edge lengths.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Bounding Box Volume", "Bv", "Volume of axis-aligned bounding box.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Single-line summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh? mesh = null;
        if (!da.GetData(0, ref mesh) || mesh == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh input required.");
            return;
        }

        int v = MeshDiagnostics.VertexCount(mesh);
        int f = MeshDiagnostics.FaceCount(mesh);
        int t = MeshDiagnostics.TriangleCount(mesh);
        int q = MeshDiagnostics.QuadCount(mesh);
        bool closed = MeshDiagnostics.IsClosed(mesh);
        bool manifold = MeshDiagnostics.IsManifold(mesh);
        bool winding = MeshDiagnostics.HasConsistentWinding(mesh);
        double avg = MeshDiagnostics.AverageEdgeLength(mesh);
        double vol = MeshDiagnostics.BoundingBoxVolume(mesh);

        if (!manifold)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Mesh is not manifold; surface-pack BFF unwrap may fail.");
        if (!closed)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Mesh is not closed; this is OK for an open surface but a 3D mesh container needs Closed=true.");

        da.SetData(0, v);
        da.SetData(1, f);
        da.SetData(2, t);
        da.SetData(3, q);
        da.SetData(4, closed);
        da.SetData(5, manifold);
        da.SetData(6, winding);
        da.SetData(7, avg);
        da.SetData(8, vol);
        da.SetData(9,
            $"MeshDiagnostics: {v} verts, {f} faces ({t} tri + {q} quad), " +
            $"closed={closed}, manifold={manifold}, avgEdge={avg:0.###}, bboxVol={vol:0.##}");
    }
}
