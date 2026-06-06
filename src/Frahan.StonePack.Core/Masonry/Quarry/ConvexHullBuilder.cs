#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Quarry;

// =============================================================================
// ConvexHullBuilder — incremental 3D convex hull on a flat double[] point
// list. Used to bound a roughly-convex quarry-block scan with a single
// convex Slab when the user opts in to convex-hull approximation.
//
// Algorithm: classic incremental hull (Preparata & Shamos).
//   1. Find 4 non-coplanar seeds → initial tetrahedron.
//   2. For each remaining point P:
//        a. Mark faces visible from P (signed distance > eps).
//        b. Find horizon edges (boundary between visible and hidden faces).
//        c. Remove visible faces, add new triangular faces from each horizon
//           edge to P.
//
// Output: one convex Slab whose faces are triangulated. For a given input,
// the result is deterministic in the input ordering. Coplanar / collinear /
// fewer-than-4 inputs throw with a clear message.
//
// Complexity: O(N * F) per insertion where F is current face count; for
// "well-behaved" inputs F is O(N), giving O(N^2) total. Good enough for
// quarry block sizes (N typically < 100k vertices). Power-of-10 hardened
// in the same style as AshlarLayoutEngine.
// =============================================================================

public static class ConvexHullBuilder
{
    private const double DefaultEps = 1e-9;
    private const int MaxIterationGuard = 100000;

    /// <summary>
    /// Build the convex hull of <paramref name="points"/> (flat
    /// [x0,y0,z0,x1,y1,z1,...]) and wrap it as a <see cref="Slab"/>
    /// whose faces are triangles ordered CCW outward.
    /// </summary>
    public static Slab BuildSlab(IReadOnlyList<double> points, double eps = DefaultEps)
    {
        if (points == null) throw new ArgumentNullException(nameof(points));
        if (points.Count < 12 || points.Count % 3 != 0)
            throw new ArgumentException(
                $"need at least 4 points (12 coords); points length must be a multiple of 3 (got {points.Count})",
                nameof(points));
        if (eps < 0.0) throw new ArgumentOutOfRangeException(nameof(eps));

        int n = points.Count / 3;
        var px = new double[n];
        var py = new double[n];
        var pz = new double[n];
        for (int i = 0; i < n; i++)
        {
            px[i] = points[3 * i + 0];
            py[i] = points[3 * i + 1];
            pz[i] = points[3 * i + 2];
        }

        // ---- Seed tetrahedron ----------------------------------------------
        FindSeedTetra(px, py, pz, eps,
            out int s0, out int s1, out int s2, out int s3);

        var faces = new List<(int a, int b, int c)>(64);
        // Orient face (s0, s1, s2) so its normal points away from s3.
        if (SignedDistance(px[s3], py[s3], pz[s3],
                            px[s0], py[s0], pz[s0],
                            px[s1], py[s1], pz[s1],
                            px[s2], py[s2], pz[s2]) > 0)
        {
            // s3 is on +n side → flip so normal points -n away from s3.
            faces.Add((s0, s2, s1));
            faces.Add((s0, s1, s3));
            faces.Add((s1, s2, s3));
            faces.Add((s2, s0, s3));
        }
        else
        {
            faces.Add((s0, s1, s2));
            faces.Add((s0, s3, s1));
            faces.Add((s1, s3, s2));
            faces.Add((s2, s3, s0));
        }

        // Seed-set members are already on the hull; mark them processed.
        var processed = new bool[n];
        processed[s0] = processed[s1] = processed[s2] = processed[s3] = true;

        // ---- Incremental insertion -----------------------------------------
        int iters = 0;
        for (int p = 0; p < n; p++)
        {
            if (processed[p]) continue;
            iters += 1;
            if (iters > MaxIterationGuard)
                throw new InvalidOperationException("hull insertion guard tripped");

            InsertPoint(p, px, py, pz, faces, eps);
            processed[p] = true;
        }

        return BuildSlabFromFaces(faces, px, py, pz);
    }

    // ─── Seed tetrahedron ────────────────────────────────────────────────────

    private static void FindSeedTetra(
        double[] px, double[] py, double[] pz, double eps,
        out int s0, out int s1, out int s2, out int s3)
    {
        if (px == null) throw new ArgumentNullException(nameof(px));
        int n = px.Length;

        s0 = 0;
        // s1 = farthest from s0
        s1 = -1;
        double bestDist1 = 0.0;
        for (int i = 1; i < n; i++)
        {
            double d = Sq(px[i] - px[s0]) + Sq(py[i] - py[s0]) + Sq(pz[i] - pz[s0]);
            if (d > bestDist1)
            {
                bestDist1 = d;
                s1 = i;
            }
        }
        if (s1 < 0 || bestDist1 < eps * eps)
            throw new ArgumentException("input points are coincident", "points");

        // s2 = farthest from line (s0, s1)
        s2 = -1;
        double bestDist2 = 0.0;
        double dx01 = px[s1] - px[s0];
        double dy01 = py[s1] - py[s0];
        double dz01 = pz[s1] - pz[s0];
        for (int i = 0; i < n; i++)
        {
            if (i == s0 || i == s1) continue;
            double dx = px[i] - px[s0];
            double dy = py[i] - py[s0];
            double dz = pz[i] - pz[s0];
            double cx = dy01 * dz - dz01 * dy;
            double cy = dz01 * dx - dx01 * dz;
            double cz = dx01 * dy - dy01 * dx;
            double d = cx * cx + cy * cy + cz * cz;
            if (d > bestDist2)
            {
                bestDist2 = d;
                s2 = i;
            }
        }
        if (s2 < 0 || bestDist2 < eps * eps)
            throw new ArgumentException("input points are collinear", "points");

        // s3 = farthest from plane (s0, s1, s2)
        s3 = -1;
        double bestDist3 = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (i == s0 || i == s1 || i == s2) continue;
            double d = Math.Abs(SignedDistance(px[i], py[i], pz[i],
                                                px[s0], py[s0], pz[s0],
                                                px[s1], py[s1], pz[s1],
                                                px[s2], py[s2], pz[s2]));
            if (d > bestDist3)
            {
                bestDist3 = d;
                s3 = i;
            }
        }
        if (s3 < 0 || bestDist3 < eps)
            throw new ArgumentException("input points are coplanar", "points");
    }

    // ─── Insert one point ────────────────────────────────────────────────────

    private static void InsertPoint(
        int p, double[] px, double[] py, double[] pz,
        List<(int a, int b, int c)> faces, double eps)
    {
        // Find visible faces.
        var visible = new List<int>(faces.Count);
        for (int f = 0; f < faces.Count; f++)
        {
            var (a, b, c) = faces[f];
            double dist = SignedDistance(px[p], py[p], pz[p],
                                          px[a], py[a], pz[a],
                                          px[b], py[b], pz[b],
                                          px[c], py[c], pz[c]);
            if (dist > eps) visible.Add(f);
        }
        if (visible.Count == 0) return;  // p is inside; nothing to do.

        // Collect horizon edges: edges of visible faces NOT shared with another visible face.
        var visibleSet = new HashSet<int>(visible);
        var horizonEdges = new List<(int u, int v)>(visible.Count * 3);
        for (int vi = 0; vi < visible.Count; vi++)
        {
            int f = visible[vi];
            var (a, b, c) = faces[f];
            for (int e = 0; e < 3; e++)
            {
                int u = e == 0 ? a : (e == 1 ? b : c);
                int v = e == 0 ? b : (e == 1 ? c : a);
                if (!IsEdgeSharedWithVisible(u, v, faces, visibleSet, f))
                    horizonEdges.Add((u, v));
            }
        }
        if (horizonEdges.Count < 3)
            throw new InvalidOperationException("horizon has fewer than 3 edges");

        // Remove visible faces (descending index so removals are stable).
        visible.Sort();
        for (int i = visible.Count - 1; i >= 0; i--)
        {
            faces.RemoveAt(visible[i]);
        }

        // Add a new triangle for each horizon edge.
        for (int i = 0; i < horizonEdges.Count; i++)
        {
            var (u, v) = horizonEdges[i];
            faces.Add((u, v, p));
        }
    }

    private static bool IsEdgeSharedWithVisible(
        int u, int v, List<(int a, int b, int c)> faces, HashSet<int> visibleSet, int self)
    {
        for (int f = 0; f < faces.Count; f++)
        {
            if (f == self) continue;
            if (!visibleSet.Contains(f)) continue;
            var (a, b, c) = faces[f];
            if (HasEdge(a, b, c, u, v)) return true;
        }
        return false;
    }

    private static bool HasEdge(int a, int b, int c, int u, int v)
    {
        return (a == v && b == u) || (b == v && c == u) || (c == v && a == u)
            || (a == u && b == v) || (b == u && c == v) || (c == u && a == v);
    }

    // ─── Build Slab ──────────────────────────────────────────────────────────

    private static Slab BuildSlabFromFaces(
        List<(int a, int b, int c)> faces, double[] px, double[] py, double[] pz)
    {
        // Re-index to keep only used vertices.
        var globalToLocal = new Dictionary<int, int>();
        var verts = new List<double>();
        for (int f = 0; f < faces.Count; f++)
        {
            var (a, b, c) = faces[f];
            EnsureLocal(a, globalToLocal, verts, px, py, pz);
            EnsureLocal(b, globalToLocal, verts, px, py, pz);
            EnsureLocal(c, globalToLocal, verts, px, py, pz);
        }
        var slabFaces = new IReadOnlyList<int>[faces.Count];
        for (int f = 0; f < faces.Count; f++)
        {
            var (a, b, c) = faces[f];
            slabFaces[f] = new[] { globalToLocal[a], globalToLocal[b], globalToLocal[c] };
        }
        return new Slab(verts, slabFaces);
    }

    private static void EnsureLocal(
        int g, Dictionary<int, int> map, List<double> verts,
        double[] px, double[] py, double[] pz)
    {
        if (map.ContainsKey(g)) return;
        map[g] = verts.Count / 3;
        verts.Add(px[g]); verts.Add(py[g]); verts.Add(pz[g]);
    }

    // ─── Geometry helpers ────────────────────────────────────────────────────

    private static double SignedDistance(
        double px_, double py_, double pz_,
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz)
    {
        // n = (b-a) x (c-a), unnormalised.
        double e1x = bx - ax, e1y = by - ay, e1z = bz - az;
        double e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
        double nx = e1y * e2z - e1z * e2y;
        double ny = e1z * e2x - e1x * e2z;
        double nz = e1x * e2y - e1y * e2x;
        double m = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (m < 1e-20) return 0.0;
        return ((px_ - ax) * nx + (py_ - ay) * ny + (pz_ - az) * nz) / m;
    }

    private static double Sq(double x) => x * x;
}
