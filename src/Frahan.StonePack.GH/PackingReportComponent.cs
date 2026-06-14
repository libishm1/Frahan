using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Exposes Frahan.Core.PackingMetrics as a Grasshopper component. Takes an
/// opaque PackResult (typically the output of Pack3D Irregular / Pack3D
/// Mesh Heightmap / Pack3D Irregular Container) and surfaces the per-run
/// metrics: placement / failure counts, fill ratio, score statistics,
/// per-reason failure breakdown.
///
/// Spec 5 + spec 7; runbook section 16.7 component family
/// "Frahan Packing Report".
/// </summary>
[DesignApplication(
    "Compute summary metrics for a 3D PackResult: placements, failures,  fill ratio, average placement score, it...",
    DesignFlow.Bridges,
    Precedent = "Frahan-original packing-report generator (consumes PackResult)")]
public sealed class PackingReportComponent : FrahanComponentBase
{
    public PackingReportComponent()
        : base("Frahan Packing Report", "PackRpt",
            "Compute summary metrics for a 3D PackResult: placements, failures, " +
            "fill ratio, average placement score, item-volume stats, per-reason " +
            "failure counts.",
            "Frahan", "Reports")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C004-1A2B-4C3D-9E4F-5A6B7C8D9E04");
    protected override Bitmap? Icon => IconProvider.Load("PackDiagnostics.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Pack Result", "R",
            "PackResult from a 3D pack solver (opaque).", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Placement Count", "P", "Number of placed items.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Failure Count", "F", "Number of failed items.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Failure Ratio", "Fr", "Failures / (Placements + Failures).", GH_ParamAccess.item);
        pManager.AddNumberParameter("Packed Volume", "Vp", "Sum of placed item volumes.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Container Volume", "Vc", "Container volume.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Fill Ratio", "Fl", "Packed / Container.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Average Score", "S", "Average placement score.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Max Item Height", "H", "Max top-of-item Z across placements.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Item Volume Min/Max/Avg", "Vi", "Three-element list: [min, max, avg].", GH_ParamAccess.list);
        pManager.AddTextParameter("Failure Reasons", "Rs", "One '<reason>: <count>' line per distinct failure reason.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Single-line summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        IGH_Goo? goo = null;
        if (!da.GetData(0, ref goo) || goo == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Pack Result input required.");
            return;
        }

        PackResult? result = null;
        if (goo is GH_ObjectWrapper wrapper && wrapper.Value is PackResult r)
            result = r;
        else if (goo.ScriptVariable() is PackResult r2)
            result = r2;
        if (result == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Pack Result must be a PackResult (use a Frahan 3D pack component upstream).");
            return;
        }

        PackingMetricsReport m;
        try { m = PackingMetrics.Compute(result); }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Compute failed: " + ex.Message);
            return;
        }

        da.SetData(0, m.PlacementCount);
        da.SetData(1, m.FailureCount);
        da.SetData(2, m.FailureRatio);
        da.SetData(3, m.PackedVolume);
        da.SetData(4, m.ContainerVolume);
        da.SetData(5, m.FillRatio);
        da.SetData(6, m.AveragePlacementScore);
        da.SetData(7, m.MaxItemHeight);
        da.SetDataList(8, new[] { m.MinItemVolume, m.MaxItemVolume, m.AverageItemVolume });

        var reasonLines = new List<string>(m.FailureReasonCounts.Count);
        foreach (var kv in m.FailureReasonCounts.OrderByDescending(p => p.Value))
            reasonLines.Add($"{kv.Key}: {kv.Value}");
        da.SetDataList(9, reasonLines);

        da.SetData(10, m.ToString());
    }
}
