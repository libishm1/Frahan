#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Frahan.Masonry.Geometry;
using Rhino;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Exact No-Fit-Polygon Bottom-Left-Fill nester (2026-06-03).
///
/// Evolved sibling of <see cref="IrregularSheetFillV506"/>. Where V506 uses an
/// approximate subsampled NFP plus a fixed-size bottom-left candidate grid, this
/// solver builds the COMPLETE feasible region for each part and rotation:
///
///   feasible(B) = IFP(outer, B)  \  ( union_k NFP(A_k, B) )  \  ( union_j NFP(hole_j, B) )
///
/// and places the part at the bottom-left vertex of that region. Non-overlap is a
/// HARD CONSTRAINT satisfied by construction (the reference point lies outside every
/// no-fit polygon), not a post-hoc trim. This closes the named-but-unbuilt
/// edge-exclusivity gap.
///
///   NFP(A,B) = A (+) (-B)            Clipper2 Minkowski sum (Clipper2Adapter).
///   IFP(outer,B) = intersect_v (outer - v), v in vertices(hull(B))
///                                    exact for convex B, conservative (safe) otherwise.
///   holes are obstacles handled identically to placed parts.
///
/// Study + validation: outputs/2026-06-03/pack2d_nfp_evolution/ (mean wasted-area
/// cut 53.9% vs the V506 heuristic on a seeded instance set, zero overlap, Python
/// reference). All Clipper work is done in a scaled integer-friendly space to avoid
/// the Clipper int64 large-coordinate overflow and low-precision artifacts.
/// </summary>
public sealed class IrregularSheetFillNfpBlf
{
    private const int    MaxVerts = 200;
    private const double Scale    = 1000.0;

    private readonly double _spacing;
    private readonly double _tol;
    private readonly List<double> _rotDeg;
    private readonly PackingSortMode _sortMode;
    private readonly int _seed;
    private readonly List<SheetData> _sheets;

    // ── Evolution knobs (2026-06-06 SLM evolution). Every default reproduces the
    // original greedy NFP-BLF byte-for-byte, so the legacy 7-arg constructor is
    // unchanged behaviour. _score=BottomLeft means the cross-rotation ranking key
    // is exactly p_y then p_x (identical to the original). Compaction and
    // reinsertion are no-ops when their flags are false. See
    // outputs/2026-06-06/packing_slm_evolution/SYNTHESIS_2D.md. ──
    private readonly PlacementScore _score;
    private readonly bool _enableCompaction;
    private readonly int  _maxCompactionPasses;
    private readonly bool _enableReinsertion;
    private readonly int  _maxOuterRounds;
    private readonly bool _multiStart;
    private readonly bool _verifyOverlap;
    private readonly bool _gls;
    private readonly double _epsRel;

    public IrregularSheetFillNfpBlf(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double> rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        int seed)
        : this(sheetOutlines, sheetHoles, spacing, rotationsDeg, tolerance, sortMode, seed,
               PlacementScore.BottomLeft, false, 0, false, 0, false)
    {
    }

    public IrregularSheetFillNfpBlf(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double> rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        int seed,
        PlacementScore score,
        bool enableCompaction,
        int maxCompactionPasses,
        bool enableReinsertion,
        int maxOuterRounds,
        bool multiStart = false,
        bool gls = false)
    {
        _tol     = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        _spacing = Math.Max(0.0, spacing);
        _sortMode = sortMode;
        _seed     = seed;
        _score                = score;
        _enableCompaction     = enableCompaction;
        _maxCompactionPasses  = Math.Max(0, maxCompactionPasses);
        _enableReinsertion    = enableReinsertion;
        _maxOuterRounds       = Math.Max(0, maxOuterRounds);
        _multiStart           = multiStart;
        // The Minkowski-sum NFP is exact for convex parts but can admit a small
        // overlap for CONCAVE parts (the no-fit polygon of a concave part is not
        // captured by a single Minkowski sum). Whenever any evolution feature is
        // on we add a real polygon-intersection verify so the evolved path is
        // 0-overlap by REJECTION even on concave parts. Legacy path (all flags
        // off) keeps the original behaviour byte-for-byte (no verify).
        _verifyOverlap        = enableCompaction || enableReinsertion || multiStart || gls;
        _gls                  = gls;
        _epsRel               = _tol;

        var rotList = rotationsDeg?.Where(RhinoMath.IsValidDouble).Distinct().ToList() ?? new List<double>();
        if (rotList.Count == 0) rotList.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        _rotDeg = rotList;

        var outlines = sheetOutlines.ToList();
        _sheets = new List<SheetData>(outlines.Count);
        for (var i = 0; i < outlines.Count; i++)
        {
            var holes = i < sheetHoles.Count ? sheetHoles[i] : (IReadOnlyList<Curve>)Array.Empty<Curve>();
            var sd = PrepareSheet(outlines[i], holes, _sheets.Count);
            if (sd != null) _sheets.Add(sd);
        }
    }

    // ─── Public entry point ──────────────────────────────────────────────────

    public PackingResult Pack(IEnumerable<Curve> inputCurves, CancellationToken ct = default)
    {
        var sw     = Stopwatch.StartNew();
        var result = new PackingResult();

        foreach (var s in _sheets)
        {
            result.SheetPreviewCurves.Add(s.OuterRhinoCurve.DuplicateCurve());
            result.SheetPreviewCurves.AddRange(s.HoleRhinoCurves.Select(c => c.DuplicateCurve()));
        }

        if (_sheets.Count == 0)
        {
            result.Report = "No valid sheet outlines.";
            result.RuntimeMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }

        var input = inputCurves?.ToList() ?? new List<Curve>();
        result.InputCount = input.Count;

        var parts = new List<PartData>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            var pd = PreparePart(input[i], i);
            if (pd != null) parts.Add(pd);
        }
        result.PreparedCount = parts.Count;
        result.InvalidCount  = result.InputCount - result.PreparedCount;

        // Placement. Single-order (legacy, byte-identical) or multi-start: run the
        // greedy over several part orders and keep the order that places the most
        // parts, breaking ties by the tightest used bounding box (higher covUsed).
        // Multi-start is the measured lever (the BL objective makes plain
        // compaction a no-op after BL greedy; reordering is what moves density).
        Dictionary<int, List<Placed>> placedBySheet;
        List<PartData> unplacedParts;
        if (!_multiStart)
        {
            placedBySheet = PlaceAll(parts, _sortMode, result, ct, out unplacedParts);
        }
        else
        {
            placedBySheet = null; unplacedParts = null;
            int bestCount = -1; double bestUsed = double.MaxValue;
            foreach (var ord in MultiStartOrders())
            {
                var pl = PlaceAll(parts, ord, result, ct, out var un);
                int cnt = 0; foreach (var kv in pl) cnt += kv.Value.Count;
                double used = UsedBBoxScaled(pl);
                if (cnt > bestCount || (cnt == bestCount && used < bestUsed - 1e-6))
                {
                    bestCount = cnt; bestUsed = used; placedBySheet = pl; unplacedParts = un;
                    result.OptimizationRuns++;
                }
            }
            if (placedBySheet == null)
                placedBySheet = PlaceAll(parts, _sortMode, result, ct, out unplacedParts);
        }

        // Emit placed parts in global placement order (Seq), then unplaced.
        var allPlaced = placedBySheet.Values.SelectMany(x => x).OrderBy(p => p.Seq).ToList();
        foreach (var pl in allPlaced)
            EmitPlacement(pl.Part, pl, _sheets.First(s => s.Index == pl.Sheet), result);
        foreach (var up in unplacedParts)
        {
            result.UnplacedCurves.Add(up.SourceCurve.DuplicateCurve());
            result.FailureReasons.Add("No feasible position found.");
        }

        result.RuntimeMilliseconds = sw.ElapsedMilliseconds;
        result.Report =
            $"Freeform Sheet Nest (exact NFP-BLF) — Placed: {result.PackedCurves.Count}, " +
            $"Unplaced: {result.UnplacedCurves.Count}, Invalid: {result.InvalidCount}, " +
            $"Sheets: {_sheets.Count}, FeasibleRegions: {result.FeasibleRegionCount}, " +
            $"NfpBuilt: {result.CollisionCheckCount}, Runtime: {result.RuntimeMilliseconds} ms";
        return result;
    }

    // One greedy first-fit pass for a given part order, followed by the optional
    // compaction + reinsertion sweeps. Emission is deferred to the caller; Seq
    // records global placement order so the emitted order is stable.
    private Dictionary<int, List<Placed>> PlaceAll(
        List<PartData> parts, PackingSortMode order, PackingResult result,
        CancellationToken ct, out List<PartData> unplaced)
    {
        var ordered = SortParts(parts, order);
        var placedBySheet = new Dictionary<int, List<Placed>>();
        foreach (var s in _sheets) placedBySheet[s.Index] = new List<Placed>();
        unplaced = new List<PartData>();
        int seq = 0;
        foreach (var part in ordered)
        {
            ct.ThrowIfCancellationRequested();
            Placed best = null; int bestSheet = -1;
            foreach (var sheet in _sheets)
            {
                var p = FindBestPlacement(part, sheet, placedBySheet[sheet.Index], result);
                if (p != null) { best = p; bestSheet = sheet.Index; break; }
            }
            if (best == null) { unplaced.Add(part); continue; }
            best.Part = part; best.Sheet = bestSheet; best.Seq = seq++;
            placedBySheet[bestSheet].Add(best);
        }
        if (_enableCompaction || _enableReinsertion || _gls)
            CompactAndReinsert(placedBySheet, unplaced, ordered.Count, result, ct);
        return placedBySheet;
    }

    // Deterministic multi-start order family (area / max-dim / width / height
    // descending). Each is a full greedy pass; the best is kept. This realises
    // PRISMA Rank 3 (order search) and is the lever that actually raises density.
    private static PackingSortMode[] MultiStartOrders() => new[]
    {
        PackingSortMode.AreaDescending,
        PackingSortMode.MaxDimensionDescending,
        PackingSortMode.WidthDescending,
        PackingSortMode.HeightDescending,
    };

    private static double UsedBBoxScaled(Dictionary<int, List<Placed>> placedBySheet)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (var kv in placedBySheet)
            foreach (var p in kv.Value)
            {
                any = true;
                if (p.MinX < minX) minX = p.MinX; if (p.MinY < minY) minY = p.MinY;
                if (p.MaxX > maxX) maxX = p.MaxX; if (p.MaxY > maxY) maxY = p.MaxY;
            }
        return any ? Math.Max(0.0, (maxX - minX) * (maxY - minY)) : double.MaxValue;
    }

    // ─── Placement search ────────────────────────────────────────────────────

    private Placed FindBestPlacement(PartData part, SheetData sheet, List<Placed> placed, PackingResult result)
    {
        Placed best = null;
        foreach (var deg in _rotDeg)
        {
            var rot = RotateNormalized(part.LoopAtOrigin, deg);
            // Inner-fit region (conservative erosion of outer by hull(B)).
            var ifp = ComputeIfp(sheet.OuterScaled, rot);
            if (ifp.Count == 0) continue;
            if (_spacing > _tol)
            {
                ifp = Clipper2Adapter.InflateLoops(ifp, -_spacing * Scale);
                if (ifp.Count == 0) continue;
            }

            // Obstacles: holes (always) + placed parts whose reach can hit the IFP.
            var ifpBox = LoopsBounds(ifp);
            // reach must be in SCALED space: rot.W/H are model-space extents (= (max-min)/Scale,
            // see RotPart), but ifpBox and pp.Max/Min below are scaled (x Scale). Comparing a
            // model-space reach against scaled coords understates it ~1000x, so the cull drops
            // placed parts from the obstacle set, their NFP is never subtracted, and a BL vertex
            // can land inside a placed part -> real overlap (worst in the default no-verify ctor).
            var reach  = Math.Max(rot.W, rot.H) * Scale;
            var obstacles = new List<List<(double X, double Y)>>();
            foreach (var hole in sheet.HolesScaled)
            {
                var nfp = Clipper2Adapter.MinkowskiSum(hole, rot.Refl);
                if (nfp.Count > 0) obstacles.AddRange(nfp);
            }
            foreach (var pp in placed)
            {
                if (pp.MaxX + reach < ifpBox.minX || ifpBox.maxX < pp.MinX - reach ||
                    pp.MaxY + reach < ifpBox.minY || ifpBox.maxY < pp.MinY - reach)
                    continue;
                var nfp = Clipper2Adapter.MinkowskiSum(pp.AbsScaled, rot.Refl);
                if (nfp.Count > 0) obstacles.AddRange(nfp);
            }
            result.CollisionCheckCount += obstacles.Count;

            List<List<(double X, double Y)>> feasible;
            if (obstacles.Count == 0)
            {
                feasible = ifp;
            }
            else
            {
                var blocked = UnionAll(obstacles);
                if (_spacing > _tol) blocked = Clipper2Adapter.InflateLoops(blocked, _spacing * Scale);
                feasible = Clipper2Adapter.DifferenceLoops(ifp, blocked);
            }
            result.FeasibleRegionCount++;
            if (feasible.Count == 0) continue;

            // Bottom-left vertex of the feasible region (min (y, x)).
            if (!BottomLeftVertex(feasible, out var bx, out var by)) continue;
            var px = bx / Scale;
            var py = by / Scale;

            // Build the placed loop now so the evolved path can verify the real
            // geometry against the no-fit-polygon result (concave safety net).
            var absScaled = TranslateLoop(rot.Loop, bx, by);
            if (_verifyOverlap && OverlapsPlaced(absScaled, placed)) continue;

            // Cross-rotation ranking key. BottomLeft = p_y (identical to legacy
            // since _epsRel == _tol). LowestGravityCenter adds the rotated part's
            // centroid Y so a flatter, lower-gravity rotation wins even at a
            // slightly higher reference Y.
            double primary = _score == PlacementScore.LowestGravityCenter ? py + rot.CYModel : py;

            if (best == null || primary < best.Primary - _epsRel ||
                (Math.Abs(primary - best.Primary) <= _epsRel && px < best.RefX - _epsRel))
            {
                var bbox = LoopBounds(absScaled);
                best = new Placed
                {
                    RefX = px, RefY = py, Deg = deg, Primary = primary,
                    RotMinX = rot.RotMinX, RotMinY = rot.RotMinY,
                    AbsScaled = absScaled,
                    MinX = bbox.minX, MinY = bbox.minY, MaxX = bbox.maxX, MaxY = bbox.maxY,
                };
            }
        }
        return best;
    }

    // ─── Compaction + reinsertion (2026-06-06 SLM evolution) ───────────────────
    //
    // Gravitational compaction (discrete Li-Milenkovic redrop): hold every part
    // but i fixed and re-place i at the score-minimizing vertex of its feasible
    // region; accept only if its ranking key strictly drops by >= _epsRel. Each
    // accepted move keeps 0-overlap by construction (the new position lies in the
    // feasible region built against the other parts) and strictly lowers the
    // global potential Phi = sum_i key_i, which is bounded below, so the sweep
    // terminates. Reinsertion then tries to place parts the greedy pass dropped
    // into the space compaction freed. Both preserve the hard non-overlap and
    // containment guarantees of the base solver.
    private void CompactAndReinsert(
        Dictionary<int, List<Placed>> placedBySheet, List<PartData> unplaced,
        int seqBase, PackingResult result, CancellationToken ct)
    {
        if (_enableCompaction)
            foreach (var sheet in _sheets)
                CompactionSweep(sheet, placedBySheet[sheet.Index], result, ct);

        if ((_enableReinsertion || _gls) && unplaced.Count > 0)
        {
            int nextSeq = seqBase; // strictly above any greedy Seq (which is < seqBase)
            for (int round = 0; round < _maxOuterRounds && unplaced.Count > 0; round++)
            {
                ct.ThrowIfCancellationRequested();
                bool changed = false;
                for (int u = unplaced.Count - 1; u >= 0; u--)
                {
                    var part = unplaced[u];
                    foreach (var sheet in _sheets)
                    {
                        var cand = FindBestPlacement(part, sheet, placedBySheet[sheet.Index], result);
                        // GLS fallback: when the strict bottom-left feasible region is
                        // empty, try an overlap-minimization separation insert that may
                        // find a clean spot the BL-vertex-only search missed. Returns a
                        // verified 0-overlap placement or null (never disturbs placed parts).
                        if (cand == null && _gls)
                            cand = TryOverlapMinInsert(part, sheet, placedBySheet[sheet.Index], result, ct);
                        if (cand != null)
                        {
                            cand.Part = part; cand.Sheet = sheet.Index; cand.Seq = nextSeq++;
                            placedBySheet[sheet.Index].Add(cand);
                            unplaced.RemoveAt(u);
                            result.ReinsertionGains++;
                            changed = true;
                            break;
                        }
                    }
                }
                if (!changed) break;
                // Re-tighten after new parts land so the next round sees freed space.
                if (_enableCompaction)
                    foreach (var sheet in _sheets)
                        CompactionSweep(sheet, placedBySheet[sheet.Index], result, ct);
            }
        }
    }

    // GLS overlap-minimization insert (sparrow/jagua-rs separation mechanism,
    // applied to the ONE part being inserted so placed parts are never disturbed).
    // Start the reference at the inner-fit bottom-left, then walk it out of the
    // union of no-fit polygons by repeatedly projecting to the nearest NFP-boundary
    // point until it reaches a gap. Accept only a verified 0-overlap, contained
    // placement; return null otherwise. Monotone: only ever adds a clean part.
    private Placed TryOverlapMinInsert(PartData part, SheetData sheet, List<Placed> placed, PackingResult result, CancellationToken ct)
    {
        const int maxSep = 40;
        foreach (var deg in _rotDeg)
        {
            ct.ThrowIfCancellationRequested();
            var rot = RotateNormalized(part.LoopAtOrigin, deg);
            var ifp = ComputeIfp(sheet.OuterScaled, rot);
            if (ifp.Count == 0) continue;
            if (_spacing > _tol)
            {
                ifp = Clipper2Adapter.InflateLoops(ifp, -_spacing * Scale);
                if (ifp.Count == 0) continue;
            }

            // Union of no-fit polygons (holes always + placed parts) = the blocked
            // region the reference point must escape.
            var obstacles = new List<List<(double X, double Y)>>();
            foreach (var hole in sheet.HolesScaled)
            {
                var nfp = Clipper2Adapter.MinkowskiSum(hole, rot.Refl);
                if (nfp.Count > 0) obstacles.AddRange(nfp);
            }
            foreach (var pp in placed)
            {
                var nfp = Clipper2Adapter.MinkowskiSum(pp.AbsScaled, rot.Refl);
                if (nfp.Count > 0) obstacles.AddRange(nfp);
            }
            var blocked = obstacles.Count == 0 ? new List<List<(double X, double Y)>>() : UnionAll(obstacles);
            if (_spacing > _tol && blocked.Count > 0) blocked = Clipper2Adapter.InflateLoops(blocked, _spacing * Scale);

            // Seed the reference at the IFP bottom-left.
            if (!BottomLeftVertex(ifp, out var rx, out var ry)) continue;
            double eps = Math.Max(_tol * Scale * 2.0, 1.0);

            for (int k = 0; k < maxSep; k++)
            {
                bool inBlocked = blocked.Count > 0 && PointInLoops(blocked, rx, ry);
                bool inIfp = PointInLoops(ifp, rx, ry);
                if (!inBlocked && inIfp)
                {
                    var abs = TranslateLoop(rot.Loop, rx, ry);
                    if (!OverlapsPlaced(abs, placed))
                    {
                        var bb = LoopBounds(abs);
                        return new Placed
                        {
                            RefX = rx / Scale, RefY = ry / Scale, Deg = deg,
                            Primary = ry / Scale + (_score == PlacementScore.LowestGravityCenter ? rot.CYModel : 0.0),
                            RotMinX = rot.RotMinX, RotMinY = rot.RotMinY, AbsScaled = abs,
                            MinX = bb.minX, MinY = bb.minY, MaxX = bb.maxX, MaxY = bb.maxY,
                        };
                    }
                }
                // Project out of the blocked region to its nearest boundary point.
                if (inBlocked && NearestOnLoops(blocked, rx, ry, out var qx, out var qy))
                {
                    double dx = qx - rx, dy = qy - ry;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 1e-9) { rx += eps; ry += eps; }
                    else { rx = qx + eps * dx / d; ry = qy + eps * dy / d; }
                }
                else if (!inIfp && NearestOnLoops(ifp, rx, ry, out var ix, out var iy))
                {
                    // Outside containment: pull back toward the IFP.
                    rx = ix; ry = iy;
                }
                else break;
            }
        }
        return null;
    }

    // Even-odd point-in-polygon over multi-loop geometry (scaled space).
    private static bool PointInLoops(List<List<(double X, double Y)>> loops, double x, double y)
    {
        bool inside = false;
        foreach (var loop in loops)
        {
            int n = loop.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = loop[i].X, yi = loop[i].Y, xj = loop[j].X, yj = loop[j].Y;
                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi + 1e-30) + xi))
                    inside = !inside;
            }
        }
        return inside;
    }

    // Nearest point on the boundary edges of multi-loop geometry to (x,y).
    private static bool NearestOnLoops(List<List<(double X, double Y)>> loops, double x, double y, out double qx, out double qy)
    {
        qx = 0; qy = 0; double best = double.MaxValue; bool found = false;
        foreach (var loop in loops)
        {
            int n = loop.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double ax = loop[j].X, ay = loop[j].Y, bx = loop[i].X, by = loop[i].Y;
                double vx = bx - ax, vy = by - ay;
                double len2 = vx * vx + vy * vy;
                double t = len2 < 1e-12 ? 0.0 : ((x - ax) * vx + (y - ay) * vy) / len2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double px = ax + t * vx, py = ay + t * vy;
                double dd = (px - x) * (px - x) + (py - y) * (py - y);
                if (dd < best) { best = dd; qx = px; qy = py; found = true; }
            }
        }
        return found;
    }

    private void CompactionSweep(SheetData sheet, List<Placed> placed, PackingResult result, CancellationToken ct)
    {
        for (int pass = 0; pass < _maxCompactionPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();
            bool improved = false;
            for (int i = 0; i < placed.Count; i++)
            {
                var target = placed[i];
                var others = new List<Placed>(placed.Count - 1);
                for (int k = 0; k < placed.Count; k++) if (k != i) others.Add(placed[k]);

                var cand = FindBestPlacement(target.Part, sheet, others, result);
                if (cand != null && cand.Primary < target.Primary - _epsRel)
                {
                    cand.Part = target.Part; cand.Sheet = sheet.Index; cand.Seq = target.Seq;
                    placed[i] = cand;
                    result.CompactionMoves++;
                    improved = true;
                }
            }
            if (!improved) break;
        }
    }

    private List<List<(double X, double Y)>> ComputeIfp(List<(double X, double Y)> outerScaled, RotPart rot)
    {
        // IFP = intersection over hull vertices v of (outerScaled - v).
        var ifp = new List<List<(double X, double Y)>> { outerScaled };
        foreach (var (vx, vy) in rot.HullScaled)
        {
            var shifted = new List<(double X, double Y)>(outerScaled.Count);
            for (int i = 0; i < outerScaled.Count; i++)
                shifted.Add((outerScaled[i].X - vx, outerScaled[i].Y - vy));
            ifp = Clipper2Adapter.IntersectLoops(ifp, new List<IReadOnlyList<(double X, double Y)>> { shifted });
            if (ifp.Count == 0) break;
        }
        return ifp;
    }

    private static List<List<(double X, double Y)>> UnionAll(List<List<(double X, double Y)>> loops)
    {
        if (loops.Count == 1) return new List<List<(double X, double Y)>> { loops[0] };
        var acc = new List<List<(double X, double Y)>> { loops[0] };
        for (int i = 1; i < loops.Count; i++)
            acc = Clipper2Adapter.UnionLoops(acc, new List<IReadOnlyList<(double X, double Y)>> { loops[i] });
        return acc;
    }

    private static bool BottomLeftVertex(List<List<(double X, double Y)>> loops, out double bx, out double by)
    {
        bx = 0; by = 0; var found = false;
        foreach (var loop in loops)
            foreach (var (x, y) in loop)
                if (!found || y < by || (y == by && x < bx)) { bx = x; by = y; found = true; }
        return found;
    }

    // Real polygon-intersection check of a candidate placement against the parts
    // already placed (scaled space). The NFP says "feasible" but for concave
    // parts the Minkowski sum can miss a pocket; this verifies the actual
    // geometry so the evolved path stays 0-overlap by rejection. Threshold is the
    // same 1e-4 model-area the benchmark uses, scaled by Scale^2.
    private bool OverlapsPlaced(List<(double X, double Y)> cand, List<Placed> placed)
    {
        if (placed.Count == 0) return false;
        var cb = LoopBounds(cand);
        double thr = (_tol * Scale) * (_tol * Scale);
        foreach (var pp in placed)
        {
            if (pp.MaxX < cb.minX || cb.maxX < pp.MinX || pp.MaxY < cb.minY || cb.maxY < pp.MinY) continue;
            var inter = Clipper2Adapter.IntersectLoops(
                new List<List<(double X, double Y)>> { cand },
                new List<IReadOnlyList<(double X, double Y)>> { pp.AbsScaled });
            double a = 0;
            foreach (var loop in inter) a += Math.Abs(SignedArea(loop));
            if (a > thr) return true;
        }
        return false;
    }

    // ─── Emission (transform composition mirrors V506's root-cause-fixed order) ─

    private void EmitPlacement(PartData part, Placed best, SheetData sheet, PackingResult result)
    {
        var rotTx  = Transform.Rotation(best.Deg * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
        var normTx = Transform.Translation(-best.RotMinX, -best.RotMinY, 0);
        var moveTx = Transform.Translation(best.RefX, best.RefY, 0);
        var wts    = sheet.WorkToSheet;

        var outCurve = part.SourceCurve.DuplicateCurve();
        outCurve.Transform(part.PlaneToWork);   // 1. source plane → WorkXY
        outCurve.Transform(part.NormalizeTx);    // 2. bbox min → origin
        outCurve.Transform(rotTx);               // 3. rotate around origin
        outCurve.Transform(normTx);              // 4. undo rotation bbox shift
        outCurve.Transform(moveTx);              // 5. place at packed position in WorkXY
        outCurve.Transform(wts);                 // 6. WorkXY → sheet plane

        var compound = wts * moveTx * normTx * rotTx * part.NormalizeTx * part.PlaneToWork;

        result.PackedCurves.Add(outCurve);
        result.Transforms.Add(compound);
        result.SourceIndices.Add(part.SourceIndex);
        result.SheetIndices.Add(sheet.Index);
    }

    // ─── Preparation ───────────────────────────────────────────────────────

    private SheetData PrepareSheet(Curve outer, IReadOnlyList<Curve> holes, int index)
    {
        if (outer == null || !outer.IsClosed) return null;
        var ptol = Math.Max(_tol, 0.01);
        if (!outer.IsPlanar(ptol) || !outer.TryGetPlane(out var plane, ptol))
        {
            if (!outer.TryGetPlane(out plane, Math.Max(ptol * 100, 1.0))) return null;
        }

        var toWork  = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var toSheet = Transform.PlaneToPlane(Plane.WorldXY, plane);

        var outerWork = outer.DuplicateCurve();
        outerWork.Transform(toWork);
        var outerLoop = CurveToLoop(outerWork);
        if (outerLoop == null || outerLoop.Count < 3) return null;

        var holeLoops       = new List<List<(double X, double Y)>>();
        var holeRhinoCurves = new List<Curve>();
        foreach (var hole in holes)
        {
            if (hole == null || !hole.IsClosed) continue;
            var hw = hole.DuplicateCurve();
            hw.Transform(toWork);
            var hl = CurveToLoop(hw);
            if (hl != null && hl.Count >= 3)
            {
                holeLoops.Add(ScaleLoop(hl));
                holeRhinoCurves.Add(hole.DuplicateCurve());
            }
        }

        return new SheetData
        {
            Index           = index,
            OuterScaled     = ScaleLoop(outerLoop),
            HolesScaled     = holeLoops,
            WorkToSheet     = toSheet,
            OuterRhinoCurve = outer.DuplicateCurve(),
            HoleRhinoCurves = holeRhinoCurves,
        };
    }

    private PartData PreparePart(Curve curve, int sourceIndex)
    {
        if (curve == null || !curve.IsClosed) return null;
        var ptol = Math.Max(_tol, 0.01);
        if (!curve.IsPlanar(ptol) || !curve.TryGetPlane(out var plane, ptol)) return null;

        var planeToWork = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var wc          = curve.DuplicateCurve();
        wc.Transform(planeToWork);

        var bb          = wc.GetBoundingBox(true);
        var normalizeTx = Transform.Translation(-bb.Min.X, -bb.Min.Y, 0);
        wc.Transform(normalizeTx);

        var loop = CurveToLoop(wc);
        if (loop == null || loop.Count < 3) return null;
        var area = Math.Abs(SignedArea(loop));
        if (area <= _tol * _tol) return null;

        return new PartData
        {
            SourceIndex = sourceIndex,
            SourceCurve = curve.DuplicateCurve(),
            LoopAtOrigin = ScaleLoop(loop),   // scaled, bbox-min at origin
            PlaneToWork = planeToWork,
            NormalizeTx = normalizeTx,
            Area = area,
            Width = bb.Max.X - bb.Min.X,
            Height = bb.Max.Y - bb.Min.Y,
        };
    }

    private List<PartData> SortParts(List<PartData> parts) => SortParts(parts, _sortMode);

    private List<PartData> SortParts(List<PartData> parts, PackingSortMode mode)
    {
        var rnd = _seed == 0 ? null : new Random(_seed);
        IEnumerable<PartData> q = mode switch
        {
            PackingSortMode.UserOrder              => parts.OrderBy(p => p.SourceIndex),
            PackingSortMode.WidthDescending        => parts.OrderByDescending(p => p.Width).ThenBy(_ => rnd?.Next() ?? 0),
            PackingSortMode.HeightDescending       => parts.OrderByDescending(p => p.Height).ThenBy(_ => rnd?.Next() ?? 0),
            PackingSortMode.MaxDimensionDescending => parts.OrderByDescending(p => Math.Max(p.Width, p.Height)).ThenBy(_ => rnd?.Next() ?? 0),
            _                                      => parts.OrderByDescending(p => p.Area).ThenBy(_ => rnd?.Next() ?? 0)
        };
        return q.ToList();
    }

    // ─── Geometry helpers ────────────────────────────────────────────────────

    private List<(double X, double Y)> CurveToLoop(Curve curve)
    {
        IList<Point3d> pts = null;
        if (curve.TryGetPolyline(out var pl))
        {
            pts = pl;
        }
        else
        {
            var chord = Math.Max(_tol, 1e-3);
            var poly  = curve.ToPolyline(chord, Math.PI / 90.0, 0, 0);
            if (poly != null && poly.TryGetPolyline(out var pl2) && pl2.Count >= 4)
                pts = pl2;
            else
            {
                var divPar = curve.DivideByCount(Math.Min(MaxVerts, 128), false);
                if (divPar == null || divPar.Length < 3) return null;
                var tmp = new List<Point3d>(divPar.Length);
                foreach (var t in divPar) tmp.Add(curve.PointAt(t));
                pts = tmp;
            }
        }

        var n = pts.Count;
        if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < _tol) n--;
        if (n < 3) return null;

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
        return loop;
    }

    private static List<(double X, double Y)> ScaleLoop(List<(double X, double Y)> loop)
    {
        var outl = new List<(double X, double Y)>(loop.Count);
        for (int i = 0; i < loop.Count; i++) outl.Add((loop[i].X * Scale, loop[i].Y * Scale));
        return outl;
    }

    private RotPart RotateNormalized(List<(double X, double Y)> loopScaled, double deg)
    {
        var rad = deg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var n   = loopScaled.Count;
        var rx  = new double[n];
        var ry  = new double[n];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i < n; i++)
        {
            var x = cos * loopScaled[i].X - sin * loopScaled[i].Y;
            var y = sin * loopScaled[i].X + cos * loopScaled[i].Y;
            rx[i] = x; ry[i] = y;
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y;
        }
        var loop = new List<(double X, double Y)>(n);
        var refl = new List<(double X, double Y)>(n);
        for (var i = 0; i < n; i++)
        {
            var x = rx[i] - minX; var y = ry[i] - minY;
            loop.Add((x, y));
            refl.Add((-x, -y));
        }
        var hull = ConvexHull(loop);
        return new RotPart
        {
            Loop = loop, Refl = refl, HullScaled = hull,
            RotMinX = minX / Scale, RotMinY = minY / Scale,
            W = (maxX - minX) / Scale, H = (maxY - minY) / Scale,
            CYModel = CentroidYScaled(loop) / Scale,   // centroid Y relative to bbox-min origin (LGC scoring)
        };
    }

    private static double CentroidYScaled(List<(double X, double Y)> loop)
    {
        double a = 0, cy = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var j = (i + 1) % loop.Count;
            double cross = loop[i].X * loop[j].Y - loop[j].X * loop[i].Y;
            a  += cross;
            cy += (loop[i].Y + loop[j].Y) * cross;
        }
        if (Math.Abs(a) < 1e-9)
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var (_, y) in loop) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
            return 0.5 * (minY + maxY);
        }
        return cy / (3.0 * a);   // Cy = sum (y_i+y_{i+1})*cross / (3 * sum cross)
    }

    private static List<(double X, double Y)> ConvexHull(List<(double X, double Y)> pts)
    {
        var p = pts.Distinct().OrderBy(a => a.X).ThenBy(a => a.Y).ToList();
        if (p.Count < 3) return new List<(double X, double Y)>(p);
        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var lower = new List<(double X, double Y)>();
        foreach (var pt in p)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], pt) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(pt);
        }
        var upper = new List<(double X, double Y)>();
        for (int i = p.Count - 1; i >= 0; i--)
        {
            var pt = p[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], pt) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(pt);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static List<(double X, double Y)> TranslateLoop(List<(double X, double Y)> loop, double dx, double dy)
    {
        var outl = new List<(double X, double Y)>(loop.Count);
        for (int i = 0; i < loop.Count; i++) outl.Add((loop[i].X + dx, loop[i].Y + dy));
        return outl;
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

    private static (double minX, double minY, double maxX, double maxY) LoopBounds(List<(double X, double Y)> loop)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (x, y) in loop)
        {
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y;
        }
        return (minX, minY, maxX, maxY);
    }

    private static (double minX, double minY, double maxX, double maxY) LoopsBounds(List<List<(double X, double Y)>> loops)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var loop in loops)
            foreach (var (x, y) in loop)
            {
                if (x < minX) minX = x; if (y < minY) minY = y;
                if (x > maxX) maxX = x; if (y > maxY) maxY = y;
            }
        return (minX, minY, maxX, maxY);
    }

    // ─── Inner types ──────────────────────────────────────────────────────────

    private sealed class SheetData
    {
        public int Index;
        public List<(double X, double Y)> OuterScaled;
        public List<List<(double X, double Y)>> HolesScaled;
        public Transform WorkToSheet;
        public Curve OuterRhinoCurve;
        public List<Curve> HoleRhinoCurves;
    }

    private sealed class PartData
    {
        public int SourceIndex;
        public Curve SourceCurve;
        public List<(double X, double Y)> LoopAtOrigin;  // scaled, bbox-min at origin
        public Transform PlaneToWork;
        public Transform NormalizeTx;
        public double Area, Width, Height;
    }

    private sealed class RotPart
    {
        public List<(double X, double Y)> Loop;        // scaled, bbox-min at origin
        public List<(double X, double Y)> Refl;        // reflected through origin
        public List<(double X, double Y)> HullScaled;  // convex hull verts (scaled)
        public double RotMinX, RotMinY;                // model-space rotation bbox min
        public double W, H;                            // model-space rotated extents
        public double CYModel;                         // centroid Y (model units, from bbox-min)
    }

    private sealed class Placed
    {
        public double RefX, RefY, Deg, RotMinX, RotMinY;
        public List<(double X, double Y)> AbsScaled;   // placed loop, scaled, absolute
        public double MinX, MinY, MaxX, MaxY;          // scaled bbox
        public double Primary;                         // cross-rotation ranking key (BL=p_y, LGC=p_y+cY)
        public PartData Part;                          // back-ref for compaction/reinsertion redrop
        public int Sheet;                              // sheet index this part is placed on
        public int Seq;                                // global placement order (emit order)
    }
}
