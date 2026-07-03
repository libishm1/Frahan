#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core.Discontinuity;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// FractureIntensityComponent (D5F1004E, Frahan > Quarry)
//
// Reports the Dershowitz-Herda (1992) P_ij fracture-intensity family for a
// jointed rock mass, centred on P32 (fracture area per unit rock volume, 1/m) --
// the scale-independent measure a DFN is conditioned on, unlike raw counts.
// Feed the per-set Dip / Dip dir / Spacing (from Discontinuity Sets) and, if
// available, a scanline (for the expected P10) and a finite-fracture DFN
// (areas + volume, for a directly-measured P32/P30 cross-check).
// =============================================================================

[Algorithm("Fracture intensity P_ij", "Dershowitz & Herda (1992); P32 = sum 1/spacing (persistent)",
    Note = "P10=count/length, P21=trace-length/area, P32=area/volume; P32 is scale-independent.")]
[Algorithm("Scanline P10 -> P32", "Wang (2005); P10 = P32 |cos(angle scanline,pole)|",
    Note = "A scanline sub-parallel to a set under-counts it (the Terzaghi geometry factor).")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Cloud)", Reason = "Upstream source of per-set Dip/Dip dir/Spacing.")]
[RelatedComponent("Frahan > Quarry > Stereonet + Block Size", Reason = "Jv equals total P32 for persistent joints.")]
public sealed class FractureIntensityComponent : FrahanComponentBase
{
    public FractureIntensityComponent()
        : base("Fracture Intensity", "Intensity",
            "Dershowitz P_ij fracture intensity, centred on P32 (fracture area / rock volume, 1/m). Feed per-set " +
            "Dip / Dip dir / Spacing; optionally a scanline (expected P10) and a finite-fracture DFN (areas + volume) " +
            "for a directly-measured P32/P30 cross-check. P32 is the scale-independent measure to report for DFN work.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1004E-ED9E-4ED9-A04E-ED9EED9E004E");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Spacing", "Sp", "Per-set mean normal spacing (cloud units).", GH_ParamAccess.list);
        p.AddNumberParameter("Unit scale", "U", "Multiplies spacing into metres (1 if already metres).", GH_ParamAccess.item, 1.0);
        p.AddLineParameter("Scanline", "L", "Optional scanline (its direction) for the expected P10.", GH_ParamAccess.item);
        p.AddNumberParameter("DFN areas", "Fa", "Optional finite-fracture areas (m^2) for a directly-measured P32.", GH_ParamAccess.list);
        p.AddNumberParameter("Volume", "V", "Optional rock-mass volume (m^3) for the DFN P32/P30.", GH_ParamAccess.item, 0.0);
        p[4].Optional = true; p[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("P32 per set", "P32s", "Per-set volumetric intensity 1/spacing (1/m).", GH_ParamAccess.list);
        p.AddNumberParameter("P32", "P32", "Total volumetric fracture intensity (area/volume, 1/m).", GH_ParamAccess.item);
        p.AddNumberParameter("P10", "P10", "Expected scanline linear intensity (1/m); NaN if no scanline.", GH_ParamAccess.item);
        p.AddNumberParameter("P30", "P30", "DFN volumetric fracture count (1/m^3); NaN if no DFN.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Intensity summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var dip = new List<double>(); var dipdir = new List<double>(); var spacing = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || !da.GetDataList(2, spacing))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip, Dip dir and Spacing lists."); return; }

        double unit = 1.0; da.GetData(3, ref unit);
        Line scan = Line.Unset; bool hasScan = da.GetData(4, ref scan) && scan.Length > 1e-9;
        var areas = new List<double>(); da.GetDataList(5, areas);
        double vol = 0.0; da.GetData(6, ref vol);

        int n = Math.Min(dip.Count, Math.Min(dipdir.Count, spacing.Count));
        if (n == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Empty input."); return; }

        Vector3d dir = hasScan ? scan.Direction : Vector3d.Zero;
        var res = FractureIntensity.Compute(
            dip.GetRange(0, n), dipdir.GetRange(0, n), spacing.GetRange(0, n),
            unit, hasScan, dir,
            areas.Count > 0 ? areas : null, vol, areas.Count,
            unit == 1.0 ? "m (assumed)" : "scaled");

        da.SetDataList(0, res.P32PerSet != null ? res.P32PerSet.ToList() : new List<double>());
        da.SetData(1, res.P32);
        da.SetData(2, res.P10);
        da.SetData(3, res.P30);
        da.SetData(4, res.Report);

        if (unit == 1.0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Unit scale = 1: spacings assumed already in metres. Set it if the cloud is cm/mm.");
    }
}
