#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Frahan.Core.ScanIngest;

/// <summary>
/// Runs surface reconstruction in a separate process (frahan_recon_worker.exe)
/// so a hard native crash (e.g. a C++ abort 0xC0000409 from geogram/CGAL inside
/// a host with FP exceptions unmasked, or any access violation) takes down only
/// the worker, never the host (Rhino). The crash is detected via the worker's
/// exit code / missing output magic and surfaced as a clean managed error.
///
/// If the worker exe is not deployed beside this assembly, falls back to the
/// in-process <see cref="ReconstructionNative"/> path (which is itself
/// FP-exception-guarded), with a note in <c>error</c>.
/// </summary>
public static class OutOfProcessReconstructor
{
    private const int MAGIC = 0x46524543; // 'FREC'
    public const int DefaultTimeoutMs = 300_000;

    /// <summary>Modes: 1=AlphaShape(CGAL) 2=Poisson(geogram) 3=AdvancingFront(CGAL) 4=Poisson(CGAL).</summary>
    public static bool TryReconstruct(
        int mode, double[] points, double[] normals,
        double alpha, int depth, double spn, double rr, double bt,
        out double[] verts, out int[] tris, out string error,
        int timeoutMs = DefaultTimeoutMs)
    {
        verts = null; tris = null; error = null;
        if (points == null || points.Length % 3 != 0 || points.Length < 12)
        { error = "points must be non-null, length divisible by 3, >= 4 points"; return false; }
        if ((mode == 2 || mode == 4) && (normals == null || normals.Length != points.Length))
        { error = "Poisson requires oriented normals (same length as points)"; return false; }

        string workerDir = null;
        try { workerDir = Path.GetDirectoryName(typeof(OutOfProcessReconstructor).Assembly.Location); }
        catch { }
        string worker = workerDir == null ? null : Path.Combine(workerDir, "frahan_recon_worker.exe");

        if (worker == null || !File.Exists(worker))
        {
            // Graceful fallback: in-process (FP-guarded) reconstruction.
            return InProcessFallback(mode, points, normals, alpha, depth, spn, rr, bt,
                                     out verts, out tris, ref error,
                                     workerMissing: true);
        }

        string inPath = Path.Combine(Path.GetTempPath(), "frahan_recon_" + Guid.NewGuid().ToString("N") + ".in");
        string outPath = inPath + ".out";
        try
        {
            WriteInput(inPath, mode, points, normals, alpha, depth, spn, rr, bt);

            var psi = new ProcessStartInfo
            {
                FileName = worker,
                Arguments = "\"" + inPath + "\" \"" + outPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workerDir,   // native shim DLLs live here
            };
            using (var proc = Process.Start(psi))
            {
                if (proc == null) { error = "failed to start reconstruction worker"; return false; }
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    error = $"reconstruction worker timed out after {timeoutMs} ms";
                    return false;
                }
                int code = proc.ExitCode;
                if (!File.Exists(outPath))
                {
                    error = $"reconstruction worker crashed (exit 0x{code:X8}); no output produced. " +
                            "The reconstruction backend faulted; Rhino is unaffected.";
                    return false;
                }
                if (!ReadOutput(outPath, out verts, out tris, out string werr))
                {
                    error = werr ?? $"reconstruction worker failed (exit 0x{code:X8})";
                    return false;
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            error = $"out-of-process reconstruction error: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
        finally
        {
            try { if (File.Exists(inPath)) File.Delete(inPath); } catch { }
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
        }
    }

    private static bool InProcessFallback(
        int mode, double[] points, double[] normals,
        double alpha, int depth, double spn, double rr, double bt,
        out double[] verts, out int[] tris, ref string error, bool workerMissing)
    {
        verts = null; tris = null;
        ReconstructionResult r; string e;
        bool ok;
        switch (mode)
        {
            case 1: ok = ReconstructionNative.TryAlphaShape3(points, alpha, out r, out e); break;
            case 2: ok = ReconstructionNative.TryPoisson(points, normals, depth, spn, out r, out e); break;
            case 3: ok = ReconstructionNative.TryAdvancingFront(points, rr, bt, out r, out e); break;
            case 4: ok = ReconstructionNative.TryPoissonCgal(points, normals, out r, out e); break;
            default: error = $"unknown mode {mode}"; return false;
        }
        if (!ok) { error = (workerMissing ? "[in-process fallback] " : "") + e; return false; }
        verts = r.Vertices; tris = r.Triangles;
        return true;
    }

    private static void WriteInput(string path, int mode, double[] pts, double[] nrm,
                                   double alpha, int depth, double spn, double rr, double bt)
    {
        int nPts = pts.Length / 3;
        using (var bw = new BinaryWriter(File.Create(path)))
        {
            bw.Write(mode);
            bw.Write(alpha);
            bw.Write(depth);
            bw.Write(spn);
            bw.Write(rr);
            bw.Write(bt);
            bw.Write(nPts);
            var pb = new byte[pts.Length * sizeof(double)];
            Buffer.BlockCopy(pts, 0, pb, 0, pb.Length); bw.Write(pb);
            bool hasN = nrm != null && nrm.Length == pts.Length;
            bw.Write(hasN ? 1 : 0);
            if (hasN)
            {
                var nb = new byte[nrm.Length * sizeof(double)];
                Buffer.BlockCopy(nrm, 0, nb, 0, nb.Length); bw.Write(nb);
            }
        }
    }

    private static bool ReadOutput(string path, out double[] verts, out int[] tris, out string error)
    {
        verts = null; tris = null; error = null;
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 8 || BitConverter.ToInt32(b, 0) != MAGIC)
        {
            error = "reconstruction worker produced no valid output (likely crashed mid-run)";
            return false;
        }
        int o = 4;
        int status = BitConverter.ToInt32(b, o); o += 4;
        if (status != 0)
        {
            int el = BitConverter.ToInt32(b, o); o += 4;
            error = "reconstruction failed: " + Encoding.UTF8.GetString(b, o, Math.Min(el, b.Length - o));
            return false;
        }
        int vc = BitConverter.ToInt32(b, o); o += 4;
        verts = new double[3 * vc];
        Buffer.BlockCopy(b, o, verts, 0, 3 * vc * sizeof(double)); o += 3 * vc * sizeof(double);
        int tc = BitConverter.ToInt32(b, o); o += 4;
        tris = new int[3 * tc];
        Buffer.BlockCopy(b, o, tris, 0, 3 * tc * sizeof(int)); o += 3 * tc * sizeof(int);
        return true;
    }
}
