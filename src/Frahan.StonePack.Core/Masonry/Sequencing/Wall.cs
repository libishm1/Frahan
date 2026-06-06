#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// Wall — polygonal-masonry wall with regions, DAG, depth search, holes.
// Top-level entry point for Kim 2024 (Finding Installation Sequence of
// Polygonal Masonry through Design and Depth Search of a Directed
// Acyclic Graph, ASME IDETC-CIE2024, DETC2024-142563).
//
// The Python reference at
// Template-General/outputs/2026-05-20/polygonal_masonry_sequence/ is the
// 1:1 source of truth. This C# port preserves naming and behaviour.
//
// Pipeline:
//   Wall.FromChains(chains, bbox)  — chains + bbox to PSLG to regions
//   wall.InstallSequence()         — DAG (rules 5-8) + reversed Kahn's
//   wall.RemoveRegions(...)        — hole insertion (sec. 5.4)
// =============================================================================

public sealed class Region
{
    public int Id { get; }
    public List<(double X, double Y)> Ring { get; }
    public bool IsFinite { get; }
    public bool IsOuter { get; }
    public string Label { get; set; }

    public Region(int id, List<(double X, double Y)> ring,
                  bool isFinite, bool isOuter)
    {
        Id = id;
        Ring = ring;
        IsFinite = isFinite;
        IsOuter = isOuter;
        Label = string.Empty;
    }
}

public sealed class InstallationPlan
{
    public IReadOnlyDictionary<int, int> Depth { get; }
    public IReadOnlyDictionary<int, int> Order { get; }
    public IReadOnlyList<(int Low, int High)> DagEdges { get; }
    public int RegionCountExpected { get; }
    public int RegionCountActual { get; }

    public InstallationPlan(
        Dictionary<int, int> depth,
        Dictionary<int, int> order,
        List<(int Low, int High)> dagEdges,
        int regionCountExpected,
        int regionCountActual)
    {
        Depth = depth;
        Order = order;
        DagEdges = dagEdges;
        RegionCountExpected = regionCountExpected;
        RegionCountActual = regionCountActual;
    }
}

public sealed class Wall
{
    public Pslg Pslg { get; }
    public IReadOnlyList<Region> Regions { get; }
    public IReadOnlyList<List<(double X, double Y)>> Chains { get; }
    public (double XMin, double YMin, double XMax, double YMax) Bbox { get; }
    public HashSet<int> Holes { get; } = new();
    public IReadOnlyList<(double X, double Y)> Meetings { get; }
    public IReadOnlyList<int> ChainsPerMeeting { get; }

    private Wall(Pslg pslg, IReadOnlyList<Region> regions,
                 IReadOnlyList<List<(double X, double Y)>> chains,
                 (double, double, double, double) bbox,
                 IReadOnlyList<(double X, double Y)> meetings,
                 IReadOnlyList<int> cpm)
    {
        Pslg = pslg;
        Regions = regions;
        Chains = chains;
        Bbox = bbox;
        Meetings = meetings;
        ChainsPerMeeting = cpm;
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public static Wall FromChains(
        IEnumerable<IReadOnlyList<(double X, double Y)>> chains,
        (double XMin, double YMin, double XMax, double YMax) bbox,
        bool extendToBbox = true,
        double eps = Geom2D.DefaultEps)
    {
        if (chains == null) throw new ArgumentNullException(nameof(chains));
        if (!(bbox.XMax > bbox.XMin && bbox.YMax > bbox.YMin))
        {
            throw new ArgumentException(
                $"bbox must be non-degenerate, got {bbox}", nameof(bbox));
        }

        var normalised = new List<List<(double X, double Y)>>();
        int chainIndex = 0;
        foreach (var rawChain in chains)
        {
            var ch = Geom2D.NormaliseChain(rawChain);
            if (!Geom2D.ChainIsMonotone(ch, eps))
            {
                throw new ArgumentException(
                    $"chain {chainIndex} is not monotone in x or y");
            }
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            for (int i = 0; i < ch.Count; i++)
            {
                if (ch[i].X < minX) minX = ch[i].X;
                if (ch[i].X > maxX) maxX = ch[i].X;
            }
            bool xMonotone = (maxX - minX) > eps;
            if (extendToBbox && xMonotone && ch.Count >= 2)
            {
                if (ch[0].X > bbox.XMin + eps)
                {
                    ch.Insert(0, (bbox.XMin, ch[0].Y));
                }
                if (ch[ch.Count - 1].X < bbox.XMax - eps)
                {
                    ch.Add((bbox.XMax, ch[ch.Count - 1].Y));
                }
            }
            if (xMonotone)
            {
                ch = ClipChainToXRange(ch, bbox.XMin, bbox.XMax, eps);
            }
            if (ch.Count >= 2) normalised.Add(ch);
            chainIndex++;
        }

        var segments = new List<((double X, double Y) A, (double X, double Y) B)>();
        foreach (var ch in normalised)
        {
            for (int i = 0; i + 1 < ch.Count; i++)
            {
                segments.Add((ch[i], ch[i + 1]));
            }
        }
        segments.Add(((bbox.XMin, bbox.YMin), (bbox.XMax, bbox.YMin)));
        segments.Add(((bbox.XMax, bbox.YMin), (bbox.XMax, bbox.YMax)));
        segments.Add(((bbox.XMax, bbox.YMax), (bbox.XMin, bbox.YMax)));
        segments.Add(((bbox.XMin, bbox.YMax), (bbox.XMin, bbox.YMin)));

        var pslg = Pslg.FromSegments(segments, eps);

        var regions = new List<Region>(pslg.Faces.Count);
        for (int i = 0; i < pslg.Faces.Count; i++)
        {
            var face = pslg.Faces[i];
            regions.Add(new Region(i, face.Ring,
                                    isFinite: !face.IsOuter,
                                    isOuter: face.IsOuter));
        }

        var (meetings, cpm) = DetectMeetings(normalised, eps);
        return new Wall(pslg, regions, normalised, bbox, meetings, cpm);
    }

    private static List<(double X, double Y)> ClipChainToXRange(
        List<(double X, double Y)> chain, double xmin, double xmax, double eps)
    {
        if (chain.Count < 2) return new List<(double X, double Y)>(chain);
        var output = new List<(double X, double Y)>();
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            double x1 = chain[i].X, y1 = chain[i].Y;
            double x2 = chain[i + 1].X, y2 = chain[i + 1].Y;
            double lo = Math.Min(x1, x2), hi = Math.Max(x1, x2);
            if (hi < xmin - eps || lo > xmax + eps) continue;
            double cx1 = x1, cy1 = y1, cx2 = x2, cy2 = y2;
            if (cx1 < xmin)
            {
                double t = (xmin - cx1) / (cx2 - cx1);
                cy1 = cy1 + t * (cy2 - cy1);
                cx1 = xmin;
            }
            if (cx2 > xmax)
            {
                double t = (xmax - cx1) / (cx2 - cx1);
                cy2 = cy1 + t * (cy2 - cy1);
                cx2 = xmax;
            }
            if (output.Count == 0) output.Add((cx1, cy1));
            output.Add((cx2, cy2));
        }
        return output;
    }

    private static (List<(double X, double Y)> Meetings, List<int> Cpm)
        DetectMeetings(IReadOnlyList<List<(double X, double Y)>> chains, double eps)
    {
        double scale = 1.0 / Math.Max(eps, 1e-12);
        var owners = new Dictionary<(long, long), HashSet<int>>();
        var coords = new Dictionary<(long, long), (double X, double Y)>();
        for (int ci = 0; ci < chains.Count; ci++)
        {
            foreach (var p in chains[ci])
            {
                var key = ((long)Math.Round(p.X * scale), (long)Math.Round(p.Y * scale));
                if (!owners.TryGetValue(key, out var who))
                {
                    who = new HashSet<int>();
                    owners[key] = who;
                    coords[key] = p;
                }
                who.Add(ci);
            }
        }
        var meetings = new List<(double X, double Y)>();
        var cpm = new List<int>();
        foreach (var kvp in owners)
        {
            if (kvp.Value.Count >= 2)
            {
                meetings.Add(coords[kvp.Key]);
                cpm.Add(kvp.Value.Count);
            }
        }
        return (meetings, cpm);
    }

    // ------------------------------------------------------------------
    // Eq. (9) cross-check
    // ------------------------------------------------------------------

    public int ExpectedRegionCount()
    {
        int m = Chains.Count;
        int k = Meetings.Count;
        int sumC = 0;
        for (int i = 0; i < ChainsPerMeeting.Count; i++) sumC += ChainsPerMeeting[i];
        return m + 1 + sumC - k;
    }

    public int ActualFiniteRegionCount()
    {
        int finite = 0;
        foreach (var r in Regions) if (r.IsFinite) finite++;
        return Math.Max(0, finite - 2);
    }

    // ------------------------------------------------------------------
    // Adjacency and DAG construction (sec. 4)
    // ------------------------------------------------------------------

    private List<(int A, int B, List<((double X, double Y) P, (double X, double Y) Q)> Segs)>
        SharedSegments()
    {
        var outerIds = new HashSet<int>();
        foreach (var r in Regions) if (r.IsOuter) outerIds.Add(r.Id);
        var result = new List<(int A, int B, List<((double X, double Y) P, (double X, double Y) Q)> Segs)>();
        foreach (var (a, b, segs) in Pslg.AdjacencyPairs())
        {
            if (outerIds.Contains(a) || outerIds.Contains(b)) continue;
            if (segs.Count == 0) continue;
            result.Add((a, b, segs));
        }
        return result;
    }

    // Applies rules (5)-(8). Returns (lower, higher) or null when the
    // pair has no ordering constraint (purely vertical shared edge).
    // Throws when rule (8) is violated.
    private (int Low, int High)? DirectEdge(
        int a, int b,
        List<((double X, double Y) P, (double X, double Y) Q)> shared,
        double eps = Geom2D.DefaultEps)
    {
        var ra = Regions[a].Ring;
        var rb = Regions[b].Ring;
        int aAboveB = 0;
        int bAboveA = 0;
        double probeOffset = Math.Max(eps * 1000.0, 1e-4);
        foreach (var (rawP, rawQ) in shared)
        {
            var p = rawP;
            var q = rawQ;
            if (q.X < p.X) { (p, q) = (q, p); }
            double dx = q.X - p.X;
            double dy = q.Y - p.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < eps) continue;
            if (Math.Abs(dx) < eps) continue;  // purely vertical: no above/below
            double nx = -dy / length;
            double ny = dx / length;            // > 0, points to upper side
            var mid = ((p.X + q.X) * 0.5, (p.Y + q.Y) * 0.5);
            var upPt = (mid.Item1 + nx * probeOffset, mid.Item2 + ny * probeOffset);
            var dnPt = (mid.Item1 - nx * probeOffset, mid.Item2 - ny * probeOffset);
            bool upA = Geom2D.PointInRing(upPt, ra);
            bool upB = Geom2D.PointInRing(upPt, rb);
            bool dnA = Geom2D.PointInRing(dnPt, ra);
            bool dnB = Geom2D.PointInRing(dnPt, rb);
            if (upA && !upB) aAboveB++;
            else if (upB && !upA) bAboveA++;
            else if (dnA && !dnB) bAboveA++;
            else if (dnB && !dnA) aAboveB++;
        }
        if (aAboveB > 0 && bAboveA > 0)
        {
            throw new InvalidOperationException(
                $"rule (8) violated between regions {a} and {b}: " +
                "each is above the other on different segments");
        }
        if (aAboveB > 0) return (b, a);
        if (bAboveA > 0) return (a, b);
        return null;
    }

    public (Dictionary<int, List<int>> Graph, List<(int Low, int High)> Edges)
        BuildDag()
    {
        var edges = new List<(int Low, int High)>();
        var graph = new Dictionary<int, List<int>>();
        foreach (var (a, b, segs) in SharedSegments())
        {
            var edge = DirectEdge(a, b, segs);
            if (!edge.HasValue) continue;
            int low = edge.Value.Low;
            int high = edge.Value.High;
            if (Holes.Contains(Regions[low].Id) || Holes.Contains(Regions[high].Id))
            {
                continue;
            }
            edges.Add(edge.Value);
            if (!graph.TryGetValue(low, out var succs))
            {
                succs = new List<int>();
                graph[low] = succs;
            }
            succs.Add(high);
        }
        foreach (var r in Regions)
        {
            if (r.IsOuter) continue;
            if (Holes.Contains(r.Id)) continue;
            if (!graph.ContainsKey(r.Id)) graph[r.Id] = new List<int>();
        }
        return (graph, edges);
    }

    // ------------------------------------------------------------------
    // Reversed Kahn's depth search (Code 1, sec. 6)
    // ------------------------------------------------------------------

    public static Dictionary<int, int> ReversedKahnDepths(
        IReadOnlyDictionary<int, List<int>> graph)
    {
        var nodes = new HashSet<int>(graph.Keys);
        foreach (var kvp in graph)
        {
            foreach (var w in kvp.Value) nodes.Add(w);
        }
        var preds = new Dictionary<int, List<int>>();
        foreach (int v in nodes) preds[v] = new List<int>();
        foreach (var kvp in graph)
        {
            foreach (var w in kvp.Value) preds[w].Add(kvp.Key);
        }
        var depth = new Dictionary<int, int>();
        foreach (int v in nodes) depth[v] = 0;
        var current = new List<int>();
        foreach (int v in nodes)
        {
            if (!graph.TryGetValue(v, out var succs) || succs.Count == 0)
            {
                current.Add(v);
            }
        }
        int d = 1;
        int guard = 0;
        while (current.Count > 0)
        {
            var next = new List<int>();
            var seen = new HashSet<int>();
            foreach (int u in current)
            {
                foreach (int p in preds[u])
                {
                    depth[p] = d;            // overwrite is intentional
                    if (seen.Add(p)) next.Add(p);
                }
            }
            current = next;
            d++;
            guard++;
            if (guard > nodes.Count + 2)
            {
                throw new InvalidOperationException(
                    "reversed Kahn's exceeded node budget; graph likely has a cycle");
            }
        }
        return depth;
    }

    // ------------------------------------------------------------------
    // Top-level entry
    // ------------------------------------------------------------------

    public InstallationPlan InstallSequence()
    {
        var (graph, edges) = BuildDag();
        var depth = ReversedKahnDepths(graph);
        var keys = new List<int>(depth.Keys);
        keys.Sort((u, v) =>
        {
            int byDepth = depth[v].CompareTo(depth[u]);
            return byDepth != 0 ? byDepth : u.CompareTo(v);
        });
        var order = new Dictionary<int, int>();
        for (int i = 0; i < keys.Count; i++) order[keys[i]] = i + 1;
        return new InstallationPlan(
            depth, order, edges,
            ExpectedRegionCount(),
            ActualFiniteRegionCount());
    }

    // ------------------------------------------------------------------
    // Hole insertion (sec. 5.4)
    // ------------------------------------------------------------------

    public void RemoveRegions(IEnumerable<int> regionIds)
    {
        foreach (int rid in regionIds)
        {
            if (rid >= 0 && rid < Regions.Count) Holes.Add(rid);
        }
    }

    // ------------------------------------------------------------------
    // Convenience
    // ------------------------------------------------------------------

    public (int? BottomId, int? TopId) ClassifyTopBottom(double eps = Geom2D.DefaultEps)
    {
        var finite = new List<Region>();
        foreach (var r in Regions) if (r.IsFinite) finite.Add(r);
        if (finite.Count == 0) return (null, null);
        Region bot = null, top = null;
        foreach (var r in finite)
        {
            bool touchesBottom = false, touchesTop = false;
            foreach (var p in r.Ring)
            {
                if (Math.Abs(p.Y - Bbox.YMin) < eps) touchesBottom = true;
                if (Math.Abs(p.Y - Bbox.YMax) < eps) touchesTop = true;
            }
            if (touchesBottom && (bot == null || r.Ring.Count > bot.Ring.Count)) bot = r;
            if (touchesTop && (top == null || r.Ring.Count > top.Ring.Count)) top = r;
        }
        return (bot?.Id, top?.Id);
    }
}
