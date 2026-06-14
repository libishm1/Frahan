#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Frahan.Core.Fabrication;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Fabrication;

// =============================================================================
// FabricationPrepReportComponent — "Fabrication Prep Report".
//
// Turn cut blocks into shop-floor handling facts: per-piece weight (volume x
// density), centroid, and a lift class (hand / two-person / mechanical / crane)
// so the crate + hoist + crane plan follows from the cut. Closes part of the
// fabrication-prep market gap. Assumes model units are METRES (so volume is m^3
// and weight is kg). Reuses RhinoCommon VolumeMassProperties + Core lift-class.
// =============================================================================

[DesignApplication(
    "Per-block weight (volume x density), centroid, and lift class  (hand <25 kg / two-person <50 / mechanical <...",
    DesignFlow.Bridges,
    Precedent = "Quarra MIT Out of Frame lecture 2025-10-24 SS11 -- lift class, CoM sphere abstraction, rigging engineering",
    Tolerance = "weight estimate within 5 % of computed volume x density; CoM within 1 % of bounding-box span",
    CardSet = "wiki/research/hitl_cards/br_fab_prep/")]
public sealed class FabricationPrepReportComponent : FrahanComponentBase
{
    public FabricationPrepReportComponent()
        : base("Fabrication Prep Report", "FabPrep",
            "Per-block weight (volume x density), centroid, and lift class "
            + "(hand <25 kg / two-person <50 / mechanical <2000 / crane) for the "
            + "crate + hoist plan. Assumes model units are metres. Default "
            + "density = 2700 kg/m3 (granite). Wire block meshes from Staggered "
            + "Block Decompose / Slab Cut.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D07A04-1A2B-4C3D-9E4F-5A6B7C8D9E04");
    protected override Bitmap Icon => IconProvider.Load("StockpileManager.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Blocks", "B", "Closed block meshes (e.g. from Staggered Block Decompose).", GH_ParamAccess.list);
        p.AddNumberParameter("Density", "D", "Stone density kg/m3 (default granite 2700).", GH_ParamAccess.item, FabricationReport.GraniteDensityKgM3);
        p.AddTextParameter("Ids", "Id", "Optional per-block ids (parallel to Blocks).", GH_ParamAccess.list);
        p[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Weights", "W", "Per-block weight (kg).", GH_ParamAccess.list);
        p.AddNumberParameter("Volumes", "V", "Per-block volume (m^3).", GH_ParamAccess.list);
        p.AddPointParameter("Centroids", "C", "Per-block centroid.", GH_ParamAccess.list);
        p.AddTextParameter("Lift Class", "L", "Per-block lift class.", GH_ParamAccess.list);
        p.AddNumberParameter("Total Weight", "T", "Sum of block weights (kg).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Summary + per-class counts.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var blocks = new List<Mesh>();
        double density = FabricationReport.GraniteDensityKgM3;
        var ids = new List<string>();
        if (!da.GetDataList(0, blocks) || blocks.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No block meshes provided."); return; }
        da.GetData(1, ref density);
        da.GetDataList(2, ids);
        if (density <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Density must be > 0."); return; }

        var weights = new List<double>(blocks.Count);
        var volumes = new List<double>(blocks.Count);
        var centroids = new List<Point3d>(blocks.Count);
        var classes = new List<string>(blocks.Count);
        int hand = 0, two = 0, mech = 0, crane = 0;
        double total = 0;
        bool anyOpen = false;

        for (int i = 0; i < blocks.Count; i++)
        {
            Mesh m = blocks[i];
            double vol = 0; Point3d c = Point3d.Origin;
            if (m != null && m.IsValid)
            {
                try
                {
                    var vmp = VolumeMassProperties.Compute(m);
                    if (vmp != null) { vol = Math.Abs(vmp.Volume); c = vmp.Centroid; }
                    if (!m.IsClosed) anyOpen = true;
                }
                catch { anyOpen = true; }
            }
            double w = FabricationReport.WeightKg(vol, density);
            LiftClass lc = FabricationReport.Classify(w);
            switch (lc) { case LiftClass.Hand: hand++; break; case LiftClass.TwoPerson: two++; break; case LiftClass.Mechanical: mech++; break; default: crane++; break; }
            weights.Add(w); volumes.Add(vol); centroids.Add(c); classes.Add(lc.ToString());
            total += w;
        }

        if (anyOpen)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "One or more blocks are not closed; their volume/weight may be unreliable. Sanitize / Close Holes first.");

        var inv = CultureInfo.InvariantCulture;
        string report =
            $"{blocks.Count} block(s); total {total.ToString("0.#", inv)} kg.\n"
            + $"Lift class: Hand {hand}, TwoPerson {two}, Mechanical {mech}, Crane {crane}.\n"
            + $"Density {density.ToString("0", inv)} kg/m3 (assumes model units = metres).";

        da.SetDataList(0, weights);
        da.SetDataList(1, volumes);
        da.SetDataList(2, centroids);
        da.SetDataList(3, classes);
        da.SetData(4, total);
        da.SetData(5, report);
    }
}
