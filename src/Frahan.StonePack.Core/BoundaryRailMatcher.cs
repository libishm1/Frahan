using System;
using System.Collections.Generic;

namespace Frahan.Core;

/// <summary>
/// One scored match of a query <see cref="EdgeDescriptor"/> against an
/// interval payload returned by <see cref="BoundaryRailIndex{TInterval}"/>.
/// </summary>
public sealed class EdgeMatch<TInterval>
    where TInterval : class
{
    public EdgeMatch(TInterval interval, double affinityScore, EdgeKey indexKey)
    {
        Interval = interval ?? throw new ArgumentNullException(nameof(interval));
        AffinityScore = affinityScore;
        IndexKey = indexKey;
    }

    public TInterval Interval { get; }
    public double AffinityScore { get; }
    public EdgeKey IndexKey { get; }

    public override string ToString() =>
        $"EdgeMatch(score={AffinityScore:0.###}, key={IndexKey})";
}

/// <summary>
/// Knobs for <see cref="BoundaryRailMatcher.MatchEdge{TInterval}"/>.
/// </summary>
public sealed class MatchOptions
{
    public MatchOptions()
    {
        LengthBucketSize = 1.0;
        AngleBucketSizeDegrees = 5.0;
        CurvatureBucketSize = 0.01;
        LengthRadius = 1;
        AngleRadius = 1;
        PreserveZone = true;
        TopK = 16;
        MinAffinityScore = 0.0;
    }

    /// <summary>EdgeKey length bucket size, must match the index's bucketing.</summary>
    public double LengthBucketSize { get; set; }

    /// <summary>EdgeKey angle bucket size in degrees, must match the index's bucketing.</summary>
    public double AngleBucketSizeDegrees { get; set; }

    /// <summary>EdgeKey curvature bucket size, must match the index's bucketing.</summary>
    public double CurvatureBucketSize { get; set; }

    /// <summary>How many length-buckets to widen on each side. 0 = exact length only.</summary>
    public int LengthRadius { get; set; }

    /// <summary>How many angle-buckets to widen on each side.</summary>
    public int AngleRadius { get; set; }

    /// <summary>If true, only match within the query's zone bucket.</summary>
    public bool PreserveZone { get; set; }

    /// <summary>Maximum number of matches to return (sorted by descending score). 0 = unlimited.</summary>
    public int TopK { get; set; }

    /// <summary>Filter out matches with score below this threshold (range [0, 1]). 0 = no filter.</summary>
    public double MinAffinityScore { get; set; }
}

/// <summary>
/// Pure-managed query side of the boundary-rail-index pipeline. Given a
/// populated <see cref="BoundaryRailIndex{TInterval}"/> and a query
/// <see cref="EdgeDescriptor"/>, returns ranked matches scored by
/// <see cref="EdgeAffinityScorer"/>.
///
/// Spec 5 section 5.5 + 5.6 + the proposed "Frahan Edge Match" GH component
/// (runbook section 16.1).
///
/// Decoupled from any specific TInterval type via a caller-supplied
/// converter <c>intervalToDescriptor</c>. For BoundaryIntervalInfo
/// (Frahan.Surface), the converter extracts the average tangent angle and
/// length/curvature/zone fields. For test fixtures the converter can return
/// a fixed test descriptor.
/// </summary>
public static class BoundaryRailMatcher
{
    /// <summary>
    /// Match one query edge against the populated rail index. Returns matches
    /// sorted by descending affinity score, capped at <see cref="MatchOptions.TopK"/>
    /// (or unlimited if TopK == 0), filtered by <see cref="MatchOptions.MinAffinityScore"/>.
    /// </summary>
    public static IReadOnlyList<EdgeMatch<TInterval>> MatchEdge<TInterval>(
        BoundaryRailIndex<TInterval> index,
        EdgeDescriptor query,
        MatchOptions options,
        Func<TInterval, EdgeDescriptor> intervalToDescriptor)
        where TInterval : class
    {
        if (index == null) throw new ArgumentNullException(nameof(index));
        if (query == null) throw new ArgumentNullException(nameof(query));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (intervalToDescriptor == null) throw new ArgumentNullException(nameof(intervalToDescriptor));

        EdgeKey queryKey = query.ToEdgeKey(
            options.LengthBucketSize,
            options.AngleBucketSizeDegrees,
            options.CurvatureBucketSize);

        var candidates = new List<EdgeMatch<TInterval>>();
        foreach (TInterval candidate in index.QueryNeighbors(
            queryKey,
            lengthRadius: options.LengthRadius,
            angleRadius: options.AngleRadius,
            preserveZone: options.PreserveZone))
        {
            EdgeDescriptor candidateDescriptor;
            try
            {
                candidateDescriptor = intervalToDescriptor(candidate);
            }
            catch
            {
                // The user-supplied converter threw; skip this candidate.
                continue;
            }
            if (candidateDescriptor == null) continue;

            double score = EdgeAffinityScorer.Score(query, candidateDescriptor, options.PreserveZone);
            if (score < options.MinAffinityScore) continue;

            // The IndexKey we report is the key of THIS candidate, not the query.
            // Reconstruct using the candidate's descriptor so callers can see
            // which bucket the match landed in.
            var candidateKey = candidateDescriptor.ToEdgeKey(
                options.LengthBucketSize,
                options.AngleBucketSizeDegrees,
                options.CurvatureBucketSize);

            candidates.Add(new EdgeMatch<TInterval>(candidate, score, candidateKey));
        }

        candidates.Sort((a, b) => b.AffinityScore.CompareTo(a.AffinityScore));

        if (options.TopK > 0 && candidates.Count > options.TopK)
            candidates.RemoveRange(options.TopK, candidates.Count - options.TopK);

        return candidates;
    }

    /// <summary>
    /// Match every edge of a fragment against the rail index. Returns one
    /// match list per fragment edge, in fragment-edge order.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<EdgeMatch<TInterval>>> MatchFragment<TInterval>(
        BoundaryRailIndex<TInterval> index,
        FragmentDescriptor fragment,
        MatchOptions options,
        Func<TInterval, EdgeDescriptor> intervalToDescriptor)
        where TInterval : class
    {
        if (fragment == null) throw new ArgumentNullException(nameof(fragment));

        var perEdge = new List<IReadOnlyList<EdgeMatch<TInterval>>>(fragment.Edges.Count);
        for (int i = 0; i < fragment.Edges.Count; i++)
        {
            perEdge.Add(MatchEdge(index, fragment.Edges[i], options, intervalToDescriptor));
        }
        return perEdge;
    }
}
