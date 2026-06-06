#nullable disable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Frahan.Core.ScanIngest;

/// <summary>
/// Summary returned by the E57 worker after it converts + voxel-downsamples an
/// E57 to a PLY. Coordinates in the PLY are SHIFTED by <see cref="Shift"/> (an
/// integer-metre global offset) so PLY float32 stays sub-mm accurate; add the
/// shift back to georeference. Bounds are in the ORIGINAL (unshifted) frame.
/// </summary>
public sealed class E57CloudSummary
{
    public long InputPoints;
    public long ValidPoints;
    public int OutputPoints;
    public double MinX, MinY, MinZ;
    public double MaxX, MaxY, MaxZ;
    public double ShiftX, ShiftY, ShiftZ;
    public string PlyPath;
}

/// <summary>
/// Runs the E57 -> voxel-downsampled PLY conversion in a Python subprocess
/// (frahan_e57_worker.py), mirroring <see cref="OutOfProcessReconstructor"/> and
/// PythonSubprocessFractureDetector: .NET has no managed E57 reader and parsing
/// a multi-GB scan in-process inside Rhino risks an OOM / native fault, so the
/// heavy read runs out-of-process. A crash kills only the worker.
///
/// The produced PLY (binary_little_endian, float x/y/z) is read back in chunks
/// by <see cref="PlyCloudReader"/> on the GH side so the cloud materialises as a
/// single RhinoCommon PointCloud, never thousands of loose points.
/// </summary>
public static class E57CloudWorker
{
    public const int DefaultTimeoutMs = 600_000;
    public const string WorkerScriptName = "frahan_e57_worker.py";

    /// <summary>
    /// Convert <paramref name="e57Path"/> to a voxel-downsampled PLY at
    /// <paramref name="outPlyPath"/> via the Python worker.
    /// </summary>
    /// <param name="pythonExe">Python interpreter. Null/empty auto-resolves
    /// ("python" on PATH). May be a full path or a bare name.</param>
    /// <param name="scriptPath">Worker .py. Null/empty auto-resolves beside this
    /// assembly, then a couple of source-tree fallbacks.</param>
    /// <param name="progress">Optional sink for the worker's PROGRESS lines.</param>
    public static bool TryConvert(
        string e57Path, string outPlyPath, double voxel,
        string pythonExe, string scriptPath,
        out E57CloudSummary summary, out string error,
        int timeoutMs = DefaultTimeoutMs, Action<string> progress = null)
    {
        summary = null; error = null;
        if (string.IsNullOrWhiteSpace(e57Path) || !File.Exists(e57Path))
        { error = "E57 file not found: " + e57Path; return false; }

        string python = ResolvePython(pythonExe);
        string script = ResolveScript(scriptPath, out string scriptErr);
        if (script == null) { error = scriptErr; return false; }

        var inv = CultureInfo.InvariantCulture;
        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = string.Format(inv, "\"{0}\" \"{1}\" \"{2}\" {3}",
                script, e57Path, outPlyPath, voxel.ToString("R", inv)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(script),
        };

        var stdout = new StringBuilder();
        var stderrTail = new StringBuilder();
        try
        {
            using (var proc = new Process { StartInfo = psi })
            {
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    if (e.Data.StartsWith("PROGRESS ", StringComparison.Ordinal))
                        progress?.Invoke(e.Data.Substring("PROGRESS ".Length));
                    else
                    {
                        stderrTail.AppendLine(e.Data);
                        if (stderrTail.Length > 4000) stderrTail.Remove(0, stderrTail.Length - 4000);
                    }
                };
                if (!proc.Start()) { error = "failed to start the E57 worker (" + python + ")"; return false; }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    error = $"E57 worker timed out after {timeoutMs} ms";
                    return false;
                }
                proc.WaitForExit(); // flush async readers
                int code = proc.ExitCode;
                if (!TryParseSummary(stdout.ToString(), out summary))
                {
                    error = $"E57 worker failed (exit {code}). " +
                            (stderrTail.Length > 0 ? stderrTail.ToString().Trim()
                                                   : "no SUMMARY line produced.");
                    return false;
                }
                summary.PlyPath = outPlyPath;
                if (!File.Exists(outPlyPath))
                { error = "E57 worker reported success but the PLY was not written: " + outPlyPath; return false; }
                return true;
            }
        }
        catch (Exception ex)
        {
            error = $"E57 worker error: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    // SUMMARY in valid out  minx miny minz  maxx maxy maxz  shiftx shifty shiftz
    private static bool TryParseSummary(string stdout, out E57CloudSummary s)
    {
        s = null;
        if (string.IsNullOrEmpty(stdout)) return false;
        var inv = CultureInfo.InvariantCulture;
        foreach (var raw in stdout.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("SUMMARY ", StringComparison.Ordinal)) continue;
            var t = line.Substring("SUMMARY ".Length)
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 12) return false;
            try
            {
                s = new E57CloudSummary
                {
                    InputPoints = long.Parse(t[0], inv),
                    ValidPoints = long.Parse(t[1], inv),
                    OutputPoints = int.Parse(t[2], inv),
                    MinX = double.Parse(t[3], inv), MinY = double.Parse(t[4], inv), MinZ = double.Parse(t[5], inv),
                    MaxX = double.Parse(t[6], inv), MaxY = double.Parse(t[7], inv), MaxZ = double.Parse(t[8], inv),
                    ShiftX = double.Parse(t[9], inv), ShiftY = double.Parse(t[10], inv), ShiftZ = double.Parse(t[11], inv),
                };
                return true;
            }
            catch { return false; }
        }
        return false;
    }

    private static string ResolvePython(string pythonExe)
    {
        if (!string.IsNullOrWhiteSpace(pythonExe)) return pythonExe;
        return "python"; // PATH-resolved; Windows also has the "py" launcher
    }

    private static string ResolveScript(string scriptPath, out string error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            if (File.Exists(scriptPath)) return scriptPath;
            error = "E57 worker script not found: " + scriptPath;
            return null;
        }
        // 1) beside this assembly (deployed next to the .gha)
        string asmDir = null;
        try { asmDir = Path.GetDirectoryName(typeof(E57CloudWorker).Assembly.Location); } catch { }
        if (asmDir != null)
        {
            string cand = Path.Combine(asmDir, WorkerScriptName);
            if (File.Exists(cand)) return cand;
            // 2) a "workers" subfolder beside the assembly
            cand = Path.Combine(asmDir, "workers", WorkerScriptName);
            if (File.Exists(cand)) return cand;
        }
        error = "E57 worker script (" + WorkerScriptName + ") not found beside the " +
                "assembly. Deploy it next to Frahan.StonePack.gha, or wire the " +
                "Script Path input.";
        return null;
    }
}
