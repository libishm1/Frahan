#nullable disable
using System;

namespace Frahan.Core.Registration;

// =============================================================================
// GeoreferenceMath — WGS84 ellipsoid + LLH↔ECEF + ENU rotation + UTM↔LLH.
// Pure managed, zero third-party dependencies.
//
// Phase I2 of the UX architecture report §7.7.F rollout.
//
// References:
//   - Bowring, B.R. "Transformation from spatial to geographical coordinates."
//     Survey Review 23.181 (1976): 323-327. (LLH ↔ ECEF)
//   - Karney, C.F.F. "Transverse Mercator with an accuracy of a few nanometres."
//     Journal of Geodesy 85.8 (2011): 475-485. (UTM ↔ LLH)
//   - Snyder, J.P. "Map Projections: A Working Manual." USGS PP 1395 (1987).
//
// All angles are in radians inside the API; degree-based helpers are
// provided for the GH-side wiring (where users type degrees).
//
// Coordinate conventions:
//   LLH:  (latitude, longitude, height-above-ellipsoid) in (rad, rad, m).
//   ECEF: Earth-centred, Earth-fixed Cartesian (X, Y, Z) in metres.
//   ENU:  Local east-north-up Cartesian, relative to a chosen LLH origin.
//   UTM:  (easting, northing) in metres, plus zone (1..60) and hemisphere.
// =============================================================================

/// <summary>
/// WGS84 ellipsoid constants. Public so users can verify the values
/// the math uses.
/// </summary>
public static class Wgs84
{
    /// <summary>Semi-major axis in metres.</summary>
    public const double A = 6378137.0;
    /// <summary>Flattening (dimensionless).</summary>
    public const double F = 1.0 / 298.257223563;
    /// <summary>Semi-minor axis in metres.</summary>
    public static readonly double B = A * (1.0 - F);
    /// <summary>First eccentricity squared.</summary>
    public static readonly double E2 = F * (2.0 - F);
    /// <summary>Second eccentricity squared.</summary>
    public static readonly double Ep2 = E2 / (1.0 - E2);
}

/// <summary>
/// LLH ↔ ECEF ↔ ENU ↔ UTM conversions for georeferencing scan data
/// onto a real-world frame. All static; no state.
/// </summary>
public static class GeoreferenceMath
{
    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    // ─── LLH ↔ ECEF (Bowring) ─────────────────────────────────────────────

    /// <summary>
    /// Latitude / longitude / height-above-ellipsoid (radians, radians,
    /// metres) → ECEF (metres). Closed-form Bowring 1976 formulation.
    /// </summary>
    public static void LlhToEcef(
        double latRad, double lonRad, double heightM,
        out double x, out double y, out double z)
    {
        double sinLat = Math.Sin(latRad);
        double cosLat = Math.Cos(latRad);
        double sinLon = Math.Sin(lonRad);
        double cosLon = Math.Cos(lonRad);

        double n = Wgs84.A / Math.Sqrt(1.0 - Wgs84.E2 * sinLat * sinLat);
        x = (n + heightM) * cosLat * cosLon;
        y = (n + heightM) * cosLat * sinLon;
        z = (n * (1.0 - Wgs84.E2) + heightM) * sinLat;
    }

    /// <summary>
    /// ECEF (metres) → latitude / longitude / height (radians, radians,
    /// metres). Bowring 1976 closed-form (non-iterative).
    /// </summary>
    public static void EcefToLlh(
        double x, double y, double z,
        out double latRad, out double lonRad, out double heightM)
    {
        double p = Math.Sqrt(x * x + y * y);
        lonRad = Math.Atan2(y, x);

        // Bowring's auxiliary angle θ.
        double theta = Math.Atan2(z * Wgs84.A, p * Wgs84.B);
        double sinTheta = Math.Sin(theta);
        double cosTheta = Math.Cos(theta);

        latRad = Math.Atan2(
            z + Wgs84.Ep2 * Wgs84.B * sinTheta * sinTheta * sinTheta,
            p - Wgs84.E2 * Wgs84.A * cosTheta * cosTheta * cosTheta);

        double sinLat = Math.Sin(latRad);
        double n = Wgs84.A / Math.Sqrt(1.0 - Wgs84.E2 * sinLat * sinLat);
        // Near the poles cos(lat) → 0 so use the z-based formula there.
        double cosLat = Math.Cos(latRad);
        if (Math.Abs(cosLat) > 1e-12)
            heightM = p / cosLat - n;
        else
            heightM = z / sinLat - n * (1.0 - Wgs84.E2);
    }

    // ─── ECEF ↔ ENU (rigid rotation about a chosen LLH origin) ───────────

    /// <summary>
    /// Transform an ECEF point into local east-north-up coordinates
    /// centred at the given LLH origin. Pure rigid rotation + translation.
    /// </summary>
    public static void EcefToEnu(
        double targetX, double targetY, double targetZ,
        double originLatRad, double originLonRad, double originHeightM,
        out double e, out double n, out double u)
    {
        LlhToEcef(originLatRad, originLonRad, originHeightM,
            out double ox, out double oy, out double oz);

        double dx = targetX - ox;
        double dy = targetY - oy;
        double dz = targetZ - oz;

        double sinLat = Math.Sin(originLatRad);
        double cosLat = Math.Cos(originLatRad);
        double sinLon = Math.Sin(originLonRad);
        double cosLon = Math.Cos(originLonRad);

        e = -sinLon * dx + cosLon * dy;
        n = -sinLat * cosLon * dx - sinLat * sinLon * dy + cosLat * dz;
        u =  cosLat * cosLon * dx + cosLat * sinLon * dy + sinLat * dz;
    }

    /// <summary>
    /// Transform an ENU point back into ECEF, given the LLH origin used
    /// to define the ENU frame.
    /// </summary>
    public static void EnuToEcef(
        double e, double n, double u,
        double originLatRad, double originLonRad, double originHeightM,
        out double x, out double y, out double z)
    {
        LlhToEcef(originLatRad, originLonRad, originHeightM,
            out double ox, out double oy, out double oz);

        double sinLat = Math.Sin(originLatRad);
        double cosLat = Math.Cos(originLatRad);
        double sinLon = Math.Sin(originLonRad);
        double cosLon = Math.Cos(originLonRad);

        double dx = -sinLon * e - sinLat * cosLon * n + cosLat * cosLon * u;
        double dy =  cosLon * e - sinLat * sinLon * n + cosLat * sinLon * u;
        double dz =  cosLat * n + sinLat * u;

        x = ox + dx;
        y = oy + dy;
        z = oz + dz;
    }

    // ─── LLH ↔ UTM (Karney 2011, truncated to series order 6) ────────────

    /// <summary>
    /// Latitude / longitude (radians) → UTM easting / northing (metres),
    /// zone (1..60), hemisphere flag. Latitudes outside [-80°, +84°]
    /// fall outside the UTM zone system; caller must handle polar cases
    /// (UPS) separately.
    /// </summary>
    public static void LlhToUtm(
        double latRad, double lonRad,
        out double easting, out double northing,
        out int zone, out bool isNorthernHemisphere)
    {
        double latDeg = latRad * Rad2Deg;
        double lonDeg = lonRad * Rad2Deg;

        // Standard UTM zone numbering: zone 1 starts at -180°.
        zone = (int)Math.Floor((lonDeg + 180.0) / 6.0) + 1;
        if (zone < 1) zone = 1;
        if (zone > 60) zone = 60;
        isNorthernHemisphere = latDeg >= 0.0;

        ProjectLlhToUtmZone(latRad, lonRad, zone, out easting, out northing);
    }

    /// <summary>
    /// UTM easting / northing (metres) + zone + hemisphere → latitude /
    /// longitude (radians). Karney 2011 series, truncated to order 6.
    /// </summary>
    public static void UtmToLlh(
        double easting, double northing,
        int zone, bool isNorthernHemisphere,
        out double latRad, out double lonRad)
    {
        if (zone < 1 || zone > 60)
            throw new ArgumentOutOfRangeException(nameof(zone), "UTM zone must be in [1, 60]");

        const double K0 = 0.9996;
        const double FalseEasting = 500000.0;
        double falseNorthing = isNorthernHemisphere ? 0.0 : 10000000.0;
        double lon0Rad = ((zone - 1) * 6.0 - 180.0 + 3.0) * Deg2Rad;

        double n = Wgs84.F / (2.0 - Wgs84.F);
        double a = (Wgs84.A / (1.0 + n)) * (1.0 + n * n / 4.0 + n * n * n * n / 64.0);

        double xi  = (northing - falseNorthing) / (a * K0);
        double eta = (easting - FalseEasting)   / (a * K0);

        // Karney's β coefficients (inverse direction).
        double[] beta = {
            n * (1.0 / 2.0 + n * (-2.0 / 3.0 + n * (37.0 / 96.0))),
            n * n * (1.0 / 48.0 + n * (1.0 / 15.0)),
            n * n * n * (17.0 / 480.0),
        };

        double xiPrime = xi;
        double etaPrime = eta;
        for (int j = 0; j < beta.Length; j++)
        {
            int k = 2 * (j + 1);
            xiPrime  -= beta[j] * Math.Sin(k * xi) * Math.Cosh(k * eta);
            etaPrime -= beta[j] * Math.Cos(k * xi) * Math.Sinh(k * eta);
        }

        double chi = Math.Asin(Math.Sin(xiPrime) / Math.Cosh(etaPrime));

        // δ-coefficients to recover geodetic latitude from conformal latitude.
        double[] delta = {
            n * (2.0 + n * (-2.0 / 3.0 + n * (-2.0))),
            n * n * (7.0 / 3.0 + n * (-8.0 / 5.0)),
            n * n * n * (56.0 / 15.0),
        };
        double lat = chi;
        for (int j = 0; j < delta.Length; j++)
        {
            int k = 2 * (j + 1);
            lat += delta[j] * Math.Sin(k * chi);
        }
        latRad = lat;

        lonRad = lon0Rad + Math.Atan2(Math.Sinh(etaPrime), Math.Cos(xiPrime));
    }

    private static void ProjectLlhToUtmZone(
        double latRad, double lonRad, int zone,
        out double easting, out double northing)
    {
        const double K0 = 0.9996;
        const double FalseEasting = 500000.0;
        bool isNorth = latRad >= 0.0;
        double falseNorthing = isNorth ? 0.0 : 10000000.0;
        double lon0Rad = ((zone - 1) * 6.0 - 180.0 + 3.0) * Deg2Rad;
        double dLon = lonRad - lon0Rad;

        double n = Wgs84.F / (2.0 - Wgs84.F);
        double a = (Wgs84.A / (1.0 + n)) * (1.0 + n * n / 4.0 + n * n * n * n / 64.0);

        // Conformal latitude.
        double sinLat = Math.Sin(latRad);
        double e = Math.Sqrt(Wgs84.E2);
        double t = Math.Sinh(Atanh(sinLat) - e * Atanh(e * sinLat));
        double xiPrime  = Math.Atan2(t, Math.Cos(dLon));
        double etaPrime = Atanh(Math.Sin(dLon) / Math.Sqrt(1.0 + t * t));

        // Karney's α coefficients (forward direction).
        double[] alpha = {
            n * (1.0 / 2.0 + n * (-2.0 / 3.0 + n * (5.0 / 16.0))),
            n * n * (13.0 / 48.0 + n * (-3.0 / 5.0)),
            n * n * n * (61.0 / 240.0),
        };

        double xi = xiPrime;
        double eta = etaPrime;
        for (int j = 0; j < alpha.Length; j++)
        {
            int k = 2 * (j + 1);
            xi  += alpha[j] * Math.Sin(k * xiPrime) * Math.Cosh(k * etaPrime);
            eta += alpha[j] * Math.Cos(k * xiPrime) * Math.Sinh(k * etaPrime);
        }

        easting  = FalseEasting   + K0 * a * eta;
        northing = falseNorthing  + K0 * a * xi;
    }

    private static double Atanh(double x) => 0.5 * Math.Log((1.0 + x) / (1.0 - x));

    // ─── Degree-based convenience wrappers ───────────────────────────────

    /// <summary>Convenience: LLH in degrees / degrees / metres → ECEF.</summary>
    public static void LlhDegToEcef(
        double latDeg, double lonDeg, double heightM,
        out double x, out double y, out double z)
        => LlhToEcef(latDeg * Deg2Rad, lonDeg * Deg2Rad, heightM, out x, out y, out z);

    /// <summary>Convenience: ECEF → LLH in degrees / degrees / metres.</summary>
    public static void EcefToLlhDeg(
        double x, double y, double z,
        out double latDeg, out double lonDeg, out double heightM)
    {
        EcefToLlh(x, y, z, out double latRad, out double lonRad, out heightM);
        latDeg = latRad * Rad2Deg;
        lonDeg = lonRad * Rad2Deg;
    }
}
