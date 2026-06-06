using System;
using System.Collections.Generic;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Rotation/translation-invariant bucketed index for both planar 2D
    /// and spatial 3D segments. A segment is routed to the 2D table when
    /// its TorsionSignature is null (the marker stamped by
    /// <see cref="BoundarySegmenter"/>) and to the 3D table otherwise
    /// (stamped by <see cref="BoundarySegmenter3D"/>). Complement queries
    /// route the same way: a 2D segment finds 2D complements, a 3D
    /// segment finds 3D complements. Mixed-mode matching is intentionally
    /// not supported — addendum §5 states "a planar segment cannot
    /// complement a strongly-twisted one and produce a flush joint".
    /// </summary>
    public sealed class SegmentHashIndex
    {
        private readonly SortedDictionary<SegmentHashKey, List<Segment>> _buckets2D;
        private readonly SortedDictionary<SegmentHashKey3D, List<Segment>> _buckets3D;
        private readonly HashOptions _opt;

        public SegmentHashIndex(HashOptions? opt = null)
        {
            _opt = opt ?? new HashOptions();
            _buckets2D = new SortedDictionary<SegmentHashKey, List<Segment>>(KeyComparer2D);
            _buckets3D = new SortedDictionary<SegmentHashKey3D, List<Segment>>(KeyComparer3D);
        }

        private static IComparer<SegmentHashKey> KeyComparer2D { get; } =
            Comparer<SegmentHashKey>.Create((a, b) =>
            {
                int c = a.LengthBin.CompareTo(b.LengthBin); if (c != 0) return c;
                c = a.TurningBin.CompareTo(b.TurningBin); if (c != 0) return c;
                c = a.MeanBin.CompareTo(b.MeanBin); if (c != 0) return c;
                c = a.StdBin.CompareTo(b.StdBin); if (c != 0) return c;
                return a.Sign.CompareTo(b.Sign);
            });

        private static IComparer<SegmentHashKey3D> KeyComparer3D { get; } =
            Comparer<SegmentHashKey3D>.Create((a, b) =>
            {
                int c = KeyComparer2D.Compare(a.Base, b.Base); if (c != 0) return c;
                c = a.PlanarityBin.CompareTo(b.PlanarityBin); if (c != 0) return c;
                return a.TorsionVarBin.CompareTo(b.TorsionVarBin);
            });

        public SegmentHashKey KeyOf(Segment s)
        {
            double mean = Mean(s.TurningSignature);
            double std = StdDev(s.TurningSignature, mean);
            return new SegmentHashKey(
                (int)Math.Round(s.ChordLength / _opt.LengthBinSize),
                (int)Math.Round(s.TotalTurning / _opt.TurningBinSize),
                (int)Math.Round(mean / _opt.MeanBinSize),
                (int)Math.Round(std / _opt.StdBinSize),
                s.Sign);
        }

        public SegmentHashKey3D KeyOf3D(Segment s)
        {
            if (s.TorsionSignature == null)
                throw new InvalidOperationException(
                    $"Segment {s.PanelId}/{s.Index} has no torsion signature; route via the 2D key instead.");
            var baseKey = KeyOf(s);
            int planarityBin = (int)Math.Round(s.PanelPlanarityRms / _opt.PlanarityBinSize);
            double tMean = Mean(s.TorsionSignature);
            double tStd = StdDev(s.TorsionSignature, tMean);
            int torsionVarBin = (int)Math.Round(tStd / _opt.TorsionVarBinSize);
            return new SegmentHashKey3D(baseKey, planarityBin, torsionVarBin);
        }

        public void Add(Segment s)
        {
            if (s.TorsionSignature == null)
            {
                var k = KeyOf(s);
                if (!_buckets2D.TryGetValue(k, out var list))
                {
                    list = new List<Segment>();
                    _buckets2D[k] = list;
                }
                list.Add(s);
            }
            else
            {
                var k = KeyOf3D(s);
                if (!_buckets3D.TryGetValue(k, out var list))
                {
                    list = new List<Segment>();
                    _buckets3D[k] = list;
                }
                list.Add(s);
            }
        }

        public List<Segment> QueryComplement(Segment s) =>
            s.TorsionSignature == null ? Query2D(s) : Query3D(s);

        public int Count2D
        {
            get
            {
                int n = 0;
                foreach (var kv in _buckets2D) n += kv.Value.Count;
                return n;
            }
        }

        public int Count3D
        {
            get
            {
                int n = 0;
                foreach (var kv in _buckets3D) n += kv.Value.Count;
                return n;
            }
        }

        private List<Segment> Query2D(Segment s)
        {
            var key = KeyOf(s);
            int n = _opt.BinNeighbourhood;
            var hits = new List<Segment>();
            for (int dl = -n; dl <= n; dl++)
            for (int dt = -n; dt <= n; dt++)
            for (int dm = -n; dm <= n; dm++)
            for (int ds = -n; ds <= n; ds++)
            {
                var probe = new SegmentHashKey(
                    key.LengthBin + dl,
                    -key.TurningBin + dt,
                    -key.MeanBin + dm,
                    key.StdBin + ds,
                    -key.Sign);
                if (_buckets2D.TryGetValue(probe, out var list))
                    hits.AddRange(list);
            }
            SortDeterministic(hits);
            return hits;
        }

        private List<Segment> Query3D(Segment s)
        {
            var key = KeyOf3D(s);
            int n = _opt.BinNeighbourhood;
            var hits = new List<Segment>();
            for (int dl = -n; dl <= n; dl++)
            for (int dt = -n; dt <= n; dt++)
            for (int dm = -n; dm <= n; dm++)
            for (int ds = -n; ds <= n; ds++)
            {
                var probeBase = new SegmentHashKey(
                    key.Base.LengthBin + dl,
                    -key.Base.TurningBin + dt,
                    -key.Base.MeanBin + dm,
                    key.Base.StdBin + ds,
                    -key.Base.Sign);
                // Planarity matches exactly: a flat shard cannot fit a
                // strongly-twisted edge. Torsion variance also stays the
                // same — torsion sign flips, but its statistical spread
                // is invariant under the reflection.
                var probe = new SegmentHashKey3D(probeBase, key.PlanarityBin, key.TorsionVarBin);
                if (_buckets3D.TryGetValue(probe, out var list))
                    hits.AddRange(list);
            }
            SortDeterministic(hits);
            return hits;
        }

        private static void SortDeterministic(List<Segment> hits)
        {
            hits.Sort((x, y) =>
            {
                int c = string.CompareOrdinal(x.PanelId, y.PanelId);
                return c != 0 ? c : x.Index.CompareTo(y.Index);
            });
        }

        private static double Mean(double[] xs)
        {
            double s = 0.0;
            for (int i = 0; i < xs.Length; i++) s += xs[i];
            return xs.Length == 0 ? 0.0 : s / xs.Length;
        }

        private static double StdDev(double[] xs, double mean)
        {
            double s = 0.0;
            for (int i = 0; i < xs.Length; i++) { double d = xs[i] - mean; s += d * d; }
            return xs.Length == 0 ? 0.0 : Math.Sqrt(s / xs.Length);
        }
    }
}
