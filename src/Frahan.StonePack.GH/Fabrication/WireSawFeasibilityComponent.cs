#nullable disable
using System;
using System.Drawing;
using System.Linq;
using Frahan.Core.Fabrication;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;
using RGSurf = Rhino.Geometry.Surface;

namespace Frahan.GH.Fabrication;

// =============================================================================
// WireSawFeasibilityComponent (D5F10051, Frahan > Fabrication)
//
// Pre-CAM manufacturability check for a diamond wire-saw cut: is the target cut
// surface RULED (a straight tensioned wire can sweep it)? Is it DEVELOPABLE (a
// clean single pass)? It reports the verdict, the wire positions (rulings), the
// ruling twist, and emits the kerf-compensated toolpath surface
// (Delta=(D+delta)/2). This is the stone-specific pre-CAM validation no general
// CAM does -- it catches un-cuttable geometry before it reaches the machine.
// =============================================================================

[Algorithm("Wire-saw feasibility", "Ruled/developable surface test + kerf offset (JCDE 2024 robotic diamond-wire cutting)",
    Note = "Wire = straight ruling; sawable <=> ruled. Developable = clean single pass. Delta=(D+delta)/2.")]
[RelatedComponent("Frahan > Fabrication > Wire Saw Toolpath", Reason = "Feasibility gate upstream of toolpath generation.")]
[RelatedComponent("Frahan > Masonry > Trim Shell by Curves", Reason = "Check the trimmed shell's cut faces are wire-sawable.")]
public sealed class WireSawFeasibilityComponent : FrahanComponentBase
{
    public WireSawFeasibilityComponent()
        : base("Wire-Saw Feasibility", "WireSaw?",
            "Pre-CAM check: is a target cut surface wire-sawable? A tensioned wire is straight, so the cut must be a " +
            "RULED surface; a DEVELOPABLE ruled surface is a clean single pass. Reports the verdict, the wire positions, " +
            "ruling twist, and the kerf-compensated toolpath surface (Delta=(D+delta)/2). Feed one surface / Brep face.",
            "Frahan", "Fabrication")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10051-ED9E-4ED9-A051-ED9EED9E0051");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("StoneCutExport.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddSurfaceParameter("Surface", "S", "Target cut surface (or a single Brep face).", GH_ParamAccess.item);
        p.AddNumberParameter("Wire dia", "D", "Wire diameter (model units).", GH_ParamAccess.item, 0.003);
        p.AddNumberParameter("Vibration", "V", "Vibration / positioning error (model units).", GH_ParamAccess.item, 0.0005);
        p.AddNumberParameter("Tolerance", "T", "Ruling-straightness tolerance as a fraction of the surface diagonal.", GH_ParamAccess.item, 0.005);
        p.AddIntegerParameter("Samples", "N", "Grid samples per direction.", GH_ParamAccess.item, 12);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Wire-sawable", "W", "Planar or ruled -> a straight wire can sweep it.", GH_ParamAccess.item);
        p.AddBooleanParameter("Ruled", "R", "Surface is ruled (straight lines in one direction).", GH_ParamAccess.item);
        p.AddBooleanParameter("Developable", "Dv", "Gaussian curvature ~ 0 (clean single pass).", GH_ParamAccess.item);
        p.AddBooleanParameter("Planar", "Pl", "Surface is planar (trivial case).", GH_ParamAccess.item);
        p.AddNumberParameter("Max Gaussian", "K", "Max |Gaussian curvature| x diagonal^2 (0 = developable).", GH_ParamAccess.item);
        p.AddNumberParameter("Ruling dev", "Rd", "Max ruling deviation from straight (model units).", GH_ParamAccess.item);
        p.AddNumberParameter("Twist", "Tw", "Max twist between consecutive rulings (deg).", GH_ParamAccess.item);
        p.AddNumberParameter("Kerf", "Ko", "Kerf offset Delta = (D+V)/2 (model units).", GH_ParamAccess.item);
        p.AddLineParameter("Rulings", "Ru", "Successive wire positions (rulings).", GH_ParamAccess.list);
        p.AddSurfaceParameter("Toolpath", "Os", "Cut surface offset by the kerf (toolpath surface).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Feasibility verdict + metrics.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        RGSurf srf = null;
        if (!da.GetData(0, ref srf) || srf == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide a surface or single Brep face."); return; }
        double d = 0.003, v = 0.0005, tol = 0.005; int n = 12;
        da.GetData(1, ref d); da.GetData(2, ref v); da.GetData(3, ref tol); da.GetData(4, ref n);

        var res = WireSawFeasibility.Analyze(srf, d, v, tol, Math.Max(4, n));

        da.SetData(0, res.WireSawable);
        da.SetData(1, res.IsRuled);
        da.SetData(2, res.IsDevelopable);
        da.SetData(3, res.IsPlanar);
        da.SetData(4, res.MaxGaussianScaled);
        da.SetData(5, res.MaxRulingDeviation);
        da.SetData(6, res.MaxRulingTwistDeg);
        da.SetData(7, res.KerfOffset);
        da.SetDataList(8, res.Rulings);
        if (res.OffsetSurface != null) da.SetData(9, res.OffsetSurface);
        da.SetData(10, res.Report);

        if (!res.WireSawable)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Not wire-sawable (doubly-curved, non-ruled) -- see Report.");
        else if (!res.IsDevelopable && !res.IsPlanar)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Ruled but doubly-curved: wire twists, watch feed.");
    }
}
