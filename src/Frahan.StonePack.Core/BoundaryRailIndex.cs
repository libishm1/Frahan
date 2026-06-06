using System;
using System.Collections.Generic;

namespace Frahan.Core;

/// <summary>
/// Four-bucket key for the boundary rail index: length, angle, curvature, zone.
/// Pure-managed value type; net48 / netstandard2.0 friendly hash (manual GetHashCode,
/// no HashCode.Combine).
/// </summary>
public readonly struct EdgeKey : IEquatable<EdgeKey>
{
    public readonly int LengthBucket;
    public readonly int AngleBucket;
    public readonly int CurvatureBucket;
    public readonly int ZoneBucket;

    public EdgeKey(int lengthBucket, int angleBucket, int curvatureBucket, int zoneBucket)
    {
        LengthBucket = lengthBucket;
        AngleBucket = angleBucket;
        CurvatureBucket = curvatureBucket;
        ZoneBucket = zoneBucket;
    }

    public bool Equals(EdgeKey other) =>
        LengthBucket == other.LengthBucket
        && AngleBucket == other.AngleBucket
        && CurvatureBucket == other.CurvatureBucket
        && ZoneBucket == other.ZoneBucket;

    public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = LengthBucket;
            h = (h * 397) ^ AngleBucket;
            h = (h * 397) ^ CurvatureBucket;
            h = (h * 397) ^ ZoneBucket;
            return h;
        }
    }

    public override string ToString() =>
        $"EdgeKey(L={LengthBucket}, A={AngleBucket}, C={CurvatureBucket}, Z={ZoneBucket})";
}

/// <summary>
/// Generic boundary-rail index. Maps an <see cref="EdgeKey"/> to a list of
/// caller-defined interval payloads. Supports widening neighbour queries by
/// length-bucket and angle-bucket radius, and optional widening across all
/// zone buckets the index has seen.
///
/// Pure managed. No Rhino.Geometry dependency. Callers parameterise
/// <typeparamref name="TInterval"/> with their own DTO (typically a Rhino-bound
/// BoundaryIntervalInfo in Frahan.Surface, or a test-only POD).
///
/// Bug fix B1 (2026-05-04): the original research snippet's QueryNeighbors used
/// the ternary expression
///     preserveZone ? key.ZoneBucket : key.ZoneBucket
/// which was a no-op (both arms returned the same value), silently disabling the
/// preserveZone parameter. The corrected behaviour, implemented below, is:
///     preserveZone == true  =&gt; query only key.ZoneBucket
///     preserveZone == false =&gt; iterate every zone bucket the index has seen
/// </summary>
public sealed class BoundaryRailIndex<TInterval>
    where TInterval : class
{
    private readonly Dictionary<EdgeKey, List<TInterval>> _map =
        new Dictionary<EdgeKey, List<TInterval>>();
    private readonly HashSet<int> _knownZones = new HashSet<int>();

    public int IntervalCount { get; private set; }

    public int KeyCount => _map.Count;

    public IReadOnlyCollection<int> KnownZones => _knownZones;

    public void Add(EdgeKey key, TInterval interval)
    {
        if (interval == null) throw new ArgumentNullException(nameof(interval));

        if (!_map.TryGetValue(key, out var list))
        {
            list = new List<TInterval>();
            _map[key] = list;
        }

        list.Add(interval);
        _knownZones.Add(key.ZoneBucket);
        IntervalCount++;
    }

    /// <summary>
    /// Exact-key lookup. Returns an empty list when the key is not present.
    /// </summary>
    public IReadOnlyList<TInterval> Query(EdgeKey key)
    {
        return _map.TryGetValue(key, out var list)
            ? (IReadOnlyList<TInterval>)list
            : Array.Empty<TInterval>();
    }

    /// <summary>
    /// Widening neighbour lookup. Yields every interval whose key falls within
    /// +/- lengthRadius and +/- angleRadius of the supplied key, sharing the
    /// same curvature bucket, and either the same zone bucket
    /// (preserveZone == true) or any zone bucket the index has seen
    /// (preserveZone == false).
    /// </summary>
    public IEnumerable<TInterval> QueryNeighbors(
        EdgeKey key,
        int lengthRadius = 1,
        int angleRadius = 1,
        bool preserveZone = true)
    {
        if (lengthRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(lengthRadius));
        if (angleRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(angleRadius));

        // FIX-B1: see class summary. Replace the no-op ternary with an explicit
        // zone selector. preserveZone == true narrows to the supplied zone;
        // preserveZone == false widens across every known zone.
        IEnumerable<int> zonesToScan = preserveZone
            ? (IEnumerable<int>)new[] { key.ZoneBucket }
            : _knownZones;

        foreach (int zone in zonesToScan)
        {
            for (int dl = -lengthRadius; dl <= lengthRadius; dl++)
            {
                for (int da = -angleRadius; da <= angleRadius; da++)
                {
                    var neighbor = new EdgeKey(
                        key.LengthBucket + dl,
                        key.AngleBucket + da,
                        key.CurvatureBucket,
                        zone);

                    if (_map.TryGetValue(neighbor, out var list))
                    {
                        foreach (var interval in list)
                            yield return interval;
                    }
                }
            }
        }
    }
}
