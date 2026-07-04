using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Discontinuity Sets (Async) -- the lag-free, scales-to-10M version of
/// Discontinuity Sets (Cloud). Runs a clean-room out-of-process C++ worker
/// (frahan_discontinuity_worker.exe: FACETS planar facets + DSE antipodal
/// mean-shift joint sets) on a background task so the canvas never freezes,
/// and stride-downsamples to a work budget so a 10M-point cloud is handled.
/// Feed a PointCloud, or a .ply path for very large clouds.
/// </summary>
[Algorithm("Planar-facet extraction", "FACETS (Dewez et al. 2016); Frahan clean-room C++ worker",
    Note = "Out-of-process; PCA normals + region-grow; no CloudCompare/GPL code.")]
[Algorithm("Joint-set clustering", "DSE (Riquelme et al. 2014); antipodal Watson mean-shift",
    Note = "Set count discovered; family-constrained normal spacing.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Cloud)", Reason = "The in-process managed twin (small clouds).")]
[RelatedComponent("Frahan > Quarry > BlockCutOpt Solve", Reason = "Consumes the discontinuity model.")]
public class DiscontinuitySetsAsyncComponent : GH_TaskCapableComponent<DiscontinuitySetsAsyncComponent.DiscResult>
{
    public DiscontinuitySetsAsyncComponent()
        : base("Discontinuity Sets (Async)", "DiscSetsA",
            "Lag-free, 10M-capable point-cloud -> joint sets. Runs a clean-room out-of-process worker on a " +
            "background task (canvas never blocks). Feed a PointCloud or a .ply path. Outputs the cloud coloured " +
            "by joint set + per-set dip / dip-direction / spacing.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10048-ED9E-4ED9-A048-ED9EED9E0048");
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public sealed class DiscResult
    {
        public PointCloud Segmented;
        public List<Line> Poles = new List<Line>();
        public List<double> Dip = new List<double>();
        public List<double> DipDir = new List<double>();
        public List<double> Spacing = new List<double>();
        public List<int> FacetCount = new List<int>();
        public List<double> Share = new List<double>();
        public List<Vector3d> _poles = new List<Vector3d>();
        public string FacetsPath = "";
        public string Report = "";
        public bool Ok;
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Cloud", "C", "Point cloud (PointCloud / mesh vertices). Optional if File is given.", GH_ParamAccess.item);
        pManager.AddTextParameter("File", "F", "Path to a .ply cloud (use for very large clouds instead of Cloud).", GH_ParamAccess.item);
        pManager.AddIntegerParameter("K", "K", "Neighbours for PCA normals.", GH_ParamAccess.item, 24);
        pManager.AddNumberParameter("Max angle", "A", "Region-grow normal agreement (deg).", GH_ParamAccess.item, 12.0);
        pManager.AddIntegerParameter("Min facet pts", "Mp", "Minimum points per facet.", GH_ParamAccess.item, 40);
        pManager.AddNumberParameter("Bandwidth", "Bw", "Mean-shift angular bandwidth (deg).", GH_ParamAccess.item, 15.0);
        pManager.AddIntegerParameter("Min set facets", "Ms", "Minimum facets per joint set.", GH_ParamAccess.item, 4);
        pManager.AddIntegerParameter("Max points", "Mx", "Work budget; clouds larger than this are stride-downsampled. Runs off-process so the canvas never blocks -- higher resolves more joint sets (6M ~ 10 s, full 8M ~ 15 s).", GH_ParamAccess.item, 6000000);
        pManager.AddBooleanParameter("Run", "R", "Set true to segment (runs off-process, async).", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Keep facets", "Kf", "Copy the worker's facets.csv (per-facet pole + set id) to a stable path and expose it on the 'Facets path' output, for the Stereonet + Block Size card.", GH_ParamAccess.item, false);
        pManager[0].Optional = true;
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGeometryParameter("Segmented", "S", "Cloud coloured by joint set (unassigned = grey).", GH_ParamAccess.item);
        pManager.AddLineParameter("Set poles", "P", "A pole line per joint set through the cloud centroid.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Dip", "D", "Per-set dip (deg).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Dip dir", "Dd", "Per-set dip-direction (deg).", GH_ParamAccess.list);
        pManager.AddNumberParameter("Spacing", "Sp", "Per-set mean normal spacing.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Facets/set", "Nf", "Per-set facet count.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Re", "Summary + timings.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Share", "Sh", "Per-set fraction of facet points (set dominance).", GH_ParamAccess.list);
        pManager.AddTextParameter("Facets path", "Fp", "Path to the copied facets.csv (empty unless 'Keep facets' is true). Feed the Stereonet + Block Size card.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            IGH_GeometricGoo goo = null; da.GetData(0, ref goo);
            string file = null; da.GetData(1, ref file);
            int k = 24; da.GetData(2, ref k);
            double ang = 12; da.GetData(3, ref ang);
            int minFp = 40; da.GetData(4, ref minFp);
            double bw = 15; da.GetData(5, ref bw);
            int minSf = 4; da.GetData(6, ref minSf);
            int maxPts = 6000000; da.GetData(7, ref maxPts);
            bool run = false; da.GetData(8, ref run);
            bool keep = false; da.GetData(9, ref keep);
            if (!run) { TaskList.Add(Task.FromResult(new DiscResult { Ok = false, Report = "Run is false." })); return; }

            var worker = FindWorker();
            if (worker == null) { TaskList.Add(Task.FromResult(new DiscResult { Ok = false, Report = "Worker exe not found beside the plug-in (frahan_discontinuity_worker.exe)." })); return; }

            // capture points (if a PointCloud was given) off the goo before the task
            List<Point3d> pts = goo != null ? ReadPoints(goo) : null;
            string capFile = file; int ck = Math.Max(6, k), cmf = Math.Max(10, minFp), cms = Math.Max(1, minSf), cmx = Math.Max(50000, maxPts);
            double ca = ang, cb = bw; bool ckeep = keep;

            TaskList.Add(Task.Run(() => RunWorker(worker, pts, capFile, ck, ca, cmf, cb, cms, cmx, ckeep)));
            return;
        }

        DiscResult r;
        if (!GetSolveResults(da, out r))
        {
            // synchronous fallback (non-async / headless context)
            IGH_GeometricGoo goo = null; da.GetData(0, ref goo);
            string file = null; da.GetData(1, ref file);
            int k = 24; da.GetData(2, ref k);
            double ang = 12; da.GetData(3, ref ang);
            int minFp = 40; da.GetData(4, ref minFp);
            double bw = 15; da.GetData(5, ref bw);
            int minSf = 4; da.GetData(6, ref minSf);
            int maxPts = 6000000; da.GetData(7, ref maxPts);
            bool run = false; da.GetData(8, ref run);
            bool keep = false; da.GetData(9, ref keep);
            if (!run) { da.SetData(6, "Run is false."); return; }
            var w = FindWorker();
            if (w == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Worker exe not found."); da.SetData(6, "Worker not found."); return; }
            var pts = goo != null ? ReadPoints(goo) : null;
            r = RunWorker(w, pts, file, Math.Max(6, k), ang, Math.Max(10, minFp), bw, Math.Max(1, minSf), Math.Max(50000, maxPts), keep);
        }
        if (r == null) return;
        if (!r.Ok)
        {
            if (!string.IsNullOrEmpty(r.Report)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Report);
            da.SetData(6, r.Report);
            return;
        }
        da.SetData(0, r.Segmented);
        da.SetDataList(1, r.Poles);
        da.SetDataList(2, r.Dip);
        da.SetDataList(3, r.DipDir);
        da.SetDataList(4, r.Spacing);
        da.SetDataList(5, r.FacetCount);
        da.SetData(6, r.Report);
        da.SetDataList(7, r.Share);
        da.SetData(8, r.FacetsPath);
    }

    // ---- runs on a background thread; blocks only this task, not the canvas ----
    private static DiscResult RunWorker(string worker, List<Point3d> pts, string file,
        int k, double ang, int minFp, double bw, int minSf, int maxPts, bool keepFacets = false)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "frahan_disc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string inPath = file;
            if (string.IsNullOrEmpty(inPath))
            {
                if (pts == null || pts.Count == 0) return new DiscResult { Ok = false, Report = "No Cloud and no File." };
                inPath = Path.Combine(tmp, "in.ply");
                WriteBinaryPly(inPath, pts);
            }
            if (!File.Exists(inPath)) return new DiscResult { Ok = false, Report = "Cloud file not found: " + inPath };

            var args = $"--in \"{inPath}\" --out \"{tmp}\" --k {k} --angle {ang.ToString(CultureInfo.InvariantCulture)} " +
                       $"--minfacet {minFp} --bw {bw.ToString(CultureInfo.InvariantCulture)} --minset {minSf} --maxpts {maxPts} --segply";
            var psi = new ProcessStartInfo
            {
                FileName = worker, Arguments = args, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
            };
            using (var proc = Process.Start(psi))
            {
                // Drain both streams async, THEN wait: a synchronous stdout+stderr ReadToEnd before
                // WaitForExit deadlocks on a chatty child and defeats the timeout on a hang.
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(600000)) { try { proc.Kill(); } catch { } return new DiscResult { Ok = false, Report = "Worker timed out (>600 s)." }; }
                _ = outTask.Result; _ = errTask.Result;   // flush the async readers
                if (proc.ExitCode != 0) return new DiscResult { Ok = false, Report = "Worker exit code " + proc.ExitCode + "." };
            }

            string jsonPath = Path.Combine(tmp, "discontinuity.json");
            string segPath = Path.Combine(tmp, "segmented.ply");
            if (!File.Exists(jsonPath)) return new DiscResult { Ok = false, Report = "Worker produced no result." };

            var res = ParseJson(File.ReadAllText(jsonPath));
            if (File.Exists(segPath)) res.Segmented = ReadSegPly(segPath);
            // optionally keep facets.csv (the temp dir is deleted in finally)
            if (keepFacets)
            {
                string facetsSrc = Path.Combine(tmp, "facets.csv");
                if (File.Exists(facetsSrc))
                {
                    string stable = Path.Combine(Path.GetTempPath(), "frahan_facets_" + Guid.NewGuid().ToString("N") + ".csv");
                    try { File.Copy(facetsSrc, stable, true); res.FacetsPath = stable; } catch { }
                }
            }
            // pole lines through the segmented-cloud centroid
            if (res.Segmented != null && res.Segmented.Count > 0)
            {
                var bb = new BoundingBox(res.Segmented.GetPoints());
                var c = bb.Center; double len = bb.Diagonal.Length * 0.25;
                foreach (var pole in res._poles) res.Poles.Add(new Line(c - pole * len, c + pole * len));
            }
            res.Ok = true;
            return res;
        }
        catch (Exception ex) { return new DiscResult { Ok = false, Report = "Worker error: " + ex.Message }; }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private static List<Point3d> ReadPoints(IGH_GeometricGoo goo)
    {
        var g = goo.ScriptVariable();
        if (g is PointCloud pc) return pc.GetPoints().ToList();
        if (g is Mesh m) return m.Vertices.ToPoint3dArray().ToList();
        if (g is Point3d p) return new List<Point3d> { p };
        return null;
    }

    private static void WriteBinaryPly(string path, List<Point3d> pts)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            string hdr = "ply\nformat binary_little_endian 1.0\nelement vertex " + pts.Count +
                         "\nproperty float x\nproperty float y\nproperty float z\nend_header\n";
            bw.Write(System.Text.Encoding.ASCII.GetBytes(hdr));
            foreach (var p in pts) { bw.Write((float)p.X); bw.Write((float)p.Y); bw.Write((float)p.Z); }
        }
    }

    private static PointCloud ReadSegPly(string path)
    {
        var by = File.ReadAllBytes(path);
        int p = 0; long nv = 0;
        Func<string> rl = () => { var sb = new System.Text.StringBuilder(); while (p < by.Length) { byte b = by[p++]; if (b == '\n') break; if (b != '\r') sb.Append((char)b); } return sb.ToString(); };
        while (true) { var l = rl(); if (l.StartsWith("element vertex")) nv = long.Parse(l.Split(' ')[2]); else if (l == "end_header") break; if (p >= by.Length) break; }
        int stride = 15;
        var pc = new PointCloud();
        for (long i = 0; i < nv; i++)
        {
            long b = p + i * stride;
            float x = BitConverter.ToSingle(by, (int)b), y = BitConverter.ToSingle(by, (int)b + 4), z = BitConverter.ToSingle(by, (int)b + 8);
            pc.Add(new Point3d(x, y, z), Color.FromArgb(by[(int)b + 12], by[(int)b + 13], by[(int)b + 14]));
        }
        return pc;
    }

    private static DiscResult ParseJson(string json)
    {
        var r = new DiscResult();
        var re = new Regex(@"\""dip\"":\s*([-\d.]+),\s*\""dipdir\"":\s*([-\d.]+),\s*\""pole\"":\s*\[([-\d.]+),([-\d.]+),([-\d.]+)\],\s*\""facets\"":\s*(\d+),\s*\""point_share\"":\s*([-\d.]+),\s*\""spacing\"":\s*([-\d.]+)");
        foreach (Match m in re.Matches(json))
        {
            double D(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            r.Dip.Add(D(1)); r.DipDir.Add(D(2));
            r._poles.Add(new Vector3d(D(3), D(4), D(5)));
            r.FacetCount.Add(int.Parse(m.Groups[6].Value));
            r.Share.Add(D(7));
            r.Spacing.Add(D(8));
        }
        var rf = Regex.Match(json, @"\""facets\"":\s*(\d+),\s*\""sets\"":\s*(\d+)");
        var rt = Regex.Match(json, @"\""ms_total\"":\s*(\d+)");
        var rp = Regex.Match(json, @"\""points_in\"":\s*(\d+),\s*\""points_used\"":\s*(\d+)");
        r.Report = $"{(rp.Success ? rp.Groups[2].Value : "?")} of {(rp.Success ? rp.Groups[1].Value : "?")} pts -> " +
                   $"{(rf.Success ? rf.Groups[1].Value : "?")} facets, {r.Dip.Count} joint sets, {(rt.Success ? rt.Groups[1].Value : "?")} ms (off-process). " +
                   string.Join("; ", r.Dip.Select((d, i) => $"J{i + 1}: {d:F0}/{r.DipDir[i]:F0} n={r.FacetCount[i]} sp={r.Spacing[i]:F2}"));
        return r;
    }

    private string FindWorker()
    {
        var cands = new List<string>();
        try { cands.Add(Path.GetDirectoryName(typeof(Frahan.Core.Discontinuity.FacetExtractor).Assembly.Location)); } catch { }
        try { cands.Add(Path.GetDirectoryName(GetType().Assembly.Location)); } catch { }
        foreach (var d in cands.Where(x => x != null).Distinct())
        {
            var w = Path.Combine(d, "frahan_discontinuity_worker.exe"); if (File.Exists(w)) return w;
            var w2 = Path.Combine(d, "Frahan.StonePack.MeshHeightmap", "frahan_discontinuity_worker.exe"); if (File.Exists(w2)) return w2;
        }
        return null;
    }
}
