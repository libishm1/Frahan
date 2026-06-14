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
// StaggeredBlockDecomposeComponent — "Staggered Block Decompose" (Fabricate
// flagship).
//
// Split a sculpted / freeform stone form into staggered, running-bond-like
// blocks, each sized for wire-saw + 3-axis / robotic milling. This component
// produces the staggered CELL layout (Core StaggeredBlockLayout) over the form's
// bounding box and emits the cells as boxes + box-meshes + per-cell course index
// (ascending = build order).
//
// It deliberately does NOT fan out N RhinoCommon mesh booleans (the HITL failure
// mode on large slabs). To get form-fitted blocks, pipe Cell Meshes into the
// CGAL/geogram backend — Quarry Decompose By Mesh (CGAL) or Mesh CSG (CGAL) —
// which scales to many cuts. Compose, don't duplicate.
// =============================================================================

[DesignApplication(
    "Lay out staggered (running-bond) blocks over a sculpted form's  bounding box for wire-saw + robotic-mill fa...",
    DesignFlow.TopDown,
    Precedent = "Frahan flagship Fabricate niche; staggered masonry-like decomposition for wire-saw + robotic milling")]
[Algorithm("Running-bond staggered cell layout", "Frahan-original",
    Note = "Core StaggeredBlockLayout grid + brick-offset layout; running-bond is a masonry convention, not a citable algorithm")]
public sealed class StaggeredBlockDecomposeComponent : FrahanComponentBase
{
    public StaggeredBlockDecomposeComponent()
        : base("Staggered Block Decompose", "StaggerBlocks",
            "Lay out staggered (running-bond) blocks over a sculpted form's "
            + "bounding box for wire-saw + robotic-mill fabrication. Emits the "
            + "staggered cells (boxes + box meshes) + per-cell course index "
            + "(ascending = build order). Pipe Cell Meshes into Quarry Decompose "
            + "By Mesh (CGAL) / Mesh CSG (CGAL) for form-fitted blocks — this "
            + "component does NOT fire many RhinoCommon booleans (the HITL "
            + "large-slab failure mode). Frahan-original method.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D07A02-1A2B-4C3D-9E4F-5A6B7C8D9E02");
    protected override Bitmap Icon => IconProvider.Load("BondPattern.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Form", "M", "Sculpted / freeform stone mesh to decompose.", GH_ParamAccess.item);
        p.AddNumberParameter("Course Height", "Hc", "Course (layer) height along the up axis.", GH_ParamAccess.item, 0.3);
        p.AddNumberParameter("Block Length", "Lb", "Block length along the bond axis.", GH_ParamAccess.item, 0.5);
        p.AddNumberParameter("Stagger", "St", "Odd-course shift as a fraction of block length (0..1). Default 0.5 = running bond.", GH_ParamAccess.item, 0.5);
        p.AddIntegerParameter("Up Axis", "Up", "Course-stacking axis: 0=X, 1=Y, 2=Z (default).", GH_ParamAccess.item, 2);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBoxParameter("Cells", "C", "Staggered cell boxes.", GH_ParamAccess.list);
        p.AddMeshParameter("Cell Meshes", "Cm", "Cell boxes as meshes (feed into CGAL/geogram decompose).", GH_ParamAccess.list);
        p.AddIntegerParameter("Course", "Cr", "Per-cell course index (ascending = build order).", GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N", "Number of cells.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Layout summary + min/max cell size (wire-saw / mill feasibility).", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh form = null;
        double hc = 0.3, lb = 0.5, st = 0.5;
        int up = 2;
        if (!da.GetData(0, ref form) || form == null || !form.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid or missing Form mesh."); return; }
        da.GetData(1, ref hc); da.GetData(2, ref lb); da.GetData(3, ref st); da.GetData(4, ref up);
        if (hc <= 0 || lb <= 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Course Height and Block Length must be > 0."); return; }
        if (up < 0 || up > 2)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Up Axis must be 0, 1 or 2."); return; }

        BoundingBox bb = form.GetBoundingBox(true);
        IReadOnlyList<StaggeredCell> cells;
        try
        {
            cells = StaggeredBlockLayout.Build(
                bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z, hc, lb, st, up);
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

        var boxes = new List<Box>(cells.Count);
        var meshes = new List<Mesh>(cells.Count);
        var courses = new List<int>(cells.Count);
        double minVol = double.PositiveInfinity, maxVol = 0;
        double minEdge = double.PositiveInfinity, maxEdge = 0;
        int nCourses = 0;
        foreach (var c in cells)
        {
            var box = new Box(new BoundingBox(c.MinX, c.MinY, c.MinZ, c.MaxX, c.MaxY, c.MaxZ));
            boxes.Add(box);
            meshes.Add(Mesh.CreateFromBox(box, 1, 1, 1));
            courses.Add(c.Course);
            if (c.Course + 1 > nCourses) nCourses = c.Course + 1;
            double vol = c.SizeX * c.SizeY * c.SizeZ;
            if (vol < minVol) minVol = vol; if (vol > maxVol) maxVol = vol;
            double mn = Math.Min(c.SizeX, Math.Min(c.SizeY, c.SizeZ));
            double mx = Math.Max(c.SizeX, Math.Max(c.SizeY, c.SizeZ));
            if (mn < minEdge) minEdge = mn; if (mx > maxEdge) maxEdge = mx;
        }

        var inv = CultureInfo.InvariantCulture;
        string report =
            $"{cells.Count} staggered cells across {nCourses} course(s).\n"
            + $"Course height {hc.ToString("0.###", inv)}, block length {lb.ToString("0.###", inv)}, stagger {st.ToString("0.##", inv)}, up axis {up}.\n"
            + $"Cell volume range: {minVol.ToString("0.###", inv)} .. {maxVol.ToString("0.###", inv)}; edge range: {minEdge.ToString("0.###", inv)} .. {maxEdge.ToString("0.###", inv)}.\n"
            + "Pipe Cell Meshes into Quarry Decompose By Mesh (CGAL) or Mesh CSG (CGAL) for form-fitted blocks.";

        da.SetDataList(0, boxes);
        da.SetDataList(1, meshes);
        da.SetDataList(2, courses);
        da.SetData(3, cells.Count);
        da.SetData(4, report);
    }
}
