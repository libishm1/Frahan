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
// VoxelDownsampleComponent — Phase I.6-I15 helper (UX architecture
// report §7.7.F). Buckets a point cloud into cubic voxels and emits
// the centroid of each non-empty cell. Native shim path (Geogram) when
// available; managed hash-grid fallback otherwise.
// =============================================================================

[Algorithm("Voxel-grid centroid downsample",
    "Voxel-grid filter: one centroid per occupied cubic cell",
    Note = "Native Geogram path when available; managed hash-grid fallback. Frahan-original impl.")]
[DesignApplication(
    "Reduce a point cloud by averaging points within each voxel",
    DesignFlow.Bridges,
    Precedent = "Standard voxel-grid downsampling (Open3D voxel_down_sample equivalent)")]
public sealed class VoxelDownsampleComponent : FrahanComponentBase
{
    public VoxelDownsampleComponent()
        : base("Voxel Downsample", "VoxelDown",
            "Reduce a point cloud by averaging points within each voxel. " +
            "Native Geogram path (Phase I shim) when available; managed " +
            "hash-grid fallback otherwise. Use upstream of Cloud ICP for " +
            "interactive ~10M+-point clouds.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3202-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Points", "P", "Input cloud.", GH_ParamAccess.list);
        p.AddNumberParameter("Voxel Size", "V",
            "Edge length of the cubic voxel in model units.",
            GH_ParamAccess.item, 0.05);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Downsampled", "D",
            "One centroid per non-empty voxel.", GH_ParamAccess.list);
        p.AddNumberParameter("Reduction Factor", "Rf",
            "Output count / input count.", GH_ParamAccess.item);
        p.AddIntegerParameter("Output Count", "N",
            "Number of centroids.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var pts = new List<Point3d>();
        double voxel = 0.05;
        if (!da.GetDataList(0, pts) || pts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need at least 1 point.");
            return;
        }
        da.GetData(1, ref voxel);
        if (!(voxel > 0.0))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Voxel Size must be > 0; got {voxel}.");
            return;
        }

        var flat = new double[3 * pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            flat[3*i + 0] = pts[i].X;
            flat[3*i + 1] = pts[i].Y;
            flat[3*i + 2] = pts[i].Z;
        }

        double[] outFlat = null;
        bool native = ReconstructionNative.TryVoxelDownsample(flat, voxel, out outFlat, out string err);
        if (!native)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Native shim unavailable ({err}); using managed hash-grid fallback.");
            outFlat = ManagedVoxelDownsample(flat, voxel);
        }

        int outN = outFlat.Length / 3;
        var outPts = new List<Point3d>(outN);
        for (int i = 0; i < outN; i++)
            outPts.Add(new Point3d(outFlat[3*i + 0], outFlat[3*i + 1], outFlat[3*i + 2]));

        da.SetDataList(0, outPts);
        da.SetData(1, (double)outN / pts.Count);
        da.SetData(2, outN);
    }

    /// <summary>
    /// Managed hash-grid voxel reducer, used when the native shim is
    /// unavailable. O(N) single pass; tens of millions of points are
    /// possible but slower than the Geogram native path.
    /// </summary>
    private static double[] ManagedVoxelDownsample(double[] flat, double voxel)
    {
        int n = flat.Length / 3;
        var cells = new Dictionary<(long, long, long), (double sx, double sy, double sz, long c)>(n / 2 + 1);
        for (int i = 0; i < n; i++)
        {
            double x = flat[3*i + 0], y = flat[3*i + 1], z = flat[3*i + 2];
            var key = ((long)Math.Floor(x / voxel),
                       (long)Math.Floor(y / voxel),
                       (long)Math.Floor(z / voxel));
            if (cells.TryGetValue(key, out var v))
                cells[key] = (v.sx + x, v.sy + y, v.sz + z, v.c + 1);
            else
                cells[key] = (x, y, z, 1);
        }
        var outFlat = new double[3 * cells.Count];
        int j = 0;
        foreach (var kv in cells)
        {
            double inv = 1.0 / kv.Value.c;
            outFlat[3*j + 0] = kv.Value.sx * inv;
            outFlat[3*j + 1] = kv.Value.sy * inv;
            outFlat[3*j + 2] = kv.Value.sz * inv;
            j++;
        }
        return outFlat;
    }
}
