using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Frahan.Core.Discontinuity;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Discontinuity Sets (Cloud) -- the photogrammetry/TLS-cloud to joint-set
/// bridge (a managed Frahan equivalent of the CloudCompare FACETS + DSE
/// plugins). Region-grows planar facets from the cloud, reads each facet's
/// dip / dip-direction, then clusters the facet poles into joint sets by
/// antipodal mean-shift. Outputs the cloud coloured by joint set (segmentation)
/// plus the per-set pole / dip / dip-direction / spacing table.
/// </summary>
[Algorithm("Planar-facet extraction", "FACETS (Dewez, Girardeau-Montaut et al. 2016); Frahan managed port",
    Note = "Per-point PCA normals over kNN, region-grow on axial-normal + plane-band gates.")]
[Algorithm("Joint-set clustering", "DSE (Riquelme et al. 2014) set step; antipodal (Watson) mean-shift",
    Note = "Mode-seeking on the sphere; set count discovered, not preset.")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Consumes the discontinuity model this produces.")]
[RelatedComponent("Frahan > Ingest > Load E57 Cloud", Reason = "Produces the point cloud this segments.")]
public class DiscontinuitySetsComponent : FrahanComponentBase
{
    public DiscontinuitySetsComponent()
        : base("Discontinuity Sets (Cloud)", "DiscSets",
            "Extract planar facets from a rock-face point cloud and cluster their poles into joint sets " +
            "(managed FACETS + DSE). Outputs the cloud coloured by joint set plus per-set dip / dip-direction / " +
            "spacing. Subsample very large clouds first.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10047-ED9E-4ED9-A047-ED9EED9E0047");
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Cloud", "C", "Rock-face point cloud (PointCloud, or points/mesh vertices).", GH_ParamAccess.item);
        pManager.AddIntegerParameter("K", "K", "Neighbours for PCA normals.", GH_ParamAccess.item, 24);
        pManager.AddNumberParameter("Max angle", "A", "Region-grow normal agreement (deg).", GH_ParamAccess.item, 12.0);
        pManager.AddIntegerParameter("Min facet pts", "Mp", "Minimum points per facet.", GH_ParamAccess.item, 40);
        pManager.AddNumberParameter("Bandwidth", "Bw", "Mean-shift angular bandwidth for joint sets (deg).", GH_ParamAccess.item, 15.0);
        pManager.AddIntegerParameter("Min set facets", "Ms", "Minimum facets per joint set.", GH_ParamAccess.item, 3);
        pManager.AddBooleanParameter("Run", "R", "Set true to segment.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGeometryParameter("Segmented", "S", "Cloud coloured by joint set (unassigned = grey).", GH_ParamAccess.item);
        pManager.AddLineParameter("Set poles", "P", "A pole line per joint set through the cloud centroid.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Spacing", "Sp", "Per-set mean normal spacing.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Facets/set", "Nf", "Per-set facet count.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Re", "Summary.", GH_ParamAccess.item);
    }

    private static readonly int[][] Pal =
    {
        new[]{220,50,47}, new[]{38,139,210}, new[]{133,153,0}, new[]{181,137,0},
        new[]{211,54,130}, new[]{42,161,152}, new[]{203,75,22}, new[]{108,113,196},
        new[]{0,160,90}, new[]{150,100,40}
    };

    protected override void SolveSafe(IGH_DataAccess da)
    {
        IGH_GeometricGoo goo = null;
        if (!da.GetData(0, ref goo) || goo == null) return;
        int k = 24; da.GetData(1, ref k);
        double maxAng = 12; da.GetData(2, ref maxAng);
        int minFp = 40; da.GetData(3, ref minFp);
        double bw = 15; da.GetData(4, ref bw);
        int minSf = 3; da.GetData(5, ref minSf);
        bool run = false; da.GetData(6, ref run);
        if (!run) return;

        var pts = ReadPoints(goo);
        if (pts == null || pts.Count < k + 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need a point cloud with more than K points.");
            return;
        }

        var fr = FacetExtractor.Extract(pts, new FacetOptions { K = Math.Max(6, k), MaxNormalAngleDeg = maxAng, MinFacetPoints = Math.Max(10, minFp) });
        var sets = SetClusterer.Cluster(fr.Facets, new SetOptions { BandwidthDeg = bw, MinSetFacets = Math.Max(1, minSf) });

        // segmented cloud: colour each facet's points by its set; non-facet points grey
        var cloud = new PointCloud();
        var col = new Color[pts.Count];
        for (int i = 0; i < pts.Count; i++) col[i] = Color.FromArgb(110, 110, 110);
        for (int s = 0; s < sets.Count; s++)
        {
            var c = Pal[s % Pal.Length];
            var color = Color.FromArgb(c[0], c[1], c[2]);
            foreach (var fi in sets[s].FacetIndices)
                foreach (var pi in fr.Facets[fi].PointIndices)
                    col[pi] = color;
        }
        for (int i = 0; i < pts.Count; i++) cloud.Add(pts[i], col[i]);

        // pole lines through the cloud centroid
        var bb = new BoundingBox(pts);
        var ctr = bb.Center;
        double len = bb.Diagonal.Length * 0.25;
        var poleLines = sets.Select(js => new Line(ctr - js.Pole * len, ctr + js.Pole * len)).ToList();

        string report = $"{fr.Facets.Count} facets (spacing {fr.Spacing:F3}) -> {sets.Count} joint sets. " +
                        string.Join("; ", sets.Select((js, i) => $"J{i + 1}: {js.Dip:F0}/{js.DipDir:F0} n={js.FacetCount} sp={js.MeanSpacing:F2}"));

        da.SetData(0, cloud);
        da.SetDataList(1, poleLines);
        da.SetDataList(2, sets.Select(s => s.Dip).ToList());
        da.SetDataList(3, sets.Select(s => s.DipDir).ToList());
        da.SetDataList(4, sets.Select(s => s.MeanSpacing).ToList());
        da.SetDataList(5, sets.Select(s => s.FacetCount).ToList());
        da.SetData(6, report);
    }

    private static List<Point3d> ReadPoints(IGH_GeometricGoo goo)
    {
        var g = goo.ScriptVariable();
        if (g is PointCloud pc) return pc.GetPoints().ToList();
        if (g is Mesh m) return m.Vertices.ToPoint3dArray().ToList();
        if (g is Point3d p) return new List<Point3d> { p };
        return null;
    }
}
