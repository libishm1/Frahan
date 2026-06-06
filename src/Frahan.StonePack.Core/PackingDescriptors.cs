using System;
using System.Collections.Generic;

namespace Frahan.Core;

/// <summary>
/// Pure-managed descriptor for one polyline edge segment. Used by the rail-
/// index matcher and the proposed "Frahan Edge Match" GH component (runbook
/// section 16.1) to score compatibility between a fragment edge and a sheet
/// boundary edge.
///
/// All four fields map directly to <see cref="EdgeKey"/> bucket inputs via
/// <see cref="ToEdgeKey"/>.
/// </summary>
public sealed class EdgeDescriptor
{
    public EdgeDescriptor(
        double length,
        double angleDegrees,
        double curvatureScore,
        double straightnessScore,
        int zoneId)
    {
        if (length < 0.0) throw new ArgumentOutOfRangeException(nameof(length), "must be >= 0");
        Length = length;
        AngleDegrees = angleDegrees;
        CurvatureScore = curvatureScore;
        StraightnessScore = straightnessScore;
        ZoneId = zoneId;
    }

    public double Length { get; }
    public double AngleDegrees { get; }
    public double CurvatureScore { get; }
    public double StraightnessScore { get; }
    public int ZoneId { get; }

    /// <summary>
    /// Quantise this descriptor into an <see cref="EdgeKey"/> using the
    /// supplied bucket sizes. The angle is wrapped to [0, 360) before
    /// bucketing.
    /// </summary>
    public EdgeKey ToEdgeKey(double lengthBucket, double angleBucketDegrees, double curvatureBucket)
    {
        if (lengthBucket <= 0.0) throw new ArgumentOutOfRangeException(nameof(lengthBucket));
        if (angleBucketDegrees <= 0.0) throw new ArgumentOutOfRangeException(nameof(angleBucketDegrees));
        if (curvatureBucket <= 0.0) throw new ArgumentOutOfRangeException(nameof(curvatureBucket));

        double a = AngleDegrees % 360.0;
        if (a < 0.0) a += 360.0;

        return new EdgeKey(
            lengthBucket: (int)Math.Floor(Length / lengthBucket),
            angleBucket: (int)Math.Floor(a / angleBucketDegrees),
            curvatureBucket: (int)Math.Floor(CurvatureScore / curvatureBucket),
            zoneBucket: ZoneId);
    }
}

/// <summary>
/// Pure-managed descriptor for one fragment (a closed 2D polygon plus its
/// summary geometry). Used by the proposed "Frahan Fragment Descriptors" GH
/// component (runbook section 16.1) and the rail-index matcher.
/// </summary>
public sealed class FragmentDescriptor
{
    public FragmentDescriptor(
        string id,
        double area,
        double perimeter,
        double aspectRatio,
        IReadOnlyList<EdgeDescriptor> edges)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        if (area < 0.0) throw new ArgumentOutOfRangeException(nameof(area), "must be >= 0");
        if (perimeter < 0.0) throw new ArgumentOutOfRangeException(nameof(perimeter), "must be >= 0");
        if (aspectRatio <= 0.0) throw new ArgumentOutOfRangeException(nameof(aspectRatio), "must be > 0");
        Area = area;
        Perimeter = perimeter;
        AspectRatio = aspectRatio;
        Edges = edges ?? Array.Empty<EdgeDescriptor>();
    }

    public string Id { get; }
    public double Area { get; }
    public double Perimeter { get; }
    public double AspectRatio { get; }
    public IReadOnlyList<EdgeDescriptor> Edges { get; }
    public int EdgeCount => Edges.Count;

    public override string ToString() =>
        $"FragmentDescriptor(id={Id}, area={Area:0.##}, perim={Perimeter:0.##}, " +
        $"aspect={AspectRatio:0.##}, edges={EdgeCount})";
}

/// <summary>
/// Pure-managed compatibility scorer between two <see cref="EdgeDescriptor"/>s.
/// Returns a number in [0, 1] where 1.0 = perfect match.
///
/// The score combines four sub-scores, each in [0, 1], multiplied:
///   - length similarity   (1 - |dL| / max(L1, L2))
///   - angle similarity    (1 - angular distance / 180)
///   - curvature similarity (1 - |dC| / max(|C1|, |C2|, eps))
///   - zone match           (1 if same zone OR caller passed preserveZone=false; else 0)
///
/// The product gives 0 when any single dimension is fully incompatible.
/// </summary>
public static class EdgeAffinityScorer
{
    public static double Score(
        EdgeDescriptor a,
        EdgeDescriptor b,
        bool preserveZone = true)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));

        double maxLen = Math.Max(a.Length, b.Length);
        double lenScore = maxLen <= 1e-12
            ? 1.0
            : Math.Max(0.0, 1.0 - Math.Abs(a.Length - b.Length) / maxLen);

        double angleDelta = AngleDistanceDegrees(a.AngleDegrees, b.AngleDegrees);
        double angleScore = Math.Max(0.0, 1.0 - angleDelta / 180.0);

        double maxCurv = Math.Max(Math.Abs(a.CurvatureScore), Math.Abs(b.CurvatureScore));
        double curvScore = maxCurv <= 1e-12
            ? 1.0
            : Math.Max(0.0, 1.0 - Math.Abs(a.CurvatureScore - b.CurvatureScore) / maxCurv);

        double zoneScore = preserveZone
            ? (a.ZoneId == b.ZoneId ? 1.0 : 0.0)
            : 1.0;

        return lenScore * angleScore * curvScore * zoneScore;
    }

    /// <summary>
    /// Smallest absolute difference between two angles, in degrees, taking
    /// 360-degree wrap into account. Range: [0, 180].
    /// </summary>
    public static double AngleDistanceDegrees(double a, double b)
    {
        double d = Math.Abs(a - b) % 360.0;
        if (d > 180.0) d = 360.0 - d;
        return d;
    }
}
