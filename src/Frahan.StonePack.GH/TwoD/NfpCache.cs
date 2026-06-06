using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

public sealed class NfpCache
{
    private readonly Dictionary<string, NfpRhino> _cache = new();

    public int Hits { get; private set; }
    public int Misses { get; private set; }

    public NfpRhino GetOrCreate(
        string stationaryKey,
        IList<Point2d> stationary,
        string slidingKey,
        IList<Point2d> sliding,
        double tolerance,
        int maxIterations,
        bool rectangleShortcut)
    {
        var key = $"{stationaryKey}|{slidingKey}|{tolerance:R}|{maxIterations}";
        if (_cache.TryGetValue(key, out var nfp))
        {
            Hits++;
            return nfp;
        }

        nfp = new NfpRhino(stationary, sliding, tolerance, maxIterations, rectangleShortcut);
        _cache[key] = nfp;
        Misses++;
        return nfp;
    }
}
