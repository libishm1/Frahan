using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Thin Grasshopper wrapper around <see cref="PackingPlanReportBuilder.Build"/>.
/// Aggregates a <see cref="PackingMetricsReport"/>, a residual-void list, and
/// per-fragment-per-edge match scores into a single <see cref="PackingPlanReport"/>
/// surfaced as opaque data plus three flat scalars + a text summary.
///
/// Spec 5 section 5; spec 7 section 5 component family "Frahan Packing Report".
/// Sprint-of-2026-05-04 hand-off addition.
/// </summary>
[DesignApplication(
    "Aggregate PackingMetricsReport + residual voids + edge-match scores  into one PackingPlanReport",
    DesignFlow.TopDown,
    Precedent = "Frahan-original packing-plan report")]
public sealed class PackingPlanReportComponent : FrahanComponentBase
{
    public PackingPlanReportComponent()
        : base("Frahan Packing Plan Report", "PackPlanRpt",
            "Aggregate PackingMetricsReport + residual voids + edge-match scores " +
            "into one PackingPlanReport. All inputs come from upstream Frahan " +
            "components (Pack3D, Residual Voids, Fragment Edge Match).",
            "Frahan", "Reports")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C008-1A2B-4C3D-9E4F-5A6B7C8D9E08");
    protected override Bitmap? Icon => IconProvider.Load("PackDiagnostics.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Packing Metrics", "M",
            "PackingMetricsReport (opaque) from Frahan Pack3D / Frahan Packing Metrics.",
            GH_ParamAccess.item);
        pManager.AddGenericParameter("Residual Voids", "V",
            "ResidualVoid list (opaque) from Frahan Residual Voids component. " +
            "Optional; defaults to empty.",
            GH_ParamAccess.list);
        pManager.AddGenericParameter("Edge Match Scores", "E",
            "Per-fragment-per-edge best match scores as a nested list " +
            "(IReadOnlyList<IReadOnlyList<double>>) wrapped opaque, or a flat " +
            "list of doubles (one entry per fragment-edge). Optional; defaults to empty.",
            GH_ParamAccess.item);
        // Item D (2026-05-04): DataTree alternative for edge scores. Branches
        // map 1:1 to fragments, items in each branch are that fragment's
        // per-edge best match scores. Wiring directly from a flat
        // DataTree<Number> is the natural Grasshopper UX; opaque (E) input
        // remains for code paths that already produce nested lists.
        pManager.AddNumberParameter("Edge Match Tree", "Et",
            "Per-fragment-per-edge best match scores as a DataTree<Number>. " +
            "One branch per fragment, items in each branch are that fragment's " +
            "per-edge scores. If both Edge Match Scores (E) and Edge Match Tree " +
            "(Et) are wired, the tree takes precedence. Optional.",
            GH_ParamAccess.tree);
        pManager[1].Optional = true;
        pManager[2].Optional = true;
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Plan Report", "R",
            "PackingPlanReport (opaque) for downstream serialisation / further reporting.",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("Total Residual Void Area", "Va",
            "Sum of approximate areas across all residual voids.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Avg Best Edge Match Score", "Es",
            "Mean of best-match scores across all fragment edges. Zero if no edges supplied.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Summary", "S",
            "One-line human-readable summary (PackingPlanReport.ToString()).",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        // Input 0 - PackingMetricsReport, required.
        var metricsWrapper = new GH_ObjectWrapper();
        if (!da.GetData(0, ref metricsWrapper) || metricsWrapper?.Value == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Packing Metrics input is required (PackingMetricsReport).");
            return;
        }
        var metrics = metricsWrapper.Value as PackingMetricsReport;
        if (metrics == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Packing Metrics input must be a PackingMetricsReport. Got " +
                metricsWrapper.Value.GetType().Name + ".");
            return;
        }

        // Input 1 - ResidualVoid list, optional.
        var voidWrappers = new List<GH_ObjectWrapper>();
        da.GetDataList(1, voidWrappers);
        var residualVoids = new List<ResidualVoid>(voidWrappers.Count);
        for (int i = 0; i < voidWrappers.Count; i++)
        {
            var rv = voidWrappers[i]?.Value as ResidualVoid;
            if (rv != null) residualVoids.Add(rv);
        }

        // Input 2 - edge match scores. Accept either:
        //   (a) IReadOnlyList<IReadOnlyList<double>> wrapped opaque, or
        //   (b) IReadOnlyList<double> wrapped opaque (will be re-wrapped as one row).
        // If neither, treat as empty.
        IReadOnlyList<IReadOnlyList<double>>? perFragmentEdgeScores = null;
        var edgeWrapper = new GH_ObjectWrapper();
        if (da.GetData(2, ref edgeWrapper) && edgeWrapper?.Value != null)
        {
            if (edgeWrapper.Value is IReadOnlyList<IReadOnlyList<double>> nested)
            {
                perFragmentEdgeScores = nested;
            }
            else if (edgeWrapper.Value is IReadOnlyList<double> flat)
            {
                perFragmentEdgeScores = new List<IReadOnlyList<double>> { flat };
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Edge Match Scores ignored: expected IReadOnlyList<IReadOnlyList<double>> " +
                    "or IReadOnlyList<double>, got " + edgeWrapper.Value.GetType().Name + ".");
            }
        }

        // Input 3 - DataTree<Number> alternative. Branches = fragments.
        // Takes precedence over opaque input 2 when both are wired.
        GH_Structure<GH_Number>? edgeTree = null;
        da.GetDataTree(3, out edgeTree);
        if (edgeTree != null && edgeTree.DataCount > 0)
        {
            if (perFragmentEdgeScores != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Both Edge Match Scores (E) and Edge Match Tree (Et) supplied. " +
                    "Tree takes precedence; opaque input ignored.");
            }
            var fromTree = new List<IReadOnlyList<double>>(edgeTree.PathCount);
            foreach (var path in edgeTree.Paths)
            {
                var branch = edgeTree.get_Branch(path);
                var perFragment = new List<double>(branch.Count);
                foreach (var item in branch)
                {
                    if (item is GH_Number gn) perFragment.Add(gn.Value);
                }
                fromTree.Add(perFragment);
            }
            perFragmentEdgeScores = fromTree;
        }

        PackingPlanReport report;
        try
        {
            report = PackingPlanReportBuilder.Build(
                packingMetrics: metrics,
                residualVoids: residualVoids,
                perFragmentEdgeMatchScores: perFragmentEdgeScores ?? Array.Empty<IReadOnlyList<double>>());
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "PackingPlanReportBuilder.Build threw: " + ex.Message);
            return;
        }

        da.SetData(0, new GH_ObjectWrapper(report));
        da.SetData(1, report.TotalResidualVoidArea);
        da.SetData(2, report.AverageBestEdgeMatchScore);
        da.SetData(3, report.ToString());
    }
}
