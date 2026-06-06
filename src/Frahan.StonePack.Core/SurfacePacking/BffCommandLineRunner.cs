using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Frahan.Surface
{
    /// <summary>
    /// Runs the BFF (Boundary-First Flattening) command-line executable as an external process.
    /// stdout and stderr are consumed asynchronously on separate threads to prevent deadlock
    /// when the subprocess fills its OS pipe buffers.
    /// Download the executable from: https://github.com/GeometryCollective/boundary-first-flattening
    /// </summary>
    public sealed class BffCommandLineRunner
    {
        private readonly string _exePath;

        public BffCommandLineRunner(string exePath)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException($"BFF executable not found: {exePath}");
            _exePath = exePath;
        }

        /// <summary>
        /// Runs BFF and returns true when the output OBJ exists.
        /// Throws TimeoutException if the process exceeds timeoutMs.
        /// Throws InvalidOperationException on non-zero exit code.
        /// </summary>
        public async Task<bool> RunAsync(
            string inputObj,
            string outputObj,
            int cones = 0,
            bool normalizeUVs = true,
            int timeoutMs = 30000)
        {
            if (!File.Exists(inputObj))
                throw new FileNotFoundException($"BFF input OBJ not found: {inputObj}");

            var outDir = Path.GetDirectoryName(outputObj);
            if (!string.IsNullOrWhiteSpace(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var args = BuildArgs(inputObj, outputObj, cones, normalizeUVs);
            var errorLog = new StringBuilder();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (var cts = new CancellationTokenSource(timeoutMs))
            using (var proc = new Process())
            {
                proc.StartInfo.FileName = _exePath;
                proc.StartInfo.Arguments = args;
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.EnableRaisingEvents = true;

                // Consume stderr asynchronously — prevents pipe deadlock
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) errorLog.AppendLine(e.Data);
                };

                proc.Exited += (_, _2) =>
                {
                    if (proc.ExitCode == 0)
                        tcs.TrySetResult(true);
                    else
                        tcs.TrySetException(new InvalidOperationException(
                            $"BFF exited with code {proc.ExitCode}. stderr: {errorLog}"));
                };

                // Register timeout cancellation
                cts.Token.Register(() =>
                {
                    if (!proc.HasExited)
                    {
                        try { proc.Kill(); } catch { /* already exited */ }
                    }
                    tcs.TrySetException(new TimeoutException(
                        $"BFF process exceeded {timeoutMs} ms timeout."));
                });

                proc.Start();
                proc.BeginOutputReadLine(); // consume stdout asynchronously (discard content)
                proc.BeginErrorReadLine();  // consume stderr asynchronously

                await tcs.Task.ConfigureAwait(false);

                if (!File.Exists(outputObj))
                    throw new InvalidOperationException(
                        $"BFF reported success but output OBJ was not created: {outputObj}");

                return true;
            }
        }

        private static string BuildArgs(string input, string output, int cones, bool normalize)
        {
            var sb = new StringBuilder();
            sb.Append($"\"{input}\" \"{output}\"");
            if (cones > 0) sb.Append($" --nCones={cones}");
            if (normalize) sb.Append(" --normalizeUVs");
            return sb.ToString();
        }
    }
}
