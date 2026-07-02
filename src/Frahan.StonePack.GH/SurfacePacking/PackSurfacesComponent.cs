#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Frahan.Surface;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Surface
{
    // Public so the async payload type is at least as accessible as the component.
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
    /// Packs closed 2D part curves across ONE OR MORE surface charts using the
    /// deterministic hole-aware nester (Core ContactNfpHoleNester): exact
    /// no-fit-polygon bottom-left-fill with multi-start, so layouts are
    /// 0-overlap by construction and reproducible. EACH chart is its own sheet
    /// (greedy overflow: chart 0 fills first, unplaced parts carry to chart 1,
    /// and so on); each chart's inner naked edges become sheet holes the parts
    /// route around. Packed positions are mapped back to their 3D surfaces.
    ///
    /// The solver runs on a background task so the canvas never freezes; while a
    /// new layout computes, the previous result stays visible and live progress
    /// ticks in the component message. The finished result pops in when ready.
    ///
    /// Fabrication outputs:
    ///   Full Transform (FT): single transform from the original flat part
    ///   directly to its 3D surface position. Apply to the ORIGINAL part.
    ///   Transforms 3D (T3): from the PACKED 2D position to the 3D surface.
    ///   Apply to Packed 2D curves.
    ///   Max Deviation: max gap (model units) between the flat part and the
    ///   curved surface at the four bounding-box corners of the placement.
    ///   Part Index: 0-based index into the original Parts input per packed part.
    /// </summary>
    public sealed class PackSurfacesComponent : FrahanComponentBase
    {
        public PackSurfacesComponent()
            : base(
                "Pack Surfaces", "PackSurfs",
                "Packs 2D shapes across one or more surface charts with the deterministic hole-aware " +
                "nester (exact NFP bottom-left-fill, multi-start, 0-overlap), then maps them onto the 3D " +
                "surfaces. Runs async: the canvas stays live and the result pops in when ready. Outputs " +
                "Full Transform to place original flat parts on the surface without distortion.",
                "Frahan", "Surface Packing")
        {
        }

        // GUID unchanged: existing canvases keep finding this component.
        public override Guid ComponentGuid =>
            new Guid("C4A8D2E1-7F3B-4C5D-9A2E-6B8D4F1E3C7A");

        protected override Bitmap Icon => IconProvider.Load("SurfaceUnroll.png");

        // --- Params ---------------------------------------------------------

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            // 0..9 kept in their original slots (no reindex) so existing wiring
            // is preserved; the four V506-only controls are now inert under the
            // deterministic hole-aware engine and documented as such.
            p.AddGenericParameter("Surface Maps", "Maps",
                "One or more FrahanSurfaceChart objects from the Surface Chart component. Each becomes one " +
                "sheet (greedy overflow chart 0 -> chart 1 -> ...).",
                GH_ParamAccess.list);
            p.AddCurveParameter("Parts", "P",
                "Closed planar 2D part curves to pack.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Spacing", "Gap",
                "Clearance between parts and chart boundaries (model units).",
                GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Rotations", "R",
                "Allowed rotation angles in degrees. The hole-aware engine uses the COUNT of angles as its " +
                "uniform base rotation count (default list 0/90/180/270 -> 4) and extends it with " +
                "contact (edge-alignment) angles. Pass more angles to raise the base count.",
                GH_ParamAccess.list, 0.0);
            p.AddNumberParameter("Tolerance", "T",
                "Geometric tolerance for the 3D barycentric mapping and containment checks.",
                GH_ParamAccess.item, 0.01);
            p.AddIntegerParameter("Sort Mode", "M",
                "IGNORED by the hole-aware engine (kept for compatibility). The engine multi-starts over " +
                "area/max-dim/width/height orders automatically and keeps the best - see MultiStart.",
                GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Corner Mode", "Cnr",
                "IGNORED by the hole-aware engine (kept for compatibility). Placement is always bottom-left-fill.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Seed", "Seed",
                "IGNORED by the hole-aware engine (kept for compatibility). The engine is deterministic: " +
                "identical inputs always reproduce the same layout.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Max Candidates", "Max",
                "IGNORED by the hole-aware engine (kept for compatibility). The exact NFP enumerates feasible " +
                "placements directly.",
                GH_ParamAccess.item, 300);
            p.AddBooleanParameter("Run", "Run",
                "Set to True to execute packing. False shows the idle message and cancels any running solve.",
                GH_ParamAccess.item, false);
            // 10..12 appended (optional): hole-aware engine controls.
            p.AddIntegerParameter("ContactRotations", "CR",
                "Longest-edge count per polygon used to build contact (edge-alignment) rotation angles so " +
                "parts seat flush. Default 6.",
                GH_ParamAccess.item, 6);
            p[10].Optional = true;
            p.AddIntegerParameter("Resolution", "Res",
                "Solver sampling resolution for smooth part curves (16..200, default 24). This only sets the " +
                "collision proxy - packed output is always the exact original curve. Solve time grows " +
                "~quadratically; raise only for tight concave notches.",
                GH_ParamAccess.item, 24);
            p[11].Optional = true;
            p.AddIntegerParameter("MultiStart", "MS",
                "Deterministic part orders the engine tries per chart, keeping the densest valid layout " +
                "(1..4, default 4). 1 = single largest-first pass. Higher raises density at ~linear cost and " +
                "never reduces placements or validity.",
                GH_ParamAccess.item, 4);
            p[12].Optional = true;
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
            p.AddTransformParameter("Transforms 3D", "T3",
                "Transform from PACKED 2D position to the 3D surface placement frame. " +
                "Apply to Packed 2D curves to get rigid (non-deformed) parts on the surface.",
                GH_ParamAccess.list);
            // 3
            p.AddTransformParameter("Full Transform", "FT",
                "Composed transform: original flat part -> 3D surface in one step. " +
                "Apply to the ORIGINAL part geometry (before packing) using Part Index to select it.",
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

        // ─── async solve (HoleNest self-trigger pattern) ────────────────────
        // The solver + 3D mapping run on a background Task so the canvas never
        // freezes; the PREVIOUS result stays visible while a new one computes,
        // progress text ticks live, and the finished result pops in via
        // ScheduleSolution. A self-trigger flag distinguishes MY OWN scheduled
        // re-solves (emit only) from real GH input changes (rebuild + restart),
        // and a stable bbox hash prevents redundant recomputes.
        private readonly object _gate = new object();
        private Task _task;
        private CancellationTokenSource _cts;
        private ulong _taskHash;
        private volatile string _progress = "";
        private Payload _readyPayload;
        private string _readyError;
        private bool _hasReady;
        private Payload _last;
        private volatile bool _selfTrigger;

        private sealed class Snapshot
        {
            public List<FrahanSurfaceChart> Charts;
            public List<IReadOnlyList<(double X, double Y)>> Sheets;
            public List<double> SheetZ;
            public List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> SheetHoles;
            public List<HoleNestPart> Parts;
            public List<Curve> Originals;   // duplicated on the UI thread (owned)
            public List<int> InputIndexOf;
            public List<double> PartZOf;
            public double Tolerance, EngineSpacing, MaxDev;
            public int BaseRotations, ContactRotations, MultiStart;
            public ulong Hash;
        }

        private sealed class Payload
        {
            public Snapshot Snap;
            public PackSurfacesResult Result;
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            // ── SELF-TRIGGERED re-solve: emit the latest result, no rebuild ──
            if (_selfTrigger)
            {
                _selfTrigger = false;
                // honor Run even on a self-scheduled re-solve: if Run flipped
                // false in the +10ms schedule window, clear the canvas rather
                // than repainting a layout (matches the AsyncScanComponent gate).
                bool runST = false; da.GetData(9, ref runST);
                if (!runST) { Message = "idle"; EmitEmpty(da, "Run is false."); return; }
                Payload sready = null; string serr = null; bool srunning;
                lock (_gate)
                {
                    if (_hasReady)
                    {
                        sready = _readyPayload; serr = _readyError;
                        _readyPayload = null; _readyError = null; _hasReady = false;
                        if (serr == null && sready != null) _last = sready;
                    }
                    srunning = _task != null && !_task.IsCompleted;
                }
                if (serr != null)
                { Message = "error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, serr); EmitEmpty(da, serr); return; }
                if (_last != null) { Message = srunning ? _progress : null; EmitPayload(da, _last, srunning); }
                else EmitEmpty(da, "Packing in the background — canvas stays live; the result pops in when ready.");
                return;
            }

            // ── REAL GH expiration: honor Run, then build/cache/restart ──────
            bool run = false;
            da.GetData(9, ref run);
            if (!run)
            {
                lock (_gate) { try { _cts?.Cancel(); } catch { } }
                Message = "idle";
                EmitEmpty(da, "Run is false.");
                return;
            }

            var snap = BuildSnapshot(da);
            if (snap == null) return; // validation error already reported

            Payload ready = null; string readyError = null; bool taskRunning; ulong taskHash;
            lock (_gate)
            {
                if (_hasReady)
                {
                    ready = _readyPayload; readyError = _readyError;
                    _readyPayload = null; _readyError = null; _hasReady = false;
                    if (readyError == null && ready != null) _last = ready;
                }
                taskRunning = _task != null && !_task.IsCompleted;
                taskHash = _taskHash;
            }
            if (readyError != null)
            {
                Message = "error";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, readyError);
                EmitEmpty(da, readyError);
                return;
            }

            // a task is already running for these exact inputs: show progress
            if (taskRunning && taskHash == snap.Hash)
            {
                Message = _progress;
                if (_last != null) EmitPayload(da, _last, true);
                else EmitEmpty(da, "Packing in the background — canvas stays live; the result pops in when ready.");
                return;
            }

            // cache hit: same inputs as the last completed solve -> instant emit
            if (_last != null && _last.Snap.Hash == snap.Hash && !taskRunning)
            {
                Message = null;
                EmitPayload(da, _last, false);
                return;
            }

            // genuinely new inputs -> (re)start the background solve
            StartCompute(snap);
            Message = "packing...";
            if (_last != null) EmitPayload(da, _last, true);
            else EmitEmpty(da, "Packing in the background — canvas stays live; the result pops in when ready.");
        }

        private void StartCompute(Snapshot snap)
        {
            var doc = OnPingDocument();
            var iguid = InstanceGuid;
            lock (_gate)
            {
                try { _cts?.Cancel(); } catch { }
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _taskHash = snap.Hash;
                _progress = $"packing {snap.Parts.Count} parts...";
                _task = Task.Run(() =>
                {
                    PackSurfacesResult result = null; string error = null;
                    // PROGRESSIVE: every ~300 ms publish progress TEXT only (3D
                    // mapping is too heavy to redo per tick) and self-trigger a
                    // cheap emit-only re-solve so the message updates live.
                    var tick = System.Diagnostics.Stopwatch.StartNew();
                    Action<HoleNestResult> onPlacement = partial =>
                    {
                        if (token.IsCancellationRequested) return;
                        if (tick.ElapsedMilliseconds < 300) return;
                        tick.Restart();
                        _progress = $"packing {partial.PlacedCount}/{snap.Parts.Count}...";
                        _selfTrigger = true;
                        AsyncResolve.Kick(doc, iguid);
                    };
                    try { result = ComputePacking(snap, onPlacement, token); }
                    catch (Exception ex) { error = "Surface packing failed: " + ex.Message; }
                    if (token.IsCancellationRequested) return; // stale job: discard
                    lock (_gate) { _readyPayload = result == null ? null : new Payload { Snap = snap, Result = result }; _readyError = error; _hasReady = true; }
                    _selfTrigger = true;
                    AsyncResolve.Kick(doc, iguid); // guarded delivery: schedule, then UI-thread fallback
                }, token);
            }
        }

        // --- Worker (background thread) -------------------------------------

        private static PackSurfacesResult ComputePacking(
            Snapshot snap, Action<HoleNestResult> onPlacement, CancellationToken token)
        {
            var result = new PackSurfacesResult
            {
                PackedCurves3D = new List<Curve>(),
                PlacementPlanes = new List<Plane>(),
                Transforms3D = new List<Transform>(),
                FullTransforms = new List<Transform>(),
                MaxDeviations = new List<double>(),
                PackedCurves2D = new List<Curve>(),
                ChartIndices = new List<int>(),
                PartIndices = new List<int>(),
                UnplacedCurves = new List<Curve>(),
            };

            List<HoleNestResult> perSheet;
            try
            {
                perSheet = ContactNfpHoleNester.PackSheets(
                    snap.Sheets, snap.SheetHoles, snap.Parts,
                    snap.EngineSpacing, snap.BaseRotations, snap.ContactRotations,
                    onPlacement: onPlacement, multiStartOrders: snap.MultiStart);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Packing failed: {ex.GetType().Name}: {ex.Message}";
                return result;
            }

            int placedCount = 0, failedMap = 0;
            var placedPart = new bool[snap.Parts.Count];

            for (int si = 0; si < perSheet.Count && si < snap.Charts.Count; si++)
            {
                var chart = snap.Charts[si];
                double sheetZ = si < snap.SheetZ.Count ? snap.SheetZ[si] : 0.0;
                foreach (var pl in perSheet[si].Placements)
                {
                    if (token.IsCancellationRequested) return null;
                    int prep = pl.PartIndex;
                    if (prep < 0 || prep >= snap.Originals.Count) continue;
                    placedCount++;
                    if (prep < placedPart.Length) placedPart[prep] = true;
                    int partIdx = prep < snap.InputIndexOf.Count ? snap.InputIndexOf[prep] : -1;

                    // packed-2D curve in the chart's native flat space (Z lifted
                    // to the flat chart plane so barycentric mapping locates it)
                    double partZ = prep < snap.PartZOf.Count ? snap.PartZOf[prep] : 0.0;
                    var packTx = SurfaceHoleNestBridge.PackTransform(pl, sheetZ - partZ);
                    Curve nativeCurve = snap.Originals[prep] != null ? snap.Originals[prep].DuplicateCurve() : null;
                    if (nativeCurve != null && !nativeCurve.Transform(packTx)) nativeCurve = null;

                    result.ChartIndices.Add(si);
                    result.PartIndices.Add(partIdx);
                    result.PackedCurves2D.Add(nativeCurve);

                    // shape-deformed 3D curve following the surface exactly
                    var c3D = nativeCurve == null ? null : BarycentricMapper2DTo3D.MapCurveTo3DSurface(
                        nativeCurve, chart.FlatMesh, chart.SurfaceMesh3D, snap.Tolerance);
                    result.PackedCurves3D.Add(c3D);
                    if (c3D == null) failedMap++;

                    // rigid fabrication frame (no shape distortion)
                    var (plane, tx3D, maxDev) = ComputePlacementFrame(
                        nativeCurve, chart.FlatMesh, chart.SurfaceMesh3D, snap.Tolerance);
                    result.PlacementPlanes.Add(plane);
                    result.Transforms3D.Add(tx3D);
                    result.MaxDeviations.Add(maxDev);

                    // original flat part -> 3D surface in one step
                    var fullTx = Transform.Multiply(tx3D, packTx);
                    result.FullTransforms.Add(fullTx);
                }
            }

            for (int prep = 0; prep < snap.Parts.Count; prep++)
                if (!placedPart[prep] && prep < snap.Originals.Count && snap.Originals[prep] != null)
                    result.UnplacedCurves.Add(snap.Originals[prep]);

            var sb = new StringBuilder();
            sb.AppendLine($"Pack Surfaces (hole-aware) — Placed: {placedCount}/{snap.Parts.Count} across {perSheet.Count} chart(s).");
            for (int si = 0; si < perSheet.Count; si++)
                sb.AppendLine($"  chart {si}: {perSheet[si].Placements.Count} placed, valid {perSheet[si].Valid}, {perSheet[si].ElapsedMs:0.0} ms.");
            if (snap.MaxDev > 0)
                sb.AppendLine($"Proxy-deviation compensation: +{2.0 * snap.MaxDev:0.####} units on spacing.");
            if (failedMap > 0)
                sb.AppendLine($"WARNING: {failedMap} 3D mapping(s) failed (part crosses a UV seam).");
            result.Report = sb.ToString().TrimEnd();
            return result;
        }

        // --- Snapshot build (UI thread) -------------------------------------

        private Snapshot BuildSnapshot(IGH_DataAccess da)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            var partCurves = new List<Curve>();
            double spacing = 5.0;
            var rotations = new List<double>();
            double tolerance = 0.01;
            int contactRotations = 6, resolution = 24, multiStart = 4;

            if (!da.GetDataList(0, wrappers) || wrappers.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Surface Maps connected."); return null; }
            if (!da.GetDataList(1, partCurves) || partCurves.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No parts input."); return null; }
            da.GetData(2, ref spacing);
            da.GetDataList(3, rotations);
            da.GetData(4, ref tolerance);
            // 5..8 (Sort/Corner/Seed/MaxCandidates) intentionally not read: inert.
            da.GetData(10, ref contactRotations);
            da.GetData(11, ref resolution);
            da.GetData(12, ref multiStart);

            spacing = Math.Max(0.0, spacing);
            tolerance = Math.Max(1e-9, tolerance);
            int baseRotations = Math.Max(1, rotations.Count);
            contactRotations = Math.Max(0, contactRotations);
            resolution = Math.Max(16, Math.Min(SurfaceHoleNestBridge.MaxVerts, resolution));
            multiStart = Math.Max(1, multiStart);

            var charts = new List<FrahanSurfaceChart>();
            foreach (var w in wrappers)
                if (w?.Value is FrahanSurfaceChart c && c.FlatOuterBoundary != null && c.FlatMesh != null)
                    charts.Add(c);
            if (charts.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid FrahanSurfaceChart found in Maps input."); return null; }

            // each chart -> one sheet (native flat space) + its inner-edge holes
            var sheets = new List<IReadOnlyList<(double X, double Y)>>(charts.Count);
            var sheetZ = new List<double>(charts.Count);
            var sheetHoles = new List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>(charts.Count);
            double sheetDev = 0.0;
            var keptCharts = new List<FrahanSurfaceChart>(charts.Count);
            foreach (var chart in charts)
            {
                var outerCurve = new PolylineCurve(chart.FlatOuterBoundary);
                var sr = SurfaceHoleNestBridge.CurveToLoop(outerCurve, SurfaceHoleNestBridge.SheetSampleVerts);
                if (sr.Loop == null) continue;
                var holes = new List<IReadOnlyList<(double X, double Y)>>();
                var naked = chart.FlatMesh.GetNakedEdges();
                if (naked != null && naked.Length > 1)
                {
                    double outerLen = chart.FlatOuterBoundary.Length;
                    foreach (var nk in naked)
                    {
                        if (nk.Length >= outerLen - 1e-6) continue; // the outer boundary itself
                        var hr = SurfaceHoleNestBridge.CurveToLoop(new PolylineCurve(nk), SurfaceHoleNestBridge.SheetSampleVerts);
                        if (hr.Loop != null) { holes.Add(hr.Loop); if (hr.MaxDev > sheetDev) sheetDev = hr.MaxDev; }
                    }
                }
                sheets.Add(sr.Loop); sheetZ.Add(sr.PlaneZ); sheetHoles.Add(holes);
                if (sr.MaxDev > sheetDev) sheetDev = sr.MaxDev;
                keptCharts.Add(chart);
            }
            if (sheets.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid chart boundaries found."); return null; }

            // parts -> HoleNestParts (outer only; surface parts carry no holes)
            var parts = new List<HoleNestPart>();
            var originals = new List<Curve>();
            var inputIndexOf = new List<int>();
            var partZOf = new List<double>();
            double partDev = 0.0;
            int droppedParts = 0;
            for (int i = 0; i < partCurves.Count; i++)
            {
                var pr = SurfaceHoleNestBridge.CurveToLoop(partCurves[i], resolution);
                if (pr.Loop == null) { droppedParts++; continue; }
                if (pr.MaxDev > partDev) partDev = pr.MaxDev;
                parts.Add(new HoleNestPart { Outer = pr.Loop });
                originals.Add(partCurves[i] != null ? partCurves[i].DuplicateCurve() : null);
                inputIndexOf.Add(i);
                partZOf.Add(pr.PlaneZ);
            }
            if (droppedParts > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{droppedParts} part curve(s) ignored (must be closed and WorldXY-parallel).");
            if (parts.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid part curves."); return null; }

            // proxy-deviation compensation (mirrors HoleNest): part-pair clearance
            // needs 2x the worst part deviation, the sheet term enters once.
            double maxDev = Math.Max(partDev, sheetDev);
            double engineSpacing = spacing + 2.0 * partDev + sheetDev;

            // stable, sampling-free input hash for the async loop-guard
            ulong h = 1469598103934665603UL;
            void HD(double v) { unchecked { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; } }
            void HQ(double v) { HD(Math.Round(v * 1e4) / 1e4); }
            void HBox(Curve c)
            {
                if (c == null) { HD(-7.0); return; }
                var b = c.GetBoundingBox(false);
                HQ(b.Min.X); HQ(b.Min.Y); HQ(b.Min.Z); HQ(b.Max.X); HQ(b.Max.Y); HQ(b.Max.Z);
                HD(c.SpanCount);
            }
            HQ(spacing); HD(baseRotations); HD(contactRotations); HD(resolution); HD(multiStart); HQ(tolerance);
            HD(keptCharts.Count);
            foreach (var chart in keptCharts)
            {
                HBox(new PolylineCurve(chart.FlatOuterBoundary));
                HD(chart.FlatMesh != null ? chart.FlatMesh.Vertices.Count : -1);
                HD(chart.SurfaceMesh3D != null ? chart.SurfaceMesh3D.Vertices.Count : -1);
            }
            HD(partCurves.Count);
            foreach (var c in partCurves) HBox(c);

            return new Snapshot
            {
                Charts = keptCharts, Sheets = sheets, SheetZ = sheetZ, SheetHoles = sheetHoles,
                Parts = parts, Originals = originals, InputIndexOf = inputIndexOf, PartZOf = partZOf,
                Tolerance = tolerance, EngineSpacing = engineSpacing, MaxDev = maxDev,
                BaseRotations = baseRotations, ContactRotations = contactRotations, MultiStart = multiStart,
                Hash = h,
            };
        }

        // --- Emit -----------------------------------------------------------

        private void EmitPayload(IGH_DataAccess da, Payload payload, bool stale)
        {
            var r = payload.Result;
            if (r == null) { EmitEmpty(da, "No result."); return; }
            if (!string.IsNullOrWhiteSpace(r.ErrorMessage))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, r.ErrorMessage); EmitEmpty(da, r.ErrorMessage); return; }

            if (!stale && r.UnplacedCurves?.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{r.UnplacedCurves.Count} part(s) could not be placed.");

            da.SetDataList(0, FilterNulls(r.PackedCurves3D));
            da.SetDataList(1, r.PlacementPlanes ?? new List<Plane>());
            da.SetDataList(2, r.Transforms3D ?? new List<Transform>());
            da.SetDataList(3, r.FullTransforms ?? new List<Transform>());
            da.SetDataList(4, r.MaxDeviations ?? new List<double>());
            da.SetDataList(5, FilterNulls(r.PackedCurves2D));
            da.SetDataList(6, r.ChartIndices ?? new List<int>());
            da.SetDataList(7, r.PartIndices ?? new List<int>());
            da.SetDataList(8, r.UnplacedCurves ?? new List<Curve>());
            da.SetData(9, (stale ? "[updating...] " : "") + (r.Report ?? string.Empty));
        }

        private void EmitEmpty(IGH_DataAccess da, string message)
        {
            da.SetDataList(0, new List<Curve>());
            da.SetDataList(1, new List<Plane>());
            da.SetDataList(2, new List<Transform>());
            da.SetDataList(3, new List<Transform>());
            da.SetDataList(4, new List<double>());
            da.SetDataList(5, new List<Curve>());
            da.SetDataList(6, new List<int>());
            da.SetDataList(7, new List<int>());
            da.SetDataList(8, new List<Curve>());
            da.SetData(9, message);
        }

        // --- Placement frame (unchanged math; operates on the native packed curve) ---

        private static (Plane plane3D, Transform transform3D, double maxDeviation)
            ComputePlacementFrame(Curve nativeCurve2D, Mesh flatMesh, Mesh surfaceMesh, double tolerance)
        {
            if (nativeCurve2D == null)
                return (Plane.Unset, Transform.Unset, 0);

            var bbox = nativeCurve2D.GetBoundingBox(Plane.WorldXY);
            var center2D = bbox.Center; center2D.Z = 0;

            double w = bbox.Max.X - bbox.Min.X;
            double hh = bbox.Max.Y - bbox.Min.Y;
            double step = Math.Max(tolerance * 10.0, Math.Min(w, hh) * 0.1);
            if (step < 1e-8) step = 1.0;

            var origin3D = BarycentricMapper2DTo3D.MapPoint(center2D, flatMesh, surfaceMesh, tolerance);
            if (origin3D == Point3d.Unset)
                return (Plane.Unset, Transform.Unset, 0);

            var xSample3D = BarycentricMapper2DTo3D.MapPoint(center2D + new Vector3d(step, 0, 0), flatMesh, surfaceMesh, tolerance);
            var ySample3D = BarycentricMapper2DTo3D.MapPoint(center2D + new Vector3d(0, step, 0), flatMesh, surfaceMesh, tolerance);

            var xAxis = xSample3D != Point3d.Unset ? xSample3D - origin3D : new Vector3d(1, 0, 0);
            var yAxis = ySample3D != Point3d.Unset ? ySample3D - origin3D : new Vector3d(0, 1, 0);
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
                var corner3D = BarycentricMapper2DTo3D.MapPoint(corner, flatMesh, surfaceMesh, tolerance);
                if (corner3D == Point3d.Unset) continue;
                double dev = Math.Abs(plane3D.DistanceTo(corner3D));
                if (dev > maxDev) maxDev = dev;
            }
            return (plane3D, transform3D, maxDev);
        }

        private static List<Curve> FilterNulls(List<Curve> src)
        {
            if (src == null) return new List<Curve>();
            var outc = new List<Curve>(src.Count);
            foreach (var c in src) if (c != null) outc.Add(c);
            return outc;
        }
    }
}
