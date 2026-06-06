#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// EdgeTriangleObbIntersection -- I4 alternative to the 13-axis SAT.
//
// Tests whether an OBB and a triangle overlap by:
//   1. Point-in-OBB on each triangle vertex.
//   2. Moller-Trumbore segment-vs-triangle on each of the 12 OBB edges.
//   3. Each of the 12 edges of the triangle (only 3, but treated as line
//      segments) tested against the 6 OBB faces via segment-vs-plane plus
//      point-in-quad.
//
// This is structurally different from SAT and serves two purposes:
//   - profiling-driven alternative: when SAT becomes the bottleneck on
//     specific triangle distributions, this can be substituted as a drop-
//     in replacement.
//   - correctness oracle: differential testing between SAT and this
//     primitive on random inputs catches bugs in either path.
//
// API parity with ObbTriangleIntersection.Intersects.
// =============================================================================

public static class EdgeTriangleObbIntersection
{
    private const double Eps = 1e-12;

    public static bool Intersects(
        in OrientedBlock obb,
        double p0X, double p0Y, double p0Z,
        double p1X, double p1Y, double p1Z,
        double p2X, double p2Y, double p2Z)
    {
        // 1. Any triangle vertex inside the OBB?
        if (PointInObb(in obb, p0X, p0Y, p0Z)) return true;
        if (PointInObb(in obb, p1X, p1Y, p1Z)) return true;
        if (PointInObb(in obb, p2X, p2Y, p2Z)) return true;

        // 2. Any OBB edge crossing the triangle?
        // Build the 8 corners of the OBB. net48: plain arrays (no Span<T>).
        double[] cx = new double[8];
        double[] cy = new double[8];
        double[] cz = new double[8];
        for (int k = 0; k < 8; k++)
        {
            double sx = ((k & 1) != 0 ? +1 : -1) * obb.HalfX;
            double sy = ((k & 2) != 0 ? +1 : -1) * obb.HalfY;
            double sz = ((k & 4) != 0 ? +1 : -1) * obb.HalfZ;
            cx[k] = obb.CenterX + sx * obb.UX + sy * obb.VX + sz * obb.WX;
            cy[k] = obb.CenterY + sx * obb.UY + sy * obb.VY + sz * obb.WY;
            cz[k] = obb.CenterZ + sx * obb.UZ + sy * obb.VZ + sz * obb.WZ;
        }
        // 12 edges of the OBB by corner pairs
        int[] edgesA = { 0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 2, 3 };
        int[] edgesB = { 1, 3, 2, 0, 5, 7, 6, 4, 4, 5, 6, 7 };
        for (int e = 0; e < 12; e++)
        {
            int a = edgesA[e]; int b = edgesB[e];
            if (SegmentTriangle(
                cx[a], cy[a], cz[a],
                cx[b], cy[b], cz[b],
                p0X, p0Y, p0Z, p1X, p1Y, p1Z, p2X, p2Y, p2Z))
                return true;
        }

        // 3. Any triangle edge crossing the OBB? Use a coarse line-OBB test:
        //    treat the edge as a segment and test against the OBB by clipping
        //    in the OBB's local frame.
        if (SegmentObb(in obb, p0X, p0Y, p0Z, p1X, p1Y, p1Z)) return true;
        if (SegmentObb(in obb, p1X, p1Y, p1Z, p2X, p2Y, p2Z)) return true;
        if (SegmentObb(in obb, p2X, p2Y, p2Z, p0X, p0Y, p0Z)) return true;

        return false;
    }

    private static bool PointInObb(
        in OrientedBlock obb, double x, double y, double z)
    {
        double dx = x - obb.CenterX, dy = y - obb.CenterY, dz = z - obb.CenterZ;
        double u = dx * obb.UX + dy * obb.UY + dz * obb.UZ;
        if (u < -obb.HalfX - Eps || u > obb.HalfX + Eps) return false;
        double v = dx * obb.VX + dy * obb.VY + dz * obb.VZ;
        if (v < -obb.HalfY - Eps || v > obb.HalfY + Eps) return false;
        double w = dx * obb.WX + dy * obb.WY + dz * obb.WZ;
        return w >= -obb.HalfZ - Eps && w <= obb.HalfZ + Eps;
    }

    /// <summary>
    /// Moller-Trumbore segment-triangle intersection. True when the segment
    /// (q0 -> q1) crosses the triangle's interior or boundary.
    /// </summary>
    private static bool SegmentTriangle(
        double q0X, double q0Y, double q0Z,
        double q1X, double q1Y, double q1Z,
        double p0X, double p0Y, double p0Z,
        double p1X, double p1Y, double p1Z,
        double p2X, double p2Y, double p2Z)
    {
        double dirX = q1X - q0X, dirY = q1Y - q0Y, dirZ = q1Z - q0Z;
        double e1X = p1X - p0X, e1Y = p1Y - p0Y, e1Z = p1Z - p0Z;
        double e2X = p2X - p0X, e2Y = p2Y - p0Y, e2Z = p2Z - p0Z;
        double pvX = dirY * e2Z - dirZ * e2Y;
        double pvY = dirZ * e2X - dirX * e2Z;
        double pvZ = dirX * e2Y - dirY * e2X;
        double det = e1X * pvX + e1Y * pvY + e1Z * pvZ;
        if (Math.Abs(det) < Eps) return false;
        double inv = 1.0 / det;
        double tvX = q0X - p0X, tvY = q0Y - p0Y, tvZ = q0Z - p0Z;
        double u = (tvX * pvX + tvY * pvY + tvZ * pvZ) * inv;
        if (u < -Eps || u > 1.0 + Eps) return false;
        double qvX = tvY * e1Z - tvZ * e1Y;
        double qvY = tvZ * e1X - tvX * e1Z;
        double qvZ = tvX * e1Y - tvY * e1X;
        double v = (dirX * qvX + dirY * qvY + dirZ * qvZ) * inv;
        if (v < -Eps || u + v > 1.0 + Eps) return false;
        double t = (e2X * qvX + e2Y * qvY + e2Z * qvZ) * inv;
        return t >= -Eps && t <= 1.0 + Eps;
    }

    private static bool SegmentObb(
        in OrientedBlock obb,
        double q0X, double q0Y, double q0Z,
        double q1X, double q1Y, double q1Z)
    {
        // sample N points on the segment and test each via PointInObb. For
        // exact correctness, project the segment into the OBB's local frame
        // and clip against [-h, +h] on each axis (Liang-Barsky style).
        double q0u = (q0X - obb.CenterX) * obb.UX + (q0Y - obb.CenterY) * obb.UY + (q0Z - obb.CenterZ) * obb.UZ;
        double q0v = (q0X - obb.CenterX) * obb.VX + (q0Y - obb.CenterY) * obb.VY + (q0Z - obb.CenterZ) * obb.VZ;
        double q0w = (q0X - obb.CenterX) * obb.WX + (q0Y - obb.CenterY) * obb.WY + (q0Z - obb.CenterZ) * obb.WZ;
        double q1u = (q1X - obb.CenterX) * obb.UX + (q1Y - obb.CenterY) * obb.UY + (q1Z - obb.CenterZ) * obb.UZ;
        double q1v = (q1X - obb.CenterX) * obb.VX + (q1Y - obb.CenterY) * obb.VY + (q1Z - obb.CenterZ) * obb.VZ;
        double q1w = (q1X - obb.CenterX) * obb.WX + (q1Y - obb.CenterY) * obb.WY + (q1Z - obb.CenterZ) * obb.WZ;
        double tIn = 0.0, tOut = 1.0;
        if (!ClipAxis(q0u, q1u, -obb.HalfX, obb.HalfX, ref tIn, ref tOut)) return false;
        if (!ClipAxis(q0v, q1v, -obb.HalfY, obb.HalfY, ref tIn, ref tOut)) return false;
        if (!ClipAxis(q0w, q1w, -obb.HalfZ, obb.HalfZ, ref tIn, ref tOut)) return false;
        return tIn <= tOut + Eps;
    }

    private static bool ClipAxis(double q0, double q1, double lo, double hi,
                                  ref double tIn, ref double tOut)
    {
        double d = q1 - q0;
        if (Math.Abs(d) < Eps)
        {
            return q0 >= lo - Eps && q0 <= hi + Eps;
        }
        double t1 = (lo - q0) / d;
        double t2 = (hi - q0) / d;
        if (t1 > t2) { double t = t1; t1 = t2; t2 = t; }
        if (t1 > tIn) tIn = t1;
        if (t2 < tOut) tOut = t2;
        return tIn <= tOut + Eps;
    }
}
