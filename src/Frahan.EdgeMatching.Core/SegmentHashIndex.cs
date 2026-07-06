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

        // Phase 0 #2: scale-relative length bin width when an assembly Scale is set;
        // otherwise the legacy absolute LengthBinSize.
        private double EffectiveLengthBinSize =>
            _opt.Scale > 0.0 ? _opt.Scale * _opt.RelativeLengthBinFraction : _opt.LengthBinSize;

        public SegmentHashKey KeyOf(Segment s)
        {
            double mean = Mean(s.TurningSignature);
            double std = StdDev(s.TurningSignature, mean);
            return new SegmentHashKey(
                (int)Math.Round(s.ChordLength / EffectiveLengthBinSize),
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
            if (_opt.UseMultiProbe) return Query2DMultiProbe(s);
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
            if (_opt.UseMultiProbe) return Query3DMultiProbe(s);
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

        // -------- Phase 0 #1: query-directed multi-probe over the 4 banded dims --------
        // Complement-query continuous coordinates (in bin units) and home bins.
        // Complementarity transform: length same, turning + mean negate, std invariant.
        private void ComplementCoords(Segment s, out double[] coord, out int[] home)
        {
            double mean = Mean(s.TurningSignature);
            double std = StdDev(s.TurningSignature, mean);
            double lb = EffectiveLengthBinSize;
            coord = new[]
            {
                s.ChordLength / lb,
                -s.TotalTurning / _opt.TurningBinSize,
                -mean / _opt.MeanBinSize,
                std / _opt.StdBinSize,
            };
            home = new int[4];
            for (int i = 0; i < 4; i++) home[i] = (int)Math.Round(coord[i]);
        }

        // Top-T perturbation vectors over the 4 dims, ranked by squared boundary distance
        // (query-directed multi-probe ranking, Lv/Josephson/Wang/Charikar/Li 2007
        // "Multi-Probe LSH", VLDB: the score is their squared distance-to-boundary per
        // perturbed dimension). Reaches +/-MaxProbeStep where the query straddles
        // a boundary, while keeping the probe count bounded.
        private List<int[]> RankedProbes(double[] coord, int[] home)
        {
            int step = Math.Max(0, _opt.MaxProbeStep);
            var cands = new List<KeyValuePair<double, int[]>>();
            for (int d0 = -step; d0 <= step; d0++)
            for (int d1 = -step; d1 <= step; d1++)
            for (int d2 = -step; d2 <= step; d2++)
            for (int d3 = -step; d3 <= step; d3++)
            {
                var dv = new[] { d0, d1, d2, d3 };
                double sc = 0.0;
                for (int d = 0; d < 4; d++)
                {
                    int t = dv[d];
                    if (t == 0) continue;
                    double delta = coord[d] - home[d];           // residual in [-0.5, 0.5]
                    double dist = t > 0 ? (t - 0.5 - delta) : (-t - 0.5 + delta);
                    sc += dist * dist;
                }
                cands.Add(new KeyValuePair<double, int[]>(sc, dv));
            }
            cands.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key); if (c != 0) return c;
                for (int d = 0; d < 4; d++) { c = a.Value[d].CompareTo(b.Value[d]); if (c != 0) return c; }
                return 0;
            });
            int take = Math.Min(Math.Max(1, _opt.MultiProbeT), cands.Count);
            var outl = new List<int[]>(take);
            for (int i = 0; i < take; i++) outl.Add(cands[i].Value);
            return outl;
        }

        private List<Segment> Query2DMultiProbe(Segment s)
        {
            ComplementCoords(s, out var coord, out var home);
            int signC = -s.Sign;
            var hits = new List<Segment>();
            foreach (var dv in RankedProbes(coord, home))
            {
                var probe = new SegmentHashKey(home[0] + dv[0], home[1] + dv[1], home[2] + dv[2], home[3] + dv[3], signC);
                if (_buckets2D.TryGetValue(probe, out var list)) hits.AddRange(list);
            }
            SortDeterministic(hits);
            return hits;
        }

        private List<Segment> Query3DMultiProbe(Segment s)
        {
            var k3 = KeyOf3D(s);   // validates torsion presence + gives planarity/torsion-var bins
            ComplementCoords(s, out var coord, out var home);
            int signC = -s.Sign;
            var hits = new List<Segment>();
            foreach (var dv in RankedProbes(coord, home))
            {
                var baseProbe = new SegmentHashKey(home[0] + dv[0], home[1] + dv[1], home[2] + dv[2], home[3] + dv[3], signC);
                var probe = new SegmentHashKey3D(baseProbe, k3.PlanarityBin, k3.TorsionVarBin);
                if (_buckets3D.TryGetValue(probe, out var list)) hits.AddRange(list);
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
