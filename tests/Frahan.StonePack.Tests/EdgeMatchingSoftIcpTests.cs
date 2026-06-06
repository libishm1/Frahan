#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino;
using Rhino.Geometry;

namespace Frahan.Tests;

// Pillar A — Soft-ICP (EM weighted-Kabsch) rim-contact + non-penetration refiner
// (2026-05-25). Design basis wiki/research/differentiable_edge_matching.md §3.
// These tests are PURE MANAGED: they exercise the Lie-algebra Exp retractions and
// the EM weighted-Kabsch path, which use only Transform value math + MathNet SVD,
// no rhcommon_c. The 2D refine path is driven with Contour2D = null so the
// penetration term (the only Curve.Contains call) is skipped, keeping the EM
// alignment test off the Rhino runtime. Defaults_* lock the opt-in default OFF.
static class EdgeMatchingSoftIcpTests
{
    // ---- (1) Default-OFF guard (byte-for-byte back-compat) ----------------

    public static void Defaults_SoftIcpRefineIsOff()
    {
        var opt = new AssemblyOptions();
        Assert(opt.SoftIcpRefine == false, "default SoftIcpRefine must be false");
        Assert(opt.SoftIcp != null, "SoftIcp tuning must never be null");
        var s = opt.SoftIcp;
        Assert(s.Tau0Factor > 0, "Tau0Factor must be positive");
        Assert(s.TauAnneal > 0 && s.TauAnneal < 1, "TauAnneal must be in (0,1)");
        Assert(s.PenetrationStep > 0 && s.PenetrationStep <= 1, "PenetrationStep in (0,1]");
        Assert(s.MaxIterations >= 1, "MaxIterations >= 1");
    }

    // ---- (2) Lie-algebra Exp retraction correctness -----------------------

    // SE(2) Exp of a pure-translation tangent (theta=0) is exactly that
    // translation; Exp of [0,0,theta] is a pure rotation by theta about origin.
    public static void LieSe2_Exp_PureTranslationAndRotation()
    {
        var t = LieSe2.Exp(3.0, -2.0, 0.0);
        Assert(Close(t.M03, 3.0) && Close(t.M13, -2.0), "SE2 pure translation");
        Assert(Close(t.M00, 1.0) && Close(t.M11, 1.0) && Close(t.M01, 0.0), "SE2 no rotation");

        double ang = Math.PI / 3.0; // 60 deg
        var r = LieSe2.Exp(0.0, 0.0, ang);
        Assert(Close(r.M00, Math.Cos(ang)) && Close(r.M10, Math.Sin(ang)),
            "SE2 rotation block");
        // Pure-rotation tangent (zero linear part) -> zero translation.
        Assert(Close(r.M03, 0.0) && Close(r.M13, 0.0), "SE2 rotation has zero translation");
    }

    // SE(2) Exp must produce a proper rotation (orthonormal, det +1).
    public static void LieSe2_Exp_IsProperRotation()
    {
        var t = LieSe2.Exp(1.5, 0.7, 0.9);
        double det = t.M00 * t.M11 - t.M01 * t.M10;
        Assert(Close(det, 1.0), $"SE2 rotation det must be 1, got {det}");
        // Columns orthonormal.
        Assert(Close(t.M00 * t.M00 + t.M10 * t.M10, 1.0), "SE2 col0 unit");
    }

    // SE(3) so(3) Exp of a known axis-angle recovers the same rotation as
    // Rhino's Transform.Rotation (the reference).
    public static void LieSe3_ExpSo3_MatchesRhinoRotation()
    {
        var axis = new Vector3d(1, 2, 3); axis.Unitize();
        double ang = RhinoMath.ToRadians(25.0);
        var reference = Transform.Rotation(ang, axis, Point3d.Origin);
        var t = LieSe3.ExpSo3(axis.X * ang, axis.Y * ang, axis.Z * ang);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                Assert(Close(t[r, c], reference[r, c], 1e-9),
                    $"so3 Exp mismatch at [{r},{c}]: {t[r, c]} vs {reference[r, c]}");
        // Proper rotation: det +1.
        double det = Det3(t);
        Assert(Close(det, 1.0, 1e-9), $"so3 Exp det must be 1, got {det}");
    }

    // SE(3) full Exp near identity: a small omega + rho retracts to a rotation
    // with the expected first-order translation, and the rotation det is +1.
    public static void LieSe3_Exp_SmallTwistIsValid()
    {
        var t = LieSe3.Exp(0.5, -0.3, 0.2, 0.01, -0.02, 0.015);
        Assert(Close(Det3(t), 1.0, 1e-6), "SE3 small-twist rotation det must be 1");
        // Translation is finite and near rho for a tiny rotation.
        Assert(Math.Abs(t.M03 - 0.5) < 0.05, "SE3 small-twist tx near rho.x");
        Assert(Math.Abs(t.M13 + 0.3) < 0.05, "SE3 small-twist ty near rho.y");
    }

    // ---- (3) EM weighted-Kabsch recovers a known transform ----------------

    // Two identical rims; the second is the first moved by a known SE(2) motion
    // and we anchor the second. Soft-ICP must move the first onto the second so
    // the rim gap collapses to ~0. (Contour2D=null -> no penetration term, pure
    // managed.) This is the EM weighted-Kabsch recovering the alignment.
    public static void Em_RecoversKnown2DAlignment()
    {
        var rim = MakeWigglyRing(40, 10.0);
        // Known motion: rotate 12 deg + translate (5, -3).
        var motion = Transform.Multiply(
            Transform.Translation(5.0, -3.0, 0.0),
            Transform.Rotation(RhinoMath.ToRadians(12.0), Vector3d.ZAxis, Point3d.Origin));
        var rimB = Apply(rim, motion);

        // A starts at identity (offset from B by the inverse motion); B anchored.
        var fragA = new SoftIcpRefiner.Fragment("A", Clone(rim), solid: null, contour2D: null, anchored: false);
        var fragB = new SoftIcpRefiner.Fragment("B", rimB, solid: null, contour2D: null, anchored: true);
        var frags = new List<SoftIcpRefiner.Fragment> { fragA, fragB };

        var opt = new SoftIcpOptions { MaxIterations = 60 };
        var before = SoftIcpRefiner.Measure(frags, opt, threeD: false);
        var after = SoftIcpRefiner.Refine2D(frags, opt);

        Assert(after.MeanRimGap < before.MeanRimGap,
            $"rim gap must drop: before={before.MeanRimGap:F4} after={after.MeanRimGap:F4}");
        Assert(after.MeanRimGap < 0.5,
            $"rim gap must approach contact, got {after.MeanRimGap:F4}");
        // Anchor B did not move.
        Assert(fragB.Delta.IsIdentity, "anchored fragment must not move");
        // A's recovered increment should approximate the known motion.
        Assert(!fragA.Delta.IsIdentity, "moving fragment must have a non-identity increment");
    }

    // 3D analogue with null solids (no penetration term): EM weighted-Kabsch on
    // 3D rim points recovers a known SE(3) alignment. Uses MathNet SVD only.
    public static void Em_RecoversKnown3DAlignment()
    {
        var rim = MakeHelixRim(48);
        var axis = new Vector3d(0.3, 1, 0.5); axis.Unitize();
        var motion = Transform.Multiply(
            Transform.Translation(2.0, -1.5, 1.0),
            Transform.Rotation(RhinoMath.ToRadians(8.0), axis, Point3d.Origin));
        var rimB = Apply(rim, motion);

        var fragA = new SoftIcpRefiner.Fragment("A", Clone(rim), solid: null, contour2D: null, anchored: false);
        var fragB = new SoftIcpRefiner.Fragment("B", rimB, solid: null, contour2D: null, anchored: true);
        var frags = new List<SoftIcpRefiner.Fragment> { fragA, fragB };

        var opt = new SoftIcpOptions { MaxIterations = 80 };
        var before = SoftIcpRefiner.Measure(frags, opt, threeD: true);
        var after = SoftIcpRefiner.Refine3D(frags, opt);

        Assert(after.MeanRimGap < before.MeanRimGap,
            $"3D rim gap must drop: before={before.MeanRimGap:F4} after={after.MeanRimGap:F4}");
        Assert(after.MeanRimGap < 0.5,
            $"3D rim gap must approach contact, got {after.MeanRimGap:F4}");

        // Reflection guard: recovered rotation must be proper (det +1), not a
        // mirror — same invariant ConstrainedIcp3D guards.
        double det = Det3(fragA.Delta);
        Assert(Close(det, 1.0, 1e-3), $"recovered 3D rotation det must be +1, got {det}");
    }

    // ---- (4) Determinism ---------------------------------------------------

    public static void Em_IsDeterministic()
    {
        double[] Run()
        {
            var rim = MakeWigglyRing(40, 10.0);
            var motion = Transform.Multiply(
                Transform.Translation(5.0, -3.0, 0.0),
                Transform.Rotation(RhinoMath.ToRadians(12.0), Vector3d.ZAxis, Point3d.Origin));
            var rimB = Apply(rim, motion);
            var fragA = new SoftIcpRefiner.Fragment("A", Clone(rim), null, null, false);
            var fragB = new SoftIcpRefiner.Fragment("B", rimB, null, null, true);
            var frags = new List<SoftIcpRefiner.Fragment> { fragA, fragB };
            var r = SoftIcpRefiner.Refine2D(frags, new SoftIcpOptions { MaxIterations = 60 });
            var d = fragA.Delta;
            return new[] { r.MeanRimGap, d.M00, d.M01, d.M03, d.M10, d.M11, d.M13 };
        }
        var a = Run();
        var b = Run();
        for (int i = 0; i < a.Length; i++)
            Assert(a[i].Equals(b[i]), $"Soft-ICP determinism drift at {i}: {a[i]} vs {b[i]}");
    }

    // ---- helpers ----------------------------------------------------------

    private static Point3d[] MakeWigglyRing(int n, double radius)
    {
        var pts = new Point3d[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2.0 * Math.PI * i / n;
            double r = radius + 1.5 * Math.Sin(5.0 * a); // non-circular -> unique alignment
            pts[i] = new Point3d(r * Math.Cos(a), r * Math.Sin(a), 0.0);
        }
        return pts;
    }

    private static Point3d[] MakeHelixRim(int n)
    {
        var pts = new Point3d[n];
        for (int i = 0; i < n; i++)
        {
            double t = i * 0.25;
            pts[i] = new Point3d(Math.Cos(t) * 10, Math.Sin(t) * 10, t * 0.5 + 1.2 * Math.Sin(3 * t));
        }
        return pts;
    }

    private static Point3d[] Apply(Point3d[] src, Transform t)
    {
        var dst = new Point3d[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var p = src[i]; p.Transform(t); dst[i] = p;
        }
        return dst;
    }

    private static Point3d[] Clone(Point3d[] src)
    {
        var dst = new Point3d[src.Length];
        Array.Copy(src, dst, src.Length);
        return dst;
    }

    private static double Det3(Transform r)
        => r.M00 * (r.M11 * r.M22 - r.M12 * r.M21)
         - r.M01 * (r.M10 * r.M22 - r.M12 * r.M20)
         + r.M02 * (r.M10 * r.M21 - r.M11 * r.M20);

    private static bool Close(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
