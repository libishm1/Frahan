#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// ObbTriangleIntersection -- SAT (Separating Axis Theorem) intersection
// test between an OrientedBlock (3D oriented bounding box) and a triangle.
//
// 13 candidate separating axes:
//   3 OBB face normals (U, V, W).
//   1 triangle normal.
//   9 cross products (OBB axis) x (triangle edge).
//
// If a separating axis exists, the OBB and triangle do not overlap.
// Reference: Akenine-Moller 2001, "Fast 3D triangle-box overlap testing".
// =============================================================================

public static class ObbTriangleIntersection
{
    private const double Eps = 1e-12;

    /// <summary>
    /// True if the triangle (p0, p1, p2) intersects the OBB. Touching counts as
    /// intersection.
    /// </summary>
    public static bool Intersects(
        in OrientedBlock obb,
        double p0X, double p0Y, double p0Z,
        double p1X, double p1Y, double p1Z,
        double p2X, double p2Y, double p2Z)
    {
        // translate triangle so that OBB center is at the origin
        double q0X = p0X - obb.CenterX, q0Y = p0Y - obb.CenterY, q0Z = p0Z - obb.CenterZ;
        double q1X = p1X - obb.CenterX, q1Y = p1Y - obb.CenterY, q1Z = p1Z - obb.CenterZ;
        double q2X = p2X - obb.CenterX, q2Y = p2Y - obb.CenterY, q2Z = p2Z - obb.CenterZ;

        // axes of the OBB (I1: full 3D; W != +Z when theta/phi non-zero)
        double aUX = obb.UX, aUY = obb.UY, aUZ = obb.UZ;
        double aVX = obb.VX, aVY = obb.VY, aVZ = obb.VZ;
        double aWX = obb.WX, aWY = obb.WY, aWZ = obb.WZ;

        double hU = obb.HalfX, hV = obb.HalfY, hW = obb.HalfZ;

        // edges of the triangle
        double e0X = q1X - q0X, e0Y = q1Y - q0Y, e0Z = q1Z - q0Z;
        double e1X = q2X - q1X, e1Y = q2Y - q1Y, e1Z = q2Z - q1Z;
        double e2X = q0X - q2X, e2Y = q0Y - q2Y, e2Z = q0Z - q2Z;

        // 1) OBB face normals U, V, W -- project triangle, compare to half-extent
        if (!OverlapOnAxis(aUX, aUY, aUZ, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z, hU)) return false;
        if (!OverlapOnAxis(aVX, aVY, aVZ, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z, hV)) return false;
        if (!OverlapOnAxis(aWX, aWY, aWZ, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z, hW)) return false;

        // 2) Triangle normal -- project OBB extents and triangle
        double nX = e0Y * (-e2Z) - e0Z * (-e2Y);
        double nY = e0Z * (-e2X) - e0X * (-e2Z);
        double nZ = e0X * (-e2Y) - e0Y * (-e2X);
        double nLen2 = nX * nX + nY * nY + nZ * nZ;
        if (nLen2 > Eps)
        {
            double inv = 1.0 / Math.Sqrt(nLen2);
            nX *= inv; nY *= inv; nZ *= inv;
            if (!OverlapOnAxis(nX, nY, nZ, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
                ObbHalfProjection(nX, nY, nZ, aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)))
                return false;
        }

        // 3) Nine cross products (OBB axis) x (triangle edge)
        if (!CrossAxis(aUX, aUY, aUZ, e0X, e0Y, e0Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;
        if (!CrossAxis(aUX, aUY, aUZ, e1X, e1Y, e1Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;
        if (!CrossAxis(aUX, aUY, aUZ, e2X, e2Y, e2Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;

        if (!CrossAxis(aVX, aVY, aVZ, e0X, e0Y, e0Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;
        if (!CrossAxis(aVX, aVY, aVZ, e1X, e1Y, e1Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;
        if (!CrossAxis(aVX, aVY, aVZ, e2X, e2Y, e2Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;

        if (!CrossAxis(aWX, aWY, aWZ, e0X, e0Y, e0Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;
        if (!CrossAxis(aWX, aWY, aWZ, e1X, e1Y, e1Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;
        if (!CrossAxis(aWX, aWY, aWZ, e2X, e2Y, e2Z, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            aUX, aUY, aUZ, aVX, aVY, aVZ, aWX, aWY, aWZ, hU, hV, hW)) return false;

        return true;
    }

    private static bool OverlapOnAxis(
        double aX, double aY, double aZ,
        double q0X, double q0Y, double q0Z,
        double q1X, double q1Y, double q1Z,
        double q2X, double q2Y, double q2Z,
        double obbHalfProjection)
    {
        double t0 = q0X * aX + q0Y * aY + q0Z * aZ;
        double t1 = q1X * aX + q1Y * aY + q1Z * aZ;
        double t2 = q2X * aX + q2Y * aY + q2Z * aZ;
        double triMin = Math.Min(t0, Math.Min(t1, t2));
        double triMax = Math.Max(t0, Math.Max(t1, t2));
        return !(triMin > obbHalfProjection || triMax < -obbHalfProjection);
    }

    private static double ObbHalfProjection(
        double aX, double aY, double aZ,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ,
        double wX, double wY, double wZ,
        double hU, double hV, double hW)
    {
        return Math.Abs(uX * aX + uY * aY + uZ * aZ) * hU
             + Math.Abs(vX * aX + vY * aY + vZ * aZ) * hV
             + Math.Abs(wX * aX + wY * aY + wZ * aZ) * hW;
    }

    private static bool CrossAxis(
        double aX, double aY, double aZ,
        double eX, double eY, double eZ,
        double q0X, double q0Y, double q0Z,
        double q1X, double q1Y, double q1Z,
        double q2X, double q2Y, double q2Z,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ,
        double wX, double wY, double wZ,
        double hU, double hV, double hW)
    {
        double cX = aY * eZ - aZ * eY;
        double cY = aZ * eX - aX * eZ;
        double cZ = aX * eY - aY * eX;
        double len2 = cX * cX + cY * cY + cZ * cZ;
        if (len2 < Eps) return true; // axes are parallel; skip
        return OverlapOnAxis(cX, cY, cZ, q0X, q0Y, q0Z, q1X, q1Y, q1Z, q2X, q2Y, q2Z,
            ObbHalfProjection(cX, cY, cZ, uX, uY, uZ, vX, vY, vZ, wX, wY, wZ, hU, hV, hW));
    }
}
