#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Frahan.GH.Attributes;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Sheet Nest (Hole-Aware). Canvas wrapper for the Core ContactNfpHoleNester
/// (Frahan.Packing.TwoD): a deterministic, exact-NFP, hole-aware 2D nester on
/// the Clipper2 primitive layer. Evolves the Freeform Sheet Nest (Exact NFP)
/// sibling with (1) part-in-part-hole nesting via inner-fit regions and
/// (2) contact-adaptive rotations (edge-alignment angles), both validated in
/// outputs/2026-06-12/hole_packer_evolution. Synchronous component: the solver
/// runs in milliseconds on bench instances, per the repo async-vs-sync rule.
/// </summary>
[Algorithm("Exact No-Fit-Polygon Bottom-Left-Fill with part-in-part-hole nesting",
    "Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). \"A New Bottom-Left-Fill Heuristic Algorithm for the Two-Dimensional Irregular Packing Problem.\" Operations Research 54(3):587-601",
    Doi = "10.1287/opre.1060.0293")]
[Algorithm("No-fit-polygon / inner-fit-polygon via Minkowski sum",
    "Bennell, J.A. & Oliveira, J.F. (2009). \"A tutorial in irregular shape packing problems.\" J. Oper. Res. Soc. 60(S1):S93-S105",
    Doi = "10.1057/jors.2008.169",
    WikiPath = "wiki/index/references.md#BennellOliveira2008")]
[Algorithm("Clipper2 polygon Minkowski sum + Boolean back-end",
    "Johnson, A. Clipper2 (BSL-1.0); Minkowski sum + NonZero Boolean operations")]
[Algorithm("Contact-adaptive rotations (edge-alignment angle set) + holes-first host nesting",
    "Frahan ContactNfpHoleNester evolution study, outputs/2026-06-12/hole_packer_evolution",
    Note = "Frahan-original; head-to-head benchmark protocol and comparators documented in the study")]
[RelatedComponent("Frahan > 2D Packing > Freeform Sheet Nest (Exact NFP)",
    Reason = "Multi-sheet exact NFP-BLF production sibling without part-in-part-hole nesting; use it when parts have no usable holes.",
    ComponentGuid = "2d351646-2cb0-402a-bbd8-3950b5bb1fbc")]
public sealed class HoleNestComponent : FrahanComponentBase
{
    // Curve-sampling constants (MaxVerts, SheetSampleVerts) and the smooth-
    // sample-verts COST lane (Resolution input) now live in HoleNestShared —
    // extracted 2026-07-05 so SheetNestLiveComponent (Run-gated async sibling)
    // shares the exact same conversion/build code instead of re-implementing it.

    public HoleNestComponent()
        : base("Sheet Nest (Hole-Aware)", "HoleNest",
            "Deterministic hole-aware 2D nester: parts are placed on a sheet with defects (holes) by " +
            "exact no-fit-polygon bottom-left-fill, and smaller parts are nested INSIDE the holes of " +
            "larger placed parts via the inner-fit region. No-fit and inner-fit polygons are built " +
            "exactly as Clipper2 Minkowski sums/erosions (Bennell & Oliveira 2009) and placement is " +
            "bottom-left-fill (Burke et al. 2006), so layouts are 0-overlap by construction. " +
            "Rotations are contact-adaptive: the uniform base set is extended with edge-alignment " +
            "angles against the sheet, the latest neighbour, and host holes so parts seat flush. " +
            "Returns valid hole-aware layouts where hole-blind nesters fail; an exact rectangle " +
            "shelf fast-path accelerates all-rectangle instances. Deterministic: the same inputs " +
            "always reproduce the same cut layout.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10019-8A3C-4D17-B5E2-6C90F2A47D31");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Frahan.GH.IconProvider.Load("NoFitPolygon.png");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Sheets", "S",
            "Closed planar sheet boundary curve(s). Multiple sheets nest by greedy overflow: sheet 0 " +
            "fills first, unplaced parts carry to sheet 1, and so on. Sheets stay at their drawn " +
            "positions.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "SH",
            "Closed sheet defect/hole curves (flat list or tree). Each hole is routed to whichever sheet " +
            "geometrically CONTAINS it (tree path {s} is only the fallback) — no tree matching or " +
            "grafting required; sheets without holes need nothing.",
            GH_ParamAccess.tree);
        pManager[1].Optional = true;
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar part outline curves to nest.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Part Holes", "PH",
            "Part hole curves (flat list or tree). Each hole is routed to the SMALLEST part outline that " +
            "geometrically CONTAINS it (tree path {i} -> Parts[i] is only the fallback) — no tree " +
            "matching or grafting required; parts without holes need nothing. Parts with holes are " +
            "placed first as hosts, then smaller parts nest into their holes via the inner-fit region.",
            GH_ParamAccess.tree);
        pManager[3].Optional = true;
        pManager.AddNumberParameter("Spacing", "Gap",
            "Clearance between parts and boundaries.", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("BaseRotations", "BR",
            "Uniform base rotation count (4 = 0/90/180/270 degrees).", GH_ParamAccess.item, 4);
        pManager.AddIntegerParameter("ContactRotations", "CR",
            "Longest-edge count per polygon used to build contact (edge-alignment) rotation angles.",
            GH_ParamAccess.item, 6);
        pManager.AddIntegerParameter("Resolution", "Res",
            "SOLVER sampling resolution for smooth curves: uniform-by-length vertices per closed curve " +
            "(16..200, default 24). This ONLY sets the collision proxy — the Placed output is always the " +
            "exact ORIGINAL curve, transformed — so there is no output-quality reason to raise it. Solve " +
            "time grows ~QUADRATICALLY with this while packing density is nearly flat (benchmark: 48 verts " +
            "was ~10-20x slower than 24 for <2% density gain). Raise it ONLY when small parts must seat " +
            "into tight CONCAVE notches; otherwise leave it low for fast nesting.", GH_ParamAccess.item, 24);
        pManager[7].Optional = true;
        pManager.AddIntegerParameter("MultiStart", "MS",
            "Number of deterministic part orders the general engine tries per sheet, keeping the densest " +
            "valid layout (1..4; default 4). Orders: area / max-dimension / width / height, all descending. " +
            "1 = the original single largest-first pass. Higher values raise irregular-outline density at a " +
            "near-linear wall-time cost (4 orders is ~4x the solve time of 1) and never reduce placements or " +
            "validity. The exact rectangle fast-path ignores this (it is already optimal). Output stays " +
            "deterministic: identical inputs always reproduce the same layout.", GH_ParamAccess.item, 4);
        pManager[8].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Placed", "C",
            "The ORIGINAL part curves at full resolution, moved to their placed positions (placement " +
            "order). The solver works on coarse collision proxies internally; output geometry stays " +
            "exact for fabrication.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source", "I",
            "For each placed curve, the index of the source curve in the Parts input (labeling/etching map).",
            GH_ParamAccess.list);
        pManager.AddTransformParameter("Transform", "X",
            "For each placed curve, the rigid placement transform (rotation about the world Z origin, " +
            "then translation). Apply it to the original part curve, its holes, or any decoration.",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter("Nested", "N",
            "True where the corresponding placed part was nested into a host part's hole.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Placed count, part-holes filled, density, engine note, elapsed ms, valid flag.",
            GH_ParamAccess.item);
        pManager.AddNumberParameter("Density", "D",
            "Placed part material area / net sheet area (sheet minus its holes).", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Valid", "V",
            "True when the final layout passed the independent boolean (path-free) validation.",
            GH_ParamAccess.item);
        pManager.AddCurveParameter("Placed Holes", "CH",
            "The placed parts' own hole curves at full resolution, moved with their parts: branch path " +
            "{i} holds the hole curves of Placed[i]. Subtract them from Placed[i] for the true cut profile.",
            GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Sheet", "Sh",
            "For each placed curve, the index of the sheet it landed on (greedy overflow order).",
            GH_ParamAccess.list);
    }

    // ─── async solve (the Geogram-Remesh / Kintsugi house pattern, adapted) ──
    // The solver runs on a background Task so the canvas NEVER freezes; while a
    // new layout computes, the PREVIOUS layout stays visible ("updating...") and
    // the finished result pops in via ScheduleSolution — the instant-feel
    // pattern. Unlike the scan-ingest nodes there is no Run gate (a mid-graph
    // nester must auto-solve; async alone removes the freeze risk), and an
    // input-hash cache prevents redundant recomputes on benign re-expires.
    private readonly object _gate = new object();
    private Task _task;
    private CancellationTokenSource _cts;
    private ulong _taskHash;
    private volatile string _progress = "";
    private Payload _readyPayload;
    private string _readyError;
    private bool _hasReady;
    private Payload _last;
    private volatile bool _selfTrigger;  // true when a pending re-solve is my OWN (progress/completion), not a GH input change

    // Snapshot moved to HoleNestShared.Snapshot (shared with SheetNestLiveComponent).

    private sealed class Payload
    {
        public HoleNestShared.Snapshot Snap;
        public HoleNestResult Res;          // aggregate across sheets
        public List<HoleNestResult> PerSheet; // per-sheet engine results (null for partials)
        public bool Partial;   // progressive snapshot (mid-solve), not a final result
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        // ── SELF-TRIGGERED re-solve (2026-06-13) ────────────────────────────
        // This re-solve was scheduled by MY OWN progress/completion callback,
        // not by a GH input change. Just emit the latest result and return —
        // crucially WITHOUT re-running BuildSnapshot (which re-does the PIP
        // routing + sampling + deviation and is expensive). Running BuildSnapshot
        // on every progress tick was what starved the background solve (~100x
        // slowdown). A bool flag is deterministic (no geometry-hash noise), so
        // it cannot mis-fire the way the old hash did.
        if (_selfTrigger)
        {
            _selfTrigger = false;
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
            if (_last != null)
            { Message = srunning ? _progress : null; EmitPayload(da, _last, _last.Partial ? _progress : null); }
            else EmitEmpty(da, "Nesting in the background — canvas stays live; the result pops in when ready.");
            return;
        }

        // ── REAL GH expiration (inputs changed): start/cache as appropriate ──
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

        // a task is already running for these exact inputs (stable bbox hash):
        // don't restart, just show progress
        if (taskRunning && taskHash == snap.Hash)
        {
            Message = _progress;
            if (_last != null) EmitPayload(da, _last, _last.Partial ? _progress : "updating...");
            else EmitEmpty(da, "Nesting in the background — canvas stays live; the result pops in when ready.");
            return;
        }

        // cache hit: same inputs as the last completed solve -> instant emit
        if (_last != null && !_last.Partial && _last.Snap.Hash == snap.Hash && !taskRunning)
        {
            Message = null;
            EmitPayload(da, _last, null);
            return;
        }

        // genuinely new inputs -> (re)start the background solve
        StartCompute(snap);
        Message = "nesting...";
        if (_last != null) EmitPayload(da, _last, "updating...");
        else EmitEmpty(da, "Nesting in the background — canvas stays live; the result pops in when ready.");
    }

    private void StartCompute(HoleNestShared.Snapshot snap)
    {
        var doc = OnPingDocument();
        var iguid = InstanceGuid;
        lock (_gate)
        {
            try { _cts?.Cancel(); } catch { }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _taskHash = snap.Hash;
            _progress = $"nesting {snap.Parts.Count} parts...";
            _task = Task.Run(() =>
            {
                Payload payload = null; string error = null;
                // PROGRESSIVE display: every ~300 ms publish the partial layout
                // and self-trigger a re-solve. The re-solve takes the SELF-TRIGGER
                // fast path (emit only, NO BuildSnapshot), so it is cheap and does
                // not starve this solve — the fix for the earlier ~100x slowdown.
                var tick = System.Diagnostics.Stopwatch.StartNew();
                Action<HoleNestResult> onPlacement = partial =>
                {
                    if (token.IsCancellationRequested) return;
                    if (tick.ElapsedMilliseconds < 300) return;
                    tick.Restart();
                    var pp = new Payload { Snap = snap, Res = partial, Partial = true };
                    lock (_gate) { _last = pp; }
                    _progress = $"nesting {partial.PlacedCount}/{snap.Parts.Count}...";
                    _selfTrigger = true;
                    AsyncResolve.Kick(doc, iguid);
                };
                try
                {
                    var perSheet = ContactNfpHoleNester.PackSheets(snap.Sheets, snap.SheetHolesPerSheet,
                        snap.Parts, snap.EngineSpacing, snap.BaseRotations, snap.ContactRotations,
                        onPlacement: onPlacement, multiStartOrders: snap.MultiStart);
                    var agg = new HoleNestResult { Note = "" };
                    double usedArea = 0, netArea = 0;
                    var notes = new System.Collections.Generic.List<string>();
                    bool allValid = true;
                    for (int si = 0; si < perSheet.Count; si++)
                    {
                        var r = perSheet[si];
                        agg.Placements.AddRange(r.Placements);
                        agg.PartHolesFilled += r.PartHolesFilled;
                        agg.ElapsedMs += r.ElapsedMs;
                        usedArea += r.UsedArea;
                        if (si < snap.SheetNetArea.Count) netArea += Math.Max(1e-9, snap.SheetNetArea[si]);
                        if (r.Placements.Count > 0 || !r.Note.StartsWith("empty"))
                            allValid &= r.Valid;
                        if (!string.IsNullOrEmpty(r.Note) && !notes.Contains(r.Note)) notes.Add(r.Note);
                    }
                    agg.PlacedCount = agg.Placements.Count;
                    agg.UsedArea = usedArea;
                    agg.Density = netArea > 1e-9 ? usedArea / netArea : 0.0;
                    agg.Valid = allValid;
                    agg.Note = string.Join(" ; ", notes);
                    payload = new Payload { Snap = snap, Res = agg, PerSheet = perSheet };
                }
                catch (Exception ex) { error = "Hole-aware nesting failed: " + ex.Message; }
                bool cancelled = token.IsCancellationRequested;
                if (cancelled) return; // stale job: discard silently
                lock (_gate) { _readyPayload = payload; _readyError = error; _hasReady = true; }
                _selfTrigger = true;   // the completion re-solve is mine: emit, don't restart
                AsyncResolve.Kick(doc, iguid); // guarded delivery: schedule, then UI-thread fallback
            }, token);
        }
    }

    /// <summary>
    /// Read raw GH inputs (this component's own parameter indices) and delegate
    /// conversion + PIP routing + hashing to HoleNestShared.BuildSnapshot. UI
    /// thread only.
    /// </summary>
    private HoleNestShared.Snapshot BuildSnapshot(IGH_DataAccess da)
    {
        var sheetCurves = new List<Curve>();
        GH_Structure<GH_Curve> sheetHolesTree = null;
        var partCurves = new List<Curve>();
        GH_Structure<GH_Curve> partHolesTree = null;
        double spacing = 0.0;
        int baseRotations = 4;
        int contactRotations = 6;

        if (!da.GetDataList(0, sheetCurves) || sheetCurves.Count == 0) return null;
        da.GetDataTree(1, out sheetHolesTree);
        if (!da.GetDataList(2, partCurves)) return null;
        da.GetDataTree(3, out partHolesTree);
        da.GetData(4, ref spacing);
        da.GetData(5, ref baseRotations);
        da.GetData(6, ref contactRotations);
        int resolution = 24;
        da.GetData(7, ref resolution);
        int multiStart = 4;
        da.GetData(8, ref multiStart);

        return HoleNestShared.BuildSnapshot(this, sheetCurves, sheetHolesTree, partCurves, partHolesTree,
            spacing, baseRotations, contactRotations, resolution, multiStart);
    }

    private void EmitPayload(IGH_DataAccess da, Payload payload, string staleNote)
    {
        var res = payload.Res;
        var snap = payload.Snap;
        var outputs = HoleNestShared.BuildOutputs(res, snap);

        if (staleNote == null && outputs.Unplaced > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{outputs.Unplaced} part(s) could not be placed.");
        if (staleNote == null && !res.Valid)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Layout failed independent boolean validation: " + res.Note);

        var note = string.IsNullOrEmpty(res.Note) ? "ok" : res.Note;
        var devNote = snap.MaxDev > 0 ? $", ProxyDevComp: +{2.0 * snap.MaxDev:0.####}" : "";
        var stale = staleNote != null ? $" [{staleNote}]" : "";
        var perSheetNote = "";
        if (payload.PerSheet != null && payload.PerSheet.Count > 1)
        {
            var counts = new List<string>();
            for (int si = 0; si < payload.PerSheet.Count; si++)
                counts.Add($"s{si}:{payload.PerSheet[si].Placements.Count}");
            perSheetNote = $", Sheets: [{string.Join(" ", counts)}]";
        }
        var report =
            $"Sheet Nest (Hole-Aware){stale} — Placed: {res.PlacedCount}/{snap.Parts.Count}{perSheetNote}, " +
            $"PartHolesFilled: {res.PartHolesFilled}, Density: {res.Density:0.000}, " +
            $"Valid: {res.Valid}, Elapsed: {res.ElapsedMs:0.0} ms, Note: {note}{devNote}";

        da.SetDataList(0, outputs.Placed);
        da.SetDataList(1, outputs.Source);
        da.SetDataList(2, outputs.Transform);
        da.SetDataList(3, outputs.Nested);
        da.SetData(4, report);
        da.SetData(5, res.Density);
        da.SetData(6, res.Valid);
        da.SetDataTree(7, outputs.PlacedHoles);
        da.SetDataList(8, outputs.Sheet);
    }

    private void EmitEmpty(IGH_DataAccess da, string message)
    {
        da.SetDataList(0, new List<Curve>());
        da.SetDataList(1, new List<int>());
        da.SetDataList(2, new List<Transform>());
        da.SetDataList(3, new List<bool>());
        da.SetData(4, message);
        da.SetData(5, 0.0);
        da.SetData(6, false);
        da.SetDataTree(7, new GH_Structure<GH_Curve>());
        da.SetDataList(8, new List<int>());
    }

    // Curve<->loop conversion (CurveToLoop / LoopToCurve / SignedArea) moved to
    // HoleNestShared — shared with SheetNestLiveComponent (2026-07-05).
}
