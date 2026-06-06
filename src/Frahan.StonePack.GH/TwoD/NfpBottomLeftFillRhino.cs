using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Frahan.GH.TwoD;

public sealed class NfpBottomLeftFillRhino
{
    private const int MaxPreparedPolylinePoints = 128;
    private const int HardMaxPolylinePoints = 400;
    private const int MaxCandidatePointsPerRotation = 500;
    private const int MaxDiagnosticCurves = 0;
    private readonly double _sheetWidth;
    private readonly double _sheetLength;
    private readonly double _spacing;
    private readonly double _tolerance;
    private readonly List<double> _rotationsDeg;
    private readonly PackingSortMode _sortMode;
    private readonly PackingCornerMode _cornerMode;
    private readonly bool _simplifyCurves;
    private readonly double _simplifyTolerance;
    private readonly int _nfpMaxIterations;
    private readonly int _optimizationMode;
    private readonly int _optimizationIterations;
    private readonly int _seed;

    public NfpBottomLeftFillRhino(
        double sheetWidth,
        double sheetLength,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        bool simplifyCurves,
        double simplifyTolerance,
        int nfpMaxIterations,
        int optimizationMode,
        int optimizationIterations,
        int seed = 0,
        PackingCornerMode cornerMode = PackingCornerMode.BottomLeft)
    {
        _sheetWidth = sheetWidth;
        _sheetLength = sheetLength;
        _spacing = Math.Max(0, spacing);
        _tolerance = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        _sortMode = sortMode;
        _cornerMode = cornerMode;
        _simplifyCurves = simplifyCurves;
        _simplifyTolerance = Math.Max(simplifyTolerance, _tolerance);
        _nfpMaxIterations = Math.Max(250, nfpMaxIterations);
        _optimizationMode = Math.Max(0, optimizationMode);
        _optimizationIterations = Math.Max(0, optimizationIterations);
        _seed = seed;
        _rotationsDeg = rotationsDeg == null
            ? new List<double>()
            : rotationsDeg.Where(RhinoMath.IsValidDouble).Distinct().ToList();

        if (_rotationsDeg.Count == 0)
        {
            _rotationsDeg.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
        }

        if (_seed != 0 && _rotationsDeg.Count > 1)
        {
            var random = new Random(_seed);
            _rotationsDeg = _rotationsDeg.OrderBy(_ => random.Next()).ToList();
        }
    }

    public PackingResult Pack(IEnumerable<Curve>? inputCurves)
    {
        var stopwatch = Stopwatch.StartNew();
        var baseResult = new PackingResult { SheetPreview = GetSheetPreviewCurve() };
        if (inputCurves == null)
        {
            baseResult.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
            baseResult.Report = "No input curves.";
            return baseResult;
        }

        var input = inputCurves.ToList();
        baseResult.InputCount = input.Count;
        var prepared = input
            .Select((curve, index) => PreparePart(curve, index))
            .Where(part => part != null)
            .Cast<PackingPart>()
            .ToList();

        baseResult.PreparedCount = prepared.Count;
        baseResult.InvalidCount = baseResult.InputCount - baseResult.PreparedCount;
        if (prepared.Count == 0)
        {
            baseResult.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
            baseResult.Report = "No valid closed planar curves found.";
            return baseResult;
        }

        var sequences = BuildOptimizationSequences(prepared);
        var cache = new NfpCache();
        PackingResult? best = null;
        foreach (var sequence in sequences)
        {
            var candidate = PackSequence(sequence, cache);
            candidate.InputCount = baseResult.InputCount;
            candidate.PreparedCount = baseResult.PreparedCount;
            candidate.InvalidCount = baseResult.InvalidCount;
            if (best == null || IsBetterResult(candidate, best))
            {
                best = candidate;
            }
        }

        stopwatch.Stop();
        var result = best ?? baseResult;
        result.SheetPreview = GetSheetPreviewCurve();
        result.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
        result.NfpCacheHits = cache.Hits;
        result.NfpCacheMisses = cache.Misses;
        result.OptimizationRuns = sequences.Count;
        result.Report = BuildReport(result);
        return result;
    }

    private PackingResult PackSequence(List<PackingPart> parts, NfpCache cache)
    {
        var result = new PackingResult { SheetPreview = GetSheetPreviewCurve() };
        var placed = new List<PlacedPart>();
        foreach (var part in parts)
        {
            var bestPlacement = FindBestPlacement(part, placed, cache, result);
            if (bestPlacement == null)
            {
                result.UnplacedCurves.Add(part.PreparedCurve.DuplicateCurve());
                continue;
            }

            placed.Add(bestPlacement);
            result.PackedCurves.Add(bestPlacement.Curve);
            result.Transforms.Add(bestPlacement.Transform);
            result.SourceIndices.Add(part.SourceIndex);
        }

        result.UsedLength = GetUsedLength(result.PackedCurves);
        result.Utilization = ComputeUtilization(result.PackedCurves);
        return result;
    }

    private PlacedPart? FindBestPlacement(
        PackingPart part,
        List<PlacedPart> placed,
        NfpCache cache,
        PackingResult result)
    {
        PlacedPart? best = null;
        foreach (var angle in _rotationsDeg)
        {
            var sliding = BuildRotatedPart(part, angle);
            if (sliding == null)
            {
                continue;
            }

            var feasibleSpace = BuildFeasiblePlacementSpace(placed, sliding, cache, result);
            PlacedPart? rotationBest = null;
            foreach (var candidate in GenerateCandidatePoints(placed, sliding, feasibleSpace, result))
            {
                result.CandidateCount++;
                if (!IsInsideFeasibleRegion(candidate, feasibleSpace, sliding))
                {
                    continue;
                }

                if (IsInsideBlockedNfp(candidate, sliding, placed, cache))
                {
                    result.NfpRejectCount++;
                    continue;
                }

                var curve = sliding.Curve.DuplicateCurve();
                var translation = Transform.Translation(candidate.X, candidate.Y, 0);
                curve.Transform(translation);
                if (CollidesWithAny(curve, placed, out var collisionChecks))
                {
                    result.CollisionCheckCount += collisionChecks;
                    continue;
                }

                result.CollisionCheckCount += collisionChecks;
                var bounds = curve.GetBoundingBox(true);
                rotationBest = new PlacedPart
                {
                    Curve = curve,
                    Bounds = bounds,
                    Vertices = GetCurveVertices(curve).ToList(),
                    Origin = candidate,
                    Shape = sliding,
                    BottomLeft = bounds.Min,
                    Transform = sliding.TransformToOrigin * translation,
                    SourceIndex = part.SourceIndex
                };
                break;
            }

            if (rotationBest != null && (best == null || IsBetterPlacement(rotationBest.Bounds, best.Bounds)))
            {
                best = rotationBest;
            }
        }

        return best;
    }

    private List<Point3d> GenerateCandidatePoints(
        List<PlacedPart> placed,
        RotatedPart sliding,
        FeasiblePlacementSpace feasibleSpace,
        PackingResult result)
    {
        var maxX = _sheetLength - sliding.Width;
        var maxY = _sheetWidth - sliding.Height;
        var candidates = new List<Point3d>
        {
            Point3d.Origin,
            new(Math.Max(0, maxX), 0, 0),
            new(0, Math.Max(0, maxY), 0),
            new(Math.Max(0, maxX), Math.Max(0, maxY), 0)
        };

        foreach (var placedPart in placed)
        {
            var bounds = placedPart.Bounds;
            candidates.Add(new Point3d(bounds.Max.X + _spacing, bounds.Min.Y, 0));
            candidates.Add(new Point3d(bounds.Min.X, bounds.Max.Y + _spacing, 0));
            candidates.Add(new Point3d(bounds.Max.X + _spacing, bounds.Max.Y + _spacing, 0));
            candidates.Add(new Point3d(bounds.Max.X + _spacing, 0, 0));
            candidates.Add(new Point3d(0, bounds.Max.Y + _spacing, 0));
            foreach (var vertex in placedPart.Vertices)
            {
                candidates.Add(new Point3d(vertex.X + _spacing, vertex.Y, 0));
                candidates.Add(new Point3d(vertex.X, vertex.Y + _spacing, 0));
                candidates.Add(new Point3d(vertex.X - sliding.Width - _spacing, vertex.Y, 0));
                candidates.Add(new Point3d(vertex.X, vertex.Y - sliding.Height - _spacing, 0));
            }
        }

        foreach (var region in feasibleSpace.Regions)
        {
            foreach (var vertex in GetCurveVertices(region))
            {
                AddNfpCandidate(candidates, vertex);
                result.FeasibleRegionCandidateCount++;
            }
        }

        foreach (var blockedRegion in feasibleSpace.BlockedRegions)
        {
            foreach (var vertex in GetCurveVertices(blockedRegion))
            {
                AddNfpCandidate(candidates, vertex);
                if (_spacing > _tolerance)
                {
                    AddNfpCandidate(candidates, new Point3d(vertex.X + _spacing, vertex.Y, 0));
                    AddNfpCandidate(candidates, new Point3d(vertex.X, vertex.Y + _spacing, 0));
                    AddNfpCandidate(candidates, new Point3d(vertex.X - _spacing, vertex.Y, 0));
                    AddNfpCandidate(candidates, new Point3d(vertex.X, vertex.Y - _spacing, 0));
                }
            }
        }

        var filtered = candidates
            .Select(ClampSmallNegativeToZero)
            .Where(point => IsInsideFeasibleRegion(point, feasibleSpace, sliding))
            .Distinct(new Point3dToleranceComparer(Math.Max(_tolerance, 1e-6)));

        return OrderCandidates(filtered, sliding.Width, sliding.Height)
            .Take(MaxCandidatePointsPerRotation)
            .ToList();
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

    private FeasiblePlacementSpace BuildFeasiblePlacementSpace(
        List<PlacedPart> placed,
        RotatedPart sliding,
        NfpCache cache,
        PackingResult result)
    {
        var feasibleSpace = new FeasiblePlacementSpace();
        var innerFit = GetInnerFitRegionCurve(sliding);
        if (innerFit == null)
        {
            return feasibleSpace;
        }

        feasibleSpace.InnerFitRegion = innerFit;
        AddDiagnosticCurve(result, innerFit.DuplicateCurve());
        foreach (var placedPart in placed)
        {
            var nfp = cache.GetOrCreate(
                placedPart.Shape.GeometryKey,
                placedPart.Shape.Polygon,
                sliding.GeometryKey,
                sliding.Polygon,
                _tolerance,
                _nfpMaxIterations,
                false);

            foreach (var regionCurve in nfp.RegionCurves)
            {
                var curve = regionCurve.DuplicateCurve();
                curve.Transform(Transform.Translation(placedPart.Origin.X, placedPart.Origin.Y, 0));
                if (BoundingBoxesOverlap(innerFit.GetBoundingBox(true), curve.GetBoundingBox(true)))
                {
                    feasibleSpace.BlockedRegions.Add(curve);
                    AddDiagnosticCurve(result, curve.DuplicateCurve());
                }
            }
        }

        if (feasibleSpace.BlockedRegions.Count == 0)
        {
            feasibleSpace.Regions.Add(innerFit.DuplicateCurve());
            feasibleSpace.UsedBooleanExtraction = true;
            result.FeasibleRegionCount += feasibleSpace.Regions.Count;
            return feasibleSpace;
        }

        var blockedUnion = BuildBlockedUnion(feasibleSpace.BlockedRegions);
        try
        {
            var difference = Curve.CreateBooleanDifference(innerFit, blockedUnion, _tolerance);
            var regions = CleanRegions(difference);
            if (regions.Count > 0)
            {
                feasibleSpace.Regions.AddRange(regions);
                feasibleSpace.UsedBooleanExtraction = true;
                result.FeasibleRegionCount += feasibleSpace.Regions.Count;
                foreach (var region in feasibleSpace.Regions)
                {
                    AddDiagnosticCurve(result, region.DuplicateCurve());
                }
                return feasibleSpace;
            }

            if (difference != null)
            {
                feasibleSpace.UsedBooleanExtraction = true;
                return feasibleSpace;
            }
        }
        catch
        {
            result.FeasibleRegionFallbackCount++;
        }

        feasibleSpace.Regions.Add(innerFit.DuplicateCurve());
        result.FeasibleRegionFallbackCount++;
        return feasibleSpace;
    }

    private Curve? GetInnerFitRegionCurve(RotatedPart sliding)
    {
        var maxX = _sheetLength - sliding.Width;
        var maxY = _sheetWidth - sliding.Height;
        if (maxX < -_tolerance || maxY < -_tolerance)
        {
            return null;
        }

        var x = Math.Max(0, maxX);
        var y = Math.Max(0, maxY);
        var polyline = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(x, 0, 0),
            new Point3d(x, y, 0),
            new Point3d(0, y, 0),
            new Point3d(0, 0, 0)
        };
        return polyline.ToNurbsCurve();
    }

    private List<Curve> BuildBlockedUnion(List<Curve> blockedRegions)
    {
        try
        {
            var union = CleanRegions(Curve.CreateBooleanUnion(blockedRegions, _tolerance));
            if (union.Count > 0)
            {
                return union;
            }
        }
        catch
        {
        }

        return blockedRegions
            .Where(curve => curve != null && Math.Abs(Area(curve)) > _tolerance * _tolerance)
            .Select(curve => curve.DuplicateCurve())
            .ToList();
    }

    private List<Curve> CleanRegions(IEnumerable<Curve>? regions)
    {
        var clean = new List<Curve>();
        if (regions == null)
        {
            return clean;
        }

        foreach (var region in regions)
        {
            if (region != null && region.IsClosed && Math.Abs(Area(region)) > _tolerance * _tolerance)
            {
                clean.Add(region.DuplicateCurve());
            }
        }

        return clean;
    }

    private bool IsInsideFeasibleRegion(Point3d candidate, FeasiblePlacementSpace feasibleSpace, RotatedPart sliding)
    {
        if (!IsInsideFitRegion(candidate, sliding) || feasibleSpace.Regions.Count == 0)
        {
            return false;
        }

        if (!feasibleSpace.UsedBooleanExtraction)
        {
            return true;
        }

        foreach (var region in feasibleSpace.Regions)
        {
            var containment = region.Contains(candidate, Plane.WorldXY, _tolerance);
            if (containment == PointContainment.Inside || containment == PointContainment.Coincident)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddNfpCandidate(List<Point3d> candidates, Point3d candidate)
    {
        candidates.Add(new Point3d(candidate.X, candidate.Y, 0));
    }

    private bool IsInsideBlockedNfp(Point3d candidate, RotatedPart sliding, List<PlacedPart> placed, NfpCache cache)
    {
        foreach (var placedPart in placed)
        {
            var nfp = cache.GetOrCreate(
                placedPart.Shape.GeometryKey,
                placedPart.Shape.Polygon,
                sliding.GeometryKey,
                sliding.Polygon,
                _tolerance,
                _nfpMaxIterations,
                false);

            var local = new Point3d(candidate.X - placedPart.Origin.X, candidate.Y - placedPart.Origin.Y, 0);
            foreach (var regionCurve in nfp.RegionCurves)
            {
                if (regionCurve.Contains(local, Plane.WorldXY, _tolerance) == PointContainment.Inside)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsInsideFitRegion(Point3d origin, RotatedPart part)
    {
        return origin.X >= -_tolerance
            && origin.Y >= -_tolerance
            && origin.X + part.Width <= _sheetLength + _tolerance
            && origin.Y + part.Height <= _sheetWidth + _tolerance;
    }

    private bool BoundingBoxesOverlap(BoundingBox a, BoundingBox b)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return false;
        }

        var tolerance = _tolerance;
        return a.Max.X + tolerance >= b.Min.X
            && b.Max.X + tolerance >= a.Min.X
            && a.Max.Y + tolerance >= b.Min.Y
            && b.Max.Y + tolerance >= a.Min.Y;
    }

    private static void AddDiagnosticCurve(PackingResult result, Curve? curve)
    {
        if (curve == null || result.DiagnosticCurves.Count >= MaxDiagnosticCurves)
        {
            return;
        }

        result.DiagnosticCurves.Add(curve);
    }

    private PackingPart? PreparePart(Curve? curve, int sourceIndex)
    {
        var prepared = SimplifyPartCurve(curve);
        if (prepared == null || !prepared.IsClosed || !prepared.IsPlanar(_tolerance) || !prepared.TryGetPlane(out var plane, _tolerance))
        {
            return null;
        }

        if (plane.ZAxis * Vector3d.ZAxis < 0)
        {
            prepared.Reverse();
        }

        MoveToOrigin(prepared, out _);
        var area = Area(prepared);
        if (area <= _tolerance * _tolerance)
        {
            return null;
        }

        return new PackingPart
        {
            SourceIndex = sourceIndex,
            PreparedCurve = prepared,
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
        var polygon = NfpRhino.CurveToPolygon(curve, _tolerance);
        if (polygon.Count < 3)
        {
            return null;
        }

        var bounds = curve.GetBoundingBox(true);
        return new RotatedPart
        {
            Curve = curve,
            Polygon = polygon,
            Width = bounds.Max.X - bounds.Min.X,
            Height = bounds.Max.Y - bounds.Min.Y,
            TransformToOrigin = rotation * originMove,
            GeometryKey = $"{part.SourceIndex}:{angleDeg:R}"
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

    private static void MoveToOrigin(Curve curve, out Transform move)
    {
        var bounds = curve.GetBoundingBox(true);
        move = Transform.Translation(-bounds.Min.X, -bounds.Min.Y, 0);
        curve.Transform(move);
    }

    private List<List<PackingPart>> BuildOptimizationSequences(List<PackingPart> parts)
    {
        var sequences = new List<List<PackingPart>> { SortParts(parts, _sortMode) };
        if (_optimizationMode >= 1)
        {
            AddUniqueSequence(sequences, SortParts(parts, PackingSortMode.AreaDescending));
            AddUniqueSequence(sequences, SortParts(parts, PackingSortMode.MaxDimensionDescending));
            AddUniqueSequence(sequences, SortParts(parts, PackingSortMode.WidthDescending));
            AddUniqueSequence(sequences, SortParts(parts, PackingSortMode.HeightDescending));
            AddUniqueSequence(sequences, SortParts(parts, PackingSortMode.UserOrder));
        }

        if (_optimizationMode >= 2)
        {
            var count = sequences.Count;
            for (var i = 0; i < count; i++)
            {
                var reversed = sequences[i].ToList();
                reversed.Reverse();
                AddUniqueSequence(sequences, reversed);
            }
        }

        if (_optimizationMode >= 3 && _optimizationIterations > 0)
        {
            var random = new Random(_seed == 0 ? 17 : _seed);
            var source = sequences[0];
            for (var i = 0; i < _optimizationIterations; i++)
            {
                var candidate = source.ToList();
                var swaps = Math.Max(1, Math.Min(candidate.Count / 3, 4));
                for (var j = 0; j < swaps && candidate.Count > 1; j++)
                {
                    var index = random.Next(0, candidate.Count - 1);
                    (candidate[index], candidate[index + 1]) = (candidate[index + 1], candidate[index]);
                }
                AddUniqueSequence(sequences, candidate);
            }
        }

        return sequences.Take(Math.Max(1, 1 + _optimizationIterations + 10)).ToList();
    }

    private static void AddUniqueSequence(List<List<PackingPart>> sequences, List<PackingPart> candidate)
    {
        var key = string.Join(",", candidate.Select(part => part.SourceIndex));
        foreach (var sequence in sequences)
        {
            if (string.Join(",", sequence.Select(part => part.SourceIndex)) == key)
            {
                return;
            }
        }

        sequences.Add(candidate);
    }

    private static List<PackingPart> SortParts(List<PackingPart> parts, PackingSortMode sortMode)
    {
        return sortMode switch
        {
            PackingSortMode.UserOrder => parts.OrderBy(part => part.SourceIndex).ToList(),
            PackingSortMode.AreaDescending => parts.OrderByDescending(part => part.Area).ToList(),
            PackingSortMode.WidthDescending => parts.OrderByDescending(part => part.Width).ToList(),
            PackingSortMode.HeightDescending => parts.OrderByDescending(part => part.Height).ToList(),
            PackingSortMode.MaxDimensionDescending => parts.OrderByDescending(part => Math.Max(part.Width, part.Height)).ToList(),
            _ => parts.OrderByDescending(part => part.Area).ToList()
        };
    }

    private bool IsBetterResult(PackingResult candidate, PackingResult currentBest)
    {
        return candidate.PackedCurves.Count > currentBest.PackedCurves.Count
            || (candidate.PackedCurves.Count == currentBest.PackedCurves.Count
                && (candidate.UsedLength < currentBest.UsedLength - _tolerance
                    || (Math.Abs(candidate.UsedLength - currentBest.UsedLength) <= _tolerance
                        && candidate.Utilization > currentBest.Utilization)));
    }

    private Point3d ClampSmallNegativeToZero(Point3d point)
    {
        return new Point3d(
            Math.Abs(point.X) <= _tolerance ? 0 : point.X,
            Math.Abs(point.Y) <= _tolerance ? 0 : point.Y,
            0);
    }

    private bool CollidesWithAny(Curve testCurve, List<PlacedPart> placed, out int collisionChecks)
    {
        collisionChecks = 0;
        var bounds = testCurve.GetBoundingBox(true);
        foreach (var placedPart in placed)
        {
            collisionChecks++;
            if (CurvesOverlapOrTooClose(testCurve, bounds, placedPart))
            {
                return true;
            }
        }

        return false;
    }

    private bool CurvesOverlapOrTooClose(Curve a, BoundingBox aBox, PlacedPart placed)
    {
        return BoundingBoxesMightInteract(aBox, placed.Bounds)
            && (HasInteriorOverlap(a, placed.Curve, placed.Vertices)
                || (_spacing > _tolerance && ApproximateMinimumDistance(a, placed.Curve) < _spacing - _tolerance));
    }

    private bool BoundingBoxesMightInteract(BoundingBox a, BoundingBox b)
    {
        var pad = _spacing + _tolerance;
        return a.Max.X + pad >= b.Min.X
            && b.Max.X + pad >= a.Min.X
            && a.Max.Y + pad >= b.Min.Y
            && b.Max.Y + pad >= a.Min.Y;
    }

    private bool HasInteriorOverlap(Curve a, Curve b, IReadOnlyList<Point3d> bVertices)
    {
        var intersections = Intersection.CurveCurve(a, b, _tolerance, _tolerance);
        if (intersections != null && (intersections.Count > 1 || intersections.Count == 1 && _spacing > _tolerance))
        {
            return true;
        }

        foreach (var vertex in GetCurveVertices(a))
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
        foreach (var point in SampleCurve(a))
        {
            if (b.ClosestPoint(point, out var t))
            {
                minimum = Math.Min(minimum, point.DistanceTo(b.PointAt(t)));
            }
        }

        foreach (var point in SampleCurve(b))
        {
            if (a.ClosestPoint(point, out var t))
            {
                minimum = Math.Min(minimum, point.DistanceTo(a.PointAt(t)));
            }
        }

        return minimum;
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

    private static Point3d GetInteriorSamplePoint(Curve curve)
    {
        var area = AreaMassProperties.Compute(curve);
        return area?.Centroid ?? curve.GetBoundingBox(true).Center;
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

    public Curve GetSheetPreviewCurve()
    {
        var polyline = new Polyline
        {
            new Point3d(0, 0, 0),
            new Point3d(_sheetLength, 0, 0),
            new Point3d(_sheetLength, _sheetWidth, 0),
            new Point3d(0, _sheetWidth, 0),
            new Point3d(0, 0, 0)
        };
        return polyline.ToNurbsCurve();
    }

    private static double GetUsedLength(IEnumerable<Curve> placed)
    {
        return placed.Aggregate(0.0, (length, curve) => Math.Max(length, curve.GetBoundingBox(true).Max.X));
    }

    private double ComputeUtilization(IEnumerable<Curve> placed)
    {
        var curves = placed.ToList();
        if (curves.Count == 0)
        {
            return 0;
        }

        var area = curves.Sum(Area);
        var usedLength = GetUsedLength(curves);
        return usedLength <= _tolerance || _sheetWidth <= _tolerance ? 0 : area / (usedLength * _sheetWidth);
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

    private string BuildReport(PackingResult result)
    {
        return $"NFP Pack Placed: {result.PackedCurves.Count}, Unplaced: {result.UnplacedCurves.Count}, Invalid: {result.InvalidCount}, Used Length: {result.UsedLength:F3}, Utilization: {result.Utilization:P2}, Candidates: {result.CandidateCount}, Feasible Regions: {result.FeasibleRegionCount}, Feasible Candidates: {result.FeasibleRegionCandidateCount}, Feasible Fallbacks: {result.FeasibleRegionFallbackCount}, NFP Rejects: {result.NfpRejectCount}, Collision Checks: {result.CollisionCheckCount}, NFP Cache: {result.NfpCacheHits} hit(s), {result.NfpCacheMisses} miss(es), Optimization Runs: {result.OptimizationRuns}, Runtime: {result.RuntimeMilliseconds} ms";
    }

    private sealed class PackingPart
    {
        public int SourceIndex;
        public Curve PreparedCurve = null!;
        public double Area;
        public double Width;
        public double Height;
    }

    private sealed class RotatedPart
    {
        public Curve Curve = null!;
        public List<Point2d> Polygon = new();
        public double Width;
        public double Height;
        public Transform TransformToOrigin;
        public string GeometryKey = string.Empty;
    }

    private sealed class PlacedPart
    {
        public Curve Curve = null!;
        public BoundingBox Bounds;
        public List<Point3d> Vertices = new();
        public Transform Transform;
        public Point3d Origin;
        public RotatedPart Shape = null!;
        public Point3d BottomLeft;
        public int SourceIndex;
    }

    private sealed class FeasiblePlacementSpace
    {
        public Curve? InnerFitRegion;
        public List<Curve> Regions = new();
        public List<Curve> BlockedRegions = new();
        public bool UsedBooleanExtraction;
    }
}
