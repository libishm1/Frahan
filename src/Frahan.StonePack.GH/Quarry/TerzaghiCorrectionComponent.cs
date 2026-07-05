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
// TerzaghiCorrectionComponent (D5F1004D, Frahan > Quarry)
//
// Corrects a discontinuity survey for orientation sampling bias (Terzaghi 1965):
// a scanline / rock-face preferentially samples discontinuities perpendicular to
// it, so raw pole counts and set proportions are biased. Feed per-discontinuity
// Dip / Dip dir (from Discontinuity Ingest, or the per-facet poles of a scan),
// the scanline direction (or the face normal in Window mode), and a blind-zone
// cap. Outputs per-discontinuity Terzaghi weights + raw-vs-corrected set
// proportions -- the fix a rock-mechanics reviewer expects before any pole plot.
// =============================================================================

[Algorithm("Terzaghi bias correction", "Terzaghi (1965); w = 1/sin(delta) capped at the blind-zone angle",
    Note = "delta = angle between the discontinuity plane and the sampling line/face; grazing planes are under-sampled.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Ingest", Reason = "Upstream source of measured Dip / Dip dir / Set id.")]
[RelatedComponent("Frahan > Quarry > Stereonet + Block Size", Reason = "Plot the bias-corrected set proportions.")]
[RelatedComponent("Frahan > Quarry > Fracture Intensity", Reason = "Same orientation geometry drives P10 -> P32.")]
public sealed class TerzaghiCorrectionComponent : FrahanComponentBase
{
    public TerzaghiCorrectionComponent()
        : base("Terzaghi Correction", "Terzaghi",
            "Correct a discontinuity survey for orientation sampling bias (Terzaghi 1965). Feed per-discontinuity " +
            "Dip / Dip dir + the scanline direction (or face normal in Window mode). Outputs per-discontinuity weights " +
            "(1/sin of the plane-to-sampler angle, capped by the blind-zone angle) and raw-vs-corrected set proportions.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1004D-ED9E-4ED9-A04D-ED9EED9E004D");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("Terzaghi.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-discontinuity dip (deg, [0,90]).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-discontinuity dip-direction (deg, clockwise from North).", GH_ParamAccess.list);
        p.AddIntegerParameter("Set id", "Si", "Optional per-discontinuity set id (>=0; -1 unassigned) for raw-vs-corrected proportions.", GH_ParamAccess.list);
        p.AddVectorParameter("Sampling", "S", "Scanline direction (Window=false) or sampling-face normal (Window=true).", GH_ParamAccess.item, new Vector3d(1, 0, 0));
        p.AddBooleanParameter("Window", "W", "False = scanline (line sampling); True = window / rock-face (areal sampling).", GH_ParamAccess.item, false);
        p.AddNumberParameter("Min angle", "Ma", "Blind-zone half-angle (deg): weight cap = 1/sin(Ma).", GH_ParamAccess.item, 15.0);
        p[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Weight", "W", "Per-discontinuity Terzaghi weight (>=1).", GH_ParamAccess.list);
        p.AddNumberParameter("Bias angle", "B", "Per-discontinuity plane-to-sampler angle delta (deg).", GH_ParamAccess.list);
        p.AddIntegerParameter("Set ids", "Si", "Distinct set ids (ascending).", GH_ParamAccess.list);
        p.AddNumberParameter("Raw frac", "Rf", "Raw per-set proportion (0..1).", GH_ParamAccess.list);
        p.AddNumberParameter("Corrected frac", "Cf", "Bias-corrected per-set proportion (0..1).", GH_ParamAccess.list);
        p.AddNumberParameter("Blind zone", "Bz", "Blind-zone half-angle used (deg).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "Correction summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var dip = new List<double>(); var dipdir = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || dip.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip and Dip dir lists."); return; }
        int n = Math.Min(dip.Count, dipdir.Count);

        var setIdsIn = new List<int>(); da.GetDataList(2, setIdsIn);
        Vector3d sampling = new Vector3d(1, 0, 0); da.GetData(3, ref sampling);
        bool window = false; da.GetData(4, ref window);
        double minAng = 15.0; da.GetData(5, ref minAng);

        var poles = new List<Vector3d>(n);
        for (int i = 0; i < n; i++) poles.Add(OrientationMath.NormalFromDipDipDir(dip[i], dipdir[i]));
        List<int> setIds = (setIdsIn.Count == n) ? setIdsIn : null;
        if (setIdsIn.Count > 0 && setIdsIn.Count != n)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set id count != Dip count; ignoring set ids.");

        var res = TerzaghiCorrection.Correct(poles, sampling, window, minAng, setIds);

        da.SetDataList(0, res.Weights.ToList());
        da.SetDataList(1, res.BiasAngleDeg.ToList());
        if (res.SetIds != null)
        {
            da.SetDataList(2, res.SetIds.ToList());
            da.SetDataList(3, res.RawFraction.ToList());
            da.SetDataList(4, res.CorrectedFraction.ToList());
        }
        da.SetData(5, res.BlindZoneDeg);
        da.SetData(6, res.Report);

        if (res.Clamped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{res.Clamped} discontinuities inside the blind zone (weight capped).");
    }
}
