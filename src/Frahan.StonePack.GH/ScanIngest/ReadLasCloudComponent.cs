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
// ReadLasCloudComponent — native .las / .laz point-cloud reader.
//
// Reads a LAS / LAZ LiDAR or TLS file via Unofficial.laszip.net (pure-managed
// LASzip port) and voxel-downsamples DURING the read with the same hash-grid
// as Load Cloud, so a 357M-point LAZ collapses to a manageable centroid cloud.
// The LAS header scale + offset are applied internally, so the points come out
// in real-world (UTM / project) coordinates.
//
// NON-BLOCKING (2026-05-28): a big LAZ read can take tens of seconds, so the
// read runs on a background thread via AsyncScanComponent. A "Run" gate
// (default false) means opening a definition never auto-loads a huge file.
//
// Deploy note: laszip.net.dll must be copied next to Frahan.StonePack.gha.
// =============================================================================

[Algorithm("LASzip LAS/LAZ decode", "Isenburg 2013 (LASzip lossless LiDAR compression)",
    Note = "Unofficial.laszip.net pure-managed port; LAS scale + offset applied")]
[Algorithm("Voxel-grid downsample (on read)", "Voxel-grid filter (one centroid per occupied cell)",
    Note = "Frahan-original; memory bounded by occupied voxels")]
public sealed class ReadLasCloudComponent
    : AsyncScanComponent<ReadLasCloudComponent.Snapshot, ReadLasCloudComponent.Payload>
{
    public ReadLasCloudComponent()
        : base("Read LAS Cloud", "ReadLAS",
            "Read a .las / .laz LiDAR or TLS point cloud and voxel-downsample " +
            "on the fly. Handles both uncompressed .las and compressed .laz. " +
            "Memory is bounded by occupied voxels, not the input point count, " +
            "so very large clouds (100M+ points) load without materialising " +
            "the full set. The LAS scale + offset are applied, so points are " +
            "in real-world coordinates. Runs on a background thread (Run gate); " +
            "the canvas stays responsive. Use upstream of Cloud ICP / Scale " +
            "Calibrate. [Isenburg 2013]",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3220-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File Path", "F",
            "Path to a .las (uncompressed) or .laz (compressed) point-cloud file.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Voxel Size", "V",
            "Edge length of the cubic downsample voxel in model units. " +
            "Default 0.05. If <= 0, no downsample (warns for huge files).",
            GH_ParamAccess.item, 0.05);
        // Appended LAST so existing canvases keep their wiring. Default false:
        // the file is NOT read until the user toggles Run, so opening a
        // definition never auto-loads a giant LAZ on the UI thread.
        p.AddBooleanParameter("Run", "R",
            "Set true to read the file (on a background thread). False = idle; " +
            "nothing is read, the canvas never freezes.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Points", "P",
            "One centroid per occupied voxel (or all points when Voxel Size <= 0). " +
            "Real-world coordinates (LAS scale + offset applied).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Input Count", "Ni",
            "Total points read from the file.", GH_ParamAccess.item);
        p.AddIntegerParameter("Output Count", "No",
            "Number of output points (occupied voxels).", GH_ParamAccess.item);
        p.AddBoxParameter("Bounding Box", "B",
            "Axis-aligned bounding box of the input cloud.", GH_ParamAccess.item);
        p.AddGeometryParameter("Cloud", "C",
            "Downsampled cloud as a single native PointCloud - fast viewport " +
            "display and bake-ready (far cheaper than the Points list for big " +
            "clouds). Wire into a Point param to explode it back into points.",
            GH_ParamAccess.item);
    }

    public sealed class Snapshot
    {
        public string Path;
        public double Voxel;
    }

    public sealed class Payload
    {
        public List<Point3d> Points;   // Point3d is a struct; safe to build off-thread
        public long InputCount;
        public int OutputCount;
        public bool HasBounds;
        public BoundingBox Bounds;
        public bool EmptyFile;
        public bool NoDownsampleWarn;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(2, ref run);
        if (!run) return true;

        string path = null;
        double voxel = 0.05;
        if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No file path provided.");
            return false;
        }
        da.GetData(1, ref voxel);
        if (!System.IO.File.Exists(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {path}");
            return false;
        }
        snapshot = new Snapshot { Path = path, Voxel = voxel };
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        progress($"reading {System.IO.Path.GetFileName(s.Path)}...");
        token.ThrowIfCancellationRequested();
        StreamingCloudResult r = LazCloudReader.ReadAndDownsample(s.Path, s.Voxel);
        token.ThrowIfCancellationRequested();

        progress("building cloud...");
        var flat = r.DownsampledXyz;
        int outN = flat.Length / 3;
        var pts = new List<Point3d>(outN);
        for (int i = 0; i < outN; i++)
            pts.Add(new Point3d(flat[3 * i + 0], flat[3 * i + 1], flat[3 * i + 2]));

        var payload = new Payload
        {
            Points = pts,
            InputCount = r.InputPointCount,
            OutputCount = outN,
            HasBounds = r.HasBounds,
            EmptyFile = r.InputPointCount == 0,
            NoDownsampleWarn = !(s.Voxel > 0.0),
        };
        if (r.HasBounds)
            payload.Bounds = new BoundingBox(r.MinX, r.MinY, r.MinZ, r.MaxX, r.MaxY, r.MaxZ);
        return payload;
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        if (r.NoDownsampleWarn)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Voxel Size <= 0: no downsampling. The full cloud is kept in " +
                "memory; LAS/LAZ files are often 100M+ points and can exhaust RAM.");
        if (r.EmptyFile)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "File contained no points.");

        da.SetDataList(0, r.Points);
        da.SetData(1, r.InputCount > int.MaxValue ? int.MaxValue : (int)r.InputCount);
        da.SetData(2, r.OutputCount);
        if (r.HasBounds) da.SetData(3, new Box(r.Bounds));
        da.SetData(4, new PointCloud(r.Points));   // RhinoCommon geometry built on UI thread
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
    }
}
