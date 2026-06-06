using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Version 2 freeform-curve packing solver.
/// Any closed planar curve is accepted as a sheet or part — freeform, arc, spline, etc.
/// Inputs are projected to WorldXY and discretised to Poly2d once.
/// All hot-path logic (containment, collision, candidate generation) works on plain
/// double[] arrays with no Rhino Curve operations, making it safe for Parallel.For.
/// </summary>
public sealed class IrregularSheetFillV2
{
    private const int ExactMaxVerts  = 128;
    private const int MaxValidPerRot = 8;
    private const int MaxNfpVerts    = 16;

    private readonly double _spacing;
    private readonly double _tol;
    private readonly List<double> _rotDeg;
    private readonly PackingSortMode _sortMode;
    private readonly PackingCornerMode _cornerMode;
    private readonly int _seed;
    private readonly int _maxCandidates;
    private readonly List<SheetData> _sheets;

    public IrregularSheetFillV2(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        PackingCornerMode cornerMode,
        int seed,
        int maxCandidates)
    {
        _spacing = Math.Max(0.1, spacing);  // minimum 0.1 — prevents floating-point overlap at zero spacing
        _tol = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        _sortMode = sortMode;
        _cornerMode = cornerMode;
        _seed = seed;
        _maxCandidates = Math.Max(30, maxCandidates <= 0 ? 300 : maxCandidates);

        var rotList = rotationsDeg?.Where(RhinoMath.IsValidDouble).Distinct().ToList() ?? new List<double>();
        if (rotList.Count == 0) rotList.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        _rotDeg = rotList;

        var outlines = sheetOutlines.ToList();
        _sheets = new List<SheetData>(outlines.Count);
        for (var i = 0; i < outlines.Count; i++)
        {
            var holes = i < sheetHoles.Count ? sheetHoles[i] : (IReadOnlyList<Curve>)Array.Empty<Curve>();
            var sd = PrepareSheet(outlines[i], holes, i);
            if (sd != null) _sheets.Add(sd);
        }
    }

    // ─── Public entry point ──────────────────────────────────────────────────

    public PackingResult Pack(IEnumerable<Curve>? inputCurves, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
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

        // Convert all input curves to PartData once — all Rhino work happens here.
        var parts = new List<PartData>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            var pd = PreparePart(input[i], i);
            if (pd != null) parts.Add(pd);
        }
        result.PreparedCount = parts.Count;
        result.InvalidCount  = result.InputCount - result.PreparedCount;

        // Build rotation cache: all (partSourceIndex × angleKey) upfront, pure math.
        var rotCache = BuildRotationCache(parts);

        var ordered = SortParts(parts);

        var placedBySheet = new Dictionary<int, List<PlacedPoly>>();
        var gridBySheet   = new Dictionary<int, Grid2d>();
        foreach (var s in _sheets)
        {
            placedBySheet[s.Index] = new List<PlacedPoly>();
            var span = Math.Max(s.BBoxMaxX - s.BBoxMinX, s.BBoxMaxY - s.BBoxMinY);
            gridBySheet[s.Index]   = new Grid2d(Math.Max(span / 20.0, _tol * 10));
        }

        foreach (var part in ordered)
        {
            ct.ThrowIfCancellationRequested();

            PlacedPoly? best = null;
            Transform   bestTx    = Transform.Identity;
            int         bestSheet = -1;

            foreach (var sheet in _sheets)
            {
                var (placed, tx) = FindBestPlacement(
                    part, sheet, placedBySheet[sheet.Index], gridBySheet[sheet.Index], rotCache, result);
                if (placed != null) { best = placed; bestTx = tx; bestSheet = sheet.Index; break; }
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

            var outCurve = part.SourceCurve.DuplicateCurve();
            outCurve.Transform(bestTx);
            result.PackedCurves.Add(outCurve);
            result.Transforms.Add(bestTx);
            result.SourceIndices.Add(part.SourceIndex);
            result.SheetIndices.Add(bestSheet);
        }

        result.RuntimeMilliseconds = sw.ElapsedMilliseconds;
        result.Report = $"Freeform Sheet Pack V2 — Placed: {result.PackedCurves.Count}, " +
                        $"Unplaced: {result.UnplacedCurves.Count}, Invalid: {result.InvalidCount}, " +
                        $"Sheets: {_sheets.Count}, Candidates: {result.CandidateCount}, " +
                        $"Collisions: {result.CollisionCheckCount}, Runtime: {result.RuntimeMilliseconds} ms";
        return result;
    }

    // ─── Sheet / part preparation (Rhino calls — runs once at setup) ─────────

    private SheetData? PrepareSheet(Curve outer, IReadOnlyList<Curve> holes, int index)
    {
        if (outer == null || !outer.IsClosed) return null;

        // Project to WorldXY.
        if (!outer.IsPlanar(_tol) || !outer.TryGetPlane(out var plane, _tol)) return null;
        var toWork  = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var toSheet = Transform.PlaneToPlane(Plane.WorldXY, plane);

        var outerWork = outer.DuplicateCurve();
        outerWork.Transform(toWork);

        var outerEff = _spacing > _tol ? OffsetCurve(outerWork, inward: true) ?? outerWork : outerWork.DuplicateCurve();
        var outerPoly = CurveToPoly2d(outerEff, ExactMaxVerts);
        if (outerPoly == null || outerPoly.N < 3) return null;

        var holePoly       = new List<Poly2d>();
        var holeRhinoCurves = new List<Curve>();
        foreach (var hole in holes)
        {
            if (hole == null || !hole.IsClosed) continue;
            var hw = hole.DuplicateCurve();
            hw.Transform(toWork);
            var holeEff = _spacing > _tol ? OffsetCurve(hw, inward: false) ?? hw : hw.DuplicateCurve();
            var hp = CurveToPoly2d(holeEff, ExactMaxVerts);
            if (hp != null && hp.N >= 3) { holePoly.Add(hp); holeRhinoCurves.Add(hole.DuplicateCurve()); }
        }

        var bb = outerWork.GetBoundingBox(true);
        return new SheetData
        {
            Index            = index,
            Outer            = outerPoly,
            Holes            = holePoly,
            WorkToSheet      = toSheet,
            OuterRhinoCurve  = outer.DuplicateCurve(),
            HoleRhinoCurves  = holeRhinoCurves,
            BBoxMinX         = bb.Min.X,
            BBoxMinY         = bb.Min.Y,
            BBoxMaxX         = bb.Max.X,
            BBoxMaxY         = bb.Max.Y,
        };
    }

    private PartData? PreparePart(Curve? curve, int sourceIndex)
    {
        if (curve == null || !curve.IsClosed) return null;
        if (!curve.IsPlanar(_tol) || !curve.TryGetPlane(out var plane, _tol)) return null;

        var toWork = Transform.PlaneToPlane(plane, Plane.WorldXY);
        var wc = curve.DuplicateCurve();
        wc.Transform(toWork);

        // Normalise: translate so bounding-box min is at origin.
        var bb = wc.GetBoundingBox(true);
        var normalize = Transform.Translation(-bb.Min.X, -bb.Min.Y, 0);
        wc.Transform(normalize);

        var poly = CurveToPoly2d(wc, ExactMaxVerts);
        if (poly == null || poly.N < 3 || poly.Area <= _tol * _tol) return null;

        return new PartData
        {
            SourceIndex   = sourceIndex,
            SourceCurve   = curve.DuplicateCurve(),
            ExactAtOrigin = poly,
            PartToWork    = toWork * normalize,
            Width         = poly.MaxX - poly.MinX,
            Height        = poly.MaxY - poly.MinY,
            Area          = poly.Area,
        };
    }

    // ─── Rotation cache (pure math, thread-safe) ─────────────────────────────

    private Dictionary<(int, int), RotEntry> BuildRotationCache(List<PartData> parts)
    {
        var cache = new Dictionary<(int, int), RotEntry>(parts.Count * _rotDeg.Count);
        foreach (var part in parts)
            foreach (var deg in _rotDeg)
                cache[(part.SourceIndex, AngleKey(deg))] = RotatePoly(part.ExactAtOrigin, deg);
        return cache;
    }

    private static int AngleKey(double deg) => (int)Math.Round(deg * 100) % 36000;

    private static RotEntry RotatePoly(Poly2d src, double angleDeg)
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
        // Normalise rotated polygon to origin (so placement offset = the candidate point).
        for (var i = 0; i < n; i++) { rx[i] -= minX; ry[i] -= minY; }
        return new RotEntry(Poly2d.Build(rx, ry)!, minX, minY);
    }

    // ─── Placement search ────────────────────────────────────────────────────

    private (PlacedPoly? placed, Transform tx) FindBestPlacement(
        PartData part, SheetData sheet,
        List<PlacedPoly> placedOnSheet, Grid2d grid,
        Dictionary<(int, int), RotEntry> rotCache,
        PackingResult result)
    {
        var rotCount = _rotDeg.Count;
        var rotBests = new (PlacedPoly? p, Transform tx, double rotMinX, double rotMinY)[rotCount];
        var cands    = 0;
        var colls    = 0;

        Parallel.For(0, rotCount, i =>
        {
            var deg = _rotDeg[i];
            if (!rotCache.TryGetValue((part.SourceIndex, AngleKey(deg)), out var re)) return;
            var (best, c, cc) = EvaluateRotation(re.Poly, sheet, placedOnSheet, grid);
            rotBests[i] = (best, Transform.Identity, re.RotMinX, re.RotMinY);
            // Store transform lazily — only compute if best != null.
            if (best != null)
                rotBests[i].tx = BuildTransform(part, deg, re.RotMinX, re.RotMinY, best, sheet);
            Interlocked.Add(ref cands, c);
            Interlocked.Add(ref colls, cc);
        });

        result.CandidateCount      += cands;
        result.CollisionCheckCount += colls;

        (PlacedPoly? p, Transform tx, double, double) winner = (null, Transform.Identity, 0, 0);
        foreach (var r in rotBests)
        {
            if (r.p == null) continue;
            if (winner.p == null || IsBetter(r.p, winner.p)) winner = r;
        }
        return (winner.p, winner.tx);
    }

    private (PlacedPoly? best, int cands, int colls) EvaluateRotation(
        Poly2d rotPoly, SheetData sheet, List<PlacedPoly> placedOnSheet, Grid2d grid)
    {
        PlacedPoly? best  = null;
        var cands  = 0;
        var colls  = 0;
        var valid  = 0;
        var w = rotPoly.MaxX - rotPoly.MinX;
        var h = rotPoly.MaxY - rotPoly.MinY;

        foreach (var (ox, oy) in GenerateCandidates(sheet, rotPoly, placedOnSheet))
        {
            cands++;

            // Phase 1: bounding-box containment (O(1)).
            if (ox < sheet.BBoxMinX - _tol || oy < sheet.BBoxMinY - _tol ||
                ox + w > sheet.BBoxMaxX + _tol || oy + h > sheet.BBoxMaxY + _tol)
                continue;

            // Phase 2: exact polygon containment + hole avoidance (pure math).
            if (!ContainedInSheet(rotPoly, ox, oy, sheet)) continue;

            // Phase 3: collision via spatial grid (O(k), k = nearby placed parts).
            colls++;
            if (CollidesWithGrid(rotPoly, ox, oy, placedOnSheet, grid)) continue;

            var placed = new PlacedPoly(rotPoly, ox, oy);
            if (best == null || IsBetter(placed, best)) best = placed;
            if (++valid >= MaxValidPerRot) break;
        }
        return (best, cands, colls);
    }

    private Transform BuildTransform(
        PartData part, double angleDeg, double rotMinX, double rotMinY,
        PlacedPoly placed, SheetData sheet)
    {
        // PartToWork    : source → WorldXY, bbox-min at origin
        // rotation      : rotate by angleDeg
        // postRotNorm   : translate by (-rotMinX, -rotMinY) — bakes the normalisation done in RotatePoly
        // translation   : move to placement position
        // WorkToSheet   : WorldXY → sheet's 3-D plane
        var rotation    = Transform.Rotation(angleDeg * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
        var postRotNorm = Transform.Translation(-rotMinX, -rotMinY, 0);
        var translation = Transform.Translation(placed.OriginX, placed.OriginY, 0);
        // FIX 2026-06-05: the compound was REVERSED. outCurve.Transform(compound)
        // applies right-to-left (column-vector convention), so the intended order
        // PartToWork -> rotation -> postRotNorm -> translation -> WorkToSheet must
        // be written right-to-left as below. The old order
        // (PartToWork * ... * WorkToSheet) applied WorkToSheet FIRST and scattered
        // the output curves outside the sheet ("curves off in the distance"); the
        // packing math was correct but the Rhino output was misplaced. This is the
        // same bug V506's own path documents and fixes; V2 had it too (and V506
        // inherited it when its plain mode delegates to V2).
        return sheet.WorkToSheet * translation * postRotNorm * rotation * part.PartToWork;
    }

    // ─── Candidate generation ────────────────────────────────────────────────

    private IEnumerable<(double ox, double oy)> GenerateCandidates(
        SheetData sheet, Poly2d rotPoly, List<PlacedPoly> placed)
    {
        var w = rotPoly.MaxX - rotPoly.MinX;
        var h = rotPoly.MaxY - rotPoly.MinY;
        var raw = new List<(double, double)>(128);

        // Sheet bbox corners.
        raw.Add((sheet.BBoxMinX, sheet.BBoxMinY));
        raw.Add((sheet.BBoxMaxX - w, sheet.BBoxMinY));
        raw.Add((sheet.BBoxMinX, sheet.BBoxMaxY - h));
        raw.Add((sheet.BBoxMaxX - w, sheet.BBoxMaxY - h));

        // IFP: positions where the part is tangent to two sheet edges simultaneously.
        ComputeIfpCandidates(sheet.Outer, rotPoly, raw);

        // Sheet outer boundary vertices.
        var o = sheet.Outer;
        for (var i = 0; i < o.N; i++) AddCornerOffsets(raw, o.Vx[i], o.Vy[i], w, h);

        // Hole boundary vertices.
        foreach (var hole in sheet.Holes)
            for (var i = 0; i < hole.N; i++) AddCornerOffsets(raw, hole.Vx[i], hole.Vy[i], w, h);

        // Per-placed-part candidates.
        foreach (var p in placed)
        {
            // NFP contact candidates: Minkowski difference.
            ComputeNfpCandidates(p, rotPoly, raw);
            // BBox contact.
            raw.Add((p.MaxX + _spacing, p.MinY));
            raw.Add((p.MinX, p.MaxY + _spacing));
            raw.Add((p.MaxX + _spacing, p.MaxY + _spacing));
            // Edge-sweep: slide along placed part vertices.
            var pv = p.Poly;
            var edgeStep = Math.Max(Math.Min(w, h) * 0.5, Math.Max(_spacing, _tol) * 4);
            if (IsFinite(edgeStep) && edgeStep > _tol)
            {
                for (var vi = 0; vi < pv.N; vi++)
                {
                    var vj   = (vi + 1) % pv.N;
                    var dx   = pv.Vx[vj] - pv.Vx[vi]; var dy = pv.Vy[vj] - pv.Vy[vi];
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

        // Sparse fallback grid.
        var gridStep = Math.Max(Math.Max(w, h) * 0.5, _tol * 8);
        if (IsFinite(gridStep) && gridStep > _tol)
        {
            var maxGrid = Math.Min(80, Math.Max(10, _maxCandidates / 6));
            var added   = 0;
            for (var y = sheet.BBoxMinY; y <= sheet.BBoxMaxY - h + _tol && added < maxGrid; y += gridStep)
                for (var x = sheet.BBoxMinX; x <= sheet.BBoxMaxX - w + _tol && added < maxGrid; x += gridStep)
                    { raw.Add((x, y)); added++; }
        }

        // Dedup + filter.
        var tol2 = Math.Max(_tol, 1e-6);
        var seen = new HashSet<long>();
        var filtered = new List<(double, double)>(raw.Count);
        foreach (var (ox, oy) in raw)
        {
            if (ox + w < sheet.BBoxMinX - _tol || oy + h < sheet.BBoxMinY - _tol ||
                ox > sheet.BBoxMaxX + _tol    || oy > sheet.BBoxMaxY + _tol)
                continue;
            var kx = (long)Math.Round(ox / tol2);
            var ky = (long)Math.Round(oy / tol2);
            if (!seen.Add(kx * 2000003L + ky)) continue;
            filtered.Add((ox, oy));
        }

        return OrderCandidates(filtered, w, h).Take(_maxCandidates);
    }

    private void AddCornerOffsets(List<(double, double)> raw, double vx, double vy, double w, double h)
    {
        raw.Add((vx,         vy));
        raw.Add((vx - w,     vy));
        raw.Add((vx,         vy - h));
        raw.Add((vx - w,     vy - h));
        if (_spacing > _tol)
        {
            raw.Add((vx + _spacing,         vy));
            raw.Add((vx,                     vy + _spacing));
            raw.Add((vx - w - _spacing,      vy));
            raw.Add((vx,                     vy - h - _spacing));
        }
    }

    // ─── IFP — Minkowski erosion ──────────────────────────────────────────────

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
            var j  = (i + 1) % n;
            var dx = sheetOuter.Vx[j] - sheetOuter.Vx[i];
            var dy = sheetOuter.Vy[j] - sheetOuter.Vy[i];
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < _tol) continue;
            var nx = sign * dy / len;
            var ny = sign * (-dx) / len;
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

        var m = constraints.Count;
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

        // Sample IFP edges.
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

    // ─── Containment + collision (pure math, no Rhino) ───────────────────────

    private bool ContainedInSheet(Poly2d poly, double ox, double oy, SheetData sheet)
    {
        var outer = sheet.Outer;
        // All vertices of the part must be inside the sheet outer boundary.
        for (var i = 0; i < poly.N; i++)
            if (!PointInPoly(poly.Vx[i] + ox, poly.Vy[i] + oy, outer.Vx, outer.Vy, outer.N))
                return false;
        // No edge crossings with the outer boundary.
        if (PolysEdgesCross(poly, ox, oy, outer, 0, 0)) return false;

        // Not intersecting any hole.
        foreach (var hole in sheet.Holes)
        {
            if (!BBoxOverlap(poly.MinX + ox, poly.MinY + oy, poly.MaxX + ox, poly.MaxY + oy,
                             hole.MinX, hole.MinY, hole.MaxX, hole.MaxY, 0)) continue;
            for (var i = 0; i < poly.N; i++)
                if (PointInPoly(poly.Vx[i] + ox, poly.Vy[i] + oy, hole.Vx, hole.Vy, hole.N))
                    return false;
            if (PointInPoly(poly.Cx + ox, poly.Cy + oy, hole.Vx, hole.Vy, hole.N)) return false;
            if (PolysEdgesCross(poly, ox, oy, hole, 0, 0)) return false;
        }
        return true;
    }

    private bool CollidesWithGrid(Poly2d poly, double ox, double oy, List<PlacedPoly> placed, Grid2d grid)
    {
        var pad  = _spacing + _tol;
        foreach (var idx in grid.Query(poly.MinX + ox - pad, poly.MinY + oy - pad,
                                        poly.MaxX + ox + pad, poly.MaxY + oy + pad))
        {
            if (idx < 0 || idx >= placed.Count) continue;
            var p = placed[idx];
            if (!BBoxOverlap(poly.MinX + ox, poly.MinY + oy, poly.MaxX + ox, poly.MaxY + oy,
                              p.MinX, p.MinY, p.MaxX, p.MaxY, _spacing)) continue;
            if (PolysOverlap(poly, ox, oy, p.Poly, p.OriginX, p.OriginY)) return true;
        }
        return false;
    }

    // ─── Pure-math polygon operations (static, no allocations on hot path) ───

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
        var d1x = bx - ax; var d1y = by - ay;
        var d2x = dx - cx; var d2y = dy - cy;
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
            var ax = a.Vx[i] + aox; var ay = a.Vy[i] + aoy;
            var bx = a.Vx[(i + 1) % a.N] + aox; var by_ = a.Vy[(i + 1) % a.N] + aoy;
            for (var j = 0; j < b.N; j++)
            {
                var cx = b.Vx[j] + box; var cy = b.Vy[j] + boy;
                var dx = b.Vx[(j + 1) % b.N] + box; var dy = b.Vy[(j + 1) % b.N] + boy;
                if (SegmentsIntersect(ax, ay, bx, by_, cx, cy, dx, dy, _tol)) return true;
            }
        }
        return false;
    }

    private bool PolysOverlap(Poly2d a, double aox, double aoy, Poly2d b, double box, double boy)
    {
        if (PolysEdgesCross(a, aox, aoy, b, box, boy)) return true;
        // Translate each centroid into the other polygon's local coordinate system before testing.
        // (Poly2d vertices are normalised to origin; adding the origin offset gives world position.)
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

    // ─── Ordering / sorting ───────────────────────────────────────────────────

    private IEnumerable<(double, double)> OrderCandidates(
        List<(double ox, double oy)> candidates, double w, double h)
        => _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidates.OrderBy(p => p.oy).ThenByDescending(p => p.ox + w),
            PackingCornerMode.TopLeft     => candidates.OrderByDescending(p => p.oy + h).ThenBy(p => p.ox),
            PackingCornerMode.TopRight    => candidates.OrderByDescending(p => p.oy + h).ThenByDescending(p => p.ox + w),
            _                             => candidates.OrderBy(p => p.oy).ThenBy(p => p.ox)
        };

    private bool IsBetter(PlacedPoly candidate, PlacedPoly current)
        => _cornerMode switch
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

    private List<PartData> SortParts(List<PartData> parts)
    {
        var rnd = _seed == 0 ? null : new Random(_seed);
        return _sortMode switch
        {
            PackingSortMode.UserOrder              => parts.OrderBy(p => p.SourceIndex).ToList(),
            PackingSortMode.WidthDescending        => parts.OrderByDescending(p => p.Width).ThenBy(_ => rnd?.Next() ?? 0).ToList(),
            PackingSortMode.HeightDescending       => parts.OrderByDescending(p => p.Height).ThenBy(_ => rnd?.Next() ?? 0).ToList(),
            PackingSortMode.MaxDimensionDescending => parts.OrderByDescending(p => Math.Max(p.Width, p.Height)).ThenBy(_ => rnd?.Next() ?? 0).ToList(),
            _                                      => parts.OrderByDescending(p => p.Area).ThenBy(_ => rnd?.Next() ?? 0).ToList()
        };
    }

    // ─── Curve → Poly2d (Rhino calls — boundary only) ───────────────────────

    private Poly2d? CurveToPoly2d(Curve curve, int maxVerts)
    {
        double[] vx, vy;
        if (curve.TryGetPolyline(out var pl))
        {
            // Polyline: take vertices directly.
            var cnt = pl.Count;
            if (cnt > 1 && pl[0].DistanceTo(pl[cnt - 1]) < _tol) cnt--;
            if (cnt > maxVerts) { var step = (double)cnt / maxVerts; cnt = maxVerts; }
            vx = new double[cnt]; vy = new double[cnt];
            // Uniform sample if oversized.
            if (pl.Count <= maxVerts + 1)
                for (var i = 0; i < cnt; i++) { vx[i] = pl[i].X; vy[i] = pl[i].Y; }
            else
            {
                var step = (double)pl.Count / maxVerts;
                for (var i = 0; i < maxVerts; i++)
                {
                    var idx = Math.Min(pl.Count - 1, (int)(i * step));
                    vx[i] = pl[idx].X; vy[i] = pl[idx].Y;
                }
                cnt = maxVerts;
            }
            if (cnt < 3) return null;
        }
        else
        {
            // Freeform curve: project to CPlane, then densely discretise.
            var pts = curve.ToPolyline(_tol, Math.PI / 180.0, 0, 0);
            IList<Point3d> ptList;
            if (pts != null && pts.TryGetPolyline(out pl) && pl.Count >= 3)
            {
                ptList = pl;
            }
            else
            {
                var divParams = curve.DivideByCount(Math.Min(maxVerts, 64), false);
                if (divParams == null || divParams.Length < 3) return null;
                ptList = divParams.Select(t => curve.PointAt(t)).ToList();
            }

            var cnt = ptList.Count;
            if (cnt > 1)
            {
                var last = cnt - 1;
                if (ptList[0].DistanceTo(ptList[last]) < _tol) cnt--;
            }
            if (cnt < 3) return null;

            if (cnt > maxVerts)
            {
                var step = (double)cnt / maxVerts;
                vx = new double[maxVerts]; vy = new double[maxVerts];
                for (var i = 0; i < maxVerts; i++)
                {
                    var idx = Math.Min(cnt - 1, (int)(i * step));
                    vx[i] = ptList[idx].X; vy[i] = ptList[idx].Y;
                }
            }
            else
            {
                vx = new double[cnt]; vy = new double[cnt];
                for (var i = 0; i < cnt; i++) { vx[i] = ptList[i].X; vy[i] = ptList[i].Y; }
            }
        }

        return Poly2d.Build(vx, vy);
    }

    // ─── Rhino offset helpers (called only during sheet setup) ───────────────

    private Curve? OffsetCurve(Curve curve, bool inward)
    {
        if (_spacing <= _tol) return curve.DuplicateCurve();
        var area = RhinoArea(curve);
        foreach (var d in new[] { _spacing, -_spacing })
        {
            try
            {
                var offsets = curve.Offset(Plane.WorldXY, d, _tol, CurveOffsetCornerStyle.Sharp);
                if (offsets == null) continue;
                foreach (var off in offsets)
                {
                    if (off == null || !off.IsClosed) continue;
                    var offArea = RhinoArea(off);
                    if (inward && offArea < area + _tol) return off;
                    if (!inward && offArea > area - _tol) return off;
                }
            }
            catch { }
        }
        return null;
    }

    private static double RhinoArea(Curve c)
    {
        var p = AreaMassProperties.Compute(c);
        return p == null ? 0 : Math.Abs(p.Area);
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

        public static Poly2d? Build(double[] vx, double[] vy)
            => vx.Length < 3 ? null : new Poly2d(vx, vy);
    }

    private sealed class RotEntry
    {
        public readonly Poly2d Poly;
        public readonly double RotMinX, RotMinY;
        public RotEntry(Poly2d poly, double rotMinX, double rotMinY)
            { Poly = poly; RotMinX = rotMinX; RotMinY = rotMinY; }
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
    }

    private sealed class PartData
    {
        public int SourceIndex;
        public Curve SourceCurve = null!;
        public Poly2d ExactAtOrigin = null!;
        public Transform PartToWork;
        public double Width, Height, Area;
    }

    private sealed class PlacedPoly
    {
        public readonly Poly2d Poly;
        public readonly double OriginX, OriginY;
        public readonly double MinX, MinY, MaxX, MaxY;
        public int ListIdx;

        public PlacedPoly(Poly2d poly, double ox, double oy)
        {
            Poly    = poly;
            OriginX = ox; OriginY = oy;
            MinX = poly.MinX + ox; MinY = poly.MinY + oy;
            MaxX = poly.MaxX + ox; MaxY = poly.MaxY + oy;
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
