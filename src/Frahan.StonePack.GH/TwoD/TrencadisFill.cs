#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Frahan.Core;
using Frahan.Surface;
using Rhino;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Trencadís packer (F-2D-002).
///
/// Trencadís ("broken-tile" technique, Gaudí, Park Güell): irregular
/// ceramic shards placed close together, edges butting up against each
/// other. Pieces are explicitly *meant* to be chipped (re-shaped) at
/// the contact so they fit. Grout fills the gaps.
///
/// Algorithmic contract (differs from V506):
///   • Trim is mandatory. Greedy placement allows bounded overlap up to
///     `Trim Tolerance` (depth-based safety) AND the Battiato 2013
///     §4 cumulative-cut budget (T_N for new piece, T_P for placed).
///     Rejects placements that would breach EITHER cap.
///   • CVD-Lloyd seed init (F-2D-002.2 wiki rec) anchors a per-sheet
///     blue-noise distribution; first piece per region settles near
///     a Lloyd centroid instead of a bbox corner.
///   • GVF orientation (F-2D-002.3 wiki rec) gives a continuous tile-
///     angle oracle. Per-candidate rotations get a score bonus when
///     they align with the local GVF tangent; pieces follow boundary
///     curves as the field propagates inward.
///   • Trim post-pass uses Curve.CreateBooleanDifference (earlier
///     wins, later gets chipped). Grout offset post-trim leaves the
///     mortar gap.
///
/// Self-contained: Poly2d / SheetData / PartData / PlacedPoly types
/// are duplicated from V506 (stripped of boundary-aware fields). After
/// F-2D-003 rollback both solvers can share a primitives helper.
///
/// Boundary-following modes (F-2D-002.E, 2026-05-07) — same semantics
/// as V506 Half D-I, ported in:
///   • Mode 0: off (interior fill only).
///   • Mode 1: bias — boundary-worthy parts placed first AND auto-rotated
///     to align with matched boundary tangent. All candidate sources used.
///   • Mode 2: strict two-phase ring/interior. Boundary-worthy parts use
///     ONLY boundary-anchor candidates first; non-boundary parts fill
///     interior. Falls back to all candidates if a phase saturates.
///   • Mode 3: uniform curve division — divide outer + holes by arc
///     length, place each part with longest edge tangent to the curve at
///     its assigned position. Most predictable ring layout.
/// </summary>
public sealed class TrencadisFill
{
    private const int ExactMaxVerts = 256;
    private const int MaxValidPerRot = 24;
    private const int MaxValidPerRotBoundary = 256;
    private const double TrimFloor = 1e-4;
    // F-2D-002.F1 (NFP boundary ring): density of arc-length slide samples
    // per part-charLen along outer + holes during Mode 2 phase 1.
    private const int NfpSlideMaxSamples = 256;
    // F-2D-002.F1 score reweighting in Mode 2 phase 1: boundary-worthy parts
    // should win edge-of-sheet positions even after interior pieces start
    // landing nearby. Boost sheet-edge contact and damp neighbour contact.
    private const double Phase1SheetEdgeWeight = 2.5;
    private const double Phase1NeighbourWeight = 0.3;
    // F-2D-002.F2 (edge-pyramid): edges are considered aligned when |cos θ|
    // exceeds this. 0.95 ≈ 18° tolerance — strict enough that "almost
    // parallel" still counts, lax enough that chipping-fit candidates
    // don't all fall to round-off.
    private const double EdgePairCosThreshold = 0.95;
    // Edge-pair score weight relative to point-to-edge contact. Tuned so
    // a perfectly parallel adjacent edge of length L contributes ~ L (the
    // same magnitude as L vertices each at zero distance from a neighbour
    // edge).
    private const double EdgePairScoreWeight = 1.0;
    // F-2D-002.F3 (CDT-style occupancy pre-filter): grid resolution per sheet
    // bbox. 64 cells per axis = 4096 cells total. Skip candidates whose bbox
    // fits entirely inside cells that already host at least this many pieces.
    private const int OccupancyGridRes = 64;
    private const int OccupancyOccludedThreshold = 1;
    // F-2D-002.F6 variety penalty: a placement is "3-in-a-row" with its two
    // closest neighbours if those neighbours sit roughly opposite each other
    // around the candidate. The dot product threshold gates the test:
    // -0.92 ≈ within 23° of collinear-opposite. Penalty magnitude is a
    // fraction of perimeter so it competes with contact scores.
    private const double VarietyAntiParallelThreshold = -0.92;
    private const double VarietyPenaltyFactor = 0.4;
    // Battiato 2013 Eq. 6: R = T_P / T_N = 1/2, R_N = S_N / T_N = 1/2.
    // Single budget input → derive all four caps.
    private const double TpFraction = 0.5;
    private const double SnFraction = 0.5;
    private const double SpFraction = 0.25;
    // Boundary-aware constants (V506 Half D-I values).
    private const double BoundaryAngleBucketDeg = 15.0;
    private const double BoundaryCurvBucket = 0.1;
    private const int BoundaryLengthRadius = 1;
    private const int BoundaryAngleRadius = 1;
    private const int MaxSnapRotationsPerPart = 4;
    private const int BoundaryTopK = 16;
    private const int BoundaryMaxCandidates = 10000;
    private const double InvGolden = 0.6180339887498949;

    private readonly double _spacing;
    private readonly double _tol;
    private readonly List<double> _rotDeg;
    private readonly int _seed;
    private readonly int _maxCandidates;
    private readonly int _boundaryMode;
    private readonly double _minBoundaryAffinity;
    private readonly double _trimTolerance;
    private readonly double _grout;
    private readonly double _cutBudget;
    private readonly bool _useCvdSeeds;
    private readonly bool _useGvf;
    private readonly double _gvfMu;
    private readonly int _gvfIterations;
    private readonly int _gvfGridRes;
    private readonly List<SheetData> _sheets;

    // Trim accounting. Filled as parts are accepted with overlap; consumed
    // by ApplyTrimPostPass. (earlierPackedIdx, laterPackedIdx, overlapArea).
    private readonly List<(int earlier, int later, double overlapArea)> _overlapPairs = new();
    private readonly Dictionary<int, List<int>> _placedPackedIdxBySheet = new();

    public TrencadisFill(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double> rotationsDeg,
        double tolerance,
        int seed,
        int maxCandidates,
        double trimTolerance,
        double grout,
        int boundaryMode = 0,
        double minBoundaryAffinity = 0.5,
        double cutBudget = 0.35,
        bool useCvdSeeds = true,
        bool useGvf = true,
        double gvfMu = 0.2,
        int gvfIterations = 80,
        int gvfGridRes = 48)
    {
        _spacing             = Math.Max(0.0, spacing);
        _tol                 = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        _rotDeg              = (rotationsDeg ?? new[] { 0.0, 45.0, 90.0, 135.0 }).ToList();
        if (_rotDeg.Count == 0) _rotDeg.Add(0.0);
        _seed                = seed;
        _maxCandidates       = maxCandidates > 0 ? maxCandidates : 600;
        _trimTolerance       = Math.Max(TrimFloor, trimTolerance);
        _grout               = Math.Max(0.0, grout);
        _boundaryMode        = boundaryMode;
        _minBoundaryAffinity = Math.Max(0.0, Math.Min(1.0, minBoundaryAffinity));
        _cutBudget           = Math.Max(0.0, Math.Min(0.99, cutBudget));
        _useCvdSeeds         = useCvdSeeds;
        _useGvf              = useGvf;
        _gvfMu               = Math.Max(0.01, gvfMu);
        _gvfIterations       = Math.Max(0, gvfIterations);
        _gvfGridRes          = Math.Max(8, gvfGridRes);

        var outerList = (sheetOutlines ?? Enumerable.Empty<Curve>()).ToList();
        _sheets = new List<SheetData>(outerList.Count);
        for (int i = 0; i < outerList.Count; i++)
        {
            var holes = (sheetHoles != null && i < sheetHoles.Count)
                ? sheetHoles[i]
                : (IReadOnlyList<Curve>)new List<Curve>();
            var sd = PrepareSheet(outerList[i], holes, i);
            if (sd != null) _sheets.Add(sd);
        }
    }

    public PackingResult Pack(IEnumerable<Curve> parts)
    {
        var sw = Stopwatch.StartNew();
        var partsList = (parts ?? Enumerable.Empty<Curve>()).ToList();
        var result = new PackingResult { InputCount = partsList.Count };

        foreach (var s in _sheets)
            result.SheetPreviewCurves.Add(s.OuterRhinoCurve.DuplicateCurve());

        if (_sheets.Count == 0)
        {
            result.Report = "Trencadís: no valid sheets.";
            return result;
        }

        var prepared = new List<PartData>(partsList.Count);
        for (int i = 0; i < partsList.Count; i++)
        {
            var pd = PreparePart(partsList[i], i);
            if (pd == null) result.InvalidCount++;
            else prepared.Add(pd);
        }
        result.PreparedCount = prepared.Count;

        // Boundary-aware: build per-sheet rail indexes (modes 1, 2) and
        // compute per-part anchor affinity. Mode 3 uses curve-division
        // directly and does not need the index.
        if (_boundaryMode > 0 && _boundaryMode != 3)
        {
            BuildBoundaryIndexes(prepared);
            foreach (var part in prepared)
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

        // Per-sheet: build CVD seeds + GVF field (F-2D-002.2 + .3).
        // K seeds per sheet ≈ proportional area share of total parts.
        var totalSheetArea = _sheets.Sum(s => s.Outer.Area - s.Holes.Sum(h => h.Area));
        foreach (var sheet in _sheets)
        {
            var sheetArea = sheet.Outer.Area - sheet.Holes.Sum(h => h.Area);
            var sheetK = totalSheetArea > 0
                ? Math.Max(1, (int)Math.Ceiling(prepared.Count * sheetArea / totalSheetArea))
                : prepared.Count;

            if (_useCvdSeeds)
            {
                var holesAsTuples = (IList<(double[] vx, double[] vy)>)sheet.Holes
                    .Select(h => (h.Vx, h.Vy)).ToList();
                sheet.CvdSeeds = CvdLloyd2d.GenerateSeeds(
                    sheet.Outer.Vx, sheet.Outer.Vy,
                    holesAsTuples,
                    sheet.BBoxMinX, sheet.BBoxMinY, sheet.BBoxMaxX, sheet.BBoxMaxY,
                    K: sheetK, iterations: 20, gridRes: 64,
                    seed: _seed == 0 ? sheet.Index + 1 : _seed + sheet.Index);
            }

            if (_useGvf && _gvfIterations > 0)
            {
                var holesAsTuples = (IList<(double[] vx, double[] vy)>)sheet.Holes
                    .Select(h => (h.Vx, h.Vy)).ToList();
                sheet.GvfField = Gvf2d.Compute(
                    sheet.Outer.Vx, sheet.Outer.Vy,
                    holesAsTuples,
                    sheet.BBoxMinX, sheet.BBoxMinY, sheet.BBoxMaxX, sheet.BBoxMaxY,
                    gridRes: _gvfGridRes, mu: _gvfMu, iterations: _gvfIterations);
            }
        }

        // Sort: boundary-worthy first (modes 1 & 2), then area desc.
        var rnd = _seed == 0 ? null : new Random(_seed);
        IEnumerable<PartData> q = prepared
            .OrderByDescending(p => p.Area)
            .ThenBy(p => rnd?.Next() ?? 0);
        if (_boundaryMode == 1 || _boundaryMode == 2)
        {
            q = q.OrderByDescending(p => p.MaxEdgeAffinity >= _minBoundaryAffinity ? 1 : 0)
                 .ThenByDescending(p => p.MaxEdgeAffinity)
                 .ThenByDescending(p => p.Area);
        }
        prepared = q.ToList();

        var rotCache = BuildRotationCache(prepared);

        // Mode 3 pre-pass: uniform curve division.
        var placedSourceIndices = new HashSet<int>();
        if (_boundaryMode == 3)
            DoCurveDivisionPlacement(prepared, result, placedSourceIndices);

        // Greedy placement.
        foreach (var part in prepared)
        {
            if (placedSourceIndices.Contains(part.SourceIndex)) continue;

            // Mode 2: phase 1 = boundary-anchor candidates only for
            // boundary-worthy parts; phase 2 = interior only for the rest.
            // Modes 0/1/3 always use phase = 0 (all sources).
            var targetPhase = 0;
            if (_boundaryMode == 2)
                targetPhase = part.MaxEdgeAffinity >= _minBoundaryAffinity ? 1 : 2;

            (PlacedPoly placed, double deg, int sheetIdx, List<(int idx, double area)> overlaps) winner =
                (null, 0, -1, null);
            double winnerScore = double.NegativeInfinity;

            foreach (var sheet in _sheets)
            {
                var (best, bestScore, deg, overlaps) = FindBestPlacement(part, sheet, rotCache, result, targetPhase);
                if (best == null) continue;
                if (bestScore > winnerScore)
                {
                    winnerScore = bestScore;
                    winner = (best, deg, sheet.Index, overlaps);
                }
            }

            // Mode 2 fallback: phase-restricted attempt failed → retry with
            // all candidate sources before giving up.
            if (winner.placed == null && _boundaryMode == 2 && targetPhase != 0)
            {
                foreach (var sheet in _sheets)
                {
                    var (best, bestScore, deg, overlaps) = FindBestPlacement(part, sheet, rotCache, result, 0);
                    if (best == null) continue;
                    if (bestScore > winnerScore)
                    {
                        winnerScore = bestScore;
                        winner = (best, deg, sheet.Index, overlaps);
                    }
                }
            }

            if (winner.placed == null)
            {
                result.UnplacedCurves.Add(part.SourceCurve);
                result.FailureReasons.Add("No valid placement (sheet full, part too large, or trim budget exhausted).");
                continue;
            }

            CommitPlacement(part, winner.placed, winner.deg, winner.sheetIdx, winner.overlaps, result);
        }

        // F-2D-002.F5 annealing: settle each placed piece into its best-
        // contact neighbourhood before the trim post-pass.
        ApplyAnnealingPostPass(result);

        ApplyTrimPostPass(result);
        if (_grout > _tol) ApplyGroutOffset(result);

        sw.Stop();
        result.RuntimeMilliseconds = sw.ElapsedMilliseconds;
        result.Report = BuildReport(result);
        return result;
    }

    // ─── Sheet / part preparation ────────────────────────────────────────

    private SheetData PrepareSheet(Curve outer, IReadOnlyList<Curve> holes, int index)
    {
        if (outer == null || !outer.IsClosed) return null;
        var ptol = Math.Max(_tol, 0.01);
        if (!outer.IsPlanar(ptol) || !outer.TryGetPlane(out var plane, ptol))
        {
            if (!outer.TryGetPlane(out plane, Math.Max(ptol * 100, 1.0))) return null;
        }

        var toWork = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var toSheet = Transform.PlaneToPlane(Plane.WorldXY, plane);

        var outerWork = outer.DuplicateCurve();
        outerWork.Transform(toWork);

        var outerPoly = CurveToPoly2dAdaptive(outerWork);
        if (outerPoly == null || outerPoly.N < 3) return null;

        var holePolys = new List<Poly2d>();
        var holeRhinoCurves = new List<Curve>();
        foreach (var hole in holes ?? new List<Curve>())
        {
            if (hole == null || !hole.IsClosed) continue;
            var hw = hole.DuplicateCurve();
            hw.Transform(toWork);
            var hp = CurveToPoly2dAdaptive(hw);
            if (hp != null && hp.N >= 3)
            {
                holePolys.Add(hp);
                holeRhinoCurves.Add(hole.DuplicateCurve());
            }
        }

        var bb = outerWork.GetBoundingBox(true);
        var sd = new SheetData
        {
            Index = index,
            Plane = plane,
            Outer = outerPoly,
            Holes = holePolys,
            WorkToSheet = toSheet,
            OuterRhinoCurve = outer.DuplicateCurve(),
            HoleRhinoCurves = holeRhinoCurves,
            BBoxMinX = bb.Min.X,
            BBoxMinY = bb.Min.Y,
            BBoxMaxX = bb.Max.X,
            BBoxMaxY = bb.Max.Y,
        };

        // F-2D-002.F3 occupancy pre-filter grid.
        sd.OccupancyCounts = new int[OccupancyGridRes, OccupancyGridRes];
        sd.OccupancyCellW = (sd.BBoxMaxX - sd.BBoxMinX) / OccupancyGridRes;
        sd.OccupancyCellH = (sd.BBoxMaxY - sd.BBoxMinY) / OccupancyGridRes;

        // Boundary-aware: stash WorkXY curves so BuildBoundaryIndexes (for
        // modes 1/2) and DoCurveDivisionPlacement (mode 3) can use them.
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

    private PartData PreparePart(Curve curve, int sourceIndex)
    {
        if (curve == null || !curve.IsClosed) return null;
        var ptol = Math.Max(_tol, 0.01);
        if (!curve.IsPlanar(ptol) || !curve.TryGetPlane(out var plane, ptol)) return null;

        var planeToWork = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var wc = curve.DuplicateCurve();
        wc.Transform(planeToWork);

        var bb = wc.GetBoundingBox(true);
        var normalizeTx = Transform.Translation(-bb.Min.X, -bb.Min.Y, 0);
        wc.Transform(normalizeTx);

        var poly = CurveToPoly2dAdaptive(wc);
        if (poly == null || poly.N < 3 || poly.Area <= _tol * _tol) return null;

        var part = new PartData
        {
            SourceIndex = sourceIndex,
            SourceCurve = curve.DuplicateCurve(),
            ExactAtOrigin = poly,
            PlaneToWork = planeToWork,
            NormalizeTx = normalizeTx,
            Width = poly.MaxX - poly.MinX,
            Height = poly.MaxY - poly.MinY,
            Area = poly.Area,
        };

        // Boundary-aware: build FragmentDescriptor so BuildBoundaryIndexes
        // can median-tune window length, and ComputeBoundaryAffinity can
        // run the rail matcher.
        if (_boundaryMode > 0 && _boundaryMode != 3)
        {
            try
            {
                part.Descriptor = FragmentDescriptorBuilder.BuildFromCurve(
                    id: $"trencadis_part_{part.SourceIndex}",
                    boundary: wc,
                    zoneId: 0,
                    discretisationTolerance: Math.Max(_tol, 0.01));
            }
            catch { part.Descriptor = null; }
        }
        else if (_boundaryMode == 3)
        {
            // Mode 3 needs the descriptor too — for FindLongestEdgeIdx.
            try
            {
                part.Descriptor = FragmentDescriptorBuilder.BuildFromCurve(
                    id: $"trencadis_part_{part.SourceIndex}",
                    boundary: wc,
                    zoneId: 0,
                    discretisationTolerance: Math.Max(_tol, 0.01));
            }
            catch { part.Descriptor = null; }
        }

        return part;
    }

    private Poly2d CurveToPoly2dAdaptive(Curve curve)
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

        var chord = Math.Max(_tol, 1e-3);
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

    // ─── Rotation cache ──────────────────────────────────────────────────

    private Dictionary<(int, int), RotEntry> BuildRotationCache(List<PartData> parts)
    {
        var cache = new Dictionary<(int, int), RotEntry>(parts.Count * (_rotDeg.Count + MaxSnapRotationsPerPart));
        foreach (var part in parts)
        {
            foreach (var deg in _rotDeg)
                cache[(part.SourceIndex, AngleKey(deg))] = RotatePoly(part.ExactAtOrigin, deg, part.BestMatchedEdgeIndex);
            // Boundary-aware: also cache per-part snap rotations.
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
        // Snap rotations come first so high-affinity edge gets evaluated
        // against snap angle before any user rotation.
        var combined = new List<double>(_rotDeg.Count + part.SnapRotationsDeg.Count);
        combined.AddRange(part.SnapRotationsDeg);
        combined.AddRange(_rotDeg);
        return combined;
    }

    private static RotEntry RotatePoly(Poly2d src, double angleDeg, int matchedEdgeIndex = -1)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var n = src.N;
        var rx = new double[n];
        var ry = new double[n];
        double minX = double.MaxValue, minY = double.MaxValue;
        for (var i = 0; i < n; i++)
        {
            rx[i] = cos * src.Vx[i] - sin * src.Vy[i];
            ry[i] = sin * src.Vx[i] + cos * src.Vy[i];
            if (rx[i] < minX) minX = rx[i];
            if (ry[i] < minY) minY = ry[i];
        }
        for (var i = 0; i < n; i++) { rx[i] -= minX; ry[i] -= minY; }

        double mmx = double.NaN, mmy = double.NaN;
        if (matchedEdgeIndex >= 0 && matchedEdgeIndex < n)
        {
            var j = (matchedEdgeIndex + 1) % n;
            mmx = 0.5 * (rx[matchedEdgeIndex] + rx[j]);
            mmy = 0.5 * (ry[matchedEdgeIndex] + ry[j]);
        }
        return new RotEntry(Poly2d.Build(rx, ry), minX, minY, mmx, mmy);
    }

    private static int AngleKey(double deg) => (int)Math.Round(deg * 1000.0);

    // ─── Placement search ────────────────────────────────────────────────

    private (PlacedPoly best, double bestScore, double deg, List<(int idx, double area)> overlaps)
        FindBestPlacement(
            PartData part, SheetData sheet,
            Dictionary<(int, int), RotEntry> rotCache, PackingResult result,
            int candidatePhase = 0)
    {
        PlacedPoly best = null;
        double bestDeg = 0;
        double bestScore = double.NegativeInfinity;
        List<(int idx, double area)> bestOverlaps = null;

        // Boundary-aware: only this part's boundary-anchored sheet can
        // bias rotations; other sheets get the geometric ordering.
        var hasAnchor = _boundaryMode > 0
                        && part.BestMatchedSheetIndex == sheet.Index
                        && part.BestMatchedInterval != null;

        foreach (var deg in RotationsForPart(part))
        {
            if (!rotCache.TryGetValue((part.SourceIndex, AngleKey(deg)), out var re)) continue;

            var (p, score, overlaps) = EvaluateRotation(part, re, deg, sheet, result, candidatePhase, hasAnchor, _boundaryMode);
            if (p == null) continue;
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
                bestDeg = deg;
                bestOverlaps = overlaps;
            }
        }

        return (best, bestScore, bestDeg, bestOverlaps);
    }

    private (PlacedPoly best, double bestScore, List<(int idx, double area)> overlaps)
        EvaluateRotation(
            PartData part, RotEntry re, double rotDeg,
            SheetData sheet, PackingResult result,
            int candidatePhase = 0, bool hasAnchor = false, int boundaryMode = 0)
    {
        var rotPoly = re.Poly;
        var w = rotPoly.MaxX - rotPoly.MinX;
        var h = rotPoly.MaxY - rotPoly.MinY;

        // Battiato cut budget caps for THIS part.
        var tNCap = _cutBudget * part.Area;
        var sNCap = _cutBudget * SnFraction * part.Area;

        PlacedPoly best = null;
        double bestScore = double.NegativeInfinity;
        List<(int idx, double area)> bestOverlaps = null;

        var placedOnSheet = sheet.Placed;
        // Boundary-anchored rotations get a larger explore budget.
        var maxValidThis = (hasAnchor && !double.IsNaN(re.MatchedMidX))
            ? MaxValidPerRotBoundary
            : MaxValidPerRot;

        var valid = 0;
        foreach (var (ox, oy) in GenerateCandidates(sheet, rotPoly, placedOnSheet, candidatePhase))
        {
            result.CandidateCount++;
            if (ox < sheet.BBoxMinX - _tol || oy < sheet.BBoxMinY - _tol ||
                ox + w > sheet.BBoxMaxX + _tol || oy + h > sheet.BBoxMaxY + _tol)
                continue;
            if (!ContainedInSheet(rotPoly, ox, oy, sheet)) continue;

            // Collisions: depth-cap + Battiato area-budget caps.
            var overlaps = new List<(int idx, double area)>();
            var cumulativeForNew = 0.0;
            var rejected = false;
            for (int i = 0; i < placedOnSheet.Count; i++)
            {
                var p = placedOnSheet[i];
                if (!BBoxOverlap(rotPoly.MinX + ox, rotPoly.MinY + oy,
                                  rotPoly.MaxX + ox, rotPoly.MaxY + oy,
                                  p.MinX, p.MinY, p.MaxX, p.MaxY, _spacing)) continue;
                result.CollisionCheckCount++;

                if (PolysActuallyOverlap(rotPoly, ox, oy, p.Poly, p.OriginX, p.OriginY))
                {
                    var pen = MaxPenetrationDepth(rotPoly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                    if (pen > _trimTolerance + _tol) { rejected = true; break; }

                    var area = ApproxOverlapArea(rotPoly, ox, oy, p.Poly, p.OriginX, p.OriginY);

                    // S_N: single-cut cap on the new piece.
                    if (area > sNCap) { rejected = true; break; }
                    // S_P: single-cut cap on placed piece.
                    if (area > _cutBudget * SpFraction * p.Area) { rejected = true; break; }
                    // T_P: cumulative cap on placed piece.
                    if (p.CumulativeTrimmedArea + area > _cutBudget * TpFraction * p.Area)
                    { rejected = true; break; }

                    cumulativeForNew += area;
                    overlaps.Add((p.ListIdx, area));
                }
                else if (_spacing > _tol)
                {
                    var minDist = MinDistPolys(rotPoly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                    if (minDist < _spacing - _tol) { rejected = true; break; }
                }
            }
            if (rejected) continue;
            // T_N: cumulative cap across all overlaps for the new piece.
            if (cumulativeForNew > tNCap) continue;

            // Score = neighbour contact + boundary contact + GVF alignment bonus.
            // F-2D-002.F1: Mode 2 phase 1 reweights to favor sheet-edge contact,
            // making boundary-worthy parts actually ring before interior pieces
            // can outvote their boundary positions.
            var phase1 = boundaryMode == 2 && candidatePhase == 1;
            var score = ContactScoreAt(rotPoly, ox, oy, placedOnSheet, sheet,
                sheetEdgeWeight: phase1 ? Phase1SheetEdgeWeight : 0.5,
                neighbourWeight: phase1 ? Phase1NeighbourWeight : 1.0);
            score += GvfAlignmentScore(sheet, rotPoly, ox, oy, rotDeg);
            // CVD pull: pieces near a CVD seed get a small score bonus
            // (encourages each Lloyd-cell to absorb at least one piece).
            score += CvdProximityScore(sheet, rotPoly, ox, oy);
            // F-2D-002.F6 variety penalty: 3-in-a-row collinear placement
            // looks like a tile floor, not trencadís. Subtract a fraction of
            // perimeter when this candidate sits between two collinear
            // neighbours.
            score -= VarietyPenalty(rotPoly, ox, oy, placedOnSheet);

            if (score > bestScore)
            {
                bestScore = score;
                best = new PlacedPoly(rotPoly, ox, oy, re.RotMinX, re.RotMinY);
                bestOverlaps = overlaps;
            }

            valid++;
            if (valid >= maxValidThis) break;
        }

        return (best, bestScore, bestOverlaps);
    }

    private IEnumerable<(double ox, double oy)> GenerateCandidates(
        SheetData sheet, Poly2d rotPoly, List<PlacedPoly> placed, int candidatePhase = 0)
    {
        // candidatePhase: 0 = all (Mode 0/1, Mode 2 fallback).
        //                 1 = boundary anchors + already-placed only (Mode 2 phase 1).
        //                 2 = interior grid + already-placed only (Mode 2 phase 2).
        var includeBoundary = candidatePhase != 2;
        var includeInterior = candidatePhase != 1;
        var includeCvdSeeds = candidatePhase != 1;

        var w = rotPoly.MaxX - rotPoly.MinX;
        var h = rotPoly.MaxY - rotPoly.MinY;
        var raw = new List<(double, double)>(512);

        // CVD seed anchors — interior-distribution starting points.
        if (includeCvdSeeds && sheet.CvdSeeds != null)
            foreach (var (sx, sy) in sheet.CvdSeeds)
                raw.Add((sx - w * 0.5, sy - h * 0.5));

        if (includeBoundary)
        {
            raw.Add((sheet.BBoxMinX, sheet.BBoxMinY));
            raw.Add((sheet.BBoxMaxX - w, sheet.BBoxMinY));
            raw.Add((sheet.BBoxMinX, sheet.BBoxMaxY - h));
            raw.Add((sheet.BBoxMaxX - w, sheet.BBoxMaxY - h));

            var o = sheet.Outer;
            for (var i = 0; i < o.N; i++) AddCornerOffsets(raw, o.Vx[i], o.Vy[i], w, h);

            foreach (var hole in sheet.Holes)
                for (var i = 0; i < hole.N; i++) AddCornerOffsets(raw, hole.Vx[i], hole.Vy[i], w, h);

            // F-2D-002.F1: Mode 2 phase 1 NFP-style arc-length slide. Vertex
            // anchors alone leave gaps between curve vertices on smooth
            // boundaries (circles, splines), so the part can never settle
            // mid-arc. Sample along the curve at part-charLen spacing.
            if (candidatePhase == 1) AddNfpSlideCandidates(raw, sheet, w, h);
        }

        // Already-placed neighbour anchors fire regardless of phase.
        foreach (var p in placed)
        {
            var pv = p.Poly;
            raw.Add((p.MaxX + _tol, p.MinY));
            raw.Add((p.MinX, p.MaxY + _tol));
            raw.Add((p.MaxX + _tol, p.MaxY + _tol));
            raw.Add((p.MinX - w - _tol, p.MinY));
            raw.Add((p.MinX, p.MinY - h - _tol));

            var edgeStep = Math.Max(Math.Min(w, h) * 0.4, _tol * 4);
            if (!IsFinite(edgeStep) || edgeStep <= _tol) continue;
            for (var vi = 0; vi < pv.N; vi++)
            {
                var vj = (vi + 1) % pv.N;
                var dx = pv.Vx[vj] - pv.Vx[vi];
                var dy = pv.Vy[vj] - pv.Vy[vi];
                var elen = Math.Sqrt(dx * dx + dy * dy);
                if (elen < edgeStep * 1.5) continue;
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

        if (includeInterior)
        {
            var sheetSpan = Math.Max(sheet.BBoxMaxX - sheet.BBoxMinX, sheet.BBoxMaxY - sheet.BBoxMinY);
            var gridStep = Math.Max(Math.Min(w, h) * 0.3, Math.Max(sheetSpan / 100.0, _tol * 4));
            if (IsFinite(gridStep) && gridStep > _tol)
            {
                var maxPts = Math.Min(800, _maxCandidates);
                var cnt = 0;
                var ovx = sheet.Outer.Vx; var ovy = sheet.Outer.Vy; var on_ = sheet.Outer.N;
                for (var gy = sheet.BBoxMinY; gy <= sheet.BBoxMaxY - h + _tol && cnt < maxPts; gy += gridStep)
                    for (var gx = sheet.BBoxMinX; gx <= sheet.BBoxMaxX - w + _tol && cnt < maxPts; gx += gridStep)
                    {
                        if (PointInPoly(gx, gy, ovx, ovy, on_) &&
                            PointInPoly(gx + w, gy, ovx, ovy, on_) &&
                            PointInPoly(gx, gy + h, ovx, ovy, on_) &&
                            PointInPoly(gx + w, gy + h, ovx, ovy, on_))
                        { raw.Add((gx, gy)); cnt++; }
                    }
            }
        }

        // Boundary-anchored placement gets a larger candidate ceiling so
        // multiple parts targeting nearby anchors don't all converge.
        var maxCands = candidatePhase == 1
            ? Math.Max(BoundaryMaxCandidates, _maxCandidates)
            : _maxCandidates;

        var tol2 = Math.Max(_tol, 1e-6);
        var seen = new HashSet<long>();
        var filtered = new List<(double, double)>(raw.Count);
        foreach (var (ox, oy) in raw)
        {
            if (ox + w < sheet.BBoxMinX - _tol || oy + h < sheet.BBoxMinY - _tol ||
                ox > sheet.BBoxMaxX + _tol || oy > sheet.BBoxMaxY + _tol) continue;
            // F-2D-002.F3 CDT-style occupancy pre-filter — drop candidates
            // whose bbox sits entirely inside cells already hosting at
            // least OccupancyOccludedThreshold pieces. Saves the per-placed
            // collision checks that would otherwise reject the same
            // candidate at higher cost.
            if (OccupancyAllOccluded(sheet, ox, oy, ox + w, oy + h, OccupancyOccludedThreshold)) continue;
            var key = ((long)Math.Round(ox / tol2)) * 100000003L + (long)Math.Round(oy / tol2);
            if (!seen.Add(key)) continue;
            filtered.Add((ox, oy));
            if (filtered.Count >= maxCands) break;
        }

        // Order: closest to existing neighbours first when any placed.
        // Otherwise, closest to CVD seeds first.
        if (placed.Count > 0)
        {
            filtered.Sort((a, b) =>
            {
                var da = MinDistToPlaced(a.Item1 + (rotPoly.MinX + rotPoly.MaxX) * 0.5,
                                          a.Item2 + (rotPoly.MinY + rotPoly.MaxY) * 0.5, placed);
                var db = MinDistToPlaced(b.Item1 + (rotPoly.MinX + rotPoly.MaxX) * 0.5,
                                          b.Item2 + (rotPoly.MinY + rotPoly.MaxY) * 0.5, placed);
                return da.CompareTo(db);
            });
        }
        else if (sheet.CvdSeeds != null && sheet.CvdSeeds.Count > 0)
        {
            filtered.Sort((a, b) =>
            {
                var da = MinDistToSeeds(a.Item1 + (rotPoly.MinX + rotPoly.MaxX) * 0.5,
                                         a.Item2 + (rotPoly.MinY + rotPoly.MaxY) * 0.5, sheet.CvdSeeds);
                var db = MinDistToSeeds(b.Item1 + (rotPoly.MinX + rotPoly.MaxX) * 0.5,
                                         b.Item2 + (rotPoly.MinY + rotPoly.MaxY) * 0.5, sheet.CvdSeeds);
                return da.CompareTo(db);
            });
        }

        return filtered;
    }

    private static void AddCornerOffsets(List<(double, double)> list, double x, double y, double w, double h)
    {
        list.Add((x, y));
        list.Add((x - w, y));
        list.Add((x, y - h));
        list.Add((x - w, y - h));
    }

    // F-2D-002.F1: NFP-style boundary slide — sample along outer + holes at
    // part-charLen spacing, push inward by half-bbox + spacing so the part's
    // bbox sits flush against the boundary curve. Reuses the cached WorkXY
    // boundary curves from BuildBoundaryIndexes (requires Mode > 0).
    private void AddNfpSlideCandidates(
        List<(double, double)> raw, SheetData sheet, double w, double h)
    {
        if (sheet.OuterWorkCurve == null) return;
        var charLen = Math.Max(Math.Min(w, h), _tol * 10);
        var nudge = Math.Max(_spacing, _tol * 5);
        SampleAlongCurveInward(raw, sheet.OuterWorkCurve, w, h, charLen, nudge, isOuter: true);
        foreach (var hole in sheet.HoleWorkCurves)
            SampleAlongCurveInward(raw, hole, w, h, charLen, nudge, isOuter: false);
    }

    private void SampleAlongCurveInward(
        List<(double, double)> raw, Curve curve,
        double w, double h, double charLen, double nudge, bool isOuter)
    {
        if (curve == null) return;
        double len;
        try { len = curve.GetLength(); }
        catch { return; }
        if (!IsFinite(len) || len < charLen) return;

        var stepLen = Math.Max(charLen * 0.5, _tol * 4);
        var nSamples = Math.Min(NfpSlideMaxSamples, Math.Max(8, (int)Math.Ceiling(len / stepLen)));

        // Inward = left of tangent for CCW (outer convention) or right for CW
        // (typical hole). Sense flips if a curve is supplied with the
        // opposite handedness; ContainedInSheet filters those candidates out.
        var orient = curve.ClosedCurveOrientation(Plane.WorldXY);
        var ccw = orient == CurveOrientation.CounterClockwise;
        // Outer + ccw  → inward is left  (-Ty,  Tx)
        // Outer + cw   → inward is right ( Ty, -Tx)
        // Hole  + cw   → inward (into solid) is left (-Ty, Tx)
        // Hole  + ccw  → inward is right
        var leftIsInward = (isOuter && ccw) || (!isOuter && !ccw);

        for (int i = 0; i < nSamples; i++)
        {
            var arc = (i + 0.5) * len / nSamples;
            if (!curve.LengthParameter(arc, out var u)) continue;
            Point3d pt;
            Vector3d tan;
            try
            {
                pt = curve.PointAt(u);
                tan = curve.TangentAt(u);
            }
            catch { continue; }
            if (!tan.IsValid || tan.IsZero) continue;
            tan.Unitize();

            var nx = leftIsInward ? -tan.Y : tan.Y;
            var ny = leftIsInward ?  tan.X : -tan.X;

            var inwardOffset = charLen * 0.5 + nudge;
            var cx = pt.X + nx * inwardOffset;
            var cy = pt.Y + ny * inwardOffset;
            // Place bbox so part center sits at (cx, cy).
            raw.Add((cx - w * 0.5, cy - h * 0.5));
        }
    }

    private static double MinDistToPlaced(double cx, double cy, List<PlacedPoly> placed)
    {
        var min = double.MaxValue;
        foreach (var p in placed)
        {
            var pcx = (p.MinX + p.MaxX) * 0.5;
            var pcy = (p.MinY + p.MaxY) * 0.5;
            var d = Math.Sqrt((cx - pcx) * (cx - pcx) + (cy - pcy) * (cy - pcy));
            if (d < min) min = d;
        }
        return min;
    }

    private static double MinDistToSeeds(double cx, double cy, List<(double x, double y)> seeds)
    {
        var min = double.MaxValue;
        foreach (var s in seeds)
        {
            var d = Math.Sqrt((cx - s.x) * (cx - s.x) + (cy - s.y) * (cy - s.y));
            if (d < min) min = d;
        }
        return min;
    }

    // F-2D-002.F3 occupancy grid helpers.
    private static void OccupancyMark(SheetData sheet, double x0, double y0, double x1, double y1)
    {
        if (sheet.OccupancyCounts == null) return;
        if (sheet.OccupancyCellW <= 0 || sheet.OccupancyCellH <= 0) return;
        var i0 = Math.Max(0, (int)Math.Floor((x0 - sheet.BBoxMinX) / sheet.OccupancyCellW));
        var j0 = Math.Max(0, (int)Math.Floor((y0 - sheet.BBoxMinY) / sheet.OccupancyCellH));
        var i1 = Math.Min(OccupancyGridRes - 1, (int)Math.Floor((x1 - sheet.BBoxMinX) / sheet.OccupancyCellW));
        var j1 = Math.Min(OccupancyGridRes - 1, (int)Math.Floor((y1 - sheet.BBoxMinY) / sheet.OccupancyCellH));
        for (var i = i0; i <= i1; i++)
            for (var j = j0; j <= j1; j++)
                sheet.OccupancyCounts[i, j]++;
    }

    private static bool OccupancyAllOccluded(SheetData sheet, double x0, double y0, double x1, double y1, int threshold)
    {
        if (sheet.OccupancyCounts == null) return false;
        if (sheet.OccupancyCellW <= 0 || sheet.OccupancyCellH <= 0) return false;
        var i0 = Math.Max(0, (int)Math.Floor((x0 - sheet.BBoxMinX) / sheet.OccupancyCellW));
        var j0 = Math.Max(0, (int)Math.Floor((y0 - sheet.BBoxMinY) / sheet.OccupancyCellH));
        var i1 = Math.Min(OccupancyGridRes - 1, (int)Math.Floor((x1 - sheet.BBoxMinX) / sheet.OccupancyCellW));
        var j1 = Math.Min(OccupancyGridRes - 1, (int)Math.Floor((y1 - sheet.BBoxMinY) / sheet.OccupancyCellH));
        if (i1 < i0 || j1 < j0) return false;
        for (var i = i0; i <= i1; i++)
            for (var j = j0; j <= j1; j++)
                if (sheet.OccupancyCounts[i, j] < threshold) return false;
        return true;
    }

    // ─── Scoring (the trencadís differentiator) ─────────────────────────

    private double ContactScoreAt(Poly2d poly, double ox, double oy,
        List<PlacedPoly> placed, SheetData sheet,
        double sheetEdgeWeight = 0.5, double neighbourWeight = 1.0)
    {
        var threshold = _spacing + 2 * _trimTolerance + _tol;
        var score = 0.0;

        foreach (var p in placed)
        {
            if (!BBoxOverlap(poly.MinX + ox, poly.MinY + oy,
                              poly.MaxX + ox, poly.MaxY + oy,
                              p.MinX, p.MinY, p.MaxX, p.MaxY, threshold)) continue;
            score += EdgeContact(poly, ox, oy, p.Poly, p.OriginX, p.OriginY, threshold) * neighbourWeight;
            // F-2D-002.F2 edge-pyramid: parallel-edge alignment bonus on top
            // of point-to-edge contact. This is what makes chip-fit visible.
            score += EdgePairAlignment(poly, ox, oy, p.Poly, p.OriginX, p.OriginY, threshold) * neighbourWeight * EdgePairScoreWeight;
        }

        score += EdgeContact(poly, ox, oy, sheet.Outer, 0, 0, threshold) * sheetEdgeWeight;
        score += EdgePairAlignment(poly, ox, oy, sheet.Outer, 0, 0, threshold) * sheetEdgeWeight * EdgePairScoreWeight;
        foreach (var hole in sheet.Holes)
        {
            score += EdgeContact(poly, ox, oy, hole, 0, 0, threshold) * sheetEdgeWeight;
            score += EdgePairAlignment(poly, ox, oy, hole, 0, 0, threshold) * sheetEdgeWeight * EdgePairScoreWeight;
        }

        return score;
    }

    // F-2D-002.F2 edge-pyramid: pairs each edge of A with each edge of B,
    // and rewards edges that are (a) nearly parallel, (b) close in
    // perpendicular distance, (c) overlapping when projected onto the
    // common direction. Returns sum of overlap_length * (1 - perpDist/threshold)
    // over qualifying pairs.
    private static double EdgePairAlignment(
        Poly2d a, double aox, double aoy, Poly2d b, double box, double boy, double threshold)
    {
        var sum = 0.0;
        for (var i = 0; i < a.N; i++)
        {
            var ai = (i + 1) % a.N;
            var ax0 = a.Vx[i] + aox;  var ay0 = a.Vy[i] + aoy;
            var ax1 = a.Vx[ai] + aox; var ay1 = a.Vy[ai] + aoy;
            var adx = ax1 - ax0;       var ady = ay1 - ay0;
            var aLen = Math.Sqrt(adx * adx + ady * ady);
            if (aLen < 1e-9) continue;
            var aux = adx / aLen;      var auy = ady / aLen;

            for (var j = 0; j < b.N; j++)
            {
                var bj = (j + 1) % b.N;
                var bx0 = b.Vx[j] + box;   var by0 = b.Vy[j] + boy;
                var bx1 = b.Vx[bj] + box;  var by1 = b.Vy[bj] + boy;
                var bdx = bx1 - bx0;       var bdy = by1 - by0;
                var bLen = Math.Sqrt(bdx * bdx + bdy * bdy);
                if (bLen < 1e-9) continue;

                // Parallelism gate (cosine via dot product).
                var dot = (adx * bdx + ady * bdy) / (aLen * bLen);
                if (Math.Abs(dot) < EdgePairCosThreshold) continue;

                // Perpendicular distance from B's first endpoint to A's line.
                // A's normal = (-auy, aux).
                var perp = Math.Abs(-auy * (bx0 - ax0) + aux * (by0 - ay0));
                if (perp > threshold) continue;

                // Project both B endpoints onto A's tangent (parametrised
                // along A from 0 to aLen) and intersect with [0, aLen].
                var s0 = aux * (bx0 - ax0) + auy * (by0 - ay0);
                var s1 = aux * (bx1 - ax0) + auy * (by1 - ay0);
                var sMin = Math.Min(s0, s1);
                var sMax = Math.Max(s0, s1);
                var lo = Math.Max(0.0, sMin);
                var hi = Math.Min(aLen, sMax);
                if (hi <= lo) continue;
                var overlap = hi - lo;

                sum += overlap * (1.0 - perp / threshold);
            }
        }
        return sum;
    }

    private double GvfAlignmentScore(SheetData sheet, Poly2d poly, double ox, double oy, double rotDeg)
    {
        var field = sheet.GvfField;
        if (field == null || field.GridX == 0) return 0;
        var cx = ox + (poly.MinX + poly.MaxX) * 0.5;
        var cy = oy + (poly.MinY + poly.MaxY) * 0.5;
        var pref = field.OrientationDeg(cx, cy);
        if (pref == null) return 0;
        // Compare against rotation modulo 180 (square-symmetric tile).
        var d = ((rotDeg - pref.Value) % 180.0 + 180.0) % 180.0;
        if (d > 90) d = 180 - d;
        // F-2D-002.F4 GVF gap priority: scale alignment bonus by field
        // magnitude, capped to 1.0. Pieces in strong-field zones (near
        // boundary curves where GVF tension is highest) get a larger
        // alignment bonus, so the placer effectively walks the priority
        // queue from boundary inward as Battiato 2013 prescribes.
        var sample = field.Sample(cx, cy);
        var mag = Math.Min(1.0, Math.Sqrt(sample.u * sample.u + sample.w * sample.w));
        // Bonus: 1.0 at perfect alignment, 0 at orthogonal.
        // Magnitude tied to perimeter so it competes with contact score.
        var perim = 2 * (poly.MaxX - poly.MinX + poly.MaxY - poly.MinY);
        return (1.0 - d / 90.0) * perim * 0.25 * (0.25 + 0.75 * mag);
    }

    // F-2D-002.F6 variety penalty: detects when this candidate would sit
    // between two collinear-opposite neighbours, which makes a row of three
    // tiles. Scans neighbours within ~3 charLen, sorts by distance, and
    // checks the two closest. Returns a positive penalty when the dot
    // product of the two centre-to-neighbour vectors is < threshold (i.e.
    // they're nearly antiparallel = collinear-opposite).
    private static double VarietyPenalty(Poly2d poly, double ox, double oy, List<PlacedPoly> placed)
    {
        if (placed.Count < 2) return 0;
        var cx = ox + (poly.MinX + poly.MaxX) * 0.5;
        var cy = oy + (poly.MinY + poly.MaxY) * 0.5;
        var charLen = Math.Max(poly.MaxX - poly.MinX, poly.MaxY - poly.MinY);
        var maxDist = charLen * 3;

        // Inline two-closest scan (no allocation).
        double d1 = double.MaxValue, d2 = double.MaxValue;
        double v1x = 0, v1y = 0, v2x = 0, v2y = 0;
        foreach (var p in placed)
        {
            var pcx = (p.MinX + p.MaxX) * 0.5;
            var pcy = (p.MinY + p.MaxY) * 0.5;
            var dx = pcx - cx;
            var dy = pcy - cy;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d > maxDist || d < 1e-9) continue;
            if (d < d1)
            {
                d2 = d1; v2x = v1x; v2y = v1y;
                d1 = d;  v1x = dx;  v1y = dy;
            }
            else if (d < d2)
            {
                d2 = d; v2x = dx; v2y = dy;
            }
        }
        if (d2 == double.MaxValue) return 0;
        var dot = (v1x * v2x + v1y * v2y) / (d1 * d2);
        if (dot >= VarietyAntiParallelThreshold) return 0;
        var perim = 2 * (poly.MaxX - poly.MinX + poly.MaxY - poly.MinY);
        // Severity scales with how collinear they are: dot=-1 → full penalty,
        // dot=threshold → zero penalty.
        var severity = (VarietyAntiParallelThreshold - dot) / (1.0 + VarietyAntiParallelThreshold);
        return perim * VarietyPenaltyFactor * Math.Min(1.0, severity);
    }

    private static double CvdProximityScore(SheetData sheet, Poly2d poly, double ox, double oy)
    {
        if (sheet.CvdSeeds == null || sheet.CvdSeeds.Count == 0) return 0;
        var cx = ox + (poly.MinX + poly.MaxX) * 0.5;
        var cy = oy + (poly.MinY + poly.MaxY) * 0.5;
        var charLen = Math.Sqrt(poly.Area);
        var nearest = MinDistToSeeds(cx, cy, sheet.CvdSeeds);
        // Bonus inversely proportional to seed distance, normalised by
        // characteristic length so it doesn't swamp contact score.
        return charLen / (1.0 + nearest / Math.Max(charLen, 1e-6));
    }

    private static double EdgeContact(
        Poly2d a, double aox, double aoy, Poly2d b, double box, double boy, double threshold)
    {
        var sum = 0.0;
        for (var i = 0; i < a.N; i++)
        {
            var px = a.Vx[i] + aox;
            var py = a.Vy[i] + aoy;
            var min = double.MaxValue;
            for (var j = 0; j < b.N; j++)
            {
                int k = (j + 1) % b.N;
                var d = PointSegDist(px, py,
                    b.Vx[j] + box, b.Vy[j] + boy,
                    b.Vx[k] + box, b.Vy[k] + boy);
                if (d < min) min = d;
                if (min < 1e-9) break;
            }
            if (min < threshold) sum += threshold - min;
        }
        return sum;
    }

    // Grid-sampled overlap area estimation. Robust for non-convex polys;
    // ~400 PIP-pair calls per check at default n=20.
    internal static double ApproxOverlapArea(
        Poly2d a, double aox, double aoy, Poly2d b, double box, double boy, int n = 20)
    {
        var minX = Math.Max(a.MinX + aox, b.MinX + box);
        var minY = Math.Max(a.MinY + aoy, b.MinY + boy);
        var maxX = Math.Min(a.MaxX + aox, b.MaxX + box);
        var maxY = Math.Min(a.MaxY + aoy, b.MaxY + boy);
        if (maxX <= minX || maxY <= minY) return 0;
        var dx = (maxX - minX) / n;
        var dy = (maxY - minY) / n;
        if (dx <= 0 || dy <= 0) return 0;
        int count = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var px = minX + (i + 0.5) * dx;
                var py = minY + (j + 0.5) * dy;
                if (PointInPoly(px - aox, py - aoy, a.Vx, a.Vy, a.N) &&
                    PointInPoly(px - box, py - boy, b.Vx, b.Vy, b.N))
                    count++;
            }
        return count * dx * dy;
    }

    private void CommitPlacement(
        PartData part, PlacedPoly placed, double deg, int sheetIdx,
        List<(int idx, double area)> overlaps, PackingResult result)
    {
        var sheet = _sheets[sheetIdx];
        placed.ListIdx = sheet.Placed.Count;
        sheet.Placed.Add(placed);

        // F-2D-002.F3 occupancy update — mark cells covered by placed bbox.
        OccupancyMark(sheet, placed.MinX, placed.MinY, placed.MaxX, placed.MaxY);

        // Update cumulative trim accounting (Battiato §4 budget tracking).
        if (overlaps != null)
        {
            foreach (var (placedListIdx, area) in overlaps)
            {
                if (placedListIdx < 0 || placedListIdx >= sheet.Placed.Count - 1) continue;
                sheet.Placed[placedListIdx].CumulativeTrimmedArea += area;
                placed.CumulativeTrimmedArea += area;
            }
        }

        var rad = deg * Math.PI / 180.0;
        var rotation = Transform.Rotation(rad, Point3d.Origin);
        var normTx = Transform.Translation(-placed.RotMinX, -placed.RotMinY, 0);
        var moveTx = Transform.Translation(placed.OriginX, placed.OriginY, 0);
        var compound = sheet.WorkToSheet * moveTx * normTx * rotation * part.NormalizeTx * part.PlaneToWork;

        var outCurve = part.SourceCurve.DuplicateCurve();
        outCurve.Transform(part.PlaneToWork);
        outCurve.Transform(part.NormalizeTx);
        outCurve.Transform(rotation);
        outCurve.Transform(normTx);
        outCurve.Transform(moveTx);
        outCurve.Transform(sheet.WorkToSheet);

        var packedIdx = result.PackedCurves.Count;
        result.PackedCurves.Add(outCurve);
        result.Transforms.Add(compound);
        result.SourceIndices.Add(part.SourceIndex);
        result.SheetIndices.Add(sheetIdx);

        if (!_placedPackedIdxBySheet.TryGetValue(sheetIdx, out var list))
        {
            list = new List<int>();
            _placedPackedIdxBySheet[sheetIdx] = list;
        }
        list.Add(packedIdx);

        if (overlaps != null)
        {
            foreach (var (placedListIdx, area) in overlaps)
            {
                if (placedListIdx < 0 || placedListIdx >= list.Count - 1) continue;
                var earlierPackedIdx = list[placedListIdx];
                _overlapPairs.Add((earlierPackedIdx, packedIdx, area));
            }
        }
    }

    // ─── Annealing post-pass (F-2D-002.F5) ──────────────────────────────

    private void ApplyAnnealingPostPass(PackingResult result)
    {
        // Cheap settle: try 8 jitters per piece at two magnitudes, accept
        // the best one that improves contact score AND stays valid.
        // Bounded ~ N*16 collision checks per sheet — negligible vs the
        // greedy loop. No rotation jitter (rotated poly cache stays intact).
        for (int sheetIdx = 0; sheetIdx < _sheets.Count; sheetIdx++)
        {
            var sheet = _sheets[sheetIdx];
            if (sheet.Placed.Count < 2) continue;

            var mags = new[] { Math.Max(_spacing * 0.5, _tol * 4),
                               Math.Max(_spacing * 1.5, _tol * 12) };

            for (int pi = 0; pi < sheet.Placed.Count; pi++)
            {
                var piece = sheet.Placed[pi];
                var bestDx = 0.0;
                var bestDy = 0.0;
                var bestDelta = 0.0;
                var baseScore = ContactScoreSingle(piece, sheet, pi);

                foreach (var mag in mags)
                {
                    var dirs = new (double dx, double dy)[]
                    {
                        ( mag, 0), (-mag, 0), (0,  mag), (0, -mag),
                        ( mag,  mag), (-mag,  mag), ( mag, -mag), (-mag, -mag),
                    };
                    foreach (var (dx, dy) in dirs)
                    {
                        if (!CanJitter(piece, sheet, pi, dx, dy)) continue;
                        // Delta from "if I were at (origin+jitter)" — score uses
                        // the piece's poly + new origin; collision checks already
                        // gated above.
                        var newScore = ContactScoreAtCandidate(
                            piece.Poly, piece.OriginX + dx, piece.OriginY + dy,
                            sheet, excludeIdx: pi);
                        var delta = newScore - baseScore;
                        if (delta > bestDelta)
                        {
                            bestDelta = delta;
                            bestDx = dx;
                            bestDy = dy;
                        }
                    }
                }

                if (bestDx != 0 || bestDy != 0)
                {
                    piece.Translate(bestDx, bestDy);
                    // Mirror the translation in the output curve (sheet frame).
                    if (_placedPackedIdxBySheet.TryGetValue(sheetIdx, out var packedIdxList)
                        && pi < packedIdxList.Count)
                    {
                        var packedIdx = packedIdxList[pi];
                        if (packedIdx >= 0 && packedIdx < result.PackedCurves.Count)
                        {
                            var v = new Vector3d(bestDx, bestDy, 0);
                            v.Transform(sheet.WorkToSheet);
                            result.PackedCurves[packedIdx].Translate(v);
                            result.Transforms[packedIdx] = Transform.Translation(v) * result.Transforms[packedIdx];
                        }
                    }
                    result.AnnealingMoves++;
                }
            }
        }
    }

    private bool CanJitter(PlacedPoly piece, SheetData sheet, int selfIdx, double dx, double dy)
    {
        // Containment: shifted poly must remain in sheet (cheap vertex-in-poly).
        var newOx = piece.OriginX + dx;
        var newOy = piece.OriginY + dy;
        var poly = piece.Poly;
        if (!ContainedInSheet(poly, newOx, newOy, sheet)) return false;
        // Collision with all other placed pieces — penetration must not
        // exceed _trimTolerance, and we keep cumulative trim accounting
        // intact (don't introduce any new overlap that wasn't already there).
        for (int j = 0; j < sheet.Placed.Count; j++)
        {
            if (j == selfIdx) continue;
            var q = sheet.Placed[j];
            if (!BBoxOverlap(poly.MinX + newOx, poly.MinY + newOy,
                              poly.MaxX + newOx, poly.MaxY + newOy,
                              q.MinX, q.MinY, q.MaxX, q.MaxY, _spacing)) continue;
            if (PolysActuallyOverlap(poly, newOx, newOy, q.Poly, q.OriginX, q.OriginY))
            {
                var pen = MaxPenetrationDepth(poly, newOx, newOy, q.Poly, q.OriginX, q.OriginY);
                if (pen > _trimTolerance + _tol) return false;
            }
            else if (_spacing > _tol)
            {
                var minDist = MinDistPolys(poly, newOx, newOy, q.Poly, q.OriginX, q.OriginY);
                if (minDist < _spacing - _tol) return false;
            }
        }
        return true;
    }

    private double ContactScoreSingle(PlacedPoly piece, SheetData sheet, int selfIdx)
    {
        var poly = piece.Poly;
        var threshold = _spacing + 2 * _trimTolerance + _tol;
        var score = 0.0;
        for (int j = 0; j < sheet.Placed.Count; j++)
        {
            if (j == selfIdx) continue;
            var q = sheet.Placed[j];
            if (!BBoxOverlap(piece.MinX, piece.MinY, piece.MaxX, piece.MaxY,
                              q.MinX, q.MinY, q.MaxX, q.MaxY, threshold)) continue;
            score += EdgeContact(poly, piece.OriginX, piece.OriginY, q.Poly, q.OriginX, q.OriginY, threshold);
            score += EdgePairAlignment(poly, piece.OriginX, piece.OriginY, q.Poly, q.OriginX, q.OriginY, threshold) * EdgePairScoreWeight;
        }
        score += EdgeContact(poly, piece.OriginX, piece.OriginY, sheet.Outer, 0, 0, threshold) * 0.5;
        score += EdgePairAlignment(poly, piece.OriginX, piece.OriginY, sheet.Outer, 0, 0, threshold) * 0.5 * EdgePairScoreWeight;
        foreach (var hole in sheet.Holes)
        {
            score += EdgeContact(poly, piece.OriginX, piece.OriginY, hole, 0, 0, threshold) * 0.5;
            score += EdgePairAlignment(poly, piece.OriginX, piece.OriginY, hole, 0, 0, threshold) * 0.5 * EdgePairScoreWeight;
        }
        return score;
    }

    private double ContactScoreAtCandidate(Poly2d poly, double ox, double oy, SheetData sheet, int excludeIdx)
    {
        var threshold = _spacing + 2 * _trimTolerance + _tol;
        var score = 0.0;
        for (int j = 0; j < sheet.Placed.Count; j++)
        {
            if (j == excludeIdx) continue;
            var q = sheet.Placed[j];
            if (!BBoxOverlap(poly.MinX + ox, poly.MinY + oy,
                              poly.MaxX + ox, poly.MaxY + oy,
                              q.MinX, q.MinY, q.MaxX, q.MaxY, threshold)) continue;
            score += EdgeContact(poly, ox, oy, q.Poly, q.OriginX, q.OriginY, threshold);
            score += EdgePairAlignment(poly, ox, oy, q.Poly, q.OriginX, q.OriginY, threshold) * EdgePairScoreWeight;
        }
        score += EdgeContact(poly, ox, oy, sheet.Outer, 0, 0, threshold) * 0.5;
        score += EdgePairAlignment(poly, ox, oy, sheet.Outer, 0, 0, threshold) * 0.5 * EdgePairScoreWeight;
        foreach (var hole in sheet.Holes)
        {
            score += EdgeContact(poly, ox, oy, hole, 0, 0, threshold) * 0.5;
            score += EdgePairAlignment(poly, ox, oy, hole, 0, 0, threshold) * 0.5 * EdgePairScoreWeight;
        }
        return score;
    }

    // ─── Trim post-pass ──────────────────────────────────────────────────

    private void ApplyTrimPostPass(PackingResult result)
    {
        var n = result.PackedCurves.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            result.TrimmedCurves.Add(result.PackedCurves[i].DuplicateCurve());
            result.TrimAdjacency.Add(new List<int>());
        }
        if (_overlapPairs.Count == 0) return;

        _overlapPairs.Sort((a, b) =>
        {
            var c = a.later.CompareTo(b.later);
            if (c != 0) return c;
            return a.earlier.CompareTo(b.earlier);
        });

        var planeTol = Math.Max(_tol, 0.001);
        foreach (var (earlier, later, _) in _overlapPairs)
        {
            if (later < 0 || later >= n) continue;
            if (earlier < 0 || earlier >= n) continue;

            var laterCurve = result.TrimmedCurves[later];
            var earlierCurve = result.TrimmedCurves[earlier];
            Curve[] diff = null;
            try { diff = Curve.CreateBooleanDifference(laterCurve, earlierCurve, planeTol); }
            catch { diff = null; }
            if (diff == null || diff.Length == 0) continue;

            Curve best = null;
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

    // ─── Grout offset ────────────────────────────────────────────────────

    private void ApplyGroutOffset(PackingResult result)
    {
        var inset = _grout * 0.5;
        var planeTol = Math.Max(_tol, 0.001);
        for (int i = 0; i < result.TrimmedCurves.Count; i++)
        {
            var c = result.TrimmedCurves[i];
            if (c == null || !c.IsClosed) continue;
            if (!c.TryGetPlane(out var plane, planeTol)) continue;

            Curve[] off;
            try { off = c.Offset(plane, -inset, planeTol, CurveOffsetCornerStyle.Sharp); }
            catch { off = null; }
            if (off == null || off.Length == 0)
            {
                try { off = c.Offset(plane, inset, planeTol, CurveOffsetCornerStyle.Sharp); }
                catch { off = null; }
            }
            if (off == null || off.Length == 0) continue;

            var origArea = AreaMassProperties.Compute(c)?.Area ?? 0;
            Curve picked = null;
            double pickedArea = double.MaxValue;
            foreach (var o in off)
            {
                if (o == null || !o.IsClosed) continue;
                var a = AreaMassProperties.Compute(o)?.Area ?? 0;
                if (a > 0 && a < origArea && a < pickedArea)
                {
                    pickedArea = a;
                    picked = o;
                }
            }
            if (picked != null) result.TrimmedCurves[i] = picked;
        }
    }

    // ─── Geometry primitives ─────────────────────────────────────────────

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
            for (var i = 0; i < poly.N; i++)
                if (PointInPoly(poly.Vx[i] + ox, poly.Vy[i] + oy, hole.Vx, hole.Vy, hole.N))
                    return false;
            if (PointInPoly(poly.Cx + ox, poly.Cy + oy, hole.Vx, hole.Vy, hole.N)) return false;
            for (var j = 0; j < hole.N; j++)
                if (PointInPoly(hole.Vx[j] - ox, hole.Vy[j] - oy, poly.Vx, poly.Vy, poly.N))
                    return false;
            if (PointInPoly(hole.Cx - ox, hole.Cy - oy, poly.Vx, poly.Vy, poly.N)) return false;
            if (PolysEdgesCross(poly, ox, oy, hole, 0, 0)) return false;
            if (_spacing > _tol && MinDistPolys(poly, ox, oy, hole, 0, 0) < _spacing - _tol) return false;
        }
        return true;
    }

    private bool PolysActuallyOverlap(Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        if (PolysEdgesCross(a, aox, aoy, b, box, boy)) return true;
        if (PointInPoly(a.Cx + aox - box, a.Cy + aoy - boy, b.Vx, b.Vy, b.N)) return true;
        if (PointInPoly(b.Cx + box - aox, b.Cy + boy - aoy, a.Vx, a.Vy, a.N)) return true;
        return false;
    }

    private static double MaxPenetrationDepth(
        Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        double maxPen = 0;
        for (int i = 0; i < a.N; i++)
        {
            var px = a.Vx[i] + aox;
            var py = a.Vy[i] + aoy;
            if (!PointInPoly(px - box, py - boy, b.Vx, b.Vy, b.N)) continue;
            var min = double.MaxValue;
            for (int j = 0; j < b.N; j++)
            {
                int k = (j + 1) % b.N;
                var d = PointSegDist(px, py, b.Vx[j] + box, b.Vy[j] + boy, b.Vx[k] + box, b.Vy[k] + boy);
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
                var d = PointSegDist(px, py, a.Vx[j] + aox, a.Vy[j] + aoy, a.Vx[k] + aox, a.Vy[k] + aoy);
                if (d < min) min = d;
            }
            if (min > maxPen) maxPen = min;
        }
        return maxPen;
    }

    private static bool PointInPoly(double px, double py, double[] vx, double[] vy, int n)
    {
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if ((vy[i] > py) != (vy[j] > py) &&
                px < (vx[j] - vx[i]) * (py - vy[i]) / (vy[j] - vy[i]) + vx[i])
                inside = !inside;
        return inside;
    }

    private static bool BBoxOverlap(
        double ax0, double ay0, double ax1, double ay1,
        double bx0, double by0, double bx1, double by1, double pad)
        => ax1 + pad >= bx0 && bx1 + pad >= ax0 && ay1 + pad >= by0 && by1 + pad >= ay0;

    private bool SegmentsIntersect(
        double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy)
    {
        var d1x = bx - ax; var d1y = by - ay;
        var d2x = dx - cx; var d2y = dy - cy;
        var denom = d1x * d2y - d1y * d2x;
        if (Math.Abs(denom) < _tol * _tol) return false;
        var t = ((cx - ax) * d2y - (cy - ay) * d2x) / denom;
        var u = ((cx - ax) * d1y - (cy - ay) * d1x) / denom;
        return t > _tol && t < 1.0 - _tol && u > _tol && u < 1.0 - _tol;
    }

    private bool PolysEdgesCross(Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        for (var i = 0; i < a.N; i++)
        {
            var ax = a.Vx[i] + aox; var ay = a.Vy[i] + aoy;
            var bx_ = a.Vx[(i + 1) % a.N] + aox; var by_ = a.Vy[(i + 1) % a.N] + aoy;
            for (var j = 0; j < b.N; j++)
            {
                var cx = b.Vx[j] + box; var cy = b.Vy[j] + boy;
                var dx = b.Vx[(j + 1) % b.N] + box; var dy = b.Vy[(j + 1) % b.N] + boy;
                if (SegmentsIntersect(ax, ay, bx_, by_, cx, cy, dx, dy)) return true;
            }
        }
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
                var q0x = b.Vx[j] + box; var q0y = b.Vy[j] + boy;
                var q1x = b.Vx[(j + 1) % b.N] + box; var q1y = b.Vy[(j + 1) % b.N] + boy;
                var d = PointSegDist(px, py, q0x, q0y, q1x, q1y);
                if (d < min) min = d;
            }
        }
        return min;
    }

    private static double PointSegDist(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax; var dy = by - ay;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        var t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lenSq));
        var nx = ax + t * dx; var ny = ay + t * dy;
        return Math.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    // ─── Reporting ───────────────────────────────────────────────────────

    private string BuildReport(PackingResult result)
    {
        var bm = _boundaryMode switch
        {
            0 => "0 off",
            1 => "1 bias",
            2 => "2 strict ring/interior two-phase",
            3 => "3 uniform curve division",
            _ => $"{_boundaryMode} (unknown)"
        };
        return
            $"Trencadís pack: {result.PackedCurves.Count}/{result.PreparedCount} placed " +
            $"(invalid {result.InvalidCount}, unplaced {result.UnplacedCurves.Count}). " +
            $"Trims {result.TrimEventCount}. {result.RuntimeMilliseconds} ms.\n" +
            $"Sheets        : {_sheets.Count}\n" +
            $"Spacing       : {_spacing:F4}\n" +
            $"Tolerance     : {_tol:F4}\n" +
            $"TrimTolerance : {_trimTolerance:F4}\n" +
            $"CutBudget     : T_N={_cutBudget:F2}, T_P={_cutBudget * TpFraction:F2}, " +
            $"S_N={_cutBudget * SnFraction:F2}, S_P={_cutBudget * SpFraction:F2}\n" +
            $"Grout         : {_grout:F4}\n" +
            $"BoundaryMode  : {bm} (MinAffinity={_minBoundaryAffinity:F2})\n" +
            $"CVD seeds     : {(_useCvdSeeds ? "on" : "off")}\n" +
            $"GVF orient    : {(_useGvf ? $"on (μ={_gvfMu:F2}, iter={_gvfIterations}, grid={_gvfGridRes})" : "off")}\n" +
            $"Candidates    : {result.CandidateCount}\n" +
            $"Collision chk : {result.CollisionCheckCount}\n" +
            $"Anneal moves  : {result.AnnealingMoves}\n";
    }

    // ─── Boundary-aware rail index + matcher (modes 1 and 2) ────────────

    private void BuildBoundaryIndexes(List<PartData> parts)
    {
        var allEdgeLens = new List<double>();
        foreach (var p in parts)
        {
            if (p.Descriptor == null) continue;
            foreach (var e in p.Descriptor.Edges)
                if (e.Length > _tol * 5) allEdgeLens.Add(e.Length);
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
            var windowLen = Math.Max(_tol * 5, Math.Min(medianEdgeLen, sheetSpan / 4.0));
            var stepLen = Math.Max(windowLen * 0.5, _tol * 2);
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

    private void ComputeBoundaryAffinity(PartData part)
    {
        var desc = part.Descriptor;
        if (desc == null) return;

        var allMatches = new List<(double score, int edgeIdx, int sheetIdx, BoundaryIntervalInfo iv, double angle)>();

        foreach (var sheet in _sheets)
        {
            if (sheet.BoundaryIndex == null) continue;
            var sheetCx = (sheet.BBoxMinX + sheet.BBoxMaxX) * 0.5;
            var sheetCy = (sheet.BBoxMinY + sheet.BBoxMaxY) * 0.5;

            var options = new MatchOptions
            {
                LengthBucketSize = sheet.BoundaryLengthBucket,
                AngleBucketSizeDegrees = BoundaryAngleBucketDeg,
                CurvatureBucketSize = BoundaryCurvBucket,
                LengthRadius = BoundaryLengthRadius,
                AngleRadius = BoundaryAngleRadius,
                PreserveZone = true,
                TopK = BoundaryTopK,
                MinAffinityScore = _minBoundaryAffinity,
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
                    var angle = Math.Atan2(ivc.Y - sheetCy, ivc.X - sheetCx);
                    allMatches.Add((m.AffinityScore, e, sheet.Index, m.Interval, angle));
                }
            }
        }

        if (allMatches.Count == 0) return;

        // Angular sort + golden-ratio stride pick (V506 Half I).
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

        // Snap rotation: align matched part edge with matched interval's
        // tangent. Two snaps (forward + 180° flip).
        if (picked.iv != null && picked.edgeIdx >= 0 && picked.edgeIdx < desc.Edges.Count)
        {
            var partEdgeAngle = desc.Edges[picked.edgeIdx].AngleDegrees;
            var bndAngle = AngleDegrees2D(picked.iv.AverageTangent);
            var snap1 = NormalizeDeg(bndAngle - partEdgeAngle);
            var snap2 = NormalizeDeg(snap1 + 180);
            AddSnapRotation(part, snap1);
            AddSnapRotation(part, snap2);
        }
    }

    private static EdgeDescriptor IntervalToEdgeDescriptor(BoundaryIntervalInfo iv)
        => new EdgeDescriptor(
            length: iv.ApproxLength,
            angleDegrees: AngleDegrees2D(iv.AverageTangent),
            curvatureScore: iv.CurvatureScore,
            straightnessScore: iv.StraightnessScore,
            zoneId: 0);

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

    // ─── Mode 3: uniform curve division ─────────────────────────────────

    private void DoCurveDivisionPlacement(
        List<PartData> parts, PackingResult result, HashSet<int> placedSourceIndices)
    {
        if (_sheets.Count == 0) return;
        var primarySheet = _sheets[0];
        if (primarySheet.OuterWorkCurve == null) return;

        var boundaryCurves = new List<(Curve curve, double length)>();
        var outerLen = primarySheet.OuterWorkCurve.GetLength();
        if (outerLen <= _tol) return;
        boundaryCurves.Add((primarySheet.OuterWorkCurve, outerLen));
        foreach (var hw in primarySheet.HoleWorkCurves)
        {
            var hLen = hw.GetLength();
            if (hLen > _tol) boundaryCurves.Add((hw, hLen));
        }

        var totalLen = boundaryCurves.Sum(c => c.length);
        if (totalLen <= _tol) return;

        var prefixLens = new double[boundaryCurves.Count];
        var acc = 0.0;
        for (int i = 0; i < boundaryCurves.Count; i++)
        {
            prefixLens[i] = acc;
            acc += boundaryCurves[i].length;
        }

        var N = parts.Count;
        if (N == 0) return;

        for (int i = 0; i < N; i++)
        {
            var part = parts[i];
            if (part.Descriptor == null) continue;
            var longestEdgeIdx = FindLongestEdgeIdx(part.Descriptor);
            if (longestEdgeIdx < 0) continue;

            var targetTotal = (i + 0.5) * totalLen / N;
            Curve targetCurve = null;
            double targetWithin = 0;
            for (int c = 0; c < boundaryCurves.Count; c++)
            {
                var endLen = prefixLens[c] + boundaryCurves[c].length;
                if (targetTotal <= endLen + _tol)
                {
                    targetCurve = boundaryCurves[c].curve;
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

            var inwardX = -targetTangent.Y;
            var inwardY = targetTangent.X;

            var partEdgeAngle = part.Descriptor.Edges[longestEdgeIdx].AngleDegrees;
            var bndAngle = AngleDegrees2D(targetTangent);
            var snapDeg = NormalizeDeg(bndAngle - partEdgeAngle);

            var re = RotatePoly(part.ExactAtOrigin, snapDeg, longestEdgeIdx);
            var nudge = Math.Max(_spacing, _tol * 5);
            var ox = targetPoint.X - re.MatchedMidX + inwardX * nudge;
            var oy = targetPoint.Y - re.MatchedMidY + inwardY * nudge;

            if (TryPlaceMode3(part, snapDeg, ox, oy, primarySheet, re, result))
            {
                placedSourceIndices.Add(part.SourceIndex);
                continue;
            }

            var snapDeg2 = NormalizeDeg(snapDeg + 180);
            var re2 = RotatePoly(part.ExactAtOrigin, snapDeg2, longestEdgeIdx);
            var ox2 = targetPoint.X - re2.MatchedMidX + inwardX * nudge;
            var oy2 = targetPoint.Y - re2.MatchedMidY + inwardY * nudge;

            if (TryPlaceMode3(part, snapDeg2, ox2, oy2, primarySheet, re2, result))
                placedSourceIndices.Add(part.SourceIndex);
        }
    }

    private bool TryPlaceMode3(
        PartData part, double snapDeg, double ox, double oy,
        SheetData sheet, RotEntry re, PackingResult result)
    {
        var w = re.Poly.MaxX - re.Poly.MinX;
        var h = re.Poly.MaxY - re.Poly.MinY;

        if (ox < sheet.BBoxMinX - _tol || oy < sheet.BBoxMinY - _tol ||
            ox + w > sheet.BBoxMaxX + _tol || oy + h > sheet.BBoxMaxY + _tol)
            return false;
        if (!ContainedInSheet(re.Poly, ox, oy, sheet)) return false;

        // Mode 3 also respects trim-aware overlap, with cut budgets.
        var tNCap = _cutBudget * part.Area;
        var sNCap = _cutBudget * SnFraction * part.Area;
        var overlaps = new List<(int idx, double area)>();
        var cumulativeForNew = 0.0;
        foreach (var p in sheet.Placed)
        {
            if (!BBoxOverlap(re.Poly.MinX + ox, re.Poly.MinY + oy,
                              re.Poly.MaxX + ox, re.Poly.MaxY + oy,
                              p.MinX, p.MinY, p.MaxX, p.MaxY, _spacing)) continue;
            if (PolysActuallyOverlap(re.Poly, ox, oy, p.Poly, p.OriginX, p.OriginY))
            {
                var pen = MaxPenetrationDepth(re.Poly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                if (pen > _trimTolerance + _tol) return false;
                var area = ApproxOverlapArea(re.Poly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                if (area > sNCap) return false;
                if (area > _cutBudget * SpFraction * p.Area) return false;
                if (p.CumulativeTrimmedArea + area > _cutBudget * TpFraction * p.Area) return false;
                cumulativeForNew += area;
                overlaps.Add((p.ListIdx, area));
            }
            else if (_spacing > _tol)
            {
                var minDist = MinDistPolys(re.Poly, ox, oy, p.Poly, p.OriginX, p.OriginY);
                if (minDist < _spacing - _tol) return false;
            }
        }
        if (cumulativeForNew > tNCap) return false;

        var placed = new PlacedPoly(re.Poly, ox, oy, re.RotMinX, re.RotMinY);
        CommitPlacement(part, placed, snapDeg, sheet.Index, overlaps, result);
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

    // ─── Inner types ─────────────────────────────────────────────────────

    internal sealed class Poly2d
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
                var j = (i + 1) % N;
                var cross = vx[i] * vy[j] - vx[j] * vy[i];
                cx += (vx[i] + vx[j]) * cross;
                cy += (vy[i] + vy[j]) * cross;
                area += cross;
            }
            MinX = mnX; MinY = mnY; MaxX = mxX; MaxY = mxY;
            area *= 0.5; Area = Math.Abs(area);
            if (Area > 1e-20) { cx /= 6 * area; cy /= 6 * area; }
            else { cx = (mnX + mxX) * 0.5; cy = (mnY + mxY) * 0.5; }
            Cx = cx; Cy = cy;
        }

        public static Poly2d Build(double[] vx, double[] vy)
            => vx.Length < 3 ? null : new Poly2d(vx, vy);
    }

    private sealed class RotEntry
    {
        public readonly Poly2d Poly;
        public readonly double RotMinX, RotMinY;
        // Mode 3: matched-edge midpoint in normalised rotation frame.
        // (NaN, NaN) when no matched edge.
        public readonly double MatchedMidX, MatchedMidY;
        public RotEntry(Poly2d poly, double rx, double ry,
            double mmx = double.NaN, double mmy = double.NaN)
        {
            Poly = poly; RotMinX = rx; RotMinY = ry;
            MatchedMidX = mmx; MatchedMidY = mmy;
        }
    }

    private sealed class SheetData
    {
        public int Index;
        public Plane Plane;
        public Poly2d Outer;
        public List<Poly2d> Holes = new();
        public Transform WorkToSheet;
        public Curve OuterRhinoCurve;
        public List<Curve> HoleRhinoCurves = new();
        public double BBoxMinX, BBoxMinY, BBoxMaxX, BBoxMaxY;
        public List<PlacedPoly> Placed = new();
        public List<(double x, double y)> CvdSeeds;
        public Gvf2d GvfField;
        // Boundary-aware mode (modes 1/2/3): WorkXY-frame curves used by
        // the rail-index builder + Mode 3's curve-division placement.
        public Curve OuterWorkCurve;
        public List<Curve> HoleWorkCurves = new();
        public BoundaryRailIndex<BoundaryIntervalInfo> BoundaryIndex;
        public double BoundaryLengthBucket;
        // F-2D-002.F3 occupancy pre-filter — uniform grid over sheet bbox.
        public int[,] OccupancyCounts;
        public double OccupancyCellW, OccupancyCellH;
    }

    private sealed class PartData
    {
        public int SourceIndex;
        public Curve SourceCurve;
        public Poly2d ExactAtOrigin;
        public Transform PlaneToWork;
        public Transform NormalizeTx;
        public double Width, Height, Area;
        // Boundary-aware mode: descriptor + matched-anchor state.
        public FragmentDescriptor Descriptor;
        public double MaxEdgeAffinity;
        public int BestMatchedEdgeIndex = -1;
        public int BestMatchedSheetIndex = -1;
        public BoundaryIntervalInfo BestMatchedInterval;
        public List<double> SnapRotationsDeg = new();
    }

    private sealed class PlacedPoly
    {
        public readonly Poly2d Poly;
        // F-2D-002.F5 annealing: origin and bbox are mutable so the post-pass
        // settle step can translate a placement without rebuilding it.
        public double OriginX, OriginY;
        public double MinX, MinY, MaxX, MaxY;
        public readonly double RotMinX, RotMinY;
        public readonly double Area;
        public int ListIdx;
        public double CumulativeTrimmedArea;

        public PlacedPoly(Poly2d poly, double ox, double oy, double rotMinX = 0, double rotMinY = 0)
        {
            Poly = poly;
            OriginX = ox; OriginY = oy;
            MinX = poly.MinX + ox; MinY = poly.MinY + oy;
            MaxX = poly.MaxX + ox; MaxY = poly.MaxY + oy;
            RotMinX = rotMinX; RotMinY = rotMinY;
            Area = poly.Area;
            CumulativeTrimmedArea = 0;
        }

        public void Translate(double dx, double dy)
        {
            OriginX += dx; OriginY += dy;
            MinX += dx; MinY += dy; MaxX += dx; MaxY += dy;
        }
    }
}
