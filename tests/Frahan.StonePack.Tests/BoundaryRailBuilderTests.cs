#nullable disable
using System;
using Frahan.Core;
using Frahan.Surface;
using Rhino.Geometry;

namespace Frahan.Tests;

// Unit tests for Frahan.Surface.BoundaryIntervalInfo + BoundaryRailBuilder.
// Bucketing tests are pure-managed (no Rhino runtime required) because they
// construct BoundaryIntervalInfo directly via its public ctor and exercise
// BoundaryRailBuilder.BucketInterval. The AddCurve(Curve) path requires the
// Rhino native runtime (Curve / Vector3d / Point3d / BoundingBox); those
// tests are tagged so the runner SKIPs them when rhcommon_c.dll is missing.

static class BoundaryRailBuilderTests
{
    // -- Constructor guards (pure managed, always run) -----------------------

    public static void Builder_Ctor_NonPositiveWindow_Throws()
    {
        bool threw = false;
        try { _ = new BoundaryRailBuilder(0.0, 1.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "windowLength <= 0 should throw");
    }

    public static void Builder_Ctor_NonPositiveStep_Throws()
    {
        bool threw = false;
        try { _ = new BoundaryRailBuilder(10.0, 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "stepLength <= 0 should throw");
    }

    public static void Builder_Ctor_NonPositiveLengthBucket_Throws()
    {
        bool threw = false;
        try { _ = new BoundaryRailBuilder(10.0, 1.0, lengthBucketSize: 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "lengthBucketSize <= 0 should throw");
    }

    public static void Builder_Ctor_NonPositiveAngleBucket_Throws()
    {
        bool threw = false;
        try { _ = new BoundaryRailBuilder(10.0, 1.0, angleBucketSizeDegrees: 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "angleBucketSizeDegrees <= 0 should throw");
    }

    // -- BoundaryIntervalInfo construction guards ---------------------------

    public static void IntervalInfo_NullCurve_Throws()
    {
        bool threw = false;
        try
        {
            _ = new BoundaryIntervalInfo(
                originalBoundary: null,
                simplifiedBoundary: null,
                t0: 0, t1: 1, approxLength: 1,
                averageTangent: Vector3d.XAxis,
                inwardNormal: Vector3d.YAxis,
                curvatureScore: 0,
                straightnessScore: 0,
                isOuterBoundary: true,
                isHoleBoundary: false,
                isConcavePocket: false,
                localBounds: BoundingBox.Empty);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null original boundary should throw");
    }

    // -- BucketInterval (pure managed) --------------------------------------

    public static void BucketInterval_LengthBucket_QuantisesByLengthBucketSize()
    {
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        var interval = BoundaryRailBuilder.BuildInterval(line, 0.0, 1.0, 5.5, true);

        var b1 = new BoundaryRailBuilder(10, 1, lengthBucketSize: 1.0);
        var b2 = new BoundaryRailBuilder(10, 1, lengthBucketSize: 2.0);

        var k1 = b1.BucketInterval(interval, zoneBucket: 7);
        var k2 = b2.BucketInterval(interval, zoneBucket: 7);

        Assert(k1.LengthBucket == 5, $"approxLength 5.5 / bucketSize 1 -> bucket 5, got {k1.LengthBucket}");
        Assert(k2.LengthBucket == 2, $"approxLength 5.5 / bucketSize 2 -> bucket 2, got {k2.LengthBucket}");
        Assert(k1.ZoneBucket == 7, "zone bucket should round-trip");
    }

    public static void BucketInterval_AngleBucket_HorizontalLineIsBucket0()
    {
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        var interval = BoundaryRailBuilder.BuildInterval(line, 0.0, 1.0, 10.0, true);

        var b = new BoundaryRailBuilder(10, 1, angleBucketSizeDegrees: 5.0);
        var k = b.BucketInterval(interval, 0);

        // Horizontal +X line -> tangent (1,0,0) -> angle 0 deg -> bucket 0.
        Assert(k.AngleBucket == 0, $"horizontal tangent should be angle bucket 0, got {k.AngleBucket}");
    }

    public static void BucketInterval_AngleBucket_NinetyDegreeLineIsExpectedBucket()
    {
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(0, 10, 0));
        var interval = BoundaryRailBuilder.BuildInterval(line, 0.0, 1.0, 10.0, true);

        var b = new BoundaryRailBuilder(10, 1, angleBucketSizeDegrees: 5.0);
        var k = b.BucketInterval(interval, 0);

        // +Y tangent -> 90 deg -> bucket 90 / 5 = 18.
        Assert(k.AngleBucket == 18, $"vertical tangent should be angle bucket 18, got {k.AngleBucket}");
    }

    public static void BucketInterval_AngleBucket_NegativeAngleWrapsTo360()
    {
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(-10, 0, 0));
        var interval = BoundaryRailBuilder.BuildInterval(line, 0.0, 1.0, 10.0, true);

        var b = new BoundaryRailBuilder(10, 1, angleBucketSizeDegrees: 5.0);
        var k = b.BucketInterval(interval, 0);

        // -X tangent -> atan2 returns 180 deg -> wraps to 180 -> bucket 180/5 = 36.
        Assert(k.AngleBucket == 36, $"-X tangent should be angle bucket 36, got {k.AngleBucket}");
    }

    public static void BucketInterval_NullInterval_Throws()
    {
        var b = new BoundaryRailBuilder(10, 1);
        bool threw = false;
        try { b.BucketInterval(null, 0); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null interval should throw");
    }

    public static void BucketInterval_DeterministicForSameInput()
    {
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(7, 3, 0));
        var interval = BoundaryRailBuilder.BuildInterval(line, 0.0, 1.0, 7.6, true);

        var b = new BoundaryRailBuilder(10, 1);
        var k1 = b.BucketInterval(interval, 5);
        var k2 = b.BucketInterval(interval, 5);

        Assert(k1.Equals(k2), "bucketing should be deterministic for the same interval+zone");
    }

    // -- BuildInterval (uses Rhino runtime; SKIPs if rhcommon_c.dll missing)

    public static void BuildInterval_HorizontalLine_PopulatesFields()
    {
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        var interval = BoundaryRailBuilder.BuildInterval(line, 0.0, 1.0, 10.0, true);

        Assert(interval != null, "interval should be constructed");
        Assert(interval.OriginalBoundary == line, "original curve should round-trip");
        Assert(interval.IsOuterBoundary, "outer flag should round-trip");
        Assert(!interval.IsHoleBoundary, "hole flag should be the inverse");
        Assert(Math.Abs(interval.AverageTangent.X - 1.0) < 1e-6, $"tangent X should be ~1, got {interval.AverageTangent.X}");
        Assert(Math.Abs(interval.AverageTangent.Y) < 1e-6, $"tangent Y should be ~0, got {interval.AverageTangent.Y}");
        Assert(Math.Abs(interval.InwardNormal.Y - 1.0) < 1e-6, $"inward normal Y should be ~1 (CCW rotation), got {interval.InwardNormal.Y}");
        Assert(interval.StraightnessScore < 0.01, $"straight line should have ~0 straightness score, got {interval.StraightnessScore}");
    }

    public static void BuildInterval_NullCurve_Throws()
    {
        bool threw = false;
        try { _ = BoundaryRailBuilder.BuildInterval(null, 0, 1, 1, true); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null curve should throw");
    }

    // -- AddCurve end-to-end (uses Rhino runtime) ---------------------------

    public static void AddCurve_PopulatesIndex_WithExpectedIntervalCount()
    {
        // 100-unit horizontal line, window 20, step 10 -> sliding windows at
        // 0..20, 10..30, 20..40, ..., 80..100. Step count = floor(100/10) = 10.
        // Each window length 20 >= 0.75 * 20 = 15, so none are skipped.
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
        var b = new BoundaryRailBuilder(windowLength: 20.0, stepLength: 10.0);
        var index = new BoundaryRailIndex<BoundaryIntervalInfo>();

        b.AddCurve(line, isOuterBoundary: true, zoneBucket: 0, index: index);

        // 10 sample points; the i=9 case has endLen = min(90+20, 100) = 100,
        // length = 10, which is < 15 -> SKIPPED. So 9 expected.
        Assert(index.IntervalCount == 9, $"expected 9 intervals on a 100-unit line, got {index.IntervalCount}");
    }

    public static void AddCurve_NullBoundary_Throws()
    {
        var b = new BoundaryRailBuilder(10, 1);
        var index = new BoundaryRailIndex<BoundaryIntervalInfo>();
        bool threw = false;
        try { b.AddCurve(null, true, 0, index); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null boundary curve should throw");
    }

    public static void AddCurve_NullIndex_Throws()
    {
        var b = new BoundaryRailBuilder(10, 1);
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        bool threw = false;
        try { b.AddCurve(line, true, 0, null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null index should throw");
    }

    // -- Helpers ------------------------------------------------------------

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
