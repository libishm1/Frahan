#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// TriangleAabbBvh -- flat-array bounding volume hierarchy over the triangles
// of a PlyMesh. Built once per fracture mesh; reused for every candidate OBB
// in the brute-force search.
//
// Purpose: replace the O(blocks * triangles) inner loop of BlockCutOptSolver
// with O(blocks * log(triangles)) by pruning triangles whose AABB does not
// overlap the candidate OBB's world-AABB.
//
// Algorithm: top-down construction by median split on the longest axis. Each
// internal node stores its AABB plus indices of its left and right children;
// each leaf stores its AABB plus a contiguous range of triangle indices in
// a permutation array.
//
// Build complexity: O(T log T). Query complexity: O(log T + k) for k hits.
//
// This is improvement I2 in
// `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md`
// (Phase 2 of the implementation roadmap).
// =============================================================================

public sealed class TriangleAabbBvh
{
    private const int LeafSize = 8;

    private struct Node
    {
        public double MinX, MinY, MinZ;
        public double MaxX, MaxY, MaxZ;
        public int Left;       // -1 for leaves
        public int Right;      // -1 for leaves
        public int RangeStart; // first index into _permutation for leaves
        public int RangeCount; // 0 for internal nodes
    }

    private readonly Node[] _nodes;
    private readonly int[] _permutation;
    private readonly int _rootIndex;
    private readonly IReadOnlyList<double> _vertices;
    private readonly IReadOnlyList<int> _triangles;

    /// <summary>Number of triangles indexed by this BVH.</summary>
    public int TriangleCount { get; }

    private TriangleAabbBvh(
        Node[] nodes, int[] permutation, int rootIndex,
        IReadOnlyList<double> vertices, IReadOnlyList<int> triangles)
    {
        _nodes = nodes;
        _permutation = permutation;
        _rootIndex = rootIndex;
        _vertices = vertices;
        _triangles = triangles;
        TriangleCount = triangles.Count / 3;
    }

    /// <summary>
    /// Build a BVH over every triangle in the given PlyMesh.
    /// </summary>
    public static TriangleAabbBvh Build(PlyMesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        int n = mesh.TriangleCount;
        var v = mesh.VertexCoordsXyz;
        var t = mesh.TriangleIndices;

        var permutation = new int[n];
        var triMin = new double[n * 3];
        var triMax = new double[n * 3];
        for (int i = 0; i < n; i++)
        {
            permutation[i] = i;
            int i0 = t[3 * i + 0], i1 = t[3 * i + 1], i2 = t[3 * i + 2];
            double x0 = v[3 * i0 + 0], y0 = v[3 * i0 + 1], z0 = v[3 * i0 + 2];
            double x1 = v[3 * i1 + 0], y1 = v[3 * i1 + 1], z1 = v[3 * i1 + 2];
            double x2 = v[3 * i2 + 0], y2 = v[3 * i2 + 1], z2 = v[3 * i2 + 2];
            triMin[3 * i + 0] = Math.Min(x0, Math.Min(x1, x2));
            triMin[3 * i + 1] = Math.Min(y0, Math.Min(y1, y2));
            triMin[3 * i + 2] = Math.Min(z0, Math.Min(z1, z2));
            triMax[3 * i + 0] = Math.Max(x0, Math.Max(x1, x2));
            triMax[3 * i + 1] = Math.Max(y0, Math.Max(y1, y2));
            triMax[3 * i + 2] = Math.Max(z0, Math.Max(z1, z2));
        }

        var nodes = new List<Node>(Math.Max(1, 2 * n));
        int root = BuildRecursive(nodes, permutation, triMin, triMax, 0, n);
        return new TriangleAabbBvh(nodes.ToArray(), permutation, root, v, t);
    }

    private static int BuildRecursive(
        List<Node> nodes,
        int[] perm,
        double[] triMin,
        double[] triMax,
        int start,
        int count)
    {
        // compute aggregate AABB over the range
        double mnx = double.PositiveInfinity, mny = double.PositiveInfinity, mnz = double.PositiveInfinity;
        double mxx = double.NegativeInfinity, mxy = double.NegativeInfinity, mxz = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            int tri = perm[start + k];
            if (triMin[3 * tri + 0] < mnx) mnx = triMin[3 * tri + 0];
            if (triMin[3 * tri + 1] < mny) mny = triMin[3 * tri + 1];
            if (triMin[3 * tri + 2] < mnz) mnz = triMin[3 * tri + 2];
            if (triMax[3 * tri + 0] > mxx) mxx = triMax[3 * tri + 0];
            if (triMax[3 * tri + 1] > mxy) mxy = triMax[3 * tri + 1];
            if (triMax[3 * tri + 2] > mxz) mxz = triMax[3 * tri + 2];
        }

        int nodeIdx = nodes.Count;
        nodes.Add(default);

        if (count <= LeafSize)
        {
            nodes[nodeIdx] = new Node
            {
                MinX = mnx, MinY = mny, MinZ = mnz,
                MaxX = mxx, MaxY = mxy, MaxZ = mxz,
                Left = -1, Right = -1,
                RangeStart = start, RangeCount = count
            };
            return nodeIdx;
        }

        // pick the longest axis of the AABB
        double dx = mxx - mnx, dy = mxy - mny, dz = mxz - mnz;
        int axis = 0;
        if (dy > dx) { axis = 1; if (dz > dy) axis = 2; }
        else        { if (dz > dx) axis = 2; }

        // median split by triangle centroid on chosen axis
        SortByCentroid(perm, triMin, triMax, start, count, axis);
        int half = count / 2;

        int leftIdx = BuildRecursive(nodes, perm, triMin, triMax, start, half);
        int rightIdx = BuildRecursive(nodes, perm, triMin, triMax, start + half, count - half);

        nodes[nodeIdx] = new Node
        {
            MinX = mnx, MinY = mny, MinZ = mnz,
            MaxX = mxx, MaxY = mxy, MaxZ = mxz,
            Left = leftIdx, Right = rightIdx,
            RangeStart = 0, RangeCount = 0
        };
        return nodeIdx;
    }

    private static void SortByCentroid(int[] perm, double[] triMin, double[] triMax, int start, int count, int axis)
    {
        var slice = new int[count];
        Array.Copy(perm, start, slice, 0, count);
        Array.Sort(slice, (a, b) =>
        {
            double ca = 0.5 * (triMin[3 * a + axis] + triMax[3 * a + axis]);
            double cb = 0.5 * (triMin[3 * b + axis] + triMax[3 * b + axis]);
            return ca.CompareTo(cb);
        });
        Array.Copy(slice, 0, perm, start, count);
    }

    /// <summary>
    /// Test whether any triangle whose AABB overlaps the candidate OBB's
    /// world-AABB actually intersects the OBB via the SAT inner test.
    /// Returns true on the first hit.
    /// </summary>
    public bool AnyTriangleIntersects(in OrientedBlock obb)
    {
        // world-AABB of the OBB (I1: full 3D-aware)
        double cx = obb.CenterX, cy = obb.CenterY, cz = obb.CenterZ;
        double ex = Math.Abs(obb.UX) * obb.HalfX + Math.Abs(obb.VX) * obb.HalfY + Math.Abs(obb.WX) * obb.HalfZ;
        double ey = Math.Abs(obb.UY) * obb.HalfX + Math.Abs(obb.VY) * obb.HalfY + Math.Abs(obb.WY) * obb.HalfZ;
        double ez = Math.Abs(obb.UZ) * obb.HalfX + Math.Abs(obb.VZ) * obb.HalfY + Math.Abs(obb.WZ) * obb.HalfZ;
        double minX = cx - ex, maxX = cx + ex;
        double minY = cy - ey, maxY = cy + ey;
        double minZ = cz - ez, maxZ = cz + ez;

        return RecurseQuery(_rootIndex, in obb, minX, minY, minZ, maxX, maxY, maxZ);
    }

    private bool RecurseQuery(
        int nodeIdx,
        in OrientedBlock obb,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        ref readonly Node node = ref _nodes[nodeIdx];
        // AABB-AABB overlap test
        if (node.MinX > maxX || node.MaxX < minX) return false;
        if (node.MinY > maxY || node.MaxY < minY) return false;
        if (node.MinZ > maxZ || node.MaxZ < minZ) return false;

        if (node.Left < 0)
        {
            // leaf -- run SAT on each triangle in the range
            for (int k = 0; k < node.RangeCount; k++)
            {
                int tri = _permutation[node.RangeStart + k];
                int i0 = _triangles[3 * tri + 0];
                int i1 = _triangles[3 * tri + 1];
                int i2 = _triangles[3 * tri + 2];
                if (ObbTriangleIntersection.Intersects(in obb,
                    _vertices[3 * i0 + 0], _vertices[3 * i0 + 1], _vertices[3 * i0 + 2],
                    _vertices[3 * i1 + 0], _vertices[3 * i1 + 1], _vertices[3 * i1 + 2],
                    _vertices[3 * i2 + 0], _vertices[3 * i2 + 1], _vertices[3 * i2 + 2]))
                {
                    return true;
                }
            }
            return false;
        }

        if (RecurseQuery(node.Left, in obb, minX, minY, minZ, maxX, maxY, maxZ)) return true;
        if (RecurseQuery(node.Right, in obb, minX, minY, minZ, maxX, maxY, maxZ)) return true;
        return false;
    }
}
