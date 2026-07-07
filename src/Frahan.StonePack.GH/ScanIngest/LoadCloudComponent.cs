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
// LoadCloudComponent — streaming "Load Cloud" reader (pure-managed).
//
// Reads a large point-cloud FILE (PLY binary_le / ascii, points-only OR
// mesh-vertex; plain ASCII XYZ / PTS) and voxel-downsamples DURING the read so
// the full cloud never materialises. Peak memory is bounded by the number of
// occupied voxels, not the input point count, so 28M+-point files load without
// exhausting RAM. Wire the Points output straight into Cloud ICP / Scale
// Calibrate / Scan Reconstruct.
//
// NON-BLOCKING (2026-05-28): the streaming read runs on a background thread via
// AsyncScanComponent, gated by a "Run" toggle (default false), so opening a
// definition never auto-loads a huge file on the UI thread.
// =============================================================================

[Algorithm("Streaming cloud read + voxel-grid downsample",
    "Voxel-grid filter (one centroid per occupied cell)",
    Note = "Frahan-original; forward-only PLY/XYZ/PTS read, memory bounded by occupied voxels not point count")]
public sealed class LoadCloudComponent
    : AsyncScanComponent<LoadCloudComponent.Snapshot, LoadCloudComponent.Payload>
{
    public LoadCloudComponent()
        : base("Load Cloud", "LoadCloud",
            "Stream a point cloud from a file and voxel-downsample on the " +
            "fly. Supports PLY (binary_little_endian + ascii; points-only " +
            "and mesh-vertex clouds) and plain ASCII XYZ / PTS. Memory is " +
            "bounded by occupied voxels, not the input point count, so very " +
            "large clouds (28M+ points) load without materialising the full " +
            "set. Pure-managed; no native dependency. Runs on a background " +
            "thread (Run gate) so the canvas stays responsive. Use upstream of " +
            "Cloud ICP / Scale Calibrate.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3210-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File Path", "F",
            "Path to a .ply / .xyz / .pts / .asc / .txt point-cloud file.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Voxel Size", "V",
            "Edge length of the cubic downsample voxel in model units. " +
            "Default 0.05. If <= 0, no downsample (warns for huge files).",
            GH_ParamAccess.item, 0.05);
        // Appended LAST so existing canvases keep their wiring. Default false.
        p.AddBooleanParameter("Run", "R",
            "Set true to read the file (on a background thread). False = idle; " +
            "nothing is read, the canvas never freezes.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Points", "P",
            "One centroid per occupied voxel (or all points when Voxel Size <= 0).",
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
        StreamingCloudResult r = StreamingCloudReader.ReadAndDownsample(s.Path, s.Voxel);
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
                "memory; this can be very large for big files.");
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
