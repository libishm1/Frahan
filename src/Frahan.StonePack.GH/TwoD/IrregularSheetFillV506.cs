using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Frahan.Core;
using Frahan.Surface;
using Rhino;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Version 5.0.6 freeform packing solver.
///
/// Root-cause fix over V3: V3 composed all transforms into one compound with
///   part.PartToWork * rotation * postRotNorm * translation * sheet.WorkToSheet
/// In Rhino's column-vector (right-to-left) convention, this applies WorkToSheet
/// FIRST and PartToWork LAST — the exact opposite of what is intended.  The pure-math
/// packing positions were always correct; only the Rhino curve output was placed in
/// the wrong location ("curves off in the distance").
///
/// Fix: every transform is applied to the output curve individually, one call at a time.
/// Each outCurve.Transform(T) call does p' = T·p which is unambiguous.  The compound
/// stored in the Transforms output is built in the matching right-to-left order so that
/// applying the single compound gives the same result as the sequential calls.
///
/// All V3 freeform improvements are retained:
///   - Adaptive ToPolyline discretisation (2° / chord-height), 256 verts max.
///   - Interior-filtered candidate grid (PointInPoly pre-filter) — fixes organic sheets.
///   - Spacing enforced via MinDistPolys in ContainedInSheet (no Curve.Offset needed).
///   - Relaxed IsPlanar tolerance for freeform curves drawn slightly off-plane.
/// MaxValidPerRot raised from 8 to 16 to improve coverage on organic sheets.
/// </summary>
public sealed class IrregularSheetFillV506
{
    private const int ExactMaxVerts  = 256;
    private const int MaxValidPerRot = 16;
    private const int MaxNfpVerts    = 24;

    // Boundary-aware mode (Half B, 2026-05-05). Solver-internal bucket sizes;
    // not exposed as user inputs to keep the unified component under the
    // 5-input fold-in budget. See wiki/algorithms/packing_2D/intent.md.
    private const double BoundaryAngleBucketDeg  = 15.0;
    private const double BoundaryCurvBucket      = 0.1;
    private const int    BoundaryLengthRadius    = 1;
    private const int    BoundaryAngleRadius     = 1;
    private const int    MaxSnapRotationsPerPart = 4;
    // Half D/H (2026-05-06): anchor diversity. Each part picks one of
    // the top-K matches indexed by (SourceIndex + seed), so identical-
    // shape parts spread their anchors across the boundary instead of
    // all converging on the single global-best interval. Half H raised
    // these from 8 → 16 because, after auto-detect (Half F/G) correctly
    // adds the inner curve as a hole, matches are split between outer
    // and hole intervals — a wider top-K is needed for both boundaries
    // to receive enough distinct anchors to look densely populated.
    private const int    BoundaryTopK            = 16;
    private const int    BoundaryDiversityK      = 16;
    // Half E/H (2026-05-06): aggressive boundary explore budget. Half H
    // raises these (256 → 512, 10000 → 30000) because user observed
    // less aggressive boundary wrapping in Half G compared to Half F:
    // when both outer and hole edges are simultaneously in the rail
    // index, each part needs a wider per-rotation search to find a slot
    // adjacent to its specific matched interval — otherwise the first
    // part hugs the boundary and subsequent parts have to fall back to
    // less-good positions. Geometric path keeps its lower budget;
    // phase 2 (interior) keeps user's _maxCandidates value untouched.
    private const int    MaxValidPerRotBoundary  = 512;
    private const int    BoundaryMaxCandidates   = 30000;

    private readonly double _spacing;
    private readonly double _tol;
    private readonly double _discretizationTol;
    private readonly List<double> _rotDeg;
    private readonly PackingSortMode _sortMode;
    private readonly PackingCornerMode _cornerMode;
    private readonly int _seed;
    private readonly int _maxCandidates;
    private readonly int _boundaryMode;
    private readonly double _minBoundaryAffinity;
    private readonly double _trimTolerance;
    private readonly List<SheetData> _sheets;
    // Raw constructor inputs, retained so the plain geometric mode can delegate
    // to the V2 engine (which packs strictly better on simple/rectangular sheets;
    // benchmark 2026-06-05: V506 14/24 vs V2 24/24 on the same parts). V506's own
    // path is kept for boundary-affinity / trim / auto-nested-hole modes.
    private readonly List<Curve> _rawOutlines;
    private readonly IReadOnlyList<IReadOnlyList<Curve>> _rawHoles;
    // 2026-06-06 SLM evolution: when true the plain geometric mode routes to the
    // EVOLVED exact Clipper2 NFP-BLF (multi-start order + compaction + reinsertion
    // + concave overlap-verify) instead of V2. Measured tighter (covUsed up) and
    // places more parts when oversubscribed, at 0 overlap. Default false keeps the
    // current V2 delegation behaviour byte-for-byte. Boundary/trim/auto-hole modes
    // keep V506's own path regardless.
    private readonly bool _qualityNfp;
    // Test-only escape hatch: set true to run V506's OWN packing path even in plain
    // mode (bypasses the V2 delegation). Used by the harness to A/B the own-path vs
    // the delegated path. Default false = production (plain mode delegates to V2).
    internal static bool DisableV2Delegation;
    // Half J: overlap-pair tracking. Filled as parts get accepted with
    // overlap; consumed by the trim post-pass. Items: (earlierPackedIdx,
    // laterPackedIdx, maxPenetration). Earlier-placed wins, later gets
    // boolean-differenced.
    private readonly List<(int earlierPackedIdx, int laterPackedIdx, double maxPen)> _overlapPairs = new();
    // Per-sheet running list of placed parts paired with their PackedCurves
    // index, so the overlap tracker can scan against existing placements
    // and emit (earlier, later) pairs in PackedCurves index space.
    private readonly Dictionary<int, List<int>> _placedPackedIdxBySheet = new();

    public IrregularSheetFillV506(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        PackingCornerMode cornerMode,
        int seed,
        int maxCandidates,
        int boundaryMode = 0,
        double minBoundaryAffinity = 0.5,
        double discretizationTolerance = -1.0,
        double trimTolerance = 0.0,
        bool qualityNfp = false)
    {
        _qualityNfp = qualityNfp;
        _tol                 = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        // Half J: trim-aware mode. 0 = trim off (legacy strict no-overlap).
        // > 0 = max penetration depth to allow at part-to-part collisions
        // before placement is rejected. Overlapping placements are recorded
        // and post-processed via Curve.CreateBooleanDifference. Boundary
        // (sheet outline + holes) is NEVER trimmed — only part-to-part.
        _trimTolerance       = Math.Max(0.0, trimTolerance);
        // Half C: discretization tolerance is the ToPolyline tolerance for
        // both sheet and part curve→polyline conversion. -1 (default) means
        // "use the geometric tolerance" — matches Half A/B behaviour. > 0
        // lets users dial in coarser/finer polylines independently of the
        // geometric tolerance used for containment + collision checks.
        _discretizationTol   = discretizationTolerance > 0 ? discretizationTolerance : _tol;
        _spacing             = Math.Max(0.1, spacing);
        _sortMode            = sortMode;
        _cornerMode          = cornerMode;
        _seed                = seed;
        _maxCandidates       = Math.Max(30, maxCandidates <= 0 ? 300 : maxCandidates);
        _boundaryMode        = boundaryMode;
        _minBoundaryAffinity = Math.Max(0.0, Math.Min(1.0, minBoundaryAffinity));

        var rotList = rotationsDeg?.Where(RhinoMath.IsValidDouble).Distinct().ToList() ?? new List<double>();
        if (rotList.Count == 0) rotList.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        _rotDeg = rotList;

        var outlines = sheetOutlines.ToList();
        _rawOutlines = outlines;
        _rawHoles = sheetHoles;
        _sheets = new List<SheetData>(outlines.Count);
        for (var i = 0; i < outlines.Count; i++)
        {
            var holes = i < sheetHoles.Count ? sheetHoles[i] : (IReadOnlyList<Curve>)Array.Empty<Curve>();
            var sd = PrepareSheet(outlines[i], holes, i);
            if (sd != null) _sheets.Add(sd);
        }

        // Half F (2026-05-06): defensive UX. If the user wired multiple
        // sheet outlines where one is geometrically inside another, treat
        // the inner one as a hole of the outer instead of as a separate
        // sheet. Common wiring confusion: user puts both the outer and
        // the inner ellipse on Sheet Outlines (S) input rather than
        // wiring the inner one to Sheet Holes (H) input. Without this
        // fix, parts pack into the inner sheet too — visually appearing
        // to "ignore the hole".
        AutoDetectNestedHoles();
    }

    private int _autoDetectedHoleCount;

    private void AutoDetectNestedHoles()
    {
        _autoDetectedHoleCount = 0;
        if (_sheets.Count < 2) return;

        // Half G fix: containment must be tested in WORLD coords, NOT in
        // each sheet's own WorkXY frame (which is centered on that sheet's
        // centroid via PlaneToPlane). Half F's IsPolyContainedIn compared
        // Poly2d coords across frames, which produced false detections /
        // misplaced holes — visible as parts still crossing into the
        // inner ellipse.
        var toRemove = new HashSet<int>();
        var toAddAsHole = new List<(int outerIdx, int innerIdx)>();

        for (int i = 0; i < _sheets.Count; i++)
        {
            for (int j = 0; j < _sheets.Count; j++)
            {
                if (i == j || toRemove.Contains(i) || toRemove.Contains(j)) continue;
                if (IsCurveContainedIn(_sheets[j].OuterRhinoCurve, _sheets[i].OuterRhinoCurve))
                {
                    toAddAsHole.Add((i, j));
                    toRemove.Add(j);
                    _autoDetectedHoleCount++;
                }
            }
        }

        if (toRemove.Count == 0) return;

        // For each detected (outer, inner) pair, transform inner's original
        // Rhino curve into outer's WorkXY frame and rebuild Poly2d there
        // so the hole geometry sits at the correct position in outer's
        // frame for ContainedInSheet's PointInPoly checks.
        foreach (var (outerIdx, innerIdx) in toAddAsHole)
        {
            var outer = _sheets[outerIdx];
            var inner = _sheets[innerIdx];

            var ptol = Math.Max(_tol, 0.01);
            if (!outer.OuterRhinoCurve.TryGetPlane(out var outerPlane, ptol))
            {
                if (!outer.OuterRhinoCurve.TryGetPlane(out outerPlane, Math.Max(ptol * 100, 1.0)))
                    continue;
            }
            var toWorkOuter = Transform.PlaneToPlane(outerPlane, Plane.WorldXY);

            var innerInOuterWork = inner.OuterRhinoCurve.DuplicateCurve();
            innerInOuterWork.Transform(toWorkOuter);

            var holePoly = CurveToPoly2dAdaptive(innerInOuterWork);
            if (holePoly == null || holePoly.N < 3) continue;

            outer.Holes.Add(holePoly);
            outer.HoleRhinoCurves.Add(inner.OuterRhinoCurve);
            // OuterWorkCurve is populated only when boundary mode > 0.
            // When set, mirror the auto-detected hole into HoleWorkCurves
            // so BuildBoundaryIndexes includes it in the rail index.
            if (outer.OuterWorkCurve != null)
                outer.HoleWorkCurves.Add(innerInOuterWork);
        }

        // Rebuild sheet list with sequential indices.
        var rebuilt = new List<SheetData>(_sheets.Count - toRemove.Count);
        for (int i = 0; i < _sheets.Count; i++)
        {
            if (toRemove.Contains(i)) continue;
            _sheets[i].Index = rebuilt.Count;
            rebuilt.Add(_sheets[i]);
        }
        _sheets.Clear();
        _sheets.AddRange(rebuilt);
    }

    private bool IsCurveContainedIn(Curve inner, Curve outer)
    {
        // Use Rhino's Curve.Contains in world coords against outer's
        // fitted plane. 16-point sample along inner; all must be Inside
        // or Coincident for inner to be considered contained.
        var ptol = Math.Max(_tol, 0.01);
        if (!outer.TryGetPlane(out var plane, ptol))
        {
            if (!outer.TryGetPlane(out plane, Math.Max(ptol * 100, 1.0)))
                return false;
        }
        var ts = inner.DivideByCount(16, true);
        if (ts == null || ts.Length < 4) return false;
        foreach (var t in ts)
        {
            var pt = inner.PointAt(t);
            var c = outer.Contains(pt, plane, ptol);
            if (c != PointContainment.Inside && c != PointContainment.Coincident)
                return false;
        }
        return true;
    }

    // ─── Public entry point ──────────────────────────────────────────────────

    public PackingResult Pack(IEnumerable<Curve>? inputCurves, CancellationToken ct = default)
    {
        var sw     = Stopwatch.StartNew();
        var result = new PackingResult();

        // Plain geometric mode (no boundary-affinity, no trim, no auto-nested
        // holes): the V2 engine packs strictly better here. Benchmark 2026-06-05
        // on 24 parts: V506's own path placed 14/24 (46.3%), V2 placed 24/24
        // (60.0%), both overlap-free. Route plain packing through V2 (which is
        // fully holes-capable). V506's specialized path is retained below for
        // boundary modes, trim, and auto-detected nested holes.
        if (!DisableV2Delegation && _boundaryMode == 0 && _trimTolerance <= 0 && _autoDetectedHoleCount == 0)
        {
            if (_qualityNfp)
            {
                // Quality mode: route plain packing to the EVOLVED exact NFP-BLF
                // (multi-start + compaction + reinsertion + concave overlap-verify).
                // Holes are honoured exactly via the NFP obstacle path.
                var nfp = new IrregularSheetFillNfpBlf(
                    _rawOutlines, _rawHoles, _spacing, _rotDeg, _tol,
                    _sortMode, _seed,
                    PlacementScore.BottomLeft, true, 3, true, 2, true);
                return nfp.Pack(inputCurves, ct);
            }
            var v2 = new IrregularSheetFillV2(
                _rawOutlines, _rawHoles, _spacing, _rotDeg, _tol,
                _sortMode, _cornerMode, _seed, _maxCandidates);
            return v2.Pack(inputCurves, ct);
        }

        foreach (var s in _sheets)
        {
            result.SheetPreviewCurves.Add(s.OuterRhinoCurve.DuplicateCurve());
            result.SheetPreviewCurves.AddRange(s.HoleRhinoCurves.Select(c => c.DuplicateCurve()));
        }

        if (_sheets.Count == 0)
        {
            result.Report              = "No valid sheet outlines.";
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

        // Half C: deferred boundary build pass. Auto-tunes window length to
        // the median part edge length, then scores every part against every
        // sheet's index. Skipped entirely when boundary mode is off.
        // Half I extension (Mode 3): curve-division mode skips the rail
        // index + scoring entirely — placement uses direct LengthParameter
        // on the boundary curve. The index is still useful for the phase 2
        // fallback (parts that fail curve-division still go through the
        // existing matching path), but only when there's something to match
        // against.
        if (_boundaryMode > 0 && _boundaryMode != 3)
        {
            BuildBoundaryIndexes(parts);
            foreach (var part in parts)
            {
                try { ComputeBoundaryAffinity(part); }
                catch
                {
                    part.MaxEdgeAffinity = 0;
                    part.BestMatchedEdgeIndex = -1;
                    part.BestMatchedSheetIndex = -1;
                    part.BestMatchedInterval = null;
                }
            }
        }

        var rotCache = BuildRotationCache(parts);
        var ordered  = SortParts(parts);
        var sheetMap = _sheets.ToDictionary(s => s.Index);

        var placedBySheet = new Dictionary<int, List<PlacedPoly>>();
        var gridBySheet   = new Dictionary<int, Grid2d>();
        foreach (var s in _sheets)
        {
            placedBySheet[s.Index] = new List<PlacedPoly>();
            var span = Math.Max(s.BBoxMaxX - s.BBoxMinX, s.BBoxMaxY - s.BBoxMinY);
            gridBySheet[s.Index]   = new Grid2d(Math.Max(span / 20.0, _tol * 10));
        }

        // Half I extension (Mode 3): curve-division pre-pass. Distributes
        // parts uniformly along the outer + hole boundary arc length;
        // each part lands with its longest edge tangent to the curve at
        // the assigned position. Parts that don't fit (collision /
        // containment) fall through to the main loop below for a normal
        // matching-based attempt.
        var placedSourceIndices = new HashSet<int>();
        if (_boundaryMode == 3)
        {
            DoCurveDivisionPlacement(ordered, placedBySheet, gridBySheet, result, placedSourceIndices);
        }

        foreach (var part in ordered)
        {
            ct.ThrowIfCancellationRequested();

            // Half I (Mode 3): skip parts already placed by the curve-
            // division pre-pass. The placedBySheet / gridBySheet state
            // already reflects them so subsequent parts treat them as
            // collision targets correctly.
            if (placedSourceIndices.Contains(part.SourceIndex)) continue;

            PlacedPoly? best      = null;
            double      bestDeg   = 0;
            double      bestRMinX = 0;
            double      bestRMinY = 0;
            int         bestSheet = -1;

            // Half C: Mode 2 splits placement into a true two-phase pass.
            // Boundary-worthy parts try phase-1 candidates (boundary anchors
            // + already-placed-NFP only). Non-boundary-worthy parts try
            // phase-2 candidates (interior grid + IFP + placed-NFP). If the
            // phase-restricted attempt fails (band full / interior full),
            // fall back to ALL candidates so the part isn't unfairly
            // unplaced. Mode 0 and Mode 1 always use candidatePhase = 0.
            var targetPhase = 0;
            if (_boundaryMode == 2)
            {
                targetPhase = part.MaxEdgeAffinity >= _minBoundaryAffinity ? 1 : 2;
            }

            foreach (var sheet in _sheets)
            {
                var (placed, deg, rMinX, rMinY) = FindBestPlacement(
                    part, sheet, placedBySheet[sheet.Index], gridBySheet[sheet.Index], rotCache, result, targetPhase);
                if (placed != null)
                {
                    best      = placed;
                    bestDeg   = deg;
                    bestRMinX = rMinX;
                    bestRMinY = rMinY;
                    bestSheet = sheet.Index;
                    break;
                }
            }

            // Mode 2 fallback: phase-restricted placement failed, retry with
            // all candidate sources before giving up.
            if (best == null && _boundaryMode == 2 && targetPhase != 0)
            {
                foreach (var sheet in _sheets)
                {
                    var (placed, deg, rMinX, rMinY) = FindBestPlacement(
                        part, sheet, placedBySheet[sheet.Index], gridBySheet[sheet.Index], rotCache, result, 0);
                    if (placed != null)
                    {
                        best      = placed;
                        bestDeg   = deg;
                        bestRMinX = rMinX;
                        bestRMinY = rMinY;
                        bestSheet = sheet.Index;
                        break;
                    }
                }
            }

            if (best == null)
            {
                result.UnplacedCurves.Add(part.SourceCurve.DuplicateCurve());
                result.FailureReasons.Add("No valid position found.");
                continue;
            }

            var listIdx = placedBySheet[bestSheet].Count;
            best.ListIdx = listIdx;
            placedBySheet[bestSheet].Add(best);
            gridBySheet[bestSheet].Insert(listIdx, best.MinX, best.MinY, best.MaxX, best.MaxY);

            // Half J: track part-to-part overlaps for the trim post-pass.
            // We record (earlierPackedIdx, laterPackedIdx, maxPen) so the
            // post-pass can boolean-difference the later part by the
            // earlier one. Only fires when _trimTolerance > 0; otherwise
            // CollidesWithGrid would have rejected the placement above.
            if (_trimTolerance > 0)
            {
                if (!_placedPackedIdxBySheet.TryGetValue(bestSheet, out var sheetPackedIdxs))
                {
                    sheetPackedIdxs = new List<int>();
                    _placedPackedIdxBySheet[bestSheet] = sheetPackedIdxs;
                }
                var thisPackedIdx = result.PackedCurves.Count; // will be assigned below
                for (int p = 0; p < placedBySheet[bestSheet].Count - 1; p++)
                {
                    var earlier = placedBySheet[bestSheet][p];
                    var pen = MaxPenetrationDepth(
                        best.Poly, best.OriginX, best.OriginY,
                        earlier.Poly, earlier.OriginX, earlier.OriginY);
                    if (pen > _tol)
                    {
                        _overlapPairs.Add((sheetPackedIdxs[p], thisPackedIdx, pen));
                    }
                }
                sheetPackedIdxs.Add(thisPackedIdx);
            }

            // Build individual transforms.
            var rotTx  = Transform.Rotation(bestDeg * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
            var normTx = Transform.Translation(-bestRMinX, -bestRMinY, 0);
            var moveTx = Transform.Translation(best.OriginX, best.OriginY, 0);
            var wts    = sheetMap[bestSheet].WorkToSheet;

            // Apply step-by-step — avoids right-to-left matrix convention reversing the order.
            var outCurve = part.SourceCurve.DuplicateCurve();
            outCurve.Transform(part.PlaneToWork);   // 1. source plane → WorkXY
            outCurve.Transform(part.NormalizeTx);  // 2. bbox min → origin
            outCurve.Transform(rotTx);              // 3. rotate around origin
            outCurve.Transform(normTx);             // 4. undo rotation bbox shift
            outCurve.Transform(moveTx);             // 5. place at packed position in WorkXY
            outCurve.Transform(wts);               // 6. WorkXY → sheet plane

            // Compound for Transforms output. Right-to-left order matches step-by-step above:
            //   applying compound to a point v: wts*(moveTx*(normTx*(rotTx*(NormalizeTx*(PlaneToWork*v)))))
            var compound = wts * moveTx * normTx * rotTx * part.NormalizeTx * part.PlaneToWork;

            result.PackedCurves.Add(outCurve);
            result.Transforms.Add(compound);
            result.SourceIndices.Add(part.SourceIndex);
            result.SheetIndices.Add(bestSheet);
        }

        // Half J: trim post-pass. For each recorded overlap pair, boolean-
        // difference the later-placed curve by the earlier one. Earlier
        // wins (its geometry is preserved); later loses material at the
        // overlap. Output collected in result.TrimmedCurves (parallel to
        // PackedCurves) and TrimAdjacency (per-part list of trim-causing
        // earlier indices). When TrimTolerance == 0, the lists stay empty.
        if (_trimTolerance > 0)
        {
            ApplyTrimPostPass(result);
        }

        result.RuntimeMilliseconds = sw.ElapsedMilliseconds;

        // Half G: post-placement hole-violation counter. If ContainedInSheet
        // is doing its job, this stays at 0. > 0 indicates a real bug —
        // surface it loudly in the report so the user can flag it.
        var holeViolations = CountHoleViolations(placedBySheet);
        var totalHoles = 0;
        foreach (var s in _sheets) totalHoles += s.Holes.Count;
        var autoNote = _autoDetectedHoleCount > 0
            ? $", AutoNestedHoles: {_autoDetectedHoleCount}"
            : "";
        var violNote = holeViolations > 0
            ? $", HoleViolations: {holeViolations} (BUG — please report)"
            : ", HoleViolations: 0";

        result.Report =
            $"Freeform Sheet Pack V5.0.6 — Placed: {result.PackedCurves.Count}, " +
            $"Unplaced: {result.UnplacedCurves.Count}, Invalid: {result.InvalidCount}, " +
            $"Sheets: {_sheets.Count}{autoNote}, TotalHoles: {totalHoles}{violNote}, " +
            $"Candidates: {result.CandidateCount}, " +
            $"Collisions: {result.CollisionCheckCount}, Runtime: {result.RuntimeMilliseconds} ms";
        return result;
    }

    private void ApplyTrimPostPass(PackingResult result)
    {
        // Initialize parallel arrays: TrimmedCurves starts as duplicates of
        // PackedCurves. Adjacency starts as empty lists. We fill them by
        // processing recorded overlap pairs in placement order.
        var n = result.PackedCurves.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            result.TrimmedCurves.Add(result.PackedCurves[i].DuplicateCurve());
            result.TrimAdjacency.Add(new List<int>());
        }

        // Sort overlap pairs by laterPackedIdx so we accumulate trims from
        // multiple earlier neighbors onto the same later part in a stable
        // order (earlier-source first → later-source last). For the boolean
        // difference, we always carve the LATER curve (preserving the
        // earlier one's geometry).
        _overlapPairs.Sort((a, b) =>
        {
            var c = a.laterPackedIdx.CompareTo(b.laterPackedIdx);
            if (c != 0) return c;
            return a.earlierPackedIdx.CompareTo(b.earlierPackedIdx);
        });

        var planeTol = Math.Max(_tol, 0.001);
        foreach (var (earlier, later, _) in _overlapPairs)
        {
            if (later < 0 || later >= n) continue;
            if (earlier < 0 || earlier >= n) continue;

            var laterCurve = result.TrimmedCurves[later];
            var earlierCurve = result.TrimmedCurves[earlier];
            // Curve.CreateBooleanDifference requires planar closed curves
            // and a tolerance. Returns null/empty if the difference is
            // empty (later fully contained in earlier — rare with our
            // _trimTolerance gate but possible at edge cases).
            Curve[]? diff = null;
            try
            {
                diff = Curve.CreateBooleanDifference(laterCurve, earlierCurve, planeTol);
            }
            catch
            {
                // Boolean op rejected the input pair (non-planar after
                // transform, self-intersecting, etc.). Skip — the original
                // curve stays in TrimmedCurves.
                diff = null;
            }
            if (diff == null || diff.Length == 0) continue;

            // Pick the largest-area piece. Some boolean differences split
            // a curve into multiple disconnected pieces; we keep the
            // largest as the "trimmed part" and discard slivers.
            Curve? best = null;
            double bestArea = 0;
            foreach (var d in diff)
            {
                if (d == null || !d.IsClosed) continue;
                var area = AreaMassProperties.Compute(d)?.Area ?? 0;
                if (area > bestArea) { bestArea = area; best = d; }
            }
            if (best == null) continue;

            result.TrimmedCurves[later] = best;
            result.TrimAdjacency[later].Add(result.SourceIndices[earlier]);
            result.TrimEventCount++;
        }
    }

    private int CountHoleViolations(Dictionary<int, List<PlacedPoly>> placedBySheet)
    {
        var violations = 0;
        foreach (var sheet in _sheets)
        {
            if (sheet.Holes.Count == 0) continue;
            if (!placedBySheet.TryGetValue(sheet.Index, out var placedList)) continue;
            foreach (var pp in placedList)
            {
                foreach (var hole in sheet.Holes)
                {
                    if (!BBoxOverlap(pp.MinX, pp.MinY, pp.MaxX, pp.MaxY,
                                      hole.MinX, hole.MinY, hole.MaxX, hole.MaxY, 0)) continue;
                    var hit = false;
                    for (int i = 0; i < pp.Poly.N && !hit; i++)
                    {
                        if (PointInPoly(pp.Poly.Vx[i] + pp.OriginX, pp.Poly.Vy[i] + pp.OriginY,
                                        hole.Vx, hole.Vy, hole.N))
                            hit = true;
                    }
                    if (!hit && PointInPoly(pp.Poly.Cx + pp.OriginX, pp.Poly.Cy + pp.OriginY,
                                            hole.Vx, hole.Vy, hole.N))
                        hit = true;
                    if (hit) { violations++; break; }
                }
            }
        }
        return violations;
    }

    // ─── Sheet / part preparation ────────────────────────────────────────────

    private SheetData? PrepareSheet(Curve outer, IReadOnlyList<Curve> holes, int index)
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

        var outerPoly = CurveToPoly2dAdaptive(outerWork);
        if (outerPoly == null || outerPoly.N < 3) return null;

        var holePoly        = new List<Poly2d>();
        var holeRhinoCurves = new List<Curve>();
        foreach (var hole in holes)
        {
            if (hole == null || !hole.IsClosed) continue;
            var hw = hole.DuplicateCurve();
            hw.Transform(toWork);
            var hp = CurveToPoly2dAdaptive(hw);
            if (hp != null && hp.N >= 3) { holePoly.Add(hp); holeRhinoCurves.Add(hole.DuplicateCurve()); }
        }

        var bb = outerWork.GetBoundingBox(true);
        var sd = new SheetData
        {
            Index           = index,
            Outer           = outerPoly,
            Holes           = holePoly,
            WorkToSheet     = toSheet,
            OuterRhinoCurve = outer.DuplicateCurve(),
            HoleRhinoCurves = holeRhinoCurves,
            BBoxMinX        = bb.Min.X,
            BBoxMinY        = bb.Min.Y,
            BBoxMaxX        = bb.Max.X,
            BBoxMaxY        = bb.Max.Y,
        };

        // Boundary-aware mode (Half C): stash the WorkXY curves for the
        // deferred BuildBoundaryIndexes pass — we don't know the right
        // window length until parts have been discretised and we can see
        // their median edge length. The actual index gets built in Pack().
        if (_boundaryMode > 0)
        {
            sd.OuterWorkCurve = outerWork.DuplicateCurve();
            foreach (var hole in holeRhinoCurves)
            {
                var hw = hole.DuplicateCurve();
                hw.Transform(toWork);
                sd.HoleWorkCurves.Add(hw);
            }
        }

        return sd;
    }

    private PartData? PreparePart(Curve? curve, int sourceIndex)
    {
        if (curve == null || !curve.IsClosed) return null;
        var ptol = Math.Max(_tol, 0.01);
        if (!curve.IsPlanar(ptol) || !curve.TryGetPlane(out var plane, ptol)) return null;

        var planeToWork = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var wc          = curve.DuplicateCurve();
        wc.Transform(planeToWork);                    // step 1: source plane → WorkXY

        var bb          = wc.GetBoundingBox(true);
        var normalizeTx = Transform.Translation(-bb.Min.X, -bb.Min.Y, 0);
        wc.Transform(normalizeTx);                    // step 2: shift bbox min to origin

        var poly = CurveToPoly2dAdaptive(wc);
        if (poly == null || poly.N < 3 || poly.Area <= _tol * _tol) return null;

        var part = new PartData
        {
            SourceIndex   = sourceIndex,
            SourceCurve   = curve.DuplicateCurve(),
            ExactAtOrigin = poly,
            PlaneToWork   = planeToWork,
            NormalizeTx   = normalizeTx,
            Width         = poly.MaxX - poly.MinX,
            Height        = poly.MaxY - poly.MinY,
            Area          = poly.Area,
        };

        // Boundary-aware mode (Half C): build the FragmentDescriptor here so
        // BuildBoundaryIndexes can use median edge length to auto-tune the
        // sliding-window length for each sheet's index. Affinity scoring
        // happens in a later pass once the indexes exist.
        if (_boundaryMode > 0)
        {
            try
            {
                part.Descriptor = FragmentDescriptorBuilder.BuildFromCurve(
                    id: $"part_{part.SourceIndex}",
                    boundary: wc,
                    zoneId: 0,
                    discretisationTolerance: Math.Max(_discretizationTol, 0.01));
            }
            catch
            {
                part.Descriptor = null;
            }
        }

        return part;
    }

    private void ComputeBoundaryAffinity(PartData part)
    {
        var desc = part.Descriptor;
        if (desc == null) return;

        // Half I: collect all above-threshold matches and sort by
        // ANGULAR POSITION around the matched interval's sheet centroid
        // (not by score). Then pick using a golden-ratio stride for
        // uniform low-discrepancy distribution. Half D-H sorted by
        // score desc + picked top-K modulo, which clustered same-shape
        // parts on whichever section happened to score highest. The
        // angular sort gives true circumferential ordering — adjacent
        // entries are adjacent on the boundary — and the golden-ratio
        // stride spreads parts uniformly regardless of count.
        var allMatches = new List<(double score, int edgeIdx, int sheetIdx, BoundaryIntervalInfo iv, double angle)>();

        foreach (var sheet in _sheets)
        {
            if (sheet.BoundaryIndex == null) continue;

            var sheetCx = (sheet.BBoxMinX + sheet.BBoxMaxX) * 0.5;
            var sheetCy = (sheet.BBoxMinY + sheet.BBoxMaxY) * 0.5;

            var options = new MatchOptions
            {
                LengthBucketSize       = sheet.BoundaryLengthBucket,
                AngleBucketSizeDegrees = BoundaryAngleBucketDeg,
                CurvatureBucketSize    = BoundaryCurvBucket,
                LengthRadius           = BoundaryLengthRadius,
                AngleRadius            = BoundaryAngleRadius,
                PreserveZone           = true,
                TopK                   = BoundaryTopK,
                MinAffinityScore       = _minBoundaryAffinity,
            };

            var perEdge = BoundaryRailMatcher.MatchFragment(
                index: sheet.BoundaryIndex,
                fragment: desc,
                options: options,
                intervalToDescriptor: IntervalToEdgeDescriptor);

            for (int e = 0; e < perEdge.Count; e++)
            {
                foreach (var m in perEdge[e])
                {
                    var ivc = m.Interval.LocalBounds.Center;
                    var dx = ivc.X - sheetCx;
                    var dy = ivc.Y - sheetCy;
                    var angle = Math.Atan2(dy, dx); // [-π, π]
                    allMatches.Add((m.AffinityScore, e, sheet.Index, m.Interval, angle));
                }
            }
        }

        if (allMatches.Count == 0) return;

        // Sort: sheet first (so single-sheet runs are contiguous in the
        // list), then by angle ascending around that sheet's centroid,
        // then by score desc for a deterministic tie-break when multiple
        // matches sit at the same angle.
        allMatches.Sort((a, b) =>
        {
            var c = a.sheetIdx.CompareTo(b.sheetIdx);
            if (c != 0) return c;
            c = a.angle.CompareTo(b.angle);
            if (c != 0) return c;
            c = b.score.CompareTo(a.score);
            if (c != 0) return c;
            return a.edgeIdx.CompareTo(b.edgeIdx);
        });

        // Golden-ratio low-discrepancy stride: phase = ((SourceIndex +
        // seed) * 1/φ) mod 1, then map to [0, count). 1/φ ≈ 0.618 is
        // the most irrational multiplier — same property exploited by
        // sunflower-seed phyllotaxis to give the most uniform circular
        // distribution. With N parts and M matches sorted angularly,
        // each part lands at a distinct angular position spread evenly
        // around the boundary.
        const double InvGolden = 0.6180339887498949;
        double phase = ((part.SourceIndex + _seed) * InvGolden) % 1.0;
        if (phase < 0) phase += 1.0;
        int pickIdx = (int)(phase * allMatches.Count);
        if (pickIdx >= allMatches.Count) pickIdx = allMatches.Count - 1;
        if (pickIdx < 0) pickIdx = 0;
        var picked = allMatches[pickIdx];

        part.MaxEdgeAffinity = picked.score;
        part.BestMatchedEdgeIndex = picked.edgeIdx;
        part.BestMatchedSheetIndex = picked.sheetIdx;
        part.BestMatchedInterval = picked.iv;

        // Snap rotation: align the SELECTED matched edge with the SELECTED
        // matched interval's tangent — not the global-best one. Half J+:
        // no Math.Round here. The AngleKey quantization in the rotation
        // cache (0.01°) handles determinism; integer-degree rounding cost
        // up to 0.5° of visual alignment per part along smooth boundaries.
        if (picked.iv != null
            && picked.edgeIdx >= 0
            && picked.edgeIdx < desc.Edges.Count)
        {
            var partEdgeAngle = desc.Edges[picked.edgeIdx].AngleDegrees;
            var bndAngle = AngleDegrees2D(picked.iv.AverageTangent);
            var snap1 = NormalizeDeg(bndAngle - partEdgeAngle);
            var snap2 = NormalizeDeg(snap1 + 180);
            AddSnapRotation(part, snap1);
            AddSnapRotation(part, snap2);
        }
    }

    // ─── Mode 3: uniform curve division ─────────────────────────────────────

    // Signed area of a closed boundary curve (work frame), used to detect winding
    // for the inward-normal sign in curve-division placement. > 0 = CCW.
    private double SignedAreaOfCurve(Curve c)
    {
        if (c == null) return 0.0;
        Polyline pl;
        if (!c.TryGetPolyline(out pl) || pl.Count < 4)
        {
            var divs = c.DivideByCount(64, true);
            if (divs == null || divs.Length < 3) return 0.0;
            pl = new Polyline();
            foreach (var t in divs) pl.Add(c.PointAt(t));
            pl.Add(c.PointAt(divs[0]));
        }
        double a = 0.0;
        for (int i = 0; i < pl.Count - 1; i++)
            a += pl[i].X * pl[i + 1].Y - pl[i + 1].X * pl[i].Y;
        return 0.5 * a;
    }

    private void DoCurveDivisionPlacement(
        List<PartData> parts,
        Dictionary<int, List<PlacedPoly>> placedBySheet,
        Dictionary<int, Grid2d> gridBySheet,
        PackingResult result,
        HashSet<int> placedSourceIndices)
    {
        if (_sheets.Count == 0) return;
        var primarySheet = _sheets[0];
        if (primarySheet.OuterWorkCurve == null) return;

        // Build the per-curve descriptor list: outer first, then any
        // user-wired or auto-detected hole curves. All in WorkXY frame.
        var boundaryCurves = new List<(Curve curve, double length, bool isOuter)>();
        var outerLen = primarySheet.OuterWorkCurve.GetLength();
        if (outerLen <= _tol) return;
        boundaryCurves.Add((primarySheet.OuterWorkCurve, outerLen, true));
        foreach (var hw in primarySheet.HoleWorkCurves)
        {
            var hLen = hw.GetLength();
            if (hLen > _tol) boundaryCurves.Add((hw, hLen, false));
        }

        var totalLen = 0.0;
        foreach (var (_, len, _) in boundaryCurves) totalLen += len;
        if (totalLen <= _tol) return;

        // Prefix-sum lengths for global-arc-length → per-curve arc-length lookup.
        var prefixLens = new double[boundaryCurves.Count];
        var acc = 0.0;
        for (int i = 0; i < boundaryCurves.Count; i++)
        {
            prefixLens[i] = acc;
            acc += boundaryCurves[i].length;
        }

        // WINDING FIX (2026-06-06): the inward normal toward sheet material is
        // s * (-Ty, Tx). For an outer loop the material interior is on the left
        // of a CCW tangent, so s = +1 if CCW (signed area > 0) else -1. For a
        // hole the material is OUTSIDE the hole, so s flips. The old code
        // hardcoded s = +1, which is wrong for a Rhino-default CCW hole (the
        // part pointed INTO the hole and silently fell through). Compute s per
        // boundary curve from its signed area; this only flips a candidate
        // position's sign, never a containment check, so it cannot create overlap.
        var inwardSigns = new double[boundaryCurves.Count];
        for (int c = 0; c < boundaryCurves.Count; c++)
        {
            bool ccw = SignedAreaOfCurve(boundaryCurves[c].curve) > 0;
            inwardSigns[c] = boundaryCurves[c].isOuter ? (ccw ? 1.0 : -1.0) : (ccw ? -1.0 : 1.0);
        }

        // Distribute parts uniformly along total arc length. Each part i
        // lands at (i + 0.5) / N of the total perimeter so the first part
        // sits in the middle of the first interval rather than at the
        // exact start (avoids "all at parameter 0" visual).
        var N = parts.Count;
        if (N == 0) return;

        for (int i = 0; i < N; i++)
        {
            var part = parts[i];
            if (part.Descriptor == null) continue;

            var longestEdgeIdx = FindLongestEdgeIdx(part.Descriptor);
            if (longestEdgeIdx < 0) continue;

            var targetTotal = (i + 0.5) * totalLen / N;
            // Find the curve this falls on.
            Curve? targetCurve = null;
            double targetWithin = 0;
            int targetCidx = -1;
            for (int c = 0; c < boundaryCurves.Count; c++)
            {
                var endLen = prefixLens[c] + boundaryCurves[c].length;
                if (targetTotal <= endLen + _tol)
                {
                    targetCurve = boundaryCurves[c].curve;
                    targetCidx = c;
                    targetWithin = Math.Max(0, targetTotal - prefixLens[c]);
                    targetWithin = Math.Min(targetWithin, boundaryCurves[c].length - _tol);
                    break;
                }
            }
            if (targetCurve == null) continue;

            if (!targetCurve.LengthParameter(targetWithin, out var targetT)) continue;
            var targetPoint = targetCurve.PointAt(targetT);
            var targetTangent = targetCurve.TangentAt(targetT);
            if (!targetTangent.IsValid || targetTangent.IsZero) continue;
            targetTangent.Unitize();

            // 90° CCW of tangent. Per BoundaryRailBuilder convention,
            // this points toward the SHEET interior for both outer-CCW
            // and hole-CW (the convention assumes those windings). If
            // user supplies a hole-CCW curve (Rhino's default closed-
            // planar winding), the inward direction is wrong here and
            // ContainedInSheet will reject the placement — the part
            // falls through to the main loop's fallback path.
            double inwardSgn = (targetCidx >= 0 && targetCidx < inwardSigns.Length) ? inwardSigns[targetCidx] : 1.0;
            var inwardX = inwardSgn * -targetTangent.Y;
            var inwardY = inwardSgn *  targetTangent.X;

            var partEdgeAngle = part.Descriptor.Edges[longestEdgeIdx].AngleDegrees;
            var bndAngle = AngleDegrees2D(targetTangent);
            // Half J+: exact angle, no integer-degree rounding (visual
            // alignment improvement; AngleKey cache quantizes to 0.01°).
            var snapDeg = NormalizeDeg(bndAngle - partEdgeAngle);

            // Candidate placement: matched-edge midpoint at target_point,
            // then nudge inward by spacing so the edge sits cleanly inside
            // the boundary rather than straddling it.
            var re = RotatePoly(part.ExactAtOrigin, snapDeg, longestEdgeIdx);
            var nudge = Math.Max(_spacing, _tol * 5);
            var ox = targetPoint.X - re.MatchedMidX + inwardX * nudge;
            var oy = targetPoint.Y - re.MatchedMidY + inwardY * nudge;

            if (TryPlaceMode3(part, snapDeg, ox, oy, primarySheet, re,
                               placedBySheet, gridBySheet, result))
            {
                placedSourceIndices.Add(part.SourceIndex);
                continue;
            }

            // Flip 180° and retry — handles the case where the rotation
            // had the part pointing the wrong way relative to the boundary.
            var snapDeg2 = NormalizeDeg(snapDeg + 180);
            var re2 = RotatePoly(part.ExactAtOrigin, snapDeg2, longestEdgeIdx);
            var ox2 = targetPoint.X - re2.MatchedMidX + inwardX * nudge;
            var oy2 = targetPoint.Y - re2.MatchedMidY + inwardY * nudge;

            if (TryPlaceMode3(part, snapDeg2, ox2, oy2, primarySheet, re2,
                               placedBySheet, gridBySheet, result))
            {
                placedSourceIndices.Add(part.SourceIndex);
                continue;
            }

            // Both directions failed — leave part for the main loop's
            // fallback path. placedSourceIndices is NOT updated so the main
            // loop will re-attempt with full candidate generation.
        }
    }

    private bool TryPlaceMode3(
        PartData part, double snapDeg, double ox, double oy,
        SheetData sheet, RotEntry re,
        Dictionary<int, List<PlacedPoly>> placedBySheet,
        Dictionary<int, Grid2d> gridBySheet,
        PackingResult result)
    {
        var w = re.Poly.MaxX - re.Poly.MinX;
        var h = re.Poly.MaxY - re.Poly.MinY;

        if (ox < sheet.BBoxMinX - _tol || oy < sheet.BBoxMinY - _tol ||
            ox + w > sheet.BBoxMaxX + _tol || oy + h > sheet.BBoxMaxY + _tol)
            return false;

        if (!ContainedInSheet(re.Poly, ox, oy, sheet)) return false;
        if (CollidesWithGrid(re.Poly, ox, oy, placedBySheet[sheet.Index], gridBySheet[sheet.Index]))
            return false;

        var placed = new PlacedPoly(re.Poly, ox, oy, re.MatchedMidX, re.MatchedMidY);
        var listIdx = placedBySheet[sheet.Index].Count;
        placed.ListIdx = listIdx;
        placedBySheet[sheet.Index].Add(placed);
        gridBySheet[sheet.Index].Insert(listIdx, placed.MinX, placed.MinY, placed.MaxX, placed.MaxY);

        // Build transforms following the same composition order as the
        // main placement loop (V506 root-cause-fix order — see Pack()).
        var rotTx  = Transform.Rotation(snapDeg * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
        var normTx = Transform.Translation(-re.RotMinX, -re.RotMinY, 0);
        var moveTx = Transform.Translation(ox, oy, 0);
        var wts    = sheet.WorkToSheet;

        var outCurve = part.SourceCurve.DuplicateCurve();
        outCurve.Transform(part.PlaneToWork);
        outCurve.Transform(part.NormalizeTx);
        outCurve.Transform(rotTx);
        outCurve.Transform(normTx);
        outCurve.Transform(moveTx);
        outCurve.Transform(wts);

        var compound = wts * moveTx * normTx * rotTx * part.NormalizeTx * part.PlaneToWork;

        result.PackedCurves.Add(outCurve);
        result.Transforms.Add(compound);
        result.SourceIndices.Add(part.SourceIndex);
        result.SheetIndices.Add(sheet.Index);
        return true;
    }

    private static int FindLongestEdgeIdx(FragmentDescriptor desc)
    {
        int bestIdx = -1;
        double bestLen = 0;
        for (int i = 0; i < desc.Edges.Count; i++)
        {
            if (desc.Edges[i].Length > bestLen)
            {
                bestLen = desc.Edges[i].Length;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    // ─── Boundary index build (Half C) ──────────────────────────────────────

    private void BuildBoundaryIndexes(List<PartData> parts)
    {
        // Half C: auto-tune window length to match part edge scale. The
        // sliding-window descriptors only score well against part edges
        // when their length-bucket lines up — fixed sheetSpan/50 (Half B
        // default) collapsed scores when parts were ~5x bigger or smaller
        // than the windows. Median across part edges is a robust proxy
        // for "the scale users care about".
        var allEdgeLens = new List<double>();
        foreach (var p in parts)
        {
            if (p.Descriptor == null) continue;
            foreach (var e in p.Descriptor.Edges)
            {
                if (e.Length > _tol * 5) allEdgeLens.Add(e.Length);
            }
        }
        double medianEdgeLen;
        if (allEdgeLens.Count == 0) medianEdgeLen = _tol * 50;
        else
        {
            allEdgeLens.Sort();
            medianEdgeLen = allEdgeLens[allEdgeLens.Count / 2];
        }

        foreach (var sheet in _sheets)
        {
            if (sheet.OuterWorkCurve == null) continue;
            var sheetSpan = Math.Max(sheet.BBoxMaxX - sheet.BBoxMinX,
                                     sheet.BBoxMaxY - sheet.BBoxMinY);
            // Clamp window length to a reasonable range. Min: enough above
            // tolerance to be meaningful. Max: at least 4 windows fit on
            // the sheet so we get coverage.
            var windowLen = Math.Max(_tol * 5, Math.Min(medianEdgeLen, sheetSpan / 4.0));
            var stepLen   = Math.Max(windowLen * 0.5, _tol * 2);
            // Length bucket: roughly half the window length so part edges
            // within ±50% of window length match through the LengthRadius=1
            // widening.
            var lengthBucket = Math.Max(_tol * 5, windowLen / 2.0);

            try
            {
                var idx = new BoundaryRailIndex<BoundaryIntervalInfo>();
                var builder = new BoundaryRailBuilder(
                    windowLength: windowLen,
                    stepLength: stepLen,
                    tolerance: _tol,
                    lengthBucketSize: lengthBucket,
                    angleBucketSizeDegrees: BoundaryAngleBucketDeg,
                    curvatureBucketSize: BoundaryCurvBucket);

                builder.AddCurve(sheet.OuterWorkCurve, isOuterBoundary: true, zoneBucket: 0, index: idx);
                foreach (var hw in sheet.HoleWorkCurves)
                    builder.AddCurve(hw, isOuterBoundary: false, zoneBucket: 0, index: idx);

                sheet.BoundaryIndex = idx;
                sheet.BoundaryLengthBucket = lengthBucket;
            }
            catch
            {
                sheet.BoundaryIndex = null;
            }
        }
    }

    private static EdgeDescriptor IntervalToEdgeDescriptor(BoundaryIntervalInfo iv)
    {
        var ang = AngleDegrees2D(iv.AverageTangent);
        return new EdgeDescriptor(
            length: iv.ApproxLength,
            angleDegrees: ang,
            curvatureScore: iv.CurvatureScore,
            straightnessScore: iv.StraightnessScore,
            zoneId: 0);
    }

    private static double AngleDegrees2D(Vector3d v)
    {
        var a = Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
        if (a < 0) a += 360.0;
        return a;
    }

    private static double NormalizeDeg(double deg)
    {
        var d = deg % 360.0;
        if (d < 0) d += 360.0;
        return d;
    }

    private void AddSnapRotation(PartData part, double snapDeg)
    {
        if (part.SnapRotationsDeg.Count >= MaxSnapRotationsPerPart) return;
        // Skip if it is within 0.5° of an existing user rotation or already-
        // added snap rotation — avoids redundant rotation-cache entries.
        foreach (var r in _rotDeg)
            if (AngleNear(NormalizeDeg(r), snapDeg, 0.5)) return;
        foreach (var r in part.SnapRotationsDeg)
            if (AngleNear(r, snapDeg, 0.5)) return;
        part.SnapRotationsDeg.Add(snapDeg);
    }

    private static bool AngleNear(double a, double b, double eps)
    {
        var d = Math.Abs(a - b) % 360.0;
        if (d > 180.0) d = 360.0 - d;
        return d <= eps;
    }

    // ─── Adaptive curve → Poly2d ─────────────────────────────────────────────

    private Poly2d? CurveToPoly2dAdaptive(Curve curve)
    {
        double[] vx, vy;

        if (curve.TryGetPolyline(out var pl))
        {
            var cnt = pl.Count;
            if (cnt > 1 && pl[0].DistanceTo(pl[cnt - 1]) < _tol) cnt--;
            if (cnt < 3) return null;
            if (cnt > ExactMaxVerts)
            {
                vx = new double[ExactMaxVerts]; vy = new double[ExactMaxVerts];
                var step = (double)cnt / ExactMaxVerts;
                for (var i = 0; i < ExactMaxVerts; i++)
                {
                    var idx = Math.Min(cnt - 1, (int)(i * step));
                    vx[i] = pl[idx].X; vy[i] = pl[idx].Y;
                }
                return Poly2d.Build(vx, vy);
            }
            vx = new double[cnt]; vy = new double[cnt];
            for (var i = 0; i < cnt; i++) { vx[i] = pl[i].X; vy[i] = pl[i].Y; }
            return Poly2d.Build(vx, vy);
        }

        var chord  = Math.Max(_tol, 1e-3);
        var toPoly = curve.ToPolyline(chord, Math.PI / 90.0, 0, 0);
        IList<Point3d> pts;
        if (toPoly != null && toPoly.TryGetPolyline(out pl) && pl.Count >= 4)
        {
            pts = pl;
        }
        else
        {
            var divPar = curve.DivideByCount(Math.Min(ExactMaxVerts, 128), false);
            if (divPar == null || divPar.Length < 3) return null;
            var tmp = new List<Point3d>(divPar.Length);
            foreach (var t in divPar) tmp.Add(curve.PointAt(t));
            pts = tmp;
        }

        var n = pts.Count;
        if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < _tol) n--;
        if (n < 3) return null;

        if (n > ExactMaxVerts)
        {
            vx = new double[ExactMaxVerts]; vy = new double[ExactMaxVerts];
            var step = (double)n / ExactMaxVerts;
            for (var i = 0; i < ExactMaxVerts; i++)
            {
                var idx = Math.Min(n - 1, (int)(i * step));
                vx[i] = pts[idx].X; vy[i] = pts[idx].Y;
            }
        }
        else
        {
            vx = new double[n]; vy = new double[n];
            for (var i = 0; i < n; i++) { vx[i] = pts[i].X; vy[i] = pts[i].Y; }
        }
        return Poly2d.Build(vx, vy);
    }

    // ─── Rotation cache ───────────────────────────────────────────────────────

    private Dictionary<(int, int), RotEntry> BuildRotationCache(List<PartData> parts)
    {
        var cache = new Dictionary<(int, int), RotEntry>(parts.Count * _rotDeg.Count);
        foreach (var part in parts)
        {
            foreach (var deg in _rotDeg)
                cache[(part.SourceIndex, AngleKey(deg))] = RotatePoly(part.ExactAtOrigin, deg, part.BestMatchedEdgeIndex);
            // Boundary-aware mode: also cache the per-part snap rotations.
            // These angles only apply to this part — the rotation key
            // is (part.SourceIndex, AngleKey(deg)), which is already
            // part-scoped, so no collision with another part's rotations
            // at the same angle.
            foreach (var deg in part.SnapRotationsDeg)
            {
                var key = (part.SourceIndex, AngleKey(deg));
                if (!cache.ContainsKey(key))
                    cache[key] = RotatePoly(part.ExactAtOrigin, deg, part.BestMatchedEdgeIndex);
            }
        }
        return cache;
    }

    private IReadOnlyList<double> RotationsForPart(PartData part)
    {
        if (part.SnapRotationsDeg.Count == 0) return _rotDeg;
        // Place snap rotations FIRST so they get the best chance at the
        // first valid placement before user-supplied generic rotations.
        var combined = new List<double>(_rotDeg.Count + part.SnapRotationsDeg.Count);
        combined.AddRange(part.SnapRotationsDeg);
        foreach (var r in _rotDeg)
        {
            var rNorm = NormalizeDeg(r);
            var dup = false;
            foreach (var s in part.SnapRotationsDeg)
                if (AngleNear(s, rNorm, 0.5)) { dup = true; break; }
            if (!dup) combined.Add(r);
        }
        return combined;
    }

    private static int AngleKey(double deg) => (int)Math.Round(deg * 100) % 36000;

    private static RotEntry RotatePoly(Poly2d src, double angleDeg, int matchedEdgeIndex)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var n   = src.N;
        var rx  = new double[n];
        var ry  = new double[n];
        double minX = double.MaxValue, minY = double.MaxValue;
        for (var i = 0; i < n; i++)
        {
            rx[i] = cos * src.Vx[i] - sin * src.Vy[i];
            ry[i] = sin * src.Vx[i] + cos * src.Vy[i];
            if (rx[i] < minX) minX = rx[i];
            if (ry[i] < minY) minY = ry[i];
        }
        for (var i = 0; i < n; i++) { rx[i] -= minX; ry[i] -= minY; }

        // Boundary-aware mode: capture the matched-edge midpoint (in this
        // rotation's normalized frame) so OrderCandidates can bias placement
        // toward the matched boundary interval. NaN when no edge is matched.
        double mmx = double.NaN, mmy = double.NaN;
        if (matchedEdgeIndex >= 0 && matchedEdgeIndex < n)
        {
            var j = (matchedEdgeIndex + 1) % n;
            mmx = 0.5 * (rx[matchedEdgeIndex] + rx[j]);
            mmy = 0.5 * (ry[matchedEdgeIndex] + ry[j]);
        }
        return new RotEntry(Poly2d.Build(rx, ry)!, minX, minY, mmx, mmy);
    }

    // ─── Placement search ────────────────────────────────────────────────────

    private (PlacedPoly? placed, double deg, double rotMinX, double rotMinY) FindBestPlacement(
        PartData part, SheetData sheet,
        List<PlacedPoly> placedOnSheet, Grid2d grid,
        Dictionary<(int, int), RotEntry> rotCache,
        PackingResult result,
        int candidatePhase)
    {
        // Rotation pool: user-supplied rotations + per-part snap rotations
        // (boundary-aware mode). Snap rotations come first so a high-affinity
        // edge gets evaluated against the snap angle before any geometric
        // rotation. Empty snap list → falls back to _rotDeg verbatim, which
        // is the boundary-mode-off byte-equivalent path.
        var rots = RotationsForPart(part);
        var rotCount = rots.Count;
        var rotBests = new (PlacedPoly? p, double deg, double rotMinX, double rotMinY)[rotCount];
        var cands    = 0;
        var colls    = 0;

        // Anchor for boundary bias. Only meaningful when this part's best
        // match was on THIS sheet — otherwise the bias is a no-op and
        // EvaluateRotation falls back to the geometric ordering.
        var hasBoundaryAnchor = _boundaryMode > 0
            && part.BestMatchedSheetIndex == sheet.Index
            && part.BestMatchedInterval != null;
        var anchorX = hasBoundaryAnchor ? part.BestMatchedInterval!.LocalBounds.Center.X : double.NaN;
        var anchorY = hasBoundaryAnchor ? part.BestMatchedInterval!.LocalBounds.Center.Y : double.NaN;

        Parallel.For(0, rotCount, i =>
        {
            var deg = rots[i];
            if (!rotCache.TryGetValue((part.SourceIndex, AngleKey(deg)), out var re)) return;
            var (best, c, cc) = EvaluateRotation(re, sheet, placedOnSheet, grid, anchorX, anchorY, candidatePhase);
            rotBests[i] = (best, deg, re.RotMinX, re.RotMinY);
            Interlocked.Add(ref cands, c);
            Interlocked.Add(ref colls, cc);
        });

        result.CandidateCount      += cands;
        result.CollisionCheckCount += colls;

        (PlacedPoly? p, double deg, double rotMinX, double rotMinY) winner = (null, 0, 0, 0);
        foreach (var r in rotBests)
        {
            if (r.p == null) continue;
            if (winner.p == null || IsBetter(r.p, winner.p, anchorX, anchorY)) winner = r;
        }
        return winner;
    }

    private (PlacedPoly? best, int cands, int colls) EvaluateRotation(
        RotEntry re, SheetData sheet, List<PlacedPoly> placedOnSheet, Grid2d grid,
        double anchorX, double anchorY, int candidatePhase)
    {
        var rotPoly = re.Poly;
        PlacedPoly? best = null;
        var cands = 0;
        var colls = 0;
        var valid = 0;
        var w = rotPoly.MaxX - rotPoly.MinX;
        var h = rotPoly.MaxY - rotPoly.MinY;

        // Half D: boundary-anchored rotations get a larger explore budget
        // so multiple parts targeting nearby anchors don't collapse onto
        // the first valid placement found near the centroid. Geometric
        // path keeps the original 16 ceiling — no perf regression there.
        var maxValidThis = !double.IsNaN(re.MatchedMidX)
            ? MaxValidPerRotBoundary
            : MaxValidPerRot;

        foreach (var (ox, oy) in GenerateCandidates(sheet, rotPoly, placedOnSheet, re, anchorX, anchorY, candidatePhase))
        {
            cands++;
            if (ox < sheet.BBoxMinX - _tol || oy < sheet.BBoxMinY - _tol ||
                ox + w > sheet.BBoxMaxX + _tol || oy + h > sheet.BBoxMaxY + _tol)
                continue;

            if (!ContainedInSheet(rotPoly, ox, oy, sheet)) continue;

            colls++;
            if (CollidesWithGrid(rotPoly, ox, oy, placedOnSheet, grid)) continue;

            var placed = new PlacedPoly(rotPoly, ox, oy, re.MatchedMidX, re.MatchedMidY);
            if (best == null || IsBetter(placed, best, anchorX, anchorY)) best = placed;
            if (++valid >= maxValidThis) break;
        }
        return (best, cands, colls);
    }

    // ─── Candidate generation ────────────────────────────────────────────────

    private IEnumerable<(double ox, double oy)> GenerateCandidates(
        SheetData sheet, Poly2d rotPoly, List<PlacedPoly> placed,
        RotEntry re, double anchorX, double anchorY, int candidatePhase = 0)
    {
        // candidatePhase: 0 = all (geometric path / Mode 1).
        //                 1 = boundary anchors only (Mode 2 phase 1).
        //                 2 = interior only         (Mode 2 phase 2).
        var includeBoundary = candidatePhase != 2;
        var includeInterior = candidatePhase != 1;

        var w   = rotPoly.MaxX - rotPoly.MinX;
        var h   = rotPoly.MaxY - rotPoly.MinY;
        var raw = new List<(double, double)>(512);

        if (includeBoundary)
        {
            // BBox corners.
            raw.Add((sheet.BBoxMinX, sheet.BBoxMinY));
            raw.Add((sheet.BBoxMaxX - w, sheet.BBoxMinY));
            raw.Add((sheet.BBoxMinX, sheet.BBoxMaxY - h));
            raw.Add((sheet.BBoxMaxX - w, sheet.BBoxMaxY - h));

            // Sheet outline vertex anchors.
            var o = sheet.Outer;
            for (var i = 0; i < o.N; i++) AddCornerOffsets(raw, o.Vx[i], o.Vy[i], w, h);

            // Hole vertex anchors.
            foreach (var hole in sheet.Holes)
                for (var i = 0; i < hole.N; i++) AddCornerOffsets(raw, hole.Vx[i], hole.Vy[i], w, h);
        }

        // NFP + BBox contact from placed parts (always — regardless of
        // phase, the geometry of already-placed parts is real).
        foreach (var p in placed)
        {
            ComputeNfpCandidates(p, rotPoly, raw);
            raw.Add((p.MaxX + _spacing, p.MinY));
            raw.Add((p.MinX, p.MaxY + _spacing));
            raw.Add((p.MaxX + _spacing, p.MaxY + _spacing));

            var pv       = p.Poly;
            var edgeStep = Math.Max(Math.Min(w, h) * 0.5, Math.Max(_spacing, _tol) * 4);
            if (IsFinite(edgeStep) && edgeStep > _tol)
            {
                for (var vi = 0; vi < pv.N; vi++)
                {
                    var vj   = (vi + 1) % pv.N;
                    var dx   = pv.Vx[vj] - pv.Vx[vi];
                    var dy   = pv.Vy[vj] - pv.Vy[vi];
                    var elen = Math.Sqrt(dx * dx + dy * dy);
                    if (elen < edgeStep * 2) continue;
                    var steps = Math.Min(8, (int)Math.Ceiling(elen / edgeStep));
                    for (var s = 1; s < steps; s++)
                    {
                        var t = (double)s / steps;
                        AddCornerOffsets(raw,
                            pv.Vx[vi] + t * dx + p.OriginX,
                            pv.Vy[vi] + t * dy + p.OriginY, w, h);
                    }
                }
            }
        }

        if (includeInterior)
        {
            // Interior-sampled grid pre-filtered to the sheet polygon.
            // Uses all-4-corners check instead of center-only: a position is added only when
            // all four bbox corners of the part placement are inside the sheet outline.  This
            // matches what ContainedInSheet requires for rectangular parts and dramatically
            // reduces false candidates for organic / curved sheet outlines.
            // Finer grid step (0.15× min-dim vs old 0.4×) compensates for fewer surviving pts.
            var sheetSpan = Math.Max(sheet.BBoxMaxX - sheet.BBoxMinX, sheet.BBoxMaxY - sheet.BBoxMinY);
            var gridStep  = Math.Max(Math.Min(w, h) * 0.15, Math.Max(sheetSpan / 150.0, _tol * 4));
            if (IsFinite(gridStep) && gridStep > _tol)
            {
                var maxPts  = Math.Min(1200, Math.Max(200, _maxCandidates * 2));
                var cnt     = 0;
                var outerVx = sheet.Outer.Vx;
                var outerVy = sheet.Outer.Vy;
                var outerN  = sheet.Outer.N;
                for (var gy = sheet.BBoxMinY;
                         gy <= sheet.BBoxMaxY - h + _tol && cnt < maxPts;
                         gy += gridStep)
                {
                    for (var gx = sheet.BBoxMinX;
                             gx <= sheet.BBoxMaxX - w + _tol && cnt < maxPts;
                             gx += gridStep)
                    {
                        if (PointInPoly(gx,     gy,     outerVx, outerVy, outerN) &&
                            PointInPoly(gx + w, gy,     outerVx, outerVy, outerN) &&
                            PointInPoly(gx,     gy + h, outerVx, outerVy, outerN) &&
                            PointInPoly(gx + w, gy + h, outerVx, outerVy, outerN))
                        { raw.Add((gx, gy)); cnt++; }
                    }
                }
            }

            // IFP vertices (still useful for convex / near-convex sheets).
            ComputeIfpCandidates(sheet.Outer, rotPoly, raw);
        }

        // Dedup + bbox filter.
        var tol2     = Math.Max(_tol, 1e-6);
        var seen     = new HashSet<long>();
        var filtered = new List<(double, double)>(raw.Count);
        foreach (var (ox, oy) in raw)
        {
            if (ox + w < sheet.BBoxMinX - _tol || oy + h < sheet.BBoxMinY - _tol ||
                ox > sheet.BBoxMaxX + _tol     || oy > sheet.BBoxMaxY + _tol)
                continue;
            var kx = (long)Math.Round(ox / tol2);
            var ky = (long)Math.Round(oy / tol2);
            if (!seen.Add(kx * 2000003L + ky)) continue;
            filtered.Add((ox, oy));
        }
        // Half E: aggressive candidate cap for boundary phase. Phase 2
        // (interior) and Mode 0/1 keep the user's _maxCandidates value.
        var cap = candidatePhase == 1
            ? Math.Max(BoundaryMaxCandidates, _maxCandidates)
            : _maxCandidates;
        return OrderCandidates(filtered, w, h, re, anchorX, anchorY).Take(cap);
    }

    private void AddCornerOffsets(List<(double, double)> raw, double vx, double vy, double w, double h)
    {
        raw.Add((vx,     vy));
        raw.Add((vx - w, vy));
        raw.Add((vx,     vy - h));
        raw.Add((vx - w, vy - h));
        if (_spacing > _tol)
        {
            raw.Add((vx + _spacing,     vy));
            raw.Add((vx,                vy + _spacing));
            raw.Add((vx - w - _spacing, vy));
            raw.Add((vx,                vy - h - _spacing));
        }
    }

    // ─── IFP — Minkowski erosion (best for convex sheets) ────────────────────

    private void ComputeIfpCandidates(Poly2d sheetOuter, Poly2d part, List<(double, double)> result)
    {
        var n = sheetOuter.N;
        if (n < 3 || part.N < 3) return;

        var signedArea = 0.0;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            signedArea += sheetOuter.Vx[i] * sheetOuter.Vy[j] - sheetOuter.Vx[j] * sheetOuter.Vy[i];
        }
        var sign = signedArea > 0 ? 1.0 : -1.0;

        var constraints = new List<(double nx, double ny, double c)>(n);
        for (var i = 0; i < n; i++)
        {
            var j   = (i + 1) % n;
            var dx  = sheetOuter.Vx[j] - sheetOuter.Vx[i];
            var dy  = sheetOuter.Vy[j] - sheetOuter.Vy[i];
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < _tol) continue;
            var nx  = sign * dy / len;
            var ny  = sign * (-dx) / len;
            var sup = double.MinValue;
            for (var k = 0; k < part.N; k++)
            {
                var s = nx * part.Vx[k] + ny * part.Vy[k];
                if (s > sup) sup = s;
            }
            if (!IsFinite(sup)) continue;
            constraints.Add((nx, ny, nx * sheetOuter.Vx[i] + ny * sheetOuter.Vy[i] - sup));
        }

        if (constraints.Count < 3) return;
        var m        = constraints.Count;
        var ifpVerts = new List<(double, double)>(m);
        for (var i = 0; i < m; i++)
        {
            var (nx1, ny1, c1) = constraints[i];
            var (nx2, ny2, c2) = constraints[(i + 1) % m];
            var det = nx1 * ny2 - nx2 * ny1;
            if (Math.Abs(det) < 1e-10) continue;
            var x = (c1 * ny2 - c2 * ny1) / det;
            var y = (nx1 * c2 - nx2 * c1) / det;
            if (IsFinite(x) && IsFinite(y)) { ifpVerts.Add((x, y)); result.Add((x, y)); }
        }

        var sampleStep = Math.Max(Math.Min(part.MaxX - part.MinX, part.MaxY - part.MinY) * 0.5, _tol * 4);
        if (IsFinite(sampleStep) && sampleStep > _tol && ifpVerts.Count > 1)
        {
            for (var i = 0; i < ifpVerts.Count; i++)
            {
                var (ax, ay) = ifpVerts[i];
                var (bx, by) = ifpVerts[(i + 1) % ifpVerts.Count];
                var elen = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (elen < sampleStep * 2) continue;
                var steps = Math.Min(20, (int)Math.Ceiling(elen / sampleStep));
                for (var s = 1; s < steps; s++)
                {
                    var t = (double)s / steps;
                    result.Add((ax + t * (bx - ax), ay + t * (by - ay)));
                }
            }
        }
    }

    // ─── NFP — Minkowski difference ───────────────────────────────────────────

    private static void ComputeNfpCandidates(PlacedPoly placed, Poly2d part, List<(double, double)> result)
    {
        var A  = placed.Poly;
        var B  = part;
        var ox = placed.OriginX;
        var oy = placed.OriginY;
        var sA = Math.Max(1, A.N / MaxNfpVerts);
        var sB = Math.Max(1, B.N / MaxNfpVerts);
        for (var ai = 0; ai < A.N; ai += sA)
            for (var bi = 0; bi < B.N; bi += sB)
                result.Add((A.Vx[ai] - B.Vx[bi] + ox, A.Vy[ai] - B.Vy[bi] + oy));
    }

    // ─── Containment + collision ─────────────────────────────────────────────

    private bool ContainedInSheet(Poly2d poly, double ox, double oy, SheetData sheet)
    {
        var outer = sheet.Outer;
        for (var i = 0; i < poly.N; i++)
            if (!PointInPoly(poly.Vx[i] + ox, poly.Vy[i] + oy, outer.Vx, outer.Vy, outer.N))
                return false;
        if (PolysEdgesCross(poly, ox, oy, outer, 0, 0)) return false;
        if (_spacing > _tol && MinDistPolys(poly, ox, oy, outer, 0, 0) < _spacing - _tol) return false;

        foreach (var hole in sheet.Holes)
        {
            if (!BBoxOverlap(poly.MinX + ox, poly.MinY + oy, poly.MaxX + ox, poly.MaxY + oy,
                             hole.MinX, hole.MinY, hole.MaxX, hole.MaxY, 0)) continue;
            // Part vertices inside hole.
            for (var i = 0; i < poly.N; i++)
                if (PointInPoly(poly.Vx[i] + ox, poly.Vy[i] + oy, hole.Vx, hole.Vy, hole.N))
                    return false;
            if (PointInPoly(poly.Cx + ox, poly.Cy + oy, hole.Vx, hole.Vy, hole.N)) return false;
            // Hole vertices inside part — catches the case where the hole is fully enclosed by the part.
            for (var j = 0; j < hole.N; j++)
                if (PointInPoly(hole.Vx[j] - ox, hole.Vy[j] - oy, poly.Vx, poly.Vy, poly.N))
                    return false;
            if (PointInPoly(hole.Cx - ox, hole.Cy - oy, poly.Vx, poly.Vy, poly.N)) return false;
            if (PolysEdgesCross(poly, ox, oy, hole, 0, 0)) return false;
            if (_spacing > _tol && MinDistPolys(poly, ox, oy, hole, 0, 0) < _spacing - _tol) return false;
        }
        return true;
    }

    private bool CollidesWithGrid(Poly2d poly, double ox, double oy, List<PlacedPoly> placed, Grid2d grid)
    {
        var pad = _spacing + _tol;
        foreach (var idx in grid.Query(poly.MinX + ox - pad, poly.MinY + oy - pad,
                                        poly.MaxX + ox + pad, poly.MaxY + oy + pad))
        {
            if (idx < 0 || idx >= placed.Count) continue;
            var p = placed[idx];
            if (!BBoxOverlap(poly.MinX + ox, poly.MinY + oy, poly.MaxX + ox, poly.MaxY + oy,
                              p.MinX, p.MinY, p.MaxX, p.MaxY, _spacing)) continue;

            // Half J fix: split the legacy PolysOverlap check (which
            // bundled real-overlap and spacing-violation under one bool)
            // into two distinct branches so trim relaxation NEVER eats
            // the user's Spacing constraint.
            //
            //   real-overlap   -> trim relaxation may accept up to
            //                     _trimTolerance penetration depth
            //   spacing-only   -> always strict; reject if MinDist <
            //                     _spacing
            if (PolysActuallyOverlap(poly, ox, oy, p.Poly, p.OriginX, p.OriginY))
            {
                if (_trimTolerance <= 0) return true;
                var pen = MaxPenetrationDepth(poly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                if (pen > _trimTolerance + _tol) return true;
                // Accept — trim post-pass will boolean-difference this pair.
            }
            else if (_spacing > _tol)
            {
                var minDist = MinDistPolys(poly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                if (minDist < _spacing - _tol) return true;
            }
        }
        return false;
    }

    private bool PolysActuallyOverlap(
        Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        // Real geometric overlap test: edges cross OR a centroid lies
        // strictly inside the other polygon. Excludes the
        // "MinDistPolys < spacing" branch from PolysOverlap so trim
        // relaxation does not silently disable Spacing.
        if (PolysEdgesCross(a, aox, aoy, b, box, boy)) return true;
        if (PointInPoly(a.Cx + aox - box, a.Cy + aoy - boy, b.Vx, b.Vy, b.N)) return true;
        if (PointInPoly(b.Cx + box - aox, b.Cy + boy - aoy, a.Vx, a.Vy, a.N)) return true;
        return false;
    }

    private static double MaxPenetrationDepth(
        Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        // Approximate penetration depth: for each vertex of EITHER polygon
        // that lies inside the OTHER, compute its distance to the nearest
        // edge of that other polygon. Take the max. This bounds how deep
        // the overlap is along any line; trimming to that depth resolves
        // the overlap.
        double maxPen = 0;
        for (int i = 0; i < a.N; i++)
        {
            var px = a.Vx[i] + aox;
            var py = a.Vy[i] + aoy;
            // Translate (px, py) into b's local frame for PointInPoly
            if (!PointInPoly(px - box, py - boy, b.Vx, b.Vy, b.N)) continue;
            // Distance from this vertex to the nearest edge of b
            var min = double.MaxValue;
            for (int j = 0; j < b.N; j++)
            {
                int k = (j + 1) % b.N;
                var d = PointSegDist(px, py,
                    b.Vx[j] + box, b.Vy[j] + boy,
                    b.Vx[k] + box, b.Vy[k] + boy);
                if (d < min) min = d;
            }
            if (min > maxPen) maxPen = min;
        }
        for (int i = 0; i < b.N; i++)
        {
            var px = b.Vx[i] + box;
            var py = b.Vy[i] + boy;
            if (!PointInPoly(px - aox, py - aoy, a.Vx, a.Vy, a.N)) continue;
            var min = double.MaxValue;
            for (int j = 0; j < a.N; j++)
            {
                int k = (j + 1) % a.N;
                var d = PointSegDist(px, py,
                    a.Vx[j] + aox, a.Vy[j] + aoy,
                    a.Vx[k] + aox, a.Vy[k] + aoy);
                if (d < min) min = d;
            }
            if (min > maxPen) maxPen = min;
        }
        return maxPen;
    }

    // ─── Pure-math polygon operations ────────────────────────────────────────

    private static bool PointInPoly(double px, double py, double[] vx, double[] vy, int n)
    {
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((vy[i] > py) != (vy[j] > py) &&
                px < (vx[j] - vx[i]) * (py - vy[i]) / (vy[j] - vy[i]) + vx[i])
                inside = !inside;
        }
        return inside;
    }

    private static bool BBoxOverlap(
        double ax0, double ay0, double ax1, double ay1,
        double bx0, double by0, double bx1, double by1, double pad)
        => ax1 + pad >= bx0 && bx1 + pad >= ax0 && ay1 + pad >= by0 && by1 + pad >= ay0;

    private static bool SegmentsIntersect(
        double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy, double tol)
    {
        var d1x   = bx - ax; var d1y = by - ay;
        var d2x   = dx - cx; var d2y = dy - cy;
        var denom = d1x * d2y - d1y * d2x;
        if (Math.Abs(denom) < tol * tol) return false;
        var t = ((cx - ax) * d2y - (cy - ay) * d2x) / denom;
        var u = ((cx - ax) * d1y - (cy - ay) * d1x) / denom;
        return t > tol && t < 1.0 - tol && u > tol && u < 1.0 - tol;
    }

    private bool PolysEdgesCross(Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        for (var i = 0; i < a.N; i++)
        {
            var ax  = a.Vx[i] + aox;              var ay  = a.Vy[i] + aoy;
            var bx_ = a.Vx[(i + 1) % a.N] + aox; var by_ = a.Vy[(i + 1) % a.N] + aoy;
            for (var j = 0; j < b.N; j++)
            {
                var cx = b.Vx[j] + box;              var cy = b.Vy[j] + boy;
                var dx = b.Vx[(j + 1) % b.N] + box; var dy = b.Vy[(j + 1) % b.N] + boy;
                if (SegmentsIntersect(ax, ay, bx_, by_, cx, cy, dx, dy, _tol)) return true;
            }
        }
        return false;
    }

    private bool PolysOverlap(Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        if (PolysEdgesCross(a, aox, aoy, b, box, boy)) return true;
        // Translate each centroid into the other polygon's local coordinate system before PointInPoly.
        if (PointInPoly(a.Cx + aox - box, a.Cy + aoy - boy, b.Vx, b.Vy, b.N)) return true;
        if (PointInPoly(b.Cx + box - aox, b.Cy + boy - aoy, a.Vx, a.Vy, a.N)) return true;
        if (MinDistPolys(a, aox, aoy, b, box, boy) < _spacing - _tol) return true;
        return false;
    }

    private static double MinDistPolys(Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        var min = double.MaxValue;
        for (var i = 0; i < a.N; i++)
        {
            var px = a.Vx[i] + aox; var py = a.Vy[i] + aoy;
            for (var j = 0; j < b.N; j++)
            {
                var q0x = b.Vx[j] + box;              var q0y = b.Vy[j] + boy;
                var q1x = b.Vx[(j + 1) % b.N] + box; var q1y = b.Vy[(j + 1) % b.N] + boy;
                var d   = PointSegDist(px, py, q0x, q0y, q1x, q1y);
                if (d < min) min = d;
            }
        }
        return min;
    }

    private static double PointSegDist(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx    = bx - ax; var dy = by - ay;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        var t  = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lenSq));
        var nx = ax + t * dx; var ny = ay + t * dy;
        return Math.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
    }

    // ─── Ordering / sorting ───────────────────────────────────────────────────

    private IEnumerable<(double, double)> OrderCandidates(
        List<(double ox, double oy)> candidates, double w, double h,
        RotEntry re, double anchorX, double anchorY)
    {
        var hasBias = !double.IsNaN(anchorX) && !double.IsNaN(re.MatchedMidX);
        if (hasBias)
        {
            // Half E: pure anchor-distance ordering. The Half C/D version
            // used corner-mode as a secondary key, which clustered all
            // equidistant candidates around the anchor centroid into
            // whichever corner the user (or the default) had set — the
            // bottom-left clump in HitL #3. Dropping the secondary lets
            // candidates around the anchor distribute by generation order,
            // which mixes outline anchors, hole anchors, and NFP/edge
            // contacts naturally.
            return candidates.OrderBy(p => Hypot(
                p.ox + re.MatchedMidX - anchorX,
                p.oy + re.MatchedMidY - anchorY));
        }

        // Geometric path (Mode 0, or Mode 1/2 parts that did not get an
        // anchor): pure corner-mode ordering, unchanged from V506 baseline.
        return _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidates.OrderBy(p => p.oy).ThenByDescending(p => p.ox + w),
            PackingCornerMode.TopLeft     => candidates.OrderByDescending(p => p.oy + h).ThenBy(p => p.ox),
            PackingCornerMode.TopRight    => candidates.OrderByDescending(p => p.oy + h).ThenByDescending(p => p.ox + w),
            _                             => candidates.OrderBy(p => p.oy).ThenBy(p => p.ox)
        };
    }

    private bool IsBetter(PlacedPoly candidate, PlacedPoly current, double anchorX = double.NaN, double anchorY = double.NaN)
    {
        // Boundary-aware mode: when both placements have a matched-edge
        // midpoint and a valid anchor, prefer the one whose matched mid is
        // closer to the anchor. This is a HARD primary key — overrides the
        // corner-mode geometric preference.
        if (!double.IsNaN(anchorX)
            && !double.IsNaN(candidate.MatchedMidX)
            && !double.IsNaN(current.MatchedMidX))
        {
            var dCand = Hypot(candidate.MatchedMidX - anchorX, candidate.MatchedMidY - anchorY);
            var dCur  = Hypot(current.MatchedMidX  - anchorX, current.MatchedMidY  - anchorY);
            if (dCand < dCur - _tol) return true;
            if (dCand > dCur + _tol) return false;
            // Half E: when boundary mode is on, equidistant placements
            // around the anchor stick with first-found instead of falling
            // through to corner-mode tie-break (which produced the
            // bottom-left clump observed in HitL #3). Geometric path is
            // unaffected — its anchor is NaN so this branch never fires.
            if (_boundaryMode > 0) return false;
        }

        return _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidate.MinY < current.MinY - _tol ||
                (Math.Abs(candidate.MinY - current.MinY) <= _tol && candidate.MaxX > current.MaxX + _tol),
            PackingCornerMode.TopLeft     => candidate.MaxY > current.MaxY + _tol ||
                (Math.Abs(candidate.MaxY - current.MaxY) <= _tol && candidate.MinX < current.MinX - _tol),
            PackingCornerMode.TopRight    => candidate.MaxY > current.MaxY + _tol ||
                (Math.Abs(candidate.MaxY - current.MaxY) <= _tol && candidate.MaxX > current.MaxX + _tol),
            _                             => candidate.MinY < current.MinY - _tol ||
                (Math.Abs(candidate.MinY - current.MinY) <= _tol && candidate.MinX < current.MinX - _tol)
        };
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private List<PartData> SortParts(List<PartData> parts)
    {
        var rnd = _seed == 0 ? null : new Random(_seed);
        IEnumerable<PartData> q = _sortMode switch
        {
            PackingSortMode.UserOrder              => parts.OrderBy(p => p.SourceIndex),
            PackingSortMode.WidthDescending        => parts.OrderByDescending(p => p.Width).ThenBy(_ => rnd?.Next() ?? 0),
            PackingSortMode.HeightDescending       => parts.OrderByDescending(p => p.Height).ThenBy(_ => rnd?.Next() ?? 0),
            PackingSortMode.MaxDimensionDescending => parts.OrderByDescending(p => Math.Max(p.Width, p.Height)).ThenBy(_ => rnd?.Next() ?? 0),
            _                                      => parts.OrderByDescending(p => p.Area).ThenBy(_ => rnd?.Next() ?? 0)
        };
        // Boundary-aware mode: parts with at least one edge at affinity
        // >= MinBoundaryAffinity come FIRST. Within each group, the
        // existing sort key above breaks ties. When boundary mode is off
        // (MaxEdgeAffinity == 0 for every part) every part falls into the
        // "not-worthy" group and the prepended sort is a no-op.
        var list = q.ToList();
        if (_boundaryMode > 0)
        {
            list = list
                .OrderByDescending(p => p.MaxEdgeAffinity >= _minBoundaryAffinity ? 1 : 0)
                .ThenByDescending(p => p.MaxEdgeAffinity)
                .ToList();
        }
        return list;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    // ─── Inner types ──────────────────────────────────────────────────────────

    private sealed class Poly2d
    {
        public readonly double[] Vx, Vy;
        public readonly int N;
        public readonly double MinX, MinY, MaxX, MaxY;
        public readonly double Cx, Cy, Area;

        private Poly2d(double[] vx, double[] vy)
        {
            Vx = vx; Vy = vy; N = vx.Length;
            double mnX = double.MaxValue, mnY = double.MaxValue;
            double mxX = double.MinValue, mxY = double.MinValue;
            double cx = 0, cy = 0, area = 0;
            for (var i = 0; i < N; i++)
            {
                if (vx[i] < mnX) mnX = vx[i]; if (vy[i] < mnY) mnY = vy[i];
                if (vx[i] > mxX) mxX = vx[i]; if (vy[i] > mxY) mxY = vy[i];
                var j     = (i + 1) % N;
                var cross = vx[i] * vy[j] - vx[j] * vy[i];
                cx   += (vx[i] + vx[j]) * cross;
                cy   += (vy[i] + vy[j]) * cross;
                area += cross;
            }
            MinX = mnX; MinY = mnY; MaxX = mxX; MaxY = mxY;
            area *= 0.5; Area = Math.Abs(area);
            if (Area > 1e-20) { cx /= 6 * area; cy /= 6 * area; }
            else { cx = (mnX + mxX) * 0.5; cy = (mnY + mxY) * 0.5; }
            Cx = cx; Cy = cy;
        }

        public static Poly2d? Build(double[] vx, double[] vy)
            => vx.Length < 3 ? null : new Poly2d(vx, vy);
    }

    private sealed class RotEntry
    {
        public readonly Poly2d Poly;
        public readonly double RotMinX, RotMinY;
        // Boundary-aware mode: midpoint of the part's matched edge after this
        // rotation is applied (in the rotEntry's normalized origin frame, i.e.
        // the same frame as Poly.Vx/Vy). Used by OrderCandidates / IsBetter
        // to bias placements toward the matched boundary interval centroid.
        // (NaN, NaN) when the part has no boundary affinity or boundary mode
        // is off — sentinel that disables the bias for this rotation.
        public readonly double MatchedMidX, MatchedMidY;
        public RotEntry(Poly2d poly, double rx, double ry, double mmx, double mmy)
        {
            Poly = poly;
            RotMinX = rx; RotMinY = ry;
            MatchedMidX = mmx; MatchedMidY = mmy;
        }
    }

    private sealed class SheetData
    {
        public int Index;
        public Poly2d Outer = null!;
        public List<Poly2d> Holes = new();
        public Transform WorkToSheet;
        public Curve OuterRhinoCurve = null!;
        public List<Curve> HoleRhinoCurves = new();
        public double BBoxMinX, BBoxMinY, BBoxMaxX, BBoxMaxY;
        // Boundary-aware mode: WorkXY-space curves used by the deferred
        // BuildBoundaryIndexes pass (Half C). Stored only when mode > 0;
        // null on the geometric path. The window length is computed from
        // the median part edge length so the index buckets align with
        // actual part scale.
        public Curve? OuterWorkCurve;
        public List<Curve> HoleWorkCurves = new();
        public BoundaryRailIndex<BoundaryIntervalInfo>? BoundaryIndex;
        public double BoundaryLengthBucket;
    }

    private sealed class PartData
    {
        public int SourceIndex;
        public Curve SourceCurve = null!;
        public Poly2d ExactAtOrigin = null!;
        public Transform PlaneToWork;   // PlaneToPlane(sourcePlane, WorldXY)
        public Transform NormalizeTx;   // Translation(-bb.Min.X, -bb.Min.Y, 0)
        public double Width, Height, Area;
        // Boundary-aware mode. MaxEdgeAffinity is the best edge-match score
        // across every (sheet, edge) pair; >= _minBoundaryAffinity marks the
        // part as "boundary-worthy" and triggers the sort + bias paths.
        // BestMatchedEdgeIndex is the index of the part edge that scored
        // best (used to compute the rotation snap angle and the matched
        // midpoint per rotation). BestMatchedInterval is the boundary
        // interval that scored best (used to read AverageTangent for the
        // snap rotation, and LocalBounds.Center as the candidate-bias
        // anchor). BestMatchedSheetIndex is the index of the sheet whose
        // boundary contains that interval; the part's placement bias is
        // anchored only when it lands on this sheet.
        public double MaxEdgeAffinity;
        public int BestMatchedEdgeIndex = -1;
        public int BestMatchedSheetIndex = -1;
        public BoundaryIntervalInfo? BestMatchedInterval;
        // Per-part snap rotations, computed from the best-matched edge ↔
        // boundary tangent angle, capped at MaxSnapRotationsPerPart, rounded
        // to integer degrees for determinism. Empty when no high-affinity
        // match exists.
        public List<double> SnapRotationsDeg = new();
        // Half C: pre-built fragment descriptor (built in PreparePart when
        // mode > 0) so BuildBoundaryIndexes can compute the median edge
        // length across all parts before per-sheet indexes are built. Null
        // on the geometric path.
        public FragmentDescriptor? Descriptor;
    }

    private sealed class PlacedPoly
    {
        public readonly Poly2d Poly;
        public readonly double OriginX, OriginY;
        public readonly double MinX, MinY, MaxX, MaxY;
        // Boundary-aware mode: world-space midpoint of the matched edge for
        // this rotation+placement. NaN when no boundary affinity. Used by
        // IsBetter to compare distance-to-boundary-anchor.
        public readonly double MatchedMidX, MatchedMidY;
        public int ListIdx;

        public PlacedPoly(Poly2d poly, double ox, double oy, double mmx = double.NaN, double mmy = double.NaN)
        {
            Poly    = poly;
            OriginX = ox; OriginY = oy;
            MinX = poly.MinX + ox; MinY = poly.MinY + oy;
            MaxX = poly.MaxX + ox; MaxY = poly.MaxY + oy;
            MatchedMidX = double.IsNaN(mmx) ? double.NaN : mmx + ox;
            MatchedMidY = double.IsNaN(mmy) ? double.NaN : mmy + oy;
        }
    }

    private sealed class Grid2d
    {
        private readonly double _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public Grid2d(double cellSize) => _cell = Math.Max(cellSize, 1e-6);

        private int Cx(double x) => (int)Math.Floor(x / _cell);
        private int Cy(double y) => (int)Math.Floor(y / _cell);
        private static long Key(int cx, int cy) => (long)(cx + 500000) * 1000001L + (cy + 500000);

        public void Insert(int idx, double minX, double minY, double maxX, double maxY)
        {
            for (var cx = Cx(minX); cx <= Cx(maxX); cx++)
                for (var cy = Cy(minY); cy <= Cy(maxY); cy++)
                {
                    var k = Key(cx, cy);
                    if (!_cells.TryGetValue(k, out var list))
                    { list = new List<int>(4); _cells[k] = list; }
                    list.Add(idx);
                }
        }

        public IEnumerable<int> Query(double minX, double minY, double maxX, double maxY)
        {
            var seen = new HashSet<int>();
            for (var cx = Cx(minX); cx <= Cx(maxX); cx++)
                for (var cy = Cy(minY); cy <= Cy(maxY); cy++)
                    if (_cells.TryGetValue(Key(cx, cy), out var list))
                        foreach (var idx in list) seen.Add(idx);
            return seen;
        }
    }
}
