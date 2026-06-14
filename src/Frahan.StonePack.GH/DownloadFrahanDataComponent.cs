#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;

namespace Frahan.GH;

// =============================================================================
// Download Frahan Data (2026-05-28). Fetches the OPTIONAL plugin data that is
// too big to ship inside the .gha / Yak package, on demand, from a GitHub
// Release (or any URL host), into the deploy folder beside this .gha, and
// SHA-256-verifies each file. Keeps the plugin install lean.
//
//   * Port (kintsugi.bin + the TorchSharp/CUDA runtime) -> enables Kintsugi
//     Mode=Port. GPL-3.0; only needed for the learned path.
//   * Examples -> sample meshes / a GPR file / .gh cards to try the plugin.
//
// Manifest format (plain text, one asset per line; '#' comments):
//   base_url=https://github.com/<owner>/<repo>/releases/download/<tag>/
//   port|kintsugi.bin|<sha256>|no
//   port|frahan-port-runtime.zip|<sha256>|yes
//   examples|examples.zip|<sha256>|yes
// (last field = extract zip into the deploy folder?). Files already present
// with a matching hash are skipped.
// =============================================================================

[RelatedComponent("Frahan > Kintsugi > Frahan Kintsugi", Reason = "Mode=Port needs kintsugi.bin + the torch/CUDA runtime this fetches.")]
[Algorithm("On-demand data fetch + SHA-256 verify",
    "Frahan-original distribution helper",
    Note = "Downloads optional large assets (Port weights/runtime, examples) from a release manifest; verifies hashes.")]
[DesignApplication(
    "Download the optional large plugin data (Kintsugi Mode=Port weights +  torch/CUDA runtime, and/or examples)...",
    DesignFlow.Bridges,
    Precedent = "Frahan-original dataset bootstrap (ETH1100, Granite Dells, Tongjiang per reference_quarry_scan_datasets)")]
public sealed class DownloadFrahanDataComponent : FrahanComponentBase
{
    public DownloadFrahanDataComponent()
        : base("Download Frahan Data", "GetData",
            "Download the optional large plugin data (Kintsugi Mode=Port weights + " +
            "torch/CUDA runtime, and/or examples) from a release manifest into the " +
            "folder beside the .gha, with SHA-256 verification. Runs on a background " +
            "thread; the canvas stays responsive. Files already present and verified " +
            "are skipped.",
            "Frahan", "Lab")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A08-1A2B-4C3D-9E4F-5A6B7C8D9E08");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Manifest URL", "U",
            "URL of the data manifest (plain text; see component help). Usually a " +
            "manifest.txt attached to the GitHub Release.", GH_ParamAccess.item);
        p.AddIntegerParameter("What", "W",
            "0 = Port (kintsugi.bin + torch/CUDA runtime), 1 = Examples, 2 = All. Default 0.",
            GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Run", "R", "Set true to download.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Port Ready", "Ok",
            "True if kintsugi.bin is present beside the .gha (Mode=Port can run).", GH_ParamAccess.item);
        p.AddTextParameter("Installed", "I", "Files installed / skipped this run.", GH_ParamAccess.list);
        p.AddTextParameter("Report", "R", "Status summary.", GH_ParamAccess.item);
    }

    private readonly object _gate = new object();
    private Task _task;
    private volatile string _progress = "";
    private string _report;
    private List<string> _installed;
    private bool _done;

    private static string DeployDir =>
        Path.GetDirectoryName(typeof(DownloadFrahanDataComponent).Assembly.Location);

    private static bool PortReady() =>
        File.Exists(Path.Combine(DeployDir ?? ".", "kintsugi.bin"));

    protected override void SolveSafe(IGH_DataAccess da)
    {
        string url = null; int what = 0; bool run = false;
        da.GetData(0, ref url); da.GetData(1, ref what); da.GetData(2, ref run);

        if (!run)
        {
            da.SetData(0, PortReady());
            da.SetData(2, PortReady()
                ? "kintsugi.bin present. Set Run to (re)check/download other assets."
                : "kintsugi.bin NOT present (Mode=Port unavailable). Provide a Manifest URL + Run to download.");
            return;
        }

        lock (_gate)
        {
            if (_done)
            {
                _done = false;
                da.SetData(0, PortReady());
                da.SetDataList(1, _installed ?? new List<string>());
                da.SetData(2, _report ?? "done");
                Message = "done";
                return;
            }
            if (_task != null && !_task.IsCompleted)
            {
                Message = _progress;
                da.SetData(0, PortReady());
                da.SetData(2, "Downloading... " + _progress + "  (canvas stays responsive).");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Manifest URL required.");
            return;
        }
        string deploy = DeployDir;
        if (deploy == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cannot locate deploy folder."); return; }

        var doc = OnPingDocument();
        Guid iguid = InstanceGuid;
        string capturedUrl = url; int capturedWhat = what;
        _progress = "starting..."; Message = _progress; _done = false;

        _task = Task.Run(() =>
        {
            var log = new List<string>();
            string report;
            try { report = RunDownload(capturedUrl, capturedWhat, deploy, log, s => _progress = s); }
            catch (Exception ex) { report = "FAILED: " + ex.GetType().Name + ": " + ex.Message; }
            lock (_gate) { _installed = log; _report = report; _done = true; }
            try { doc?.ScheduleSolution(10, d =>
            { if (d?.FindComponent(iguid) is GH_Component c) c.ExpireSolution(true); }); } catch { }
        });

        Message = "started";
        da.SetData(0, PortReady());
        da.SetData(2, "Started. Watch the label for progress; results pop in when done.");
    }

    private static string RunDownload(string manifestUrl, int what, string deploy,
                                      List<string> log, Action<string> progress)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        progress("fetching manifest...");
        string manifest;
        using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "frahan-getdata/1.0"); manifest = wc.DownloadString(manifestUrl); }

        string baseUrl = "";
        var assets = new List<(string cat, string name, string sha, bool extract)>();
        foreach (var raw in manifest.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("base_url=")) { baseUrl = line.Substring("base_url=".Length).Trim(); continue; }
            var f = line.Split('|');
            if (f.Length < 4) continue;
            assets.Add((f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim().ToLowerInvariant().StartsWith("y")));
        }

        int got = 0, skipped = 0;
        foreach (var a in assets)
        {
            bool want = what == 2
                || (what == 0 && a.cat.Equals("port", StringComparison.OrdinalIgnoreCase))
                || (what == 1 && a.cat.Equals("examples", StringComparison.OrdinalIgnoreCase));
            if (!want) continue;

            string dest = Path.Combine(deploy, a.name);
            if (!a.extract && File.Exists(dest) && HashMatches(dest, a.sha))
            { log.Add("skip (verified): " + a.name); skipped++; continue; }

            progress("downloading " + a.name + " ...");
            string tmp = Path.Combine(Path.GetTempPath(), "frahan_" + Guid.NewGuid().ToString("N") + "_" + a.name);
            using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "frahan-getdata/1.0"); wc.DownloadFile(baseUrl + Uri.EscapeUriString(a.name), tmp); }

            if (!string.IsNullOrWhiteSpace(a.sha) && !HashMatches(tmp, a.sha))
            { try { File.Delete(tmp); } catch { } log.Add("HASH MISMATCH (rejected): " + a.name); continue; }

            if (a.extract)
            {
                progress("extracting " + a.name + " ...");
                using (var zip = ZipFile.OpenRead(tmp))
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue; // dir entry
                        string outPath = Path.Combine(deploy, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                        e.ExtractToFile(outPath, overwrite: true);
                    }
                try { File.Delete(tmp); } catch { }
                log.Add("installed (unzipped): " + a.name);
            }
            else
            {
                File.Copy(tmp, dest, overwrite: true);
                try { File.Delete(tmp); } catch { }
                log.Add("installed: " + a.name);
            }
            got++;
        }
        return $"Downloaded {got}, skipped {skipped} (already verified). " +
               (PortReady() ? "kintsugi.bin present -> Mode=Port ready." : "kintsugi.bin still missing.");
    }

    private static bool HashMatches(string path, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return true; // no hash to check
        try
        {
            using (var s = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var hex = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                return string.Equals(hex, expectedHex.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            }
        }
        catch { return false; }
    }
}
