#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// MeshSanitizer — analyse + (optionally) repair a MeshSnapshot.
//
// Analyse: single pass over triangles building an edge map. Reports manifold
// status, closure, normal consistency, edge length stats, area, volume.
// O(T log T) typical (sort the unique edges to compute the median).
//
// Sanitize: produces a corrected MeshSnapshot. Three orthogonal options:
//   • DedupVertices  — collapse vertices closer than the dedup tolerance
//                      onto a single representative; remap triangle indices.
//   • DropDegenerate — remove triangles with area below the degeneracy
//                      threshold (area < degenerateAreaTol).
//   • UnifyNormals   — orient all triangles consistently using a flood-fill
//                      across edge adjacency. Picks the largest connected
//                      component's "majority" winding.
//
// Pure-managed, runtime-agnostic: same flat double[]/int[] convention as
// MeshSnapshot. Algorithms can call `Analyse` cheaply on inputs and decide
// based on the report whether to call `Sanitize` (with options) before
// running their own logic.
// =============================================================================

public sealed class SanitizeOptions
{
    public bool DedupVertices = false;
    public bool DropDegenerate = false;
    public bool UnifyNormals = false;
    public double DedupTolerance = 1e-9;
    public double DegenerateAreaTol = 1e-12;
}

public sealed class SanitizeResult
{
    public SanitizeResult(MeshSnapshot mesh, MeshQualityReport before, MeshQualityReport after,
        int verticesMerged, int trianglesDropped, int trianglesFlipped)
    {
        Mesh = mesh;
        Before = before;
        After = after;
        VerticesMerged = verticesMerged;
        TrianglesDropped = trianglesDropped;
        TrianglesFlipped = trianglesFlipped;
    }

    public MeshSnapshot Mesh { get; }
    public MeshQualityReport Before { get; }
    public MeshQualityReport After { get; }
    public int VerticesMerged { get; }
    public int TrianglesDropped { get; }
    public int TrianglesFlipped { get; }
}

public static class MeshSanitizer
{
    private const double DefaultDedupTol = 1e-9;
    private const double DefaultDegenerateAreaTol = 1e-12;

    /// <summary>
    /// Compute a quality report without modifying the mesh.
    /// </summary>
    public static MeshQualityReport Analyse(
        MeshSnapshot mesh,
        double dedupTolerance = DefaultDedupTol,
        double degenerateAreaTol = DefaultDegenerateAreaTol)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        return Analyse(mesh.VertexCoordsXyz, mesh.TriangleIndices,
            dedupTolerance, degenerateAreaTol);
    }

    public static MeshQualityReport Analyse(
        IReadOnlyList<double> verts, IReadOnlyList<int> tris,
        double dedupTolerance = DefaultDedupTol,
        double degenerateAreaTol = DefaultDegenerateAreaTol)
    {
        if (verts == null) throw new ArgumentNullException(nameof(verts));
        if (tris == null) throw new ArgumentNullException(nameof(tris));

        int v = verts.Count / 3;
        int t = tris.Count / 3;

        // ── Duplicate vertices ────────────────────────────────────────────
        // O(V²) worst-case but guarded with a coarse hash bucket on Math.Floor
        // to keep typical cost ~O(V).
        int dupCount = 0;
        if (v > 1)
        {
            double cell = Math.Max(dedupTolerance * 2.0, 1e-12);
            var buckets = new Dictionary<long, List<int>>(v);
            for (int i = 0; i < v; i++)
            {
                long key = HashKey(verts[3 * i + 0], verts[3 * i + 1], verts[3 * i + 2], cell);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(2);
                    buckets[key] = list;
                }
                else
                {
                    for (int k = 0; k < list.Count; k++)
                    {
                        int j = list[k];
                        double dx = verts[3 * i + 0] - verts[3 * j + 0];
                        double dy = verts[3 * i + 1] - verts[3 * j + 1];
                        double dz = verts[3 * i + 2] - verts[3 * j + 2];
                        if (dx * dx + dy * dy + dz * dz <= dedupTolerance * dedupTolerance)
                        { dupCount++; break; }
                    }
                }
                list.Add(i);
            }
        }

        // ── Triangle areas + degenerate count ─────────────────────────────
        double surfaceArea = 0.0;
        int degenerate = 0;
        for (int i = 0; i < t; i++)
        {
            int ia = tris[3 * i + 0], ib = tris[3 * i + 1], ic = tris[3 * i + 2];
            double a = TriArea(verts, ia, ib, ic);
            if (a < degenerateAreaTol) degenerate++;
            surfaceArea += a;
        }

        // ── Signed volume via divergence theorem ──────────────────────────
        double volSum = 0.0;
        for (int i = 0; i < t; i++)
        {
            int ia = tris[3 * i + 0], ib = tris[3 * i + 1], ic = tris[3 * i + 2];
            double ax = verts[3 * ia + 0], ay = verts[3 * ia + 1], az = verts[3 * ia + 2];
            double bx = verts[3 * ib + 0], by = verts[3 * ib + 1], bz = verts[3 * ib + 2];
            double cx = verts[3 * ic + 0], cy = verts[3 * ic + 1], cz = verts[3 * ic + 2];
            // (a · (b × c)) / 6
            double crossX = by * cz - bz * cy;
            double crossY = bz * cx - bx * cz;
            double crossZ = bx * cy - by * cx;
            volSum += ax * crossX + ay * crossY + az * crossZ;
        }
        double signedVolume = volSum / 6.0;

        // ── Edge map: directed edge → list of triangles using it. ─────────
        // Canonical edge key = (min, max). Directed edges are tracked
        // separately so we can detect normal-flip pairs (two triangles
        // sharing an edge with the SAME direction).
        var edgeUse = new Dictionary<long, EdgeRecord>(t * 3);
        var edgeLengths = new List<double>(t * 3);

        int normalInconsist = 0;
        for (int ti = 0; ti < t; ti++)
        {
            int ia = tris[3 * ti + 0], ib = tris[3 * ti + 1], ic = tris[3 * ti + 2];
            ProcessEdge(edgeUse, ia, ib, ti, ref normalInconsist, edgeLengths, verts);
            ProcessEdge(edgeUse, ib, ic, ti, ref normalInconsist, edgeLengths, verts);
            ProcessEdge(edgeUse, ic, ia, ti, ref normalInconsist, edgeLengths, verts);
        }

        int boundary = 0, nonManifold = 0;
        foreach (var rec in edgeUse.Values)
        {
            if (rec.Count == 1) boundary++;
            else if (rec.Count > 2) nonManifold++;
        }

        bool isManifold = nonManifold == 0;
        bool isClosed = isManifold && boundary == 0;
        bool consistentNormals = normalInconsist == 0;

        // ── Edge-length statistics ────────────────────────────────────────
        double minLen = 0, maxLen = 0, meanLen = 0, medianLen = 0;
        if (edgeLengths.Count > 0)
        {
            edgeLengths.Sort();
            minLen = edgeLengths[0];
            maxLen = edgeLengths[edgeLengths.Count - 1];
            double sum = 0;
            for (int i = 0; i < edgeLengths.Count; i++) sum += edgeLengths[i];
            meanLen = sum / edgeLengths.Count;
            medianLen = edgeLengths[edgeLengths.Count / 2];
        }

        return new MeshQualityReport(
            vertexCount: v,
            triangleCount: t,
            duplicateVertexCount: dupCount,
            degenerateTriangleCount: degenerate,
            boundaryEdgeCount: boundary,
            nonManifoldEdgeCount: nonManifold,
            normalInconsistencyCount: normalInconsist,
            isManifold: isManifold,
            isClosed: isClosed,
            hasConsistentNormals: consistentNormals,
            minEdgeLength: minLen,
            maxEdgeLength: maxLen,
            meanEdgeLength: meanLen,
            medianEdgeLength: medianLen,
            surfaceArea: surfaceArea,
            signedVolume: signedVolume);
    }

    /// <summary>
    /// Apply the requested repairs and return a fresh MeshSnapshot plus
    /// before / after quality reports.
    /// </summary>
    public static SanitizeResult Sanitize(MeshSnapshot mesh, SanitizeOptions options)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var before = Analyse(mesh, options.DedupTolerance, options.DegenerateAreaTol);

        // Working copies.
        var verts = new List<double>(mesh.VertexCoordsXyz);
        var tris = new List<int>(mesh.TriangleIndices);
        int verticesMerged = 0;
        int trianglesDropped = 0;
        int trianglesFlipped = 0;

        if (options.DedupVertices)
            verticesMerged = DedupVerticesInPlace(verts, tris, options.DedupTolerance);

        if (options.DropDegenerate)
            trianglesDropped = DropDegenerateInPlace(verts, tris, options.DegenerateAreaTol);

        if (options.UnifyNormals)
            trianglesFlipped = UnifyNormalsInPlace(verts, tris);

        var sanitised = new MeshSnapshot(verts, tris);
        var after = Analyse(sanitised, options.DedupTolerance, options.DegenerateAreaTol);
        return new SanitizeResult(sanitised, before, after,
            verticesMerged, trianglesDropped, trianglesFlipped);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private struct EdgeRecord
    {
        public int Count;
        // Directed-edge counts. Used to detect inconsistent windings: when
        // two triangles share the same canonical edge in the same direction
        // (rather than opposite), they have inconsistent normals.
        public int Forward;   // canonical (min → max)
        public int Backward;  // canonical (max → min)
    }

    private static void ProcessEdge(
        Dictionary<long, EdgeRecord> edgeUse,
        int a, int b, int triIdx, ref int normalInconsist,
        List<double> edgeLengths, IReadOnlyList<double> verts)
    {
        long key;
        bool forward;
        if (a < b) { key = ((long)a << 32) | (uint)b; forward = true; }
        else       { key = ((long)b << 32) | (uint)a; forward = false; }

        if (!edgeUse.TryGetValue(key, out var rec))
        {
            rec.Count = 0; rec.Forward = 0; rec.Backward = 0;
            // Compute length only on first encounter.
            double dx = verts[3 * a + 0] - verts[3 * b + 0];
            double dy = verts[3 * a + 1] - verts[3 * b + 1];
            double dz = verts[3 * a + 2] - verts[3 * b + 2];
            edgeLengths.Add(Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        rec.Count++;
        if (forward) rec.Forward++; else rec.Backward++;
        // Inconsistent winding: a manifold-pair edge should be (Forward=1,
        // Backward=1). If both directed counts go above 1 we have a
        // consistency problem, OR a non-manifold edge (caller already counts
        // those separately, but normal-consistency cares about pairs only).
        if (rec.Count == 2 && (rec.Forward == 2 || rec.Backward == 2))
            normalInconsist++;
        edgeUse[key] = rec;
    }

    private static double TriArea(IReadOnlyList<double> v, int ia, int ib, int ic)
    {
        double ax = v[3 * ia + 0], ay = v[3 * ia + 1], az = v[3 * ia + 2];
        double bx = v[3 * ib + 0], by = v[3 * ib + 1], bz = v[3 * ib + 2];
        double cx = v[3 * ic + 0], cy = v[3 * ic + 1], cz = v[3 * ic + 2];
        double ex = bx - ax, ey = by - ay, ez = bz - az;
        double fx = cx - ax, fy = cy - ay, fz = cz - az;
        double nx = ey * fz - ez * fy;
        double ny = ez * fx - ex * fz;
        double nz = ex * fy - ey * fx;
        return 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
    }

    private static long HashKey(double x, double y, double z, double cell)
    {
        long ix = (long)Math.Floor(x / cell);
        long iy = (long)Math.Floor(y / cell);
        long iz = (long)Math.Floor(z / cell);
        unchecked
        {
            const long mask = (1L << 21) - 1;
            long ux = (ix + (1L << 20)) & mask;
            long uy = (iy + (1L << 20)) & mask;
            long uz = (iz + (1L << 20)) & mask;
            return (ux << 42) | (uy << 21) | uz;
        }
    }

    // ─── Repair operations ──────────────────────────────────────────────

    private static int DedupVerticesInPlace(
        List<double> verts, List<int> tris, double tol)
    {
        int v = verts.Count / 3;
        if (v < 2) return 0;
        var remap = new int[v];
        for (int i = 0; i < v; i++) remap[i] = i;

        double cell = Math.Max(tol * 2.0, 1e-12);
        var buckets = new Dictionary<long, List<int>>(v);
        int merged = 0;
        for (int i = 0; i < v; i++)
        {
            long key = HashKey(verts[3 * i + 0], verts[3 * i + 1], verts[3 * i + 2], cell);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                buckets[key] = list;
            }
            int found = -1;
            for (int k = 0; k < list.Count; k++)
            {
                int j = list[k];
                double dx = verts[3 * i + 0] - verts[3 * j + 0];
                double dy = verts[3 * i + 1] - verts[3 * j + 1];
                double dz = verts[3 * i + 2] - verts[3 * j + 2];
                if (dx * dx + dy * dy + dz * dz <= tol * tol) { found = j; break; }
            }
            if (found >= 0) { remap[i] = found; merged++; }
            else list.Add(i);
        }

        // Compact: build new vertex list with kept indices, build full remap.
        var keep = new int[v];
        for (int i = 0; i < v; i++) keep[i] = -1;
        var newVerts = new List<double>(v * 3);
        int next = 0;
        for (int i = 0; i < v; i++)
        {
            int rep = remap[i];
            if (keep[rep] < 0)
            {
                keep[rep] = next++;
                newVerts.Add(verts[3 * rep + 0]);
                newVerts.Add(verts[3 * rep + 1]);
                newVerts.Add(verts[3 * rep + 2]);
            }
        }
        verts.Clear();
        verts.AddRange(newVerts);

        for (int i = 0; i < tris.Count; i++)
            tris[i] = keep[remap[tris[i]]];
        return merged;
    }

    private static int DropDegenerateInPlace(
        List<double> verts, List<int> tris, double areaTol)
    {
        int t = tris.Count / 3;
        var keep = new List<int>(tris.Count);
        int dropped = 0;
        for (int i = 0; i < t; i++)
        {
            int ia = tris[3 * i + 0], ib = tris[3 * i + 1], ic = tris[3 * i + 2];
            if (ia == ib || ib == ic || ia == ic) { dropped++; continue; }
            double a = TriArea(verts, ia, ib, ic);
            if (a < areaTol) { dropped++; continue; }
            keep.Add(ia); keep.Add(ib); keep.Add(ic);
        }
        tris.Clear();
        tris.AddRange(keep);
        return dropped;
    }

    private static int UnifyNormalsInPlace(List<double> verts, List<int> tris)
    {
        int t = tris.Count / 3;
        if (t == 0) return 0;

        // Undirected edge → list of incident triangles. We need ALL incidents
        // because two same-direction triangles share an edge in the *same*
        // direction; a directed-only map would be blind to one of them.
        var edgeToTris = new Dictionary<long, List<int>>(t * 3);
        for (int i = 0; i < t; i++)
        {
            int ia = tris[3 * i + 0], ib = tris[3 * i + 1], ic = tris[3 * i + 2];
            AddIncidence(edgeToTris, ia, ib, i);
            AddIncidence(edgeToTris, ib, ic, i);
            AddIncidence(edgeToTris, ic, ia, i);
        }

        var visited = new bool[t];
        var queue = new Queue<int>();
        int flipped = 0;
        for (int seed = 0; seed < t; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                int ti = queue.Dequeue();
                int ia = tris[3 * ti + 0], ib = tris[3 * ti + 1], ic = tris[3 * ti + 2];
                AlignNeighbours(edgeToTris, tris, visited, queue, ti, ia, ib, ref flipped);
                AlignNeighbours(edgeToTris, tris, visited, queue, ti, ib, ic, ref flipped);
                AlignNeighbours(edgeToTris, tris, visited, queue, ti, ic, ia, ref flipped);
            }
        }
        return flipped;
    }

    private static void AddIncidence(Dictionary<long, List<int>> map, int a, int b, int ti)
    {
        long key = a < b
            ? ((long)a << 32) | (uint)b
            : ((long)b << 32) | (uint)a;
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<int>(2);
            map[key] = list;
        }
        list.Add(ti);
    }

    private static void AlignNeighbours(
        Dictionary<long, List<int>> map,
        List<int> tris, bool[] visited, Queue<int> queue,
        int ti, int a, int b, ref int flipped)
    {
        long key = a < b
            ? ((long)a << 32) | (uint)b
            : ((long)b << 32) | (uint)a;
        if (!map.TryGetValue(key, out var list)) return;
        for (int k = 0; k < list.Count; k++)
        {
            int n = list[k];
            if (n == ti || visited[n]) continue;
            // Determine the neighbour's winding on this edge. Consistent
            // when neighbour traverses (b → a) — opposite of our (a → b).
            if (!UsesDirectedEdge(tris, n, b, a))
            {
                // Neighbour uses (a → b) in same direction → flip it.
                int t0 = tris[3 * n + 0], t1 = tris[3 * n + 1];
                tris[3 * n + 0] = t1;
                tris[3 * n + 1] = t0;
                flipped++;
            }
            visited[n] = true;
            queue.Enqueue(n);
        }
    }

    private static bool UsesDirectedEdge(List<int> tris, int triIdx, int from, int to)
    {
        int a = tris[3 * triIdx + 0], b = tris[3 * triIdx + 1], c = tris[3 * triIdx + 2];
        return (a == from && b == to)
            || (b == from && c == to)
            || (c == from && a == to);
    }
}
