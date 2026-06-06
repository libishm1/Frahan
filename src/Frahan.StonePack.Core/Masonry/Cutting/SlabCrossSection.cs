#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Cutting;

// =============================================================================
// SlabCrossSection — compute the convex polygon where a plane intersects a
// convex Slab, without performing the cut.
//
// Reuses the same vertex-classification + edge-intersection + CCW ordering
// logic as SlabCutter's internal cap-polygon code, exposed as a separate
// utility so that downstream consumers (FractureCutter, geometric debug
// tooling) can ask "where would the cut land?" without paying the cost of
// building two output slabs.
//
// Output: ordered list of cross-section vertex positions, CCW around
// +plane.normal. Returns an empty list if the plane misses the slab or
// only touches it along an edge / vertex.
// =============================================================================

public static class SlabCrossSection
{
    /// <summary>
    /// Compute the convex polygon where <paramref name="plane"/> cuts
    /// <paramref name="slab"/>. Returns vertex coordinates as a flat
    /// <c>double[]</c> with length <c>3 * vertexCount</c>, ordered CCW
    /// around <c>+plane.normal</c>. Returns an empty array if the plane
    /// does not produce a 2D cross-section (misses, or grazes only an
    /// edge / vertex).
    /// </summary>
    public static double[] Compute(Slab slab, FracturePlane plane, double eps = SlabCutter.DefaultEps)
    {
        if (slab == null) throw new ArgumentNullException(nameof(slab));
        if (plane == null) throw new ArgumentNullException(nameof(plane));
        if (eps < 0.0) throw new ArgumentOutOfRangeException(nameof(eps), "must be >= 0");

        int vCount = slab.VertexCount;
        var v = slab.VertexCoordsXyz;

        var dist = new double[vCount];
        var cls = new int[vCount];
        bool anyAbove = false, anyBelow = false;
        for (int i = 0; i < vCount; i++)
        {
            double d = plane.SignedDistance(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
            dist[i] = d;
            if (d > eps) { cls[i] = 1; anyAbove = true; }
            else if (d < -eps) { cls[i] = -1; anyBelow = true; }
            else cls[i] = 0;
        }

        if (!anyAbove || !anyBelow)
        {
            // Plane misses or only touches; not a 2D cross-section.
            return Array.Empty<double>();
        }

        // Collect cap-polygon vertex positions (edge intersections + on-plane originals).
        var seenEdges = new HashSet<long>();
        var seenOnPlane = new HashSet<int>();
        var capX = new List<double>();
        var capY = new List<double>();
        var capZ = new List<double>();

        for (int fi = 0; fi < slab.Faces.Count; fi++)
        {
            var face = slab.Faces[fi];
            int n = face.Count;
            for (int k = 0; k < n; k++)
            {
                int va = face[k];
                int vb = face[(k + 1) % n];
                int ca = cls[va];
                int cb = cls[vb];

                if (ca == 0 && seenOnPlane.Add(va))
                {
                    capX.Add(v[3 * va]); capY.Add(v[3 * va + 1]); capZ.Add(v[3 * va + 2]);
                }

                if (ca * cb < 0)
                {
                    int lo = va < vb ? va : vb;
                    int hi = va < vb ? vb : va;
                    long key = (long)lo * (long)int.MaxValue + (long)hi;
                    if (seenEdges.Add(key))
                    {
                        double t = -dist[va] / (dist[vb] - dist[va]);
                        capX.Add(v[3 * va] + t * (v[3 * vb] - v[3 * va]));
                        capY.Add(v[3 * va + 1] + t * (v[3 * vb + 1] - v[3 * va + 1]));
                        capZ.Add(v[3 * va + 2] + t * (v[3 * vb + 2] - v[3 * va + 2]));
                    }
                }
            }
        }

        if (capX.Count < 3) return Array.Empty<double>();

        // Order CCW around +plane.normal via in-plane (u, v) basis + atan2.
        return OrderCcw(capX, capY, capZ, plane);
    }

    private static double[] OrderCcw(
        List<double> xs, List<double> ys, List<double> zs, FracturePlane plane)
    {
        int n = xs.Count;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++) { cx += xs[i]; cy += ys[i]; cz += zs[i]; }
        cx /= n; cy /= n; cz /= n;

        double nx = plane.NormalX, ny = plane.NormalY, nz = plane.NormalZ;
        double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
        double rx, ry, rz;
        if (ax <= ay && ax <= az) { rx = 1; ry = 0; rz = 0; }
        else if (ay <= ax && ay <= az) { rx = 0; ry = 1; rz = 0; }
        else { rx = 0; ry = 0; rz = 1; }

        double ux = ny * rz - nz * ry;
        double uy = nz * rx - nx * rz;
        double uz = nx * ry - ny * rx;
        double ulen = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= ulen; uy /= ulen; uz /= ulen;
        double vx = ny * uz - nz * uy;
        double vy = nz * ux - nx * uz;
        double vz = nx * uy - ny * ux;
        double vlen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
        vx /= vlen; vy /= vlen; vz /= vlen;

        var pairs = new (int Idx, double Theta)[n];
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - cx, dy = ys[i] - cy, dz = zs[i] - cz;
            double du = dx * ux + dy * uy + dz * uz;
            double dv = dx * vx + dy * vy + dz * vz;
            pairs[i] = (i, Math.Atan2(dv, du));
        }
        Array.Sort(pairs, (a, b) => a.Theta.CompareTo(b.Theta));

        var outArr = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            int idx = pairs[i].Idx;
            outArr[3 * i] = xs[idx];
            outArr[3 * i + 1] = ys[idx];
            outArr[3 * i + 2] = zs[idx];
        }
        return outArr;
    }
}
