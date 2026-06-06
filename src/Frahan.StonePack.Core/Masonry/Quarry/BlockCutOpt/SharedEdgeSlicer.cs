#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// SharedEdgeSlicer -- Minetto, Volpato, Stolfi, Gregori, Silva 2017 optimal
// triangle-mesh slicing. Processes each shared edge exactly once across all
// slicing planes, giving O(n + k) instead of O(n * k) for n triangles across
// k parallel slice planes.
//
// Phase 9 of the synthesis roadmap; improvement I12. Used inside the
// Shao 2022 plane-sequence loop to compute the polygonal cross-section of
// the current CPH against each candidate cutting plane.
//
// Input: triangle mesh + a list of parallel cutting planes specified as a
// plane normal + a sorted list of signed offsets along that normal.
// Output: one closed polyline (or polyline set) per plane.
//
// All units are metres (default world unit per BlockCutOptTolerances.cs).
// =============================================================================

public sealed class SliceContour
{
    public SliceContour(double offset, IReadOnlyList<(double X, double Y, double Z)> segmentEndpoints)
    {
        Offset = offset;
        SegmentEndpoints = segmentEndpoints;
    }

    /// <summary>Signed offset along the slicing normal.</summary>
    public double Offset { get; }

    /// <summary>
    /// Flat list of segment endpoints: for s segments, the list has 2*s
    /// entries (endpoint A of segment 0, endpoint B of segment 0, A of 1, ...).
    /// Use BuildClosedLoops() to recover ordered polylines.
    /// </summary>
    public IReadOnlyList<(double X, double Y, double Z)> SegmentEndpoints { get; }

    public int SegmentCount => SegmentEndpoints.Count / 2;
}

public static class SharedEdgeSlicer
{
    /// <summary>
    /// Slice <paramref name="mesh"/> by k planes whose normal is
    /// <paramref name="planeNormal"/> (unit-normalised on entry) and whose
    /// signed offsets along that normal are <paramref name="sortedOffsets"/>.
    /// Each triangle is visited once; each edge is intersected at most twice.
    /// </summary>
    public static IReadOnlyList<SliceContour> Slice(
        PlyMesh mesh,
        double normalX, double normalY, double normalZ,
        IReadOnlyList<double> sortedOffsets)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (sortedOffsets == null) throw new ArgumentNullException(nameof(sortedOffsets));
        double n2 = normalX * normalX + normalY * normalY + normalZ * normalZ;
        if (n2 < BlockCutOptTolerances.GeometricEps)
            throw new ArgumentException("plane normal must be non-zero");
        double inv = 1.0 / Math.Sqrt(n2);
        double nx = normalX * inv, ny = normalY * inv, nz = normalZ * inv;

        int k = sortedOffsets.Count;
        var bins = new List<(double X, double Y, double Z)>[k];
        for (int i = 0; i < k; i++) bins[i] = new List<(double, double, double)>();

        var v = mesh.VertexCoordsXyz;
        var t = mesh.TriangleIndices;
        int nv = mesh.VertexCount;
        int ntri = mesh.TriangleCount;

        // pre-compute signed distances of every vertex once (this is the
        // "shared-vertex" half of the optimisation -- each vertex evaluated
        // exactly once regardless of how many triangles share it)
        var d = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            d[i] = v[3 * i + 0] * nx + v[3 * i + 1] * ny + v[3 * i + 2] * nz;
        }

        for (int tri = 0; tri < ntri; tri++)
        {
            int i0 = t[3 * tri + 0], i1 = t[3 * tri + 1], i2 = t[3 * tri + 2];
            double d0 = d[i0], d1 = d[i1], d2 = d[i2];
            double mn = Math.Min(d0, Math.Min(d1, d2));
            double mx = Math.Max(d0, Math.Max(d1, d2));

            // find the slice range that overlaps this triangle's [mn, mx]
            int lo = LowerBound(sortedOffsets, mn);
            int hi = UpperBound(sortedOffsets, mx);

            if (lo >= hi) continue;
            for (int s = lo; s < hi; s++)
            {
                double off = sortedOffsets[s];
                if (TryIntersectTrianglePlane(
                    v, i0, i1, i2, d0, d1, d2, off,
                    out double ax, out double ay, out double az,
                    out double bx, out double by, out double bz))
                {
                    bins[s].Add((ax, ay, az));
                    bins[s].Add((bx, by, bz));
                }
            }
        }

        var output = new SliceContour[k];
        for (int i = 0; i < k; i++) output[i] = new SliceContour(sortedOffsets[i], bins[i]);
        return output;
    }

    private static bool TryIntersectTrianglePlane(
        IReadOnlyList<double> v,
        int i0, int i1, int i2,
        double d0, double d1, double d2,
        double off,
        out double ax, out double ay, out double az,
        out double bx, out double by, out double bz)
    {
        ax = ay = az = bx = by = bz = 0;
        double s0 = d0 - off, s1 = d1 - off, s2 = d2 - off;

        // count how many edges cross the plane
        int crossings = 0;
        if ((s0 > 0 && s1 < 0) || (s0 < 0 && s1 > 0)) crossings++;
        if ((s1 > 0 && s2 < 0) || (s1 < 0 && s2 > 0)) crossings++;
        if ((s2 > 0 && s0 < 0) || (s2 < 0 && s0 > 0)) crossings++;
        if (crossings < 2) return false;

        double[] ex = new double[3], ey = new double[3], ez = new double[3];
        int found = 0;
        if (((s0 > 0 && s1 < 0) || (s0 < 0 && s1 > 0)) && found < 3)
            InterpolateEdge(v, i0, i1, s0, s1, out ex[found], out ey[found], out ez[found], ref found);
        if (((s1 > 0 && s2 < 0) || (s1 < 0 && s2 > 0)) && found < 3)
            InterpolateEdge(v, i1, i2, s1, s2, out ex[found], out ey[found], out ez[found], ref found);
        if (((s2 > 0 && s0 < 0) || (s2 < 0 && s0 > 0)) && found < 3)
            InterpolateEdge(v, i2, i0, s2, s0, out ex[found], out ey[found], out ez[found], ref found);
        if (found < 2) return false;
        ax = ex[0]; ay = ey[0]; az = ez[0];
        bx = ex[1]; by = ey[1]; bz = ez[1];
        return true;
    }

    private static void InterpolateEdge(
        IReadOnlyList<double> v, int ia, int ib,
        double sa, double sb,
        out double x, out double y, out double z, ref int found)
    {
        double t = sa / (sa - sb);
        x = v[3 * ia + 0] + t * (v[3 * ib + 0] - v[3 * ia + 0]);
        y = v[3 * ia + 1] + t * (v[3 * ib + 1] - v[3 * ia + 1]);
        z = v[3 * ia + 2] + t * (v[3 * ib + 2] - v[3 * ia + 2]);
        found++;
    }

    private static int LowerBound(IReadOnlyList<double> sorted, double key)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] < key) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(IReadOnlyList<double> sorted, double key)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] <= key) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
