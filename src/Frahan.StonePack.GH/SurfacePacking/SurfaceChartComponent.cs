#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Frahan.Surface;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Surface
{
    // ─── Result record ──────────────────────────────────────────────────────────
    // Must be public: GH_TaskCapableComponent<T> is public, so T must be at least as accessible
    // as the derived component class.

    public sealed class SurfaceChartResult
    {
        public FrahanSurfaceChart Chart;
        public string Report;
        public string ErrorMessage;
    }

    // ─── Component ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Frahan > Surface Packing > Surface Chart
    ///
    /// Converts a 3D mesh (or BrepFace) to a 2D UV chart using the BFF command-line solver.
    /// Runs BFF on a background thread — never blocks the Grasshopper UI thread.
    ///
    /// Outputs:
    ///   FlatMesh   — 2D unwrapped mesh in UV coordinates (Z=0 plane).
    ///   SurfaceMap — FrahanSurfaceChart object. Wire into "Pack On Surface".
    ///   Boundary   — Outer boundary polyline in scaled UV space (real units).
    ///   Distortion — Max/min edge stretch ratio summary.
    ///   Report     — Human-readable quality and timing report.
    ///
    /// BFF download: https://github.com/GeometryCollective/boundary-first-flattening
    /// </summary>
    [Algorithm("BFF boundary-first flattening", "Sawhney and Crane 2017, ACM TOG 36(4):109", Doi = "10.1145/3072959.3056432", Note = "External BFF command-line exe; Frahan wraps the binary")]
    [Algorithm("Conformal chart-scale recovery", "Frahan-original barycentric UV-to-real-world scaling", WikiPath = "wiki/algorithms/surface_mosaicing/")]
    public sealed class SurfaceChartComponent : GH_TaskCapableComponent<SurfaceChartResult>
    {
        public SurfaceChartComponent()
            : base(
                "Surface Chart", "SurfChart",
                "Unwraps a 3D mesh to a 2D UV chart using Boundary-First Flattening (BFF). " +
                "BFF must be downloaded separately and the exe path provided as input.",
                "Frahan", "Surface Packing")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("A3F1C8B2-74D9-4E2A-8F5B-1C3D9E7A2B4F");

        protected override Bitmap Icon => IconProvider.Load("BffChartPack.png");

        // ─── Params ─────────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Surface", "S",
                "Mesh to unwrap. Accepts any Rhino mesh — cleaned automatically before BFF.",
                GH_ParamAccess.item);
            p.AddTextParameter("BFF Exe Path", "BFF",
                "Optional. Path to bff-command-line.exe. " +
                "Leave unconnected to auto-detect next to the .gha file.",
                GH_ParamAccess.item);
            p[1].Optional = true;
            p.AddIntegerParameter("Cones", "K",
                "Number of cone singularities (0 = auto, 1–8 for complex surfaces).",
                GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Normalize UVs", "N",
                "Normalize output UVs to [0,1]. Required for chart scale computation.",
                GH_ParamAccess.item, true);
            p.AddNumberParameter("Timeout (s)", "T",
                "Maximum seconds to wait for BFF before aborting.",
                GH_ParamAccess.item, 30.0);
            p.AddBooleanParameter("Run", "Run",
                "Set to True to execute unwrapping.",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Flat Mesh", "FM",
                "2D unwrapped mesh in UV space (Z=0 plane). Scale by ChartScale for real dimensions.",
                GH_ParamAccess.item);
            p.AddGenericParameter("Surface Map", "Map",
                "FrahanSurfaceChart object. Wire into the Pack On Surface component.",
                GH_ParamAccess.item);
            p.AddCurveParameter("Boundary", "B",
                "Outer boundary polyline of the flat chart scaled to real units.",
                GH_ParamAccess.item);
            p.AddTextParameter("Distortion", "D",
                "Max/min edge stretch ratio. Values far from 1.0 indicate mapping distortion.",
                GH_ParamAccess.item);
            p.AddTextParameter("Report", "R",
                "Quality metrics, warnings, and timing.",
                GH_ParamAccess.item);
        }

        // ─── Solve ──────────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (InPreSolve)
            {
                // ── Read inputs ────────────────────────────────────────────────
                Mesh mesh = null;
                string bffPath = null;
                int cones = 0;
                bool normalize = true;
                double timeoutSec = 30.0;
                bool run = false;

                if (!da.GetData(0, ref mesh) || mesh == null)
                {
                    da.SetData(4, "No mesh input.");
                    return;
                }
                da.GetData(1, ref bffPath);
                if (string.IsNullOrWhiteSpace(bffPath))
                    bffPath = ResolveBffExe();
                da.GetData(2, ref cones);
                da.GetData(3, ref normalize);
                da.GetData(4, ref timeoutSec);
                da.GetData(5, ref run);

                if (!run)
                {
                    da.SetData(4, "Run is false.");
                    return;
                }

                // Capture copies for background thread — never pass GH objects across threads
                var meshCopy = mesh.DuplicateMesh();
                var bffPathCopy = bffPath;
                var conesCopy = cones;
                var normalizeCopy = normalize;
                int timeoutMs = Math.Max(5000, (int)(timeoutSec * 1000));

                TaskList.Add(Task.Run(() =>
                    ComputeChart(meshCopy, bffPathCopy, conesCopy, normalizeCopy, timeoutMs)));
                return;
            }

            // ── Post-solve: output results ─────────────────────────────────────
            SurfaceChartResult result;
            if (!GetSolveResults(da, out result))
            {
                // Synchronous fallback (GH_TaskCapableComponent calls this when task not ready)
                Mesh mesh2 = null;
                string bffPath2 = null;
                int cones2 = 0;
                bool normalize2 = true;
                double timeoutSec2 = 30.0;
                bool run2 = false;

                if (!da.GetData(0, ref mesh2) || mesh2 == null) return;
                da.GetData(1, ref bffPath2);
                if (string.IsNullOrWhiteSpace(bffPath2))
                    bffPath2 = ResolveBffExe();
                da.GetData(2, ref cones2);
                da.GetData(3, ref normalize2);
                da.GetData(4, ref timeoutSec2);
                da.GetData(5, ref run2);

                if (!run2)
                {
                    da.SetData(4, "Run is false.");
                    return;
                }

                int tmSync = Math.Max(5000, (int)(timeoutSec2 * 1000));
                result = ComputeChart(mesh2, bffPath2, cones2, normalize2, tmSync);
            }

            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Charting returned null result.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result.ErrorMessage);
                da.SetData(4, result.ErrorMessage);
                return;
            }

            var chart = result.Chart;
            if (chart == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Chart object is null after computation.");
                return;
            }

            // Emit distortion warnings to GH runtime messages
            foreach (var w in chart.Distortion.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

            da.SetData(0, chart.FlatMesh);
            da.SetData(1, chart);
            da.SetData(2, chart.FlatOuterBoundary != null
                ? new PolylineCurve(chart.FlatOuterBoundary)
                : null);
            da.SetData(3, FormatDistortion(chart));
            da.SetData(4, result.Report);
        }

        // ─── Worker ─────────────────────────────────────────────────────────────

        private static SurfaceChartResult ComputeChart(
            Mesh mesh, string bffExePath, int cones, bool normalizeUVs, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new SurfaceChartResult();
            var log = new StringBuilder();

            try
            {
                // 1. Validate BFF exe
                if (!File.Exists(bffExePath))
                {
                    result.ErrorMessage = $"BFF executable not found: {bffExePath}";
                    return result;
                }

                // 2. Clean mesh
                if (!MeshCleanup.TryCleanMesh(mesh, out string cleanError))
                {
                    result.ErrorMessage = $"Mesh cleanup failed: {cleanError}";
                    return result;
                }
                log.AppendLine($"Mesh: {mesh.Vertices.Count} verts, {mesh.Faces.Count} faces after cleanup.");

                // 3. Write OBJ
                string tmpDir = Path.Combine(Path.GetTempPath(), "frahan_surfpack");
                Directory.CreateDirectory(tmpDir);
                string session = Guid.NewGuid().ToString("N").Substring(0, 8);
                string inputObj = Path.Combine(tmpDir, $"input_{session}.obj");
                string outputObj = Path.Combine(tmpDir, $"output_{session}.obj");

                MeshObjIO.WriteMeshToObj(mesh, inputObj);
                int expectedFaces = MeshObjIO.CountWrittenFaces(mesh);
                log.AppendLine($"OBJ written: {expectedFaces} triangular faces.");

                // 4. Run BFF
                var runner = new BffCommandLineRunner(bffExePath);
                runner.RunAsync(inputObj, outputObj, cones, normalizeUVs, timeoutMs)
                    .GetAwaiter().GetResult(); // safe: we are already on a pool thread

                log.AppendLine($"BFF completed in {sw.ElapsedMilliseconds} ms.");

                // 5. Parse UV output
                if (!MeshObjIO.TryParseObjWithFaceCornerUVs(outputObj, expectedFaces,
                    out FaceCornerUvTable uvTable, out string parseError))
                {
                    result.ErrorMessage = $"OBJ parse failed: {parseError}";
                    return result;
                }
                log.AppendLine($"UV table parsed: {uvTable.EntryCount} entries.");

                // 6. Build flat mesh
                Mesh flatMesh;
                try { flatMesh = uvTable.ToFlatUnweldedMesh(mesh); }
                catch (InvalidOperationException ex)
                {
                    result.ErrorMessage = $"Flat mesh construction failed: {ex.Message}";
                    return result;
                }

                // 7. Chart scale
                double scale = ChartScaleComputer.ComputeGlobalScale(
                    flatMesh, mesh, out string scaleWarning);
                if (!string.IsNullOrWhiteSpace(scaleWarning))
                    log.AppendLine($"Scale warning: {scaleWarning}");
                log.AppendLine($"Chart scale: {scale:F4} (UV × scale = real units).");

                // 8. Scale flat mesh to real units
                var scaledFlatMesh = flatMesh.DuplicateMesh();
                var xform = Transform.Scale(Point3d.Origin, scale);
                scaledFlatMesh.Transform(xform);

                // 9. Outer boundary
                var boundary = FrahanSurfaceChart.ExtractOuterBoundary(scaledFlatMesh);
                if (boundary == null)
                    log.AppendLine("Warning: could not extract outer boundary from flat mesh.");

                // 10. Distortion
                var distortion = ChartDistortionAnalyzer.Analyze(mesh, flatMesh, scale);
                log.AppendLine($"Edge stretch range: [{distortion.MinEdgeStretch:F3}, {distortion.MaxEdgeStretch:F3}].");

                // 11. Cleanup temp files
                TryDelete(inputObj);
                TryDelete(outputObj);

                result.Chart = new FrahanSurfaceChart(
                    mesh, scaledFlatMesh, scale, boundary, distortion);
                result.Report = BuildReport(log, sw, scale, distortion);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Surface charting failed: {ex.GetType().Name}: {ex.Message}";
                return result;
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private string ResolveBffExe()
        {
            // Look for bff-command-line.exe next to the .gha file.
            string assemblyDir = Path.GetDirectoryName(GetType().Assembly.Location)
                                 ?? string.Empty;
            return Path.Combine(assemblyDir, "bff-command-line.exe");
        }

        private static string BuildReport(
            StringBuilder log, System.Diagnostics.Stopwatch sw,
            double scale, ChartDistortionReport distortion)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Frahan Surface Chart ===");
            sb.Append(log);
            sb.AppendLine($"Total time: {sw.ElapsedMilliseconds} ms.");
            if (distortion.Warnings.Count > 0)
            {
                sb.AppendLine("Distortion warnings:");
                foreach (var w in distortion.Warnings) sb.AppendLine($"  • {w}");
            }
            else
            {
                sb.AppendLine("No distortion warnings.");
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatDistortion(FrahanSurfaceChart chart)
        {
            var d = chart.Distortion;
            return $"Edge stretch: min={d.MinEdgeStretch:F3}x  max={d.MaxEdgeStretch:F3}x" +
                   (d.HasWarnings ? $"  ⚠ {d.Warnings.Count} warning(s)" : "  ✓ OK");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* non-critical */ }
        }
    }
}
