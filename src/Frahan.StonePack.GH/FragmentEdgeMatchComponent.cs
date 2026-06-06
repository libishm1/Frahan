using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core;
using Frahan.Surface;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH;

/// <summary>
/// Match each edge of one or more fragment curves against a populated
/// BoundaryRailIndex. Outputs per-edge ranked match scores as DataTrees
/// (one branch per fragment, leaves are scores within that fragment's edges).
///
/// 2026-05-05: moved from "Frahan/2D Packing" to "Frahan/Analysis". The
/// unified solver now matches fragment edges against the per-sheet boundary
/// index internally when Boundary Mode is on; this standalone component is
/// kept for inspecting affinity scores externally and debugging score
/// distributions.
///
/// Spec 5 section 5.5; runbook section 16.1 component family
/// "Frahan Edge Match".
/// </summary>
[DesignApplication(
    "Inspect per-edge affinity scores when matching fragments against a boundary-rail index (debug surface for irregular-sheet packing).",
    DesignFlow.Bridges,
    Precedent = "Frahan-original fragment-edge affinity diagnostic (spec 5 §5.5; runbook §16.1)")]
[Algorithm("Boundary-rail edge affinity scoring", "Frahan-original",
    Note = "diagnostic surface for the irregular-sheet packer's internal edge-matching (spec 5 §5.5); not a published algorithm",
    WikiPath = "wiki/algorithms/edge_matching/")]
public sealed class FragmentEdgeMatchComponent : GH_Component
{
    public FragmentEdgeMatchComponent()
        : base("Frahan Fragment Edge Match", "FragMatch",
            "Diagnostic component. Match each fragment curve's polyline edges " +
            "against a populated BoundaryRailIndex; returns ranked affinity " +
            "scores per fragment per edge. The unified Frahan Sheet Pack now " +
            "matches internally when Boundary Mode is on; use this component " +
            "to inspect scores externally. Frahan-original method.",
            "Frahan", "Analysis")
    {
    }

    public override Guid ComponentGuid => new Guid("AB12C003-1A2B-4C3D-9E4F-5A6B7C8D9E03");
    protected override Bitmap? Icon => IconProvider.Load("EdgeMatchSolve.png");
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Index", "I",
            "Populated BoundaryRailIndex<BoundaryIntervalInfo> from Frahan Boundary Rail Index.",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Fragments", "F",
            "Closed planar fragment curves to query.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Zone Buckets", "Z",
            "Per-fragment zone bucket. Defaults to all-zeros if omitted.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Length Bucket Size", "Lb",
            "Must match the source index's bucket size.",
            GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Angle Bucket Size", "Ab",
            "Must match the source index's bucket size (degrees).",
            GH_ParamAccess.item, 5.0);
        pManager.AddNumberParameter("Curvature Bucket Size", "Cb",
            "Must match the source index's bucket size.",
            GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("Length Radius", "Lr",
            "How many length-buckets to widen on each side.", GH_ParamAccess.item, 1);
        pManager.AddIntegerParameter("Angle Radius", "Ar",
            "How many angle-buckets to widen on each side.", GH_ParamAccess.item, 1);
        pManager.AddBooleanParameter("Preserve Zone", "Pz",
            "If true, only match within each fragment's zone.",
            GH_ParamAccess.item, true);
        pManager.AddIntegerParameter("Top K", "K",
            "Maximum matches per edge (0 = unlimited).",
            GH_ParamAccess.item, 8);
        pManager.AddNumberParameter("Min Affinity Score", "M",
            "Filter out matches with score below this threshold.",
            GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Discretisation Tolerance", "T",
            "Tolerance for fragment ToPolyline conversion.",
            GH_ParamAccess.item, 0.01);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Top Score Per Edge", "S",
            "DataTree: branch per fragment, one number per fragment edge = best affinity score.",
            GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Match Count Per Edge", "N",
            "DataTree: branch per fragment, one int per fragment edge = number of matches kept.",
            GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Edge Counts", "E",
            "Number of edges per fragment.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Total Matches", "Tm",
            "Total matches summed across every fragment edge.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        IGH_Goo? indexGoo = null;
        var fragments = new List<Curve>();
        var zoneBuckets = new List<int>();
        double lengthBucket = 1.0;
        double angleBucket = 5.0;
        double curvatureBucket = 0.01;
        int lengthRadius = 1;
        int angleRadius = 1;
        bool preserveZone = true;
        int topK = 8;
        double minScore = 0.0;
        double tol = 0.01;

        if (!da.GetData(0, ref indexGoo) || indexGoo == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Index input required.");
            return;
        }
        if (!da.GetDataList(1, fragments) || fragments.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one fragment curve required.");
            return;
        }
        da.GetDataList(2, zoneBuckets);
        da.GetData(3, ref lengthBucket);
        da.GetData(4, ref angleBucket);
        da.GetData(5, ref curvatureBucket);
        da.GetData(6, ref lengthRadius);
        da.GetData(7, ref angleRadius);
        da.GetData(8, ref preserveZone);
        da.GetData(9, ref topK);
        da.GetData(10, ref minScore);
        da.GetData(11, ref tol);

        // Unwrap the opaque index payload.
        BoundaryRailIndex<BoundaryIntervalInfo>? index = null;
        if (indexGoo is GH_ObjectWrapper wrapper && wrapper.Value is BoundaryRailIndex<BoundaryIntervalInfo> idx)
            index = idx;
        if (index == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Index input must be a BoundaryRailIndex<BoundaryIntervalInfo> (use Frahan Boundary Rail Index upstream).");
            return;
        }

        var options = new MatchOptions
        {
            LengthBucketSize = lengthBucket,
            AngleBucketSizeDegrees = angleBucket,
            CurvatureBucketSize = curvatureBucket,
            LengthRadius = Math.Max(0, lengthRadius),
            AngleRadius = Math.Max(0, angleRadius),
            PreserveZone = preserveZone,
            TopK = Math.Max(0, topK),
            MinAffinityScore = minScore,
        };

        // Converter: BoundaryIntervalInfo -> EdgeDescriptor for scoring.
        Func<BoundaryIntervalInfo, EdgeDescriptor> toDescriptor = ToDescriptor;

        var topScores = new GH_Structure<GH_Number>();
        var matchCounts = new GH_Structure<GH_Integer>();
        var edgeCounts = new List<int>(fragments.Count);
        int totalMatches = 0;

        for (int fi = 0; fi < fragments.Count; fi++)
        {
            var curve = fragments[fi];
            int zone = zoneBuckets.Count == 0
                ? 0
                : zoneBuckets[Math.Min(fi, zoneBuckets.Count - 1)];

            FragmentDescriptor? frag = null;
            try
            {
                frag = FragmentDescriptorBuilder.BuildFromCurve(
                    id: $"frag-{fi}", boundary: curve, zoneId: zone, discretisationTolerance: tol);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Fragment {fi} failed: {ex.Message}");
            }

            if (frag == null)
            {
                edgeCounts.Add(0);
                continue;
            }

            edgeCounts.Add(frag.EdgeCount);
            var path = new GH_Path(fi);
            var perEdgeMatches = BoundaryRailMatcher.MatchFragment(index, frag, options, toDescriptor);

            for (int ei = 0; ei < perEdgeMatches.Count; ei++)
            {
                var matches = perEdgeMatches[ei];
                double topScore = matches.Count > 0 ? matches[0].AffinityScore : 0.0;
                topScores.Append(new GH_Number(topScore), path);
                matchCounts.Append(new GH_Integer(matches.Count), path);
                totalMatches += matches.Count;
            }
        }

        da.SetDataTree(0, topScores);
        da.SetDataTree(1, matchCounts);
        da.SetDataList(2, edgeCounts);
        da.SetData(3, totalMatches);
        da.SetData(4,
            $"FragmentEdgeMatch: {fragments.Count} fragments, {totalMatches} total matches, " +
            $"options: lenR={options.LengthRadius} angR={options.AngleRadius} " +
            $"preserveZone={options.PreserveZone} topK={options.TopK} minScore={options.MinAffinityScore:0.##}");
    }

    private static EdgeDescriptor ToDescriptor(BoundaryIntervalInfo info)
    {
        // The interval's average tangent is a 3D Vector3d; project to XY angle.
        var t = info.AverageTangent;
        double angleDeg = Math.Atan2(t.Y, t.X) * 180.0 / Math.PI;
        if (angleDeg < 0) angleDeg += 360.0;

        // ZoneId is not stored on the BoundaryIntervalInfo today; the builder
        // tagged it via the EdgeKey in the index. Use 0 here so the converter
        // returns a comparable descriptor; the actual zone match check
        // happens at QueryNeighbors time via preserveZone.
        return new EdgeDescriptor(
            length: info.ApproxLength,
            angleDegrees: angleDeg,
            curvatureScore: info.CurvatureScore,
            straightnessScore: info.StraightnessScore,
            zoneId: 0);
    }
}
