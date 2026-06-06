#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core;
using Rhino.Geometry;

namespace Frahan.Surface;

/// <summary>
/// Sliding-window builder that turns a Rhino <see cref="Curve"/> into a
/// populated <see cref="BoundaryRailIndex{BoundaryIntervalInfo}"/>. Sliding
/// window length and step are configurable; bucket sizes for the EdgeKey
/// fields are configurable.
///
/// Implements Spec 5 section 5.6 ("Sliding Window Construction"). The window
/// walks the boundary by `stepLength`, samples a `windowLength` interval at
/// each step, computes descriptors (average tangent, inward normal,
/// curvature score, straightness), buckets them into an EdgeKey, and adds
/// the interval to the index.
/// </summary>
public sealed class BoundaryRailBuilder
{
    private readonly double _windowLength;
    private readonly double _stepLength;
    private readonly double _tolerance;
    private readonly double _lengthBucketSize;
    private readonly double _angleBucketSizeDegrees;
    private readonly double _curvatureBucketSize;

    public BoundaryRailBuilder(
        double windowLength,
        double stepLength,
        double tolerance = 1e-3,
        double lengthBucketSize = 1.0,
        double angleBucketSizeDegrees = 5.0,
        double curvatureBucketSize = 0.01)
    {
        if (windowLength <= 0.0) throw new ArgumentOutOfRangeException(nameof(windowLength));
        if (stepLength <= 0.0) throw new ArgumentOutOfRangeException(nameof(stepLength));
        if (tolerance <= 0.0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (lengthBucketSize <= 0.0) throw new ArgumentOutOfRangeException(nameof(lengthBucketSize));
        if (angleBucketSizeDegrees <= 0.0) throw new ArgumentOutOfRangeException(nameof(angleBucketSizeDegrees));
        if (curvatureBucketSize <= 0.0) throw new ArgumentOutOfRangeException(nameof(curvatureBucketSize));

        _windowLength = windowLength;
        _stepLength = stepLength;
        _tolerance = tolerance;
        _lengthBucketSize = lengthBucketSize;
        _angleBucketSizeDegrees = angleBucketSizeDegrees;
        _curvatureBucketSize = curvatureBucketSize;
    }

    /// <summary>
    /// Walk one boundary curve and add every windowed interval to the supplied index.
    /// </summary>
    /// <param name="boundary">The Rhino curve to walk.</param>
    /// <param name="isOuterBoundary">true for outer sheet outline, false for hole.</param>
    /// <param name="zoneBucket">Caller-supplied zone bucket (groups distinct sheets, regions, layers).</param>
    /// <param name="index">Target index to populate (must be non-null).</param>
    public void AddCurve(
        Curve boundary,
        bool isOuterBoundary,
        int zoneBucket,
        BoundaryRailIndex<BoundaryIntervalInfo> index)
    {
        if (boundary == null) throw new ArgumentNullException(nameof(boundary));
        if (index == null) throw new ArgumentNullException(nameof(index));

        double totalLength = boundary.GetLength();
        if (totalLength <= _tolerance) return;

        int sampleCount = Math.Max(1, (int)Math.Floor(totalLength / _stepLength));
        for (int i = 0; i < sampleCount; i++)
        {
            double startLen = i * _stepLength;
            double endLen = Math.Min(startLen + _windowLength, totalLength);

            // Skip terminal short windows. A window shorter than 75 % of
            // windowLength is usually a partial wrap-around at the curve end
            // and would distort descriptors.
            if (endLen - startLen < _windowLength * 0.75)
                continue;

            if (!boundary.LengthParameter(startLen, out double t0)) continue;
            if (!boundary.LengthParameter(endLen, out double t1)) continue;

            var interval = BuildInterval(boundary, t0, t1, endLen - startLen, isOuterBoundary);
            var key = BucketInterval(interval, zoneBucket);
            index.Add(key, interval);
        }
    }

    /// <summary>
    /// Quantise an interval into an EdgeKey using this builder's bucket sizes.
    /// Public so tests can verify deterministic bucketing.
    /// </summary>
    public EdgeKey BucketInterval(BoundaryIntervalInfo interval, int zoneBucket)
    {
        if (interval == null) throw new ArgumentNullException(nameof(interval));

        int lengthBucket = (int)Math.Floor(interval.ApproxLength / _lengthBucketSize);

        // Angle bucket: angle of the average tangent in the XY plane,
        // wrapped to [0, 360). Bucketed by angleBucketSizeDegrees.
        double angleDeg = AngleDegrees(interval.AverageTangent);
        // Wrap to [0, 360):
        if (angleDeg < 0) angleDeg += 360.0;
        if (angleDeg >= 360.0) angleDeg -= 360.0;
        int angleBucket = (int)Math.Floor(angleDeg / _angleBucketSizeDegrees);

        int curvatureBucket = (int)Math.Floor(interval.CurvatureScore / _curvatureBucketSize);

        return new EdgeKey(lengthBucket, angleBucket, curvatureBucket, zoneBucket);
    }

    /// <summary>
    /// Build a BoundaryIntervalInfo from a sub-range of a curve. Public so
    /// tests can construct fixtures cheaply without going through AddCurve.
    /// </summary>
    public static BoundaryIntervalInfo BuildInterval(
        Curve boundary,
        double t0,
        double t1,
        double approxLength,
        bool isOuterBoundary)
    {
        if (boundary == null) throw new ArgumentNullException(nameof(boundary));

        // Average tangent: tangent at midpoint.
        double tMid = 0.5 * (t0 + t1);
        Vector3d tangent = boundary.TangentAt(tMid);
        if (tangent.IsZero) tangent = new Vector3d(1, 0, 0);
        tangent.Unitize();

        // Inward normal: rotate tangent 90 degrees CCW in XY. For an outer
        // boundary traversed CCW this points inward; for a hole traversed CW
        // this also points inward by the same rule.
        var inward = new Vector3d(-tangent.Y, tangent.X, 0);

        // Curvature score: average curvature magnitude at three sample points.
        double curvature = 0.0;
        int curvCount = 0;
        for (int s = 0; s < 3; s++)
        {
            double t = t0 + (t1 - t0) * (s + 1) / 4.0;
            Vector3d k = boundary.CurvatureAt(t);
            if (k.IsValid)
            {
                curvature += k.Length;
                curvCount++;
            }
        }
        double curvatureScore = curvCount == 0 ? 0.0 : curvature / curvCount;

        // Straightness score: 1 - (chord length / arc length). 0 = perfectly
        // straight, approaching 1 for very curved.
        Point3d p0 = boundary.PointAt(t0);
        Point3d p1 = boundary.PointAt(t1);
        double chord = p0.DistanceTo(p1);
        double straightnessScore = approxLength <= 1e-9
            ? 0.0
            : Math.Max(0.0, 1.0 - chord / approxLength);

        // Local bounding box (tight; 5 sample points along the interval).
        var bbox = BoundingBox.Empty;
        for (int s = 0; s <= 5; s++)
        {
            double t = t0 + (t1 - t0) * s / 5.0;
            bbox.Union(boundary.PointAt(t));
        }

        // Concave pocket heuristic: if straightness is high (>= 0.5) the
        // window arcs significantly; mark as concave pocket so the matching
        // step prefers concave-edge fragments.
        bool isConcave = straightnessScore >= 0.5;

        return new BoundaryIntervalInfo(
            originalBoundary: boundary,
            simplifiedBoundary: null,
            t0: t0,
            t1: t1,
            approxLength: approxLength,
            averageTangent: tangent,
            inwardNormal: inward,
            curvatureScore: curvatureScore,
            straightnessScore: straightnessScore,
            isOuterBoundary: isOuterBoundary,
            isHoleBoundary: !isOuterBoundary,
            isConcavePocket: isConcave,
            localBounds: bbox);
    }

    private static double AngleDegrees(Vector3d v)
    {
        // Angle of the 2D projection of v in degrees, range (-180, 180].
        return Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
    }
}
