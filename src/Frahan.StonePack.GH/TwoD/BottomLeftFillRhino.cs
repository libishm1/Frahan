using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Frahan.GH.TwoD;

public class BottomLeftFillRhino
{
    private const int MaxPreparedPolylinePoints = 128;
    private const int HardMaxPolylinePoints = 400;
    private const int MaxCandidatePointsPerRotation = 300;
    private readonly double _sheetWidth;
    private readonly double _sheetLength;
    private readonly double _spacing;
    private readonly double _tolerance;
    private readonly List<double> _rotationsDeg;
    private readonly PackingSortMode _sortMode;
    private readonly PackingCornerMode _cornerMode;
    private readonly bool _simplifyCurves;
    private readonly double _simplifyTolerance;
    private readonly int _seed;

    public BottomLeftFillRhino(
        double sheetWidth,
        double sheetLength,
        double spacing,
        IEnumerable<double>? rotationsDeg,
        double tolerance,
        PackingSortMode sortMode,
        bool simplifyCurves,
        double simplifyTolerance,
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
        var result = new PackingResult { SheetPreview = GetSheetPreviewCurve() };
        if (inputCurves == null)
        {
            result.Report = "No input curves.";
            result.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
            return result;
        }

        var input = inputCurves.ToList();
        result.InputCount = input.Count;
        var prepared = input
            .Select((curve, index) => PreparePart(curve, index))
            .Where(part => part != null)
            .Cast<PackingPart>()
            .ToList();

        result.PreparedCount = prepared.Count;
        result.InvalidCount = result.InputCount - result.PreparedCount;
        if (prepared.Count == 0)
        {
            result.Report = "No valid closed planar curves found.";
            result.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
            return result;
        }

        var placed = new List<PlacedPart>();
        foreach (var part in SortParts(prepared, _sortMode))
        {
            var best = FindBestPlacement(part, placed);
            result.CandidateCount += part.CandidatesTested;
            result.CollisionCheckCount += part.CollisionChecks;
            if (best == null)
            {
                result.UnplacedCurves.Add(part.PreparedCurve.DuplicateCurve());
                continue;
            }

            placed.Add(best);
            result.PackedCurves.Add(best.Curve);
            result.Transforms.Add(best.Transform);
            result.SourceIndices.Add(best.SourceIndex);
        }

        result.UsedLength = GetUsedLength(result.PackedCurves);
        result.Utilization = ComputeUtilization(result.PackedCurves);
        result.RuntimeMilliseconds = stopwatch.ElapsedMilliseconds;
        result.Report = $"Placed: {result.PackedCurves.Count}, Unplaced: {result.UnplacedCurves.Count}, Invalid: {result.InvalidCount}, Used Length: {result.UsedLength:F3}, Utilization: {result.Utilization:P2}, Candidates: {result.CandidateCount}, Runtime: {result.RuntimeMilliseconds} ms";
        return result;
    }

    protected virtual PlacedPart? FindBestPlacement(PackingPart part, List<PlacedPart> placed)
    {
        PlacedPart? best = null;
        foreach (var angle in _rotationsDeg)
        {
            var rotation = Transform.Rotation(RhinoMath.ToRadians(angle), Vector3d.ZAxis, Point3d.Origin);
            var curve = part.PreparedCurve.DuplicateCurve();
            curve.Transform(rotation);
            MoveToOrigin(curve, out var originMove);
            var partBox = curve.GetBoundingBox(true);

            PlacedPart? rotationBest = null;
            foreach (var candidate in GenerateCandidatePoints(placed, partBox))
            {
                part.CandidatesTested++;
                var candidateCurve = curve.DuplicateCurve();
                var translation = Transform.Translation(candidate.X, candidate.Y, 0);
                candidateCurve.Transform(translation);
                if (!IsInsideSheet(candidateCurve))
                {
                    continue;
                }

                if (CollidesWithAny(candidateCurve, placed, out var collisionChecks))
                {
                    part.CollisionChecks += collisionChecks;
                    continue;
                }

                part.CollisionChecks += collisionChecks;
                var bounds = candidateCurve.GetBoundingBox(true);
                rotationBest = new PlacedPart
                {
                    Curve = candidateCurve,
                    Bounds = bounds,
                    Vertices = GetCurveVertices(candidateCurve).ToList(),
                    BottomLeft = bounds.Min,
                    Transform = rotation * originMove * translation,
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

    protected PackingPart? PreparePart(Curve? curve, int sourceIndex)
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

    protected Curve? SimplifyPartCurve(Curve? curve)
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

    protected Curve? ToBoundedPolylineCurve(Curve curve)
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

    protected int TryGetPolylinePointCount(Curve curve)
    {
        return curve.TryGetPolyline(out var polyline) ? polyline.Count : 0;
    }

    protected Polyline ReducePolyline(Polyline polyline, int maxPoints)
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

    protected void MoveToOrigin(Curve curve, out Transform move)
    {
        var bounds = curve.GetBoundingBox(true);
        move = Transform.Translation(-bounds.Min.X, -bounds.Min.Y, 0);
        curve.Transform(move);
    }

    protected List<PackingPart> SortParts(List<PackingPart> parts, PackingSortMode sortMode)
    {
        var random = _seed == 0 ? null : new Random(_seed);
        return sortMode switch
        {
            PackingSortMode.UserOrder => parts.OrderBy(part => part.SourceIndex).ToList(),
            PackingSortMode.AreaDescending => parts.OrderByDescending(part => part.Area).ThenBy(_ => random?.Next() ?? 0).ToList(),
            PackingSortMode.WidthDescending => parts.OrderByDescending(part => part.Width).ThenBy(_ => random?.Next() ?? 0).ToList(),
            PackingSortMode.HeightDescending => parts.OrderByDescending(part => part.Height).ThenBy(_ => random?.Next() ?? 0).ToList(),
            PackingSortMode.MaxDimensionDescending => parts.OrderByDescending(part => Math.Max(part.Width, part.Height)).ThenBy(_ => random?.Next() ?? 0).ToList(),
            _ => parts.OrderByDescending(part => part.Area).ThenBy(_ => random?.Next() ?? 0).ToList()
        };
    }

    protected List<Point3d> GenerateCandidatePoints(List<PlacedPart> placed, BoundingBox partBox)
    {
        var partWidth = partBox.Max.X - partBox.Min.X;
        var partHeight = partBox.Max.Y - partBox.Min.Y;
        var maxX = _sheetLength - partWidth;
        var maxY = _sheetWidth - partHeight;
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
                candidates.Add(new Point3d(vertex.X - partWidth - _spacing, vertex.Y, 0));
                candidates.Add(new Point3d(vertex.X, vertex.Y - partHeight - _spacing, 0));
            }
        }

        var filtered = candidates
            .Select(ClampSmallNegativeToZero)
            .Where(pt => pt.X >= -_tolerance && pt.Y >= -_tolerance)
            .Where(pt => pt.X + partWidth <= _sheetLength + _tolerance)
            .Where(pt => pt.Y + partHeight <= _sheetWidth + _tolerance)
            .Distinct(new Point3dToleranceComparer(Math.Max(_tolerance, 1e-6)));

        return OrderCandidates(filtered, partWidth, partHeight)
            .Take(MaxCandidatePointsPerRotation)
            .ToList();
    }

    protected IEnumerable<Point3d> OrderCandidates(IEnumerable<Point3d> candidates, double partWidth, double partHeight)
    {
        return _cornerMode switch
        {
            PackingCornerMode.BottomRight => candidates.OrderBy(pt => pt.Y).ThenByDescending(pt => pt.X + partWidth),
            PackingCornerMode.TopLeft => candidates.OrderByDescending(pt => pt.Y + partHeight).ThenBy(pt => pt.X),
            PackingCornerMode.TopRight => candidates.OrderByDescending(pt => pt.Y + partHeight).ThenByDescending(pt => pt.X + partWidth),
            _ => candidates.OrderBy(pt => pt.Y).ThenBy(pt => pt.X)
        };
    }

    protected Point3d ClampSmallNegativeToZero(Point3d point)
    {
        return new Point3d(
            Math.Abs(point.X) <= _tolerance ? 0 : point.X,
            Math.Abs(point.Y) <= _tolerance ? 0 : point.Y,
            0);
    }

    protected bool IsInsideSheet(Curve curve)
    {
        var bounds = curve.GetBoundingBox(true);
        return bounds.Min.X >= -_tolerance
            && bounds.Min.Y >= -_tolerance
            && bounds.Max.X <= _sheetLength + _tolerance
            && bounds.Max.Y <= _sheetWidth + _tolerance;
    }

    protected bool CollidesWithAny(Curve testCurve, List<PlacedPart> placed, out int collisionChecks)
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

    protected bool CurvesOverlapOrTooClose(Curve a, BoundingBox aBox, PlacedPart placed)
    {
        return BoundingBoxesMightInteract(aBox, placed.Bounds)
            && (HasInteriorOverlap(a, placed.Curve, placed.Vertices)
                || (_spacing > _tolerance && ApproximateMinimumDistance(a, placed.Curve) < _spacing - _tolerance));
    }

    protected bool BoundingBoxesMightInteract(BoundingBox a, BoundingBox b)
    {
        var pad = _spacing + _tolerance;
        return a.Max.X + pad >= b.Min.X
            && b.Max.X + pad >= a.Min.X
            && a.Max.Y + pad >= b.Min.Y
            && b.Max.Y + pad >= a.Min.Y;
    }

    protected bool HasInteriorOverlap(Curve a, Curve b, IReadOnlyList<Point3d> bVertices)
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

    protected double ApproximateMinimumDistance(Curve a, Curve b)
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

    protected IEnumerable<Point3d> SampleCurve(Curve curve)
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

    protected Point3d GetInteriorSamplePoint(Curve curve)
    {
        var area = AreaMassProperties.Compute(curve);
        return area?.Centroid ?? curve.GetBoundingBox(true).Center;
    }

    protected IEnumerable<Point3d> GetCurveVertices(Curve curve)
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

    protected bool IsBetterPlacement(BoundingBox candidate, BoundingBox currentBest)
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

    protected double GetUsedLength(IEnumerable<Curve> placed)
    {
        return placed.Aggregate(0.0, (length, curve) => Math.Max(length, curve.GetBoundingBox(true).Max.X));
    }

    protected double ComputeUtilization(IEnumerable<Curve> placed)
    {
        var curves = placed.ToList();
        if (curves.Count == 0)
        {
            return 0;
        }

        var usedLength = GetUsedLength(curves);
        return usedLength <= _tolerance || _sheetWidth <= _tolerance
            ? 0
            : curves.Sum(Area) / (usedLength * _sheetWidth);
    }

    protected static double Area(Curve curve)
    {
        var area = AreaMassProperties.Compute(curve);
        return area == null ? 0 : Math.Abs(area.Area);
    }

    protected static double Width(Curve curve)
    {
        var bounds = curve.GetBoundingBox(true);
        return bounds.Max.X - bounds.Min.X;
    }

    protected static double Height(Curve curve)
    {
        var bounds = curve.GetBoundingBox(true);
        return bounds.Max.Y - bounds.Min.Y;
    }

    protected sealed class PackingPart
    {
        public int SourceIndex;
        public Curve PreparedCurve = null!;
        public double Area;
        public double Width;
        public double Height;
        public int CandidatesTested;
        public int CollisionChecks;
    }

    protected sealed class PlacedPart
    {
        public Curve Curve = null!;
        public BoundingBox Bounds;
        public List<Point3d> Vertices = new();
        public Transform Transform;
        public Point3d BottomLeft;
        public int SourceIndex;
    }
}
