#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;
using Frahan.Core.Earthworks;

namespace Frahan.GH.Quarry;

// =============================================================================
// CleanScanMeshComponent -- GH adapter over Core TinPeelFilter (card A2).
//
// Scan reconstruction (Poisson / alpha / Delaunay) fills the convex hull, so a
// concave terrain / sparse boundary / data gap gets long thin "cap" triangles
// and near-vertical gap webs that are not real terrain. This peels them (median-
// edge / verticality / cap-angle, scale-relative) and drops tiny disconnected
// components, turning a raw reconstruction into a clean ground/bench TIN ready
// for Overburden To Rock Face. ComponentGuid: A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE03.
//
// Frahan > Quarry > scan cleanup.
// =============================================================================

/// <summary>Frahan &gt; Quarry &gt; Clean Scan Mesh. Peel cap/vertical/sliver
/// border triangles from a reconstructed scan TIN (wraps Core TinPeelFilter).</summary>
[RelatedComponent("Frahan > Quarry > Overburden To Rock Face", Reason = "Cleaned ground TIN feeds the overburden volume.")]
[RelatedComponent("Frahan > Ingest > Scan Reconstruct", Reason = "Cleans the over-triangulated reconstruction output.")]
[Algorithm("TIN border-peel terrain cleanup (median-edge / verticality / cap-angle + component size)",
    "Fade2D land-survey peelOffIf; scale-relative thresholds (GeometryNumerics T2)",
    Note = "Remove border facet if max edge^2 > (k*median)^2, or facet tilt > 85 deg, or opposite-border angle > 140 deg; then drop components < N triangles.")]
[DesignApplication(
    "Turn a raw quarry scan reconstruction into clean terrain for volume + face work",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original; geom.at / Fade2D land-survey peel logic")]
public sealed class CleanScanMeshComponent : GH_Component
{
    public CleanScanMeshComponent()
        : base("Clean Scan Mesh", "CleanTIN",
            "Peel long 'cap' triangles, near-vertical gap webs and slivers from a reconstructed " +
            "scan mesh, then drop tiny disconnected islands. Thresholds are relative to the median " +
            "edge length, so it works at any survey scale. Wraps Core TinPeelFilter (card A2).",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("A7E0B0F1-0C0F-4A16-9E3D-0FACE0FACE03");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Reconstructed scan mesh to clean.", GH_ParamAccess.item);
        p.AddNumberParameter("Long Edge k", "k",
            "Remove border triangles whose longest edge exceeds k * median edge (3 = aggressive, 10 = careful).",
            GH_ParamAccess.item, 3.0);
        p.AddNumberParameter("Max Tilt", "T",
            "Remove near-vertical border facets steeper than this (deg). Default 85.", GH_ParamAccess.item, 85.0);
        p.AddNumberParameter("Max Cap Angle", "A",
            "Remove border facets whose angle opposite the border edge exceeds this (deg). Default 140.",
            GH_ParamAccess.item, 140.0);
        p.AddIntegerParameter("Min Component", "N",
            "Drop connected components smaller than this many triangles. Default 50.", GH_ParamAccess.item, 50);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Clean Mesh", "M", "Peeled mesh (kept triangles only).", GH_ParamAccess.item);
        p.AddIntegerParameter("Peeled", "P", "Triangles removed by the peel predicate.", GH_ParamAccess.item);
        p.AddIntegerParameter("Size Dropped", "S", "Triangles dropped as tiny components.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rpt", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh mesh = null;
        double k = 3.0, tilt = 85.0, cap = 140.0;
        int minComp = 50;
        if (!da.GetData(0, ref mesh) || mesh == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh."); return; }
        da.GetData(1, ref k); da.GetData(2, ref tilt); da.GetData(3, ref cap); da.GetData(4, ref minComp);
        if (!mesh.IsValid || mesh.Faces.Count < 1)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh invalid or empty."); return; }

        // flatten to xyz + triangles (triangulate quads)
        var xyz = new double[mesh.Vertices.Count * 3];
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            xyz[3 * i] = v.X; xyz[3 * i + 1] = v.Y; xyz[3 * i + 2] = v.Z;
        }
        var tris = new List<int>(mesh.Faces.Count * 3);
        for (int f = 0; f < mesh.Faces.Count; f++)
        {
            var mf = mesh.Faces[f];
            tris.Add(mf.A); tris.Add(mf.B); tris.Add(mf.C);
            if (mf.IsQuad) { tris.Add(mf.A); tris.Add(mf.C); tris.Add(mf.D); }
        }

        TinPeelResult r;
        try
        {
            r = TinPeelFilter.Filter(xyz, tris, new TinPeelOptions
            {
                LongEdgeK = k, MaxFacetTiltDeg = tilt, MaxCapAngleDeg = cap, MinComponentTriangles = Math.Max(1, minComp)
            });
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "TinPeelFilter failed: " + ex.Message); return; }

        // rebuild mesh from kept triangles (reuse original vertices; cull unused via compaction)
        var outMesh = new Mesh();
        var remap = new Dictionary<int, int>();
        for (int t = 0; t < r.KeptTriangles.Count; t += 3)
        {
            int[] vi = new int[3];
            for (int c = 0; c < 3; c++)
            {
                int orig = r.KeptTriangles[t + c];
                if (!remap.TryGetValue(orig, out int nv))
                {
                    nv = outMesh.Vertices.Count;
                    outMesh.Vertices.Add(mesh.Vertices[orig]);
                    remap[orig] = nv;
                }
                vi[c] = nv;
            }
            outMesh.Faces.AddFace(vi[0], vi[1], vi[2]);
        }
        outMesh.Normals.ComputeNormals();
        outMesh.Compact();

        if (r.KeptCount == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "All triangles removed; loosen k / Min Component.");

        da.SetData(0, outMesh);
        da.SetData(1, r.RemovedByPeel);
        da.SetData(2, r.RemovedBySize);
        da.SetData(3, r.ToString());
    }
}
