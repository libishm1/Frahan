#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using Frahan.Surface;
using Frahan.GH.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Surface
{
    // Must be public: GH_TaskCapableComponent<T> requires T at least as accessible as the component.
    public sealed class PackSurfacesResult
    {
        public List<Curve> PackedCurves3D;
        public List<Plane> PlacementPlanes;
        public List<Transform> Transforms3D;
        public List<Transform> FullTransforms;
        public List<double> MaxDeviations;
        public List<Curve> PackedCurves2D;
        public List<int> ChartIndices;
        public List<int> PartIndices;
        public List<Curve> UnplacedCurves;
        public string Report;
        public string ErrorMessage;
    }

    /// <summary>
    /// Frahan > Surface Packing > Pack Surfaces
    ///
    /// Packs closed 2D part curves across ONE OR MORE surface charts using the V5.0.6
    /// freeform nesting solver. All chart boundaries are arranged side-by-side in a
    /// shared 2D layout, packed simultaneously, then mapped back to their 3D surfaces.
    ///
    /// Fabrication mode:
    ///   Full Transform (FT): single transform from the original flat part directly to
    ///   its 3D surface position. Apply to the original part geometry (before packing).
    ///   Equivalent to: packing transform composed with the 3D surface placement transform.
    ///
    ///   Transforms 3D (T3): transform from the PACKED 2D position to the 3D surface.
    ///   Apply to Packed 2D curves.
    ///
    /// Max Deviation output:
    ///   The maximum gap (model units) between the flat part and the curved surface at
    ///   the four bounding-box corners of the placement.
    ///
    /// Part Index output:
    ///   The 0-based index into the original Parts input list for each packed part.
    ///   Use with List Item to select the matching original part for Full Transform.
    /// </summary>
    public sealed class PackSurfacesComponent : GH_TaskCapableComponent<PackSurfacesResult>
    {
        public PackSurfacesComponent()
            : base(
                "Pack Surfaces", "PackSurfs",
                "Packs 2D shapes across one or more surface charts with freeform nesting. " +
                "Outputs Full Transform to place original flat parts on the 3D surface without distortion.",
                "Frahan", "Surface Packing")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("C4A8D2E1-7F3B-4C5D-9A2E-6B8D4F1E3C7A");

        protected override Bitmap Icon => IconProvider.Load("SurfaceUnroll.png");

        // --- Params ---------------------------------------------------------

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Surface Maps", "Maps",
                "One or more FrahanSurfaceChart objects from the Surface Chart component.",
                GH_ParamAccess.list);
            p.AddCurveParameter("Parts", "P",
                "Closed planar 2D part curves to pack.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Spacing", "Gap",
                "Clearance between parts and chart boundaries.",
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
            // 0
            p.AddCurveParameter("Packed 3D", "C3",
                "Packed curves lifted to the 3D surface via barycentric mapping (shape follows surface).",
                GH_ParamAccess.list);
            // 1
            p.AddPlaneParameter("Placement Planes", "Pl",
                "Rigid placement frame on the 3D surface per packed part. " +
                "Origin = centroid on surface, X/Y = surface tangent axes, Z = surface normal.",
                GH_ParamAccess.list);
            // 2
            p.AddGenericParameter("Transforms 3D", "T3",
                "Transform from PACKED 2D position to the 3D surface placement frame. " +
                "Apply to Packed 2D curves to get rigid (non-deformed) parts on the surface.",
                GH_ParamAccess.list);
            // 3
            p.AddGenericParameter("Full Transform", "FT",
                "Composed transform: original flat part → 3D surface in one step. " +
                "Apply to the ORIGINAL part geometry (before packing) using Part Index to select it. " +
                "Equivalent to: (packing transform) then (surface placement transform).",
                GH_ParamAccess.list);
            // 4
            p.AddNumberParameter("Max Deviation", "Dev",
                "Maximum gap (model units) between the flat part and the curved surface " +
                "at the four bounding-box corners. Small = nearly flat. Large = needs shimming.",
                GH_ParamAccess.list);
            // 5
            p.AddCurveParameter("Packed 2D", "C2",
                "Packed curves in each chart's native coordinate space.",
                GH_ParamAccess.list);
            // 6
            p.AddIntegerParameter("Chart Index", "CI",
                "Which Surface Map (0-based) each packed part was placed on.",
                GH_ParamAccess.list);
            // 7
            p.AddIntegerParameter("Part Index", "PI",
                "0-based index into the original Parts input list for each packed part. " +
                "Use with List Item to select the matching original part, then apply Full Transform.",
                GH_ParamAccess.list);
            // 8
            p.AddCurveParameter("Unplaced", "U",
                "Curves that could not be placed on any chart.",
                GH_ParamAccess.list);
            // 9
            p.AddTextParameter("Report", "R",
                "Packing and mapping report.",
                GH_ParamAccess.item);
        }

        // --- Solve ----------------------------------------------------------

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (InPreSolve)
            {
                var wrappers = new List<GH_ObjectWrapper>();
                var parts = new List<Curve>();
                double spacing = 5.0;
                var rotations = new List<double>();
                double tolerance = 0.01;
                int sortVal = 1, cornerVal = 0, seed = 0, maxCand = 300;
                bool run = false;

                if (!da.GetDataList(0, wrappers) || wrappers.Count == 0)
                { da.SetData(9, "No Surface Maps connected."); return; }
                if (!da.GetDataList(1, parts) || parts.Count == 0)
                { da.SetData(9, "No parts input."); return; }

                da.GetData(2, ref spacing);
                da.GetDataList(3, rotations);
                da.GetData(4, ref tolerance);
                da.GetData(5, ref sortVal);
                da.GetData(6, ref cornerVal);
                da.GetData(7, ref seed);
                da.GetData(8, ref maxCand);
                da.GetData(9, ref run);

                if (!run) { da.SetData(9, "Run is false."); return; }

                var charts = ExtractCharts(wrappers);
                if (charts.Count == 0)
                { da.SetData(9, "No valid FrahanSurfaceChart found in Maps input."); return; }

                if (rotations.Count == 0)
                    rotations.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

                var sortMode = ToSortMode(sortVal);
                var cornerMode = ToCornerMode(cornerVal);
                var partsCopy = parts.ConvertAll(c => c?.DuplicateCurve());
                var rotsCopy = new List<double>(rotations);

                TaskList.Add(Task.Run(() =>
                    ComputePacking(charts, partsCopy, spacing, rotsCopy,
                        tolerance, sortMode, cornerMode, seed, maxCand)));
                return;
            }

            // Post-solve
            PackSurfacesResult result;
            if (!GetSolveResults(da, out result))
            {
                // Synchronous fallback
                var wrappers2 = new List<GH_ObjectWrapper>();
                var parts2 = new List<Curve>();
                double spacing2 = 5.0; var rots2 = new List<double>();
                double tol2 = 0.01;
                int sv2 = 1, cv2 = 0, seed2 = 0, mc2 = 300;
                bool run2 = false;

                if (!da.GetDataList(0, wrappers2) || !da.GetDataList(1, parts2)) return;
                da.GetData(2, ref spacing2); da.GetDataList(3, rots2);
                da.GetData(4, ref tol2); da.GetData(5, ref sv2);
                da.GetData(6, ref cv2); da.GetData(7, ref seed2);
                da.GetData(8, ref mc2); da.GetData(9, ref run2);

                if (!run2) { da.SetData(9, "Run is false."); return; }

                var charts2 = ExtractCharts(wrappers2);
                if (rots2.Count == 0) rots2.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });

                result = ComputePacking(charts2, parts2, spacing2, rots2, tol2,
                    ToSortMode(sv2), ToCornerMode(cv2), seed2, mc2);
            }

            if (result == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Packing returned null."); return; }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result.ErrorMessage);
                da.SetData(9, result.ErrorMessage);
                return;
            }

            if (result.UnplacedCurves?.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{result.UnplacedCurves.Count} part(s) could not be placed.");

            da.SetDataList(0, FilterNulls(result.PackedCurves3D));
            da.SetDataList(1, result.PlacementPlanes ?? new List<Plane>());
            da.SetDataList(2, result.Transforms3D ?? new List<Transform>());
            da.SetDataList(3, result.FullTransforms ?? new List<Transform>());
            da.SetDataList(4, result.MaxDeviations ?? new List<double>());
            da.SetDataList(5, FilterNulls(result.PackedCurves2D));
            da.SetDataList(6, result.ChartIndices ?? new List<int>());
            da.SetDataList(7, result.PartIndices ?? new List<int>());
            da.SetDataList(8, result.UnplacedCurves ?? new List<Curve>());
            da.SetData(9, result.Report ?? string.Empty);
        }

        // --- Worker ---------------------------------------------------------

        private static PackSurfacesResult ComputePacking(
            List<FrahanSurfaceChart> charts,
            List<Curve> parts,
            double spacing,
            List<double> rotationsDeg,
            double tolerance,
            PackingSortMode sortMode,
            PackingCornerMode cornerMode,
            int seed,
            int maxCandidates)
        {
            var result = new PackSurfacesResult();
            if (charts == null || charts.Count == 0)
            { result.ErrorMessage = "No charts provided."; return result; }

            // Arrange all chart flat meshes side by side in XY to avoid overlaps.
            const double chartGap = 20.0;
            double offsetX = 0;

            var layouts = new List<(Transform toLayout, Transform fromLayout,
                FrahanSurfaceChart chart)>(charts.Count);
            var sheetOutlines = new List<Curve>(charts.Count);
            var sheetHoles = new List<IReadOnlyList<Curve>>(charts.Count);

            foreach (var chart in charts)
            {
                if (chart?.FlatOuterBoundary == null || chart.FlatMesh == null) continue;

                var bbox = chart.FlatMesh.GetBoundingBox(true);
                var shift = new Vector3d(offsetX - bbox.Min.X, -bbox.Min.Y, 0);
                var toLayout = Transform.Translation(shift);
                var fromLayout = Transform.Translation(-shift);

                var outerCurve = new PolylineCurve(chart.FlatOuterBoundary);
                outerCurve.Transform(toLayout);
                sheetOutlines.Add(outerCurve);

                // Inner holes: naked edges shorter than the outer boundary
                var holes = new List<Curve>();
                var naked = chart.FlatMesh.GetNakedEdges();
                if (naked != null && naked.Length > 1)
                {
                    double outerLen = chart.FlatOuterBoundary.Length;
                    foreach (var pl in naked)
                    {
                        if (pl.Length < outerLen - 1e-6)
                        {
                            var hc = new PolylineCurve(pl);
                            hc.Transform(toLayout);
                            holes.Add(hc);
                        }
                    }
                }
                sheetHoles.Add(holes);
                layouts.Add((toLayout, fromLayout, chart));
                offsetX += bbox.Max.X - bbox.Min.X + chartGap;
            }

            if (sheetOutlines.Count == 0)
            { result.ErrorMessage = "No valid chart boundaries found."; return result; }

            // Run freeform packing across all chart sheets simultaneously
            PackingResult packed;
            try
            {
                var solver = new IrregularSheetFillV506(
                    sheetOutlines, sheetHoles,
                    spacing, rotationsDeg, tolerance,
                    sortMode, cornerMode, seed, maxCandidates);
                packed = solver.Pack(parts);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Packing failed: {ex.GetType().Name}: {ex.Message}";
                return result;
            }

            int n = packed.PackedCurves.Count;
            result.PackedCurves3D = new List<Curve>(n);
            result.PlacementPlanes = new List<Plane>(n);
            result.Transforms3D = new List<Transform>(n);
            result.FullTransforms = new List<Transform>(n);
            result.MaxDeviations = new List<double>(n);
            result.PackedCurves2D = new List<Curve>(n);
            result.ChartIndices = new List<int>(n);
            result.PartIndices = new List<int>(n);
            int failedMap = 0;

            for (int i = 0; i < n; i++)
            {
                int si = i < packed.SheetIndices.Count ? packed.SheetIndices[i] : 0;
                int partIdx = i < packed.SourceIndices.Count ? packed.SourceIndices[i] : -1;

                if (si < 0 || si >= layouts.Count)
                {
                    AppendUnset(result, -1, partIdx);
                    continue;
                }

                var (toLayout, fromLayout, chart) = layouts[si];
                result.ChartIndices.Add(si);
                result.PartIndices.Add(partIdx);

                // Convert back from layout space to chart-native coordinate space
                var nativeCurve = packed.PackedCurves[i].DuplicateCurve();
                nativeCurve.Transform(fromLayout);
                result.PackedCurves2D.Add(nativeCurve);

                // Full barycentric 3D curve (shape-deformed, follows surface exactly)
                var c3D = BarycentricMapper2DTo3D.MapCurveTo3DSurface(
                    nativeCurve, chart.FlatMesh, chart.SurfaceMesh3D, tolerance);
                result.PackedCurves3D.Add(c3D);
                if (c3D == null) failedMap++;

                // Fabrication frame: centroid + tangent axes, NO shape distortion
                var (plane, tx, maxDev) = ComputePlacementFrame(
                    nativeCurve, chart.FlatMesh, chart.SurfaceMesh3D, tolerance);
                result.PlacementPlanes.Add(plane);
                result.Transforms3D.Add(tx);
                result.MaxDeviations.Add(maxDev);

                // Full transform: original flat part → 3D surface in one step.
                // packed.Transforms[i] moves the original part to its packed 2D position.
                // tx (Transforms3D) moves from packed 2D position to the 3D surface frame.
                // Composition: apply pack transform first, then surface transform.
                Transform packTx = i < packed.Transforms.Count
                    ? packed.Transforms[i]
                    : Transform.Identity;
                // Also undo the layout offset that was applied during packing:
                // the solver packed into layout space, so packTx includes the layout shift.
                // We compose: fromLayout (undo layout shift) then tx (surface placement).
                // Net full transform = tx * fromLayout * packTx
                var fullTx = Transform.Multiply(tx, Transform.Multiply(fromLayout, packTx));
                result.FullTransforms.Add(fullTx);
            }

            result.UnplacedCurves = new List<Curve>(packed.UnplacedCurves);

            var sb = new StringBuilder();
            sb.AppendLine(packed.Report);
            sb.AppendLine($"Charts used: {sheetOutlines.Count}.");
            if (failedMap > 0)
                sb.AppendLine(
                    $"WARNING: {failedMap} 3D mapping(s) failed (part crosses UV seam).");
            result.Report = sb.ToString().TrimEnd();
            return result;
        }

        // --- Placement frame ------------------------------------------------

        private static (Plane plane3D, Transform transform3D, double maxDeviation)
            ComputePlacementFrame(
                Curve nativeCurve2D,
                Mesh flatMesh,
                Mesh surfaceMesh,
                double tolerance)
        {
            if (nativeCurve2D == null)
                return (Plane.Unset, Transform.Unset, 0);

            var bbox = nativeCurve2D.GetBoundingBox(Plane.WorldXY);
            var center2D = bbox.Center;
            center2D.Z = 0;

            double w = bbox.Max.X - bbox.Min.X;
            double h = bbox.Max.Y - bbox.Min.Y;
            double step = Math.Max(tolerance * 10.0, Math.Min(w, h) * 0.1);
            if (step < 1e-8) step = 1.0;

            var origin3D = BarycentricMapper2DTo3D.MapPoint(
                center2D, flatMesh, surfaceMesh, tolerance);
            if (origin3D == Point3d.Unset)
                return (Plane.Unset, Transform.Unset, 0);

            var xSample3D = BarycentricMapper2DTo3D.MapPoint(
                center2D + new Vector3d(step, 0, 0), flatMesh, surfaceMesh, tolerance);
            var ySample3D = BarycentricMapper2DTo3D.MapPoint(
                center2D + new Vector3d(0, step, 0), flatMesh, surfaceMesh, tolerance);

            var xAxis = xSample3D != Point3d.Unset
                ? xSample3D - origin3D : new Vector3d(1, 0, 0);
            var yAxis = ySample3D != Point3d.Unset
                ? ySample3D - origin3D : new Vector3d(0, 1, 0);

            if (!xAxis.Unitize()) xAxis = Vector3d.XAxis;

            var zAxis = Vector3d.CrossProduct(xAxis, yAxis);
            if (zAxis.Length < 1e-10) zAxis = Vector3d.ZAxis;
            zAxis.Unitize();
            var yOrtho = Vector3d.CrossProduct(zAxis, xAxis);
            if (!yOrtho.Unitize()) yOrtho = Vector3d.YAxis;

            var plane3D = new Plane(origin3D, xAxis, yOrtho);
            var sourcePlane = new Plane(center2D, Vector3d.XAxis, Vector3d.YAxis);
            var transform3D = Transform.PlaneToPlane(sourcePlane, plane3D);

            var corners2D = new[]
            {
                new Point3d(bbox.Min.X, bbox.Min.Y, 0),
                new Point3d(bbox.Max.X, bbox.Min.Y, 0),
                new Point3d(bbox.Max.X, bbox.Max.Y, 0),
                new Point3d(bbox.Min.X, bbox.Max.Y, 0),
            };
            double maxDev = 0;
            foreach (var corner in corners2D)
            {
                var corner3D = BarycentricMapper2DTo3D.MapPoint(
                    corner, flatMesh, surfaceMesh, tolerance);
                if (corner3D == Point3d.Unset) continue;
                double dev = Math.Abs(plane3D.DistanceTo(corner3D));
                if (dev > maxDev) maxDev = dev;
            }

            return (plane3D, transform3D, maxDev);
        }

        // --- Helpers --------------------------------------------------------

        private static List<FrahanSurfaceChart> ExtractCharts(List<GH_ObjectWrapper> wrappers)
        {
            var charts = new List<FrahanSurfaceChart>(wrappers.Count);
            foreach (var w in wrappers)
                if (w?.Value is FrahanSurfaceChart c) charts.Add(c);
            return charts;
        }

        private static void AppendUnset(PackSurfacesResult r, int chartIdx, int partIdx)
        {
            r.PackedCurves3D.Add(null);
            r.PlacementPlanes.Add(Plane.Unset);
            r.Transforms3D.Add(Transform.Unset);
            r.FullTransforms.Add(Transform.Unset);
            r.MaxDeviations.Add(0);
            r.PackedCurves2D.Add(null);
            r.ChartIndices.Add(chartIdx);
            r.PartIndices.Add(partIdx);
        }

        private static List<Curve> FilterNulls(List<Curve> src)
        {
            if (src == null) return new List<Curve>();
            var out_ = new List<Curve>(src.Count);
            foreach (var c in src) if (c != null) out_.Add(c);
            return out_;
        }

        private PackingSortMode ToSortMode(int v)
        {
            if (Enum.IsDefined(typeof(PackingSortMode), v)) return (PackingSortMode)v;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid sort mode.");
            return PackingSortMode.AreaDescending;
        }

        private PackingCornerMode ToCornerMode(int v)
        {
            if (Enum.IsDefined(typeof(PackingCornerMode), v)) return (PackingCornerMode)v;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid corner mode.");
            return PackingCornerMode.BottomLeft;
        }
    }
}
