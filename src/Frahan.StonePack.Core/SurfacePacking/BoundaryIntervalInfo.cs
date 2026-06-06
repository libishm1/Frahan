using System;
using Rhino.Geometry;

namespace Frahan.Surface;

/// <summary>
/// One interval along a boundary rail: a sub-range of the original Rhino curve
/// (start parameter T0, end parameter T1) plus its descriptors used to bucket
/// the interval into an <see cref="Frahan.Core.EdgeKey"/>. The DTO is the
/// payload type for <see cref="Frahan.Core.BoundaryRailIndex{TInterval}"/> in
/// the surface-packing path.
///
/// Spec 5 lines 321 - 337 specifies this DTO. Lives in Frahan.Surface because
/// it carries Rhino.Geometry types (Curve, Vector3d, BoundingBox) at a public
/// boundary; the pure-managed BoundaryRailIndex stays in Frahan.Core and is
/// generic over its payload.
///
/// Constructor takes raw fields. Use <see cref="BoundaryRailBuilder"/> to
/// construct populated instances from a Rhino Curve.
/// </summary>
public sealed class BoundaryIntervalInfo
{
    public BoundaryIntervalInfo(
        Curve originalBoundary,
        Curve simplifiedBoundary,
        double t0,
        double t1,
        double approxLength,
        Vector3d averageTangent,
        Vector3d inwardNormal,
        double curvatureScore,
        double straightnessScore,
        bool isOuterBoundary,
        bool isHoleBoundary,
        bool isConcavePocket,
        BoundingBox localBounds)
    {
        OriginalBoundary = originalBoundary ?? throw new ArgumentNullException(nameof(originalBoundary));
        SimplifiedBoundary = simplifiedBoundary;
        T0 = t0;
        T1 = t1;
        ApproxLength = approxLength;
        AverageTangent = averageTangent;
        InwardNormal = inwardNormal;
        CurvatureScore = curvatureScore;
        StraightnessScore = straightnessScore;
        IsOuterBoundary = isOuterBoundary;
        IsHoleBoundary = isHoleBoundary;
        IsConcavePocket = isConcavePocket;
        LocalBounds = localBounds;
    }

    public Curve OriginalBoundary { get; }
    public Curve SimplifiedBoundary { get; }
    public double T0 { get; }
    public double T1 { get; }
    public double ApproxLength { get; }
    public Vector3d AverageTangent { get; }
    public Vector3d InwardNormal { get; }
    public double CurvatureScore { get; }
    public double StraightnessScore { get; }
    public bool IsOuterBoundary { get; }
    public bool IsHoleBoundary { get; }
    public bool IsConcavePocket { get; }
    public BoundingBox LocalBounds { get; }

    public override string ToString() =>
        $"BoundaryIntervalInfo(T={T0:0.###}..{T1:0.###}, len={ApproxLength:0.###}, " +
        $"outer={IsOuterBoundary}, hole={IsHoleBoundary}, concave={IsConcavePocket})";
}
