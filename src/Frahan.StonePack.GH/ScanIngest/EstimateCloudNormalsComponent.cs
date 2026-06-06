#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// EstimateCloudNormalsComponent — Phase H/I helper. Wraps CGAL's
// pca_estimate_normals + mst_orient_normals via the frahan_cgal shim.
// Provides the oriented-normal input that Poisson reconstruction and
// point-to-plane Cloud ICP need.
//
// NON-BLOCKING (2026-05-28): PCA + MST orientation on a big cloud runs on a
// background thread via AsyncScanComponent, gated by a "Run" toggle
// (default false). The canvas never freezes.
// =============================================================================

[Algorithm("PCA normal estimation + MST orientation",
    "Hoppe et al. 1992, surface reconstruction from unorganized points (PCA tangent planes + MST sign propagation)",
    Note = "via frahan_cgal (CGAL PCA/jet normals + minimum-spanning-tree orientation)")]
public sealed class EstimateCloudNormalsComponent
    : AsyncScanComponent<EstimateCloudNormalsComponent.Snapshot, EstimateCloudNormalsComponent.Payload>
{
    public EstimateCloudNormalsComponent()
        : base("Estimate Cloud Normals", "EstNormals",
            "PCA + MST-oriented normals on an unstructured point cloud. " +
            "Wire upstream of Poisson reconstruction (ScanReconstruct " +
            "Mode = 2) or point-to-plane Cloud ICP. Runs on a background " +
            "thread (Run gate). Requires the Phase H/I rebuild of " +
            "frahan_cgal.dll; falls back to a Warning bubble if the shim " +
            "isn't built.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3203-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("NormalEstimation.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Points", "P",
            "Input cloud as a point list. Optional if Cloud is wired.", GH_ParamAccess.list);
        p[0].Optional = true;
        p.AddIntegerParameter("K Neighbours", "K",
            "k for PCA fit (CGAL recommends 18-24 for dense clouds). " +
            "0 uses 18.",
            GH_ParamAccess.item, 18);
        p.AddGeometryParameter("Cloud", "C",
            "Input as a single native PointCloud (lag-free; preferred over the " +
            "Points list for large scans). If wired, the Points list is ignored.",
            GH_ParamAccess.item);
        p[2].Optional = true;
        // Appended LAST so existing canvases keep their wiring. Default false.
        p.AddBooleanParameter("Run", "R",
            "Set true to estimate normals (on a background thread). " +
            "False = idle; the canvas never freezes.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddVectorParameter("Normals", "N",
            "Per-point oriented normals; same order as input.",
            GH_ParamAccess.list);
        p.AddTextParameter("Report", "R", "Summary.", GH_ParamAccess.item);
        p.AddGeometryParameter("Cloud", "C",
            "The input points as a single native PointCloud WITH the estimated " +
            "normals baked in. Wire into Scan Reconstruct (Cloud) for a lag-free " +
            "Poisson path - no million-point list crosses the canvas.",
            GH_ParamAccess.item);
    }

    public sealed class Snapshot
    {
        // Flattened on the UI thread; the Task sees only this plain array.
        public double[] Flat;
        public int K;
    }

    public sealed class Payload
    {
        // Plain arrays; the Vector3d list + PointCloud are built in EmitResult
        // on the UI thread (no RhinoCommon geometry on the background thread).
        public double[] Pts;
        public double[] Nrm;
        public string Report;
        public string Failure;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(3, ref run);
        if (!run) return true;

        int k = 18;
        PointCloud inCloud = null;
        da.GetData(2, ref inCloud);
        double[] flat;
        if (inCloud != null && inCloud.Count >= 3)
        {
            int n = inCloud.Count;
            flat = new double[3 * n];
            for (int i = 0; i < n; i++)
            {
                var loc = inCloud[i].Location;
                flat[3 * i + 0] = loc.X; flat[3 * i + 1] = loc.Y; flat[3 * i + 2] = loc.Z;
            }
        }
        else
        {
            var pts = new List<Point3d>();
            if (!da.GetDataList(0, pts) || pts.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 3 points (wire Points or Cloud).");
                return false;
            }
            flat = new double[3 * pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                flat[3 * i + 0] = pts[i].X; flat[3 * i + 1] = pts[i].Y; flat[3 * i + 2] = pts[i].Z;
            }
        }
        da.GetData(1, ref k);
        snapshot = new Snapshot { Flat = flat, K = k };
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        progress($"estimating normals on {s.Flat.Length / 3} points...");
        token.ThrowIfCancellationRequested();

        if (!ReconstructionNative.TryEstimateNormals(s.Flat, s.K, out double[] nrm, out string err))
            return new Payload { Failure = err };

        return new Payload
        {
            Pts = s.Flat,
            Nrm = nrm,
            Report = $"Estimated {nrm.Length / 3} oriented normals (k = {(s.K > 0 ? s.K : 18)}).",
        };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        if (r.Failure != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Native shim unavailable: {r.Failure}");
            da.SetData(1, $"Failed: {r.Failure}");
            return;
        }
        // Build the Vector3d list + the normals-carrying PointCloud HERE (UI thread).
        int n = r.Nrm.Length / 3;
        int np = r.Pts.Length / 3;
        var normals = new List<Vector3d>(n);
        var outCloud = new PointCloud();
        for (int i = 0; i < n; i++)
        {
            var nv = new Vector3d(r.Nrm[3 * i + 0], r.Nrm[3 * i + 1], r.Nrm[3 * i + 2]);
            normals.Add(nv);
            if (i < np)
                outCloud.Add(new Point3d(r.Pts[3 * i + 0], r.Pts[3 * i + 1], r.Pts[3 * i + 2]), nv);
        }
        da.SetDataList(0, normals);
        da.SetData(1, r.Report);
        da.SetData(2, outCloud);
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        da.SetData(1, message);
    }
}
