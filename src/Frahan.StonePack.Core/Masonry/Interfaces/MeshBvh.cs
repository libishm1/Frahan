#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Interfaces;

// =============================================================================
// MeshBvh — top-down median-split AABB Bounding Volume Hierarchy for fast
// closest-point-on-triangle queries against a mesh. Replaces the brute-force
// O(T) scan inside MeshContactDetector with O(log T) average-case query.
//
// Construction: O(N log N).
// Query:        O(log T) average (tight bounding boxes, well-distributed
//               triangles); worst case O(T) for adversarial inputs.
//
// References:
//   - Pharr, Jakob, Humphreys (2016). "Physically Based Rendering" §4.3 BVH.
//   - Wald (2007). "On fast Construction of SAH-based BVHs." (We use the
//     simpler median-split heuristic; SAH is a future optimisation.)
// =============================================================================

public sealed class MeshBvh
{
    private const int LeafTriThreshold = 4;        // Stop splitting at <= 4 tris/leaf.
    private const int MaxDepth = 64;               // Safety cap.

    private readonly IReadOnlyList<double> _verts;
    private readonly IReadOnlyList<int> _tris;
    private readonly int _triCount;
    private readonly int[] _triOrder;
    private readonly Node[] _nodes;
    private int _nodeCount;

    public int TriangleCount => _triCount;
    public int NodeCount => _nodeCount;

    private struct Node
    {
        public double MinX, MinY, MinZ;
        public double MaxX, MaxY, MaxZ;
        public int FirstTri;          // index into _triOrder (leaf only)
        public int TriCount;          // 0 → internal, >0 → leaf
        public int Left;              // index into _nodes (internal only)
        public int Right;             // index into _nodes (internal only)
    }

    public MeshBvh(IReadOnlyList<double> verts, IReadOnlyList<int> tris)
    {
        if (verts == null) throw new ArgumentNullException(nameof(verts));
        if (tris == null) throw new ArgumentNullException(nameof(tris));
        if (verts.Count % 3 != 0)
            throw new ArgumentException("verts length must be a multiple of 3");
        if (tris.Count % 3 != 0)
            throw new ArgumentException("tris length must be a multiple of 3");

        _verts = verts;
        _tris = tris;
        _triCount = tris.Count / 3;

        _triOrder = new int[_triCount];
        for (int i = 0; i < _triCount; i++) _triOrder[i] = i;

        // Pre-compute per-triangle AABB and centroid (used by build).
        var triBounds = new (double MinX, double MinY, double MinZ,
                              double MaxX, double MaxY, double MaxZ,
                              double CenX, double CenY, double CenZ)[_triCount];
        for (int t = 0; t < _triCount; t++)
        {
            int ia = tris[3 * t + 0], ib = tris[3 * t + 1], ic = tris[3 * t + 2];
            double ax = verts[3 * ia + 0], ay = verts[3 * ia + 1], az = verts[3 * ia + 2];
            double bx = verts[3 * ib + 0], by = verts[3 * ib + 1], bz = verts[3 * ib + 2];
            double cx = verts[3 * ic + 0], cy = verts[3 * ic + 1], cz = verts[3 * ic + 2];
            triBounds[t] = (
                Math.Min(ax, Math.Min(bx, cx)),
                Math.Min(ay, Math.Min(by, cy)),
                Math.Min(az, Math.Min(bz, cz)),
                Math.Max(ax, Math.Max(bx, cx)),
                Math.Max(ay, Math.Max(by, cy)),
                Math.Max(az, Math.Max(bz, cz)),
                (ax + bx + cx) / 3.0,
                (ay + by + cy) / 3.0,
                (az + bz + cz) / 3.0);
        }

        // Allocate nodes. Worst case for binary tree with N leaves and leaf
        // size L: ~2N/L nodes. We use 2N to be safe.
        _nodes = new Node[Math.Max(1, 2 * _triCount + 4)];
        _nodeCount = 0;
        Build(triBounds, 0, _triCount, 0);
    }

    // ─── Build (recursive median split) ─────────────────────────────────────

    private int Build(
        (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ,
         double CenX, double CenY, double CenZ)[] tb,
        int loIncl, int hiExcl, int depth)
    {
        int idx = _nodeCount;
        _nodeCount += 1;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        for (int i = loIncl; i < hiExcl; i++)
        {
            int t = _triOrder[i];
            if (tb[t].MinX < minX) minX = tb[t].MinX; if (tb[t].MaxX > maxX) maxX = tb[t].MaxX;
            if (tb[t].MinY < minY) minY = tb[t].MinY; if (tb[t].MaxY > maxY) maxY = tb[t].MaxY;
            if (tb[t].MinZ < minZ) minZ = tb[t].MinZ; if (tb[t].MaxZ > maxZ) maxZ = tb[t].MaxZ;
        }

        int span = hiExcl - loIncl;
        if (span <= LeafTriThreshold || depth >= MaxDepth)
        {
            _nodes[idx] = new Node
            {
                MinX = minX, MinY = minY, MinZ = minZ,
                MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
                FirstTri = loIncl, TriCount = span,
                Left = -1, Right = -1,
            };
            return idx;
        }

        // Choose split axis = longest extent of the centroid bounds.
        double cMinX = double.PositiveInfinity, cMinY = double.PositiveInfinity, cMinZ = double.PositiveInfinity;
        double cMaxX = double.NegativeInfinity, cMaxY = double.NegativeInfinity, cMaxZ = double.NegativeInfinity;
        for (int i = loIncl; i < hiExcl; i++)
        {
            int t = _triOrder[i];
            if (tb[t].CenX < cMinX) cMinX = tb[t].CenX; if (tb[t].CenX > cMaxX) cMaxX = tb[t].CenX;
            if (tb[t].CenY < cMinY) cMinY = tb[t].CenY; if (tb[t].CenY > cMaxY) cMaxY = tb[t].CenY;
            if (tb[t].CenZ < cMinZ) cMinZ = tb[t].CenZ; if (tb[t].CenZ > cMaxZ) cMaxZ = tb[t].CenZ;
        }
        double dx = cMaxX - cMinX, dy = cMaxY - cMinY, dz = cMaxZ - cMinZ;
        int axis = (dx >= dy && dx >= dz) ? 0 : (dy >= dz ? 1 : 2);

        // Median split (Hoare-style partition by centroid value).
        int mid = (loIncl + hiExcl) / 2;
        SortByCentroid(_triOrder, tb, loIncl, hiExcl, axis);

        // Build children, then patch this node.
        int leftIdx = Build(tb, loIncl, mid, depth + 1);
        int rightIdx = Build(tb, mid, hiExcl, depth + 1);

        _nodes[idx] = new Node
        {
            MinX = minX, MinY = minY, MinZ = minZ,
            MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
            FirstTri = -1, TriCount = 0,
            Left = leftIdx, Right = rightIdx,
        };
        return idx;
    }

    private static void SortByCentroid(
        int[] order,
        (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ,
         double CenX, double CenY, double CenZ)[] tb,
        int loIncl, int hiExcl, int axis)
    {
        // Selection of a small-ish slice; insertion sort is fine for typical
        // BVH leaf sizes. Switch to quicksort if profiling shows hotspot.
        Array.Sort(order, loIncl, hiExcl - loIncl, new CentroidComparer(tb, axis));
    }

    private sealed class CentroidComparer : IComparer<int>
    {
        private readonly (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ,
                           double CenX, double CenY, double CenZ)[] _tb;
        private readonly int _axis;
        public CentroidComparer(
            (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ,
             double CenX, double CenY, double CenZ)[] tb, int axis)
        {
            _tb = tb; _axis = axis;
        }
        public int Compare(int a, int b)
        {
            double ca = _axis == 0 ? _tb[a].CenX : (_axis == 1 ? _tb[a].CenY : _tb[a].CenZ);
            double cb = _axis == 0 ? _tb[b].CenX : (_axis == 1 ? _tb[b].CenY : _tb[b].CenZ);
            return ca.CompareTo(cb);
        }
    }

    // ─── Closest-point query ───────────────────────────────────────────────

    /// <summary>
    /// Closest point on the mesh to (px, py, pz). Returns the world-space
    /// closest point, the source triangle index, and the distance. If
    /// <paramref name="maxDistance"/> is finite and no triangle is within
    /// it, returns triIdx = -1.
    /// </summary>
    public bool ClosestPoint(
        double px, double py, double pz, double maxDistance,
        out double cx, out double cy, out double cz, out int triIdx, out double distance)
    {
        cx = cy = cz = 0.0;
        triIdx = -1;
        distance = double.PositiveInfinity;
        if (_nodeCount == 0) return false;

        double bestD2 = maxDistance * maxDistance;
        // Stack-based traversal to avoid recursion overhead.
        var stack = new int[64];
        int top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            int idx = stack[--top];
            ref Node n = ref _nodes[idx];
            double aabbD2 = AabbDistanceSquared(px, py, pz,
                n.MinX, n.MinY, n.MinZ, n.MaxX, n.MaxY, n.MaxZ);
            if (aabbD2 > bestD2) continue;

            if (n.TriCount > 0)
            {
                // Leaf: test each triangle.
                for (int i = 0; i < n.TriCount; i++)
                {
                    int t = _triOrder[n.FirstTri + i];
                    int ia = _tris[3 * t + 0], ib = _tris[3 * t + 1], ic = _tris[3 * t + 2];
                    ClosestPointOnTriangle(px, py, pz,
                        _verts[3 * ia + 0], _verts[3 * ia + 1], _verts[3 * ia + 2],
                        _verts[3 * ib + 0], _verts[3 * ib + 1], _verts[3 * ib + 2],
                        _verts[3 * ic + 0], _verts[3 * ic + 1], _verts[3 * ic + 2],
                        out double qx, out double qy, out double qz);
                    double dx = px - qx, dy = py - qy, dz = pz - qz;
                    double d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        cx = qx; cy = qy; cz = qz;
                        triIdx = t;
                    }
                }
            }
            else
            {
                // Internal: descend nearer child first for tighter pruning.
                int left = n.Left, right = n.Right;
                double leftD2 = AabbDistanceSquared(px, py, pz,
                    _nodes[left].MinX, _nodes[left].MinY, _nodes[left].MinZ,
                    _nodes[left].MaxX, _nodes[left].MaxY, _nodes[left].MaxZ);
                double rightD2 = AabbDistanceSquared(px, py, pz,
                    _nodes[right].MinX, _nodes[right].MinY, _nodes[right].MinZ,
                    _nodes[right].MaxX, _nodes[right].MaxY, _nodes[right].MaxZ);
                if (leftD2 <= rightD2)
                {
                    if (rightD2 < bestD2) stack[top++] = right;
                    if (leftD2  < bestD2) stack[top++] = left;
                }
                else
                {
                    if (leftD2  < bestD2) stack[top++] = left;
                    if (rightD2 < bestD2) stack[top++] = right;
                }
            }
        }
        if (triIdx < 0) return false;
        distance = Math.Sqrt(bestD2);
        return true;
    }

    // ─── Geometry helpers ──────────────────────────────────────────────────

    private static double AabbDistanceSquared(
        double px, double py, double pz,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        double dx = px < minX ? minX - px : (px > maxX ? px - maxX : 0.0);
        double dy = py < minY ? minY - py : (py > maxY ? py - maxY : 0.0);
        double dz = pz < minZ ? minZ - pz : (pz > maxZ ? pz - maxZ : 0.0);
        return dx * dx + dy * dy + dz * dz;
    }

    private static void ClosestPointOnTriangle(
        double px, double py, double pz,
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz,
        out double qx, out double qy, out double qz)
    {
        double abx = bx - ax, aby = by - ay, abz = bz - az;
        double acx = cx - ax, acy = cy - ay, acz = cz - az;
        double apx = px - ax, apy = py - ay, apz = pz - az;
        double d1 = abx * apx + aby * apy + abz * apz;
        double d2 = acx * apx + acy * apy + acz * apz;
        if (d1 <= 0 && d2 <= 0) { qx = ax; qy = ay; qz = az; return; }

        double bpx = px - bx, bpy = py - by, bpz = pz - bz;
        double d3 = abx * bpx + aby * bpy + abz * bpz;
        double d4 = acx * bpx + acy * bpy + acz * bpz;
        if (d3 >= 0 && d4 <= d3) { qx = bx; qy = by; qz = bz; return; }

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double t = d1 / (d1 - d3);
            qx = ax + t * abx; qy = ay + t * aby; qz = az + t * abz;
            return;
        }

        double cpx = px - cx, cpy = py - cy, cpz = pz - cz;
        double d5 = abx * cpx + aby * cpy + abz * cpz;
        double d6 = acx * cpx + acy * cpy + acz * cpz;
        if (d6 >= 0 && d5 <= d6) { qx = cx; qy = cy; qz = cz; return; }

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double t = d2 / (d2 - d6);
            qx = ax + t * acx; qy = ay + t * acy; qz = az + t * acz;
            return;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            double t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            qx = bx + t * (cx - bx); qy = by + t * (cy - by); qz = bz + t * (cz - bz);
            return;
        }

        double denom = 1.0 / (va + vb + vc);
        double v_ = vb * denom;
        double w_ = vc * denom;
        qx = ax + v_ * abx + w_ * acx;
        qy = ay + v_ * aby + w_ * acy;
        qz = az + v_ * abz + w_ * acz;
    }
}
