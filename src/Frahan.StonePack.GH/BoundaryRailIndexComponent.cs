using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core;
using Frahan.Surface;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Builds a Frahan.Core.BoundaryRailIndex&lt;BoundaryIntervalInfo&gt; from one
/// or more Rhino curves and reports its size + key statistics. Returns the
/// populated index as an opaque GH_ObjectWrapper consumed by FragmentEdgeMatch.
///
/// 2026-05-05: moved from "Frahan/2D Packing" to "Frahan/Analysis" to make
/// its diagnostic role explicit. The unified solver now folds boundary
/// scoring in directly (see Boundary Mode input on Frahan Sheet Pack
/// Unified). This component remains useful for inspecting the index that
/// the solver builds internally, debugging affinity scores, and exporting
/// the index for ad-hoc analysis. It does NOT need to be wired into the
/// solver any more.
///
/// Spec 5 section 5.5 + 5.6.
/// </summary>
[DesignApplication(
    "Inspect the boundary-rail affinity index that Frahan Sheet Pack builds internally for the Boundary Mode packer.",
    DesignFlow.Bridges,
    Precedent = "Frahan-original affinity-bucket diagnostic (spec 5 §5.5 + §5.6); supports irregular-sheet packer tuning")]
[Algorithm("Boundary-rail affinity bucketing", "Frahan-original",
    Note = "arc-length affinity bucketing (spec 5 §5.5-5.6); not a published algorithm",
    WikiPath = "wiki/algorithms/edge_matching/")]
public sealed class BoundaryRailIndexComponent : GH_Component
{
    public BoundaryRailIndexComponent()
        : base("Frahan Boundary Rail Index", "RailIdx",
            "Diagnostic component. Build a boundary-rail index from one or " +
            "more boundary curves; each curve is sliding-window-sampled into " +
            "(length, tangent angle, curvature) buckets and stored as a " +
            "BoundaryIntervalInfo. The unified Frahan Sheet Pack now builds " +
            "this index internally when Boundary Mode is on; this standalone " +
            "component is kept for index inspection and ad-hoc analysis. Frahan-original method.",
            "Frahan", "Analysis")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C001-1A2B-4C3D-9E4F-5A6B7C8D9E01");
    protected override Bitmap? Icon => IconProvider.Load("BoundarySegmenter.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Boundaries", "B",
            "One or more boundary curves to index.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Outer Flags", "O",
            "Per-boundary flag: true = outer outline, false = hole. " +
            "If shorter than the boundary list, the last value is repeated.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Zone Buckets", "Z",
            "Per-boundary zone bucket. Used to group boundaries (sheets, regions). " +
            "If shorter than the boundary list, the last value is repeated. " +
            "Defaults to all-zeros if omitted.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Window Length", "W",
            "Sliding-window length along each curve (model units).",
            GH_ParamAccess.item, 50.0);
        pManager.AddNumberParameter("Step Length", "S",
            "Sliding-window step (model units).", GH_ParamAccess.item, 25.0);
        pManager.AddNumberParameter("Length Bucket Size", "Lb",
            "EdgeKey length bucket size (model units).",
            GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Angle Bucket Size", "Ab",
            "EdgeKey angle bucket size (degrees).", GH_ParamAccess.item, 5.0);
        pManager.AddNumberParameter("Curvature Bucket Size", "Cb",
            "EdgeKey curvature bucket size (1 / radius).",
            GH_ParamAccess.item, 0.01);

        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Index", "I",
            "Populated BoundaryRailIndex&lt;BoundaryIntervalInfo&gt; (opaque).",
            GH_ParamAccess.item);
        pManager.AddIntegerParameter("Interval Count", "N",
            "Total intervals added to the index.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Key Count", "K",
            "Distinct EdgeKey buckets in the index.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Known Zones", "Zk",
            "Distinct zone buckets observed.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Human-readable summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var boundaries = new List<Curve>();
        var outerFlags = new List<bool>();
        var zoneBuckets = new List<int>();
        double windowLength = 50.0;
        double stepLength = 25.0;
        double lengthBucket = 1.0;
        double angleBucket = 5.0;
        double curvatureBucket = 0.01;

        if (!da.GetDataList(0, boundaries) || boundaries.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one boundary curve is required.");
            return;
        }
        da.GetDataList(1, outerFlags);
        da.GetDataList(2, zoneBuckets);
        da.GetData(3, ref windowLength);
        da.GetData(4, ref stepLength);
        da.GetData(5, ref lengthBucket);
        da.GetData(6, ref angleBucket);
        da.GetData(7, ref curvatureBucket);

        if (windowLength <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Window Length must be > 0.");
            return;
        }
        if (stepLength <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Step Length must be > 0.");
            return;
        }

        BoundaryRailBuilder builder;
        try
        {
            builder = new BoundaryRailBuilder(
                windowLength: windowLength,
                stepLength: stepLength,
                lengthBucketSize: lengthBucket,
                angleBucketSizeDegrees: angleBucket,
                curvatureBucketSize: curvatureBucket);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Bucket / window settings invalid: " + ex.Message);
            return;
        }

        var index = new BoundaryRailIndex<BoundaryIntervalInfo>();
        int skipped = 0;

        for (int i = 0; i < boundaries.Count; i++)
        {
            Curve? curve = boundaries[i];
            if (curve == null)
            {
                skipped++;
                continue;
            }

            bool isOuter = outerFlags.Count == 0
                ? true
                : outerFlags[Math.Min(i, outerFlags.Count - 1)];
            int zone = zoneBuckets.Count == 0
                ? 0
                : zoneBuckets[Math.Min(i, zoneBuckets.Count - 1)];

            try
            {
                builder.AddCurve(curve, isOuter, zone, index);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Boundary {i} failed: {ex.Message}");
                skipped++;
            }
        }

        if (skipped > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{skipped} boundary curve(s) skipped (null or threw).");
        }

        var report =
            $"BoundaryRailIndex: {index.IntervalCount} intervals across " +
            $"{index.KeyCount} distinct keys, {index.KnownZones.Count} zones. " +
            $"Window {windowLength}/{stepLength}, buckets L={lengthBucket} " +
            $"A={angleBucket}deg C={curvatureBucket}.";

        da.SetData(0, new GH_ObjectWrapper(index));
        da.SetData(1, index.IntervalCount);
        da.SetData(2, index.KeyCount);
        da.SetDataList(3, index.KnownZones.OrderBy(z => z));
        da.SetData(4, report);
    }
}
