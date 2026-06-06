using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

public sealed class NfpRhino
{
    private const int MaxPolygonPoints = 128;
    private const int DefaultMaxConcaveTrianglePairs = 2500;
    private readonly double _tolerance;

    public NfpRhino(IList<Point2d> stationary, IList<Point2d> sliding, double tolerance, int maxIterations, bool rectangleShortcut)
    {
        _tolerance = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        Nfp = new List<Point2d>();
        RegionCurves = new List<Curve>();
        ErrorCode = 0;
        ErrorMessage = "Not computed.";

        var stationaryPolygon = CleanPolygon(stationary);
        var slidingPolygon = CleanPolygon(sliding);
        if (stationaryPolygon.Count < 3 || slidingPolygon.Count < 3)
        {
            ErrorMessage = "Both polygons need at least three vertices.";
            return;
        }

        if (Math.Abs(SignedArea(stationaryPolygon)) <= _tolerance * _tolerance
            || Math.Abs(SignedArea(slidingPolygon)) <= _tolerance * _tolerance)
        {
            ErrorMessage = "Degenerate polygon area.";
            return;
        }

        EnsureCounterClockwise(stationaryPolygon);
        EnsureCounterClockwise(slidingPolygon);
        var convex = IsConvex(stationaryPolygon) && IsConvex(slidingPolygon);
        var usedConcaveUnion = false;

        if (convex)
        {
            Nfp = BuildMinkowskiDifferenceHull(stationaryPolygon, slidingPolygon);
            var curve = PolygonToCurve(Nfp);
            if (curve != null)
            {
                RegionCurves.Add(curve);
            }
        }
        else
        {
            RegionCurves = BuildConcaveMinkowskiDifferenceRegions(
                stationaryPolygon,
                slidingPolygon,
                Math.Max(maxIterations, DefaultMaxConcaveTrianglePairs));

            if (RegionCurves.Count > 0)
            {
                usedConcaveUnion = true;
                Nfp = CurveToPolygon(GetLargestRegion(RegionCurves), _tolerance);
            }
            else
            {
                Nfp = BuildMinkowskiDifferenceHull(stationaryPolygon, slidingPolygon);
            }
        }

        if (Nfp.Count < 3)
        {
            ErrorMessage = "No-fit polygon construction failed.";
            return;
        }

        if (RegionCurves.Count == 0)
        {
            var curve = PolygonToCurve(Nfp);
            if (curve != null)
            {
                RegionCurves.Add(curve);
            }
        }

        ErrorCode = convex ? 1 : usedConcaveUnion ? 3 : 2;
        ErrorMessage = convex
            ? "OK: convex no-fit polygon."
            : ErrorCode == 3
                ? "OK: concave no-fit region from triangulated Minkowski union."
                : "Approximation: concave input collapsed to convex-hull no-fit region.";
    }

    public List<Point2d> Nfp { get; }
    public List<Curve> RegionCurves { get; private set; }
    public int ErrorCode { get; }
    public string ErrorMessage { get; }

    public Curve? ToCurve(bool close)
    {
        if (Nfp.Count == 0)
        {
            return null;
        }

        var polyline = new Polyline();
        foreach (var point in Nfp)
        {
            polyline.Add(new Point3d(point.X, point.Y, 0));
        }

        if (close && polyline.Count > 0)
        {
            polyline.Add(polyline[0]);
        }

        return polyline.ToNurbsCurve();
    }

    public List<Curve> ToRegionCurves()
    {
        return RegionCurves.Select(curve => curve.DuplicateCurve()).ToList();
    }

    public static List<Point2d> CurveToPolygon(Curve? curve, double tolerance)
    {
        var polygon = new List<Point2d>();
        if (curve == null || !curve.IsClosed)
        {
            return polygon;
        }

        var duplicate = curve.DuplicateCurve();
        var effectiveTolerance = Math.Max(tolerance, RhinoMath.ZeroTolerance);
        if (!duplicate.TryGetPolyline(out var polyline))
        {
            var polylineCurve = duplicate.ToPolyline(effectiveTolerance, effectiveTolerance, 0, 0);
            if (polylineCurve == null || !polylineCurve.TryGetPolyline(out polyline))
            {
                return polygon;
            }
        }

        for (var i = 0; i < polyline.Count; i++)
        {
            polygon.Add(new Point2d(polyline[i].X, polyline[i].Y));
        }

        polygon = ReducePolygon(CleanPolygon(polygon), MaxPolygonPoints);
        EnsureCounterClockwise(polygon);
        return polygon;
    }

    private List<Point2d> BuildMinkowskiDifferenceHull(IList<Point2d> stationary, IList<Point2d> sliding)
    {
        var points = new List<Point2d>();
        foreach (var a in stationary)
        {
            foreach (var b in sliding)
            {
                points.Add(new Point2d(a.X - b.X, a.Y - b.Y));
            }
        }

        return ConvexHull(points);
    }

    private List<Curve> BuildConcaveMinkowskiDifferenceRegions(
        IList<Point2d> stationary,
        IList<Point2d> sliding,
        int maxTrianglePairs)
    {
        var stationaryTriangles = Triangulate(stationary);
        var slidingTriangles = Triangulate(sliding);
        if (stationaryTriangles.Count == 0 || slidingTriangles.Count == 0)
        {
            return new List<Curve>();
        }

        if (stationaryTriangles.Count * slidingTriangles.Count > maxTrianglePairs)
        {
            return new List<Curve>();
        }

        var pieces = new List<Curve>();
        foreach (var stationaryTriangle in stationaryTriangles)
        {
            foreach (var slidingTriangle in slidingTriangles)
            {
                var curve = PolygonToCurve(BuildMinkowskiDifferenceHull(stationaryTriangle, slidingTriangle));
                if (curve != null && Math.Abs(Area(curve)) > _tolerance * _tolerance)
                {
                    pieces.Add(curve);
                }
            }
        }

        if (pieces.Count == 0)
        {
            return new List<Curve>();
        }

        try
        {
            var union = Curve.CreateBooleanUnion(pieces, _tolerance);
            if (union != null && union.Length > 0)
            {
                return union
                    .Where(curve => curve != null && Math.Abs(Area(curve)) > _tolerance * _tolerance)
                    .Select(curve => curve.DuplicateCurve())
                    .ToList();
            }
        }
        catch
        {
            return new List<Curve>();
        }

        return new List<Curve>();
    }

    private static Curve? PolygonToCurve(IList<Point2d>? polygon)
    {
        if (polygon == null || polygon.Count < 3)
        {
            return null;
        }

        var polyline = new Polyline();
        foreach (var point in polygon)
        {
            polyline.Add(new Point3d(point.X, point.Y, 0));
        }

        polyline.Add(polyline[0]);
        return polyline.ToNurbsCurve();
    }

    private static Curve? GetLargestRegion(IList<Curve> regions)
    {
        Curve? largest = null;
        var largestArea = double.MinValue;
        foreach (var region in regions)
        {
            var area = Math.Abs(Area(region));
            if (area > largestArea)
            {
                largest = region;
                largestArea = area;
            }
        }

        return largest;
    }

    private static double Area(Curve curve)
    {
        var area = AreaMassProperties.Compute(curve);
        return area == null ? 0 : area.Area;
    }

    private static List<List<Point2d>> Triangulate(IList<Point2d> polygon)
    {
        var triangles = new List<List<Point2d>>();
        var clean = RemoveCollinear(CleanPolygon(polygon));
        if (clean.Count < 3)
        {
            return triangles;
        }

        EnsureCounterClockwise(clean);
        if (clean.Count == 3)
        {
            triangles.Add(new List<Point2d>(clean));
            return triangles;
        }

        var active = Enumerable.Range(0, clean.Count).ToList();
        var guard = clean.Count * clean.Count;
        while (active.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var i = 0; i < active.Count; i++)
            {
                var previousIndex = active[(i - 1 + active.Count) % active.Count];
                var currentIndex = active[i];
                var nextIndex = active[(i + 1) % active.Count];
                var previous = clean[previousIndex];
                var current = clean[currentIndex];
                var next = clean[nextIndex];

                if (Cross(current - previous, next - current) > RhinoMath.ZeroTolerance
                    && !ContainsAnyPoint(clean, active, previousIndex, currentIndex, nextIndex))
                {
                    triangles.Add(new List<Point2d> { previous, current, next });
                    active.RemoveAt(i);
                    clipped = true;
                    break;
                }
            }

            if (!clipped)
            {
                break;
            }
        }

        if (active.Count == 3)
        {
            triangles.Add(new List<Point2d>
            {
                clean[active[0]],
                clean[active[1]],
                clean[active[2]]
            });
        }

        return triangles.Count == 0 ? FanTriangulate(clean) : triangles;
    }

    private static List<List<Point2d>> FanTriangulate(IList<Point2d> polygon)
    {
        var triangles = new List<List<Point2d>>();
        for (var i = 1; i < polygon.Count - 1; i++)
        {
            triangles.Add(new List<Point2d> { polygon[0], polygon[i], polygon[i + 1] });
        }

        return triangles;
    }

    private static bool ContainsAnyPoint(
        IList<Point2d> polygon,
        IList<int> activeIndices,
        int previousIndex,
        int currentIndex,
        int nextIndex)
    {
        var a = polygon[previousIndex];
        var b = polygon[currentIndex];
        var c = polygon[nextIndex];
        foreach (var activeIndex in activeIndices)
        {
            if (activeIndex != previousIndex
                && activeIndex != currentIndex
                && activeIndex != nextIndex
                && IsPointInTriangle(polygon[activeIndex], a, b, c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInTriangle(Point2d point, Point2d a, Point2d b, Point2d c)
    {
        var ab = Cross(b - a, point - a);
        var bc = Cross(c - b, point - b);
        var ca = Cross(a - c, point - c);
        return ab >= -RhinoMath.ZeroTolerance
            && bc >= -RhinoMath.ZeroTolerance
            && ca >= -RhinoMath.ZeroTolerance;
    }

    private static List<Point2d> CleanPolygon(IList<Point2d>? polygon)
    {
        var clean = new List<Point2d>();
        if (polygon == null)
        {
            return clean;
        }

        foreach (var point in polygon)
        {
            if (clean.Count == 0 || Distance(clean[clean.Count - 1], point) > RhinoMath.ZeroTolerance)
            {
                clean.Add(point);
            }
        }

        if (clean.Count > 2 && Distance(clean[0], clean[clean.Count - 1]) <= RhinoMath.ZeroTolerance)
        {
            clean.RemoveAt(clean.Count - 1);
        }

        return clean;
    }

    private static List<Point2d> ReducePolygon(IList<Point2d> polygon, int maxPoints)
    {
        var reduced = RemoveCollinear(polygon);
        if (reduced.Count <= maxPoints)
        {
            return reduced;
        }

        var sampled = new List<Point2d>();
        var step = (double)reduced.Count / maxPoints;
        for (var i = 0; i < maxPoints; i++)
        {
            sampled.Add(reduced[Math.Min(reduced.Count - 1, (int)Math.Floor(i * step))]);
        }

        return RemoveCollinear(sampled);
    }

    private static List<Point2d> RemoveCollinear(IList<Point2d>? polygon)
    {
        var result = new List<Point2d>();
        if (polygon == null || polygon.Count < 3)
        {
            return polygon?.ToList() ?? result;
        }

        for (var i = 0; i < polygon.Count; i++)
        {
            var previous = polygon[(i - 1 + polygon.Count) % polygon.Count];
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            var a = current - previous;
            var b = next - current;
            if (a.Length <= RhinoMath.ZeroTolerance || b.Length <= RhinoMath.ZeroTolerance)
            {
                continue;
            }

            a.Unitize();
            b.Unitize();
            if (Math.Abs(Cross(a, b)) > 1e-8 || Dot(a, b) <= 0)
            {
                result.Add(current);
            }
        }

        return result;
    }

    private static List<Point2d> ConvexHull(IList<Point2d> points)
    {
        var sorted = points
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .Aggregate(new List<Point2d>(), (list, point) =>
            {
                if (list.Count == 0 || Distance(list[list.Count - 1], point) > RhinoMath.ZeroTolerance)
                {
                    list.Add(point);
                }
                return list;
            });

        if (sorted.Count <= 1)
        {
            return sorted;
        }

        var lower = new List<Point2d>();
        foreach (var point in sorted)
        {
            while (lower.Count >= 2
                && Cross(lower[lower.Count - 1] - lower[lower.Count - 2], point - lower[lower.Count - 1]) <= RhinoMath.ZeroTolerance)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(point);
        }

        var upper = new List<Point2d>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var point = sorted[i];
            while (upper.Count >= 2
                && Cross(upper[upper.Count - 1] - upper[upper.Count - 2], point - upper[upper.Count - 1]) <= RhinoMath.ZeroTolerance)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static bool IsConvex(IList<Point2d> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

        var sign = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var c = polygon[(i + 2) % polygon.Count];
            var cross = Cross(b - a, c - b);
            if (Math.Abs(cross) <= RhinoMath.ZeroTolerance)
            {
                continue;
            }

            if (Math.Abs(sign) <= RhinoMath.ZeroTolerance)
            {
                sign = Math.Sign(cross);
            }
            else if (Math.Sign(cross) != Math.Sign(sign))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureCounterClockwise(List<Point2d> polygon)
    {
        if (SignedArea(polygon) < 0)
        {
            polygon.Reverse();
        }
    }

    private static double SignedArea(IList<Point2d> polygon)
    {
        if (polygon.Count < 3)
        {
            return 0;
        }

        var area = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5;
    }

    private static double Cross(Vector2d a, Vector2d b) => a.X * b.Y - a.Y * b.X;
    private static double Dot(Vector2d a, Vector2d b) => a.X * b.X + a.Y * b.Y;

    private static double Distance(Point2d a, Point2d b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
