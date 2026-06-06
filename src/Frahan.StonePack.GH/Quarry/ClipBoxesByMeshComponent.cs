#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Quarry;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry;

// =============================================================================
// ClipBoxesByMeshComponent — Phase G output-side filter (UX architecture
// report §6.7).
//
// Companion to BenchFromMeshComponent. Wired downstream of BCO
// components that emit Box[] grids (BCOExtract, HeteroExt, BCOMixedPack,
// BCOMixedPack3D, BCOWatershed.ZoneBoxes), it:
//   1. Filters out cells whose centre / corners lie outside the bench
//      Mesh (the cells the AABB algorithm wrongly claimed as winnable).
//   2. Recomputes the recovery fraction as
//      cells_inside_mesh / total_cells, which is the correct numerator
//      for non-rectangular benches.
//
// Closes the §6.7 "recovery over-counting" friction itemised in the
// UX architecture report.
// =============================================================================

[Algorithm("Mesh-containment box filter", "Frahan-original", Note = "Point-in-mesh containment test via RhinoCommon half-space primitive")]
[DesignApplication(
    "Filter a Box[] grid from BCO output by mesh-boundary  containment",
    DesignFlow.Bridges,
    Precedent = "Frahan-original mesh-clip utility")]
public sealed class ClipBoxesByMeshComponent : GH_Component
{
    public ClipBoxesByMeshComponent()
        : base("Clip Boxes By Mesh", "ClipBoxesByMesh",
            "Filter a Box[] grid from BCO output by mesh-boundary " +
            "containment. Drops cells that lie outside the actual bench " +
            "(the cells the AABB algorithm wrongly claimed as winnable). " +
            "Use after BCOExtract / HeteroExt / BCOMixedPack to get the " +
            "true recovery on a non-rectangular bench. " +
            "Frahan-original method.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("D3E4F5A6-3003-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("QuarryCutOpt.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBoxParameter("Boxes", "B",
            "Box[] from a BCO output (Prime Boxes, Mixed Boxes, Zone " +
            "Boxes, etc.).",
            GH_ParamAccess.list);
        p.AddMeshParameter("Mesh Bench", "M",
            "Closed mesh of the actual bench geometry. Wire from " +
            "BenchFromMesh.Mesh.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Inside Fraction Threshold", "Tf",
            "A box is kept when at least this fraction of its 8 corners " +
            "lie inside the mesh. 0 = keep all (back-compat); 0.5 = " +
            "majority must be inside; 1.0 = entire box must be inside.",
            GH_ParamAccess.item, 0.5);
        p.AddNumberParameter("Tolerance", "T",
            "Inside/outside testing tolerance in model units.",
            GH_ParamAccess.item, 1e-6);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBoxParameter("Inside Boxes", "In",
            "Cells whose containment fraction meets or exceeds the " +
            "threshold.",
            GH_ParamAccess.list);
        p.AddBoxParameter("Outside Boxes", "Out",
            "Cells that fall below the threshold (the AABB algorithm " +
            "wrongly claimed these).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Inside Count", "Ni",
            "Number of cells inside the mesh.", GH_ParamAccess.item);
        p.AddIntegerParameter("Outside Count", "No",
            "Number of cells dropped.", GH_ParamAccess.item);
        p.AddNumberParameter("Corrected Recovery", "Rc",
            "Inside / total fraction in [0, 1]. Apply this multiplicatively " +
            "to recovery numbers reported by the BCO components when they " +
            "ran on the same AABB grid.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Human-readable summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var boxes = new List<Box>();
        Mesh mesh = null;
        double threshold = 0.5;
        double tolerance = 1e-6;

        if (!da.GetDataList(0, boxes) || boxes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one Box required.");
            return;
        }
        if (!da.GetData(1, ref mesh) || mesh == null || !mesh.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh Bench input required.");
            return;
        }
        da.GetData(2, ref threshold);
        da.GetData(3, ref tolerance);
        if (threshold < 0.0 || threshold > 1.0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Inside Fraction Threshold must be in [0, 1]; got {threshold}.");
            return;
        }

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

        var inside = new List<Box>(boxes.Count);
        var outside = new List<Box>();
        for (int i = 0; i < boxes.Count; i++)
        {
            if (bb.ContainsBox(boxes[i], threshold, tolerance))
                inside.Add(boxes[i]);
            else
                outside.Add(boxes[i]);
        }

        double correctedRecovery = boxes.Count > 0 ? (double)inside.Count / boxes.Count : 0.0;

        da.SetDataList(0, inside);
        da.SetDataList(1, outside);
        da.SetData(2, inside.Count);
        da.SetData(3, outside.Count);
        da.SetData(4, correctedRecovery);
        da.SetData(5,
            $"Kept {inside.Count} of {boxes.Count} cells " +
            $"({correctedRecovery:P2}); dropped {outside.Count} as outside-mesh. " +
            $"Threshold = {threshold:F2}.");

        if (!mesh.IsClosed)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Mesh is not closed; containment falls back to AABB checks. " +
                "Run Mesh Repair upstream for true non-rectangular clipping.");
        }
        if (outside.Count > 0 && correctedRecovery < 0.95)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"AABB grid over-counted by {(1.0 - correctedRecovery):P1}. " +
                "Multiply BCO recovery numbers by the Corrected Recovery output.");
        }
    }
}
