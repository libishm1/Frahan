using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core;
using Frahan.Surface;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Build a <see cref="FragmentDescriptor"/> from each of one or more closed
/// planar Rhino curves. Surfaces the descriptor as an opaque output plus
/// per-fragment area, perimeter, aspect ratio, and edge counts.
///
/// 2026-05-05: moved from "Frahan/2D Packing" to "Frahan/Analysis". The
/// unified solver now builds fragment descriptors internally when Boundary
/// Mode is on; this standalone component is kept for ad-hoc analysis and
/// debugging fragment edge geometry.
///
/// Spec 5; runbook section 16.1 component family "Frahan Fragment Descriptors".
/// </summary>
[DesignApplication(
    "Inspect per-fragment shape descriptors (area, perimeter, aspect, edges) before running the irregular-sheet packer.",
    DesignFlow.Bridges,
    Precedent = "Frahan-original fragment-shape descriptor extractor (spec 5; runbook §16.1)")]
[Algorithm("Fragment shape descriptor extraction", "Frahan-original",
    Note = "geometric descriptors are textbook quantities but the descriptor schema is Frahan-original (spec 5)",
    WikiPath = "wiki/algorithms/surface_mosaicing/descriptors/fragment_descriptor.md")]
public sealed class FragmentDescriptorsComponent : GH_Component
{
    public FragmentDescriptorsComponent()
        : base("Frahan Fragment Descriptors", "FragDesc",
            "Diagnostic component. Convert closed planar Rhino curves into " +
            "FragmentDescriptors with per-edge EdgeDescriptors. The unified " +
            "Frahan Sheet Pack now builds these internally when Boundary Mode " +
            "is on; use this standalone component to inspect descriptors for " +
            "ad-hoc analysis. Frahan-original method.",
            "Frahan", "Analysis")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C007-1A2B-4C3D-9E4F-5A6B7C8D9E07");
    protected override Bitmap? Icon => IconProvider.Load("FragmentCluster.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Fragments", "F", "Closed planar fragment curves.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Zone Buckets", "Z",
            "Per-fragment zone bucket. Defaults to all-zeros if omitted.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Discretisation Tolerance", "T",
            "Tolerance for ToPolyline conversion.", GH_ParamAccess.item, 0.01);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Descriptors", "D",
            "FragmentDescriptor per fragment (opaque).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Areas", "A", "Polygon area per fragment.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Perimeters", "P", "Perimeter per fragment.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Aspect Ratios", "Ar", "Aspect ratio per fragment.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Edge Counts", "E", "Edge count per fragment.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Skipped", "Sk", "Number of fragments skipped (degenerate).", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var fragments = new List<Curve>();
        var zoneBuckets = new List<int>();
        double tol = 0.01;

        if (!da.GetDataList(0, fragments) || fragments.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one fragment curve required.");
            return;
        }
        da.GetDataList(1, zoneBuckets);
        da.GetData(2, ref tol);

        var descriptors = new List<GH_ObjectWrapper>(fragments.Count);
        var areas = new List<double>(fragments.Count);
        var perims = new List<double>(fragments.Count);
        var aspects = new List<double>(fragments.Count);
        var edgeCounts = new List<int>(fragments.Count);
        int skipped = 0;

        for (int i = 0; i < fragments.Count; i++)
        {
            var curve = fragments[i];
            int zone = zoneBuckets.Count == 0
                ? 0
                : zoneBuckets[Math.Min(i, zoneBuckets.Count - 1)];

            FragmentDescriptor? frag = null;
            try
            {
                frag = FragmentDescriptorBuilder.BuildFromCurve(
                    id: $"frag-{i}", boundary: curve, zoneId: zone, discretisationTolerance: tol);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Fragment {i}: {ex.Message}");
            }

            if (frag == null)
            {
                skipped++;
                continue;
            }
            descriptors.Add(new GH_ObjectWrapper(frag));
            areas.Add(frag.Area);
            perims.Add(frag.Perimeter);
            aspects.Add(frag.AspectRatio);
            edgeCounts.Add(frag.EdgeCount);
        }

        da.SetDataList(0, descriptors);
        da.SetDataList(1, areas);
        da.SetDataList(2, perims);
        da.SetDataList(3, aspects);
        da.SetDataList(4, edgeCounts);
        da.SetData(5, skipped);
        da.SetData(6,
            $"FragmentDescriptors: {descriptors.Count} built, {skipped} skipped, " +
            $"tol={tol}");
    }
}
