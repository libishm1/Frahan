#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Core;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.BoundaryRailIndex<TInterval> + EdgeKey.
// Pure-managed; no Rhino runtime required.

static class BoundaryRailIndexTests
{
    // -- EdgeKey equality / hash ---------------------------------------------

    public static void EdgeKey_Equality_Same4Buckets_AreEqual()
    {
        var a = new EdgeKey(3, 5, 7, 11);
        var b = new EdgeKey(3, 5, 7, 11);
        Assert(a.Equals(b), "identical EdgeKey instances should be equal");
        Assert(a.GetHashCode() == b.GetHashCode(),
            "equal EdgeKey instances must produce the same hash");
    }

    public static void EdgeKey_Equality_DifferentBuckets_AreUnequal()
    {
        var a = new EdgeKey(3, 5, 7, 11);
        var b = new EdgeKey(3, 5, 7, 12); // zone differs
        Assert(!a.Equals(b), "EdgeKey with different ZoneBucket should not be equal");
    }

    public static void EdgeKey_HashIsStableAcrossInstances()
    {
        var a = new EdgeKey(0, 0, 0, 0);
        var b = new EdgeKey(0, 0, 0, 0);
        Assert(a.GetHashCode() == b.GetHashCode(),
            "all-zero EdgeKey hash should be stable across instances");
    }

    // -- Add and Query (exact) -----------------------------------------------

    public static void Add_StoresInterval_QueryReturnsIt()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(1, 2, 3, 4), "alpha");

        var result = index.Query(new EdgeKey(1, 2, 3, 4));
        Assert(result.Count == 1, "exact Query should return one interval");
        Assert(result[0] == "alpha", "returned interval should be 'alpha'");
        Assert(index.IntervalCount == 1, "IntervalCount should be 1");
        Assert(index.KeyCount == 1, "KeyCount should be 1");
    }

    public static void Query_MissingKey_ReturnsEmpty()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(1, 2, 3, 4), "alpha");

        var result = index.Query(new EdgeKey(9, 9, 9, 9));
        Assert(result.Count == 0, "missing key should return empty list");
    }

    public static void Add_NullInterval_Throws()
    {
        var index = new BoundaryRailIndex<string>();
        bool threw = false;
        try { index.Add(new EdgeKey(0, 0, 0, 0), null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "Add(null) should throw ArgumentNullException");
    }

    public static void Add_TwoIntervalsSameKey_AreBothReturned()
    {
        var index = new BoundaryRailIndex<string>();
        var key = new EdgeKey(1, 2, 3, 4);
        index.Add(key, "alpha");
        index.Add(key, "beta");

        var result = index.Query(key);
        Assert(result.Count == 2, "two intervals at same key should both be returned");
        Assert(result.Contains("alpha") && result.Contains("beta"),
            "Query should return both alpha and beta");
        Assert(index.IntervalCount == 2, "IntervalCount should be 2");
        Assert(index.KeyCount == 1, "KeyCount should still be 1 (same key)");
    }

    // -- KnownZones ----------------------------------------------------------

    public static void KnownZones_TracksDistinctZoneBuckets()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(0, 0, 0, 5), "z5");
        index.Add(new EdgeKey(0, 0, 0, 5), "z5b"); // duplicate zone
        index.Add(new EdgeKey(0, 0, 0, 7), "z7");
        index.Add(new EdgeKey(0, 0, 0, 9), "z9");

        var zones = new HashSet<int>(index.KnownZones);
        Assert(zones.Count == 3, $"expected 3 distinct zones, got {zones.Count}");
        Assert(zones.Contains(5) && zones.Contains(7) && zones.Contains(9),
            "expected zones 5, 7, 9");
    }

    // -- QueryNeighbors widening (length / angle radius) ---------------------

    public static void QueryNeighbors_LengthRadius_FindsAdjacentBuckets()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(10, 5, 0, 1), "exact");
        index.Add(new EdgeKey(11, 5, 0, 1), "L+1");
        index.Add(new EdgeKey(9, 5, 0, 1), "L-1");
        index.Add(new EdgeKey(12, 5, 0, 1), "L+2"); // outside +/-1 radius

        var hits = new HashSet<string>(index.QueryNeighbors(
            new EdgeKey(10, 5, 0, 1), lengthRadius: 1, angleRadius: 0, preserveZone: true));

        Assert(hits.Count == 3, $"expected 3 hits within length radius 1, got {hits.Count}");
        Assert(hits.Contains("exact") && hits.Contains("L+1") && hits.Contains("L-1"),
            "expected exact, L+1, L-1");
        Assert(!hits.Contains("L+2"), "L+2 should be outside the +/-1 length radius");
    }

    public static void QueryNeighbors_AngleRadius_FindsAdjacentBuckets()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(10, 5, 0, 1), "exact");
        index.Add(new EdgeKey(10, 6, 0, 1), "A+1");
        index.Add(new EdgeKey(10, 4, 0, 1), "A-1");
        index.Add(new EdgeKey(10, 7, 0, 1), "A+2"); // outside +/-1 radius

        var hits = new HashSet<string>(index.QueryNeighbors(
            new EdgeKey(10, 5, 0, 1), lengthRadius: 0, angleRadius: 1, preserveZone: true));

        Assert(hits.Count == 3, $"expected 3 hits within angle radius 1, got {hits.Count}");
        Assert(hits.Contains("exact") && hits.Contains("A+1") && hits.Contains("A-1"),
            "expected exact, A+1, A-1");
    }

    public static void QueryNeighbors_PreserveZoneTrue_NarrowsToSingleZone()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(10, 5, 0, 1), "z1");
        index.Add(new EdgeKey(10, 5, 0, 2), "z2");
        index.Add(new EdgeKey(10, 5, 0, 3), "z3");

        var hits = new HashSet<string>(index.QueryNeighbors(
            new EdgeKey(10, 5, 0, 1), lengthRadius: 0, angleRadius: 0, preserveZone: true));

        Assert(hits.Count == 1, $"preserveZone=true should return only the matching-zone hit, got {hits.Count}");
        Assert(hits.Contains("z1"), "expected only z1");
    }

    // The B1 fix verification: with preserveZone=false, the index should widen across
    // every known zone bucket and return all three. The original no-op ternary would
    // have returned only the supplied zone (z1).
    public static void QueryNeighbors_PreserveZoneFalse_WidensAcrossAllZones_FixesB1()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(10, 5, 0, 1), "z1");
        index.Add(new EdgeKey(10, 5, 0, 2), "z2");
        index.Add(new EdgeKey(10, 5, 0, 3), "z3");

        var hits = new HashSet<string>(index.QueryNeighbors(
            new EdgeKey(10, 5, 0, 1), lengthRadius: 0, angleRadius: 0, preserveZone: false));

        Assert(hits.Count == 3, $"B1 fix: preserveZone=false should return all 3 zones, got {hits.Count}");
        Assert(hits.Contains("z1") && hits.Contains("z2") && hits.Contains("z3"),
            "B1 fix: expected hits in z1, z2, z3");
    }

    public static void QueryNeighbors_CurvatureBucketMustMatch()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(10, 5, 0, 1), "c0");
        index.Add(new EdgeKey(10, 5, 1, 1), "c1");
        index.Add(new EdgeKey(10, 5, 2, 1), "c2");

        var hits = new HashSet<string>(index.QueryNeighbors(
            new EdgeKey(10, 5, 0, 1), lengthRadius: 5, angleRadius: 5, preserveZone: true));

        Assert(hits.Count == 1, "curvature bucket must match exactly; widening does not span curvature");
        Assert(hits.Contains("c0"), "expected only c0");
    }

    public static void QueryNeighbors_NegativeRadius_Throws()
    {
        var index = new BoundaryRailIndex<string>();
        bool threwLen = false, threwAng = false;
        try { index.QueryNeighbors(new EdgeKey(0, 0, 0, 0), lengthRadius: -1).ToList(); }
        catch (ArgumentOutOfRangeException) { threwLen = true; }
        try { index.QueryNeighbors(new EdgeKey(0, 0, 0, 0), angleRadius: -1).ToList(); }
        catch (ArgumentOutOfRangeException) { threwAng = true; }
        Assert(threwLen, "negative lengthRadius should throw ArgumentOutOfRangeException");
        Assert(threwAng, "negative angleRadius should throw ArgumentOutOfRangeException");
    }

    public static void QueryNeighbors_RadiusZero_ReturnsExactMatchOnly()
    {
        var index = new BoundaryRailIndex<string>();
        index.Add(new EdgeKey(10, 5, 0, 1), "exact");
        index.Add(new EdgeKey(11, 5, 0, 1), "L+1");

        var hits = new HashSet<string>(index.QueryNeighbors(
            new EdgeKey(10, 5, 0, 1), lengthRadius: 0, angleRadius: 0, preserveZone: true));

        Assert(hits.Count == 1, "radius=0 should return only the exact key");
        Assert(hits.Contains("exact"), "expected only the exact match");
    }

    // -- Helpers -------------------------------------------------------------

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
