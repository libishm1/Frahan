#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Masonry.Geometry;
using Clipper2Lib;

namespace Frahan.Packing.TwoD
{
    // ── CNH — Contact-NFP Hole nester (evolution of FreeNestX, 2026-06-12) ──
    //
    // A deterministic, exact-NFP, hole-aware 2D nester built entirely on the
    // Rhino-free Clipper2 primitive layer so it can be benchmarked head-to-head
    // without Grasshopper. It evolves the shipped IrregularSheetFillNfpBlf with
    // two capabilities it lacks, both validated against the Sparrow/native
    // hole-packing study (see outputs/2026-06-12/hole_packer_evolution):
    //
    //   1. CONTACT-ADAPTIVE ROTATIONS. Instead of a fixed user rotation list,
    //      each part is tried at {0,90,180,270} PLUS edge-alignment angles
    //      a(host_edge) - a(part_edge) over the longest edges, so a part can
    //      seat flush against a wall/neighbour/hole edge.
    //   2. PART-IN-PART-HOLE nesting (holes-first). A small part is nested into
    //      a host part's hole via the inner-fit region IFP(part, hole); the
    //      reduced outer set is then placed by exact NFP bottom-left-fill with
    //      sheet-holes as obstacles.
    //
    // Geometry, all via Clipper2 (exact, integer-snapped):
    //   NFP(part, obstacle) = obstacle (+) reflect(part)         [no-fit region]
    //   IFP(part, container) = container (-) part  (Minkowski erosion)
    //   feasible(part) = IFP(part, sheet) \ U_j NFP(part, placed_j)
    //                                    \ U_l NFP(part, sheetHole_l)
    // Placement is the bottom-left vertex of the feasible region. 0-overlap by
    // construction; every accepted move is re-checked by boolean area.
    //
    // ── FINAL VERIFICATION GATE (2026-06-12, adversarial-fuzz depth fix) ──
    // Area-relative gates alone pass NEEDLE overlaps: tiny intersection AREA
    // (1e-5..1e-4 of part area, under VerifyRelTol) but penetration DEPTH up
    // to ~0.2 caller units, sourced from (i) RDP-simplified NFP inputs
    // (NfpSimplifyTol = 2e-3 x bbox-diag carves the no-fit region inward),
    // (ii) the hull-based IFP being anti-conservative on CONCAVE sheets and
    // holes, and (iii) per-hole area tolerances letting split overlaps
    // through. The RDP/IFP constructions are PRUNING devices only; final
    // feasibility is certified by a compound gate on the TRUE geometry:
    //
    //   1. AREA  — boolean intersection, summed across sheet-holes, must stay
    //      under VerifyRelTol(1e-4) x part-area (gross-overlap net).
    //   2. DEPTH — exact distance-based penetration measured like the
    //      independent verification protocol: probes (vertices + edge
    //      midpoints + area centroid) of each loop against the other loop's
    //      boundary, plus proper edge-crossing perpendicular depth. A
    //      candidate is rejected when depth > eps_depth.
    //   3. MICRO-RETREAT — a rejected candidate is nudged ONCE along the
    //      measured penetration vector by depth + one snap-grid cell and
    //      re-verified; this rescues contact-tight candidates that only
    //      violate through NFP simplification, so placements are not starved.
    //
    // Tolerance budget (RELATIVE to geometry scale; mm and m callers both
    // exist). All engine math runs in scaled space (Scale = 1000) where the
    // Clipper PathD lane snaps to decimal precision 2:
    //
    //   SnapGrid  = 0.01 scaled            (= 1e-5 caller units)
    //   eps_depth = max(floor, 1e-6 * L),  L = sqrt(part area)
    //   floor     = 2 x SnapGrid           (= 2e-5 caller units)
    //
    // This instantiates the project budget eps_geo = max(floor, 1e-3*L) at
    // the verification-protocol level: the relative coefficient is 1e-6
    // (matching the independent checker's relative tolerance) and the floor
    // is the snap-grid resolution limit, far inside any fabrication budget.
    // Residual risk after the gate: penetrations inside the snap band
    // (<= floor + grid rounding of candidate vertices, i.e. <= ~2e-5 caller
    // units); deeper penetrations cannot be accepted on any path because
    // Validate() applies the same compound gate path-independently.
    public sealed class HoleNestPart
    {
        public IReadOnlyList<(double X, double Y)> Outer;
        public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Holes; // optional part-holes
    }

    public sealed class HoleNestPlacement
    {
        public int PartIndex;
        public double AngleRad;
        public double Tx, Ty;
        public bool NestedInHost;
        public int HostIndex = -1, HostHole = -1;
        public int SheetIndex;          // multi-sheet: which sheet this placement landed on (0 single-sheet)
        public IReadOnlyList<(double X, double Y)> PlacedOuter; // transformed, for preview/validation
    }

    public sealed class HoleNestResult
    {
        public List<HoleNestPlacement> Placements = new List<HoleNestPlacement>();
        public bool Valid;
        public int PlacedCount;
        public int PartHolesFilled;
        public double UsedArea;        // sum of placed part material area
        public double Density;         // UsedArea / sheet-net area
        public double ElapsedMs;
        public string Note = "";
    }

    public static class ContactNfpHoleNester
    {
        private const double Eps = 1e-6;

        public static HoleNestResult Pack(
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            IReadOnlyList<HoleNestPart> parts,
            double spacing = 0.0,
            int baseRotationCount = 4,
            int contactRotations = 6,
            bool enableRectFastPath = true, // append-only (2026-06-12): opt-out for the exact rect shelf fast-path
            Action<HoleNestResult> onPlacement = null) // append-only: progressive snapshot after each placement (caller-space copies)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new HoleNestResult();
            sheetHoles = sheetHoles ?? Array.Empty<IReadOnlyList<(double X, double Y)>>();

            // ---- conditioning (2026-06-12 canvas-overlap fix) ----------------
            // (a) SCALE everything up: Clipper's floating ops snap to a fixed
            //     decimal precision, so small-unit geometry (metres) builds
            //     sliver-noisy NFPs. All engine math runs in scaled space.
            // (b) NORMALIZE each part to its own bbox-min: GH curves arrive at
            //     arbitrary world positions, and a part whose coordinate origin
            //     sits far from the part itself breaks the NFP reach cull
            //     (translation space vs world space drift apart) and degrades
            //     rotation conditioning. Outputs are mapped back below.
            sheetOuter = ScaleLoop(sheetOuter, Scale);
            var scaledHoles = new List<IReadOnlyList<(double X, double Y)>>(sheetHoles.Count);
            foreach (var q in sheetHoles) scaledHoles.Add(ScaleLoop(q, Scale));
            sheetHoles = scaledHoles;
            spacing *= Scale;

            var normParts = new List<HoleNestPart>(parts.Count);
            var normOffset = new List<(double BX, double BY)>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                var src = parts[i];
                var so = ScaleLoop(src.Outer, Scale);
                BBox(so, out double bx, out double by, out _, out _);
                var no = Translate(so, -bx, -by);
                List<IReadOnlyList<(double X, double Y)>> nh = null;
                if (src.Holes != null && src.Holes.Count > 0)
                {
                    nh = new List<IReadOnlyList<(double X, double Y)>>(src.Holes.Count);
                    foreach (var hLoop in src.Holes)
                        nh.Add(Translate(ScaleLoop(hLoop, Scale), -bx, -by));
                }
                normParts.Add(new HoleNestPart { Outer = no, Holes = nh });
                normOffset.Add((bx, by));
            }
            parts = normParts;

            double sheetArea = Math.Abs(SignedArea(sheetOuter));
            double sheetHoleArea = sheetHoles.Sum(h => Math.Abs(SignedArea(h)));
            double sheetNet = Math.Max(Eps, sheetArea - sheetHoleArea);

            // largest-first: big parts anchor the layout, smalls fill holes
            var order = Enumerable.Range(0, parts.Count)
                .OrderByDescending(i => Math.Abs(SignedArea(parts[i].Outer))).ToList();

            // engine dispatch: the rect shelf fast-path runs ONLY when the whole
            // instance is axis-aligned rectangles and spacing == 0; anything
            // else falls through to the general exact-NFP engine untouched.
            // COMPLETENESS FALLBACK (review 2026-06-12): the shelf candidate set
            // is sparser than exact NFP, so on rare tight instances (~1/4000 in
            // fuzzing) the fast path strands a part the general engine could
            // place. If the fast path leaves ANY part unplaced, discard it and
            // run the general engine — speed never trades away placements.
            string engine;
            if (enableRectFastPath && spacing == 0.0 &&
                TryRectFastPath(sheetOuter, sheetHoles, parts, order, res) &&
                res.Placements.Count == parts.Count)
            {
                engine = "rect-shelf";
            }
            else
            {
                bool fastTried = res.Placements.Count > 0;
                if (fastTried)
                {
                    res.Placements.Clear();
                    res.PartHolesFilled = 0;
                    res.UsedArea = 0;
                    res.Note = "";
                }
                engine = fastTried ? "general-nfp (fast path left parts unplaced)" : "general-nfp";
                // progressive snapshots: after each committed placement hand the
                // caller a CALLER-SPACE copy of the layout so far (the engine
                // runs in scaled+normalized space; copies are mapped back)
                Action progressTick = onPlacement == null ? (Action)null : () =>
                {
                    var snap = new HoleNestResult
                    {
                        Placements = MapPlacementsToCallerSpace(res.Placements, normOffset, inPlace: false),
                        PlacedCount = res.Placements.Count,
                        PartHolesFilled = res.PartHolesFilled,
                        Note = "progress",
                    };
                    onPlacement(snap);
                };
                PackGeneral(sheetOuter, sheetHoles, parts, spacing, baseRotationCount, contactRotations, order, res, progressTick);
            }

            // ---- final validation (boolean, independent of the placement path)
            res.Valid = Validate(res, parts, sheetOuter, sheetHoles);
            res.Density = res.UsedArea / sheetNet;
            res.PlacedCount = res.Placements.Count;
            res.Note = string.IsNullOrEmpty(res.Note) ? engine : engine + " | " + res.Note;

            // ---- map results back to the caller's space ----------------------
            // Express each placement against the ORIGINAL (un-normalized,
            // un-scaled) part loop: placed = R(ang)*(p - b) + t = R(ang)*p + t'
            // with t' = t - R(ang)*b, so AngleRad+Tx/Ty applied to the caller's
            // input geometry reproduces PlacedOuter exactly.
            MapPlacementsToCallerSpace(res.Placements, normOffset, inPlace: true);
            res.UsedArea /= Scale * Scale;

            sw.Stop();
            res.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        /// <summary>
        /// MULTI-SHEET nesting (append-only API, 2026-06-12): greedy overflow —
        /// fill sheet 0, carry the unplaced parts to sheet 1, and so on. Each
        /// sheet is solved and boolean-validated by the SAME single-sheet
        /// engine. Placements come back with PartIndex remapped to the ORIGINAL
        /// parts list and SheetIndex set; onPlacement receives the CUMULATIVE
        /// caller-space layout across sheets (progressive display friendly).
        /// </summary>
        public static List<HoleNestResult> PackSheets(
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheets,
            IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> sheetHolesPerSheet,
            IReadOnlyList<HoleNestPart> parts,
            double spacing = 0.0,
            int baseRotationCount = 4,
            int contactRotations = 6,
            bool enableRectFastPath = true,
            Action<HoleNestResult> onPlacement = null)
        {
            var results = new List<HoleNestResult>();
            if (sheets == null || sheets.Count == 0) return results;
            var remaining = new List<HoleNestPart>(parts ?? new List<HoleNestPart>());
            var remainingIdx = new List<int>();
            for (int i = 0; i < remaining.Count; i++) remainingIdx.Add(i);
            var done = new List<HoleNestPlacement>();
            int holesFilledDone = 0;

            for (int si = 0; si < sheets.Count; si++)
            {
                if (remaining.Count == 0)
                {
                    results.Add(new HoleNestResult { Valid = true, Note = "empty (no parts left)" });
                    continue;
                }
                var idxSnapshot = new List<int>(remainingIdx);
                int sheetIdx = si;
                int doneHoles = holesFilledDone;
                var doneSnapshot = done; // grows only between sheets
                Action<HoleNestResult> tick = onPlacement == null ? (Action<HoleNestResult>)null : partial =>
                {
                    var agg = new HoleNestResult
                    {
                        Placements = new List<HoleNestPlacement>(doneSnapshot),
                        PartHolesFilled = doneHoles + partial.PartHolesFilled,
                        Note = "progress",
                    };
                    foreach (var pl in partial.Placements)
                        agg.Placements.Add(RemapPlacement(pl, idxSnapshot, sheetIdx));
                    agg.PlacedCount = agg.Placements.Count;
                    onPlacement(agg);
                };

                var holes = sheetHolesPerSheet != null && si < sheetHolesPerSheet.Count
                    ? sheetHolesPerSheet[si] : null;
                var res = Pack(sheets[si], holes, remaining, spacing,
                    baseRotationCount, contactRotations, enableRectFastPath, tick);

                var placedLocal = new HashSet<int>();
                foreach (var pl in res.Placements)
                {
                    placedLocal.Add(pl.PartIndex);
                    RemapPlacement(pl, idxSnapshot, sheetIdx);
                }
                done.AddRange(res.Placements);
                holesFilledDone += res.PartHolesFilled;
                results.Add(res);

                var nextRemaining = new List<HoleNestPart>();
                var nextIdx = new List<int>();
                for (int i = 0; i < remaining.Count; i++)
                    if (!placedLocal.Contains(i)) { nextRemaining.Add(remaining[i]); nextIdx.Add(remainingIdx[i]); }
                remaining = nextRemaining;
                remainingIdx = nextIdx;
            }
            return results;
        }

        // remap a placement from sheet-local part indices to the caller's
        // original parts list + tag the sheet (mutates and returns the instance;
        // progressive partials are fresh copies, finals are owned by the result)
        private static HoleNestPlacement RemapPlacement(HoleNestPlacement pl, List<int> idxMap, int sheetIdx)
        {
            pl.SheetIndex = sheetIdx;
            if (pl.PartIndex >= 0 && pl.PartIndex < idxMap.Count) pl.PartIndex = idxMap[pl.PartIndex];
            if (pl.HostIndex >= 0 && pl.HostIndex < idxMap.Count) pl.HostIndex = idxMap[pl.HostIndex];
            return pl;
        }

        /// <summary>
        /// Express placements against the caller's original (un-normalized,
        /// un-scaled) geometry. inPlace=true mutates; otherwise returns fresh
        /// copies so progressive snapshots never expose engine-space state.
        /// </summary>
        private static List<HoleNestPlacement> MapPlacementsToCallerSpace(
            List<HoleNestPlacement> placements,
            List<(double BX, double BY)> normOffset, bool inPlace)
        {
            var outList = inPlace ? placements : new List<HoleNestPlacement>(placements.Count);
            for (int i = 0; i < placements.Count; i++)
            {
                var src = placements[i];
                var pl = inPlace ? src : new HoleNestPlacement
                {
                    PartIndex = src.PartIndex, AngleRad = src.AngleRad,
                    Tx = src.Tx, Ty = src.Ty, NestedInHost = src.NestedInHost,
                    HostIndex = src.HostIndex, HostHole = src.HostHole,
                    PlacedOuter = src.PlacedOuter,
                };
                var (bx, by) = pl.PartIndex >= 0 && pl.PartIndex < normOffset.Count
                    ? normOffset[pl.PartIndex] : (0.0, 0.0);
                double c = Math.Cos(pl.AngleRad), sn = Math.Sin(pl.AngleRad);
                double tx = pl.Tx - (c * bx - sn * by);
                double ty = pl.Ty - (sn * bx + c * by);
                pl.Tx = tx / Scale;
                pl.Ty = ty / Scale;
                pl.PlacedOuter = ScaleLoop(pl.PlacedOuter, 1.0 / Scale);
                if (!inPlace) outList.Add(pl);
            }
            return outList;
        }

        // ---- general exact-NFP engine (the contract; unchanged behaviour) ----
        private static void PackGeneral(
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            IReadOnlyList<HoleNestPart> parts,
            double spacing, int baseRotationCount, int contactRotations,
            List<int> order, HoleNestResult res, Action progressTick = null)
        {
            // placed material (outer minus its own holes), as transformed loops,
            // kept for NFP obstacles + overlap re-checks
            var placedMaterial = new List<List<(double X, double Y)>>();   // each: just the outer (material upper bound)
            var placedHoles = new List<List<List<(double X, double Y)>>>(); // per placed part: its transformed holes
            var placedIndex = new List<int>();
            bool usedNativeNfp = false; // any TryPlaceOuter ran the native NFP lane

            // ---- Phase A: holes-first part-in-part-hole nesting -------------
            // try to seat the SMALLEST parts into the holes of already-chosen
            // hosts. We do a provisional host pass: any part with holes is a
            // candidate host once placed in Phase B; so Phase A runs AFTER an
            // initial host placement. To keep it single-pass and deterministic
            // we place hosts (parts that HAVE holes, by area desc) first, then
            // nest smalls, then place the rest. Track which parts are consumed.
            var consumed = new bool[parts.Count];

            var hostsFirst = order.Where(i => parts[i].Holes != null && parts[i].Holes.Count > 0).ToList();
            var nonHosts = order.Where(i => parts[i].Holes == null || parts[i].Holes.Count == 0).ToList();

            // 1) place hosts by NFP-BLF
            foreach (int pi in hostsFirst)
            {
                if (TryPlaceOuter(pi, parts[pi], sheetOuter, sheetHoles, placedMaterial,
                        spacing, baseRotationCount, contactRotations, out var pl, ref usedNativeNfp))
                {
                    Commit(pl, parts[pi], placedMaterial, placedHoles, placedIndex, res);
                    consumed[pi] = true;
                    progressTick?.Invoke();
                }
            }

            // 2) nest smalls into host holes (smallest first for tight fills)
            foreach (int si in nonHosts.AsEnumerable().Reverse())
            {
                if (consumed[si]) continue;
                if (TryNestInHostHole(si, parts[si], res.Placements, parts, placedHoles,
                        placedMaterial, spacing, contactRotations, out var pl, out int holeUsedHost, out int holeUsedIdx))
                {
                    pl.NestedInHost = true; pl.HostIndex = holeUsedHost; pl.HostHole = holeUsedIdx;
                    Commit(pl, parts[si], placedMaterial, placedHoles, placedIndex, res);
                    res.PartHolesFilled++;
                    consumed[si] = true;
                    progressTick?.Invoke();
                }
            }

            // ---- Phase B: place the remaining outer parts by NFP-BLF --------
            foreach (int pi in nonHosts)
            {
                if (consumed[pi]) continue;
                if (TryPlaceOuter(pi, parts[pi], sheetOuter, sheetHoles, placedMaterial,
                        spacing, baseRotationCount, contactRotations, out var pl, ref usedNativeNfp))
                {
                    Commit(pl, parts[pi], placedMaterial, placedHoles, placedIndex, res);
                    consumed[pi] = true;
                    progressTick?.Invoke();
                }
            }

            // tag the result when the native batched-NFP kernel did the
            // Minkowski work (Pack() prefixes the engine name afterwards;
            // a failed Validate overwrites this with its failure reason)
            if (usedNativeNfp) res.Note = "+native-nfp";
        }

        // ════════════════════════════════════════════════════════════════════
        // RECT SHELF FAST-PATH (exact special-case shortcut, 2026-06-12)
        //
        // When every loop in the instance is an axis-aligned rectangle, the
        // NFP/IFP regions degenerate to rectangles and the whole solve reduces
        // to pure interval arithmetic — the same trick the native shelf
        // reference implementation uses. The general exact-NFP engine above
        // remains the contract: this path must produce layouts that pass the
        // SAME path-independent boolean Validate() gate, and any instance that
        // is not all-rectangles runs the general engine untouched.
        //
        // Activation conditions (ALL required, else general path runs):
        //   * enableRectFastPath == true (Pack parameter, default true)
        //   * spacing == 0   (v1 limitation: spacing would need exact rect
        //     dilation bookkeeping; deferred — general path handles spacing>0)
        //   * sheet outer is an axis-aligned rectangle
        //   * every sheet-hole is an axis-aligned rectangle
        //   * every part outer AND every part-hole is an axis-aligned rectangle
        //
        // Rectangle detection: exactly 4 distinct vertices and every edge
        // axis-parallel within 1e-9 * bbox-diagonal.
        //
        // Rotations reduce to {0°, 90°} (180°/270° are duplicates for rects);
        // squares use {0°} only. 90° transforms use the exact map
        // (x,y) -> (-y,x): no trigonometric round-off, no Clipper calls.
        // ════════════════════════════════════════════════════════════════════

        private sealed class RectShelfHole
        {
            public double MinX, MinY, MaxX, MaxY;   // world-space hole rect
            public int HostPartIndex;               // PartIndex of the placed host
            public int HoleIndex;                   // hole index within the host
            public double CurX, ShelfY, ShelfH;     // shelf cursor (advance x, wrap y)
        }

        private static bool TryRectFastPath(
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            IReadOnlyList<HoleNestPart> parts,
            List<int> order, HoleNestResult res)
        {
            // ---- detection: prove EVERY loop rectangular before mutating res
            if (!TryAxisRect(sheetOuter, out var sheet)) return false;
            var defects = new List<(double MinX, double MinY, double MaxX, double MaxY)>(sheetHoles.Count);
            foreach (var q in sheetHoles)
            {
                if (!TryAxisRect(q, out var d)) return false;
                defects.Add(d);
            }
            var outers = new (double MinX, double MinY, double MaxX, double MaxY)[parts.Count];
            var partHoleRects = new List<(double MinX, double MinY, double MaxX, double MaxY)>[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null || !TryAxisRect(parts[i].Outer, out outers[i])) return false;
                if (parts[i].Holes != null && parts[i].Holes.Count > 0)
                {
                    var hl = new List<(double MinX, double MinY, double MaxX, double MaxY)>(parts[i].Holes.Count);
                    foreach (var hole in parts[i].Holes)
                    {
                        if (!TryAxisRect(hole, out var hr)) return false;
                        hl.Add(hr);
                    }
                    partHoleRects[i] = hl;
                }
            }

            // ---- pack (pure interval arithmetic from here on) ----------------
            var placedRects = new List<(double MinX, double MinY, double MaxX, double MaxY)>(); // non-nested footprints
            var openHoles = new List<RectShelfHole>();      // host holes, in placement order
            var consumed = new bool[parts.Count];

            var hosts = order.Where(i => partHoleRects[i] != null).ToList();
            var fillers = order.Where(i => partHoleRects[i] == null).ToList();

            // 1) hosts first (area-desc), bottom-left shelf with contact candidates
            foreach (int pi in hosts)
            {
                if (!TryRectPlaceOuter(pi, parts[pi].Outer, outers[pi], sheet, defects, placedRects,
                        out var pl, out var fp)) continue;
                res.Placements.Add(pl);
                placedRects.Add(fp);
                consumed[pi] = true;
                int ori = pl.AngleRad == 0.0 ? 0 : 1;
                for (int k = 0; k < partHoleRects[pi].Count; k++)
                {
                    var hr = partHoleRects[pi][k];
                    double wminx = ori == 0 ? hr.MinX + pl.Tx : pl.Tx - hr.MaxY;
                    double wmaxx = ori == 0 ? hr.MaxX + pl.Tx : pl.Tx - hr.MinY;
                    double wminy = ori == 0 ? hr.MinY + pl.Ty : hr.MinX + pl.Ty;
                    double wmaxy = ori == 0 ? hr.MaxY + pl.Ty : hr.MaxX + pl.Ty;
                    openHoles.Add(new RectShelfHole
                    {
                        MinX = wminx, MinY = wminy, MaxX = wmaxx, MaxY = wmaxy,
                        HostPartIndex = pi, HoleIndex = k,
                        CurX = wminx, ShelfY = wminy, ShelfH = 0
                    });
                }
            }

            // 2) Phase A (holes-first): smallest fillers seat into host holes
            foreach (int si in fillers.AsEnumerable().Reverse())
            {
                if (consumed[si]) continue;
                if (TryRectNestInHole(si, parts[si].Outer, outers[si], openHoles, out var pl))
                {
                    res.Placements.Add(pl);
                    res.PartHolesFilled++;
                    consumed[si] = true;
                }
            }

            // 3) Phase B: remaining parts on the open sheet (area-desc)
            foreach (int pi in fillers)
            {
                if (consumed[pi]) continue;
                if (TryRectPlaceOuter(pi, parts[pi].Outer, outers[pi], sheet, defects, placedRects,
                        out var pl, out var fp))
                {
                    res.Placements.Add(pl);
                    placedRects.Add(fp);
                    consumed[pi] = true;
                }
            }
            return true;
        }

        // axis-aligned rectangle test: exactly 4 distinct vertices, each edge
        // axis-parallel within 1e-9 * bbox-diagonal; output snapped to the bbox.
        private static bool TryAxisRect(
            IReadOnlyList<(double X, double Y)> loop,
            out (double MinX, double MinY, double MaxX, double MaxY) r)
        {
            r = default;
            if (loop == null || loop.Count < 4) return false;
            // collapse exact consecutive repeats + a repeated closing vertex
            var v = new List<(double X, double Y)>(loop.Count);
            foreach (var p in loop)
                if (v.Count == 0 || p.X != v[v.Count - 1].X || p.Y != v[v.Count - 1].Y) v.Add(p);
            if (v.Count > 1 && v[0].X == v[v.Count - 1].X && v[0].Y == v[v.Count - 1].Y)
                v.RemoveAt(v.Count - 1);
            if (v.Count != 4 || v.Distinct().Count() != 4) return false;
            BBox(v, out double minx, out double miny, out double maxx, out double maxy);
            double dx = maxx - minx, dy = maxy - miny;
            double tol = 1e-9 * Math.Sqrt(dx * dx + dy * dy);
            if (dx <= tol || dy <= tol) return false; // degenerate
            for (int i = 0; i < 4; i++)
            {
                var a = v[i]; var b = v[(i + 1) % 4];
                if (Math.Abs(b.X - a.X) > tol && Math.Abs(b.Y - a.Y) > tol) return false;
            }
            // 4 distinct vertices + axis-parallel edges + closure => rectangle
            r = (minx, miny, maxx, maxy);
            return true;
        }

        // bottom-left shelf with contact candidates:
        //   X = {sheet.minX} U {placed.maxX} U {defect.maxX, defect.minX - w}
        //   Y = {sheet.minY} U {placed.maxY} U {defect.maxY, defect.minY - h}
        // tried in (y,x) lexicographic order; the first candidate that is inside
        // the sheet, clear of every defect and clear of every placed rect wins.
        private static bool TryRectPlaceOuter(
            int pi, IReadOnlyList<(double X, double Y)> loop,
            (double MinX, double MinY, double MaxX, double MaxY) o,
            (double MinX, double MinY, double MaxX, double MaxY) sheet,
            List<(double MinX, double MinY, double MaxX, double MaxY)> defects,
            List<(double MinX, double MinY, double MaxX, double MaxY)> placed,
            out HoleNestPlacement pl,
            out (double MinX, double MinY, double MaxX, double MaxY) footprint)
        {
            pl = null; footprint = default;
            double w0 = o.MaxX - o.MinX, h0 = o.MaxY - o.MinY;
            int oriCount = Math.Abs(w0 - h0) <= 1e-9 * Math.Sqrt(w0 * w0 + h0 * h0) ? 1 : 2; // square: {0} only
            bool have = false; double bestX = 0, bestY = 0; int bestOri = 0;
            for (int ori = 0; ori < oriCount; ori++)
            {
                double w = ori == 0 ? w0 : h0, h = ori == 0 ? h0 : w0;
                if (!TryFirstShelfCandidate(w, h, sheet, defects, placed, out double px, out double py)) continue;
                if (!have || py < bestY || (py == bestY && px < bestX))
                { have = true; bestX = px; bestY = py; bestOri = ori; }
            }
            if (!have) return false;
            double bw = bestOri == 0 ? w0 : h0, bh = bestOri == 0 ? h0 : w0;
            pl = MakeRectPlacement(pi, loop, o, bestOri, bestX, bestY);
            footprint = (bestX, bestY, bestX + bw, bestY + bh);
            return true;
        }

        private static bool TryFirstShelfCandidate(
            double w, double h,
            (double MinX, double MinY, double MaxX, double MaxY) sheet,
            List<(double MinX, double MinY, double MaxX, double MaxY)> defects,
            List<(double MinX, double MinY, double MaxX, double MaxY)> placed,
            out double px, out double py)
        {
            px = py = 0;
            var xs = new List<double> { sheet.MinX };
            var ys = new List<double> { sheet.MinY };
            foreach (var p in placed) { xs.Add(p.MaxX); ys.Add(p.MaxY); }
            foreach (var d in defects) { xs.Add(d.MaxX); xs.Add(d.MinX - w); ys.Add(d.MaxY); ys.Add(d.MinY - h); }
            xs.Sort(); ys.Sort();
            for (int yi = 0; yi < ys.Count; yi++)
            {
                if (yi > 0 && ys[yi] == ys[yi - 1]) continue;       // dedupe
                double y = ys[yi];
                if (y < sheet.MinY || y + h > sheet.MaxY) continue; // inside sheet (Y)
                for (int xi = 0; xi < xs.Count; xi++)
                {
                    if (xi > 0 && xs[xi] == xs[xi - 1]) continue;   // dedupe
                    double x = xs[xi];
                    if (x < sheet.MinX || x + w > sheet.MaxX) continue;
                    bool hit = false;
                    foreach (var d in defects)
                        if (x < d.MaxX && d.MinX < x + w && y < d.MaxY && d.MinY < y + h) { hit = true; break; }
                    if (hit) continue;
                    foreach (var p in placed)
                        if (x < p.MaxX && p.MinX < x + w && y < p.MaxY && p.MinY < y + h) { hit = true; break; }
                    if (hit) continue;
                    px = x; py = y;
                    return true;
                }
            }
            return false;
        }

        // Phase A fit: filler (w,h) fits hole (W,H) iff (w<=W && h<=H) or
        // (h<=W && w<=H) — realised as a shelf inside the hole so several
        // fillers can share one hole (advance x, wrap to the next shelf y).
        private static bool TryRectNestInHole(
            int si, IReadOnlyList<(double X, double Y)> loop,
            (double MinX, double MinY, double MaxX, double MaxY) o,
            List<RectShelfHole> holes, out HoleNestPlacement pl)
        {
            pl = null;
            double w0 = o.MaxX - o.MinX, h0 = o.MaxY - o.MinY;
            int oriCount = Math.Abs(w0 - h0) <= 1e-9 * Math.Sqrt(w0 * w0 + h0 * h0) ? 1 : 2;
            foreach (var hs in holes)
            {
                for (int ori = 0; ori < oriCount; ori++)
                {
                    double w = ori == 0 ? w0 : h0, h = ori == 0 ? h0 : w0;
                    // current shelf: advance x
                    if (hs.CurX + w <= hs.MaxX && hs.ShelfY + h <= hs.MaxY)
                    {
                        pl = MakeRectPlacement(si, loop, o, ori, hs.CurX, hs.ShelfY);
                        pl.NestedInHost = true; pl.HostIndex = hs.HostPartIndex; pl.HostHole = hs.HoleIndex;
                        hs.CurX += w; hs.ShelfH = Math.Max(hs.ShelfH, h);
                        return true;
                    }
                    // wrap to the next shelf
                    if (hs.ShelfH > 0)
                    {
                        double ny = hs.ShelfY + hs.ShelfH;
                        if (hs.MinX + w <= hs.MaxX && ny + h <= hs.MaxY)
                        {
                            hs.ShelfY = ny; hs.CurX = hs.MinX; hs.ShelfH = 0;
                            pl = MakeRectPlacement(si, loop, o, ori, hs.CurX, hs.ShelfY);
                            pl.NestedInHost = true; pl.HostIndex = hs.HostPartIndex; pl.HostHole = hs.HoleIndex;
                            hs.CurX += w; hs.ShelfH = h;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // exact transform for the fast path: ori 0 is a pure translation, ori 1
        // is the exact 90° map (x,y) -> (-y,x) + translation (no trig round-off).
        // Tx/Ty are chosen so the placed bbox-min lands exactly on (px,py).
        private static HoleNestPlacement MakeRectPlacement(
            int pi, IReadOnlyList<(double X, double Y)> loop,
            (double MinX, double MinY, double MaxX, double MaxY) o,
            int ori, double px, double py)
        {
            double tx, ty, ang;
            if (ori == 0) { ang = 0.0; tx = px - o.MinX; ty = py - o.MinY; }
            else { ang = Math.PI / 2; tx = px + o.MaxY; ty = py - o.MinX; }
            var placed = new List<(double X, double Y)>(loop.Count);
            foreach (var v in loop)
                placed.Add(ori == 0 ? (v.X + tx, v.Y + ty) : (tx - v.Y, v.X + ty));
            return new HoleNestPlacement { PartIndex = pi, AngleRad = ang, Tx = tx, Ty = ty, PlacedOuter = placed };
        }

        // ════════════════════════════════ end rect shelf fast-path ═══════════

        // ---- place a part anywhere in the free space (NFP bottom-left-fill) --
        private static bool TryPlaceOuter(
            int pi, HoleNestPart part,
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            List<List<(double X, double Y)>> placedMaterial,
            double spacing, int baseRotationCount, int contactRotations,
            out HoleNestPlacement best, ref bool usedNativeNfp)
        {
            best = null;
            double bestY = double.MaxValue, bestX = double.MaxValue;

            // ── native batched-NFP lane (2026-06-12) ──────────────────────────
            // Profiling: managed Minkowski NFP builds were ~95% of the general-
            // engine solve. When nfp_kernel.dll is loadable, ALL (budgeted
            // rotation x current obstacle) NFPs for this part are built in ONE
            // P/Invoke on Clipper2's exact Int64 lane, then consumed per angle
            // exactly like the managed loop below (same rotation set, same
            // translation-space cull, same boolean subtract, same verified
            // bottom-left candidate walk — CandidateOk still runs managed).
            // spacing > 0 rides the native lane too: the un-rotated part is
            // inflated ONCE (inflation commutes with rotation) and the kernel
            // rotates internally. On ANY native failure the managed path runs
            // verbatim.
            if (NativeNfpKernel.IsAvailable &&
                TryPlaceOuterNativeNfp(pi, part, sheetOuter, sheetHoles, placedMaterial, spacing,
                    baseRotationCount, contactRotations, out best))
            {
                usedNativeNfp = true;
                return best != null;
            }

            var seenRot = new HashSet<string>();   // collapse symmetric rotations

            foreach (double ang in RotationSet(part.Outer, sheetOuter, placedMaterial, baseRotationCount, contactRotations))
            {
                var rot = Rotate(part.Outer, ang);
                if (!seenRot.Add(RotSignature(rot))) continue; // same shape already tried
                if (spacing > 0) rot = InflateOuter(rot, spacing);
                var refl = Reflect(rot);

                // IFP(part, sheet): translations keeping the part inside the sheet
                var feasible = InnerFit(rot, sheetOuter);
                if (feasible.Count == 0) continue;

                // accumulate obstacle NFPs, then subtract in ONE boolean (cheaper
                // than N sequential differences). The cull works entirely in
                // TRANSLATION space: an obstacle's NFP occupies the t-interval
                // [m.min - rot.max, m.max - rot.min] per axis, which only
                // coincides with the obstacle's own bbox when the part's origin
                // sits at the part (the 2026-06-12 canvas bug: world-positioned
                // parts made the old world-space cull drop live obstacles, so
                // their NFPs were never subtracted and parts stacked).
                BBoxLoops(feasible, out double fminx, out double fminy, out double fmaxx, out double fmaxy);
                BBox(rot, out double rminx, out double rminy, out double rmaxx, out double rmaxy);
                var reflSimp = SimplifyLoop(refl, NfpSimplifyTol(rot));
                var obstacles = new List<List<(double X, double Y)>>();
                foreach (var q in sheetHoles)
                    obstacles.AddRange(Clipper2Adapter.MinkowskiSum(SimplifyLoop(ToList(q), NfpSimplifyTol(q)), reflSimp));
                foreach (var m in placedMaterial)
                {
                    BBox(m, out double mminx, out double mminy, out double mmaxx, out double mmaxy);
                    double tminx = mminx - rmaxx, tmaxx = mmaxx - rminx;
                    double tminy = mminy - rmaxy, tmaxy = mmaxy - rminy;
                    if (tmaxx < fminx - Eps || tminx > fmaxx + Eps ||
                        tmaxy < fminy - Eps || tminy > fmaxy + Eps) continue;
                    obstacles.AddRange(Clipper2Adapter.MinkowskiSum(SimplifyLoop(m, NfpSimplifyTol(m)), reflSimp));
                }
                if (obstacles.Count > 0)
                    feasible = SubtractLoops(feasible, UnionAll(obstacles));
                if (feasible.Count == 0) continue;

                // walk the feasible-region vertices bottom-left first and take
                // the FIRST candidate that survives the exact compound check
                // (area + distance depth + one micro-retreat). The NFP is a
                // pruning device, not the safety guarantee: the Minkowski
                // construction has coverage gaps for concave/sampled shapes
                // (and was simplified above), so every placement is verified
                // against the true geometry before it can win.
                foreach (var (tx, ty) in OrderedVertices(feasible, AdaptiveCandidateCap(placedMaterial.Count)))
                {
                    if (ty > bestY + Eps) break; // sorted by (y,x): cannot beat best
                    if (Math.Abs(ty - bestY) <= Eps && tx >= bestX - Eps) continue;
                    if (!TryVerifiedCandidate(rot, tx, ty, sheetOuter, sheetHoles, placedMaterial,
                            out double atx, out double aty)) continue;
                    if (aty > bestY + Eps ||
                        (Math.Abs(aty - bestY) <= Eps && atx >= bestX - Eps)) continue; // retreat moved it past best
                    bestY = aty; bestX = atx;
                    best = new HoleNestPlacement
                    {
                        PartIndex = pi, AngleRad = ang, Tx = atx, Ty = aty,
                        PlacedOuter = Translate(Rotate(part.Outer, ang), atx, aty)
                    };
                    break;
                }
            }
            return best != null;
        }

        // ---- native batched-NFP variant of TryPlaceOuter ---------------------
        // Returns TRUE iff the native kernel ran (one batch per part); then
        // `best` is the placement result (possibly null = part does not fit).
        // Returns FALSE when there is nothing to batch or the kernel failed,
        // and the caller must run the managed loop verbatim.
        private static bool TryPlaceOuterNativeNfp(
            int pi, HoleNestPart part,
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            List<List<(double X, double Y)>> placedMaterial,
            double spacing, int baseRotationCount, int contactRotations,
            out HoleNestPlacement best)
        {
            best = null;
            int holeCount = sheetHoles.Count;
            if (holeCount + placedMaterial.Count == 0) return false; // no NFPs to build

            // spacing support (2026-06-12): mirror the managed rotate-then-
            // inflate by inflating the UN-rotated part once — inflation
            // commutes with rotation, and the kernel rotates internally. The
            // deviation-compensated spacing on smooth instances made every
            // curved-sheet solve spacing>0, which the old spacing==0 gate
            // silently excluded from the native lane.
            var baseOuter = spacing > 0
                ? (IReadOnlyList<(double X, double Y)>)InflateOuter(ToList(part.Outer), spacing)
                : part.Outer;

            // resolve the budgeted rotation list with the SAME symmetric-shape
            // dedup the managed loop applies lazily
            var angles = new List<double>();
            var rots = new List<List<(double X, double Y)>>();
            var seenRot = new HashSet<string>();
            foreach (double ang in RotationSet(part.Outer, sheetOuter, placedMaterial, baseRotationCount, contactRotations))
            {
                var rotRaw = Rotate(part.Outer, ang);
                if (!seenRot.Add(RotSignature(rotRaw))) continue;
                angles.Add(ang);
                rots.Add(spacing > 0 ? Rotate(baseOuter, ang) : rotRaw);
            }
            if (angles.Count == 0) return false;

            // obstacles in the managed order: sheet-holes first, then placed parts
            var obst = new List<IReadOnlyList<(double X, double Y)>>(holeCount + placedMaterial.Count);
            foreach (var q in sheetHoles) obst.Add(q);
            foreach (var m in placedMaterial) obst.Add(m);

            // ONE batched P/Invoke: all rotations x all obstacles. scale 100 =
            // the managed Clipper2 PathD lane's decimal precision 2; -2e-3 =
            // the relative RDP tolerance NfpSimplifyTol uses (2e-3 x bbox-diag).
            var nfps = NativeNfpKernel.BatchNfp(baseOuter, angles, obst, 100.0, -2e-3);
            if (nfps == null) return false; // kernel failed -> managed fallback

            var byAngle = new List<List<(double X, double Y)>>[angles.Count];
            var byAngleObst = new List<int>[angles.Count];
            for (int i = 0; i < angles.Count; i++)
            {
                byAngle[i] = new List<List<(double X, double Y)>>();
                byAngleObst[i] = new List<int>();
            }
            foreach (var l in nfps)
            {
                if (l.AngleIdx < 0 || l.AngleIdx >= angles.Count) continue;
                if (l.ObstIdx < 0 || l.ObstIdx >= obst.Count) continue; // defensive symmetry (review)
                byAngle[l.AngleIdx].Add(l.Loop);
                byAngleObst[l.AngleIdx].Add(l.ObstIdx);
            }

            double bestY = double.MaxValue, bestX = double.MaxValue;
            for (int i = 0; i < angles.Count; i++)
            {
                double ang = angles[i];
                var rot = rots[i];

                var feasible = InnerFit(rot, sheetOuter);
                if (feasible.Count == 0) continue;

                BBoxLoops(feasible, out double fminx, out double fminy, out double fmaxx, out double fmaxy);
                BBox(rot, out double rminx, out double rminy, out double rmaxx, out double rmaxy);

                // same translation-space cull as the managed loop: a placed
                // obstacle's NFP occupies [m.min - rot.max, m.max - rot.min]
                // per axis; sheet-hole NFPs are never culled (managed parity)
                var obstacles = new List<List<(double X, double Y)>>();
                var loopsI = byAngle[i];
                var obstI = byAngleObst[i];
                for (int k = 0; k < loopsI.Count; k++)
                {
                    int oi = obstI[k];
                    if (oi >= holeCount)
                    {
                        var m = placedMaterial[oi - holeCount];
                        BBox(m, out double mminx, out double mminy, out double mmaxx, out double mmaxy);
                        double tminx = mminx - rmaxx, tmaxx = mmaxx - rminx;
                        double tminy = mminy - rmaxy, tmaxy = mmaxy - rminy;
                        if (tmaxx < fminx - Eps || tminx > fmaxx + Eps ||
                            tmaxy < fminy - Eps || tminy > fmaxy + Eps) continue;
                    }
                    obstacles.Add(loopsI[k]);
                }
                if (obstacles.Count > 0)
                    feasible = SubtractLoops(feasible, UnionAll(obstacles));
                if (feasible.Count == 0) continue;

                // identical verified bottom-left candidate walk: the native NFP
                // is a pruning device exactly like the managed one; every
                // placement is re-checked by the exact managed compound gate
                // (area + distance depth + one micro-retreat) before it can win
                foreach (var (tx, ty) in OrderedVertices(feasible, AdaptiveCandidateCap(placedMaterial.Count)))
                {
                    if (ty > bestY + Eps) break;
                    if (Math.Abs(ty - bestY) <= Eps && tx >= bestX - Eps) continue;
                    if (!TryVerifiedCandidate(rot, tx, ty, sheetOuter, sheetHoles, placedMaterial,
                            out double atx, out double aty)) continue;
                    if (aty > bestY + Eps ||
                        (Math.Abs(aty - bestY) <= Eps && atx >= bestX - Eps)) continue;
                    bestY = aty; bestX = atx;
                    best = new HoleNestPlacement
                    {
                        PartIndex = pi, AngleRad = ang, Tx = atx, Ty = aty,
                        PlacedOuter = Translate(Rotate(part.Outer, ang), atx, aty)
                    };
                    break;
                }
            }
            return true; // native lane ran (best may still be null = no fit)
        }

        // ---- nest a small part into some host's hole ------------------------
        private static bool TryNestInHostHole(
            int si, HoleNestPart small,
            List<HoleNestPlacement> placements, IReadOnlyList<HoleNestPart> parts,
            List<List<List<(double X, double Y)>>> placedHoles,
            List<List<(double X, double Y)>> placedMaterial,
            double spacing, int contactRotations,
            out HoleNestPlacement best, out int hostIdx, out int holeIdx)
        {
            best = null; hostIdx = -1; holeIdx = -1;
            double bestY = double.MaxValue, bestX = double.MaxValue;
            double smallArea = Math.Abs(SignedArea(small.Outer));

            for (int h = 0; h < placements.Count; h++)
            {
                var holes = placedHoles[h];
                if (holes == null) continue;
                for (int k = 0; k < holes.Count; k++)
                {
                    var hole = holes[k];
                    if (Math.Abs(SignedArea(hole)) < smallArea + Eps) continue; // hole too small

                    // parts already nested in THIS host-hole become obstacles so
                    // several smalls can share one hole without overlapping
                    int hostPart = placements[h].PartIndex;
                    var coNested = placements
                        .Where(q => q.NestedInHost && q.HostIndex == hostPart && q.HostHole == k)
                        .Select(q => ToList(q.PlacedOuter)).ToList();

                    foreach (double ang in ContactAngles(small.Outer, hole, contactRotations))
                    {
                        var rot = Rotate(small.Outer, ang);
                        if (spacing > 0) rot = InflateOuter(rot, spacing);
                        // IFP(small, hole): translations keeping small INSIDE the hole
                        var feasible = InnerFit(rot, hole);
                        if (feasible.Count == 0) continue;
                        var refl = Reflect(rot);
                        foreach (var cn in coNested)
                            feasible = SubtractLoops(feasible, Clipper2Adapter.MinkowskiSum(cn, refl));
                        if (feasible.Count == 0) continue;

                        // verified bottom-left candidate: the hull-based IFP is
                        // anti-conservative for CONCAVE holes (vertices-inside
                        // does not imply containment), so every nesting is
                        // re-checked by the exact compound gate (area +
                        // distance depth + one micro-retreat) before it can win.
                        foreach (var (tx, ty) in OrderedVertices(feasible, MaxCandidateVerts))
                        {
                            if (ty > bestY + Eps) break;
                            if (Math.Abs(ty - bestY) <= Eps && tx >= bestX - Eps) continue;
                            if (!TryVerifiedNest(rot, tx, ty, hole, coNested,
                                    out double atx, out double aty)) continue;
                            if (aty > bestY + Eps ||
                                (Math.Abs(aty - bestY) <= Eps && atx >= bestX - Eps)) continue;
                            bestY = aty; bestX = atx; hostIdx = placements[h].PartIndex; holeIdx = k;
                            best = new HoleNestPlacement
                            {
                                PartIndex = si, AngleRad = ang, Tx = atx, Ty = aty,
                                PlacedOuter = Translate(Rotate(small.Outer, ang), atx, aty)
                            };
                            break;
                        }
                    }
                }
            }
            return best != null;
        }

        // ── rotation sets ───────────────────────────────────────────────────
        // ORDERED and BUDGETED (2026-06-12): base rotations first, then contact
        // angles in priority order, deduped, capped. On sampled curve input the
        // unbounded cross product (host edges x part edges x 2) exploded to
        // ~80 angles per part and each angle costs a full NFP build — 8.5 s for
        // 7 parts. The cap keeps the strongest candidates and bounds the cost.
        private static IEnumerable<double> RotationSet(
            IReadOnlyList<(double X, double Y)> part,
            IReadOnlyList<(double X, double Y)> sheet,
            List<List<(double X, double Y)>> placed,
            int baseCount, int contactCount)
        {
            int budget = Math.Max(4, Math.Max(1, baseCount) + 4 + 2 * Math.Max(0, contactCount));
            var list = new List<double>(budget);
            void Add(double a)
            {
                if (list.Count >= budget) return;
                a = Norm(a);
                for (int i = 0; i < list.Count; i++) if (Math.Abs(list[i] - a) < 1e-4) return;
                list.Add(a);
            }
            for (int r = 0; r < Math.Max(1, baseCount); r++) Add(2 * Math.PI * r / Math.Max(1, baseCount));
            // contact angles vs the sheet's longest edges + the most recent placed neighbour
            foreach (var a in ContactAngles(part, sheet, contactCount)) { if (list.Count >= budget) break; Add(a); }
            if (placed.Count > 0)
                foreach (var a in ContactAngles(part, placed[placed.Count - 1], contactCount)) { if (list.Count >= budget) break; Add(a); }
            return list;
        }

        // edge-alignment angles: a(host_edge) - a(part_edge) over longest edges
        private static IEnumerable<double> ContactAngles(
            IReadOnlyList<(double X, double Y)> part,
            IReadOnlyList<(double X, double Y)> host, int count)
        {
            yield return 0; yield return Math.PI / 2; yield return Math.PI; yield return 3 * Math.PI / 2;
            var pe = LongestEdges(part, count);
            var he = LongestEdges(host, count);
            foreach (var ha in he)
                foreach (var pa in pe)
                {
                    yield return Norm(ha - pa);
                    yield return Norm(ha - pa + Math.PI);
                }
        }

        private static List<double> LongestEdges(IReadOnlyList<(double X, double Y)> poly, int count)
        {
            var edges = new List<(double len, double ang)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                edges.Add((dx * dx + dy * dy, Math.Atan2(dy, dx)));
            }
            return edges.OrderByDescending(e => e.len).Take(Math.Max(1, count)).Select(e => e.ang).ToList();
        }

        // ── inner-fit region: exact Minkowski erosion container (-) hull(part)
        // IFP = ∩_{v ∈ hull(part)} (container − v). Exact for convex containers
        // and conservative (safe) otherwise — the same construction FreeNestX
        // uses. The reference point is the part's own coordinate origin.
        private static List<List<(double X, double Y)>> InnerFit(
            IReadOnlyList<(double X, double Y)> part,
            IReadOnlyList<(double X, double Y)> container)
        {
            var hull = ConvexHull(part);
            var ifp = new List<List<(double X, double Y)>> { ToList(container) };
            foreach (var (vx, vy) in hull)
            {
                var shifted = new List<(double X, double Y)>(container.Count);
                for (int i = 0; i < container.Count; i++) shifted.Add((container[i].X - vx, container[i].Y - vy));
                ifp = Clipper2Adapter.IntersectLoops(
                    ifp.Select(x => (IReadOnlyList<(double X, double Y)>)x).ToList(),
                    new List<IReadOnlyList<(double X, double Y)>> { shifted });
                if (ifp.Count == 0) break;
            }
            return ifp;
        }

        // Andrew's monotone chain convex hull (CCW, no repeat of first point).
        private static List<(double X, double Y)> ConvexHull(IReadOnlyList<(double X, double Y)> pts)
        {
            var p = pts.Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
            if (p.Count < 3) return p.ToList();
            double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
                (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var lo = new List<(double X, double Y)>();
            foreach (var pt in p) { while (lo.Count >= 2 && Cross(lo[lo.Count - 2], lo[lo.Count - 1], pt) <= 0) lo.RemoveAt(lo.Count - 1); lo.Add(pt); }
            var hi = new List<(double X, double Y)>();
            for (int i = p.Count - 1; i >= 0; i--) { var pt = p[i]; while (hi.Count >= 2 && Cross(hi[hi.Count - 2], hi[hi.Count - 1], pt) <= 0) hi.RemoveAt(hi.Count - 1); hi.Add(pt); }
            lo.RemoveAt(lo.Count - 1); hi.RemoveAt(hi.Count - 1);
            lo.AddRange(hi);
            return lo;
        }

        // ── validation (path-independent): boolean AREA gates plus the same
        // distance-based DEPTH gates the candidate walks use, so Valid==true
        // certifies the layout against needle overlaps on EVERY engine path ──
        private static bool Validate(HoleNestResult res, IReadOnlyList<HoleNestPart> parts,
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles)
        {
            double used = 0;
            var outerMats = new List<List<(double X, double Y)>>();   // non-nested part outers
            var nestedMats = new List<List<(double X, double Y)>>();  // nested part outers
            foreach (var pl in res.Placements)
            {
                var outer = pl.PlacedOuter;
                double a = Math.Abs(SignedArea(outer));
                double tolArea = 1e-3 * a + Eps;
                double depthTol = DepthTolFor(a);
                // inside the sheet: area gate + exact distance depth
                double inSheet = Clipper2Adapter.Intersect(outer, sheetOuter).Sum(l => Math.Abs(SignedArea(l)));
                if (inSheet < a - tolArea)
                { res.Note = $"part {pl.PartIndex} outside sheet (in={inSheet:0.0}/{a:0.0}, nested={pl.NestedInHost})"; return false; }
                if (OutsideDepth(outer, sheetOuter, out _, out _) > depthTol)
                { res.Note = $"part {pl.PartIndex} pierces the sheet boundary"; return false; }

                if (pl.NestedInHost)
                {
                    // a nested filler must sit inside its ASSIGNED host hole
                    // (distance-checked; concave-safe) and clear of OTHER
                    // nested parts.
                    HoleNestPlacement host = null;
                    foreach (var q in res.Placements)
                        if (q.PartIndex == pl.HostIndex) { host = q; break; }
                    if (host == null || pl.HostIndex < 0 || pl.HostIndex >= parts.Count ||
                        parts[pl.HostIndex].Holes == null ||
                        pl.HostHole < 0 || pl.HostHole >= parts[pl.HostIndex].Holes.Count)
                    { res.Note = $"nested part {pl.PartIndex} has no resolvable host hole"; return false; }
                    var holeWorld = Translate(Rotate(parts[pl.HostIndex].Holes[pl.HostHole], host.AngleRad), host.Tx, host.Ty);
                    if (OutsideDepth(outer, holeWorld, out _, out _) > depthTol)
                    { res.Note = $"nested part {pl.PartIndex} pierces its host hole"; return false; }
                    foreach (var m in nestedMats)
                    {
                        if (Clipper2Adapter.Intersect(outer, m).Sum(l => Math.Abs(SignedArea(l))) > tolArea)
                        { res.Note = $"nested part {pl.PartIndex} overlaps another nested part"; return false; }
                        if (PenetrationDepth(outer, m, out _, out _) > depthTol)
                        { res.Note = $"nested part {pl.PartIndex} pierces another nested part"; return false; }
                    }
                    nestedMats.Add(ToList(outer));
                }
                else
                {
                    // non-nested parts clear the sheet-defects (overlap area
                    // summed ACROSS defects: per-hole tolerances let split
                    // overlaps through) and every other non-nested part
                    // (Phase-B NFP avoids full host outers, so a host's own
                    // hole is never invaded from outside).
                    double holeAreaSum = 0;
                    foreach (var q in sheetHoles)
                    {
                        holeAreaSum += Clipper2Adapter.Intersect(outer, q).Sum(l => Math.Abs(SignedArea(l)));
                        if (holeAreaSum > tolArea)
                        { res.Note = $"part {pl.PartIndex} overlaps a sheet-defect"; return false; }
                        if (PenetrationDepth(outer, q, out _, out _) > depthTol)
                        { res.Note = $"part {pl.PartIndex} pierces a sheet-defect"; return false; }
                    }
                    foreach (var m in outerMats)
                    {
                        if (Clipper2Adapter.Intersect(outer, m).Sum(l => Math.Abs(SignedArea(l))) > tolArea)
                        { res.Note = $"part {pl.PartIndex} overlaps another part"; return false; }
                        if (PenetrationDepth(outer, m, out _, out _) > depthTol)
                        { res.Note = $"part {pl.PartIndex} pierces another part"; return false; }
                    }
                    outerMats.Add(ToList(outer));
                }
                used += a;
            }
            res.UsedArea = used;
            return res.Placements.Count > 0;
        }

        private static void Commit(HoleNestPlacement pl, HoleNestPart part,
            List<List<(double X, double Y)>> placedMaterial,
            List<List<List<(double X, double Y)>>> placedHoles,
            List<int> placedIndex, HoleNestResult res)
        {
            placedMaterial.Add(ToList(pl.PlacedOuter));
            // transform this part's holes to world for later nesting
            if (part.Holes != null && part.Holes.Count > 0)
            {
                var th = new List<List<(double X, double Y)>>();
                foreach (var hole in part.Holes)
                    th.Add(Translate(Rotate(hole, pl.AngleRad), pl.Tx, pl.Ty));
                placedHoles.Add(th);
            }
            else placedHoles.Add(null);
            placedIndex.Add(pl.PartIndex);
            res.Placements.Add(pl);
        }

        // ── geometry helpers ─────────────────────────────────────────────────
        private static List<(double X, double Y)> Rotate(IReadOnlyList<(double X, double Y)> p, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            var r = new List<(double X, double Y)>(p.Count);
            foreach (var v in p) r.Add((v.X * c - v.Y * s, v.X * s + v.Y * c));
            return r;
        }
        private static List<(double X, double Y)> Translate(IReadOnlyList<(double X, double Y)> p, double tx, double ty)
        {
            var r = new List<(double X, double Y)>(p.Count);
            foreach (var v in p) r.Add((v.X + tx, v.Y + ty));
            return r;
        }
        private static List<(double X, double Y)> Reflect(IReadOnlyList<(double X, double Y)> p)
        {
            var r = new List<(double X, double Y)>(p.Count);
            foreach (var v in p) r.Add((-v.X, -v.Y));
            return r;
        }
        private static List<(double X, double Y)> InflateOuter(IReadOnlyList<(double X, double Y)> p, double d)
        {
            var inf = Clipper2Adapter.InflateLoops(new List<IReadOnlyList<(double X, double Y)>> { p }, d);
            return inf.Count > 0 ? inf[0] : new List<(double X, double Y)>(p);
        }
        private static List<List<(double X, double Y)>> UnionLoops(
            List<List<(double X, double Y)>> a, List<List<(double X, double Y)>> b)
        {
            if (a.Count == 0) return b;
            if (b.Count == 0) return a;
            return Clipper2Adapter.Boolean(a.Select(x => (IReadOnlyList<(double X, double Y)>)x).ToList(),
                                           b.Select(x => (IReadOnlyList<(double X, double Y)>)x).ToList(),
                                           ClipType.Union);
        }
        private static List<List<(double X, double Y)>> SubtractLoops(
            List<List<(double X, double Y)>> a, List<List<(double X, double Y)>> b)
        {
            if (a.Count == 0 || b.Count == 0) return a;
            return Clipper2Adapter.Boolean(a.Select(x => (IReadOnlyList<(double X, double Y)>)x).ToList(),
                                           b.Select(x => (IReadOnlyList<(double X, double Y)>)x).ToList(),
                                           ClipType.Difference);
        }
        private static List<(double X, double Y)> ToList(IReadOnlyList<(double X, double Y)> p) =>
            p as List<(double X, double Y)> ?? p.ToList();
        private static List<List<(double X, double Y)>> ToList(IReadOnlyList<IReadOnlyList<(double X, double Y)>> ps) =>
            ps.Select(ToList).ToList();

        private static bool BottomLeftVertex(List<List<(double X, double Y)>> loops, out double bx, out double by)
        {
            bx = by = 0; bool found = false;
            double minY = double.MaxValue, minX = double.MaxValue;
            foreach (var loop in loops)
                foreach (var v in loop)
                    if (v.Y < minY - Eps || (Math.Abs(v.Y - minY) <= Eps && v.X < minX))
                    { minY = v.Y; minX = v.X; bx = v.X; by = v.Y; found = true; }
            return found;
        }
        private static double SignedArea(IReadOnlyList<(double X, double Y)> p)
        {
            double a = 0; int n = p.Count;
            for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
            return a / 2.0;
        }
        private static void BBox(IReadOnlyList<(double X, double Y)> p,
            out double minx, out double miny, out double maxx, out double maxy)
        {
            minx = miny = double.MaxValue; maxx = maxy = double.MinValue;
            foreach (var v in p) { if (v.X < minx) minx = v.X; if (v.Y < miny) miny = v.Y; if (v.X > maxx) maxx = v.X; if (v.Y > maxy) maxy = v.Y; }
        }
        private static void BBoxLoops(List<List<(double X, double Y)>> loops,
            out double minx, out double miny, out double maxx, out double maxy)
        {
            minx = miny = double.MaxValue; maxx = maxy = double.MinValue;
            foreach (var loop in loops)
                foreach (var v in loop)
                { if (v.X < minx) minx = v.X; if (v.Y < miny) miny = v.Y; if (v.X > maxx) maxx = v.X; if (v.Y > maxy) maxy = v.Y; }
        }
        private static List<List<(double X, double Y)>> UnionAll(List<List<(double X, double Y)>> loops)
        {
            if (loops.Count <= 1) return loops;
            // single NonZero union over ALL loops at once: outer/hole loop
            // orientations are resolved by winding, unlike a pairwise fold that
            // treats each CW hole loop as a filled region
            return Clipper2Adapter.Boolean(
                loops.Select(x => (IReadOnlyList<(double X, double Y)>)x).ToList(),
                new List<IReadOnlyList<(double X, double Y)>>(),
                ClipType.Union);
        }

        // ── conditioning + verification helpers (2026-06-12) ─────────────────
        private const double Scale = 1000.0;          // internal Clipper-space scale
        private const int MaxCandidateVerts = 32;     // verified BLF candidates per rotation (small instances / hole nesting)

        // CANDIDATE STARVATION FIX (2026-06-12, curved-sheet underfill): on
        // dense instances (100+ placed obstacles) the feasible region's
        // bottom-left vertices cluster in the congested zone as sliver
        // candidates that fail exact verification; a fixed 32-candidate walk
        // exhausted there and declared parts unplaceable while open area
        // remained higher up. The cap now grows with congestion so the walk
        // can climb past the junk zone; cost stays bounded (verification is
        // bbox-pruned and the walk still exits on the first verified vertex).
        private static int AdaptiveCandidateCap(int placedCount)
            => Math.Max(MaxCandidateVerts, 16 + 4 * placedCount);
        private const double VerifyRelTol = 1e-4;     // exact-check tolerance, relative to part area

        private static List<(double X, double Y)> ScaleLoop(IReadOnlyList<(double X, double Y)> p, double s)
        {
            var r = new List<(double X, double Y)>(p.Count);
            foreach (var v in p) r.Add((v.X * s, v.Y * s));
            return r;
        }

        // Ramer-Douglas-Peucker, closed-loop variant: NFP INPUTS only (the
        // verification below always runs on the exact loops). Sampled curves
        // arrive with up to 200 vertices; Minkowski cost is |pattern|x|path|.
        private static List<(double X, double Y)> SimplifyLoop(List<(double X, double Y)> p, double tol)
        {
            if (p.Count <= 8 || tol <= 0) return p;
            var keep = new bool[p.Count];
            keep[0] = true; keep[p.Count / 2] = true; // two anchors on a closed loop
            RdpMark(p, 0, p.Count / 2, tol, keep);
            RdpMark(p, p.Count / 2, p.Count, tol, keep); // wraps to index 0
            var r = new List<(double X, double Y)>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) r.Add(p[i]);
            return r.Count >= 3 ? r : p;
        }

        private static void RdpMark(List<(double X, double Y)> p, int a, int b, double tol, bool[] keep)
        {
            if (b - a < 2) return;
            var pa = p[a]; var pb = p[b % p.Count];
            double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int worst = -1; double worstD = tol;
            for (int i = a + 1; i < b; i++)
            {
                double d = len < Eps
                    ? Math.Sqrt(Sq2(p[i].X - pa.X) + Sq2(p[i].Y - pa.Y))
                    : Math.Abs(dx * (pa.Y - p[i].Y) - (pa.X - p[i].X) * dy) / len;
                if (d > worstD) { worstD = d; worst = i; }
            }
            if (worst < 0) return;
            keep[worst] = true;
            RdpMark(p, a, worst, tol, keep);
            RdpMark(p, worst, b, tol, keep);
        }

        private static double Sq2(double v) => v * v;

        private static double NfpSimplifyTol(IReadOnlyList<(double X, double Y)> p)
        {
            BBox(p, out double mnx, out double mny, out double mxx, out double mxy);
            return 2e-3 * Math.Sqrt(Sq2(mxx - mnx) + Sq2(mxy - mny));
        }

        // all loop vertices sorted bottom-left first ((y,x) lexicographic), capped
        private static IEnumerable<(double X, double Y)> OrderedVertices(
            List<List<(double X, double Y)>> loops, int cap)
        {
            var all = new List<(double X, double Y)>();
            foreach (var loop in loops) all.AddRange(loop);
            all.Sort((u, v) => u.Y != v.Y ? u.Y.CompareTo(v.Y) : u.X.CompareTo(v.X));
            int n = Math.Min(cap, all.Count);
            for (int i = 0; i < n; i++) yield return all[i];
        }

        // ── compound verification gate (2026-06-12 depth fix; header doc) ───
        // Scaled-space constants. The Clipper PathD lane snaps to decimal
        // precision 2, so 0.01 scaled (= 1e-5 caller units at Scale = 1000)
        // is the hard resolution limit of every boolean this engine runs.
        private const double SnapGrid = 0.01;            // scaled units
        private const double DepthFloor = 2 * SnapGrid;  // = 2e-5 caller units
        private const double DepthRelTol = 1e-6;         // x sqrt(part area)
        private const double RetreatSlack = SnapGrid;    // micro-retreat clearance

        // eps_depth = max(floor, 1e-6 * L), L = sqrt(part area), scaled space.
        private static double DepthTolFor(double areaScaled)
            => Math.Max(DepthFloor, DepthRelTol * Math.Sqrt(Math.Max(areaScaled, 0.0)));

        // exact verification of an outer placement: inside the sheet, clear of
        // sheet-holes (area summed ACROSS holes) and of every placed part.
        // Area gates catch gross overlap; the distance-based depth gate
        // catches needle/spike overlaps whose area is tiny. On a depth
        // failure, (pushX,pushY) is the measured penetration vector (length =
        // violDepth) that moves the part out of its deepest violation.
        private static bool CandidateOk(
            List<(double X, double Y)> placedLoop,
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            List<List<(double X, double Y)>> placedMaterial,
            out double violDepth, out double pushX, out double pushY)
        {
            violDepth = 0; pushX = 0; pushY = 0;
            double a = Math.Abs(SignedArea(placedLoop));
            double tolArea = VerifyRelTol * a + Eps;
            double depthTol = DepthTolFor(a);

            // (1) sheet containment: gross area gate, then exact distance depth
            double inSheet = Clipper2Adapter.Intersect(placedLoop, sheetOuter).Sum(l => Math.Abs(SignedArea(l)));
            if (inSheet < a - tolArea) return false;
            double dOut = OutsideDepth(placedLoop, sheetOuter, out double sx, out double sy);
            if (dOut > depthTol) { violDepth = dOut; pushX = sx; pushY = sy; return false; }

            BBox(placedLoop, out double pminx, out double pminy, out double pmaxx, out double pmaxy);

            // (2) sheet-holes: summed area (split-overlap fix) + per-hole depth
            double holeAreaSum = 0;
            foreach (var q in sheetHoles)
            {
                BBox(q, out double qminx, out double qminy, out double qmaxx, out double qmaxy);
                if (qmaxx < pminx || qminx > pmaxx || qmaxy < pminy || qminy > pmaxy) continue;
                holeAreaSum += Clipper2Adapter.Intersect(placedLoop, q).Sum(l => Math.Abs(SignedArea(l)));
                if (holeAreaSum > tolArea) return false;
                double d = PenetrationDepth(placedLoop, q, out double hx, out double hy);
                if (d > depthTol) { violDepth = d; pushX = hx; pushY = hy; return false; }
            }

            // (3) placed parts: exact distance depth (probes + crossings +
            // centroid nets subsume the old boolean area pair gate)
            foreach (var m in placedMaterial)
            {
                BBox(m, out double mminx, out double mminy, out double mmaxx, out double mmaxy);
                if (mmaxx < pminx || mminx > pmaxx || mmaxy < pminy || mminy > pmaxy) continue;
                double d = PenetrationDepth(placedLoop, m, out double ox, out double oy);
                if (d > depthTol) { violDepth = d; pushX = ox; pushY = oy; return false; }
            }
            return true;
        }

        // exact verification of a nested placement: contained in the host
        // hole (distance depth, concave-safe), clear of co-nested parts.
        private static bool NestOk(
            List<(double X, double Y)> placedLoop,
            IReadOnlyList<(double X, double Y)> hole,
            List<List<(double X, double Y)>> coNested,
            out double violDepth, out double pushX, out double pushY)
        {
            violDepth = 0; pushX = 0; pushY = 0;
            double a = Math.Abs(SignedArea(placedLoop));
            double tolArea = VerifyRelTol * a + Eps;
            double depthTol = DepthTolFor(a);
            double inHole = Clipper2Adapter.Intersect(placedLoop, hole).Sum(l => Math.Abs(SignedArea(l)));
            if (inHole < a - tolArea) return false;
            double dOut = OutsideDepth(placedLoop, hole, out double sx, out double sy);
            if (dOut > depthTol) { violDepth = dOut; pushX = sx; pushY = sy; return false; }
            foreach (var cn in coNested)
            {
                if (Clipper2Adapter.Intersect(placedLoop, cn).Sum(l => Math.Abs(SignedArea(l))) > tolArea) return false;
                double d = PenetrationDepth(placedLoop, cn, out double ox, out double oy);
                if (d > depthTol) { violDepth = d; pushX = ox; pushY = oy; return false; }
            }
            return true;
        }

        // ── micro-retreat wrappers: verify, and on a depth failure nudge ONCE
        // along the measured penetration vector by (depth + RetreatSlack),
        // then re-verify. Rescues contact-tight candidates that only violate
        // through NFP/IFP construction error instead of discarding them.
        private static bool TryVerifiedCandidate(
            List<(double X, double Y)> rot, double tx, double ty,
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            List<List<(double X, double Y)>> placedMaterial,
            out double atx, out double aty)
        {
            atx = tx; aty = ty;
            var placedLoop = Translate(rot, tx, ty);
            if (CandidateOk(placedLoop, sheetOuter, sheetHoles, placedMaterial,
                    out double depth, out double px, out double py)) return true;
            if (depth <= 0) return false;                  // area-gate failure: no direction to retreat
            double len = Math.Sqrt(px * px + py * py);
            if (len < Eps) return false;                   // crossing-only violation: no probe direction
            double k = (len + RetreatSlack) / len;
            double ntx = tx + px * k, nty = ty + py * k;
            var nudged = Translate(rot, ntx, nty);
            if (!CandidateOk(nudged, sheetOuter, sheetHoles, placedMaterial, out _, out _, out _)) return false;
            atx = ntx; aty = nty;
            return true;
        }

        private static bool TryVerifiedNest(
            List<(double X, double Y)> rot, double tx, double ty,
            IReadOnlyList<(double X, double Y)> hole,
            List<List<(double X, double Y)>> coNested,
            out double atx, out double aty)
        {
            atx = tx; aty = ty;
            var placedLoop = Translate(rot, tx, ty);
            if (NestOk(placedLoop, hole, coNested, out double depth, out double px, out double py)) return true;
            if (depth <= 0) return false;
            double len = Math.Sqrt(px * px + py * py);
            if (len < Eps) return false;
            double k = (len + RetreatSlack) / len;
            double ntx = tx + px * k, nty = ty + py * k;
            var nudged = Translate(rot, ntx, nty);
            if (!NestOk(nudged, hole, coNested, out _, out _, out _)) return false;
            atx = ntx; aty = nty;
            return true;
        }

        // ── distance-based depth measurement (mirrors the independent
        // verification protocol: probes = vertices + edge midpoints + area
        // centroid; plus proper edge-crossing perpendicular depth) ───────────

        // penetration of A into B (and B into A): max distance of a probe of
        // one loop strictly inside the other to that other loop's boundary.
        // push = vector that moves A out of the deepest violation.
        private static double PenetrationDepth(
            IReadOnlyList<(double X, double Y)> A, IReadOnlyList<(double X, double Y)> B,
            out double pushX, out double pushY)
        {
            pushX = 0; pushY = 0;
            double depth = 0;
            foreach (var (px, py) in DepthProbes(A))
            {
                if (!PointInLoop(px, py, B)) continue;
                double d = DistToBoundary(px, py, B, out double qx, out double qy);
                if (d > depth) { depth = d; pushX = qx - px; pushY = qy - py; }
            }
            foreach (var (ux, uy) in DepthProbes(B))
            {
                if (!PointInLoop(ux, uy, A)) continue;
                double d = DistToBoundary(ux, uy, A, out double rx, out double ry);
                if (d > depth) { depth = d; pushX = ux - rx; pushY = uy - ry; }
            }
            double cpd = MaxProperCross(A, B);
            if (cpd > depth) depth = cpd; // keep the probe-based push direction
            return depth;
        }

        // how far A sticks OUT of a container: max distance of a probe of A
        // outside the container to the container boundary, plus crossings.
        // push = vector that pulls A back toward the container.
        private static double OutsideDepth(
            IReadOnlyList<(double X, double Y)> A, IReadOnlyList<(double X, double Y)> container,
            out double pushX, out double pushY)
        {
            pushX = 0; pushY = 0;
            double depth = 0;
            foreach (var (px, py) in DepthProbes(A))
            {
                if (PointInLoop(px, py, container)) continue;
                double d = DistToBoundary(px, py, container, out double qx, out double qy);
                if (d > depth) { depth = d; pushX = qx - px; pushY = qy - py; }
            }
            double cpd = MaxProperCross(A, container);
            if (cpd > depth) depth = cpd;
            return depth;
        }

        // probes: vertices, edge midpoints, and the area centroid (the
        // centroid closes the exact-duplicate / full-containment blind spot)
        private static IEnumerable<(double X, double Y)> DepthProbes(IReadOnlyList<(double X, double Y)> p)
        {
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                yield return p[i];
                var q = p[(i + 1) % n];
                yield return ((p[i].X + q.X) / 2, (p[i].Y + q.Y) / 2);
            }
            double sa = 0, cx = 0, cy = 0;
            for (int i = 0; i < n; i++)
            {
                var u = p[i]; var v = p[(i + 1) % n];
                double w = u.X * v.Y - v.X * u.Y;
                sa += w; cx += (u.X + v.X) * w; cy += (u.Y + v.Y) * w;
            }
            if (Math.Abs(sa) > Eps) yield return (cx / (3 * sa), cy / (3 * sa));
        }

        // even-odd point-in-polygon (concave-safe)
        private static bool PointInLoop(double x, double y, IReadOnlyList<(double X, double Y)> poly)
        {
            bool inside = false; int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = poly[i]; var pj = poly[j];
                if ((pi.Y > y) != (pj.Y > y))
                {
                    double xc = pj.X + (y - pj.Y) * (pi.X - pj.X) / (pi.Y - pj.Y);
                    if (x < xc) inside = !inside;
                }
            }
            return inside;
        }

        // distance to the polygon boundary + the nearest boundary point
        private static double DistToBoundary(double x, double y,
            IReadOnlyList<(double X, double Y)> poly, out double qx, out double qy)
        {
            double best = double.MaxValue; qx = x; qy = y;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double l2 = dx * dx + dy * dy;
                double t = l2 < 1e-24 ? 0 : ((x - a.X) * dx + (y - a.Y) * dy) / l2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double cx = a.X + t * dx, cy = a.Y + t * dy;
                double d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (d2 < best * best) { best = Math.Sqrt(d2); qx = cx; qy = cy; }
            }
            return best;
        }

        // max proper-crossing depth proxy over all edge pairs (min
        // perpendicular endpoint distance), 0 when no edges properly cross
        private static double MaxProperCross(
            IReadOnlyList<(double X, double Y)> A, IReadOnlyList<(double X, double Y)> B)
        {
            double bestCross = 0;
            int na = A.Count, nb = B.Count;
            for (int i = 0; i < na; i++)
            {
                var a1 = A[i]; var a2 = A[(i + 1) % na];
                double aminx = Math.Min(a1.X, a2.X), amaxx = Math.Max(a1.X, a2.X);
                double aminy = Math.Min(a1.Y, a2.Y), amaxy = Math.Max(a1.Y, a2.Y);
                for (int j = 0; j < nb; j++)
                {
                    var b1 = B[j]; var b2 = B[(j + 1) % nb];
                    if (Math.Max(b1.X, b2.X) < aminx || Math.Min(b1.X, b2.X) > amaxx ||
                        Math.Max(b1.Y, b2.Y) < aminy || Math.Min(b1.Y, b2.Y) > amaxy) continue;
                    double d1 = CrossZ(b1, b2, a1), d2 = CrossZ(b1, b2, a2);
                    if (!((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))) continue;
                    double d3 = CrossZ(a1, a2, b1), d4 = CrossZ(a1, a2, b2);
                    if (!((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) continue;
                    double la = Math.Sqrt(Sq2(a2.X - a1.X) + Sq2(a2.Y - a1.Y));
                    double lb = Math.Sqrt(Sq2(b2.X - b1.X) + Sq2(b2.Y - b1.Y));
                    if (la < 1e-12 || lb < 1e-12) continue;
                    double m = Math.Min(
                        Math.Min(Math.Abs(d1), Math.Abs(d2)) / lb,
                        Math.Min(Math.Abs(d3), Math.Abs(d4)) / la);
                    if (m > bestCross) bestCross = m;
                }
            }
            return bestCross;
        }

        private static double CrossZ((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        private static double Norm(double a) { a %= 2 * Math.PI; if (a < 0) a += 2 * Math.PI; return a; }

        // Translation-invariant shape signature: vertices relative to bbox-min,
        // rounded and order-independent. Two rotations of a symmetric part yield
        // the same signature, so the more expensive NFP evaluation runs once.
        private static string RotSignature(IReadOnlyList<(double X, double Y)> rot)
        {
            BBox(rot, out double mnx, out double mny, out double _, out double _);
            var keys = new List<long>(rot.Count);
            foreach (var v in rot)
            {
                long kx = (long)Math.Round((v.X - mnx) * 1000.0);
                long ky = (long)Math.Round((v.Y - mny) * 1000.0);
                keys.Add(kx * 2147483647L + ky);
            }
            keys.Sort();
            return string.Join(",", keys);
        }

        private sealed class AngleCmp : IComparer<double>
        {
            public int Compare(double x, double y) => Math.Abs(x - y) < 1e-4 ? 0 : x.CompareTo(y);
        }
    }
}
