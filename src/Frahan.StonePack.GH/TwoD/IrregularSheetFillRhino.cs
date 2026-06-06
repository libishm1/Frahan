using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Frahan.GH.TwoD;

public sealed class IrregularSheetFillRhino
{
    private const int MaxPreparedPolylinePoints = 128;
    private const int HardMaxPolylinePoints = 400;
    private const int MaxValidCandidatesPerRotation = 8;
    private readonly List<Curve> _sheetOutlines;
    private readonly IReadOnlyList<IReadOnlyList<Curve>> _sheetHoles;
    private readonly double _spacing;
    private readonly double _tolerance;
    private readonly List<double> _rotationsDeg;
    private readonly PackingSortMode _sortMode;
    private readonly PackingCornerMode _cornerMode;
    private readonly bool _simplifyCurves;
    private readonly double _simplifyTolerance;
    private readonly int _seed;
    private readonly int _maxCandidates;

    public IrregularSheetFillRhino(
        IEnumerable<Curve> sheetOutlines,
        IReadOnlyList<IReadOnlyList<Curve>> sheetHoles,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        bool simplifyCurves,
        double simplifyTolerance,
        int seed,
        int maxCandidates,
        PackingCornerMode cornerMode = PackingCornerMode.BottomLeft)
    {
        _sheetOutlines = sheetOutlines.Select(curve => curve.DuplicateCurve()).ToList();
        _sheetHoles = sheetHoles;
        _spacing = Math.Max(0, spacing);
        _tolerance = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        _sortMode = sortMode;
        _cornerMode = cornerMode;
        _simplifyCurves = simplifyCurves;
        _simplifyTolerance = Math.Max(simplifyTolerance, _tolerance);
        _seed = seed;
        _maxCandidates = Math.Max(30, maxCandidates <= 0 ? 300 : maxCandidates);
        _rotationsDeg = rotationsDeg == null
            ? new List<double>()
            : rotationsDeg.Where(RhinoMath.IsValidDouble).Distinct().ToList();

        if (_rotationsDeg.Count == 0)
        {
            _rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        }
    }

    public PackingResult Pack(IEnumerable<Curve>? inputCurves)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new PackingResult();
        var sheets = PrepareSheets();
        foreach (var sheet in sheets)
        {
            result.SheetPreviewCurves.Add(sheet.OuterOriginal.DuplicateCurve());
            result.SheetPreviewCurves.AddRange(sheet.HolesOriginal.Select(hole => hole.DuplicateCurve()));
        }

        if (sheets.Count == 0)
        {
            result.Report = "No valid sheet outlines.";
            result.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
            return result;
        }

        var input = inputCurves?.ToList() ?? new List<Curve>();
        result.InputCount = input.Count;
        var prepared = input
            .Select((curve, index) => PreparePart(curve, index))
            .Where(part => part != null)
            .Cast<PackingPart>()
            .ToList();

        result.PreparedCount = prepared.Count;
        result.InvalidCount = result.InputCount - result.PreparedCount;
        var ordered = SortParts(prepared);
        var placed = new List<PlacedPart>();
        var placedBySheet = sheets.ToDictionary(sheet => sheet.Index, _ => new List<PlacedPart>());

        foreach (var part in ordered)
        {
            PlacedPart? accepted = null;
            foreach (var sheet in sheets)
            {
                accepted = FindBestPlacement(part, sheet, placedBySheet[sheet.Index], result);
                if (accepted != null)
                {
                    break;
                }
            }

            if (accepted == null)
            {
                result.UnplacedCurves.Add(part.SourceCurve.DuplicateCurve());
                result.FailureReasons.Add("No valid position found in any sheet region.");
                continue;
            }

            placed.Add(accepted);
            placedBySheet[accepted.SheetIndex].Add(accepted);
            result.PackedCurves.Add(accepted.OutputCurve);
            result.Transforms.Add(accepted.Transform);
            result.SourceIndices.Add(accepted.SourceIndex);
            result.SheetIndices.Add(accepted.SheetIndex);
        }

        result.UsedLength = result.PackedCurves.Count == 0 ? 0 : result.PackedCurves.Max(curve => curve.GetBoundingBox(true).Max.X);
        result.Utilization = ComputeUtilization(result.PackedCurves, sheets);
        result.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
        result.Report = BuildReport(result, sheets.Count);
        return result;
    }

    private List<SheetRegion> PrepareSheets()
    {
        var sheets = new List<SheetRegion>();
        for (var i = 0; i < _sheetOutlines.Count; i++)
        {
            var outer = SimplifySheetCurve(_sheetOutlines[i]);
            if (outer == null || !outer.IsClosed || !outer.IsPlanar(_tolerance) || !outer.TryGetPlane(out var sheetPlane, _tolerance))
            {
                continue;
            }

            var sheetToWork = Transform.PlaneToPlane(sheetPlane, Plane.WorldXY);
            var workToSheet = Transform.PlaneToPlane(Plane.WorldXY, sheetPlane);
            var outerWork = outer.DuplicateCurve();
            outerWork.Transform(sheetToWork);

            var holes = i < _sheetHoles.Count
                ? _sheetHoles[i].Select(SimplifySheetCurve).Where(curve => curve != null && curve.IsClosed).Cast<Curve>().ToList()
                : new List<Curve>();
            var holesWork = new List<Curve>();
            foreach (var hole in holes)
            {
                var holeWork = hole.DuplicateCurve();
                holeWork.Transform(sheetToWork);
                holesWork.Add(holeWork);
            }

            var effectiveOuter = OffsetClosedCurve(outerWork, inward: true) ?? outerWork.DuplicateCurve();
            var effectiveHoles = holesWork
                .Select(hole => OffsetClosedCurve(hole, inward: false) ?? hole.DuplicateCurve())
                .ToList();

            sheets.Add(new SheetRegion
            {
                Index = i,
                OuterOriginal = outer,
                WorkToSheet = workToSheet,
                OuterEffective = effectiveOuter,
                OuterVertices = GetCurveVertices(effectiveOuter).ToList(),
                HolesOriginal = holes,
                HolesEffective = effectiveHoles,
                HoleVertices = effectiveHoles.Select(hole => GetCurveVertices(hole).ToList()).ToList(),
                HoleEffectiveBounds = effectiveHoles.Select(hole => hole.GetBoundingBox(true)).ToList(),
                Bounds = outerWork.GetBoundingBox(true),
                UsableArea = Math.Max(0, Area(outerWork) - holesWork.Sum(Area))
            });
        }

        return sheets;
    }

    private PlacedPart? FindBestPlacement(
        PackingPart part,
        SheetRegion sheet,
        List<PlacedPart> placedOnSheet,
        PackingResult result)
    {
        var rotCount = _rotationsDeg.Count;
        var rotResults = new PlacedPart?[rotCount];
        var totalCandidates = 0;
        var totalCollisions = 0;

        // Rotations are independent reads — safe to evaluate in parallel.
        Parallel.For(0, rotCount, i =>
        {
            var (rotBest, cands, colls) = EvaluateRotation(part, _rotationsDeg[i], sheet, placedOnSheet);
            rotResults[i] = rotBest;
            Interlocked.Add(ref totalCandidates, cands);
            Interlocked.Add(ref totalCollisions, colls);
        });

        result.CandidateCount += totalCandidates;
        result.CollisionCheckCount += totalCollisions;

        PlacedPart? best = null;
        foreach (var rotBest in rotResults)
        {
            if (rotBest != null && (best == null || IsBetterPlacement(rotBest.Bounds, best.Bounds)))
                best = rotBest;
        }

        return best;
    }

    private (PlacedPart? best, int candidateCount, int collisionCount) EvaluateRotation(
        PackingPart part, double angle, SheetRegion sheet, List<PlacedPart> placedOnSheet)
    {
        var rotated = BuildRotatedPart(part, angle);
        if (rotated == null)
        {
            return (null, 0, 0);
        }

        PlacedPart? rotationBest = null;
        var candidateCount = 0;
        var collisionCount = 0;
        var validCandidates = 0;

        foreach (var candidate in GenerateCandidatePoints(sheet, rotated, placedOnSheet))
        {
            candidateCount++;
            var curve = rotated.Curve.DuplicateCurve();
            var translation = Transform.Translation(candidate.X, candidate.Y, 0);
            curve.Transform(translation);
            var translatedVertices = TranslateVertices(rotated.Vertices, candidate);
            var bounds = new BoundingBox(translatedVertices);
            var translatedInterior = new Point3d(
                rotated.InteriorPoint.X + candidate.X,
                rotated.InteriorPoint.Y + candidate.Y,
                0);

            if (!IsInsideSheetRegion(curve, bounds, translatedVertices, rotated.Area, sheet, translatedInterior))
            {
                continue;
            }

            if (CollidesWithAny(curve, bounds, translatedVertices, placedOnSheet, out var collisionChecks))
            {
                collisionCount += collisionChecks;
                continue;
            }

            collisionCount += collisionChecks;
            var outputCurve = curve.DuplicateCurve();
            outputCurve.Transform(sheet.WorkToSheet);
            var placement = new PlacedPart
            {
                Curve = curve,
                OutputCurve = outputCurve,
                Bounds = bounds,
                Vertices = translatedVertices,
                ShapeVertices = rotated.Vertices,
                PlacedOrigin = new Point3d(candidate.X, candidate.Y, 0),
                BottomLeft = bounds.Min,
                Transform = rotated.TransformToOrigin * translation * sheet.WorkToSheet,
                SourceIndex = part.SourceIndex,
                SheetIndex = sheet.Index
            };

            if (rotationBest == null || IsBetterPlacement(placement.Bounds, rotationBest.Bounds))
            {
                rotationBest = placement;
            }

            validCandidates++;
            if (validCandidates >= MaxValidCandidatesPerRotation)
            {
                break;
            }
        }

        return (rotationBest, candidateCount, collisionCount);
    }

    private IEnumerable<Point3d> GenerateCandidatePoints(SheetRegion sheet, RotatedPart part, List<PlacedPart> placedOnSheet)
    {
        var maxX = sheet.Bounds.Max.X - part.Width;
        var maxY = sheet.Bounds.Max.Y - part.Height;
        var candidates = new List<Point3d>
        {
            new(sheet.Bounds.Min.X, sheet.Bounds.Min.Y, 0),
            new(maxX, sheet.Bounds.Min.Y, 0),
            new(sheet.Bounds.Min.X, maxY, 0),
            new(maxX, maxY, 0)
        };

        // IFP (Inner Fit Polygon) candidates: positions where the part touches the sheet boundary.
        // Computed via Minkowski erosion — these are guaranteed near-valid positions for irregular sheets.
        candidates.AddRange(ComputeIfpCandidates(sheet, part));

        foreach (var vertex in sheet.OuterVertices)
        {
            AddSheetCandidates(candidates, vertex, part.Width, part.Height);
        }
        AddBoundaryContactCandidates(candidates, sheet.OuterEffective, part.Width, part.Height);

        for (var i = 0; i < sheet.HolesEffective.Count; i++)
        {
            foreach (var vertex in sheet.HoleVertices[i])
            {
                AddSheetCandidates(candidates, vertex, part.Width, part.Height);
            }
            AddBoundaryContactCandidates(candidates, sheet.HolesEffective[i], part.Width, part.Height);
        }

        foreach (var placed in placedOnSheet)
        {
            var bounds = placed.Bounds;
            candidates.Add(new Point3d(bounds.Max.X + _spacing, bounds.Min.Y, 0));
            candidates.Add(new Point3d(bounds.Min.X, bounds.Max.Y + _spacing, 0));
            candidates.Add(new Point3d(bounds.Max.X + _spacing, bounds.Max.Y + _spacing, 0));
            foreach (var vertex in placed.Vertices)
            {
                candidates.Add(new Point3d(vertex.X + _spacing, vertex.Y, 0));
                candidates.Add(new Point3d(vertex.X, vertex.Y + _spacing, 0));
                candidates.Add(new Point3d(vertex.X - part.Width - _spacing, vertex.Y, 0));
                candidates.Add(new Point3d(vertex.X, vertex.Y - part.Height - _spacing, 0));
            }

            // NFP contact candidates: Minkowski difference gives exact positions where the
            // new part would be in tight contact with this placed part.
            candidates.AddRange(ComputeNfpContactCandidates(placed, part));

            // Edge-sweep: slide the new part along each edge of placed parts.
            // Vertex-only candidates miss tight contact positions on long edges.
            var edgeStep = Math.Max(Math.Min(part.Width, part.Height) * 0.5, Math.Max(_spacing, _tolerance) * 4);
            if (RhinoMath.IsValidDouble(edgeStep) && edgeStep > _tolerance)
            {
                var verts = placed.Vertices;
                var vcount = verts.Count;
                for (var vi = 0; vi < vcount - 1; vi++)
                {
                    var p0 = verts[vi];
                    var p1 = verts[vi + 1];
                    var dx = p1.X - p0.X;
                    var dy = p1.Y - p0.Y;
                    var edgeLen = Math.Sqrt(dx * dx + dy * dy);
                    if (edgeLen < edgeStep * 2)
                    {
                        continue;
                    }

                    var steps = Math.Min(8, (int)Math.Ceiling(edgeLen / edgeStep));
                    for (var s = 1; s < steps; s++)
                    {
                        var t = (double)s / steps;
                        var px = p0.X + t * dx;
                        var py = p0.Y + t * dy;
                        candidates.Add(new Point3d(px + _spacing, py, 0));
                        candidates.Add(new Point3d(px, py + _spacing, 0));
                        candidates.Add(new Point3d(px - part.Width - _spacing, py, 0));
                        candidates.Add(new Point3d(px, py - part.Height - _spacing, 0));
                    }
                }
            }
        }

        AddSparseGridCandidates(candidates, sheet, part);
        var ordered = candidates
            .Select(ClampSmallZ)
            .Where(point => CandidateBoundsCanTouchSheet(point, part, sheet))
            .Distinct(new Point3dToleranceComparer(Math.Max(_tolerance, 1e-6)))
            .ToList();

        ordered = OrderCandidates(ordered, part.Width, part.Height).ToList();

        if (_seed != 0)
        {
            var random = new Random(_seed + part.SourceIndex * 7919 + sheet.Index * 104729);
            ordered = OrderCandidatesWithSeed(ordered, part.Width, part.Height, random).ToList();
        }

        return ordered.Take(_maxCandidates);
    }

    private IEnumerable<Point3d> OrderCandidates(IEnumerable<Point3d> candidates, double partWidth, double partHeight)
    {
        return _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidates.OrderBy(point => point.Y).ThenByDescending(point => point.X + partWidth),
            PackingCornerMode.TopLeft => candidates.OrderByDescending(point => point.Y + partHeight).ThenBy(point => point.X),
            PackingCornerMode.TopRight => candidates.OrderByDescending(point => point.Y + partHeight).ThenByDescending(point => point.X + partWidth),
            _ => candidates.OrderBy(point => point.Y).ThenBy(point => point.X)
        };
    }

    private IEnumerable<Point3d> OrderCandidatesWithSeed(IEnumerable<Point3d> candidates, double partWidth, double partHeight, Random random)
    {
        var tolerance = Math.Max(_tolerance, 1e-6);
        return _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidates
                .OrderBy(point => Math.Round(point.Y / tolerance))
                .ThenByDescending(point => Math.Round((point.X + partWidth) / tolerance))
                .ThenBy(_ => random.Next()),
            PackingCornerMode.TopLeft => candidates
                .OrderByDescending(point => Math.Round((point.Y + partHeight) / tolerance))
                .ThenBy(point => Math.Round(point.X / tolerance))
                .ThenBy(_ => random.Next()),
            PackingCornerMode.TopRight => candidates
                .OrderByDescending(point => Math.Round((point.Y + partHeight) / tolerance))
                .ThenByDescending(point => Math.Round((point.X + partWidth) / tolerance))
                .ThenBy(_ => random.Next()),
            _ => candidates
                .OrderBy(point => Math.Round(point.Y / tolerance))
                .ThenBy(point => Math.Round(point.X / tolerance))
                .ThenBy(_ => random.Next())
        };
    }

    private void AddSheetCandidates(List<Point3d> candidates, Point3d vertex, double partWidth, double partHeight)
    {
        candidates.Add(new Point3d(vertex.X, vertex.Y, 0));
        candidates.Add(new Point3d(vertex.X - partWidth, vertex.Y, 0));
        candidates.Add(new Point3d(vertex.X, vertex.Y - partHeight, 0));
        candidates.Add(new Point3d(vertex.X - partWidth, vertex.Y - partHeight, 0));
        if (_spacing > _tolerance)
        {
            candidates.Add(new Point3d(vertex.X + _spacing, vertex.Y, 0));
            candidates.Add(new Point3d(vertex.X, vertex.Y + _spacing, 0));
            candidates.Add(new Point3d(vertex.X - partWidth - _spacing, vertex.Y, 0));
            candidates.Add(new Point3d(vertex.X, vertex.Y - partHeight - _spacing, 0));
        }
    }

    private void AddSparseGridCandidates(List<Point3d> candidates, SheetRegion sheet, RotatedPart part)
    {
        var step = Math.Max(Math.Max(part.Width, part.Height) * 0.5, Math.Max(_spacing, _tolerance) * 4);
        if (!RhinoMath.IsValidDouble(step) || step <= _tolerance)
        {
            return;
        }

        var maxGrid = Math.Min(80, Math.Max(10, _maxCandidates / 6));
        var added = 0;
        for (var y = sheet.Bounds.Min.Y; y <= sheet.Bounds.Max.Y - part.Height + _tolerance && added < maxGrid; y += step)
        {
            for (var x = sheet.Bounds.Min.X; x <= sheet.Bounds.Max.X - part.Width + _tolerance && added < maxGrid; x += step)
            {
                candidates.Add(new Point3d(x, y, 0));
                added++;
            }
        }
    }

    private bool CandidateBoundsCanTouchSheet(Point3d candidate, RotatedPart part, SheetRegion sheet)
    {
        return candidate.X + part.Width >= sheet.Bounds.Min.X - _tolerance
            && candidate.Y + part.Height >= sheet.Bounds.Min.Y - _tolerance
            && candidate.X <= sheet.Bounds.Max.X + _tolerance
            && candidate.Y <= sheet.Bounds.Max.Y + _tolerance;
    }

    private void AddBoundaryContactCandidates(List<Point3d> candidates, Curve boundary, double partWidth, double partHeight)
    {
        var length = boundary.GetLength();
        var sampleStep = Math.Max(Math.Min(partWidth, partHeight) * 0.5, Math.Max(_spacing, _tolerance) * 8);
        if (!RhinoMath.IsValidDouble(length) || !RhinoMath.IsValidDouble(sampleStep) || sampleStep <= _tolerance)
        {
            return;
        }

        var count = Math.Max(4, Math.Min(32, (int)Math.Ceiling(length / sampleStep)));
        var parameters = boundary.DivideByCount(count, true);
        if (parameters == null)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            AddSheetCandidates(candidates, boundary.PointAt(parameter), partWidth, partHeight);
        }
    }

    private bool IsInsideSheetRegion(
        Curve curve,
        BoundingBox curveBounds,
        IReadOnlyList<Point3d> vertices,
        double partArea,
        SheetRegion sheet,
        Point3d interiorPoint)
    {
        if (curveBounds.Min.X < sheet.Bounds.Min.X - _tolerance
            || curveBounds.Min.Y < sheet.Bounds.Min.Y - _tolerance
            || curveBounds.Max.X > sheet.Bounds.Max.X + _tolerance
            || curveBounds.Max.Y > sheet.Bounds.Max.Y + _tolerance)
        {
            return false;
        }

        if (partArea <= _tolerance * _tolerance)
        {
            return false;
        }

        if (FastInsideSheetRegion(curve, curveBounds, vertices, sheet, interiorPoint))
        {
            return true;
        }

        // Polylines: FastInsideSheetRegion is a complete containment test (edge-intersection
        // + vertex containment). If it fails, reject without the expensive boolean intersection.
        if (curve.TryGetPolyline(out _))
        {
            return false;
        }

        var minArea = Math.Max(_tolerance * _tolerance, partArea * 1e-6);
        try
        {
            var inside = Curve.CreateBooleanIntersection(curve, sheet.OuterEffective, _tolerance);
            var insideArea = inside == null ? 0 : inside.Sum(item => Math.Abs(Area(item)));
            if (insideArea < partArea - minArea)
            {
                return false;
            }
        }
        catch
        {
            foreach (var vertex in vertices)
            {
                var containment = sheet.OuterEffective.Contains(vertex, Plane.WorldXY, _tolerance);
                if (containment != PointContainment.Inside && containment != PointContainment.Coincident)
                {
                    return false;
                }
            }
        }

        for (var i = 0; i < sheet.HolesEffective.Count; i++)
        {
            var hole = sheet.HolesEffective[i];
            if (!BoundingBoxesMightInteract(curveBounds, sheet.HoleEffectiveBounds[i], 0))
            {
                continue;
            }

            try
            {
                var intersection = Curve.CreateBooleanIntersection(curve, hole, _tolerance);
                if (intersection != null && intersection.Any(item => Math.Abs(Area(item)) > minArea))
                {
                    return false;
                }
            }
            catch
            {
                foreach (var vertex in vertices)
                {
                    if (hole.Contains(vertex, Plane.WorldXY, _tolerance) == PointContainment.Inside)
                    {
                        return false;
                    }
                }
            }

            if (hole.Contains(interiorPoint, Plane.WorldXY, _tolerance) == PointContainment.Inside)
            {
                return false;
            }
        }

        return true;
    }

    private bool FastInsideSheetRegion(
        Curve curve,
        BoundingBox curveBounds,
        IReadOnlyList<Point3d> vertices,
        SheetRegion sheet,
        Point3d interiorPoint)
    {
        if (vertices.Count == 0)
        {
            return false;
        }

        foreach (var vertex in vertices)
        {
            var containment = sheet.OuterEffective.Contains(vertex, Plane.WorldXY, _tolerance);
            if (containment != PointContainment.Inside && containment != PointContainment.Coincident)
            {
                return false;
            }
        }

        var outerIntersections = Intersection.CurveCurve(curve, sheet.OuterEffective, _tolerance, _tolerance);
        if (outerIntersections != null && outerIntersections.Count > 0)
        {
            return false;
        }

        for (var i = 0; i < sheet.HolesEffective.Count; i++)
        {
            var hole = sheet.HolesEffective[i];
            if (!BoundingBoxesMightInteract(curveBounds, sheet.HoleEffectiveBounds[i], 0))
            {
                continue;
            }

            foreach (var vertex in vertices)
            {
                if (hole.Contains(vertex, Plane.WorldXY, _tolerance) == PointContainment.Inside)
                {
                    return false;
                }
            }

            if (hole.Contains(interiorPoint, Plane.WorldXY, _tolerance) == PointContainment.Inside)
            {
                return false;
            }

            var holeIntersections = Intersection.CurveCurve(curve, hole, _tolerance, _tolerance);
            if (holeIntersections != null && holeIntersections.Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool CollidesWithAny(
        Curve testCurve,
        BoundingBox bounds,
        IReadOnlyList<Point3d> vertices,
        List<PlacedPart> placed,
        out int collisionChecks)
    {
        collisionChecks = 0;
        foreach (var placedPart in placed)
        {
            collisionChecks++;
            if (CurvesOverlapOrTooClose(testCurve, bounds, vertices, placedPart))
            {
                return true;
            }
        }

        return false;
    }

    private bool CurvesOverlapOrTooClose(
        Curve a,
        BoundingBox aBox,
        IReadOnlyList<Point3d> aVertices,
        PlacedPart placed)
    {
        return BoundingBoxesMightInteract(aBox, placed.Bounds)
            && (HasInteriorOverlap(a, aVertices, placed.Curve, placed.Vertices)
                || (_spacing > _tolerance && ApproximateMinimumDistance(a, placed.Curve) < _spacing - _tolerance));
    }

    private bool BoundingBoxesMightInteract(BoundingBox a, BoundingBox b)
    {
        return BoundingBoxesMightInteract(a, b, _spacing);
    }

    private bool BoundingBoxesMightInteract(BoundingBox a, BoundingBox b, double spacing)
    {
        var pad = spacing + _tolerance;
        return a.Max.X + pad >= b.Min.X
            && b.Max.X + pad >= a.Min.X
            && a.Max.Y + pad >= b.Min.Y
            && b.Max.Y + pad >= a.Min.Y;
    }

    private bool HasInteriorOverlap(Curve a, IReadOnlyList<Point3d> aVertices, Curve b, IReadOnlyList<Point3d> bVertices)
    {
        var intersections = Intersection.CurveCurve(a, b, _tolerance, _tolerance);
        if (intersections != null && (intersections.Count > 1 || intersections.Count == 1 && _spacing > _tolerance))
        {
            return true;
        }

        foreach (var vertex in aVertices)
        {
            if (b.Contains(vertex, Plane.WorldXY, _tolerance) == PointContainment.Inside)
            {
                return true;
            }
        }

        foreach (var vertex in bVertices)
        {
            if (a.Contains(vertex, Plane.WorldXY, _tolerance) == PointContainment.Inside)
            {
                return true;
            }
        }

        // For non-self-intersecting polygons (polylines), edge-intersection and vertex
        // containment tests are a complete overlap test. Skip the expensive boolean
        // intersection and centroid containment fallbacks.
        if (a.TryGetPolyline(out _) && b.TryGetPolyline(out _))
        {
            return false;
        }

        try
        {
            var intersection = Curve.CreateBooleanIntersection(a, b, _tolerance);
            var minArea = Math.Max(_tolerance * _tolerance, 1e-8);
            if (intersection != null && intersection.Any(curve => Math.Abs(Area(curve)) > minArea))
            {
                return true;
            }
        }
        catch
        {
            return intersections != null && intersections.Count > 0;
        }

        var aSample = GetInteriorSamplePoint(a);
        var bSample = GetInteriorSamplePoint(b);
        return a.Contains(bSample, Plane.WorldXY, _tolerance) == PointContainment.Inside
            || b.Contains(aSample, Plane.WorldXY, _tolerance) == PointContainment.Inside;
    }

    private double ApproximateMinimumDistance(Curve a, Curve b)
    {
        var minimum = double.MaxValue;
        var bIsPoly = b.TryGetPolyline(out var polyB);
        var aIsPoly = a.TryGetPolyline(out var polyA);

        foreach (var point in SampleCurve(a))
        {
            if (bIsPoly)
            {
                minimum = Math.Min(minimum, point.DistanceTo(polyB.ClosestPoint(point)));
            }
            else if (b.ClosestPoint(point, out var t))
            {
                minimum = Math.Min(minimum, point.DistanceTo(b.PointAt(t)));
            }
        }

        foreach (var point in SampleCurve(b))
        {
            if (aIsPoly)
            {
                minimum = Math.Min(minimum, point.DistanceTo(polyA.ClosestPoint(point)));
            }
            else if (a.ClosestPoint(point, out var t))
            {
                minimum = Math.Min(minimum, point.DistanceTo(a.PointAt(t)));
            }
        }

        return minimum;
    }

    private PackingPart? PreparePart(Curve? curve, int sourceIndex)
    {
        var source = curve?.DuplicateCurve();
        if (source == null || !source.IsClosed || !source.IsPlanar(_tolerance) || !source.TryGetPlane(out var sourcePlane, _tolerance))
        {
            return null;
        }

        var sourceToWork = Transform.PlaneToPlane(sourcePlane, Plane.WorldXY);
        var workCurve = source.DuplicateCurve();
        workCurve.Transform(sourceToWork);
        var prepared = SimplifyPartCurve(workCurve);
        if (prepared == null || !prepared.IsClosed || !prepared.IsPlanar(_tolerance) || !prepared.TryGetPlane(out var plane, _tolerance))
        {
            return null;
        }

        if (plane.ZAxis * Vector3d.ZAxis < 0)
        {
            prepared.Reverse();
        }

        MoveToOrigin(prepared, out var normalize);
        var area = Area(prepared);
        if (area <= _tolerance * _tolerance)
        {
            return null;
        }

        return new PackingPart
        {
            SourceIndex = sourceIndex,
            SourceCurve = source,
            PreparedCurve = prepared,
            SourceToWork = sourceToWork,
            NormalizeTransform = normalize,
            Area = area,
            Width = Width(prepared),
            Height = Height(prepared)
        };
    }

    private RotatedPart? BuildRotatedPart(PackingPart part, double angleDeg)
    {
        var rotation = Transform.Rotation(RhinoMath.ToRadians(angleDeg), Vector3d.ZAxis, Point3d.Origin);
        var curve = part.PreparedCurve.DuplicateCurve();
        curve.Transform(rotation);
        MoveToOrigin(curve, out var originMove);
        var vertices = GetCurveVertices(curve).ToList();
        if (vertices.Count < 3)
        {
            return null;
        }

        var bounds = curve.GetBoundingBox(true);
        var area = Area(curve);
        return new RotatedPart
        {
            Curve = curve,
            SourceIndex = part.SourceIndex,
            Width = bounds.Max.X - bounds.Min.X,
            Height = bounds.Max.Y - bounds.Min.Y,
            Area = area,
            Vertices = vertices,
            InteriorPoint = GetInteriorSamplePoint(curve),
            TransformToOrigin = part.SourceToWork * part.NormalizeTransform * rotation * originMove
        };
    }

    private Curve? SimplifyPartCurve(Curve? curve)
    {
        if (curve == null)
        {
            return null;
        }

        var duplicate = curve.DuplicateCurve();
        var result = duplicate;
        if (_simplifyCurves)
        {
            var simplified = duplicate.Simplify(CurveSimplifyOptions.All, _simplifyTolerance, RhinoMath.ToRadians(1));
            if (simplified != null && simplified.IsClosed)
            {
                result = simplified;
            }
        }

        var polylinePointCount = TryGetPolylinePointCount(result);
        if (_simplifyCurves || polylinePointCount > HardMaxPolylinePoints || polylinePointCount == 0)
        {
            var polyline = ToBoundedPolylineCurve(result);
            if (polyline != null && polyline.IsClosed)
            {
                return polyline;
            }
        }

        return result;
    }

    private Curve? SimplifySheetCurve(Curve? curve)
    {
        if (curve == null)
        {
            return null;
        }

        var duplicate = curve.DuplicateCurve();
        if (!_simplifyCurves)
        {
            return duplicate;
        }

        var simplified = duplicate.Simplify(CurveSimplifyOptions.All, _simplifyTolerance, RhinoMath.ToRadians(1));
        return simplified != null && simplified.IsClosed ? simplified : duplicate;
    }

    private Curve? ToBoundedPolylineCurve(Curve curve)
    {
        if (!curve.TryGetPolyline(out var polyline))
        {
            var polylineCurve = curve.ToPolyline(_simplifyTolerance, _simplifyTolerance, 0, 0);
            if (polylineCurve == null || !polylineCurve.TryGetPolyline(out polyline))
            {
                return curve;
            }
        }

        var reduced = ReducePolyline(polyline, MaxPreparedPolylinePoints);
        return reduced.Count < 4 ? curve : reduced.ToNurbsCurve();
    }

    private static int TryGetPolylinePointCount(Curve curve)
    {
        return curve.TryGetPolyline(out var polyline) ? polyline.Count : 0;
    }

    private Polyline ReducePolyline(Polyline polyline, int maxPoints)
    {
        var points = new List<Point3d>();
        for (var i = 0; i < polyline.Count; i++)
        {
            var point = polyline[i];
            if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > _tolerance)
            {
                points.Add(point);
            }
        }

        if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= _tolerance)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count > maxPoints)
        {
            var reduced = new List<Point3d>();
            var step = (double)points.Count / maxPoints;
            for (var i = 0; i < maxPoints; i++)
            {
                reduced.Add(points[Math.Min(points.Count - 1, (int)Math.Floor(i * step))]);
            }
            points = reduced;
        }

        var result = new Polyline(points);
        if (result.Count > 0)
        {
            result.Add(result[0]);
        }

        return result;
    }

    private Curve? OffsetClosedCurve(Curve curve, bool inward)
    {
        if (_spacing <= _tolerance)
        {
            return curve.DuplicateCurve();
        }

        var originalArea = Area(curve);
        var candidates = new List<Curve>();
        foreach (var distance in new[] { _spacing, -_spacing })
        {
            try
            {
                var offsets = curve.Offset(Plane.WorldXY, distance, _tolerance, CurveOffsetCornerStyle.Sharp);
                if (offsets != null)
                {
                    candidates.AddRange(offsets.Where(item => item != null && item.IsClosed));
                }
            }
            catch
            {
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return inward
            ? candidates.OrderBy(item => Math.Abs(Area(item) - originalArea)).Where(item => Area(item) < originalArea + _tolerance).OrderByDescending(Area).FirstOrDefault()
            : candidates.OrderByDescending(Area).FirstOrDefault();
    }

    private List<PackingPart> SortParts(List<PackingPart> parts)
    {
        var random = _seed == 0 ? null : new Random(_seed);
        return _sortMode switch
        {
            PackingSortMode.UserOrder => parts.OrderBy(part => part.SourceIndex).ToList(),
            PackingSortMode.AreaDescending => parts.OrderByDescending(part => part.Area).ThenBy(_ => random?.Next() ?? 0).ToList(),
            PackingSortMode.WidthDescending => parts.OrderByDescending(part => part.Width).ThenBy(_ => random?.Next() ?? 0).ToList(),
            PackingSortMode.HeightDescending => parts.OrderByDescending(part => part.Height).ThenBy(_ => random?.Next() ?? 0).ToList(),
            PackingSortMode.MaxDimensionDescending => parts.OrderByDescending(part => Math.Max(part.Width, part.Height)).ThenBy(_ => random?.Next() ?? 0).ToList(),
            _ => parts.OrderByDescending(part => part.Area).ThenBy(_ => random?.Next() ?? 0).ToList()
        };
    }

    private static void MoveToOrigin(Curve curve, out Transform move)
    {
        var bounds = curve.GetBoundingBox(true);
        move = Transform.Translation(-bounds.Min.X, -bounds.Min.Y, 0);
        curve.Transform(move);
    }

    private static IEnumerable<Point3d> SampleCurve(Curve curve)
    {
        var points = new List<Point3d>();
        if (curve.TryGetPolyline(out var polyline))
        {
            for (var i = 0; i < polyline.Count; i++)
            {
                points.Add(polyline[i]);
            }
            return points;
        }

        points.Add(curve.PointAtStart);
        points.Add(curve.PointAtEnd);
        var parameters = curve.DivideByCount(64, true);
        if (parameters != null)
        {
            points.AddRange(parameters.Select(curve.PointAt));
        }

        return points;
    }

    private IEnumerable<Point3d> GetCurveVertices(Curve curve)
    {
        var vertices = new List<Point3d>();
        if (curve.TryGetPolyline(out var polyline))
        {
            for (var i = 0; i < polyline.Count; i++)
            {
                vertices.Add(polyline[i]);
            }
            return vertices;
        }

        var polylineCurve = curve.ToPolyline(_tolerance, _tolerance, 0, 0);
        if (polylineCurve != null && polylineCurve.TryGetPolyline(out polyline))
        {
            for (var i = 0; i < polyline.Count; i++)
            {
                vertices.Add(polyline[i]);
            }
        }

        return vertices;
    }

    private static List<Point3d> TranslateVertices(IReadOnlyList<Point3d> vertices, Point3d offset)
    {
        var translated = new List<Point3d>(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            translated.Add(new Point3d(vertex.X + offset.X, vertex.Y + offset.Y, vertex.Z + offset.Z));
        }

        return translated;
    }

    private static Point3d GetInteriorSamplePoint(Curve curve)
    {
        var area = AreaMassProperties.Compute(curve);
        return area?.Centroid ?? curve.GetBoundingBox(true).Center;
    }

    private static Point3d ClampSmallZ(Point3d point)
    {
        return new Point3d(point.X, point.Y, 0);
    }

    private bool IsBetterPlacement(BoundingBox candidate, BoundingBox currentBest)
    {
        return _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidate.Min.Y < currentBest.Min.Y - _tolerance
                || (Math.Abs(candidate.Min.Y - currentBest.Min.Y) <= _tolerance && candidate.Max.X > currentBest.Max.X + _tolerance),
            PackingCornerMode.TopLeft => candidate.Max.Y > currentBest.Max.Y + _tolerance
                || (Math.Abs(candidate.Max.Y - currentBest.Max.Y) <= _tolerance && candidate.Min.X < currentBest.Min.X - _tolerance),
            PackingCornerMode.TopRight => candidate.Max.Y > currentBest.Max.Y + _tolerance
                || (Math.Abs(candidate.Max.Y - currentBest.Max.Y) <= _tolerance && candidate.Max.X > currentBest.Max.X + _tolerance),
            _ => candidate.Min.Y < currentBest.Min.Y - _tolerance
                || (Math.Abs(candidate.Min.Y - currentBest.Min.Y) <= _tolerance && candidate.Min.X < currentBest.Min.X - _tolerance)
        };
    }

    private static double ComputeUtilization(IEnumerable<Curve> placed, IReadOnlyList<SheetRegion> sheets)
    {
        var sheetArea = sheets.Sum(sheet => sheet.UsableArea);
        return sheetArea <= RhinoMath.ZeroTolerance ? 0 : placed.Sum(Area) / sheetArea;
    }

    // Compute IFP (Inner Fit Polygon) candidates using Minkowski erosion.
    // For each sheet edge with outward normal n, the IFP constraint is:
    //   n · p ≤ (n · P0) − h(B, n)
    // where h(B, n) = max support of the part in direction n.
    // The IFP vertices (intersections of adjacent constraints) are positions where
    // the part fits exactly tangent to two sheet edges — ideal packing seeds.
    private List<Point3d> ComputeIfpCandidates(SheetRegion sheet, RotatedPart part)
    {
        var result = new List<Point3d>();
        var verts = sheet.OuterVertices;
        var n = verts.Count;
        if (n < 3 || part.Vertices.Count < 3)
        {
            return result;
        }

        var edgeCount = (n > 1 && verts[0].DistanceTo(verts[n - 1]) < _tolerance) ? n - 1 : n;
        if (edgeCount < 3)
        {
            return result;
        }

        // Determine winding (positive signed area = CCW).
        var signedArea = 0.0;
        for (var i = 0; i < edgeCount; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % edgeCount];
            signedArea += a.X * b.Y - b.X * a.Y;
        }

        var isCcw = signedArea > 0;

        // Build shifted half-plane constraints for the IFP.
        var constraints = new List<(double NX, double NY, double C)>();
        for (var i = 0; i < edgeCount; i++)
        {
            var p0 = verts[i];
            var p1 = verts[(i + 1) % edgeCount];
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < _tolerance)
            {
                continue;
            }

            // Outward normal for CCW polygon: (dy/len, -dx/len); negate for CW.
            var sign = isCcw ? 1.0 : -1.0;
            var nx = sign * dy / len;
            var ny = sign * (-dx) / len;

            // Support of the part polygon in the outward direction.
            var support = double.MinValue;
            foreach (var v in part.Vertices)
            {
                var s = nx * v.X + ny * v.Y;
                if (s > support)
                {
                    support = s;
                }
            }

            if (!RhinoMath.IsValidDouble(support))
            {
                continue;
            }

            // IFP constraint: n · p ≤ (n · P0) − support
            constraints.Add((nx, ny, nx * p0.X + ny * p0.Y - support));
        }

        if (constraints.Count < 3)
        {
            return result;
        }

        // Compute IFP vertices by intersecting adjacent constraint pairs.
        var m = constraints.Count;
        var ifpVerts = new List<Point3d>(m);
        for (var i = 0; i < m; i++)
        {
            var c1 = constraints[i];
            var c2 = constraints[(i + 1) % m];
            var det = c1.NX * c2.NY - c2.NX * c1.NY;
            if (Math.Abs(det) < 1e-10)
            {
                continue;
            }

            var x = (c1.C * c2.NY - c2.C * c1.NY) / det;
            var y = (c1.NX * c2.C - c2.NX * c1.C) / det;
            if (RhinoMath.IsValidDouble(x) && RhinoMath.IsValidDouble(y))
            {
                ifpVerts.Add(new Point3d(x, y, 0));
            }
        }

        result.AddRange(ifpVerts);

        // Also sample IFP edges at regular intervals for better coverage.
        var sampleStep = Math.Max(Math.Min(part.Width, part.Height) * 0.5, _tolerance * 4);
        if (RhinoMath.IsValidDouble(sampleStep) && sampleStep > _tolerance && ifpVerts.Count > 1)
        {
            for (var i = 0; i < ifpVerts.Count; i++)
            {
                var a = ifpVerts[i];
                var b = ifpVerts[(i + 1) % ifpVerts.Count];
                var edgeLen = a.DistanceTo(b);
                if (edgeLen < sampleStep * 2)
                {
                    continue;
                }

                var steps = Math.Min(20, (int)Math.Ceiling(edgeLen / sampleStep));
                for (var s = 1; s < steps; s++)
                {
                    var t = (double)s / steps;
                    result.Add(new Point3d(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), 0));
                }
            }
        }

        return result;
    }

    // Compute NFP (No-Fit Polygon) contact candidates using the Minkowski difference.
    // For each pair (a ∈ placed shape, b ∈ sliding part), the position a − b + placed_origin
    // places the sliding part's vertex b exactly at the placed part's vertex a.
    // These are the exact contact positions between the two parts.
    private static List<Point3d> ComputeNfpContactCandidates(PlacedPart placed, RotatedPart part)
    {
        var result = new List<Point3d>();
        var A = placed.ShapeVertices;
        var B = part.Vertices;
        if (A.Count == 0 || B.Count < 3)
        {
            return result;
        }

        var ox = placed.PlacedOrigin.X;
        var oy = placed.PlacedOrigin.Y;

        // Limit vertices to keep candidate count manageable.
        const int MaxVerts = 16;
        var stepA = Math.Max(1, A.Count / MaxVerts);
        var stepB = Math.Max(1, B.Count / MaxVerts);

        for (var ai = 0; ai < A.Count; ai += stepA)
        {
            for (var bi = 0; bi < B.Count; bi += stepB)
            {
                result.Add(new Point3d(A[ai].X - B[bi].X + ox, A[ai].Y - B[bi].Y + oy, 0));
            }
        }

        return result;
    }

    private string BuildReport(PackingResult result, int sheetCount)
    {
        return $"Irregular Sheet Pack Placed: {result.PackedCurves.Count}, Unplaced: {result.UnplacedCurves.Count}, Invalid: {result.InvalidCount}, Sheets: {sheetCount}, Candidates: {result.CandidateCount}, Collision Checks: {result.CollisionCheckCount}, Utilization: {result.Utilization:P2}, Runtime: {result.RuntimeMilliseconds} ms";
    }

    private static double Area(Curve curve)
    {
        var area = AreaMassProperties.Compute(curve);
        return area == null ? 0 : Math.Abs(area.Area);
    }

    private static double Width(Curve curve)
    {
        var bounds = curve.GetBoundingBox(true);
        return bounds.Max.X - bounds.Min.X;
    }

    private static double Height(Curve curve)
    {
        var bounds = curve.GetBoundingBox(true);
        return bounds.Max.Y - bounds.Min.Y;
    }

    private sealed class SheetRegion
    {
        public int Index;
        public Curve OuterOriginal = null!;
        public Curve OuterEffective = null!;
        public Transform WorkToSheet;
        public List<Point3d> OuterVertices = new();
        public List<Curve> HolesOriginal = new();
        public List<Curve> HolesEffective = new();
        public List<List<Point3d>> HoleVertices = new();
        public List<BoundingBox> HoleEffectiveBounds = new();
        public BoundingBox Bounds;
        public double UsableArea;
    }

    private sealed class PackingPart
    {
        public int SourceIndex;
        public Curve SourceCurve = null!;
        public Curve PreparedCurve = null!;
        public Transform SourceToWork;
        public Transform NormalizeTransform;
        public double Area;
        public double Width;
        public double Height;
    }

    private sealed class RotatedPart
    {
        public Curve Curve = null!;
        public int SourceIndex;
        public double Width;
        public double Height;
        public double Area;
        public List<Point3d> Vertices = new();
        public Point3d InteriorPoint;
        public Transform TransformToOrigin;
    }

    private sealed class PlacedPart
    {
        public Curve Curve = null!;
        public Curve OutputCurve = null!;
        public BoundingBox Bounds;
        public List<Point3d> Vertices = new();
        public List<Point3d> ShapeVertices = new(); // vertices at origin (before placement translation)
        public Point3d PlacedOrigin; // translation applied during placement
        public Transform Transform;
        public Point3d BottomLeft;
        public int SourceIndex;
        public int SheetIndex;
    }
}
