#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// CloudIcpComponent — Phase I.6-I15 visible cloud-cloud ICP (UX
// architecture report §7.7.F).
//
// Composes the trimmed point-to-point ICP from PointCloudIcp with
// optional native acceleration (Geogram KD-tree + voxel downsample) and
// graceful managed fallback when the Phase I rebuild of the shims is
// not present. Coarse-to-fine multi-resolution wrapper hits 10M+ point
// scale within ~30-40 s wall clock when native is available.
// =============================================================================

[Algorithm("Trimmed ICP (coarse-to-fine)",
    "Besl & McKay 1992 (Iterative Closest Point); Chetverikov et al. 2002 (Trimmed ICP)",
    Note = "multi-resolution voxel scales; native Geogram KD-tree, brute-force fallback")]
[DesignApplication(
    "Register a source point cloud onto a target via coarse-to- fine trimmed ICP",
    DesignFlow.Bridges,
    Precedent = "Besl McKay 1992 Iterative Closest Point; MathNet.Numerics SVD")]
public sealed class CloudIcpComponent : GH_Component
{
    public CloudIcpComponent()
        : base("Cloud ICP", "CloudIcp",
            "Register a source point cloud onto a target via coarse-to-" +
            "fine trimmed ICP. Uses Geogram KD-tree + voxel downsample " +
            "(native shim, Phase I) when available; falls back to " +
            "managed brute-force / hash-grid otherwise. Scales to 10M+ " +
            "points with the native shim. [Besl & McKay 1992]",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3201-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("PointCloudIcp.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Source Cloud", "S",
            "Source point cloud to register.", GH_ParamAccess.list);
        p.AddPointParameter("Target Cloud", "T",
            "Target point cloud (registration goal).", GH_ParamAccess.list);
        p.AddTransformParameter("Initial Guess", "X0",
            "Optional initial transform. Identity if not wired.",
            GH_ParamAccess.item);
        p[2].Optional = true;
        p.AddNumberParameter("Voxel Scales", "Vs",
            "Coarse-to-fine voxel sizes (model units). Default {0.5, 0.1, " +
            "0.02} → 50 cm → 10 cm → 2 cm for metre-scale benches.",
            GH_ParamAccess.list);
        p[3].Optional = true;
        p.AddIntegerParameter("Max Iterations", "Mi",
            "Max ICP iterations per voxel scale.",
            GH_ParamAccess.item, 30);
        p.AddNumberParameter("Trim Fraction", "Tf",
            "Drop this fraction of worst-residual pairs each iteration. " +
            "0.2 = standard robust ICP. 0 keeps all.",
            GH_ParamAccess.item, 0.2);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTransformParameter("Transform", "X",
            "Cumulative source→target rigid transform.", GH_ParamAccess.item);
        p.AddNumberParameter("Final RMS", "RMS",
            "Final RMS distance between corresponding source-target pairs.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Iterations", "It",
            "Total iterations across all voxel scales.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Converged", "Cv",
            "True when the last iteration met the tolerance.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Correspondences", "Cn",
            "Number of correspondences used in the final iteration " +
            "(after trim).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "Summary line.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var src = new List<Point3d>();
        var tgt = new List<Point3d>();
        Transform x0 = Transform.Identity;
        var voxelScales = new List<double>();
        int maxIters = 30;
        double trim = 0.2;

        if (!da.GetDataList(0, src) || src.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 3 source points.");
            return;
        }
        if (!da.GetDataList(1, tgt) || tgt.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 3 target points.");
            return;
        }
        da.GetData(2, ref x0);
        da.GetDataList(3, voxelScales);
        da.GetData(4, ref maxIters);
        da.GetData(5, ref trim);

        double[] scales = voxelScales.Count > 0 ? voxelScales.ToArray() : null;
        CloudIcpOptions opts;
        try
        {
            opts = new CloudIcpOptions(
                voxelScales: scales,
                maxIterationsPerScale: maxIters,
                trimFraction: trim);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        CloudIcpResult r;
        try
        {
            r = PointCloudIcp.Register(src, tgt, x0, opts);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"ICP failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        da.SetData(0, r.Transform);
        da.SetData(1, r.FinalRms);
        da.SetData(2, r.IterationsUsed);
        da.SetData(3, r.Converged);
        da.SetData(4, r.CorrespondencesUsed);
        da.SetData(5,
            $"Source={src.Count} Target={tgt.Count} pts; RMS={r.FinalRms:F4}; " +
            $"iters={r.IterationsUsed} converged={r.Converged}; " +
            $"correspondences={r.CorrespondencesUsed}");

        if (!r.Converged)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "ICP did not converge within Max Iterations. Try more iterations, " +
                "a tighter Trim Fraction, or a better initial guess (marker registration).");
        }
    }
}
