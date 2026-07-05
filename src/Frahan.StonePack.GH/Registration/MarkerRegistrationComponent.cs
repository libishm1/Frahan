#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Registration;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Registration;

// =============================================================================
// MarkerRegistrationComponent — Phase I3 of the UX architecture report
// §7.7.F rollout. Wraps RegistrationApi.SolveFromPoints (which delegates to
// the existing RigidTransformRecovery / Horn 1987 closed-form QAO).
//
// Use cases on the canvas:
//   - Mark physical fiducials at known scan-frame and world-frame positions,
//     pick them in two ordered lists, derive the scan→world Transform.
//   - Align a scan to a reference object (starting block) whose pose is known.
//   - Combined with GeoreferenceComponent for GPS / UTM ground-truth points.
//
// Component metadata follows existing Frahan conventions: subcategory under
// the unified Mesh panel, primary exposure row.
// =============================================================================

[Algorithm("Absolute orientation (Horn 1987)", "Horn, B.K.P. (1987). Closed-form solution of absolute orientation using unit quaternions. J. Opt. Soc. Am. A 4(4):629-642", WikiPath = "wiki/index/references.md")]
[DesignApplication(
    "Closed-form rigid alignment of N≥3 source/target point pairs  (Horn 1987 quaternion absolute orientation)",
    DesignFlow.Bridges,
    Precedent = "Horn 1987 closed-form absolute orientation via unit quaternions (J. Opt. Soc. Am. A 4(4):629-642)")]
public sealed class MarkerRegistrationComponent : FrahanComponentBase
{
    public MarkerRegistrationComponent()
        : base("Align by Control Points", "MarkerReg",
            "Closed-form rigid alignment of N≥3 source/target point pairs " +
            "(Horn 1987 quaternion absolute orientation). Use for marker- " +
            "or reference-object-based scan-to-world registration. " +
            "Implements absolute orientation (Horn 1987).",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("B1C2D3A4-1111-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("MarkerDetect.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Source Points", "S",
            "Source-frame (e.g. scan) marker positions. " +
            "Must have N≥3 points paired by INDEX with Target Points.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Target Points", "T",
            "Target-frame (e.g. world) marker positions. " +
            "Same count as Source Points; pairing is by index.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTransformParameter("Transform", "X",
            "Rigid transform mapping source onto target (apply to scan).",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("RMS Error", "RMS",
            "Root-mean-square per-pair residual after applying Transform " +
            "(model-unit distance).",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("Per-Pair Residuals", "R",
            "Distance from R·sᵢ+t to tᵢ for each input pair. Long-tail " +
            "values indicate bad markers — drop or re-survey them.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Pair Count", "N",
            "Number of input pairs used.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var source = new List<Point3d>();
        var target = new List<Point3d>();
        if (!da.GetDataList(0, source) || source.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Need at least 3 source points.");
            return;
        }
        if (!da.GetDataList(1, target) || target.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Need at least 3 target points.");
            return;
        }
        if (source.Count != target.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Source/target point counts must match; got {source.Count} vs {target.Count}.");
            return;
        }

        RegistrationResult result;
        try
        {
            result = RegistrationApi.SolveFromPoints(source, target);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        da.SetData(0, result.Transform);
        da.SetData(1, result.RmsError);
        da.SetDataList(2, result.PerPairResiduals);
        da.SetData(3, source.Count);

        // Surface a warning when any single pair lies far from the consensus,
        // typical sign of a mis-picked marker.
        for (int i = 0; i < result.PerPairResiduals.Length; i++)
        {
            if (result.PerPairResiduals[i] > result.RmsError * 5.0 && result.RmsError > 1e-9)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Pair {i} residual {result.PerPairResiduals[i]:F4} >> 5× RMS ({result.RmsError:F4}); possible mis-picked marker.");
            }
        }
    }
}
