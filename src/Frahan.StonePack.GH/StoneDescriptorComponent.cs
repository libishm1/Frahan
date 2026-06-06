using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Surface;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Build a <see cref="StoneDescriptor"/> from each of one or more Rhino
/// meshes. Surfaces the descriptor as an opaque output (for downstream
/// Frahan packing components) plus per-stone volume, surface area, aspect
/// ratio, compactness, triangle count, and closed/manifold flags.
///
/// Counterpart to <see cref="FragmentDescriptorsComponent"/> for 3D stones.
/// Spec 7 section 4; runbook section 16.3 component family
/// "Frahan Stone Descriptor".
/// </summary>
[DesignApplication(
    "Convert Rhino meshes into StoneDescriptors",
    DesignFlow.Bridges,
    Precedent = "Frahan-original stone-descriptor extractor (shape + texture features)")]
[Algorithm("Stone shape-feature descriptor extractor", "Frahan-original",
    Note = "aggregates standard mesh measurements into a Frahan-specific descriptor (Spec 7 §4)")]
public sealed class StoneDescriptorComponent : GH_Component
{
    public StoneDescriptorComponent()
        : base("Frahan Stone Descriptor", "StoneDesc",
            "Convert Rhino meshes into StoneDescriptors. Output is consumable " +
            "by Frahan Pack3D and other 3D-packing tools. Frahan-original method.",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C009-1A2B-4C3D-9E4F-5A6B7C8D9E09");
    protected override Bitmap? Icon => IconProvider.Load("PackQuality.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Stones", "S", "Stone meshes (one descriptor per mesh).",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Ids", "I",
            "Per-stone id. Defaults to \"stone-{index}\" if omitted or shorter than the stone list.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Descriptors", "D",
            "StoneDescriptor per stone (opaque).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Mesh Volumes", "Vm",
            "Per-stone mesh volume (0 if mesh is open).", GH_ParamAccess.list);
        pManager.AddNumberParameter("AABB Volumes", "Va",
            "Per-stone axis-aligned bounding-box volume.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Surface Areas", "A",
            "Per-stone surface area.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Aspect Ratios", "Ar",
            "Per-stone aspect ratio (max/min AABB dimension; >= 1).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Compactness", "C",
            "Per-stone compactness (MeshVol / AabbVol; range (0, 1]).", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Triangle Counts", "T",
            "Per-stone triangle count.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Is Closed", "Cl",
            "Per-stone closed-mesh flag.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Is Manifold", "Mf",
            "Per-stone manifold flag.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Skipped", "Sk",
            "Number of stones skipped (null mesh or builder threw).", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        var ids = new List<string>();

        if (!da.GetDataList(0, meshes) || meshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one stone mesh required.");
            return;
        }
        da.GetDataList(1, ids);

        var descriptors = new List<GH_ObjectWrapper>(meshes.Count);
        var meshVols = new List<double>(meshes.Count);
        var aabbVols = new List<double>(meshes.Count);
        var areas = new List<double>(meshes.Count);
        var aspects = new List<double>(meshes.Count);
        var compactness = new List<double>(meshes.Count);
        var triCounts = new List<int>(meshes.Count);
        var closed = new List<bool>(meshes.Count);
        var manifold = new List<bool>(meshes.Count);
        int skipped = 0;

        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            if (mesh == null)
            {
                skipped++;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Stone {i}: null mesh, skipped.");
                continue;
            }
            string id = (ids.Count > i && !string.IsNullOrEmpty(ids[i]))
                ? ids[i]
                : $"stone-{i}";

            StoneDescriptor? stone = null;
            try
            {
                stone = StoneDescriptorBuilder.BuildFromMesh(id, mesh);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Stone {i}: {ex.Message}");
            }

            if (stone == null)
            {
                skipped++;
                continue;
            }

            descriptors.Add(new GH_ObjectWrapper(stone));
            meshVols.Add(stone.MeshVolume);
            aabbVols.Add(stone.AabbVolume);
            areas.Add(stone.SurfaceArea);
            aspects.Add(stone.AspectRatio);
            compactness.Add(stone.Compactness);
            triCounts.Add(stone.TriangleCount);
            closed.Add(stone.IsClosed);
            manifold.Add(stone.IsManifold);
        }

        da.SetDataList(0, descriptors);
        da.SetDataList(1, meshVols);
        da.SetDataList(2, aabbVols);
        da.SetDataList(3, areas);
        da.SetDataList(4, aspects);
        da.SetDataList(5, compactness);
        da.SetDataList(6, triCounts);
        da.SetDataList(7, closed);
        da.SetDataList(8, manifold);
        da.SetData(9, skipped);
        da.SetData(10,
            $"StoneDescriptors: {descriptors.Count} built, {skipped} skipped");
    }
}
