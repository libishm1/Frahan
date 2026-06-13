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
public sealed class HoleNestComponent : GH_Component
{
    private const int MaxVerts = 200;          // hard cap for explicit polylines (drawn as-is)
    private const int SheetSampleVerts = 192;  // ACCURACY lane: the sheet + sheet-holes are single loops whose
                                               // vertex count barely affects solve cost (only PART verts drive
                                               // the Minkowski NFPs), so they sample high-res — a coarse sheet
                                               // proxy on a large freeform boundary measured 13+ units of
                                               // deviation and the old shared compensation inflated every PART
                                               // by 2x that (ProxyDevComp +27 on the user's S-sheet = 21/200 fill)
    private int _smoothSampleVerts = 48;       // COST lane: parts + part-holes (Resolution input)

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
            "(16..200, default 48). This only controls the collision proxies — the Placed output is " +
            "always the ORIGINAL full-resolution curve, transformed. Raise it when parts must seat into " +
            "tight concave features; solve time grows roughly quadratically. At 48 verts the proxy chord " +
            "error is ~0.3% of part size (~0.5 mm on a 300 mm part) — keep Spacing above that for " +
            "fabrication.", GH_ParamAccess.item, 48);
        pManager[7].Optional = true;
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

    private sealed class Snapshot
    {
        public List<IReadOnlyList<(double X, double Y)>> Sheets;
        public List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> SheetHolesPerSheet;
        public List<double> SheetZ;          // per sheet (placed parts land at their sheet's elevation)
        public List<double> SheetNetArea;    // per sheet: |outer| - sum|holes| (for the aggregate density)
        public List<HoleNestPart> Parts;
        public double UserSpacing, EngineSpacing, MaxDev;
        public int BaseRotations, ContactRotations;
        public List<int> InputIndexOf;
        public List<double> PartZOf;
        public List<Curve> Originals;   // duplicated on the UI thread (owned)
        public List<List<Curve>> OriginalHoles; // per prepared part, duplicated (may be null per part)
        public ulong Hash;
    }

    private sealed class Payload
    {
        public Snapshot Snap;
        public HoleNestResult Res;          // aggregate across sheets
        public List<HoleNestResult> PerSheet; // per-sheet engine results (null for partials)
        public bool Partial;   // progressive snapshot (mid-solve), not a final result
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
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

        // cache hit: same inputs as the last completed solve -> instant emit
        if (_last != null && !_last.Partial && _last.Snap.Hash == snap.Hash && !taskRunning)
        {
            Message = null;
            EmitPayload(da, _last, null);
            return;
        }

        if (taskRunning && taskHash == snap.Hash)
        {
            Message = _progress;
            if (_last != null) EmitPayload(da, _last, _last.Partial ? _progress : "updating...");
            else EmitEmpty(da, "Nesting in the background — canvas stays live; the result pops in when ready.");
            return;
        }

        StartCompute(snap);
        Message = "nesting...";
        if (_last != null) EmitPayload(da, _last, "updating...");
        else EmitEmpty(da, "Nesting in the background — canvas stays live; the result pops in when ready.");
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
            _progress = $"nesting {snap.Parts.Count} parts...";
            _task = Task.Run(() =>
            {
                Payload payload = null; string error = null;
                // progressive steps (the instant-feel pattern): every ~300 ms a
                // caller-space snapshot of the partial layout replaces _last and
                // a re-solve is scheduled, so the nest visibly grows on canvas
                var tick = System.Diagnostics.Stopwatch.StartNew();
                Action<HoleNestResult> onPlacement = partial =>
                {
                    if (token.IsCancellationRequested) return;
                    if (tick.ElapsedMilliseconds < 300) return;
                    tick.Restart();
                    var pp = new Payload { Snap = snap, Res = partial, Partial = true };
                    lock (_gate) { _last = pp; }
                    _progress = $"nesting {partial.PlacedCount}/{snap.Parts.Count}...";
                    try
                    {
                        doc?.ScheduleSolution(10, d =>
                        {
                            if (d?.FindComponent(iguid) is GH_Component c) c.ExpireSolution(true);
                        });
                    }
                    catch { }
                };
                try
                {
                    var perSheet = ContactNfpHoleNester.PackSheets(snap.Sheets, snap.SheetHolesPerSheet,
                        snap.Parts, snap.EngineSpacing, snap.BaseRotations, snap.ContactRotations,
                        onPlacement: onPlacement);
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
                try
                {
                    doc?.ScheduleSolution(10, d =>
                    {
                        if (d?.FindComponent(iguid) is GH_Component c) c.ExpireSolution(true);
                    });
                }
                catch { }
            }, token);
        }
    }

    /// <summary>Inputs -> owned snapshot (conversion + proxy-deviation measurement + hash). UI thread only.</summary>
    private Snapshot BuildSnapshot(IGH_DataAccess da)
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
        int resolution = 48;
        da.GetData(7, ref resolution);
        _smoothSampleVerts = Math.Max(16, Math.Min(MaxVerts, resolution));

        spacing = Math.Max(0.0, spacing);
        baseRotations = Math.Max(1, baseRotations);
        contactRotations = Math.Max(0, contactRotations);

        double partDev = 0.0, sheetDev = 0.0;
        var sheets = new List<IReadOnlyList<(double X, double Y)>>();
        var sheetZs = new List<double>();
        var sheetNet = new List<double>();
        foreach (var sc in sheetCurves)
        {
            var loop = CurveToLoop(sc, "Sheet", out var sz, out var devS, SheetSampleVerts);
            if (loop == null) continue;
            sheets.Add(loop); sheetZs.Add(sz);
            sheetNet.Add(Math.Abs(SignedArea((List<(double X, double Y)>)loop)));
            if (devS > sheetDev) sheetDev = devS;
        }
        if (sheets.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one Sheet must be a valid closed curve in a WorldXY-parallel plane.");
            return null;
        }
        if (sheets.Count < sheetCurves.Count)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{sheetCurves.Count - sheets.Count} sheet curve(s) ignored (must be closed and WorldXY-parallel).");
        _outZ = sheetZs[0];

        // per-sheet holes: PIP-FIRST geometric routing (the house SheetHolesUtil
        // pattern, Bug B-2D-001 fix) — each hole goes to whichever sheet
        // geometrically CONTAINS its centroid; the GH tree path is only the
        // fallback. Flat lists, grafted trees and sparse trees all route
        // correctly; sheets without holes need nothing.
        var validSheetCurves = new List<Curve>();
        foreach (var sc in sheetCurves) if (sc != null && sc.IsClosed) validSheetCurves.Add(sc);
        var routedHoles = SheetHolesUtil.BuildHolesBySheet(
            validSheetCurves, sheetHolesTree, sheets.Count,
            Math.Max(0.01, 1e-6 * (validSheetCurves.Count > 0 ? validSheetCurves[0].GetBoundingBox(false).Diagonal.Length : 1.0)));
        var sheetHolesPerSheet = new List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>();
        for (int si = 0; si < sheets.Count; si++) sheetHolesPerSheet.Add(new List<IReadOnlyList<(double X, double Y)>>());
        var droppedSheetHoles = 0;
        for (int si = 0; si < sheets.Count && si < routedHoles.Count; si++)
        {
            var target = (List<IReadOnlyList<(double X, double Y)>>)sheetHolesPerSheet[si];
            foreach (var hc in routedHoles[si])
            {
                if (hc == null) continue;
                var loop = CurveToLoop(hc, null, out _, out var devH, SheetSampleVerts);
                if (loop != null)
                {
                    target.Add(loop);
                    sheetNet[si] -= Math.Abs(SignedArea((List<(double X, double Y)>)loop));
                    if (devH > sheetDev) sheetDev = devH;
                }
                else droppedSheetHoles++;
            }
        }
        if (droppedSheetHoles > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedSheetHoles} sheet-hole curve(s) ignored (must be closed and WorldXY-parallel).");

        // Part holes: PIP-FIRST geometric routing (mirrors the house
        // SheetHolesUtil pattern) — each hole curve is assigned to the
        // SMALLEST part outline that geometrically contains its centroid;
        // the GH tree path (branch {i} -> Parts[i]) is only the fallback when
        // no part contains it. Flat lists, grafted trees and sparse trees all
        // route correctly; parts without holes need nothing.
        Dictionary<int, List<GH_Curve>> holesByPartIndex = null;
        var unroutedHoles = 0;
        if (partHolesTree != null && !partHolesTree.IsEmpty)
        {
            holesByPartIndex = new Dictionary<int, List<GH_Curve>>();
            for (int b = 0; b < partHolesTree.PathCount; b++)
            {
                var path = partHolesTree.Paths[b];
                var branch = partHolesTree.Branches[b];
                if (branch == null || branch.Count == 0) continue;
                int pathKey = path.Indices.Length > 0 ? path.Indices[path.Indices.Length - 1] : -1;
                foreach (var gc in branch)
                {
                    if (gc == null || gc.Value == null) continue;
                    var bb = gc.Value.GetBoundingBox(false);
                    var centroid = bb.Center;
                    int bestPart = -1; double bestArea = double.MaxValue;
                    for (int pi2 = 0; pi2 < partCurves.Count; pi2++)
                    {
                        var pc = partCurves[pi2];
                        if (pc == null || !pc.IsClosed) continue;
                        var plane = new Plane(new Point3d(0, 0, centroid.Z), Vector3d.ZAxis);
                        if (pc.Contains(centroid, plane, 1e-6) != PointContainment.Inside) continue;
                        var amp = AreaMassProperties.Compute(pc);
                        double area = amp != null ? amp.Area : double.MaxValue;
                        if (area < bestArea) { bestArea = area; bestPart = pi2; }
                    }
                    int key = bestPart >= 0 ? bestPart
                        : (pathKey >= 0 && pathKey < partCurves.Count ? pathKey : -1);
                    if (key < 0) { unroutedHoles++; continue; }
                    if (!holesByPartIndex.TryGetValue(key, out var list))
                    { list = new List<GH_Curve>(); holesByPartIndex[key] = list; }
                    list.Add(gc);
                }
            }
        }
        if (unroutedHoles > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unroutedHoles} part-hole curve(s) sit inside NO part outline and have no usable tree " +
                "path; they were ignored. Draw holes inside their parts (or graft branch {i} -> Parts[i]).");

        var parts = new List<HoleNestPart>();
        var inputIndexOf = new List<int>();
        var partZOf = new List<double>();
        var originals = new List<Curve>();
        var originalHoles = new List<List<Curve>>();
        var droppedParts = 0;
        var droppedPartHoles = 0;
        for (int i = 0; i < partCurves.Count; i++)
        {
            var outer = CurveToLoop(partCurves[i], null, out var partZ, out var devP);
            if (outer == null) { droppedParts++; continue; }
            if (devP > partDev) partDev = devP;

            List<IReadOnlyList<(double X, double Y)>> holes = null;
            List<Curve> holeCurves = null;
            if (holesByPartIndex != null && holesByPartIndex.TryGetValue(i, out var branch))
            {
                foreach (var gc in branch)
                {
                    if (gc == null || gc.Value == null) continue;
                    var hl = CurveToLoop(gc.Value, null, out _, out var devHl);
                    if (hl == null) { droppedPartHoles++; continue; }
                    if (devHl > partDev) partDev = devHl;
                    if (holes == null) holes = new List<IReadOnlyList<(double X, double Y)>>();
                    holes.Add(hl);
                    if (holeCurves == null) holeCurves = new List<Curve>();
                    holeCurves.Add(gc.Value.DuplicateCurve());
                }
            }
            parts.Add(new HoleNestPart { Outer = outer, Holes = holes });
            inputIndexOf.Add(i);
            partZOf.Add(partZ);
            originals.Add(partCurves[i] != null ? partCurves[i].DuplicateCurve() : null);
            originalHoles.Add(holeCurves);
        }
        if (droppedParts > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedParts} part curve(s) ignored (must be closed and planar).");
        if (droppedPartHoles > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{droppedPartHoles} part-hole curve(s) ignored (must be closed and planar).");
        if (parts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid part curves.");
            return null;
        }

        // PROXY-DEVIATION COMPENSATION (overlap fix, 2026-06-12): the solver
        // sees sampled proxies whose chords cut INSIDE the true curve, so
        // touching proxies let the true full-resolution curves cross by up to
        // the sampling deviation on each side. Part-pair clearance needs 2x
        // the worst PART deviation; the sheet term enters ONCE (a part can
        // poke past the true boundary by at most the sheet proxy's own
        // deviation, which the high-res sheet lane keeps tiny). The original
        // shared 2x max-over-ALL-loops formula let a big freeform sheet's
        // deviation inflate every part (+27 units on the reported S-sheet).
        double maxDev = Math.Max(partDev, sheetDev); // reported for transparency
        double engineSpacing = spacing + 2.0 * partDev + sheetDev;

        ulong h = 1469598103934665603UL;
        void HD(double v) { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; }
        void HL(IReadOnlyList<(double X, double Y)> lp) { HD(lp.Count); foreach (var q in lp) { HD(q.X); HD(q.Y); } }
        HD(engineSpacing); HD(baseRotations); HD(contactRotations); HD(_smoothSampleVerts);
        HD(sheets.Count);
        for (int si = 0; si < sheets.Count; si++)
        {
            HL(sheets[si]); HD(sheetZs[si]);
            HD(sheetHolesPerSheet[si].Count);
            foreach (var q in sheetHolesPerSheet[si]) HL(q);
        }
        HD(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            HL(parts[i].Outer); HD(partZOf[i]); HD(inputIndexOf[i]);
            if (parts[i].Holes != null) foreach (var q in parts[i].Holes) HL(q);
        }

        return new Snapshot
        {
            Sheets = sheets, SheetHolesPerSheet = sheetHolesPerSheet,
            SheetZ = sheetZs, SheetNetArea = sheetNet, Parts = parts,
            UserSpacing = spacing, EngineSpacing = engineSpacing, MaxDev = maxDev,
            BaseRotations = baseRotations, ContactRotations = contactRotations,
            InputIndexOf = inputIndexOf, PartZOf = partZOf, Originals = originals,
            OriginalHoles = originalHoles,
            Hash = h,
        };
    }

    private void EmitPayload(IGH_DataAccess da, Payload payload, string staleNote)
    {
        var res = payload.Res;
        var snap = payload.Snap;
        var placedCurves = new List<Curve>(res.Placements.Count);
        var sourceIndices = new List<int>(res.Placements.Count);
        var transforms = new List<Transform>(res.Placements.Count);
        var nestedFlags = new List<bool>(res.Placements.Count);
        var sheetIndices = new List<int>(res.Placements.Count);
        var placedHoles = new GH_Structure<GH_Curve>();
        int placedIdx = 0;
        foreach (var pl in res.Placements)
        {
            int src = pl.PartIndex >= 0 && pl.PartIndex < snap.InputIndexOf.Count ? snap.InputIndexOf[pl.PartIndex] : -1;
            sourceIndices.Add(src);
            // Core placement = rotate about the world Z origin, then translate.
            // The Z term lifts a part from its own input plane to ITS sheet's.
            double sheetZ = pl.SheetIndex >= 0 && pl.SheetIndex < snap.SheetZ.Count ? snap.SheetZ[pl.SheetIndex] : snap.SheetZ[0];
            double dz = pl.PartIndex >= 0 && pl.PartIndex < snap.PartZOf.Count ? sheetZ - snap.PartZOf[pl.PartIndex] : 0.0;
            var xf = Transform.Translation(pl.Tx, pl.Ty, dz) *
                     Transform.Rotation(pl.AngleRad, Vector3d.ZAxis, Point3d.Origin);
            transforms.Add(xf);
            // Placed output = the ORIGINAL curve transformed (full resolution).
            // The sampled loop is only the solver's collision proxy; the
            // deviation-compensated spacing guarantees the true curves never
            // overlap. Fall back to the proxy loop if the duplicate fails.
            Curve placedCurve = null;
            int prep = pl.PartIndex;
            if (prep >= 0 && prep < snap.Originals.Count && snap.Originals[prep] != null)
            {
                placedCurve = snap.Originals[prep].DuplicateCurve();
                if (placedCurve != null && !placedCurve.Transform(xf)) placedCurve = null;
            }
            placedCurves.Add(placedCurve != null ? placedCurve : LoopToCurve(pl.PlacedOuter, sheetZ));
            // the part's own holes travel with it (full resolution, same xf)
            var holePath = new GH_Path(placedIdx);
            placedHoles.EnsurePath(holePath);
            if (prep >= 0 && snap.OriginalHoles != null && prep < snap.OriginalHoles.Count &&
                snap.OriginalHoles[prep] != null)
            {
                foreach (var hc in snap.OriginalHoles[prep])
                {
                    if (hc == null) continue;
                    var dup = hc.DuplicateCurve();
                    if (dup != null && dup.Transform(xf)) placedHoles.Append(new GH_Curve(dup), holePath);
                }
            }
            placedIdx++;
            nestedFlags.Add(pl.NestedInHost);
            sheetIndices.Add(pl.SheetIndex);
        }

        var unplaced = snap.Parts.Count - res.PlacedCount;
        if (staleNote == null && unplaced > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unplaced} part(s) could not be placed.");
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

        da.SetDataList(0, placedCurves);
        da.SetDataList(1, sourceIndices);
        da.SetDataList(2, transforms);
        da.SetDataList(3, nestedFlags);
        da.SetData(4, report);
        da.SetData(5, res.Density);
        da.SetData(6, res.Valid);
        da.SetDataTree(7, placedHoles);
        da.SetDataList(8, sheetIndices);
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

    // ─── Curve <-> loop conversion (mirrors IrregularSheetFillNfpBlf.CurveToLoop) ─
    // TryGetPolyline first, then chord-tolerance sampling, then DivideByCount.
    // Open curves are rejected (warning at the call sites). Loops are emitted
    // CCW because the Core nester expects CCW polygon loops. The nester is 2D:
    // every curve must lie in a WorldXY-parallel plane (tilted curves would
    // silently nest foreshortened projections), and placed output is emitted at
    // the SHEET's elevation (_outZ); the Transform output lifts each part from
    // its own plane.

    private double _outZ;

    private List<(double X, double Y)> CurveToLoop(Curve curve, string label, out double planeZ, out double maxDev, int sampleVerts = 0)
    {
        if (sampleVerts <= 0) sampleVerts = _smoothSampleVerts;
        planeZ = 0.0;
        maxDev = 0.0;
        if (curve == null) return null;
        if (!curve.IsClosed)
        {
            if (label != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, label + " curve is open; it was rejected.");
            return null;
        }

        IList<Point3d> pts = null;
        bool measureDeviation = false;
        if (curve.TryGetPolyline(out var pl))
        {
            pts = pl;
        }
        else
        {
            // UNIFORM-BY-LENGTH sampling (perf, 2026-06-12, measured): the old
            // absolute 1e-3 chord + 2-degree turn sampled smooth NURBS to ~200
            // curvature-adaptive vertices and the canvas solve took 6.3 s. The
            // engine's cost is dominated by Minkowski NFP builds, which scale
            // with vertex count AND suffer from the tiny edges that
            // curvature-adaptive sampling concentrates at high-curvature spots
            // (measured: 53 adaptive verts 4.2 s vs 48 uniform verts 2.8 s on
            // the same shields). Equidistant points at SmoothSampleVerts per
            // closed curve give the same boundary fidelity budget with none of
            // the degenerate edges. The engine's exact verification gate makes
            // placement VALIDITY independent of sampling density — only
            // boundary fidelity (~0.3% of size at 48 verts) is traded, well
            // inside nesting spacing/kerf budgets.
            measureDeviation = true;
            var seg = curve.GetLength() / sampleVerts;
            var div = seg > Rhino.RhinoMath.ZeroTolerance ? curve.DivideEquidistant(seg) : null;
            if (div != null && div.Length >= 3)
            {
                pts = div;
            }
            else
            {
                var divPar = curve.DivideByCount(sampleVerts, false);
                if (divPar == null || divPar.Length < 3) return null;
                var tmp = new List<Point3d>(divPar.Length);
                foreach (var t in divPar) tmp.Add(curve.PointAt(t));
                pts = tmp;
            }
        }

        var n = pts.Count;
        if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < 1e-9) n--;
        if (n < 3) return null;

        // measured proxy deviation (sampled smooth curves only): max distance
        // from each chord midpoint back to the true curve — feeds the
        // deviation-compensated engine spacing so full-resolution outputs
        // can never overlap even though the solver sees coarse proxies
        if (measureDeviation)
        {
            for (var i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                var mid = new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
                if (curve.ClosestPoint(mid, out var tcp))
                {
                    var d = curve.PointAt(tcp).DistanceTo(mid);
                    if (d > maxDev) maxDev = d;
                }
            }
        }

        // WorldXY-parallel plane guard: a tilted curve would project
        // foreshortened and nest silently with distorted geometry.
        double zMin = double.MaxValue, zMax = double.MinValue, span = 0.0;
        Point3d pMin = pts[0], pMax = pts[0];
        for (var i = 0; i < n; i++)
        {
            var p = pts[i];
            if (p.Z < zMin) zMin = p.Z;
            if (p.Z > zMax) zMax = p.Z;
            pMin.X = Math.Min(pMin.X, p.X); pMin.Y = Math.Min(pMin.Y, p.Y);
            pMax.X = Math.Max(pMax.X, p.X); pMax.Y = Math.Max(pMax.Y, p.Y);
        }
        span = Math.Max(pMax.X - pMin.X, pMax.Y - pMin.Y);
        if (zMax - zMin > 1e-6 * (1.0 + span))
        {
            if (label != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    label + " curve is not in a WorldXY-parallel plane; it was rejected.");
            return null;
        }
        planeZ = 0.5 * (zMin + zMax);

        var loop = new List<(double X, double Y)>(Math.Min(n, MaxVerts));
        if (n > MaxVerts)
        {
            var step = (double)n / MaxVerts;
            for (var i = 0; i < MaxVerts; i++)
            {
                var idx = Math.Min(n - 1, (int)(i * step));
                loop.Add((pts[idx].X, pts[idx].Y));
            }
        }
        else
        {
            for (var i = 0; i < n; i++) loop.Add((pts[i].X, pts[i].Y));
        }

        var area = SignedArea(loop);
        if (Math.Abs(area) < 1e-12) return null;
        if (area < 0) loop.Reverse();   // Core nester expects CCW loops
        return loop;
    }

    private static Curve LoopToCurve(IReadOnlyList<(double X, double Y)> loop, double z)
    {
        var pts = new List<Point3d>(loop.Count + 1);
        foreach (var (x, y) in loop) pts.Add(new Point3d(x, y, z));
        pts.Add(pts[0]);   // close the polyline
        return new PolylineCurve(pts);
    }

    private static double SignedArea(List<(double X, double Y)> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var j = (i + 1) % loop.Count;
            a += loop[i].X * loop[j].Y - loop[j].X * loop[i].Y;
        }
        return 0.5 * a;
    }
}
