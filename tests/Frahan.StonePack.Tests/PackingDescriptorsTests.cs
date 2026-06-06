#nullable disable
using System;
using Frahan.Core;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.EdgeDescriptor + FragmentDescriptor + EdgeAffinityScorer.
// Pure managed; no Rhino runtime required.

static class PackingDescriptorsTests
{
    // -- EdgeDescriptor -----------------------------------------------------

    public static void EdgeDescriptor_NegativeLength_Throws()
    {
        bool threw = false;
        try { _ = new EdgeDescriptor(-1, 0, 0, 0, 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative length should throw");
    }

    public static void EdgeDescriptor_ToEdgeKey_QuantisesAllFour()
    {
        var d = new EdgeDescriptor(length: 5.5, angleDegrees: 47.0, curvatureScore: 0.07, straightnessScore: 0.1, zoneId: 3);
        var k = d.ToEdgeKey(lengthBucket: 1.0, angleBucketDegrees: 5.0, curvatureBucket: 0.01);
        Assert(k.LengthBucket == 5, $"length bucket 5.5/1.0 -> 5, got {k.LengthBucket}");
        Assert(k.AngleBucket == 9, $"angle bucket 47/5 -> 9, got {k.AngleBucket}");
        Assert(k.CurvatureBucket == 7, $"curvature 0.07/0.01 -> 7, got {k.CurvatureBucket}");
        Assert(k.ZoneBucket == 3, $"zone passes through, got {k.ZoneBucket}");
    }

    public static void EdgeDescriptor_NegativeAngle_WrapsTo360()
    {
        var d = new EdgeDescriptor(1, -45, 0, 0, 0);
        var k = d.ToEdgeKey(1, 5, 1);
        // -45 % 360 = -45 in C#, then +360 = 315 -> bucket 63.
        Assert(k.AngleBucket == 63, $"-45 deg should wrap to 315 / 5 = 63, got {k.AngleBucket}");
    }

    // -- FragmentDescriptor -------------------------------------------------

    public static void FragmentDescriptor_NullId_Throws()
    {
        bool threw = false;
        try { _ = new FragmentDescriptor(null, 1, 1, 1, Array.Empty<EdgeDescriptor>()); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null id should throw");
    }

    public static void FragmentDescriptor_NullEdges_BecomesEmpty()
    {
        var f = new FragmentDescriptor("x", 1, 1, 1, null);
        Assert(f.EdgeCount == 0, "null edges should default to empty");
    }

    // -- EdgeAffinityScorer -------------------------------------------------

    public static void Score_IdenticalEdges_IsOne()
    {
        var a = new EdgeDescriptor(10, 30, 0.05, 0.1, 1);
        var b = new EdgeDescriptor(10, 30, 0.05, 0.1, 1);
        double s = EdgeAffinityScorer.Score(a, b);
        Assert(Math.Abs(s - 1.0) < 1e-9, $"identical edges should score 1, got {s}");
    }

    public static void Score_OppositeAngle_IsZero()
    {
        // 0 deg vs 180 deg -> angular distance 180 -> angleScore = 0 -> product 0.
        var a = new EdgeDescriptor(10, 0, 0, 0, 1);
        var b = new EdgeDescriptor(10, 180, 0, 0, 1);
        double s = EdgeAffinityScorer.Score(a, b);
        Assert(s == 0.0, $"opposite angles should score 0, got {s}");
    }

    public static void Score_DifferentZones_PreserveZoneTrue_IsZero()
    {
        var a = new EdgeDescriptor(10, 30, 0.05, 0.1, 1);
        var b = new EdgeDescriptor(10, 30, 0.05, 0.1, 2);
        double s = EdgeAffinityScorer.Score(a, b, preserveZone: true);
        Assert(s == 0.0, $"different zones with preserveZone=true should score 0, got {s}");
    }

    public static void Score_DifferentZones_PreserveZoneFalse_IsNonZero()
    {
        var a = new EdgeDescriptor(10, 30, 0.05, 0.1, 1);
        var b = new EdgeDescriptor(10, 30, 0.05, 0.1, 2);
        double s = EdgeAffinityScorer.Score(a, b, preserveZone: false);
        Assert(s > 0.99, $"different zones with preserveZone=false should still score ~1, got {s}");
    }

    public static void Score_NullArgument_Throws()
    {
        var a = new EdgeDescriptor(10, 30, 0.05, 0.1, 1);
        bool threw = false;
        try { EdgeAffinityScorer.Score(null, a); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null first arg should throw");
    }

    public static void AngleDistance_350vs10_IsTwenty()
    {
        // Wrap-around: closer than 340.
        double d = EdgeAffinityScorer.AngleDistanceDegrees(350, 10);
        Assert(Math.Abs(d - 20.0) < 1e-9, $"350 vs 10 should wrap to 20, got {d}");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
