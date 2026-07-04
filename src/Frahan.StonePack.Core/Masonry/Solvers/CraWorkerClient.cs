#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Solvers
{
    // =========================================================================
    // CraWorkerClient — run the CRA solve OUT OF PROCESS via frahan_cra_worker
    // (P2 wiring, 2026-07-02). The solve itself is Rhino-free, so the worker
    // hosts it standalone: the canvas thread never blocks on a big QP, a crash
    // in the native OSQP cannot take Rhino down, and long solves survive the
    // 300 s in-process ceilings. I/O is the CraAssemblyIO binary blob through
    // a private temp folder — the same arm's-length pattern as the other
    // workers. Falls back cleanly: callers use MasonryStabilityChecker
    // in-process when the worker exe is not deployed.
    // =========================================================================
    public static class CraWorkerClient
    {
        public static string LastError;

        public static bool Available() => ResolveExe() != null;

        private static string ResolveExe()
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(typeof(CraWorkerClient).Assembly.Location); }
            catch { }
            if (dir == null) return null;
            string p = Path.Combine(dir, "frahan_cra_worker.exe");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// Solve out of process. Returns false (with LastError) when the worker
        /// is missing, times out, or fails — callers then fall back in-process.
        /// </summary>
        public static bool TrySolve(MasonryAssembly assembly,
            double mu, int faceCount, bool inscribed, double tangentialScale, double gravityZ,
            int timeoutMs, out bool isStable, out int status, out double maxCompression, out string message)
        {
            isStable = false; status = 0; maxCompression = 0; message = null; LastError = null;
            string exe = ResolveExe();
            if (exe == null) { LastError = "frahan_cra_worker.exe not found beside the plugin"; return false; }

            string tmp = Path.Combine(Path.GetTempPath(), "frahan_cra_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                string inPath = Path.Combine(tmp, "assembly.bin");
                string outPath = Path.Combine(tmp, "verdict.bin");
                using (var fs = File.Create(inPath))
                    CraAssemblyIO.WriteAssembly(fs, assembly, mu, faceCount, inscribed, tangentialScale, gravityZ);

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--solve \"" + inPath + "\" \"" + outPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) { LastError = "failed to start frahan_cra_worker"; return false; }
                    // Drain BOTH streams asynchronously, THEN enforce the timeout. A blocking
                    // StandardOutput.ReadToEnd() before WaitForExit(timeoutMs) defeats the timeout:
                    // a hung native solve keeps stdout open, the read never returns, the deadline
                    // never fires, and the worker leaks (observed 2026-07-04 on the Guell CRA).
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        LastError = "CRA worker timed out after " + timeoutMs + " ms";
                        return false;
                    }
                    if (proc.ExitCode != 0)
                    {
                        LastError = "CRA worker exit " + proc.ExitCode + ". " + errTask.Result;
                        return false;
                    }
                }
                if (!File.Exists(outPath)) { LastError = "CRA worker produced no verdict"; return false; }
                using (var fs = File.OpenRead(outPath))
                    CraAssemblyIO.ReadResult(fs, out isStable, out status, out maxCompression, out message);
                return true;
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return false; }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
    }
}
