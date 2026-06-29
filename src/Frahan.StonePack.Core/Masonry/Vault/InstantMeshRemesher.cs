#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // InstantMeshRemesher — field-aligned quad remeshing via Instant Meshes
    // (Jakob, Tarini, Panozzo, Sorkine-Hornung, SIGGRAPH Asia 2015; BSD-3-Clause),
    // run OUT OF PROCESS (the frahan_instantmesh.exe worker, same pattern as the
    // reconstruction worker). The 4-RoSy orientation field + 4-PoSy position field
    // make the quad edges follow the surface flow (principal curvature ~= principal
    // thrust on a funicular membrane) — our own remesher, independent of Rhino's
    // opaque QuadRemesh, and the base for thrust-guided coursing.
    //
    // I/O is plain Wavefront .obj through the temp folder. Returns null on any
    // failure (LastError holds the reason); callers can fall back to Mesh.QuadRemesh.
    // =========================================================================
    public static class InstantMeshRemesher
    {
        public static string LastError;

        public static bool Available() => ResolveExe() != null;

        private static string ResolveExe()
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(typeof(InstantMeshRemesher).Assembly.Location); }
            catch { }
            if (dir == null) return null;
            foreach (var name in new[] { "frahan_instantmesh.exe", "Instant Meshes.exe", "InstantMeshes.exe" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>
        /// Field-aligned quad remesh. Pass edgeLength &gt; 0 for a target edge length, or
        /// faceCount &gt; 0 for a target face count (edgeLength wins if both set). rosy/posy
        /// are fixed at 4 (pure quads). Returns the quad mesh, or null on failure.
        /// </summary>
        // Defaults are PLAIN (no boundary-align / intrinsic / extra smoothing): on the
        // funicular three-prong this gives 0 interior singularities + 100% course-CRA.
        // Turning on alignBoundaries cleans the free edges but injects interior singularities
        // and drops CRA (~70%) — a real local-solver trade-off; use QuadWild for both-clean.
        public static Mesh Remesh(Mesh input, double edgeLength, int faceCount = 0,
                                  bool deterministic = true, bool intrinsic = false,
                                  bool alignBoundaries = false, double creaseAngle = 0.0,
                                  int smoothIterations = 0, int timeoutMs = 120000)
        {
            LastError = null;
            if (input == null || input.Vertices.Count < 4) { LastError = "input mesh too small"; return null; }
            string exe = ResolveExe();
            if (exe == null) { LastError = "frahan_instantmesh.exe not found next to the plugin"; return null; }

            string inPath = Path.Combine(Path.GetTempPath(), "frahan_im_" + Guid.NewGuid().ToString("N") + ".obj");
            string outPath = inPath + ".out.obj";
            try
            {
                WriteObj(inPath, input);

                var ci = CultureInfo.InvariantCulture;
                string args = "\"" + inPath + "\" -o \"" + outPath + "\" -r 4 -p 4";
                if (deterministic) args += " -d";
                if (intrinsic) args += " -i";
                if (alignBoundaries) args += " -b";                                  // snap quads to open boundaries (clean free edges)
                if (creaseAngle > 0) args += " -c " + creaseAngle.ToString("R", ci); // snap to sharp creases above this angle (deg)
                if (smoothIterations > 0) args += " -S " + smoothIterations.ToString(ci); // field smoothing -> fewer scattered singularities (8 -> 2 on the 3-prong)
                if (edgeLength > 0) args += " -s " + edgeLength.ToString("R", ci);
                else if (faceCount > 0) args += " -f " + faceCount.ToString(ci);

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) { LastError = "failed to start Instant Meshes worker"; return null; }
                    proc.StandardOutput.ReadToEnd();
                    string err = proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        LastError = "Instant Meshes worker timed out after " + timeoutMs + " ms";
                        return null;
                    }
                    if (!File.Exists(outPath))
                    {
                        LastError = "Instant Meshes worker produced no output (exit 0x" +
                                    proc.ExitCode.ToString("X8") + "). " + err;
                        return null;
                    }
                }
                var m = ReadObj(outPath);
                if (m == null || m.Faces.Count == 0) { LastError = "Instant Meshes output had no faces"; return null; }
                return m;
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return null; }
            finally
            {
                try { if (File.Exists(inPath)) File.Delete(inPath); } catch { }
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
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
                    if (f.IsQuad) w.WriteLine("f " + (f.A + 1) + " " + (f.B + 1) + " " + (f.C + 1) + " " + (f.D + 1));
                    else w.WriteLine("f " + (f.A + 1) + " " + (f.B + 1) + " " + (f.C + 1));
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
                else // 'f'
                {
                    var idx = new List<int>();
                    for (int k = 1; k < t.Length; k++)
                    {
                        var tok = t[k];
                        int slash = tok.IndexOf('/');
                        if (slash >= 0) tok = tok.Substring(0, slash);
                        if (int.TryParse(tok, NumberStyles.Integer, ci, out int vi)) idx.Add(vi - 1);
                    }
                    if (idx.Count == 4) mesh.Faces.AddFace(idx[0], idx[1], idx[2], idx[3]);
                    else if (idx.Count == 3) mesh.Faces.AddFace(idx[0], idx[1], idx[2]);
                    else if (idx.Count > 4)
                        for (int k = 2; k < idx.Count - 1; k++) mesh.Faces.AddFace(idx[0], idx[k], idx[k + 1]);
                }
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }
    }
}
