#nullable disable
using System;
using System.Linq;
using Frahan.Core;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.BoundaryRailMatcher + EdgeMatch + MatchOptions.
// Pure managed; no Rhino runtime required.
//
// Test payload type: a small POD record carrying its own EdgeDescriptor for
// the converter to surface.

static class BoundaryRailMatcherTests
{
    private sealed class TestPayload
    {
        public TestPayload(string label, EdgeDescriptor descriptor)
        {
            Label = label;
            Descriptor = descriptor;
        }
        public string Label { get; }
        public EdgeDescriptor Descriptor { get; }
    }

    private static EdgeDescriptor Convert(TestPayload p) => p.Descriptor;

    private static MatchOptions DefaultOptions() => new MatchOptions
    {
        LengthBucketSize = 1.0,
        AngleBucketSizeDegrees = 5.0,
        CurvatureBucketSize = 0.01,
        LengthRadius = 1,
        AngleRadius = 1,
        PreserveZone = true,
        TopK = 16,
        MinAffinityScore = 0.0,
    };

    private static (BoundaryRailIndex<TestPayload> index, MatchOptions opts) MakePopulatedIndex()
    {
        var idx = new BoundaryRailIndex<TestPayload>();
        var opts = DefaultOptions();

        // Bucket geometry: lengthBucket = 1.0, angleBucket = 5deg, zoneId differentiates.
        Add(idx, opts, "a-perfect", length: 10.0, angle: 30.0, curvature: 0.0, zone: 1);
        Add(idx, opts, "b-similar", length: 10.5, angle: 31.0, curvature: 0.0, zone: 1);
        Add(idx, opts, "c-far-len", length: 50.0, angle: 30.0, curvature: 0.0, zone: 1);
        Add(idx, opts, "d-other-zone", length: 10.0, angle: 30.0, curvature: 0.0, zone: 2);
        Add(idx, opts, "e-opposite-angle", length: 10.0, angle: 210.0, curvature: 0.0, zone: 1);

        return (idx, opts);
    }

    private static void Add(
        BoundaryRailIndex<TestPayload> idx,
        MatchOptions opts,
        string label,
        double length, double angle, double curvature, int zone)
    {
        var d = new EdgeDescriptor(length, angle, curvature, 0.0, zone);
        var k = d.ToEdgeKey(opts.LengthBucketSize, opts.AngleBucketSizeDegrees, opts.CurvatureBucketSize);
        idx.Add(k, new TestPayload(label, d));
    }

    // -- Argument guards -----------------------------------------------------

    public static void MatchEdge_NullIndex_Throws()
    {
        bool threw = false;
        try { BoundaryRailMatcher.MatchEdge<TestPayload>(null, new EdgeDescriptor(1, 0, 0, 0, 0), DefaultOptions(), Convert); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null index should throw");
    }

    public static void MatchEdge_NullQuery_Throws()
    {
        bool threw = false;
        try { BoundaryRailMatcher.MatchEdge<TestPayload>(new BoundaryRailIndex<TestPayload>(), null, DefaultOptions(), Convert); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null query should throw");
    }

    public static void MatchEdge_NullOptions_Throws()
    {
        bool threw = false;
        try { BoundaryRailMatcher.MatchEdge<TestPayload>(new BoundaryRailIndex<TestPayload>(), new EdgeDescriptor(1, 0, 0, 0, 0), null, Convert); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null options should throw");
    }

    public static void MatchEdge_NullConverter_Throws()
    {
        bool threw = false;
        try { BoundaryRailMatcher.MatchEdge<TestPayload>(new BoundaryRailIndex<TestPayload>(), new EdgeDescriptor(1, 0, 0, 0, 0), DefaultOptions(), null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null converter should throw");
    }

    // -- Empty index ---------------------------------------------------------

    public static void MatchEdge_EmptyIndex_ReturnsEmpty()
    {
        var matches = BoundaryRailMatcher.MatchEdge(
            new BoundaryRailIndex<TestPayload>(),
            new EdgeDescriptor(10, 30, 0, 0, 1),
            DefaultOptions(),
            Convert);
        Assert(matches.Count == 0, $"empty index -> 0 matches, got {matches.Count}");
    }

    // -- Ranking -------------------------------------------------------------

    public static void MatchEdge_PerfectMatchScoresHighest()
    {
        var (idx, opts) = MakePopulatedIndex();
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        Assert(matches.Count >= 1, $"expected at least 1 match, got {matches.Count}");
        Assert(matches[0].Interval.Label == "a-perfect",
            $"top match should be 'a-perfect', got '{matches[0].Interval.Label}'");
        Assert(Math.Abs(matches[0].AffinityScore - 1.0) < 1e-9,
            $"perfect match score should be 1.0, got {matches[0].AffinityScore}");
    }

    public static void MatchEdge_ResultsAreSortedDescending()
    {
        var (idx, opts) = MakePopulatedIndex();
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        for (int i = 1; i < matches.Count; i++)
            Assert(matches[i - 1].AffinityScore >= matches[i].AffinityScore,
                $"matches must be sorted by descending score, got " +
                $"[{i-1}]={matches[i-1].AffinityScore} > [{i}]={matches[i].AffinityScore}");
    }

    // -- Filters -------------------------------------------------------------

    public static void MatchEdge_PreserveZoneTrue_ExcludesOtherZones()
    {
        var (idx, opts) = MakePopulatedIndex();
        opts.PreserveZone = true;
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        foreach (var m in matches)
            Assert(m.Interval.Descriptor.ZoneId == 1,
                $"preserveZone=true should exclude zone {m.Interval.Descriptor.ZoneId}");
    }

    public static void MatchEdge_PreserveZoneFalse_IncludesOtherZones()
    {
        var (idx, opts) = MakePopulatedIndex();
        opts.PreserveZone = false;
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        Assert(matches.Any(m => m.Interval.Label == "d-other-zone"),
            "preserveZone=false should surface 'd-other-zone'");
    }

    public static void MatchEdge_LengthRadiusZero_ExcludesFarLength()
    {
        var (idx, opts) = MakePopulatedIndex();
        opts.LengthRadius = 0;
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        Assert(!matches.Any(m => m.Interval.Label == "c-far-len"),
            "lengthRadius=0 should exclude length 50 candidate");
    }

    public static void MatchEdge_MinAffinityScore_FiltersLowMatches()
    {
        var (idx, opts) = MakePopulatedIndex();
        opts.MinAffinityScore = 0.99;
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        Assert(matches.All(m => m.AffinityScore >= 0.99),
            $"all matches should be >= 0.99, got min {matches.Min(m => m.AffinityScore)}");
    }

    public static void MatchEdge_TopK_LimitsResults()
    {
        var (idx, opts) = MakePopulatedIndex();
        // Add many similar candidates so TopK takes effect.
        for (int i = 0; i < 50; i++)
            Add(idx, opts, $"extra-{i}", length: 10.0, angle: 30.0, curvature: 0.0, zone: 1);

        opts.TopK = 5;
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        Assert(matches.Count == 5, $"TopK=5 should cap at 5 matches, got {matches.Count}");
    }

    public static void MatchEdge_TopKZero_ReturnsAll()
    {
        var (idx, opts) = MakePopulatedIndex();
        opts.TopK = 0; // unlimited
        opts.PreserveZone = false; // include all 5 fixtures
        opts.LengthRadius = 100; // include far-length
        opts.AngleRadius = 100;
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);
        var matches = BoundaryRailMatcher.MatchEdge(idx, query, opts, Convert);

        Assert(matches.Count == 5, $"TopK=0 with wide radii should return all 5, got {matches.Count}");
    }

    // -- Converter resilience ------------------------------------------------

    public static void MatchEdge_ConverterThrows_SkipsCandidate()
    {
        var (idx, opts) = MakePopulatedIndex();
        var query = new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1);

        // Converter throws on every candidate -> no matches returned.
        var matches = BoundaryRailMatcher.MatchEdge<TestPayload>(idx, query, opts,
            _ => throw new InvalidOperationException("intentional"));
        Assert(matches.Count == 0, $"throwing converter should yield 0 matches, got {matches.Count}");
    }

    // -- MatchFragment -------------------------------------------------------

    public static void MatchFragment_NullFragment_Throws()
    {
        var (idx, opts) = MakePopulatedIndex();
        bool threw = false;
        try { BoundaryRailMatcher.MatchFragment(idx, null, opts, Convert); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null fragment should throw");
    }

    public static void MatchFragment_ReturnsOneListPerEdge()
    {
        var (idx, opts) = MakePopulatedIndex();
        var fragment = new FragmentDescriptor("frag-1", area: 100.0, perimeter: 40.0, aspectRatio: 1.0,
            edges: new[]
            {
                new EdgeDescriptor(10.0, 30.0, 0.0, 0.0, 1),
                new EdgeDescriptor(50.0, 30.0, 0.0, 0.0, 1),
            });
        var perEdge = BoundaryRailMatcher.MatchFragment(idx, fragment, opts, Convert);

        Assert(perEdge.Count == 2, $"expected 2 per-edge match lists, got {perEdge.Count}");
        Assert(perEdge[0].Count >= 1, "first edge should have at least one match");
    }

    // -- Helpers -------------------------------------------------------------

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
