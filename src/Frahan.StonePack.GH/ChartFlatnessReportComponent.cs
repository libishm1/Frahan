using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Surface;
using Grasshopper.Kernel;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Exposes Frahan.Surface.ChartFlatnessReport.Classify as a Grasshopper
/// component. Takes a list of per-face area ratios (typically from
/// ChartDistortionAnalyzer) plus a threshold and reports which faces exceed
/// the threshold + worst-case face.
///
/// Spec 6 section 7; runbook section 16.2 component family
/// "Frahan Distortion Report".
/// </summary>
[DesignApplication(
    "Classify per-face area ratios against a flatness threshold",
    DesignFlow.TopDown,
    Precedent = "Frahan-original chart-flatness diagnostic for BFF surface charts")]
[Algorithm("Per-face area-ratio distortion classification", "Frahan-original",
    Note = "max(ratio, 1/ratio) threshold test; pairs with BFF (Sawhney 2017) charts but the classifier is Frahan-original, not the BFF algorithm")]
public sealed class ChartFlatnessReportComponent : FrahanComponentBase
{
    public ChartFlatnessReportComponent()
        : base("Frahan Chart Flatness Report", "ChartFlat",
            "Classify per-face area ratios against a flatness threshold. " +
            "Threshold is interpreted as max(ratio, 1/ratio); 0.5 and 2.0 are " +
            "equally distorted from 1.0. Frahan-original method.",
            "Frahan", "Surface Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C006-1A2B-4C3D-9E4F-5A6B7C8D9E06");
    protected override Bitmap? Icon => IconProvider.Load("DistortionMap.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Per-Face Area Ratios", "A",
            "List of per-face area ratios from ChartDistortionAnalyzer.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Threshold", "T",
            "Flatness threshold (1.0 = no distortion allowed; 1.5 = 50% allowed; 2.0 = 2x).",
            GH_ParamAccess.item, 1.5);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Total Face Count", "N", "Total faces classified.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Above Threshold Count", "Nx", "Faces above the threshold.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Above Threshold Ratio", "Rx", "Above / Total.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Worst Face Index", "Wi", "Index of the most-distorted face.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Worst Area Ratio", "Wr", "Normalised distortion of the worst face.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Per-Face Above Flag", "Bf",
            "Per-face boolean: true if above threshold.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Single-line summary.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var ratios = new List<double>();
        double threshold = 1.5;

        if (!da.GetDataList(0, ratios) || ratios.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Per-face area ratios input required.");
            return;
        }
        da.GetData(1, ref threshold);

        if (threshold <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Threshold must be > 0.");
            return;
        }

        ChartFlatnessReport report;
        try { report = ChartFlatnessReport.Classify(ratios, threshold); }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Classify failed: " + ex.Message);
            return;
        }

        if (report.AboveThresholdRatio > 0.25)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{report.AboveThresholdRatio:P1} of faces exceed the threshold; consider subdividing the input surface.");

        da.SetData(0, report.TotalFaceCount);
        da.SetData(1, report.AboveThresholdCount);
        da.SetData(2, report.AboveThresholdRatio);
        da.SetData(3, report.WorstFaceIndex);
        da.SetData(4, report.WorstAreaRatio);

        var flags = new List<bool>(report.PerFaceFlags.Count);
        for (int i = 0; i < report.PerFaceFlags.Count; i++)
            flags.Add(report.PerFaceFlags[i].IsAboveThreshold);
        da.SetDataList(5, flags);

        da.SetData(6, report.ToString());
    }
}
