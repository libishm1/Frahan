#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Frahan.Surface;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Surface
{
    // Must be public: GH_TaskCapableComponent<T> is public, T must be at least as accessible.
    public sealed class PackOnSurfaceResult
    {
        public List<Curve> PackedCurves3D;
        public List<Curve> PackedCurves2D;
        public List<Curve> UnplacedCurves;
        public int FailedMappingCount;
        public string Report;
        public string ErrorMessage;
    }

    /// <summary>
    /// Frahan > Surface Packing > Pack On Surface
    ///
    /// Packs closed 2D part curves onto a FrahanSurfaceChart using the V5.0.6 freeform
    /// polygon nesting solver, then maps the packed positions back to the 3D surface via
    /// barycentric interpolation.
    ///
    /// Data flow:
    ///   chart.FlatOuterBoundary     → sheet outline (real units)
    ///   chart.FlatMesh inner edges  → sheet holes
    ///   clearance × MaxEdgeStretch  → adjusted spacing (conservative for distorted charts)
    ///   packed 2D curves (real units, Z=0) → BarycentricMapper2DTo3D → 3D surface curves
    ///
    /// Parts input: 2D closed planar curves in the same XY plane as the flat chart.
    /// </summary>
    [Algorithm("Barycentric 2D-to-3D mapping", "Floater 2003, Computer Aided Geometric Design 20(1):19-27 Mean value coordinates", Doi = "10.1016/S0167-8396(03)00002-5", Note = "Mean-value barycentric interpolation lifts packed UV curves back to the 3D surface", WikiPath = "wiki/algorithms/surface_mosaicing/")]
    public sealed class PackOnSurfaceComponent : GH_TaskCapableComponent<PackOnSurfaceResult>
    {
        public PackOnSurfaceComponent()
            : base(
                "Pack On Surface", "PackSurf",
                "Packs 2D shapes onto a surface chart using freeform nesting, then lifts " +
                "packed curves back to the 3D surface via barycentric mapping.",
                "Frahan", "Surface Packing")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("B7E4D9C1-3F8A-4B2E-91C6-5D7F3A8B2E1D");

        protected override Bitmap Icon => IconProvider.Load("SurfaceTile.png");

        // --- Params -------------------------------------------------------------

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Surface Map", "Map",
                "FrahanSurfaceChart from the Surface Chart component.",
                GH_ParamAccess.item);
            p.AddCurveParameter("Parts", "P",
                "Closed planar 2D part curves to pack (in the flat chart XY plane).",
                GH_ParamAccess.list);
            p.AddNumberParameter("Spacing", "Gap",
                "Clearance between parts and between parts and the sheet boundary.",
                GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Rotations", "R",
                "Allowed rotation angles in degrees. Default: 0, 90, 180, 270.",
                GH_ParamAccess.list, 0.0);
            p.AddNumberParameter("Tolerance", "T",
                "Geometric tolerance for containment and collision checks.",
                GH_ParamAccess.item, 0.01);
            p.AddIntegerParameter("Sort Mode", "M",
                "0 UserOrder, 1 Area↓, 2 Width↓, 3 Height↓, 4 MaxDim↓.",
                GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Corner Mode", "Cnr",
                "0 BottomLeft, 1 BottomRight, 2 TopLeft, 3 TopRight.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Seed", "Seed",
                "0 = deterministic. Non-zero changes tie-breaking randomisation.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Max Candidates", "Max",
                "Candidate budget per part per rotation. 0 = default (300).",
                GH_ParamAccess.item, 300);
            p.AddBooleanParameter("Run", "Run",
                "Set to True to execute packing.",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Packed 3D", "C3",
                "Packed part curves lifted to the 3D surface.",
                GH_ParamAccess.list);
            p.AddCurveParameter("Packed 2D", "C2",
                "Packed part curves in the flat chart plane (real units).",
                GH_ParamAccess.list);
            p.AddCurveParameter("Unplaced", "U",
                "Curves that could not be placed in the chart.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Failed 3D", "F",
                "Number of packed curves that failed 3D barycentric mapping (likely cross a UV seam).",
                GH_ParamAccess.item);
            p.AddTextParameter("Report", "R",
                "Packing and mapping report.",
                GH_ParamAccess.item);
        }

        // --- Solve --------------------------------------------------------------

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (InPreSolve)
            {
                var chartWrapper = new GH_ObjectWrapper();
                var parts = new List<Curve>();
                double spacing = 5.0;
                var rotations = new List<double>();
                double tolerance = 0.01;
                int sortModeVal = 1;
                int cornerModeVal = 0;
                int seed = 0;
                int maxCandidates = 300;
                bool run = false;

                if (!da.GetData(0, ref chartWrapper) || chartWrapper?.Value == null)
                {
                    da.SetData(4, "No Surface Map connected.");
                    return;
                }
                var chart = chartWrapper.Value as FrahanSurfaceChart;
                if (chart == null)
                {
                    da.SetData(4, "Surface Map input is not a FrahanSurfaceChart.");
                    return;
                }
                if (!da.GetDataList(1, parts) || parts.Count == 0)
                {
                    da.SetData(4, "No parts input.");
                    return;
                }
                da.GetData(2, ref spacing);
                da.GetDataList(3, rotations);
                da.GetData(4, ref tolerance);
                da.GetData(5, ref sortModeVal);
                da.GetData(6, ref cornerModeVal);
                da.GetData(7, ref seed);
                da.GetData(8, ref maxCandidates);
                da.GetData(9, ref run);

                if (!run)
                {
                    da.SetData(4, "Run is false.");
                    return;
                }

                if (rotations.Count == 0)
                    rotations.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

                var sortMode = ToSortMode(sortModeVal);
                var cornerMode = ToCornerMode(cornerModeVal);
                var partsCopy = new List<Curve>(parts.Count);
                foreach (var c in parts) partsCopy.Add(c?.DuplicateCurve());

                TaskList.Add(Task.Run(() =>
                    ComputePacking(chart, partsCopy, spacing, rotations,
                        tolerance, sortMode, cornerMode, seed, maxCandidates)));
                return;
            }

            // Post-solve: output results
            PackOnSurfaceResult result;
            if (!GetSolveResults(da, out result))
            {
                // Synchronous fallback
                var chartWrapper2 = new GH_ObjectWrapper();
                var parts2 = new List<Curve>();
                double spacing2 = 5.0;
                var rotations2 = new List<double>();
                double tolerance2 = 0.01;
                int sortModeVal2 = 1;
                int cornerModeVal2 = 0;
                int seed2 = 0;
                int maxCandidates2 = 300;
                bool run2 = false;

                if (!da.GetData(0, ref chartWrapper2)) return;
                var chart2 = chartWrapper2?.Value as FrahanSurfaceChart;
                if (chart2 == null) return;
                if (!da.GetDataList(1, parts2)) return;
                da.GetData(2, ref spacing2);
                da.GetDataList(3, rotations2);
                da.GetData(4, ref tolerance2);
                da.GetData(5, ref sortModeVal2);
                da.GetData(6, ref cornerModeVal2);
                da.GetData(7, ref seed2);
                da.GetData(8, ref maxCandidates2);
                da.GetData(9, ref run2);

                if (!run2)
                {
                    da.SetData(4, "Run is false.");
                    return;
                }

                if (rotations2.Count == 0)
                    rotations2.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

                result = ComputePacking(chart2, parts2, spacing2, rotations2, tolerance2,
                    ToSortMode(sortModeVal2), ToCornerMode(cornerModeVal2), seed2, maxCandidates2);
            }

            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Packing returned null result.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result.ErrorMessage);
                da.SetData(4, result.ErrorMessage);
                return;
            }

            if (result.UnplacedCurves != null && result.UnplacedCurves.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{result.UnplacedCurves.Count} part(s) could not be placed.");

            if (result.FailedMappingCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{result.FailedMappingCount} packed curve(s) failed 3D mapping — " +
                    "likely cross a UV seam. Reduce part size or reposition relative to seam.");

            // Filter nulls from 3D output (failed mappings)
            var out3D = new List<Curve>();
            if (result.PackedCurves3D != null)
                foreach (var c in result.PackedCurves3D)
                    if (c != null) out3D.Add(c);

            da.SetDataList(0, out3D);
            da.SetDataList(1, result.PackedCurves2D ?? new List<Curve>());
            da.SetDataList(2, result.UnplacedCurves ?? new List<Curve>());
            da.SetData(3, result.FailedMappingCount);
            da.SetData(4, result.Report ?? string.Empty);
        }

        // --- Worker -------------------------------------------------------------

        private static PackOnSurfaceResult ComputePacking(
            FrahanSurfaceChart chart,
            List<Curve> parts,
            double spacing,
            List<double> rotationsDeg,
            double tolerance,
            PackingSortMode sortMode,
            PackingCornerMode cornerMode,
            int seed,
            int maxCandidates)
        {
            var result = new PackOnSurfaceResult();

            if (chart == null)
            {
                result.ErrorMessage = "Surface Map is null.";
                return result;
            }
            if (chart.FlatOuterBoundary == null || chart.FlatOuterBoundary.Count < 3)
            {
                result.ErrorMessage = "Chart has no outer boundary — cannot define sheet.";
                return result;
            }

            var sheetCurve = new PolylineCurve(chart.FlatOuterBoundary);

            // Extract inner holes: naked edges shorter than the outer boundary
            var holeCurves = new List<Curve>();
            var allNaked = chart.FlatMesh?.GetNakedEdges();
            if (allNaked != null && allNaked.Length > 1)
            {
                double outerLen = chart.FlatOuterBoundary.Length;
                foreach (var nakedPl in allNaked)
                {
                    if (nakedPl.Length < outerLen - 1e-6)
                        holeCurves.Add(new PolylineCurve(nakedPl));
                }
            }

            // Conservatively scale spacing by max edge stretch so parts don't
            // butt against each other more tightly than intended on the 3D surface.
            double maxStretch = chart.Distortion != null
                ? Math.Max(1.0, chart.Distortion.MaxEdgeStretch)
                : 1.0;
            double adjSpacing = spacing * maxStretch;

            PackingResult packed;
            try
            {
                var solver = new IrregularSheetFillV506(
                    new[] { sheetCurve },
                    new List<IReadOnlyList<Curve>> { holeCurves },
                    adjSpacing, rotationsDeg, tolerance,
                    sortMode, cornerMode, seed, maxCandidates);
                packed = solver.Pack(parts);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Packing solver failed: {ex.GetType().Name}: {ex.Message}";
                return result;
            }

            result.PackedCurves2D = new List<Curve>(packed.PackedCurves);
            result.UnplacedCurves = new List<Curve>(packed.UnplacedCurves);

            // Map packed 2D curves to 3D via barycentric interpolation
            result.PackedCurves3D = new List<Curve>(packed.PackedCurves.Count);
            int failedMappings = 0;

            foreach (var c2D in packed.PackedCurves)
            {
                var c3D = BarycentricMapper2DTo3D.MapCurveTo3DSurface(
                    c2D, chart.FlatMesh, chart.SurfaceMesh3D, tolerance);

                result.PackedCurves3D.Add(c3D); // null if failed — filtered at output
                if (c3D == null) failedMappings++;
            }
            result.FailedMappingCount = failedMappings;

            // Build report
            var sb = new StringBuilder();
            sb.AppendLine(packed.Report);
            if (adjSpacing > spacing + 1e-6)
                sb.AppendLine($"Spacing adjusted: {spacing:F3} → {adjSpacing:F3} " +
                    $"(MaxEdgeStretch = {chart.Distortion?.MaxEdgeStretch:F3}×).");
            if (holeCurves.Count > 0)
                sb.AppendLine($"Sheet holes: {holeCurves.Count}.");
            if (failedMappings > 0)
                sb.AppendLine($"WARNING: {failedMappings} packed curve(s) failed 3D mapping " +
                    "(cross a UV seam — reduce part size or avoid seam region).");
            result.Report = sb.ToString().TrimEnd();

            return result;
        }

        // --- Helpers ------------------------------------------------------------

        private PackingSortMode ToSortMode(int v)
        {
            if (Enum.IsDefined(typeof(PackingSortMode), v)) return (PackingSortMode)v;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Invalid sort mode, using AreaDescending.");
            return PackingSortMode.AreaDescending;
        }

        private PackingCornerMode ToCornerMode(int v)
        {
            if (Enum.IsDefined(typeof(PackingCornerMode), v)) return (PackingCornerMode)v;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Invalid corner mode, using BottomLeft.");
            return PackingCornerMode.BottomLeft;
        }
    }
}
