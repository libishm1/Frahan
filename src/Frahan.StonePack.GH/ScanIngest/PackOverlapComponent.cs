#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.ScanIngest;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// PackOverlapComponent — Phase F5 (UX architecture report §7.7.C):
// per-stone vertex-in-other-mesh penetration diagnostic for 3D-packed
// stone scans. Cheap volumetric-overlap stand-in.
// =============================================================================

[DesignApplication(
    "For each placed stone, report the fraction of its vertices  that lie strictly inside another placed stone",
    DesignFlow.Bridges,
    Precedent = "Frahan-original mesh-overlap diagnostic")]
public sealed class PackOverlapComponent : GH_Component
{
    public PackOverlapComponent()
        : base("Per-Stone Overlap", "PackOverlap",
            "For each placed stone, report the fraction of its vertices " +
            "that lie strictly inside another placed stone. Useful as a " +
            "cheap penetration check after a 3D pack — anything > ~1% " +
            "indicates real overlap (mis-placement or solver bug).",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-2003-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("Pack3DNfp.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Placed Meshes", "M",
            "Placed stones after a 3D pack. Open meshes are skipped (no " +
            "inside / outside distinction).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Tolerance", "T",
            "Inside / outside testing tolerance in model units.",
            GH_ParamAccess.item, 1e-6);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Overlap Fractions", "O",
            "Per-stone fraction of vertices inside another stone, in [0, 1].",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Penetrating Ids", "P",
            "Indices of stones whose overlap fraction exceeds the warning " +
            "threshold (1% of vertices).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Max Overlap", "Mx",
            "Worst-case per-stone overlap fraction.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        double tol = 1e-6;
        if (!da.GetDataList(0, meshes) || meshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one mesh required.");
            return;
        }
        da.GetData(1, ref tol);

        double[] fractions;
        try
        {
            fractions = PackDiagnostics.PerStoneOverlap(meshes, tol);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var penetrating = new List<int>();
        double maxOv = 0.0;
        for (int i = 0; i < fractions.Length; i++)
        {
            if (fractions[i] > 0.01) penetrating.Add(i);
            if (fractions[i] > maxOv) maxOv = fractions[i];
        }

        da.SetDataList(0, fractions);
        da.SetDataList(1, penetrating);
        da.SetData(2, maxOv);

        if (penetrating.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{penetrating.Count} of {fractions.Length} stones penetrate another stone (>1% vertices inside).");
        }
    }
}
