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
// Maximise the usable-block yield when a raw quarry block is sawn into rectangular
// product blocks, and (optionally) dodge internal fractures so the recovered
// blocks are SOUND. Orients the raw block into the cut frame (from Cut
// Orientation), flexes the block size within tolerance + picks the axis
// assignment for least off-cut waste, then slides the grid to fall between the
// supplied fractures. Outputs sound + flawed blocks and the sound yield.
// =============================================================================

[Algorithm("Block-yield optimisation", "Waste-minimising rectangular cutting; size flexes within tolerance to tile the raw extent",
    Note = "Per axis: pick n and size in [s-tol,s+tol] maximising n*size; best of 6 axis permutations.")]
[Algorithm("Fracture dodging", "Slide the grid phase to minimise fracture-straddled (unsound) blocks",
    Note = "A block crossed by a fracture plane is rejected; align the cut frame to the joints to cut fewer.")]
[RelatedComponent("Frahan > Fabricate > Cut Orientation", Reason = "Supplies the fabric-aligned cut frame this fills with blocks.")]
[RelatedComponent("Frahan > Quarry > In-Situ Block Size", Reason = "Natural block-size distribution vs the sawn product size.")]
[RelatedComponent("Frahan > Fabricate > DXF Cut Plan", Reason = "Export the resulting sound-block cut profiles to CAM.")]
public sealed class BlockYieldComponent : FrahanComponentBase
{
    public BlockYieldComponent()
        : base("Block Yield", "BlockYield",
            "Maximise usable-block yield sawing a raw quarry block into rectangular product blocks, and dodge internal " +
            "fractures. Feed the raw block, the cut frame (from Cut Orientation), a target product size, a size tolerance, " +
            "and optional fracture planes. Flexes the size + picks the axis assignment for least waste, then slides the " +
            "grid to fall between fractures. Outputs sound + flawed blocks and the sound yield.",
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
        p.AddPlaneParameter("Fractures", "Fr", "Optional fracture planes inside the block; blocks crossed by one are flawed. The grid dodges them.", GH_ParamAccess.list);
        p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBrepParameter("Blocks", "Bl", "The sound (defect-free) cut blocks.", GH_ParamAccess.list);
        p.AddBrepParameter("Flawed", "Fl", "Blocks straddled by a fracture (reject / down-grade).", GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N", "Number of sound blocks.", GH_ParamAccess.item);
        p.AddVectorParameter("Block size", "Sz", "The optimised block size (x,y,z).", GH_ParamAccess.item);
        p.AddNumberParameter("Yield", "Y", "Geometric yield: usable volume / raw volume (0..1).", GH_ParamAccess.item);
        p.AddNumberParameter("Sound yield", "Sy", "Sound-block volume / raw volume (0..1); = Yield if no fractures.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Per-axis cut plan + yield + fracture dodge.", GH_ParamAccess.item);
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
        var fractures = new List<Plane>(); da.GetDataList(5, fractures);
        if (!frame.IsValid) frame = Plane.WorldXY;

        var toFrame = Transform.PlaneToPlane(frame, Plane.WorldXY);
        var toWorld = Transform.PlaneToPlane(Plane.WorldXY, frame);
        var dup = raw.DuplicateBrep(); dup.Transform(toFrame);
        var bb = dup.GetBoundingBox(true);
        double lx = bb.Max.X - bb.Min.X, ly = bb.Max.Y - bb.Min.Y, lz = bb.Max.Z - bb.Min.Z;

        // fractures -> frame coords with the raw block at the origin
        List<Plane> fr = null;
        if (fractures.Count > 0)
        {
            fr = new List<Plane>();
            var toOrigin = Transform.Translation(-(Vector3d)bb.Min);
            foreach (var fp in fractures)
            {
                if (!fp.IsValid) continue;
                var q = fp; q.Transform(toFrame); q.Transform(toOrigin);
                fr.Add(q);
            }
        }

        var res = BlockYieldOptimizer.Optimize(lx, ly, lz, target, tol, kerf, fr, 8);
        if (res.TotalBlocks == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No whole block fits; raw block smaller than the size band."); da.SetData(6, res.Report); return; }
        if (res.TotalBlocks > MaxBlocks)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{res.TotalBlocks} blocks exceeds the {MaxBlocks} preview cap; reporting numbers only."); }

        double sx = res.Size[0], sy = res.Size[1], sz = res.Size[2];
        int ny = res.Count[1], nz = res.Count[2];
        var sound = new List<Brep>();
        var flawed = new List<Brep>();
        if (res.TotalBlocks <= MaxBlocks)
        {
            for (int i = 0; i < res.Count[0]; i++)
                for (int j = 0; j < res.Count[1]; j++)
                    for (int k = 0; k < res.Count[2]; k++)
                    {
                        double cx = bb.Min.X + res.Phase[0] + i * (sx + kerf);
                        double cy = bb.Min.Y + res.Phase[1] + j * (sy + kerf);
                        double cz = bb.Min.Z + res.Phase[2] + k * (sz + kerf);
                        var box = new BoundingBox(new Point3d(cx, cy, cz), new Point3d(cx + sx, cy + sy, cz + sz));
                        var brep = box.ToBrep();
                        if (brep == null) continue;
                        brep.Transform(toWorld);
                        int idx = (i * ny + j) * nz + k;
                        bool isSound = res.Sound.Length == 0 || (idx < res.Sound.Length && res.Sound[idx]);
                        (isSound ? sound : flawed).Add(brep);
                    }
        }

        da.SetDataList(0, sound);
        da.SetDataList(1, flawed);
        da.SetData(2, res.SoundBlocks);
        da.SetData(3, new Vector3d(sx, sy, sz));
        da.SetData(4, res.Yield);
        da.SetData(5, res.SoundYield);
        da.SetData(6, res.Report);

        if (res.FlawedBlocks > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{res.FlawedBlocks} flawed block(s) straddle a fracture; sound yield {res.SoundYield * 100:F0}%.");
        else if (res.Waste > 0.25)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Waste {res.Waste * 100:F0}%: widen Tolerance or pick a size dividing the raw extents.");
    }
}
