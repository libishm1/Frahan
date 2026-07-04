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
// InSituBlockSizeComponent (D5F10050, Frahan > Quarry)
//
// Monte-Carlo in-situ block-size distribution (IBSD): samples the natural
// orientation scatter (Fisher) and spacing PDF of the joint sets over many
// realizations and reports the DISTRIBUTION of block volumes and shapes, plus
// the right-prism fraction -- the geology->fabrication signal (how sawable into
// rectangular dimension-stone blocks the natural fabric is). Complements the
// single-value Stereonet + Block Size (Palmstrom Vb) with a full distribution.
// =============================================================================

[Algorithm("Monte-Carlo IBSD", "Kalenchuk et al. (2006) / Palmstrom (2005); Vb = s1 s2 s3 / q, q = |det(n1,n2,n3)|",
    Note = "Fisher orientation + spacing-PDF sampling; block volume/shape distribution + right-prism fraction.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Cloud)", Reason = "Upstream source of per-set Dip / Dip dir / Spacing.")]
[RelatedComponent("Frahan > Quarry > Stereonet + Block Size", Reason = "Deterministic single-value block size; this gives the distribution.")]
[RelatedComponent("Frahan > Quarry > Fracture Intensity", Reason = "Same fabric; intensity vs block-size views.")]
public sealed class InSituBlockSizeComponent : FrahanComponentBase
{
    public InSituBlockSizeComponent()
        : base("In-Situ Block Size", "BlockSize",
            "Monte-Carlo in-situ block-size distribution. Feed per-set Dip / Dip dir / Spacing (from Discontinuity " +
            "Sets); samples Fisher orientation scatter + a spacing PDF over N realizations. Outputs the block-volume " +
            "distribution (P10/P50/P90), shape mix, and the right-prism fraction (q>=0.95) -- how sawable-to-rectangular " +
            "the natural fabric is. Needs >= 3 sets.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10050-ED9E-4ED9-A050-ED9EED9E0050");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("InSituBlockSize.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        p.AddNumberParameter("Spacing", "Sp", "Per-set mean normal spacing (cloud units).", GH_ParamAccess.list);
        p.AddNumberParameter("Scatter", "Sc", "Fisher orientation scatter (deg); one value for all sets or one per set.", GH_ParamAccess.list);
        p.AddBooleanParameter("Exponential", "E", "Spacing law: true = negative-exponential (default), false = normal CV0.3.", GH_ParamAccess.item, true);
        p.AddIntegerParameter("Realizations", "N", "Monte-Carlo realizations.", GH_ParamAccess.item, 1000);
        p.AddIntegerParameter("Seed", "Sd", "Random seed (deterministic).", GH_ParamAccess.item, 1);
        p.AddNumberParameter("Unit scale", "U", "Multiplies spacing into metres (1 if already metres).", GH_ParamAccess.item, 1.0);
        p[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Volumes", "V", "Per-realization block volume (m^3) -- the distribution.", GH_ParamAccess.list);
        p.AddNumberParameter("P10", "P10", "10th-percentile block volume (m^3).", GH_ParamAccess.item);
        p.AddNumberParameter("P50", "P50", "Median block volume (m^3).", GH_ParamAccess.item);
        p.AddNumberParameter("P90", "P90", "90th-percentile block volume (m^3).", GH_ParamAccess.item);
        p.AddNumberParameter("Deq", "De", "Median equivalent block diameter (m).", GH_ParamAccess.item);
        p.AddNumberParameter("Right-prism", "Rp", "Fraction of blocks with q>=0.95 (sawable-to-rectangular).", GH_ParamAccess.item);
        p.AddTextParameter("Shape mix", "Sh", "Block-shape class percentages.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Re", "IBSD summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var dip = new List<double>(); var dipdir = new List<double>(); var spacing = new List<double>();
        if (!da.GetDataList(0, dip) || !da.GetDataList(1, dipdir) || !da.GetDataList(2, spacing))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Dip, Dip dir and Spacing lists."); return; }

        var scatter = new List<double>(); da.GetDataList(3, scatter);
        bool exp = true; da.GetData(4, ref exp);
        int reals = 1000; da.GetData(5, ref reals);
        int seed = 1; da.GetData(6, ref seed);
        double unit = 1.0; da.GetData(7, ref unit);

        int n = Math.Min(dip.Count, Math.Min(dipdir.Count, spacing.Count));
        if (n < 3)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "IBSD needs at least 3 joint sets."); return; }

        var res = InSituBlockSize.Simulate(
            dip.GetRange(0, n), dipdir.GetRange(0, n), spacing.GetRange(0, n),
            scatter.Count > 0 ? scatter : null, exp, Math.Max(1, reals), seed, unit);

        if (res.Valid == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, res.Report); da.SetData(7, res.Report); return; }

        da.SetDataList(0, res.Volumes.ToList());
        da.SetData(1, res.P10Vol);
        da.SetData(2, res.P50Vol);
        da.SetData(3, res.P90Vol);
        da.SetData(4, res.DeqMedian);
        da.SetData(5, res.RightPrismFraction);
        string mix = string.Join(", ", Enumerable.Range(0, 4)
            .Select(i => $"{IbsdResult.ShapeLabels[i]} {100.0 * res.ShapeCounts[i] / res.Valid:F0}%"));
        da.SetData(6, mix);
        da.SetData(7, res.Report);

        if (res.RightPrismFraction < 0.2)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Low right-prism fraction: few rectangular blocks from the natural fabric.");
        if (unit == 1.0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Unit scale = 1: spacings assumed metres. Set it if the cloud is cm/mm.");
    }
}
