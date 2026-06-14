#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Frahan.Surface;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Surface
{
    // Public so the async payload type is at least as accessible as the component.
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
    /// Packs closed 2D part curves onto a single FrahanSurfaceChart using the
    /// deterministic hole-aware nester (Core ContactNfpHoleNester): exact
    /// no-fit-polygon bottom-left-fill with multi-start, 0-overlap by
    /// construction and reproducible. The chart's flat outer boundary is the
    /// sheet, its inner naked edges are sheet holes, and clearance is scaled by
    /// the chart's max edge stretch (conservative for distorted charts). Packed
    /// positions are lifted to the 3D surface via barycentric interpolation.
    ///
    /// Runs async: the canvas stays live and the result pops in when ready.
    /// </summary>
    [Algorithm("Barycentric 2D-to-3D mapping", "Floater 2003, Computer Aided Geometric Design 20(1):19-27 Mean value coordinates", Doi = "10.1016/S0167-8396(03)00002-5", Note = "Mean-value barycentric interpolation lifts packed UV curves back to the 3D surface", WikiPath = "wiki/algorithms/surface_mosaicing/")]
    [Algorithm("Exact No-Fit-Polygon Bottom-Left-Fill", "Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). A New Bottom-Left-Fill Heuristic Algorithm for the Two-Dimensional Irregular Packing Problem. Operations Research 54(3):587-601", Doi = "10.1287/opre.1060.0293", Note = "Core ContactNfpHoleNester places parts on the flat chart sheet")]
    public sealed class PackOnSurfaceComponent : FrahanComponentBase
    {
        public PackOnSurfaceComponent()
            : base(
                "Pack On Surface", "PackSurf",
                "Packs 2D shapes onto a surface chart with the deterministic hole-aware nester (exact NFP " +
                "bottom-left-fill, multi-start, 0-overlap), then lifts packed curves to the 3D surface via " +
                "barycentric mapping. Runs async: the canvas stays live. [Floater 2003]",
                "Frahan", "Surface Packing")
        {
        }

        // GUID unchanged: existing canvases keep finding this component.
        public override Guid ComponentGuid =>
            new Guid("B7E4D9C1-3F8A-4B2E-91C6-5D7F3A8B2E1D");

        protected override Bitmap Icon => IconProvider.Load("SurfaceTile.png");

        // --- Params ---------------------------------------------------------

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Surface Map", "Map",
                "FrahanSurfaceChart from the Surface Chart component.",
                GH_ParamAccess.item);
            p.AddCurveParameter("Parts", "P",
                "Closed planar 2D part curves to pack (in the flat chart XY plane).",
                GH_ParamAccess.list);
            p.AddNumberParameter("Spacing", "Gap",
                "Clearance between parts and between parts and the sheet boundary (model units).",
                GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Rotations", "R",
                "Allowed rotation angles in degrees. The hole-aware engine uses the COUNT of angles as its " +
                "uniform base rotation count (default list 0/90/180/270 -> 4), extended with contact angles.",
                GH_ParamAccess.list, 0.0);
            p.AddNumberParameter("Tolerance", "T",
                "Geometric tolerance for the 3D barycentric mapping and containment checks.",
                GH_ParamAccess.item, 0.01);
            p.AddIntegerParameter("Sort Mode", "M",
                "IGNORED by the hole-aware engine (kept for compatibility). It multi-starts over " +
                "area/max-dim/width/height orders automatically - see MultiStart.",
                GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Corner Mode", "Cnr",
                "IGNORED by the hole-aware engine (kept for compatibility). Placement is always bottom-left-fill.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Seed", "Seed",
                "IGNORED by the hole-aware engine (kept for compatibility). The engine is deterministic.",
                GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Max Candidates", "Max",
                "IGNORED by the hole-aware engine (kept for compatibility). The exact NFP enumerates feasible " +
                "placements directly.",
                GH_ParamAccess.item, 300);
            p.AddBooleanParameter("Run", "Run",
                "Set to True to execute packing. False shows the idle message and cancels any running solve.",
                GH_ParamAccess.item, false);
            p.AddIntegerParameter("ContactRotations", "CR",
                "Longest-edge count per polygon used to build contact (edge-alignment) rotation angles. Default 6.",
                GH_ParamAccess.item, 6);
            p[10].Optional = true;
            p.AddIntegerParameter("Resolution", "Res",
                "Solver sampling resolution for smooth part curves (16..200, default 24). Collision proxy only; " +
                "packed output is the exact original curve. Solve time grows ~quadratically.",
                GH_ParamAccess.item, 24);
            p[11].Optional = true;
            p.AddIntegerParameter("MultiStart", "MS",
                "Deterministic part orders the engine tries, keeping the densest valid layout (1..4, default 4). " +
                "1 = single largest-first pass. Higher raises density at ~linear cost, never reduces placements.",
                GH_ParamAccess.item, 4);
            p[12].Optional = true;
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

        // ─── async solve (HoleNest self-trigger pattern) ────────────────────
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
            public FrahanSurfaceChart Chart;
            public IReadOnlyList<(double X, double Y)> Sheet;
            public double SheetZ;
            public IReadOnlyList<IReadOnlyList<(double X, double Y)>> SheetHoles;
            public List<HoleNestPart> Parts;
            public List<Curve> Originals;
            public List<double> PartZOf;
            public double Tolerance, EngineSpacing, MaxDev, AdjSpacing, UserSpacing, MaxStretch;
            public int BaseRotations, ContactRotations, MultiStart;
            public ulong Hash;
        }

        private sealed class Payload
        {
            public Snapshot Snap;
            public PackOnSurfaceResult Result;
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
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
            if (snap == null) return;

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
            { Message = "error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, readyError); EmitEmpty(da, readyError); return; }

            if (taskRunning && taskHash == snap.Hash)
            {
                Message = _progress;
                if (_last != null) EmitPayload(da, _last, true);
                else EmitEmpty(da, "Packing in the background — canvas stays live; the result pops in when ready.");
                return;
            }

            if (_last != null && _last.Snap.Hash == snap.Hash && !taskRunning)
            { Message = null; EmitPayload(da, _last, false); return; }

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
                    PackOnSurfaceResult result = null; string error = null;
                    var tick = System.Diagnostics.Stopwatch.StartNew();
                    Action<HoleNestResult> onPlacement = partial =>
                    {
                        if (token.IsCancellationRequested) return;
                        if (tick.ElapsedMilliseconds < 300) return;
                        tick.Restart();
                        _progress = $"packing {partial.PlacedCount}/{snap.Parts.Count}...";
                        _selfTrigger = true;
                        try { doc?.ScheduleSolution(10, d => { if (d?.FindComponent(iguid) is GH_Component c) c.ExpireSolution(true); }); }
                        catch { }
                    };
                    try { result = ComputePacking(snap, onPlacement, token); }
                    catch (Exception ex) { error = "Surface packing failed: " + ex.Message; }
                    if (token.IsCancellationRequested) return;
                    lock (_gate) { _readyPayload = result == null ? null : new Payload { Snap = snap, Result = result }; _readyError = error; _hasReady = true; }
                    _selfTrigger = true;
                    try { doc?.ScheduleSolution(10, d => { if (d?.FindComponent(iguid) is GH_Component c) c.ExpireSolution(true); }); }
                    catch { }
                }, token);
            }
        }

        // --- Worker (background thread) -------------------------------------

        private static PackOnSurfaceResult ComputePacking(
            Snapshot snap, Action<HoleNestResult> onPlacement, CancellationToken token)
        {
            var result = new PackOnSurfaceResult
            {
                PackedCurves3D = new List<Curve>(),
                PackedCurves2D = new List<Curve>(),
                UnplacedCurves = new List<Curve>(),
            };

            List<HoleNestResult> perSheet;
            try
            {
                perSheet = ContactNfpHoleNester.PackSheets(
                    new[] { snap.Sheet }, new[] { snap.SheetHoles }, snap.Parts,
                    snap.EngineSpacing, snap.BaseRotations, snap.ContactRotations,
                    onPlacement: onPlacement, multiStartOrders: snap.MultiStart);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Packing solver failed: {ex.GetType().Name}: {ex.Message}";
                return result;
            }

            var chart = snap.Chart;
            int placedCount = 0, failedMappings = 0;
            var placedPart = new bool[snap.Parts.Count];
            var sheetResult = perSheet.Count > 0 ? perSheet[0] : new HoleNestResult();

            foreach (var pl in sheetResult.Placements)
            {
                if (token.IsCancellationRequested) return null;
                int prep = pl.PartIndex;
                if (prep < 0 || prep >= snap.Originals.Count) continue;
                placedCount++;
                if (prep < placedPart.Length) placedPart[prep] = true;

                double partZ = prep < snap.PartZOf.Count ? snap.PartZOf[prep] : 0.0;
                var packTx = SurfaceHoleNestBridge.PackTransform(pl, snap.SheetZ - partZ);
                Curve nativeCurve = snap.Originals[prep] != null ? snap.Originals[prep].DuplicateCurve() : null;
                if (nativeCurve != null && !nativeCurve.Transform(packTx)) nativeCurve = null;
                if (nativeCurve == null) continue;

                result.PackedCurves2D.Add(nativeCurve);
                var c3D = BarycentricMapper2DTo3D.MapCurveTo3DSurface(
                    nativeCurve, chart.FlatMesh, chart.SurfaceMesh3D, snap.Tolerance);
                result.PackedCurves3D.Add(c3D);
                if (c3D == null) failedMappings++;
            }

            for (int prep = 0; prep < snap.Parts.Count; prep++)
                if (!placedPart[prep] && prep < snap.Originals.Count && snap.Originals[prep] != null)
                    result.UnplacedCurves.Add(snap.Originals[prep]);

            result.FailedMappingCount = failedMappings;

            var sb = new StringBuilder();
            sb.AppendLine($"Pack On Surface (hole-aware) — Placed: {placedCount}/{snap.Parts.Count}, " +
                $"valid {sheetResult.Valid}, {sheetResult.ElapsedMs:0.0} ms.");
            if (snap.AdjSpacing > snap.UserSpacing + 1e-6)
                sb.AppendLine($"Spacing adjusted: {snap.UserSpacing:F3} -> {snap.AdjSpacing:F3} (MaxEdgeStretch = {snap.MaxStretch:F3}x).");
            int holeCount = snap.SheetHoles != null ? snap.SheetHoles.Count : 0;
            if (holeCount > 0) sb.AppendLine($"Sheet holes: {holeCount}.");
            if (snap.MaxDev > 0) sb.AppendLine($"Proxy-deviation compensation: +{2.0 * snap.MaxDev:0.####} units on spacing.");
            if (failedMappings > 0)
                sb.AppendLine($"WARNING: {failedMappings} packed curve(s) failed 3D mapping (cross a UV seam).");
            result.Report = sb.ToString().TrimEnd();
            return result;
        }

        // --- Snapshot build (UI thread) -------------------------------------

        private Snapshot BuildSnapshot(IGH_DataAccess da)
        {
            var chartWrapper = new GH_ObjectWrapper();
            var partCurves = new List<Curve>();
            double spacing = 5.0;
            var rotations = new List<double>();
            double tolerance = 0.01;
            int contactRotations = 6, resolution = 24, multiStart = 4;

            if (!da.GetData(0, ref chartWrapper) || chartWrapper?.Value == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Surface Map connected."); return null; }
            var chart = chartWrapper.Value as FrahanSurfaceChart;
            if (chart == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Surface Map input is not a FrahanSurfaceChart."); return null; }
            if (chart.FlatOuterBoundary == null || chart.FlatOuterBoundary.Count < 3)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Chart has no outer boundary — cannot define sheet."); return null; }
            if (!da.GetDataList(1, partCurves) || partCurves.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No parts input."); return null; }
            da.GetData(2, ref spacing);
            da.GetDataList(3, rotations);
            da.GetData(4, ref tolerance);
            da.GetData(10, ref contactRotations);
            da.GetData(11, ref resolution);
            da.GetData(12, ref multiStart);

            spacing = Math.Max(0.0, spacing);
            tolerance = Math.Max(1e-9, tolerance);
            int baseRotations = Math.Max(1, rotations.Count);
            contactRotations = Math.Max(0, contactRotations);
            resolution = Math.Max(16, Math.Min(SurfaceHoleNestBridge.MaxVerts, resolution));
            multiStart = Math.Max(1, multiStart);

            var sr = SurfaceHoleNestBridge.CurveToLoop(new PolylineCurve(chart.FlatOuterBoundary), SurfaceHoleNestBridge.SheetSampleVerts);
            if (sr.Loop == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Chart outer boundary is not a valid WorldXY-parallel closed loop."); return null; }

            double sheetDev = sr.MaxDev;
            var holes = new List<IReadOnlyList<(double X, double Y)>>();
            var naked = chart.FlatMesh?.GetNakedEdges();
            if (naked != null && naked.Length > 1)
            {
                double outerLen = chart.FlatOuterBoundary.Length;
                foreach (var nk in naked)
                {
                    if (nk.Length >= outerLen - 1e-6) continue;
                    var hr = SurfaceHoleNestBridge.CurveToLoop(new PolylineCurve(nk), SurfaceHoleNestBridge.SheetSampleVerts);
                    if (hr.Loop != null) { holes.Add(hr.Loop); if (hr.MaxDev > sheetDev) sheetDev = hr.MaxDev; }
                }
            }

            // conservative spacing on distorted charts (parts shouldn't butt
            // closer on the 3D surface than intended)
            double maxStretch = chart.Distortion != null ? Math.Max(1.0, chart.Distortion.MaxEdgeStretch) : 1.0;
            double adjSpacing = spacing * maxStretch;

            var parts = new List<HoleNestPart>();
            var originals = new List<Curve>();
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
                partZOf.Add(pr.PlaneZ);
            }
            if (droppedParts > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{droppedParts} part curve(s) ignored (must be closed and WorldXY-parallel).");
            if (parts.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid part curves."); return null; }

            double maxDev = Math.Max(partDev, sheetDev);
            double engineSpacing = adjSpacing + 2.0 * partDev + sheetDev;

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
            HBox(new PolylineCurve(chart.FlatOuterBoundary));
            HD(chart.FlatMesh != null ? chart.FlatMesh.Vertices.Count : -1);
            HD(chart.SurfaceMesh3D != null ? chart.SurfaceMesh3D.Vertices.Count : -1);
            HD(partCurves.Count);
            foreach (var c in partCurves) HBox(c);

            return new Snapshot
            {
                Chart = chart, Sheet = sr.Loop, SheetZ = sr.PlaneZ, SheetHoles = holes,
                Parts = parts, Originals = originals, PartZOf = partZOf,
                Tolerance = tolerance, EngineSpacing = engineSpacing, MaxDev = maxDev,
                AdjSpacing = adjSpacing, UserSpacing = spacing, MaxStretch = maxStretch,
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
            if (!stale && r.FailedMappingCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{r.FailedMappingCount} packed curve(s) failed 3D mapping — likely cross a UV seam.");

            var out3D = new List<Curve>();
            if (r.PackedCurves3D != null) foreach (var c in r.PackedCurves3D) if (c != null) out3D.Add(c);

            da.SetDataList(0, out3D);
            da.SetDataList(1, r.PackedCurves2D ?? new List<Curve>());
            da.SetDataList(2, r.UnplacedCurves ?? new List<Curve>());
            da.SetData(3, r.FailedMappingCount);
            da.SetData(4, (stale ? "[updating...] " : "") + (r.Report ?? string.Empty));
        }

        private void EmitEmpty(IGH_DataAccess da, string message)
        {
            da.SetDataList(0, new List<Curve>());
            da.SetDataList(1, new List<Curve>());
            da.SetDataList(2, new List<Curve>());
            da.SetData(3, 0);
            da.SetData(4, message);
        }
    }
}
