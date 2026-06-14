#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// Move to Origin (2026-05-28). Real quarry scans come in UTM coordinates
// (e.g. X~369000, Y~3.8M), so a Bench / reconstructed mesh built from them
// lands hundreds of kilometres from the Rhino origin ("flying somewhere").
// This recenters ANY geometry (mesh, point cloud, curves, blocks) to the
// origin as a group, and emits the applied Transform + its inverse so the
// result can be mapped back into world/UTM space later.
// =============================================================================

[RelatedComponent("Frahan > Mesh > Bench From Mesh", Reason = "Bench built from a UTM scan lands far from origin; recenter it here.")]
[RelatedComponent("Frahan > Mesh > Read LAS Cloud", Reason = "LAS/LAZ clouds are in real-world (UTM) coordinates.")]
[DesignApplication(
    "Recenter geometry (mesh / cloud / curves / blocks) to the world  origin as a group",
    DesignFlow.Bridges)]
public sealed class MoveToOriginComponent : FrahanComponentBase
{
    public MoveToOriginComponent()
        : base("Move to Origin", "ToOrigin",
            "Recenter geometry (mesh / cloud / curves / blocks) to the world " +
            "origin as a group. Fixes geometry built from UTM-coordinate scans " +
            "that lands far from the origin. Emits the applied Transform and its " +
            "inverse so you can map the result back into world space.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A03-1A2B-4C3D-9E4F-5A6B7C8D9E03");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGeometryParameter("Geometry", "G",
            "Geometry to recenter (any type; recentered together as one group).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Anchor", "A",
            "Which point maps to the target: 0 = bounding-box center, " +
            "1 = base (center XY, min Z) [good for 'set on the ground'], " +
            "2 = bounding-box min corner. Default 1.",
            GH_ParamAccess.item, 1);
        p.AddPointParameter("Target", "T",
            "Where the anchor lands. Default world origin (0,0,0).",
            GH_ParamAccess.item, Point3d.Origin);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGeometryParameter("Geometry", "G", "Recentered geometry.", GH_ParamAccess.list);
        p.AddTransformParameter("Transform", "X", "The translation applied (world -> origin).", GH_ParamAccess.item);
        p.AddTransformParameter("Inverse", "Xi", "Inverse translation (origin -> world); maps results back.", GH_ParamAccess.item);
        p.AddPointParameter("Anchor Point", "P", "The source anchor point (in world coords).", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var goos = new List<IGH_GeometricGoo>();
        int anchorMode = 1;
        Point3d target = Point3d.Origin;
        if (!da.GetDataList(0, goos) || goos.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No geometry provided.");
            return;
        }
        da.GetData(1, ref anchorMode);
        da.GetData(2, ref target);

        BoundingBox bb = BoundingBox.Empty;
        foreach (var g in goos)
        {
            if (g == null) continue;
            var b = g.Boundingbox;
            if (b.IsValid) bb.Union(b);
        }
        if (!bb.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not compute a valid bounding box from the input.");
            return;
        }

        Point3d anchor;
        switch (anchorMode)
        {
            case 0: anchor = bb.Center; break;
            case 2: anchor = bb.Min; break;
            default: anchor = new Point3d(bb.Center.X, bb.Center.Y, bb.Min.Z); break; // 1 = base
        }

        var move = Transform.Translation(target - anchor);
        var inverse = Transform.Translation(anchor - target);

        var moved = new List<IGH_GeometricGoo>(goos.Count);
        foreach (var g in goos)
        {
            if (g == null) { moved.Add(null); continue; }
            var dup = g.DuplicateGeometry();
            moved.Add(dup == null ? null : dup.Transform(move));
        }

        da.SetDataList(0, moved);
        da.SetData(1, move);
        da.SetData(2, inverse);
        da.SetData(3, anchor);
    }
}
