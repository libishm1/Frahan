#nullable disable
using System;
using System.Drawing;
using Frahan.Core.Sculpt;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Sculpt;

// =============================================================================
// EnlargeSculptureComponent — "Enlarge Sculpture" (digital pointing machine).
//
// The pointing machine is the sculptor's classic tool for transferring + SCALING
// a maquette to a full-size carving. Digital equivalent: take a scanned maquette
// mesh and enlarge it to the final size, by factor / target longest dim / target
// height / non-uniform XYZ. Grows from the base by default (plinth stays
// grounded). Feed the result into Fit In Block to verify a raw block can hold it.
// =============================================================================

[DesignApplication(
    "Digital pointing-machine scaling: enlarge a scanned maquette mesh to  a target size (Mode 0 factor, 1 targe...",
    DesignFlow.TopDown,
    Precedent = "Borrowed Earth Wood Ridge contour-sculpture pipeline (CEU 2026-03-05 SS5); heritage scan upscaling tradition")]
[Algorithm("Parametric enlargement", "Frahan-original",
    Note = "digital pointing-machine tradition; affine scale, not a published algorithm")]
public sealed class EnlargeSculptureComponent : GH_Component
{
    public EnlargeSculptureComponent()
        : base("Enlarge Sculpture", "Enlarge",
            "Digital pointing-machine scaling: enlarge a scanned maquette mesh to "
            + "a target size (Mode 0 factor, 1 target-longest, 2 target-height, 3 "
            + "non-uniform XYZ). Scales from the base centre by default so a plinth "
            + "stays grounded. Wire the output into Fit In Block. Frahan-original method (digital pointing-machine; affine scale-from-base).",
            "Frahan", "Sculpt")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D06A01-1A2B-4C3D-9E4F-5A6B7C8D9E01");
    protected override Bitmap Icon => IconProvider.Load("MorphCorrect.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Scanned maquette mesh.", GH_ParamAccess.item);
        p.AddIntegerParameter("Mode", "Mo",
            "0 = Factor, 1 = Target Longest, 2 = Target Height (Z), 3 = Non-uniform XYZ.",
            GH_ParamAccess.item, 0);
        p.AddNumberParameter("Value", "V",
            "Mode 0: scale factor. Mode 1: target longest dimension. Mode 2: target "
            + "height. Ignored in Mode 3.",
            GH_ParamAccess.item, 2.0);
        p.AddVectorParameter("Target XYZ", "T",
            "Mode 3 only: target size on each axis (model units).",
            GH_ParamAccess.item, Vector3d.Zero);
        p[3].Optional = true;
        p.AddPointParameter("Anchor", "A",
            "Scale origin. Default: base centre (bbox centre X/Y at min Z).",
            GH_ParamAccess.item);
        p[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Mesh", "M", "Enlarged mesh.", GH_ParamAccess.item);
        p.AddVectorParameter("Scale Factors", "F", "Per-axis scale factors applied.", GH_ParamAccess.item);
        p.AddVectorParameter("Final Size", "S", "Bounding size of the enlarged mesh (x,y,z).", GH_ParamAccess.item);
        p.AddNumberParameter("Volume", "Vol", "Volume of the enlarged mesh (0 if not closed).", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh mesh = null;
        if (!da.GetData(0, ref mesh) || mesh == null || !mesh.IsValid)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid or missing mesh."); return; }

        int mode = 0; double value = 2.0; Vector3d target = Vector3d.Zero;
        da.GetData(1, ref mode); da.GetData(2, ref value); da.GetData(3, ref target);

        BoundingBox bb = mesh.GetBoundingBox(true);
        Vector3d size = bb.Diagonal;
        double[] sizeArr = { size.X, size.Y, size.Z };
        if (sizeArr[0] <= 0 || sizeArr[1] <= 0 || sizeArr[2] <= 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh has zero extent on an axis."); return; }

        double[] factors;
        try
        {
            factors = SculptureFitter.EnlargeFactors(
                sizeArr, (EnlargeMode)mode, value, new[] { target.X, target.Y, target.Z });
        }
        catch (Exception ex)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

        if (factors[0] <= 0 || factors[1] <= 0 || factors[2] <= 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Non-positive scale factor (check Value / Target XYZ)."); return; }

        Point3d anchor = Point3d.Unset;
        if (!(da.GetData(4, ref anchor) && anchor.IsValid))
            anchor = new Point3d(bb.Center.X, bb.Center.Y, bb.Min.Z);

        var plane = new Plane(anchor, Vector3d.XAxis, Vector3d.YAxis);
        Transform xform = Transform.Scale(plane, factors[0], factors[1], factors[2]);
        Mesh outMesh = mesh.DuplicateMesh();
        outMesh.Transform(xform);

        Vector3d fs = outMesh.GetBoundingBox(true).Diagonal;
        double vol = 0.0;
        try
        {
            if (outMesh.IsClosed)
            {
                var vmp = VolumeMassProperties.Compute(outMesh);
                if (vmp != null) vol = Math.Abs(vmp.Volume);
            }
        }
        catch { }

        da.SetData(0, outMesh);
        da.SetData(1, new Vector3d(factors[0], factors[1], factors[2]));
        da.SetData(2, new Vector3d(fs.X, fs.Y, fs.Z));
        da.SetData(3, vol);

        if (Math.Abs(factors[0] - factors[1]) > 1e-9 || Math.Abs(factors[1] - factors[2]) > 1e-9)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Non-uniform scale: proportions changed.");
    }
}
