#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.ScanIngest;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// PackStabilityComponent — Phase F5 (UX architecture report §7.7.C):
// geometric pile-stability proxy. Catches obvious tip / unsupported-
// overhang failures without the cost of a full RBE solve.
// =============================================================================

[DesignApplication(
    "Geometric stability proxy for a 3D packed pile",
    DesignFlow.Bridges,
    Precedent = "Heyman 1966 limit-state masonry theorem; Frahan-original stability scorer")]
    [Algorithm("Limit-state CoM-over-support",
        "Heyman, J. (1966), The Stone Skeleton, Int. J. Solids Struct. 2(2):249-279",
        Doi = "10.1016/0020-7683(66)90018-7",
        WikiPath = "wiki/index/references.md#Heyman1966LimitState")]
public sealed class PackStabilityComponent : FrahanComponentBase
{
    public PackStabilityComponent()
        : base("Packed-Pile Stability", "PackStability",
            "Geometric stability proxy for a 3D packed pile. A stone is " +
            "marked stable when its centre of mass either rests inside " +
            "its own footprint on the floor, or lies inside the union of " +
            "the XY footprints of the stones it rests on. Quick check; " +
            "for full RBE physics use Frahan Masonry Stability (RBE). Stability per Heyman 1966 limit state.",
            "Frahan", "3D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-2005-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("StabilityCheck.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Placed Meshes", "M",
            "Placed stones after a 3D pack.", GH_ParamAccess.list);
        p.AddVectorParameter("Up", "U",
            "World up vector. Default world Z+.",
            GH_ParamAccess.item, Vector3d.ZAxis);
        p.AddNumberParameter("Floor Z", "Z0",
            "Z coordinate of the floor plane.",
            GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Z Tolerance", "Tz",
            "How close (in model units) a candidate supporter's top must " +
            "be to the supported stone's bottom for contact to count.",
            GH_ParamAccess.item, 1e-3);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Stable", "S",
            "Per-stone stability verdict.", GH_ParamAccess.list);
        p.AddIntegerParameter("Falling Ids", "F",
            "Indices of stones flagged unstable (CoM outside all supports).",
            GH_ParamAccess.list);
        p.AddBooleanParameter("All Stable", "OK",
            "True iff every stone passes.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var meshes = new List<Mesh>();
        Vector3d up = Vector3d.ZAxis;
        double floorZ = 0.0;
        double zTol = 1e-3;

        if (!da.GetDataList(0, meshes) || meshes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one mesh required.");
            return;
        }
        da.GetData(1, ref up);
        da.GetData(2, ref floorZ);
        da.GetData(3, ref zTol);

        bool[] stable;
        int[] falling;
        try
        {
            (stable, falling) = PackDiagnostics.PileStability(meshes, up, floorZ, zTol);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        bool allOk = falling.Length == 0;
        da.SetDataList(0, stable);
        da.SetDataList(1, falling);
        da.SetData(2, allOk);

        if (!allOk)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{falling.Length} of {stable.Length} stones are unstable (CoM unsupported).");
        }
    }
}
