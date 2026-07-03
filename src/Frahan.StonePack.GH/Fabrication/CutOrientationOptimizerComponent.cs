#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core.Discontinuity;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Fabrication;

// =============================================================================
// CutOrientationOptimizerComponent (D5F10052, Frahan > Fabrication)
//
// The geology->fabrication decision component: given the quarry joint sets, find
// the orientation of a rectangular saw-cut grid that yields right-prism blocks
// (q=1) while following the natural joint fabric as closely as possible (cheap,
// clean splits). Reports the three optimal cut planes, which joint set each cut
// follows, and the unavoidable obliquity where the fabric is not orthogonal.
// =============================================================================

[Algorithm("Cut-orientation optimization", "Orthogonal saw grid vs joint fabric; maximise sum |cut.pole|",
    Note = "Right-prism blocks by construction; optimise fabric alignment. Bench-constrained (1 DOF) or free (SO(3)).")]
[RelatedComponent("Frahan > Quarry > In-Situ Block Size", Reason = "Right-prism fraction that this cut grid targets.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Cloud)", Reason = "Upstream source of the joint-set Dip / Dip dir.")]
[RelatedComponent("Frahan > Fabrication > Wire-Saw Feasibility", Reason = "Check each optimised cut plane is wire-sawable.")]
public sealed class CutOrientationOptimizerComponent : FrahanComponentBase
{
    public CutOrientationOptimizerComponent()
        : base("Cut Orientation", "CutOpt",
            "Optimise a rectangular saw-cut grid against the quarry joint fabric. Feed per-set Dip / Dip dir. Outputs " +
            "the three optimal cut planes (right-prism blocks by construction), which joint set each cut follows, and " +
            "the obliquity where the fabric is not orthogonal (the unavoidable oblique cut). Bench mode pins one cut to " +
            "the horizontal floor.",
            "Frahan", "Fabrication")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10052-ED9E-4ED9-A052-ED9EED9E0052");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        p.AddBooleanParameter("Bench", "B", "Bench-constrained: one cut = horizontal floor, optimise the vertical grid azimuth.", GH_ParamAccess.item, true);
        p.AddPointParameter("Center", "C", "Center for the preview cut rectangles.", GH_ParamAccess.item, Point3d.Origin);
        p.AddNumberParameter("Extent", "E", "Half-size of the preview cut rectangles (model units).", GH_ParamAccess.item, 5.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Cut planes", "P", "The three optimal orthogonal cut planes.", GH_ParamAccess.list);
        p.AddNumberParameter("Cut dip", "Cd", "Per-cut dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Cut dip dir", "Cdd", "Per-cut dip-direction (deg).", GH_ParamAccess.list);
        p.AddIntegerParameter("Follows set", "F", "Joint set (1-based) each cut follows; 0 if none.", GH_ParamAccess.list);
        p.AddNumberParameter("Obliquity", "Ob", "Per-cut angle to the nearest joint (deg; 0 = along a joint).", GH_ParamAccess.list);
        p.AddNumberParameter("Max obliquity", "Mo", "The unavoidable oblique cut (deg).", GH_ParamAccess.item);
        p.AddNumberParameter("Fit", "Ft", "Fabric-fit score (0..1).", GH_ParamAccess.item);
        p.AddCurveParameter("Cut rectangles", "R", "Preview rectangles in the three cut planes.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Re", "Optimizer summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var dip = new List<double>(); var dipdir = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || dip.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip and Dip dir lists."); return; }
        bool bench = true; da.GetData(2, ref bench);
        Point3d center = Point3d.Origin; da.GetData(3, ref center);
        double ext = 5.0; da.GetData(4, ref ext); if (ext <= 0) ext = 5.0;

        int n = Math.Min(dip.Count, dipdir.Count);
        var poles = new List<Vector3d>(n);
        for (int i = 0; i < n; i++) poles.Add(OrientationMath.NormalFromDipDipDir(dip[i], dipdir[i]));

        var res = CutOrientationOptimizer.Optimize(poles, bench);

        var planes = new List<Plane>(3);
        var rects = new List<Curve>(3);
        for (int i = 0; i < 3; i++)
        {
            var pl = new Plane(center, res.CutNormals[i]);
            planes.Add(pl);
            var rc = new Rectangle3d(pl, new Interval(-ext, ext), new Interval(-ext, ext));
            rects.Add(rc.ToNurbsCurve());
        }

        da.SetDataList(0, planes);
        da.SetDataList(1, res.CutDip.ToList());
        da.SetDataList(2, res.CutDipDir.ToList());
        da.SetDataList(3, res.FollowsSet.Select(f => f + 1).ToList());
        da.SetDataList(4, res.ObliquityDeg.ToList());
        da.SetData(5, res.MaxObliquityDeg);
        da.SetData(6, res.FitScore);
        da.SetDataList(7, rects);
        da.SetData(8, res.Report);

        if (res.MaxObliquityDeg > 20)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Max obliquity {res.MaxObliquityDeg:F0} deg: one cut runs oblique to the fabric.");
    }
}
