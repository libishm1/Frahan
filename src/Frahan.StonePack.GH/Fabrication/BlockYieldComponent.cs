#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// BlockYieldComponent (D5F10055, Frahan > Fabricate)
//
// Maximise the usable-block yield when a raw quarry block is sawn into
// rectangular product blocks. Orients the raw block into the cut frame (from Cut
// Orientation, or world), then picks the block size within a tolerance band and
// the axis assignment that tiles the raw extents with the least off-cut waste,
// and emits the actual cut blocks + the yield / waste. Pairs with Cut Orientation
// (which sets the fabric-aligned grid) to complete the quarry-block cutting story.
// =============================================================================

[Algorithm("Block-yield optimisation", "Waste-minimising rectangular cutting; size flexes within tolerance to tile the raw extent",
    Note = "Per axis: pick n and size in [s-tol,s+tol] maximising n*size; best of 6 axis permutations.")]
[RelatedComponent("Frahan > Fabricate > Cut Orientation", Reason = "Supplies the fabric-aligned cut frame this fills with blocks.")]
[RelatedComponent("Frahan > Quarry > In-Situ Block Size", Reason = "Natural block-size distribution vs the sawn product size.")]
[RelatedComponent("Frahan > Fabricate > DXF Cut Plan", Reason = "Export the resulting block cut profiles to CAM.")]
public sealed class BlockYieldComponent : FrahanComponentBase
{
    public BlockYieldComponent()
        : base("Block Yield", "BlockYield",
            "Maximise usable-block yield sawing a raw quarry block into rectangular product blocks. Feed the raw block, " +
            "the cut frame (from Cut Orientation), a target product size and a size tolerance. It flexes the size within " +
            "tolerance and picks the axis assignment that tiles the block with least off-cut waste, and outputs the cut " +
            "blocks + yield / waste.",
            "Frahan", "Fabricate")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10055-ED9E-4ED9-A055-ED9EED9E0055");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("BlockYield.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBrepParameter("Raw block", "B", "The raw quarry block to saw.", GH_ParamAccess.item);
        p.AddPlaneParameter("Cut frame", "F", "Cut-grid orientation (from Cut Orientation). Default World XY.", GH_ParamAccess.item, Plane.WorldXY);
        p.AddVectorParameter("Target size", "S", "Target product block size (x,y,z) in model units.", GH_ParamAccess.item, new Vector3d(0.6, 0.4, 0.3));
        p.AddNumberParameter("Tolerance", "T", "Fractional size tolerance (+/-), e.g. 0.1 = +/-10%.", GH_ParamAccess.item, 0.1);
        p.AddNumberParameter("Kerf", "K", "Saw kerf / cut width (model units).", GH_ParamAccess.item, 0.006);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBrepParameter("Blocks", "Bl", "The yield-optimal cut blocks.", GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N", "Number of blocks.", GH_ParamAccess.item);
        p.AddVectorParameter("Block size", "Sz", "The optimised block size (x,y,z).", GH_ParamAccess.item);
        p.AddNumberParameter("Yield", "Y", "Usable volume / raw volume (0..1).", GH_ParamAccess.item);
        p.AddNumberParameter("Waste", "W", "Off-cut fraction (0..1).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Per-axis cut plan + yield.", GH_ParamAccess.item);
    }

    private const int MaxBlocks = 6000;

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Brep raw = null;
        if (!da.GetData(0, ref raw) || raw == null || !raw.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide a valid raw block Brep."); return; }
        Plane frame = Plane.WorldXY; da.GetData(1, ref frame);
        Vector3d target = new Vector3d(0.6, 0.4, 0.3); da.GetData(2, ref target);
        double tol = 0.1; da.GetData(3, ref tol);
        double kerf = 0.006; da.GetData(4, ref kerf);
        if (!frame.IsValid) frame = Plane.WorldXY;

        // raw extent in the cut frame
        var toFrame = Transform.PlaneToPlane(frame, Plane.WorldXY);
        var toWorld = Transform.PlaneToPlane(Plane.WorldXY, frame);
        var dup = raw.DuplicateBrep(); dup.Transform(toFrame);
        var bb = dup.GetBoundingBox(true);
        double lx = bb.Max.X - bb.Min.X, ly = bb.Max.Y - bb.Min.Y, lz = bb.Max.Z - bb.Min.Z;

        var res = BlockYieldOptimizer.Optimize(lx, ly, lz, target, tol, kerf);
        if (res.TotalBlocks == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No whole block fits; raw block smaller than the size band."); da.SetData(5, res.Report); return; }
        if (res.TotalBlocks > MaxBlocks)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{res.TotalBlocks} blocks exceeds the {MaxBlocks} preview cap; reporting numbers only."); }

        double sx = res.Size[0], sy = res.Size[1], sz = res.Size[2];
        var blocks = new List<Brep>();
        if (res.TotalBlocks <= MaxBlocks)
        {
            for (int i = 0; i < res.Count[0]; i++)
                for (int j = 0; j < res.Count[1]; j++)
                    for (int k = 0; k < res.Count[2]; k++)
                    {
                        double cx = bb.Min.X + i * (sx + kerf);
                        double cy = bb.Min.Y + j * (sy + kerf);
                        double cz = bb.Min.Z + k * (sz + kerf);
                        var box = new BoundingBox(new Point3d(cx, cy, cz), new Point3d(cx + sx, cy + sy, cz + sz));
                        var brep = box.ToBrep();
                        if (brep == null) continue;
                        brep.Transform(toWorld);
                        blocks.Add(brep);
                    }
        }

        da.SetDataList(0, blocks);
        da.SetData(1, res.TotalBlocks);
        da.SetData(2, new Vector3d(sx, sy, sz));
        da.SetData(3, res.Yield);
        da.SetData(4, res.Waste);
        da.SetData(5, res.Report);

        if (res.Waste > 0.25)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Waste {res.Waste * 100:F0}%: widen Tolerance or pick a size dividing the raw extents.");
    }
}
