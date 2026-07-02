#nullable disable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // QuadWildRemesher — reliable quad remeshing via QuadWild + Bi-MDF
    // (Pietroni et al. 2021 "Reliable Feature-Line Driven Quad-Remeshing";
    // Heistermann et al. / Campen group Bi-MDF quantization, GPL-3.0,
    // github.com/cgg-bern/quadwild-bimdf), run OUT OF PROCESS like the Instant
    // Meshes worker. Bi-MDF replaces Gurobi (LEMON/satsuma), so the pipeline is
    // fully open. Two modes:
    //
    //   * thrustField = true (default): frahan_quadremesh --rosy computes OUR
    //     thrust-potential cross-field (E1 = grad phi, confidence-weighted 4-RoSy
    //     smoothed) and QuadWild traces THAT field (do_remesh 0) — the quads
    //     follow the compression flow. Validated on the Park Güell portico:
    //     7283 quads, 0 tris, holes preserved, 37 irregular vertices
    //     (unrefined field: 53; QuadWild's own curvature field: 32).
    //   * thrustField = false: QuadWild's built-in curvature field (do_remesh 1).
    //
    // Use this over InstantMeshRemesher when the surface is multiply-connected
    // (openings/holes) or needs guaranteed 100%-quad watertight output — the
    // single-chart and Instant Meshes routes fold or leave tris there.
    //
    // I/O is OBJ/rosy through a private temp dir. Binaries live in
    // thirdparty/quadwild-bimdf/{bin,config} next to the plugin (see
    // install/plugin/thirdparty/quadwild-bimdf/NOTICE.md). Returns null on any
    // failure with the reason in LastError.
    // =========================================================================
    public static class QuadWildRemesher
    {
        public static string LastError;

        // Baked from the 2026-07-02 portico validation sweep.
        public const double DefaultSupportFrac = 0.30;
        public const int DefaultSmoothSweeps = 30;
        public const double DefaultDataWeight = 0.20;
        // Coarseness = the flow-config scaleFact (target edge multiplier). Measured on
        // the portico: 1 -> 7283 quads (fine skin), 3 -> 999, 5 -> 544 (whole-shell
        // CRA regime: contact-by-construction voussoirs stay solver-friendly).
        public const int DefaultCoarseness = 1;

        public static bool Available() => ResolveRoot() != null;

        private static string AsmDir()
        {
            try { return Path.GetDirectoryName(typeof(QuadWildRemesher).Assembly.Location); }
            catch { return null; }
        }

        /// <summary>thirdparty/quadwild-bimdf root holding bin/ + config/. Env var QUADWILD_HOME overrides.</summary>
        private static string ResolveRoot()
        {
            string env = Environment.GetEnvironmentVariable("QUADWILD_HOME");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "bin", "quadwild.exe"))) return env;
            string dir = AsmDir();
            if (dir == null) return null;
            foreach (var cand in new[]
            {
                Path.Combine(dir, "thirdparty", "quadwild-bimdf"),
                Path.Combine(dir, "quadwild-bimdf"),
            })
                if (File.Exists(Path.Combine(cand, "bin", "quadwild.exe"))) return cand;
            return null;
        }

        private static string ResolveFieldWorker()
        {
            string dir = AsmDir();
            if (dir == null) return null;
            string p = Path.Combine(dir, "frahan_quadremesh.exe");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// Remesh to a watertight all-quad mesh. thrustField follows the compression
        /// flow (needs frahan_quadremesh.exe beside the plugin); coarseness is the
        /// flow scaleFact (1 = fine skin, 4-6 = coarse CRA-ready structural mesh).
        /// Returns null on failure (LastError).
        /// </summary>
        public static Mesh Remesh(Mesh input, bool thrustField, double supportFrac, int smoothSweeps,
                                  int coarseness, Action<string> progress, CancellationToken token,
                                  out string report)
        {
            LastError = null; report = null;
            if (input == null || input.Vertices.Count < 4) { LastError = "input mesh too small"; return null; }
            string root = ResolveRoot();
            if (root == null) { LastError = "quadwild-bimdf not found (thirdparty/quadwild-bimdf beside the plugin, or set QUADWILD_HOME)"; return null; }
            string fieldExe = thrustField ? ResolveFieldWorker() : null;
            if (thrustField && fieldExe == null) { LastError = "frahan_quadremesh.exe not found beside the plugin (needed for the thrust field)"; return null; }

            string work = Path.Combine(Path.GetTempPath(), "frahan_qw_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                CopyTree(Path.Combine(root, "config"), Path.Combine(work, "config"));

                // triangulated working copy -> OBJ (+ blob for the field worker)
                var tri = input.DuplicateMesh();
                tri.Faces.ConvertQuadsToTriangles();
                tri.Vertices.CombineIdentical(true, true);
                tri.Compact();
                string objPath = Path.Combine(work, "m.obj");
                WriteObj(objPath, tri);

                var ci = CultureInfo.InvariantCulture;
                string rosyArg = "";
                if (thrustField)
                {
                    progress?.Invoke("thrust field...");
                    string blob = Path.Combine(work, "m.bin");
                    WriteMeshBlob(blob, tri);
                    string rosy = Path.Combine(work, "m.rosy");
                    string fargs = "--rosy \"" + blob + "\" \"" + rosy + "\" " +
                                   supportFrac.ToString("R", ci) + " " + smoothSweeps.ToString(ci) + " " +
                                   DefaultDataWeight.ToString("R", ci);
                    if (!RunProc(fieldExe, fargs, work, 300000, out string fout)) return null;
                    if (!File.Exists(rosy)) { LastError = "field worker produced no .rosy. " + fout; return null; }
                    rosyArg = " \"" + rosy + "\"";
                }
                token.ThrowIfCancellationRequested();

                // prep config: do_remesh 0 = trace OUR mesh + field; 1 = QuadWild remesh + own field
                string prep = Path.Combine(work, "config", "prep_config", "frahan_setup.txt");
                File.WriteAllText(prep, "do_remesh " + (thrustField ? "0" : "1") + "\nsharp_feature_thr 35\nalpha 0.01\nscaleFact 1\n");

                progress?.Invoke("quadwild trace...");
                if (!RunProc(Path.Combine(root, "bin", "quadwild.exe"),
                             "\"" + objPath + "\" 2 \"" + prep + "\"" + rosyArg, work, 600000, out _)) return null;
                string patches = Path.Combine(work, "m_rem_p0.obj");
                if (!File.Exists(patches)) { LastError = "quadwild produced no patch mesh (m_rem_p0.obj)"; return null; }
                token.ThrowIfCancellationRequested();

                progress?.Invoke("Bi-MDF quantize...");
                // density: rewrite scaleFact in a temp copy of the flow config (the CLI
                // number is NOT the density knob; measured 1 -> 7283, 3 -> 999, 5 -> 544).
                string flowSrc = Path.Combine(work, "config", "main_config", "flow_noalign_lemon.txt");
                string flowDst = Path.Combine(work, "config", "main_config", "flow_frahan.txt");
                // alpha 0.2 (vs upstream 0.005) weights REGULARITY/isometry over patch
                // fidelity: measured on the portico it cuts sub-0.10 m edges 43 -> 9 and
                // lifts the p5 edge 0.156 -> 0.239 m — even quad sizing, no tiny quads
                // collapsing around singularities (Libish 2026-07-02).
                File.WriteAllText(flowDst,
                    File.ReadAllText(flowSrc)
                        .Replace("scaleFact 1", "scaleFact " + Math.Max(1, coarseness).ToString(ci))
                        .Replace("alpha 0.005", "alpha 0.2"));
                // quad_from_patches exits 0x80000003 (an exit-path assert) even on
                // SUCCESS, so judge it by its output file, not its exit code.
                RunProc(Path.Combine(root, "bin", "quad_from_patches.exe"),
                        "\"" + patches + "\" 123 config/main_config/flow_frahan.txt",
                        work, 600000, out _);
                string outObj = Path.Combine(work, "m_rem_p0_123_quadrangulation_smooth.obj");
                if (!File.Exists(outObj)) { LastError = "quad_from_patches produced no quadrangulation. " + LastError; return null; }
                LastError = null;

                var q = ReadObj(outObj);
                if (q == null || q.Faces.Count == 0) { LastError = "quadrangulation OBJ had no faces"; return null; }
                q.Normals.ComputeNormals();
                report = (thrustField ? "thrust-aligned" : "curvature-aligned") + " QuadWild+BiMDF: " +
                         q.Faces.QuadCount + " quads + " + q.Faces.TriangleCount + " tris, " +
                         q.Vertices.Count + " verts (coarseness " + coarseness + ").";
                return q;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return null; }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        private static bool RunProc(string exe, string args, string cwd, int timeoutMs, out string stdout)
        {
            stdout = "";
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = cwd,
            };
            using (var proc = Process.Start(psi))
            {
                if (proc == null) { LastError = "failed to start " + Path.GetFileName(exe); return false; }
                // drain stderr asynchronously while reading stdout: a synchronous
                // stdout-then-stderr drain deadlocks when the child fills the stderr
                // pipe buffer (quadwild is verbose on both streams).
                var errTask = proc.StandardError.ReadToEndAsync();
                stdout = proc.StandardOutput.ReadToEnd();
                string err = errTask.Result;
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    LastError = Path.GetFileName(exe) + " timed out after " + timeoutMs + " ms";
                    return false;
                }
                if (proc.ExitCode != 0)
                {
                    LastError = Path.GetFileName(exe) + " exit code " + proc.ExitCode + ". " + err;
                    return false;
                }
            }
            return true;
        }

        private static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // mesh-only blob for frahan_quadremesh: int32 nv; nv*3 f64 xyz; int32 nf; nf*3 int32; f64 edgeLen
        private static void WriteMeshBlob(string path, Mesh m)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(m.Vertices.Count);
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    var v = m.Vertices[i];
                    w.Write((double)v.X); w.Write((double)v.Y); w.Write((double)v.Z);
                }
                w.Write(m.Faces.Count);
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    var f = m.Faces[i];
                    w.Write(f.A); w.Write(f.B); w.Write(f.C);
                }
                w.Write(0.0); // edgeLen unused by --rosy
            }
        }

        private static void WriteObj(string path, Mesh m)
        {
            var ci = CultureInfo.InvariantCulture;
            using (var w = new StreamWriter(path))
            {
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    var v = m.Vertices[i];
                    w.WriteLine("v " + v.X.ToString("R", ci) + " " + v.Y.ToString("R", ci) + " " + v.Z.ToString("R", ci));
                }
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    var f = m.Faces[i];
                    w.WriteLine("f " + (f.A + 1) + " " + (f.B + 1) + " " + (f.C + 1));
                }
            }
        }

        private static Mesh ReadObj(string path)
        {
            var ci = CultureInfo.InvariantCulture;
            var mesh = new Mesh();
            foreach (var raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                char c0 = raw[0];
                if (c0 != 'v' && c0 != 'f') continue;
                char c1 = raw[1];
                if (c1 != ' ' && c1 != '\t') continue;
                var t = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (c0 == 'v')
                {
                    if (t.Length >= 4)
                        mesh.Vertices.Add(double.Parse(t[1], ci), double.Parse(t[2], ci), double.Parse(t[3], ci));
                }
                else
                {
                    int n = t.Length - 1;
                    var idx = new int[n];
                    for (int k = 0; k < n; k++)
                    {
                        var tok = t[k + 1];
                        int slash = tok.IndexOf('/');
                        if (slash >= 0) tok = tok.Substring(0, slash);
                        idx[k] = int.Parse(tok, ci) - 1;
                    }
                    if (n == 4) mesh.Faces.AddFace(idx[0], idx[1], idx[2], idx[3]);
                    else if (n == 3) mesh.Faces.AddFace(idx[0], idx[1], idx[2]);
                    else for (int k = 2; k < n - 1; k++) mesh.Faces.AddFace(idx[0], idx[k], idx[k + 1]);
                }
            }
            mesh.Compact();
            return mesh;
        }
    }
}
