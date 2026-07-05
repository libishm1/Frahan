#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // SeamCut — Stage B2 of the thrust-following remesher. A field-aligned
    // parametrization (Stage B1) is only single-valued on a topological DISK.
    // A real vault surface with openings (e.g. the Park Güell portico: 8 boundary
    // loops = 1 outer + 7 light-wells, Euler χ = -6) is multiply-connected, so a
    // single-chart Poisson solve FOLDS (measured: 1835 flipped triangles, residual
    // 0.35 on the actual vault). This cuts the mesh to a disk first:
    //
    //   1. find all boundary loops; take the largest as the root.
    //   2. connect every other loop (hole) to the growing cut set by the shortest
    //      edge-path (multi-source Dijkstra) -> (b-1) seam paths.
    //   3. open the mesh along those seams by splitting each seam vertex into one
    //      copy per face-wedge (fan bounded by cut/boundary edges).
    //
    // The result is a disk (χ=1, one boundary loop) with the SAME 3D geometry
    // (duplicated vertices share positions), so the existing single-chart pipeline
    // (ThrustField.Refine -> FieldAlignedParam -> QuadExtract, C# or native) runs
    // unchanged, and the extracted quads are seamless in 3D. NewToOld transfers
    // per-vertex attributes (the thrust field) to the duplicates.
    // Spec: outputs/2026-06-30/thrust_remesh/HANDOFF_IMPLEMENTATION.md §3c (Tier-1).
    // =========================================================================
    public sealed class SeamCutResult
    {
        public Mesh Disk;                       // cut mesh (topological disk)
        public int[] NewToOld;                  // disk-vertex index -> original-vertex index
        public int BoundaryLoopsBefore;
        public int SeamPathCount;               // = BoundaryLoopsBefore - 1
        public List<Polyline> SeamPolylines = new List<Polyline>();
        public bool AlreadyDisk;
        public string Message = "";
    }

    public static class SeamCut
    {
        public static SeamCutResult CutToDisk(Mesh mesh)
        {
            var m = mesh.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            m.Vertices.CombineIdentical(true, true);
            m.Weld(Math.PI);
            m.Compact();

            int nv = m.Vertices.Count;
            var P = new Point3d[nv];
            for (int i = 0; i < nv; i++) P[i] = m.Vertices[i];
            int nf = m.Faces.Count;
            var tri = new int[nf][];
            for (int f = 0; f < nf; f++) { var mf = m.Faces[f]; tri[f] = new[] { mf.A, mf.B, mf.C }; }

            // ---- edge -> faces, vertex -> faces, vertex adjacency -----------------
            var edgeFaces = new Dictionary<long, List<int>>();
            var vFaces = new List<int>[nv];
            for (int i = 0; i < nv; i++) vFaces[i] = new List<int>();
            for (int f = 0; f < nf; f++)
            {
                var t = tri[f];
                for (int e = 0; e < 3; e++)
                {
                    int a = t[e], b = t[(e + 1) % 3];
                    long k = Key(a, b);
                    if (!edgeFaces.TryGetValue(k, out var l)) { l = new List<int>(); edgeFaces[k] = l; }
                    l.Add(f);
                }
                vFaces[t[0]].Add(f); vFaces[t[1]].Add(f); vFaces[t[2]].Add(f);
            }
            var adj = new List<int>[nv];
            for (int i = 0; i < nv; i++) adj[i] = new List<int>();
            foreach (var kv in edgeFaces) { Unkey(kv.Key, out int a, out int b); adj[a].Add(b); adj[b].Add(a); }

            // ---- boundary loops ---------------------------------------------------
            var loops = BoundaryLoops(edgeFaces, adj, nv);
            var res = new SeamCutResult { BoundaryLoopsBefore = loops.Count, NewToOld = null };
            if (loops.Count <= 1)
            {
                res.Disk = m; res.AlreadyDisk = true; res.SeamPathCount = 0;
                res.NewToOld = Identity(nv);
                res.Message = loops.Count == 1 ? "already a disk (1 boundary loop)" : "closed mesh (no boundary) — cannot cut to disk here";
                return res;
            }

            // pick the largest loop (bbox diagonal) as the root
            int root = 0; double best = -1;
            for (int i = 0; i < loops.Count; i++)
            {
                var bb = BoundingBox.Empty; foreach (int v in loops[i]) bb.Union(P[v]);
                double d = bb.Diagonal.Length; if (d > best) { best = d; root = i; }
            }

            var cutEdges = new HashSet<long>();
            var inCut = new bool[nv];
            foreach (int v in loops[root]) inCut[v] = true;
            var pending = new List<int>();
            for (int i = 0; i < loops.Count; i++) if (i != root) pending.Add(i);
            var loopOf = new int[nv]; for (int i = 0; i < nv; i++) loopOf[i] = -1;
            for (int i = 0; i < loops.Count; i++) foreach (int v in loops[i]) loopOf[v] = i;

            // ---- connect each hole to the cut set by shortest edge-path -----------
            var remaining = new HashSet<int>(pending);
            while (remaining.Count > 0)
            {
                // multi-source Dijkstra from all current cut vertices; stop at the
                // first vertex belonging to a still-pending loop.
                int hit = Dijkstra(P, adj, inCut, loopOf, remaining, out int[] prev);
                if (hit < 0) break; // disconnected component — leave as is
                int targetLoop = loopOf[hit];
                // trace path hit -> cut set
                var path = new List<int>();
                int cur = hit;
                while (cur >= 0 && !inCut[cur]) { path.Add(cur); cur = prev[cur]; }
                if (cur >= 0) path.Add(cur); // the cut-set anchor
                var pl = new Polyline();
                for (int i = 0; i < path.Count; i++)
                {
                    int v = path[i]; pl.Add(P[v]);
                    if (i + 1 < path.Count) cutEdges.Add(Key(path[i], path[i + 1]));
                }
                res.SeamPolylines.Add(pl);
                foreach (int v in path) inCut[v] = true;
                foreach (int v in loops[targetLoop]) inCut[v] = true; // whole hole now anchored
                remaining.Remove(targetLoop);
            }
            res.SeamPathCount = res.SeamPolylines.Count;

            // ---- open the mesh: split each vertex into face-wedges ----------------
            // union-find over incident faces; join two faces that share a NON-cut,
            // interior edge at the vertex. Each component -> one vertex copy.
            var newPos = new List<Point3d>(P);
            var newToOld = new List<int>(nv);
            for (int i = 0; i < nv; i++) newToOld.Add(i);
            // per (vertex, face) -> assigned vertex index
            var cornerVert = new Dictionary<long, int>();

            for (int v = 0; v < nv; v++)
            {
                var faces = vFaces[v];
                if (faces.Count == 0) continue;
                var uf = new Dictionary<int, int>();
                foreach (int f in faces) uf[f] = f;
                Func<int, int> find = null;
                find = x => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };
                // for each edge at v, union its two faces if the edge is interior & not cut
                var seen = new HashSet<int>();
                foreach (int f in faces)
                {
                    var t = tri[f];
                    for (int e = 0; e < 3; e++)
                    {
                        int a = t[e], b = t[(e + 1) % 3];
                        if (a != v && b != v) continue;
                        int w = (a == v) ? b : a;
                        long k = Key(v, w);
                        if (cutEdges.Contains(k)) continue;
                        var fl = edgeFaces[k];
                        if (fl.Count != 2) continue; // boundary edge splits the fan
                        int fa = find(fl[0]), fb = find(fl[1]); if (fa != fb) uf[fa] = fb;
                    }
                }
                // components
                var comp = new Dictionary<int, int>(); // root -> assigned vertex idx
                bool first = true;
                foreach (int f in faces)
                {
                    int r = find(f);
                    if (!comp.TryGetValue(r, out int vi))
                    {
                        if (first) { vi = v; first = false; }
                        else { vi = newPos.Count; newPos.Add(P[v]); newToOld.Add(v); }
                        comp[r] = vi;
                    }
                    cornerVert[((long)v << 32) | (uint)f] = vi;
                }
            }

            // rebuild mesh with rewired corners
            var disk = new Mesh();
            foreach (var p in newPos) disk.Vertices.Add(p);
            for (int f = 0; f < nf; f++)
            {
                var t = tri[f];
                int a = cornerVert[((long)t[0] << 32) | (uint)f];
                int b = cornerVert[((long)t[1] << 32) | (uint)f];
                int c = cornerVert[((long)t[2] << 32) | (uint)f];
                disk.Faces.AddFace(a, b, c);
            }
            disk.Compact();
            disk.Normals.ComputeNormals();

            res.Disk = disk;
            res.NewToOld = newToOld.ToArray();
            res.Message = $"cut {res.SeamPathCount} seam(s); {loops.Count} loops -> disk; verts {nv} -> {disk.Vertices.Count}";
            return res;
        }

        // ---- helpers ----------------------------------------------------------
        private static int[] Identity(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = i; return a; }
        private static long Key(int a, int b) { int lo = Math.Min(a, b), hi = Math.Max(a, b); return ((long)lo << 32) | (uint)hi; }
        private static void Unkey(long k, out int a, out int b) { a = (int)(k >> 32); b = (int)(k & 0xffffffff); }

        private static List<List<int>> BoundaryLoops(Dictionary<long, List<int>> edgeFaces, List<int>[] adj, int nv)
        {
            // boundary vertex adjacency = naked edges only
            var bAdj = new Dictionary<int, List<int>>();
            foreach (var kv in edgeFaces)
            {
                if (kv.Value.Count != 1) continue;
                Unkey(kv.Key, out int a, out int b);
                if (!bAdj.ContainsKey(a)) bAdj[a] = new List<int>();
                if (!bAdj.ContainsKey(b)) bAdj[b] = new List<int>();
                bAdj[a].Add(b); bAdj[b].Add(a);
            }
            var seen = new HashSet<int>();
            var loops = new List<List<int>>();
            foreach (var kv in bAdj)
            {
                if (seen.Contains(kv.Key)) continue;
                var loop = new List<int>(); var st = new Stack<int>(); st.Push(kv.Key);
                while (st.Count > 0) { int v = st.Pop(); if (!seen.Add(v)) continue; loop.Add(v); foreach (int w in bAdj[v]) if (!seen.Contains(w)) st.Push(w); }
                loops.Add(loop);
            }
            return loops;
        }

        // multi-source Dijkstra from all inCut vertices; returns the first reached
        // vertex that belongs to a still-pending loop (or -1). prev traces the path.
        private static int Dijkstra(Point3d[] P, List<int>[] adj, bool[] inCut, int[] loopOf,
                                    HashSet<int> remaining, out int[] prev)
        {
            int nv = P.Length;
            var dist = new double[nv]; prev = new int[nv];
            for (int i = 0; i < nv; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
            var pq = new SortedSet<(double d, int v)>();
            for (int v = 0; v < nv; v++) if (inCut[v]) { dist[v] = 0; pq.Add((0, v)); }
            while (pq.Count > 0)
            {
                var top = pq.Min; pq.Remove(top);
                int u = top.v; double du = top.d;
                if (du > dist[u]) continue;
                if (!inCut[u] && loopOf[u] >= 0 && remaining.Contains(loopOf[u])) return u;
                foreach (int w in adj[u])
                {
                    double nd = du + P[u].DistanceTo(P[w]);
                    if (nd < dist[w]) { pq.Remove((dist[w], w)); dist[w] = nd; prev[w] = u; pq.Add((nd, w)); }
                }
            }
            return -1;
        }
    }
}
