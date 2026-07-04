#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// PythonSubprocessFractureDetector -- concrete IFractureDetector backend that
// shells out to a Python script (typically a thin wrapper around GeoFractNet)
// and reads CSV traces from its stdout.
//
// Contract for the Python script:
//   - argv[1] = absolute path to the image
//   - argv[2] = GSD (metres per pixel)
//   - argv[3] = origin X (world metres)
//   - argv[4] = origin Y (world metres)
//   - stdout = CSV with columns x1,y1,x2,y2 (world metres), one trace per row
//   - non-zero exit code = error; stderr captured to the wrapper exception
//
// Example minimal Python wrapper (saved as `run_geofractnet.py` in your repo):
//
//   import sys, csv, torch
//   from PIL import Image
//   from geofractnet_model import GeoFractNet  # user code
//   img = Image.open(sys.argv[1])
//   gsd = float(sys.argv[2])
//   ox, oy = float(sys.argv[3]), float(sys.argv[4])
//   model = GeoFractNet.load("weights/best.pth")
//   mask = model.infer(img)                          # 0/1 binary mask
//   traces = vectorise_mask(mask)                     # [(px1,py1,px2,py2), ...]
//   w = csv.writer(sys.stdout)
//   w.writerow(["x1","y1","x2","y2"])
//   for (a,b,c,d) in traces:
//       w.writerow([ox + a*gsd, oy - b*gsd, ox + c*gsd, oy - d*gsd])
//
// Phase 11.5 concrete backend; the contract lives in PhotogrammetryContract.cs.
// =============================================================================

public sealed class PythonSubprocessFractureDetector : IFractureDetector
{
    public PythonSubprocessFractureDetector(
        string pythonExePath,
        string scriptPath,
        int timeoutSeconds = 300,
        string workingDirectory = null,
        IReadOnlyDictionary<string, string> envOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(pythonExePath)) throw new ArgumentException(nameof(pythonExePath));
        if (string.IsNullOrWhiteSpace(scriptPath)) throw new ArgumentException(nameof(scriptPath));
        if (timeoutSeconds < 1) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

        PythonExePath = pythonExePath;
        ScriptPath = scriptPath;
        TimeoutSeconds = timeoutSeconds;
        WorkingDirectory = workingDirectory;
        EnvOverrides = envOverrides;
    }

    public string PythonExePath { get; }
    public string ScriptPath { get; }
    public int TimeoutSeconds { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyDictionary<string, string> EnvOverrides { get; }

    public string BackendName => "python-subprocess";

    public IReadOnlyList<FractureTrace> Detect(string imagePath, ImageToWorldMap map)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) throw new ArgumentException(nameof(imagePath));
        if (map == null) throw new ArgumentNullException(nameof(map));
        if (!File.Exists(imagePath)) throw new FileNotFoundException(imagePath);
        if (!File.Exists(ScriptPath)) throw new FileNotFoundException(ScriptPath);
        if (!File.Exists(PythonExePath) && !LooksLikePathLessBinary(PythonExePath))
            throw new FileNotFoundException(PythonExePath);

        var psi = new ProcessStartInfo
        {
            FileName = PythonExePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(WorkingDirectory)) psi.WorkingDirectory = WorkingDirectory;
        if (EnvOverrides != null)
        {
            foreach (var kv in EnvOverrides) psi.EnvironmentVariables[kv.Key] = kv.Value;
        }
        // net48: ProcessStartInfo.ArgumentList is netcore-only. Build a
        // single quoted Arguments string instead.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        psi.Arguments = string.Format(inv,
            "\"{0}\" \"{1}\" {2} {3} {4}",
            ScriptPath,
            imagePath,
            map.GsdMetresPerPx.ToString(inv),
            map.OriginX.ToString(inv),
            map.OriginY.ToString(inv));

        string stdout, stderr;
        int exitCode;
        using (var proc = Process.Start(psi))
        {
            if (proc == null) throw new InvalidOperationException("Process.Start returned null");
            // Drain both streams async, THEN wait: a synchronous stdout+stderr ReadToEnd before
            // WaitForExit deadlocks on a chatty child and defeats the timeout on a hang.
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(TimeoutSeconds * 1000))
            {
                try { proc.Kill(); } catch { /* best effort; net48 has no Kill(bool) */ }
                throw new TimeoutException(
                    $"Python detector did not finish within {TimeoutSeconds}s (script={ScriptPath})");
            }
            stdout = outTask.Result;
            stderr = errTask.Result;
            exitCode = proc.ExitCode;
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Python detector failed with exit {exitCode}: {stderr}");
        }

        return ParseTraces(stdout);
    }

    private static bool LooksLikePathLessBinary(string s)
    {
        // accept bare "python" or "python3" as a PATH-resolved name
        return s == "python" || s == "python3" || s == "py";
    }

    /// <summary>
    /// Parse CSV produced by the Python wrapper (or any compatible source).
    /// Public for unit tests + direct use without a subprocess. CSV columns:
    /// <c>x1, y1, x2, y2</c> in WORLD metres. Header row optional.
    /// </summary>
    public static IReadOnlyList<FractureTrace> ParseTraces(string csv)
    {
        var list = new List<FractureTrace>();
        if (string.IsNullOrWhiteSpace(csv)) return list;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        bool headerSeen = false;
        using (var sr = new StringReader(csv))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(',');
                if (parts.Length < 4) continue;
                if (!headerSeen
                    && !double.TryParse(parts[0], System.Globalization.NumberStyles.Any, inv, out _))
                {
                    headerSeen = true;
                    continue;
                }
                if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, inv, out double x1)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, inv, out double y1)
                    && double.TryParse(parts[2], System.Globalization.NumberStyles.Any, inv, out double x2)
                    && double.TryParse(parts[3], System.Globalization.NumberStyles.Any, inv, out double y2))
                {
                    list.Add(new FractureTrace(x1, y1, x2, y2));
                }
            }
        }
        return list;
    }
}
