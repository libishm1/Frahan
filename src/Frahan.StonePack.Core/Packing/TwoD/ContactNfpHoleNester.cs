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
            bool enableRectFastPath = true) // append-only (2026-06-12): opt-out for the exact rect shelf fast-path
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new HoleNestResult();
            sheetHoles = sheetHoles ?? Array.Empty<IReadOnlyList<(double X, double Y)>>();

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
                PackGeneral(sheetOuter, sheetHoles, parts, spacing, baseRotationCount, contactRotations, order, res);
            }

            // ---- final validation (boolean, independent of the placement path)
            res.Valid = Validate(res, parts, sheetOuter, sheetHoles);
            res.Density = res.UsedArea / sheetNet;
            res.PlacedCount = res.Placements.Count;
            res.Note = string.IsNullOrEmpty(res.Note) ? engine : engine + " | " + res.Note;
            sw.Stop();
            res.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ---- general exact-NFP engine (the contract; unchanged behaviour) ----
        private static void PackGeneral(
            IReadOnlyList<(double X, double Y)> sheetOuter,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>> sheetHoles,
            IReadOnlyList<HoleNestPart> parts,
            double spacing, int baseRotationCount, int contactRotations,
            List<int> order, HoleNestResult res)
        {
            // placed material (outer minus its own holes), as transformed loops,
            // kept for NFP obstacles + overlap re-checks
            var placedMaterial = new List<List<(double X, double Y)>>();   // each: just the outer (material upper bound)
            var placedHoles = new List<List<List<(double X, double Y)>>>(); // per placed part: its transformed holes
            var placedIndex = new List<int>();

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
                        spacing, baseRotationCount, contactRotations, out var pl))
                {
                    Commit(pl, parts[pi], placedMaterial, placedHoles, placedIndex, res);
                    consumed[pi] = true;
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
                }
            }

            // ---- Phase B: place the remaining outer parts by NFP-BLF --------
            foreach (int pi in nonHosts)
            {
                if (consumed[pi]) continue;
                if (TryPlaceOuter(pi, parts[pi], sheetOuter, sheetHoles, placedMaterial,
                        spacing, baseRotationCount, contactRotations, out var pl))
                {
                    Commit(pl, parts[pi], placedMaterial, placedHoles, placedIndex, res);
                    consumed[pi] = true;
                }
            }
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
            out HoleNestPlacement best)
        {
            best = null;
            double bestY = double.MaxValue, bestX = double.MaxValue;
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
                // than N sequential differences); cull placed parts whose bbox is
                // out of reach of the IFP (the FreeNestX reach test)
                BBoxLoops(feasible, out double fminx, out double fminy, out double fmaxx, out double fmaxy);
                BBox(rot, out double rminx, out double rminy, out double rmaxx, out double rmaxy);
                double reach = Math.Max(rmaxx - rminx, rmaxy - rminy);
                var obstacles = new List<List<(double X, double Y)>>();
                foreach (var q in sheetHoles)
                    obstacles.AddRange(Clipper2Adapter.MinkowskiSum(ToList(q), refl));
                foreach (var m in placedMaterial)
                {
                    BBox(m, out double mminx, out double mminy, out double mmaxx, out double mmaxy);
                    if (mmaxx + reach < fminx || fmaxx < mminx - reach ||
                        mmaxy + reach < fminy || fmaxy < mminy - reach) continue;
                    obstacles.AddRange(Clipper2Adapter.MinkowskiSum(m, refl));
                }
                if (obstacles.Count > 0)
                    feasible = SubtractLoops(feasible, UnionAll(obstacles));
                if (feasible.Count == 0) continue;

                if (!BottomLeftVertex(feasible, out double tx, out double ty)) continue;
                if (ty < bestY - Eps || (Math.Abs(ty - bestY) <= Eps && tx < bestX - Eps))
                {
                    bestY = ty; bestX = tx;
                    best = new HoleNestPlacement
                    {
                        PartIndex = pi, AngleRad = ang, Tx = tx, Ty = ty,
                        PlacedOuter = Translate(Rotate(part.Outer, ang), tx, ty)
                    };
                }
            }
            return best != null;
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
                        if (!BottomLeftVertex(feasible, out double tx, out double ty)) continue;
                        if (ty < bestY - Eps || (Math.Abs(ty - bestY) <= Eps && tx < bestX - Eps))
                        {
                            bestY = ty; bestX = tx; hostIdx = placements[h].PartIndex; holeIdx = k;
                            best = new HoleNestPlacement
                            {
                                PartIndex = si, AngleRad = ang, Tx = tx, Ty = ty,
                                PlacedOuter = Translate(Rotate(small.Outer, ang), tx, ty)
                            };
                        }
                    }
                }
            }
            return best != null;
        }

        // ── rotation sets ───────────────────────────────────────────────────
        private static IEnumerable<double> RotationSet(
            IReadOnlyList<(double X, double Y)> part,
            IReadOnlyList<(double X, double Y)> sheet,
            List<List<(double X, double Y)>> placed,
            int baseCount, int contactCount)
        {
            var set = new SortedSet<double>(new AngleCmp());
            for (int r = 0; r < Math.Max(1, baseCount); r++) set.Add(2 * Math.PI * r / Math.Max(1, baseCount));
            // contact angles vs the sheet's longest edges + the most recent placed neighbour
            foreach (var a in ContactAngles(part, sheet, contactCount)) set.Add(Norm(a));
            if (placed.Count > 0)
                foreach (var a in ContactAngles(part, placed[placed.Count - 1], contactCount)) set.Add(Norm(a));
            return set;
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

        // ── validation (boolean, path-independent) ───────────────────────────
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
                // inside the sheet
                double inSheet = Clipper2Adapter.Intersect(outer, sheetOuter).Sum(l => Math.Abs(SignedArea(l)));
                if (inSheet < a - 1e-3 * a - Eps)
                { res.Note = $"part {pl.PartIndex} outside sheet (in={inSheet:0.0}/{a:0.0}, nested={pl.NestedInHost})"; return false; }

                if (pl.NestedInHost)
                {
                    // a nested filler is contained in its host's HOLE (Phase-A IFP
                    // guarantees host containment + host material is hole-free);
                    // it only needs to be clear of OTHER nested parts.
                    foreach (var m in nestedMats)
                        if (Clipper2Adapter.Intersect(outer, m).Sum(l => Math.Abs(SignedArea(l))) > 1e-3 * a + Eps)
                        { res.Note = $"nested part {pl.PartIndex} overlaps another nested part"; return false; }
                    nestedMats.Add(ToList(outer));
                }
                else
                {
                    // non-nested parts clear the sheet-defects and every other
                    // non-nested part (Phase-B NFP avoids full host outers, so a
                    // host's own hole is never invaded from outside).
                    foreach (var q in sheetHoles)
                        if (Clipper2Adapter.Intersect(outer, q).Sum(l => Math.Abs(SignedArea(l))) > 1e-3 * a + Eps)
                        { res.Note = $"part {pl.PartIndex} overlaps a sheet-defect"; return false; }
                    foreach (var m in outerMats)
                        if (Clipper2Adapter.Intersect(outer, m).Sum(l => Math.Abs(SignedArea(l))) > 1e-3 * a + Eps)
                        { res.Note = $"part {pl.PartIndex} overlaps another part"; return false; }
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
            var acc = new List<List<(double X, double Y)>> { loops[0] };
            for (int i = 1; i < loops.Count; i++) acc = UnionLoops(acc, new List<List<(double X, double Y)>> { loops[i] });
            return acc;
        }
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
