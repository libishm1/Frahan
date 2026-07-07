#nullable disable
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// LoadE57CloudComponent — "Load E57 Cloud".
//
// E57 is the standard registered-terrestrial-LiDAR exchange format, but there is
// no managed .NET E57 reader and a multi-GB scan must not be parsed in-process
// inside Rhino (OOM + native-fault risk). So this component shells out to a
// Python worker (frahan_e57_worker.py) — the same out-of-process pattern as
// frahan_recon_worker.exe / PythonSubprocessFractureDetector. The worker reads
// the scans, voxel-downsamples, and writes a compact binary PLY. The PLY is then
// read back HERE in chunks and assembled into ONE RhinoCommon PointCloud, so the
// canvas gets a single cloud object, never millions of loose points.
//
// Coordinates are shifted to near the origin (the worker subtracts an integer-
// metre global offset) so display stays precise even for projected/UTM scans;
// the Shift output recovers the original georeferenced position.
//
// NON-BLOCKING: AsyncScanComponent + a "Run" gate (default false). The worker
// run and the chunked ingest happen on a background thread.
// =============================================================================

[Algorithm("Out-of-process E57 read (Python worker) + voxel downsample, chunked into one PointCloud",
    "pye57 worker -> binary PLY -> chunked PointCloud assembly",
    Note = "Frahan-original; subprocess isolates the E57 parse from Rhino, coords shifted to origin")]
public sealed class LoadE57CloudComponent
    : AsyncScanComponent<LoadE57CloudComponent.Snapshot, LoadE57CloudComponent.Payload>
{
    public LoadE57CloudComponent()
        : base("Load E57 Cloud", "LoadE57",
            "Read a registered terrestrial-LiDAR .e57 via an out-of-process " +
            "Python worker (pye57), voxel-downsample, and ingest the result in " +
            "chunks as a single PointCloud. The heavy parse runs in a subprocess " +
            "so a crash never takes down Rhino. Coordinates are shifted to the " +
            "origin (add the Shift output to georeference back). Runs on a " +
            "background thread (Run gate); needs python + pye57 + numpy on PATH " +
            "and frahan_e57_worker.py deployed beside the .gha.",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid => new Guid("E4F5A6B7-3230-4F5E-A6B7-C8D9E0F12345");
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("E57 File", "F",
            "Path to a .e57 registered point-cloud file.", GH_ParamAccess.item);
        p.AddNumberParameter("Voxel Size", "V",
            "Edge length of the cubic downsample voxel in model units (metres). " +
            "Default 0.05. If <= 0, no downsample (warns; can be very large).",
            GH_ParamAccess.item, 0.05);
        p.AddTextParameter("Python Exe", "Py",
            "Optional python interpreter (full path or bare name). Empty = " +
            "'python' on PATH.", GH_ParamAccess.item, string.Empty);
        // Appended LAST so existing canvases keep their wiring. Default false.
        p.AddBooleanParameter("Run", "R",
            "Set true to run the worker + ingest (on a background thread). " +
            "False = idle; nothing runs, the canvas never freezes.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGeometryParameter("Cloud", "C",
            "The downsampled scan as a single PointCloud (shifted to the origin). " +
            "Wire into a Point param to explode into points if needed.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Input Count", "Ni",
            "Total points in the E57 (all scans).", GH_ParamAccess.item);
        p.AddIntegerParameter("Output Count", "No",
            "Number of points after voxel downsample.", GH_ParamAccess.item);
        p.AddBoxParameter("Bounding Box", "B",
            "Axis-aligned box of the output cloud (shifted frame; bounds the Cloud).",
            GH_ParamAccess.item);
        p.AddVectorParameter("Shift", "S",
            "Global offset subtracted from the original coordinates. Add it back " +
            "to the Cloud (e.g. via Move) to restore the georeferenced position.",
            GH_ParamAccess.item);
        p.AddTextParameter("PLY Path", "Pf",
            "Path to the voxel-downsampled binary PLY the worker wrote (reusable).",
            GH_ParamAccess.item);
    }

    public sealed class Snapshot
    {
        public string E57Path;
        public double Voxel;
        public string PythonExe;
    }

    public sealed class Payload
    {
        // Flat xyz only on the background thread; the PointCloud is built in
        // EmitResult on the UI thread, never off-thread (the scan-ingest
        // convention — see ScanReconstructComponent).
        public float[] Xyz;
        public int Count;
        public E57CloudSummary Summary;
        public bool NoDownsampleWarn;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(3, ref run);
        if (!run) return true;

        string path = null;
        double voxel = 0.05;
        string py = string.Empty;
        if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No E57 file path provided.");
            return false;
        }
        da.GetData(1, ref voxel);
        da.GetData(2, ref py);
        if (!File.Exists(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {path}");
            return false;
        }
        if (!path.EndsWith(".e57", StringComparison.OrdinalIgnoreCase))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "File does not end in .e57; the worker will still try to read it.");
        snapshot = new Snapshot { E57Path = path, Voxel = voxel, PythonExe = py };
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        token.ThrowIfCancellationRequested();

        // Deterministic temp PLY (overwritten on re-run), keyed by name + voxel.
        int mm = (int)Math.Round(Math.Max(0.0, s.Voxel) * 1000.0);
        string stem = Path.GetFileNameWithoutExtension(s.E57Path);
        string outPly = Path.Combine(Path.GetTempPath(),
            $"{stem}.voxel{mm}mm.ply");

        progress("converting E57 (worker)...");
        if (!E57CloudWorker.TryConvert(
                s.E57Path, outPly, s.Voxel, s.PythonExe, scriptPath: null,
                out E57CloudSummary summary, out string err,
                timeoutMs: E57CloudWorker.DefaultTimeoutMs,
                progress: progress))
        {
            throw new InvalidOperationException(err);
        }
        token.ThrowIfCancellationRequested();

        progress($"ingesting {summary.OutputPoints:N0} points in chunks...");
        // Read the worker's PLY in chunks into ONE flat float[] (plain array,
        // background-thread safe). The PointCloud is assembled in EmitResult.
        long target = summary.OutputPoints;
        var xyz = new float[3 * Math.Max(0, target)];
        long offset = 0;
        PlyCloudReader.ReadFloatXyzChunks(outPly, 1_000_000, (buf, count) =>
        {
            token.ThrowIfCancellationRequested();
            int n = count * 3;
            if (offset + n > xyz.Length) n = (int)(xyz.Length - offset); // guard vs header drift
            if (n > 0) { Array.Copy(buf, 0, xyz, offset, n); offset += n; }
            if (target > 0)
                progress($"ingesting... {offset / 3:N0}/{target:N0} points");
        });

        return new Payload
        {
            Xyz = xyz,
            Count = (int)(offset / 3),
            Summary = summary,
            NoDownsampleWarn = !(s.Voxel > 0.0),
        };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload r)
    {
        if (r.NoDownsampleWarn)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Voxel Size <= 0: no downsampling. The full cloud is kept; this " +
                "can be very large for a multi-GB E57.");

        E57CloudSummary s = r.Summary;

        // Assemble the PointCloud on the UI thread from the flat xyz (chunked
        // read already done off-thread). AddRange in blocks keeps the transient
        // Point3d[] bounded rather than allocating one giant array.
        var cloud = new PointCloud();
        const int block = 1_000_000;
        var pts = new Point3d[block];
        for (int start = 0; start < r.Count; start += block)
        {
            int n = Math.Min(block, r.Count - start);
            if (n != pts.Length) pts = new Point3d[n];
            for (int i = 0; i < n; i++)
            {
                int b = 3 * (start + i);
                pts[i] = new Point3d(r.Xyz[b], r.Xyz[b + 1], r.Xyz[b + 2]);
            }
            cloud.AddRange(pts);
        }

        // Bounds in the shifted frame so the box bounds the (origin-shifted) cloud.
        var box = new BoundingBox(
            s.MinX - s.ShiftX, s.MinY - s.ShiftY, s.MinZ - s.ShiftZ,
            s.MaxX - s.ShiftX, s.MaxY - s.ShiftY, s.MaxZ - s.ShiftZ);

        da.SetData(0, cloud);
        da.SetData(1, s.InputPoints > int.MaxValue ? int.MaxValue : (int)s.InputPoints);
        da.SetData(2, s.OutputPoints);
        da.SetData(3, new Box(box));
        da.SetData(4, new Vector3d(s.ShiftX, s.ShiftY, s.ShiftZ));
        da.SetData(5, s.PlyPath);
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
    }
}
