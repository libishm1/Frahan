#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Frahan.Core.ScanIngest;
using Frahan.Surface;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// StonePrepComponent — Phase F4 GH wrapper for StonePreparation.
// Subcategory: Mesh / secondary row. One-button cleanup pipeline.
// =============================================================================

[DesignApplication(
    "One-button cleanup pipeline for scanned stones:  Repair → optional Decimate → StoneDescriptor",
    DesignFlow.Bridges,
    Precedent = "Frahan-original scan-stone preprocessing pipeline")]
public sealed class StonePrepComponent : GH_Component
{
    public StonePrepComponent()
        : base("Stone Prep (Scan)", "StonePrep",
            "One-button cleanup pipeline for scanned stones: " +
            "Repair → optional Decimate → StoneDescriptor. Wraps the " +
            "existing Frahan repair + Rhino quadric decimation + Stone " +
            "Descriptor builder. Pure managed.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-2002-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("OutlierRemoval.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Meshes", "M",
            "Scanned stone meshes to clean.", GH_ParamAccess.list);
        p.AddTextParameter("Ids", "I",
            "Per-stone id. Missing entries default to \"stone-{index}\".",
            GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddBooleanParameter("Repair", "Rep",
            "Run the Frahan MeshRepair pipeline (cull degenerate, weld, " +
            "fill small holes).",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Decimate", "Dec",
            "Run quadric edge-collapse decimation to reach Target " +
            "Triangle Count (via RhinoCommon's managed Mesh.Reduce).",
            GH_ParamAccess.item, false);
        p.AddIntegerParameter("Target Triangle Count", "T",
            "Target triangle count for the Decimate stage. 0 disables " +
            "decimation regardless of the Decimate toggle.",
            GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Cleaned Meshes", "Mc",
            "Per-stone cleaned mesh after Repair (+ optional Decimate).",
            GH_ParamAccess.list);
        p.AddGenericParameter("Descriptors", "D",
            "StoneDescriptor per stone (opaque; consumable by Pack3D and " +
            "downstream Frahan 3D-packing tools).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Mesh Volumes", "Vm",
            "Per-stone signed mesh volume.", GH_ParamAccess.list);
        p.AddNumberParameter("Compactness", "C",
            "Per-stone compactness (MeshVolume / AabbVolume).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Triangle Counts", "Tc",
            "Per-stone triangle count after the pipeline.",
            GH_ParamAccess.list);
        p.AddTextParameter("Trace", "R",
            "Multi-line per-stone trace of every stage.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Skipped", "Sk",
            "Number of stones skipped (null mesh, builder threw, etc.).",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        var ids = new List<string>();
        bool repair = true;
        bool decimate = false;
        int target = 0;

        if (!da.GetDataList(0, meshes) || meshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one mesh required.");
            return;
        }
        da.GetDataList(1, ids);
        da.GetData(2, ref repair);
        da.GetData(3, ref decimate);
        da.GetData(4, ref target);

        StonePrepOptions options;
        try
        {
            options = new StonePrepOptions(
                repair: repair, decimate: decimate, targetTriangleCount: target);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var cleaned = new List<Mesh>(meshes.Count);
        var descriptors = new List<GH_ObjectWrapper>(meshes.Count);
        var meshVols = new List<double>(meshes.Count);
        var compactness = new List<double>(meshes.Count);
        var triCounts = new List<int>(meshes.Count);
        var traces = new List<string>(meshes.Count);
        int skipped = 0;

        for (int i = 0; i < meshes.Count; i++)
        {
            if (meshes[i] == null)
            {
                skipped++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Stone {i}: null mesh; skipped.");
                continue;
            }
            string id = (ids.Count > i && !string.IsNullOrEmpty(ids[i]))
                ? ids[i]
                : $"stone-{i}";

            StonePrepResult result;
            try
            {
                result = StonePreparation.Run(id, meshes[i], options);
            }
            catch (Exception ex)
            {
                skipped++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Stone {id}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            cleaned.Add(result.CleanedMesh);
            descriptors.Add(new GH_ObjectWrapper(result.Descriptor));
            meshVols.Add(result.Descriptor.MeshVolume);
            compactness.Add(result.Descriptor.Compactness);
            triCounts.Add(result.CleanedMesh.Faces.Count);
            traces.Add(StonePreparation.FormatTrace(result));
        }

        da.SetDataList(0, cleaned);
        da.SetDataList(1, descriptors);
        da.SetDataList(2, meshVols);
        da.SetDataList(3, compactness);
        da.SetDataList(4, triCounts);
        da.SetDataList(5, traces);
        da.SetData(6, skipped);
    }
}
