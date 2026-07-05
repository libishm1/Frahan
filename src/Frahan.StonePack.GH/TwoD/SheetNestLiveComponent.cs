#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Frahan.GH.Attributes;
using Frahan.GH.ScanIngest;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

// =============================================================================
// Sheet Nest (Live). Consolidates the three overlapping 2D-nester components
// (Sheet Nest (Hole-Aware) / HoleNestComponent, Freeform Sheet Nest / FreeNestX,
// Sheet Pack Unified / IrregularSheetFillComponent's async facade) into ONE
// primary-ribbon component that:
//   (a) runs on a TRULY non-blocking background Task, gated by an explicit Run
//       toggle — AsyncScanComponent, not GH_TaskCapableComponent (which only
//       parallelizes data-tree branches; a single big nest on that base still
//       blocks the UI thread);
//   (b) wraps the MOST-EVOLVED solver, Frahan.Packing.TwoD.ContactNfpHoleNester
//       (exact NFP-BLF + part-in-part-hole nesting + contact-adaptive
//       rotations), called exactly as HoleNestComponent calls it;
//   (c) shows a LIVE colour-coded canvas preview of the nested layout (one
//       fan-triangulated mesh per placed part, hue keyed to sheet index), the
//       FloorTileComponent DrawViewportMeshes/ClippingBox pattern; and
//   (d) REUSES HoleNestComponent's GH glue instead of re-implementing it: the
//       curve<->loop conversion, PIP-first hole routing, snapshot building and
//       the result -> output-geometry build all live in HoleNestShared, which
//       this component calls exactly as HoleNestComponent does. The packing
//       MATH is the same unmodified Core call in both components — the only
//       new code here is the AsyncScanComponent shape (Run gate, TryRead /
//       Compute / EmitResult / EmitIdle) and the preview-mesh build.
// =============================================================================

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
    "Frahan ContactNfpHoleNester benchmark study, outputs/2026-06-12/hole_packer_evolution",
    Note = "Frahan-original; head-to-head benchmark protocol and comparators documented in the study")]
[Algorithm("Run-gated background Task async shape (AsyncScanComponent)",
    "Frahan async-canvas pattern, src/Frahan.StonePack.GH/ScanIngest/AsyncScanComponent.cs",
    Note = "Frahan-original; TRUE non-blocking solve — GH_TaskCapableComponent only parallelizes data branches")]
[RelatedComponent("Frahan > 2D Packing > Sheet Nest (Hole-Aware)",
    Reason = "Synchronous sibling with the identical solver and inputs; this component adds a Run gate, background execution and a live colour preview. Consolidates HoleNest / Freeform Sheet Nest / Sheet Pack Unified.",
    ComponentGuid = "D5F10019-8A3C-4D17-B5E2-6C90F2A47D31")]
public sealed class SheetNestLiveComponent
    : AsyncScanComponent<SheetNestLiveComponent.Snapshot, SheetNestLiveComponent.Payload>
{
    public SheetNestLiveComponent()
        : base("Sheet Nest (Live)", "NestLive",
            "Consolidated 2D nester: the same hole-aware exact-NFP bottom-left-fill solver as Sheet Nest " +
            "(Hole-Aware) (Frahan.Packing.TwoD.ContactNfpHoleNester), but running TRULY asynchronously on a " +
            "background Task behind an explicit Run gate so the canvas never blocks even on a large multi-" +
            "sheet instance. Parts are placed by exact no-fit-polygon bottom-left-fill (Burke et al. 2006), " +
            "no-fit/inner-fit regions are built as Clipper2 Minkowski sums/erosions (Bennell & Oliveira 2009), " +
            "smaller parts nest into the holes of larger placed parts via the inner-fit region, and rotations " +
            "are contact-adaptive (edge-alignment angles against the sheet, the latest neighbour, and host " +
            "holes). Draws a LIVE colour-coded preview of the nested layout directly on the canvas (one colour " +
            "per sheet) so you can watch the layout land without wiring a Custom Preview. Consolidates the " +
            "three overlapping 2D nesters (Sheet Nest (Hole-Aware), Freeform Sheet Nest, Sheet Pack Unified) " +
            "into one primary-ribbon component for new work; the synchronous HoleNest sibling remains for " +
            "always-on auto-solve graphs where a Run gate is unwanted.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("2ACEE264-21AC-4095-9E93-10CD96776BB2");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("NoFitPolygon.png");

    // ── live canvas preview: one mesh per placed part, coloured by sheet index ──
    private List<Mesh> _previewMeshes;
    private List<Color> _previewColors;

    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
        if (_previewMeshes != null && _previewColors != null && _previewMeshes.Count == _previewColors.Count)
        {
            for (int i = 0; i < _previewMeshes.Count; i++)
            {
                if (_previewMeshes[i] == null) continue;
                var mat = new Rhino.Display.DisplayMaterial(_previewColors[i]);
                args.Display.DrawMeshShaded(_previewMeshes[i], mat);
            }
        }
        else base.DrawViewportMeshes(args);
    }

    public override BoundingBox ClippingBox
    {
        get
        {
            if (_previewMeshes != null && _previewMeshes.Count > 0)
            {
                var bb = BoundingBox.Empty;
                for (int i = 0; i < _previewMeshes.Count; i++)
                    if (_previewMeshes[i] != null) bb.Union(_previewMeshes[i].GetBoundingBox(false));
                return bb;
            }
            return base.ClippingBox;
        }
    }

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
            "exact ORIGINAL curve, transformed — so there is no output-quality reason to raise it.",
            GH_ParamAccess.item, 24);
        pManager[7].Optional = true;
        pManager.AddIntegerParameter("MultiStart", "MS",
            "Number of deterministic part orders the general engine tries per sheet, keeping the densest " +
            "valid layout (1..4; default 4). Higher values raise irregular-outline density at a near-linear " +
            "wall-time cost and never reduce placements or validity. Jobs over ~120 parts run a single " +
            "pass regardless (the Report notes it) so large nests stay responsive.", GH_ParamAccess.item, 4);
        pManager[8].Optional = true;
        pManager.AddIntegerParameter("Boundary Mode", "BMode",
            "0 = off (pure bottom-left fill). 1 = boundary hug: parts whose outline can seat against the " +
            "sheet boundary are placed rim-first, scored by measured contact length at verified NFP poses " +
            "(rotation-invariant, exact) and spread around the perimeter by arc-interval occupancy. Parts " +
            "that cannot reach the contact threshold fall back to bottom-left, so interior packing stays " +
            "tight.",
            GH_ParamAccess.item, 0);
        pManager[9].Optional = true;
        pManager.AddNumberParameter("Min Boundary Contact", "MBC",
            "Boundary Mode 1 only: minimum rim-contact fraction (of the part perimeter, 0..1) a candidate " +
            "must reach to be seated on the boundary; below it the part places bottom-left. Default 0.25.",
            GH_ParamAccess.item, 0.25);
        pManager[10].Optional = true;
        // Appended LAST (AsyncScanComponent contract): default false so opening
        // a definition never auto-triggers a nest; toggle Run to execute.
        pManager.AddBooleanParameter("Run", "R",
            "Set true to nest (on a background thread). False = idle; nothing is computed, the canvas " +
            "never freezes. Set back to false to cancel an in-flight solve.",
            GH_ParamAccess.item, false);
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
        pManager.AddIntegerParameter("Sheet", "Sh",
            "For each placed curve, the index of the sheet it landed on (greedy overflow order); also the " +
            "live-preview colour key.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Placed count, part-holes filled, density, engine note, elapsed ms, valid flag.",
            GH_ParamAccess.item);
    }

    // ── AsyncScanComponent snapshot / payload (public: AsyncScanComponent's
    // protected TryRead/Compute/EmitResult/EmitIdle signatures require these
    // types be at least as accessible as "protected", per C#'s CS0050/51/52 —
    // sealed does not relax this) ────────────────────────────────────────────
    public sealed class Snapshot
    {
        public HoleNestShared.Snapshot Shared;
        public int BoundaryMode;
        public double MinBoundaryContact;
    }

    public sealed class Payload
    {
        public HoleNestShared.Snapshot Snap;
        public HoleNestResult Res;
        public List<HoleNestResult> PerSheet;
    }

    protected override bool TryRead(IGH_DataAccess da, out bool run, out Snapshot snapshot)
    {
        run = false; snapshot = null;
        da.GetData(11, ref run);
        if (!run) return true;

        // Deep-capture ALL inputs here, on the UI thread — Compute() runs on a
        // background Task and must never touch live Rhino geometry. HoleNestShared
        // duplicates every curve it keeps (Originals/OriginalHoles), so the
        // returned Snapshot is fully owned.
        var sheetCurves = new List<Curve>();
        GH_Structure<GH_Curve> sheetHolesTree = null;
        var partCurves = new List<Curve>();
        GH_Structure<GH_Curve> partHolesTree = null;
        double spacing = 0.0;
        int baseRotations = 4;
        int contactRotations = 6;

        if (!da.GetDataList(0, sheetCurves) || sheetCurves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one Sheet curve is required.");
            return false;
        }
        da.GetDataTree(1, out sheetHolesTree);
        if (!da.GetDataList(2, partCurves) || partCurves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one Part curve is required.");
            return false;
        }
        da.GetDataTree(3, out partHolesTree);
        da.GetData(4, ref spacing);
        da.GetData(5, ref baseRotations);
        da.GetData(6, ref contactRotations);
        int resolution = 24;
        da.GetData(7, ref resolution);
        int multiStart = 4;
        da.GetData(8, ref multiStart);
        int boundaryMode = 0;
        da.GetData(9, ref boundaryMode);
        double minBoundaryContact = 0.25;
        da.GetData(10, ref minBoundaryContact);

        var shared = HoleNestShared.BuildSnapshot(this, sheetCurves, sheetHolesTree, partCurves, partHolesTree,
            spacing, baseRotations, contactRotations, resolution, multiStart);
        if (shared == null) return false; // HoleNestShared already reported the error

        snapshot = new Snapshot { Shared = shared, BoundaryMode = boundaryMode, MinBoundaryContact = minBoundaryContact };
        return true;
    }

    protected override Payload Compute(Snapshot s, CancellationToken token, Action<string> progress)
    {
        var snap = s.Shared;
        progress($"nesting {snap.Parts.Count} parts...");

        // Large-job clamp: multi-start reruns the WHOLE nest K times to keep the
        // densest layout — a fine trade at 30 parts, a 4x wait at 500. Above the
        // threshold run a single deterministic pass; the Report says so.
        const int MultiStartClampAt = 120;
        int effMultiStart = snap.Parts.Count > MultiStartClampAt ? 1 : snap.MultiStart;

        // PROGRESSIVE LIVE PREVIEW: the Core solver reports every placement via
        // onPlacement. Throttled to ~5 Hz, the partial layout is fan-meshed and
        // swapped into the preview fields (atomic reference swap; the UI draw
        // reads whatever pair is current), then a viewport redraw is requested on
        // the UI thread — parts appear one by one while the canvas stays live.
        // No GH solution is triggered (that would restart the state machine).
        int lastReported = -1;
        var previewTick = System.Diagnostics.Stopwatch.StartNew();
        Action<HoleNestResult> onPlacement = partial =>
        {
            if (token.IsCancellationRequested) return;
            if (partial.PlacedCount == lastReported) return;
            lastReported = partial.PlacedCount;
            progress($"nesting {partial.PlacedCount}/{snap.Parts.Count}...");

            if (previewTick.ElapsedMilliseconds < 200) return;
            previewTick.Restart();
            try
            {
                var meshes = new List<Mesh>(partial.Placements.Count);
                var colors = new List<Color>(partial.Placements.Count);
                foreach (var pl in partial.Placements)
                {
                    var m = PlanarFanMesh(pl.PlacedOuter);
                    if (m == null) continue;
                    meshes.Add(m);
                    colors.Add(Color.FromArgb(235, 170, 60)); // in-progress amber
                }
                _previewMeshes = meshes;   // reference swap: safe for the draw thread
                _previewColors = colors;
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    try { Rhino.RhinoDoc.ActiveDoc?.Views.Redraw(); } catch { }
                }));
            }
            catch { /* preview must never kill the solve */ }
        };

        // Same Core call as HoleNestComponent.StartCompute — the shared,
        // unmodified solver; only the async shape around it differs. Boundary
        // Mode 1 adds rim-hug placement (measured contact + arc occupancy) in
        // the Core solver; 0 is byte-identical bottom-left.
        var perSheet = ContactNfpHoleNester.PackSheets(snap.Sheets, snap.SheetHolesPerSheet,
            snap.Parts, snap.EngineSpacing, snap.BaseRotations, snap.ContactRotations,
            onPlacement: onPlacement, multiStartOrders: effMultiStart,
            boundaryMode: s.BoundaryMode, minBoundaryContact: s.MinBoundaryContact);
        token.ThrowIfCancellationRequested();

        var agg = new HoleNestResult { Note = "" };
        double usedArea = 0, netArea = 0;
        var notes = new List<string>();
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
        if (effMultiStart != snap.MultiStart)
            notes.Add($"large job ({snap.Parts.Count} parts): multi-start clamped to 1 pass");
        agg.Note = string.Join(" ; ", notes);

        return new Payload { Snap = snap, Res = agg, PerSheet = perSheet };
    }

    protected override void EmitResult(IGH_DataAccess da, Payload payload)
    {
        var res = payload.Res;
        var snap = payload.Snap;
        var outputs = HoleNestShared.BuildOutputs(res, snap);

        if (outputs.Unplaced > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{outputs.Unplaced} part(s) could not be placed.");
        if (!res.Valid)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Layout failed independent boolean validation: " + res.Note);

        var note = string.IsNullOrEmpty(res.Note) ? "ok" : res.Note;
        var perSheetNote = "";
        if (payload.PerSheet != null && payload.PerSheet.Count > 1)
        {
            var counts = new List<string>();
            for (int si = 0; si < payload.PerSheet.Count; si++)
                counts.Add($"s{si}:{payload.PerSheet[si].Placements.Count}");
            perSheetNote = $", Sheets: [{string.Join(" ", counts)}]";
        }
        var report =
            $"Sheet Nest (Live) — Placed: {res.PlacedCount}/{snap.Parts.Count}{perSheetNote}, " +
            $"PartHolesFilled: {res.PartHolesFilled}, Density: {res.Density:0.000}, " +
            $"Valid: {res.Valid}, Elapsed: {res.ElapsedMs:0.0} ms, Note: {note}";

        da.SetDataList(0, outputs.Placed);
        da.SetDataList(1, outputs.Source);
        da.SetDataList(2, outputs.Transform);
        da.SetDataList(3, outputs.Nested);
        da.SetDataList(4, outputs.Sheet);
        da.SetData(5, report);

        BuildPreview(outputs.Placed, outputs.Sheet);
    }

    protected override void EmitIdle(IGH_DataAccess da, string message)
    {
        _previewMeshes = null;
        _previewColors = null;
        da.SetDataList(0, new List<Curve>());
        da.SetDataList(1, new List<int>());
        da.SetDataList(2, new List<Transform>());
        da.SetDataList(3, new List<bool>());
        da.SetDataList(4, new List<int>());
        da.SetData(5, message);
    }

    // ── live preview: fan-triangulate each placed curve, colour by sheet index ──
    private void BuildPreview(List<Curve> placed, List<int> sheetOf)
    {
        var meshes = new List<Mesh>(placed.Count);
        var colors = new List<Color>(placed.Count);
        int sheetCount = 1;
        for (int i = 0; i < sheetOf.Count; i++) sheetCount = Math.Max(sheetCount, sheetOf[i] + 1);

        for (int i = 0; i < placed.Count; i++)
        {
            var mesh = PlanarFanMesh(placed[i]);
            meshes.Add(mesh);
            colors.Add(SheetHue(i < sheetOf.Count ? sheetOf[i] : 0, sheetCount));
        }
        _previewMeshes = meshes;
        _previewColors = colors;
    }

    /// <summary>Fan-triangulate a raw XY loop (engine PlacedOuter) — the progressive-preview fast path.</summary>
    private static Mesh PlanarFanMesh(IReadOnlyList<(double X, double Y)> loop)
    {
        if (loop == null) return null;
        int n = loop.Count;
        if (n > 1 && Math.Abs(loop[0].X - loop[n - 1].X) < 1e-12 && Math.Abs(loop[0].Y - loop[n - 1].Y) < 1e-12) n--;
        if (n < 3) return null;
        var mesh = new Mesh();
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++) { cx += loop[i].X; cy += loop[i].Y; }
        cx /= n; cy /= n;
        for (int i = 0; i < n; i++) mesh.Vertices.Add(loop[i].X, loop[i].Y, 0.0);
        int centerIdx = mesh.Vertices.Add(cx, cy, 0.0);
        for (int i = 0; i < n; i++) mesh.Faces.AddFace(i, (i + 1) % n, centerIdx);
        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    /// <summary>Fan-triangulate a closed planar curve (via its rendering polyline) into a single-face-ring mesh.</summary>
    private static Mesh PlanarFanMesh(Curve curve)
    {
        if (curve == null) return null;
        Polyline pl;
        if (!curve.TryGetPolyline(out pl))
        {
            var polyCurve = curve.ToPolyline(0, 0, 0.0, 0.0, 0.0, 0.01, 0.0, 0.0, true);
            if (polyCurve == null || !polyCurve.TryGetPolyline(out pl)) return null;
        }
        int n = pl.Count;
        if (n > 1 && pl[0].DistanceTo(pl[n - 1]) < 1e-9) n--;
        if (n < 3) return null;

        var mesh = new Mesh();
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++) { cx += pl[i].X; cy += pl[i].Y; cz += pl[i].Z; }
        cx /= n; cy /= n; cz /= n;
        for (int i = 0; i < n; i++) mesh.Vertices.Add(pl[i]);
        int centerIdx = mesh.Vertices.Add(cx, cy, cz);
        for (int i = 0; i < n; i++) mesh.Faces.AddFace(i, (i + 1) % n, centerIdx);
        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    /// <summary>Distinct hue per sheet index (HSV, full saturation/value) for the live preview.</summary>
    private static Color SheetHue(int sheetIndex, int sheetCount)
    {
        double hue = sheetCount > 0 ? (sheetIndex % Math.Max(1, sheetCount)) / (double)Math.Max(1, sheetCount) : 0.0;
        return HsvToColor(hue, 0.55, 0.95);
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = (h % 1.0 + 1.0) % 1.0;
        double r, g, b;
        int hi = (int)(h * 6.0) % 6;
        double f = h * 6.0 - Math.Floor(h * 6.0);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        switch (hi)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return Color.FromArgb(255, (int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
