#nullable disable
using System;
using Frahan.Core.Registration;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// RegistrationApiTests — Phase I5 of the UX architecture report §7.7.F.
//
// Covers:
//   - RegistrationApi.SolveFromPoints round-trips on identity, translation,
//     rotation, and combined transforms with a Rhino Point3d front-end.
//   - GeoreferenceMath LLH↔ECEF round-trips at multiple latitudes.
//   - GeoreferenceMath ECEF↔ENU rigid round-trip about a chosen origin.
//   - GeoreferenceMath UTM↔LLH round-trip at known control points.
//
// Pure managed — no Rhino runtime required (Point3d / Transform / Vector3d
// are managed types in RhinoCommon and resolve from the test project's
// HintPath without invoking native code).
// =============================================================================

static class RegistrationApiTests
{
    // ─── RegistrationApi: identity ─────────────────────────────────────────

    public static void SolveFromPoints_Identity_RecoversIdentityTransform()
    {
        var src = MakeUnitCube();
        var result = RegistrationApi.SolveFromPoints(src, src);
        AssertNear(result.Transform.M00, 1.0, 1e-9, "M00");
        AssertNear(result.Transform.M11, 1.0, 1e-9, "M11");
        AssertNear(result.Transform.M22, 1.0, 1e-9, "M22");
        AssertNear(result.Transform.M03, 0.0, 1e-9, "M03");
        AssertNear(result.Transform.M13, 0.0, 1e-9, "M13");
        AssertNear(result.Transform.M23, 0.0, 1e-9, "M23");
        Assert(result.RmsError < 1e-9, $"RMS should be ~0, got {result.RmsError}");
        Assert(result.PerPairResiduals.Length == src.Length, "per-pair residual count must match input");
    }

    // ─── RegistrationApi: pure translation ─────────────────────────────────

    public static void SolveFromPoints_PureTranslation_RecoversTranslationOnly()
    {
        var src = MakeUnitCube();
        var dst = TranslatePoints(src, 5.0, -3.0, 7.0);
        var result = RegistrationApi.SolveFromPoints(src, dst);
        AssertNear(result.Transform.M00, 1.0, 1e-9, "M00");
        AssertNear(result.Transform.M11, 1.0, 1e-9, "M11");
        AssertNear(result.Transform.M22, 1.0, 1e-9, "M22");
        AssertNear(result.Transform.M03, 5.0, 1e-9, "M03");
        AssertNear(result.Transform.M13, -3.0, 1e-9, "M13");
        AssertNear(result.Transform.M23, 7.0, 1e-9, "M23");
        Assert(result.RmsError < 1e-9, $"RMS should be ~0, got {result.RmsError}");
    }

    // ─── RegistrationApi: combined rotation + translation ──────────────────

    public static void SolveFromPoints_RotationPlusTranslation_RoundTrips()
    {
        var src = MakeUnitCube();
        // Apply a known transform: 45° rotation about Y, then translate (1, 2, 3).
        var known = Transform.Translation(1.0, 2.0, 3.0)
                  * Transform.Rotation(Math.PI / 4.0, Vector3d.YAxis, Point3d.Origin);
        var dst = new Point3d[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var p = src[i];
            p.Transform(known);
            dst[i] = p;
        }
        var result = RegistrationApi.SolveFromPoints(src, dst);
        // Apply recovered transform to source and compare against destination.
        for (int i = 0; i < src.Length; i++)
        {
            var p = src[i];
            p.Transform(result.Transform);
            Assert(p.DistanceTo(dst[i]) < 1e-9,
                $"vert {i} round-trip distance {p.DistanceTo(dst[i])} > 1e-9");
        }
        Assert(result.RmsError < 1e-9, $"RMS should be ~0, got {result.RmsError}");
    }

    // ─── RegistrationApi: noisy markers produce non-zero RMS ───────────────

    public static void SolveFromPoints_NoisyMarkers_ProducesNonZeroRms()
    {
        var src = MakeUnitCube();
        var dst = new Point3d[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            // Identity transform + 1 mm jitter per coordinate. RMS should
            // hover around the jitter scale.
            double jx = ((i * 7919) % 17 - 8) * 1e-4;
            double jy = ((i * 7919) % 13 - 6) * 1e-4;
            double jz = ((i * 7919) % 11 - 5) * 1e-4;
            dst[i] = new Point3d(src[i].X + jx, src[i].Y + jy, src[i].Z + jz);
        }
        var result = RegistrationApi.SolveFromPoints(src, dst);
        Assert(result.RmsError > 1e-8 && result.RmsError < 1e-2,
            $"RMS expected in (1e-8, 1e-2) range, got {result.RmsError}");
    }

    // ─── RegistrationApi: input validation ─────────────────────────────────

    public static void SolveFromPoints_MismatchedCounts_Throws()
    {
        var a = MakeUnitCube();
        var b = new Point3d[a.Length - 1];
        try
        {
            RegistrationApi.SolveFromPoints(a, b);
            throw new Exception("Expected ArgumentException for mismatched counts.");
        }
        catch (ArgumentException) { /* expected */ }
    }

    public static void SolveFromPoints_FewerThan3Pairs_Throws()
    {
        var a = new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0) };
        var b = new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0) };
        try
        {
            RegistrationApi.SolveFromPoints(a, b);
            throw new Exception("Expected ArgumentException for N<3.");
        }
        catch (ArgumentException) { /* expected */ }
    }

    // ─── GeoreferenceMath: LLH ↔ ECEF round-trip ───────────────────────────

    public static void GeoreferenceMath_LlhEcefRoundTrip_PreservesPosition()
    {
        // Three representative latitudes: equator, mid-latitude, near-pole.
        var cases = new (double latDeg, double lonDeg, double h)[]
        {
            (  0.0,    0.0,    0.0),
            ( 45.5,   -73.6,  150.0),  // Montréal
            (-33.9,   151.2,   10.0),  // Sydney
            ( 78.2,    15.6,  500.0),  // Svalbard
        };
        const double Deg2Rad = Math.PI / 180.0;
        foreach (var c in cases)
        {
            GeoreferenceMath.LlhToEcef(
                c.latDeg * Deg2Rad, c.lonDeg * Deg2Rad, c.h,
                out double x, out double y, out double z);
            GeoreferenceMath.EcefToLlh(x, y, z,
                out double latRtRad, out double lonRtRad, out double hRt);
            AssertNear(latRtRad / Deg2Rad, c.latDeg, 1e-7, $"lat round-trip @ {c.latDeg}°");
            AssertNear(lonRtRad / Deg2Rad, c.lonDeg, 1e-7, $"lon round-trip @ {c.lonDeg}°");
            AssertNear(hRt, c.h, 1e-3, $"h round-trip @ {c.h} m");
        }
    }

    // ─── GeoreferenceMath: ENU ↔ ECEF round-trip about an origin ───────────

    public static void GeoreferenceMath_EnuEcefRoundTrip_PreservesPosition()
    {
        const double Deg2Rad = Math.PI / 180.0;
        // Origin: Chennai-ish (Tamil Nadu granite quarry latitude).
        double lat0 = 12.97 * Deg2Rad;
        double lon0 = 80.21 * Deg2Rad;
        double h0 = 5.0;
        // Pick an ECEF point ~50 m north-east-up from the origin and check
        // that it round-trips through ENU.
        GeoreferenceMath.LlhToEcef(lat0, lon0, h0,
            out double x0, out double y0, out double z0);
        double targetX = x0 + 25.3;
        double targetY = y0 - 11.7;
        double targetZ = z0 + 8.4;
        GeoreferenceMath.EcefToEnu(targetX, targetY, targetZ, lat0, lon0, h0,
            out double e, out double n, out double u);
        GeoreferenceMath.EnuToEcef(e, n, u, lat0, lon0, h0,
            out double xRt, out double yRt, out double zRt);
        AssertNear(xRt, targetX, 1e-6, "ENU→ECEF X round-trip");
        AssertNear(yRt, targetY, 1e-6, "ENU→ECEF Y round-trip");
        AssertNear(zRt, targetZ, 1e-6, "ENU→ECEF Z round-trip");
    }

    // ─── GeoreferenceMath: UTM ↔ LLH round-trip ────────────────────────────

    public static void GeoreferenceMath_UtmLlhRoundTrip_PreservesPosition()
    {
        // Spot-checks at a range of latitudes / longitudes inside the UTM domain.
        var cases = new (double latDeg, double lonDeg)[]
        {
            (  0.0,    0.0),
            ( 12.97,  80.21),  // Chennai
            ( 51.50,  -0.13),  // London
            (-33.86, 151.21),  // Sydney
            ( 60.17,  24.94),  // Helsinki
        };
        const double Deg2Rad = Math.PI / 180.0;
        foreach (var c in cases)
        {
            double latRad = c.latDeg * Deg2Rad;
            double lonRad = c.lonDeg * Deg2Rad;
            GeoreferenceMath.LlhToUtm(latRad, lonRad,
                out double easting, out double northing, out int zone, out bool isNorth);
            GeoreferenceMath.UtmToLlh(easting, northing, zone, isNorth,
                out double latRtRad, out double lonRtRad);
            // ~1 mm tolerance is typical for the truncated Karney series at
            // these scales.
            AssertNear(latRtRad / Deg2Rad, c.latDeg, 1e-7, $"UTM lat round-trip @ {c.latDeg}°");
            AssertNear(lonRtRad / Deg2Rad, c.lonDeg, 1e-7, $"UTM lon round-trip @ {c.lonDeg}°");
        }
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static Point3d[] MakeUnitCube() => new[]
    {
        new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0), new Point3d(0, 1, 0),
        new Point3d(0, 0, 1), new Point3d(1, 0, 1), new Point3d(1, 1, 1), new Point3d(0, 1, 1),
    };

    private static Point3d[] TranslatePoints(Point3d[] src, double dx, double dy, double dz)
    {
        var dst = new Point3d[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = new Point3d(src[i].X + dx, src[i].Y + dy, src[i].Z + dz);
        return dst;
    }

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} ± {tol}, got {actual}");
    }
}
