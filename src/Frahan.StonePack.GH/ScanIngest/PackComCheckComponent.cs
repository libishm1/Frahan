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
// PackComCheckComponent — Phase F5 (UX architecture report §7.7.C):
// projects each placed stone's centroid onto the container and reports
// whether the CoM is inside the container's volume. Catches stones
// sticking out of a Mesh-bounded container (e.g. Pack3D Irregular
// Container output that solved but spilled).
// =============================================================================

[DesignApplication(
    "For each placed stone, report whether its centre of mass  (vertex centroid) lies inside the container",
    DesignFlow.Bridges,
    Precedent = "Heyman 1966 CoM-over-support-polygon check; ETH dry-stone stability discipline")]
    [Algorithm("Limit-state CoM-over-support",
        "Heyman, J. (1966), The Stone Skeleton, Int. J. Solids Struct. 2(2):249-279",
        Doi = "10.1016/0020-7683(66)90018-7",
        WikiPath = "wiki/index/references.md#Heyman1966LimitState")]
public sealed class PackComCheckComponent : FrahanComponentBase
{
    public PackComCheckComponent()
        : base("CoM In-Container Check", "PackComCheck",
            "For each placed stone, report whether its centre of mass " +
            "(vertex centroid) lies inside the container. Stones with " +
            "CoM outside the container are flagged as marginal — they " +
            "are likely to tip out of the pack. Stability per Heyman 1966 limit state.",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-2004-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("StabilityCheck.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Placed Meshes", "M",
            "Placed stones after a 3D pack.", GH_ParamAccess.list);
        p.AddMeshParameter("Container", "C",
            "Closed container mesh.", GH_ParamAccess.item);
        p.AddNumberParameter("Tolerance", "T",
            "Inside / outside testing tolerance in model units.",
            GH_ParamAccess.item, 1e-6);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Inside", "In",
            "Per-stone bool: true if CoM is inside the container.",
            GH_ParamAccess.list);
        p.AddPointParameter("Centres of Mass", "CoM",
            "Per-stone vertex-centroid points.", GH_ParamAccess.list);
        p.AddIntegerParameter("Marginal Ids", "Mr",
            "Indices of stones whose CoM lies outside the container.",
            GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        Mesh container = null;
        double tol = 1e-6;
        if (!da.GetDataList(0, meshes) || meshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one mesh required.");
            return;
        }
        if (!da.GetData(1, ref container) || container == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Container mesh required.");
            return;
        }
        da.GetData(2, ref tol);

        bool[] inside;
        Point3d[] coms;
        try
        {
            (inside, coms) = PackDiagnostics.CentreOfMassInContainer(meshes, container, tol);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var marginal = new List<int>();
        for (int i = 0; i < inside.Length; i++) if (!inside[i]) marginal.Add(i);

        da.SetDataList(0, inside);
        da.SetDataList(1, coms);
        da.SetDataList(2, marginal);

        if (marginal.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{marginal.Count} of {inside.Length} stones have CoM outside the container.");
        }
    }
}
