#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// Pslg — half-edge planar straight-line graph + face extraction. Companion
// to Wall (Wall.FromChains feeds chain + bbox segments here, then walks
// faces out as regions).
//
// Assumptions:
// - Input segments meet only at endpoints. No proper crossings. The
//   paper's design method enforces this (sec. 3, sec. 5.2 Fig. 11).
// - Coincident points within eps are unified.
// - T-junctions (an endpoint lying inside another segment) ARE handled:
//   a pre-split pass slices each segment at every candidate point that
//   lies on its interior.
//
// Face-traversal rule: at each vertex the next half-edge along a face
// (CCW for bounded faces) is the clockwise neighbour of the twin in
// the outgoing-edge angular ordering.
// =============================================================================

public sealed class Face
{
    public List<int> Cycle { get; }
    public List<(double X, double Y)> Ring { get; }
    public double SignedArea { get; }
    public bool IsOuter { get; internal set; }

    public Face(List<int> cycle, List<(double X, double Y)> ring,
                double signedArea, bool isOuter = false)
    {
        Cycle = cycle;
        Ring = ring;
        SignedArea = signedArea;
        IsOuter = isOuter;
    }
}

public sealed class Pslg
{
    private sealed class HalfEdge
    {
        public int Src;
        public int Dst;
        public int Twin;
        public int Next;
        public int Face;
    }

    public IReadOnlyList<(double X, double Y)> Vertices => _vertices;
    public IReadOnlyList<Face> Faces => _faces;

    private readonly List<(double X, double Y)> _vertices;
    private readonly List<HalfEdge> _halfedges = new();
    private readonly List<Face> _faces = new();

    private Pslg(List<(double X, double Y)> vertices)
    {
        _vertices = vertices;
    }

    public static Pslg FromSegments(
        IEnumerable<((double X, double Y) A, (double X, double Y) B)> segments,
        double eps = Geom2D.DefaultEps)
    {
        if (segments == null) throw new ArgumentNullException(nameof(segments));
        double scale = 1.0 / Math.Max(eps, 1e-12);

        (long, long) Quantise((double X, double Y) p)
            => ((long)Math.Round(p.X * scale), (long)Math.Round(p.Y * scale));

        // Pass 1: collect endpoints.
        var raw = new List<((double X, double Y) A, (double X, double Y) B)>(segments);
        var endpointKeys = new Dictionary<(long, long), (double X, double Y)>();
        foreach (var (a, b) in raw)
        {
            endpointKeys[Quantise(a)] = a;
            endpointKeys[Quantise(b)] = b;
        }
        var candidatePoints = new List<(double X, double Y)>(endpointKeys.Values);

        // Pass 2: split each segment at any candidate point lying inside it.
        var split = new List<((double X, double Y) A, (double X, double Y) B)>();
        foreach (var (a, b) in raw)
        {
            var ka = Quantise(a);
            var kb = Quantise(b);
            var interior = new List<(double T, (double X, double Y) P)>();
            for (int i = 0; i < candidatePoints.Count; i++)
            {
                var p = candidatePoints[i];
                var kp = Quantise(p);
                if (kp.Equals(ka) || kp.Equals(kb)) continue;
                if (!Geom2D.OnSegment(a, b, p, eps)) continue;
                double dx = p.X - a.X;
                double dy = p.Y - a.Y;
                interior.Add((dx * dx + dy * dy, p));
            }
            interior.Sort((u, v) => u.T.CompareTo(v.T));
            var chain = new List<(double X, double Y)> { a };
            foreach (var (_, p) in interior) chain.Add(p);
            chain.Add(b);
            var cleaned = new List<(double X, double Y)>();
            foreach (var p in chain)
            {
                if (cleaned.Count == 0 ||
                    !Quantise(p).Equals(Quantise(cleaned[cleaned.Count - 1])))
                {
                    cleaned.Add(p);
                }
            }
            for (int i = 0; i + 1 < cleaned.Count; i++)
            {
                split.Add((cleaned[i], cleaned[i + 1]));
            }
        }

        // Pass 3: build vertex table and dedup edges.
        var vertexList = new List<(double X, double Y)>();
        var vertexIndex = new Dictionary<(long, long), int>();

        int VidFor((double X, double Y) p)
        {
            var key = Quantise(p);
            if (vertexIndex.TryGetValue(key, out int vid)) return vid;
            vid = vertexList.Count;
            vertexIndex[key] = vid;
            vertexList.Add(p);
            return vid;
        }

        var edgeSet = new Dictionary<(int, int), bool>();
        foreach (var (a, b) in split)
        {
            int u = VidFor(a);
            int v = VidFor(b);
            if (u == v) continue;
            var key = u < v ? (u, v) : (v, u);
            edgeSet[key] = true;
        }

        var pslg = new Pslg(vertexList);
        pslg.BuildHalfedges(edgeSet.Keys);
        pslg.WireNextPointers();
        pslg.ExtractFaces();
        return pslg;
    }

    private void BuildHalfedges(IEnumerable<(int U, int V)> edges)
    {
        foreach (var (u, v) in edges)
        {
            int h1 = _halfedges.Count;
            int h2 = h1 + 1;
            _halfedges.Add(new HalfEdge { Src = u, Dst = v, Twin = h2, Next = -1, Face = -1 });
            _halfedges.Add(new HalfEdge { Src = v, Dst = u, Twin = h1, Next = -1, Face = -1 });
        }
    }

    private double Angle(int hi)
    {
        var h = _halfedges[hi];
        var src = _vertices[h.Src];
        var dst = _vertices[h.Dst];
        return Math.Atan2(dst.Y - src.Y, dst.X - src.X);
    }

    private void WireNextPointers()
    {
        var outByV = new Dictionary<int, List<int>>();
        for (int hi = 0; hi < _halfedges.Count; hi++)
        {
            int src = _halfedges[hi].Src;
            if (!outByV.TryGetValue(src, out var list))
            {
                list = new List<int>();
                outByV[src] = list;
            }
            list.Add(hi);
        }

        var outPos = new Dictionary<int, Dictionary<int, int>>();
        foreach (var kvp in outByV)
        {
            kvp.Value.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
            var positions = new Dictionary<int, int>(kvp.Value.Count);
            for (int pos = 0; pos < kvp.Value.Count; pos++)
            {
                positions[kvp.Value[pos]] = pos;
            }
            outPos[kvp.Key] = positions;
        }

        for (int hi = 0; hi < _halfedges.Count; hi++)
        {
            var h = _halfedges[hi];
            int v = h.Dst;
            int twin = h.Twin;
            int pos = outPos[v][twin];
            int count = outByV[v].Count;
            int prevPos = (pos - 1 + count) % count;
            h.Next = outByV[v][prevPos];
        }
    }

    private void ExtractFaces()
    {
        int n = _halfedges.Count;
        var seen = new bool[n];
        for (int start = 0; start < n; start++)
        {
            if (seen[start]) continue;
            var cycle = new List<int>();
            int cur = start;
            int steps = 0;
            while (!seen[cur])
            {
                if (steps > n + 4)
                {
                    throw new InvalidOperationException(
                        "PSLG face traversal exceeded edge budget; graph malformed.");
                }
                seen[cur] = true;
                cycle.Add(cur);
                cur = _halfedges[cur].Next;
                steps++;
            }
            var ring = new List<(double X, double Y)>(cycle.Count);
            foreach (int h in cycle)
            {
                ring.Add(_vertices[_halfedges[h].Src]);
            }
            _faces.Add(new Face(cycle, ring, Geom2D.SignedArea(ring)));
        }

        if (_faces.Count > 0)
        {
            int outerIdx = 0;
            double minArea = _faces[0].SignedArea;
            for (int i = 1; i < _faces.Count; i++)
            {
                if (_faces[i].SignedArea < minArea)
                {
                    minArea = _faces[i].SignedArea;
                    outerIdx = i;
                }
            }
            _faces[outerIdx].IsOuter = true;
        }
        for (int fi = 0; fi < _faces.Count; fi++)
        {
            foreach (int h in _faces[fi].Cycle)
            {
                _halfedges[h].Face = fi;
            }
        }
    }

    public List<Face> BoundedFaces()
    {
        var result = new List<Face>();
        foreach (var f in _faces) if (!f.IsOuter) result.Add(f);
        return result;
    }

    public Face OuterFace()
    {
        foreach (var f in _faces) if (f.IsOuter) return f;
        throw new InvalidOperationException("PSLG has no outer face.");
    }

    // (faceA, faceB) -> shared undirected segments. Each entry has faceA < faceB.
    public List<(int A, int B, List<((double X, double Y) P, (double X, double Y) Q)> Segs)>
        AdjacencyPairs()
    {
        var pairs = new Dictionary<(int, int), List<((double X, double Y) P, (double X, double Y) Q)>>();
        var usedTwin = new HashSet<int>();
        for (int hi = 0; hi < _halfedges.Count; hi++)
        {
            if (usedTwin.Contains(_halfedges[hi].Twin)) continue;
            usedTwin.Add(hi);
            int f1 = _halfedges[hi].Face;
            int f2 = _halfedges[_halfedges[hi].Twin].Face;
            if (f1 == f2) continue;
            var key = f1 < f2 ? (f1, f2) : (f2, f1);
            if (!pairs.TryGetValue(key, out var list))
            {
                list = new List<((double X, double Y) P, (double X, double Y) Q)>();
                pairs[key] = list;
            }
            list.Add((_vertices[_halfedges[hi].Src], _vertices[_halfedges[hi].Dst]));
        }
        var result = new List<(int, int, List<((double, double), (double, double))>)>();
        foreach (var kvp in pairs)
        {
            result.Add((kvp.Key.Item1, kvp.Key.Item2, kvp.Value));
        }
        return result;
    }
}
