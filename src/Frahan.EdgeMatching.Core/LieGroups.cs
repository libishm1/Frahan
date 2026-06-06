#nullable disable
using System;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// SE(2) Lie-algebra retraction (Solà 2018, "A micro Lie theory"). A tangent
    /// vector xi = [vx, vy, theta] in se(2) maps to a rigid motion in the XY
    /// plane via the exponential map. This is the planar pose increment of the
    /// Soft-ICP refiner; it is left-composed onto the existing Transform (the
    /// Panel/Solve convention T_new = Exp(xi) * T_old).
    ///
    /// The closed form (Solà eq. for SE(2)):
    ///   R = [[cos t, -sin t], [sin t, cos t]]
    ///   V = (1/t) [[sin t, -(1-cos t)], [(1-cos t), sin t]]      (t != 0)
    ///   translation = V * [vx, vy]
    /// with the small-angle limit V -> I as t -> 0 (so a pure-translation tangent
    /// retracts to a pure translation, and the map is C^infinity through 0).
    /// </summary>
    public static class LieSe2
    {
        /// <summary>Exponential map se(2) -> SE(2), returned as a Rhino XY Transform.</summary>
        public static Transform Exp(double vx, double vy, double theta)
        {
            double c = Math.Cos(theta), s = Math.Sin(theta);
            double a, b; // V matrix entries: V = [[a, -b],[b, a]]
            if (Math.Abs(theta) < 1e-9)
            {
                // Limits: sin t / t -> 1, (1 - cos t)/t -> 0.
                a = 1.0 - theta * theta / 6.0; // higher-order accuracy near 0
                b = theta / 2.0;
            }
            else
            {
                a = s / theta;
                b = (1.0 - c) / theta;
            }
            double tx = a * vx - b * vy;
            double ty = b * vx + a * vy;

            var t = Transform.Identity;
            t.M00 = c; t.M01 = -s;
            t.M10 = s; t.M11 = c;
            t.M03 = tx;
            t.M13 = ty;
            return t;
        }
    }

    /// <summary>
    /// SE(3) Lie-algebra retraction (Solà 2018). A tangent vector
    /// xi = [rho (3); omega (3)] in se(3) maps to a rigid motion via the
    /// exponential map: the rotation is so(3) Exp of omega (Rodrigues), and the
    /// translation is V(omega) * rho where V is the left Jacobian of SO(3). This
    /// is the spatial pose increment of the Soft-ICP refiner; it is left-composed
    /// onto the existing Transform.
    ///
    /// For the LOCAL refine near identity the so(3) tangent is well-conditioned;
    /// the small-angle limits keep the map C^infinity through |omega| = 0.
    /// </summary>
    public static class LieSe3
    {
        /// <summary>so(3) exponential (Rodrigues): axis-angle vector -> rotation Transform.</summary>
        public static Transform ExpSo3(double wx, double wy, double wz)
        {
            double theta = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            var t = Transform.Identity;
            if (theta < 1e-12)
            {
                // I + [w]_x to first order.
                t.M01 = -wz; t.M02 = wy;
                t.M10 = wz; t.M12 = -wx;
                t.M20 = -wy; t.M21 = wx;
                return t;
            }
            double a = Math.Sin(theta) / theta;            // sinθ/θ
            double b = (1.0 - Math.Cos(theta)) / (theta * theta); // (1-cosθ)/θ²
            // R = I + a*[w]_x + b*[w]_x^2  (Rodrigues).
            double kx = wx, ky = wy, kz = wz;
            // [w]_x
            double K01 = -kz, K02 = ky, K10 = kz, K12 = -kx, K20 = -ky, K21 = kx;
            // [w]_x^2 = w w^T - theta^2 I  (since |w|^2 = theta^2)
            double xx = kx * kx, yy = ky * ky, zz = kz * kz;
            double xy = kx * ky, xz = kx * kz, yz = ky * kz;
            double th2 = theta * theta;
            double K2_00 = xx - th2, K2_01 = xy, K2_02 = xz;
            double K2_10 = xy, K2_11 = yy - th2, K2_12 = yz;
            double K2_20 = xz, K2_21 = yz, K2_22 = zz - th2;

            t.M00 = 1.0 + a * 0 + b * K2_00;
            t.M01 = a * K01 + b * K2_01;
            t.M02 = a * K02 + b * K2_02;
            t.M10 = a * K10 + b * K2_10;
            t.M11 = 1.0 + b * K2_11;
            t.M12 = a * K12 + b * K2_12;
            t.M20 = a * K20 + b * K2_20;
            t.M21 = a * K21 + b * K2_21;
            t.M22 = 1.0 + b * K2_22;
            return t;
        }

        /// <summary>
        /// Full se(3) exponential: rho = [rx,ry,rz] (tangent translation),
        /// omega = [wx,wy,wz] (so(3) tangent). Returns Exp(xi) as a Transform.
        /// </summary>
        public static Transform Exp(
            double rx, double ry, double rz,
            double wx, double wy, double wz)
        {
            var R = ExpSo3(wx, wy, wz);
            // Left Jacobian V of SO(3): V = I + b*[w]_x + ((θ - sinθ)/θ^3)*[w]_x^2
            double theta = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            double b, c;
            if (theta < 1e-9)
            {
                b = 0.5;        // (1-cosθ)/θ² -> 1/2
                c = 1.0 / 6.0;  // (θ-sinθ)/θ³ -> 1/6
            }
            else
            {
                double th2 = theta * theta, th3 = th2 * theta;
                b = (1.0 - Math.Cos(theta)) / th2;
                c = (theta - Math.Sin(theta)) / th3;
            }
            double K01 = -wz, K02 = wy, K10 = wz, K12 = -wx, K20 = -wy, K21 = wx;
            double xx = wx * wx, yy = wy * wy, zz = wz * wz;
            double xy = wx * wy, xz = wx * wz, yz = wy * wz;
            double th2b = theta * theta;
            double K2_00 = xx - th2b, K2_01 = xy, K2_02 = xz;
            double K2_10 = xy, K2_11 = yy - th2b, K2_12 = yz;
            double K2_20 = xz, K2_21 = yz, K2_22 = zz - th2b;

            double V00 = 1.0 + b * 0 + c * K2_00;
            double V01 = b * K01 + c * K2_01;
            double V02 = b * K02 + c * K2_02;
            double V10 = b * K10 + c * K2_10;
            double V11 = 1.0 + c * K2_11;
            double V12 = b * K12 + c * K2_12;
            double V20 = b * K20 + c * K2_20;
            double V21 = b * K21 + c * K2_21;
            double V22 = 1.0 + c * K2_22;

            double tx = V00 * rx + V01 * ry + V02 * rz;
            double ty = V10 * rx + V11 * ry + V12 * rz;
            double tz = V20 * rx + V21 * ry + V22 * rz;

            var t = R;
            t.M03 = tx;
            t.M13 = ty;
            t.M23 = tz;
            return t;
        }
    }
}
