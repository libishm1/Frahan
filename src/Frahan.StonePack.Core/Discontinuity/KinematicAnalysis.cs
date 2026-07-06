#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Frahan.Core.Discontinuity;

// =============================================================================
// KinematicAnalysis -- Markland / Hoek-Bray kinematic feasibility tests for
// rock-slope (and quarry-bench) stability against a discontinuity-set fabric.
// The bread-and-butter rock-engineering screen absent from a raw stereonet:
// given a cut face and a friction angle, which sets can fail, and by what mode?
//
// Modes:
//   PLANAR sliding (Markland 1972): a set daylights and exceeds friction --
//     dip_set < apparent dip of face along the set's dip direction (daylights),
//     dip_set > friction, and the set dip-direction within +/- lateralLimit of
//     the face dip-direction.
//   WEDGE sliding (Hoek & Bray): the line of intersection of two sets plunges
//     out of the face -- plunge < apparent dip of the face along the trend
//     (daylights), plunge > friction.
//   FLEXURAL TOPPLING (Goodman & Bray 1976): a set dips steeply INTO the face
//     (dip-direction within +/- lateralLimit of face dip-direction + 180) and
//     satisfies the inter-layer slip condition (90 - dip_set) <= (dip_face - friction).
//
// Convention: dip in [0,90], dip-direction azimuth clockwise from North in
// [0,360); friction is the joint friction angle (deg). Apparent dip of a face
// (dip psi_f, dip-dir alpha_f) along azimuth alpha:
//   d = az(alpha, alpha_f);  app = atan(tan(psi_f) cos d) if |d| < 90 else <= 0
//   (a face does not daylight in a direction more than 90 deg off its dip).
//
// References:
//   - Markland, J. T. (1972). A useful technique for estimating the stability of
//     rock slopes when the rigid wedge sliding type of failure is expected.
//   - Hoek, E. & Bray, J. W. (1981). Rock Slope Engineering. 3rd ed.
//   - Goodman, R. E. & Bray, J. W. (1976). Toppling of rock slopes.
//   - Wyllie, D. C. & Mah, C. W. (2004). Rock Slope Engineering. 4th ed. Ch.7.
//
// Pure managed arithmetic, Rhino-free (headless / service ready), unit-testable.
// =============================================================================

/// <summary>Kinematic failure modes screened by <see cref="KinematicAnalysis"/>.</summary>
public enum KinematicMode { PlanarSliding, WedgeSliding, FlexuralToppling }

/// <summary>One kinematic screen result (a set, or a set pair for wedges).</summary>
public sealed class KinematicHit
{
    public KinematicMode Mode;
    /// <summary>Set index (0-based). For a wedge, the first set of the pair.</summary>
    public int SetA;
    /// <summary>Second set of a wedge pair; -1 for single-set modes.</summary>
    public int SetB = -1;
    /// <summary>Governing feature dip / plunge (deg): the set dip, or the intersection plunge.</summary>
    public double DipDeg;
    /// <summary>Governing feature dip-direction / trend (deg).</summary>
    public double DipDirDeg;
    /// <summary>True if the mode is kinematically feasible (unstable) under the given face + friction.</summary>
    public bool Feasible;
    /// <summary>Which of the three conditions (daylight / friction / direction) passed.</summary>
    public bool Daylights, ExceedsFriction, DirectionOk;
    public string Why = "";
}

/// <summary>Full kinematic screen over all sets + pairs for one cut face.</summary>
public sealed class KinematicResult
{
    public List<KinematicHit> Planar = new List<KinematicHit>();
    public List<KinematicHit> Wedge = new List<KinematicHit>();
    public List<KinematicHit> Toppling = new List<KinematicHit>();
    public double SlopeDip, SlopeDipDir, Friction, LateralLimit;
    public int FeasibleCount;
    public string Report = "";
}

public static class KinematicAnalysis
{
    private const double D2R = Math.PI / 180.0, R2D = 180.0 / Math.PI;

    /// <summary>Signed azimuth difference a-b folded to (-180, 180].</summary>
    public static double AzDiff(double a, double b)
    {
        double d = ((a - b) % 360.0 + 540.0) % 360.0 - 180.0;
        return d;
    }

    /// <summary>Apparent dip (deg) of a face (dip psi_f, dip-dir alpha_f) in azimuth alpha. &lt;= 0 if the face does not daylight that way.</summary>
    public static double ApparentDip(double faceDip, double faceDipDir, double azimuth)
    {
        double d = AzDiff(azimuth, faceDipDir);
        if (Math.Abs(d) >= 90.0) return 0.0;
        double app = Math.Atan(Math.Tan(faceDip * D2R) * Math.Cos(d * D2R)) * R2D;
        return app;
    }

    /// <summary>Plunge/trend (deg) of the line of intersection of two planes given by dip/dip-direction.</summary>
    public static void Intersection(double dipA, double ddA, double dipB, double ddB, out double plunge, out double trend)
    {
        var (ax, ay, az) = LowerHemisphereNormal(dipA, ddA);
        var (bx, by, bz) = LowerHemisphereNormal(dipB, ddB);
        // L = na x nb (intersection direction of the two planes)
        double lx = ay * bz - az * by;
        double ly = az * bx - ax * bz;
        double lz = ax * by - ay * bx;
        double sq = lx * lx + ly * ly + lz * lz;
        if (sq < 1e-18) { plunge = 0; trend = 0; return; }
        double len = Math.Sqrt(sq);
        lx /= len; ly /= len; lz /= len;
        if (lz > 0) { lx = -lx; ly = -ly; lz = -lz; }   // point the line downward
        double horiz = Math.Sqrt(lx * lx + ly * ly);
        plunge = Math.Atan2(-lz, horiz) * R2D;           // -lz >= 0
        trend = (Math.Atan2(lx, ly) * R2D + 360.0) % 360.0;
    }

    // Rhino-free downward-pointing plane normal (dip / dip-direction), mirroring
    // OrientationMath.NormalFromDipDipDir + LowerHemisphere so this geology
    // component needs no RhinoCommon (headless / service ready).
    private static (double X, double Y, double Z) LowerHemisphereNormal(double dipDeg, double dipDirDeg)
    {
        double dip = dipDeg * D2R, dd = dipDirDeg * D2R;
        double s = Math.Sin(dip);
        double x = s * Math.Sin(dd), y = s * Math.Cos(dd), z = -Math.Cos(dip);
        double len = Math.Sqrt(x * x + y * y + z * z);
        if (len > 0) { x /= len; y /= len; z /= len; }
        if (z > 1e-12) { x = -x; y = -y; z = -z; }        // flip to lower hemisphere
        else if (Math.Abs(z) <= 1e-12 &&
                 (x < -1e-12 || (Math.Abs(x) <= 1e-12 && y < 0)))
        { x = -x; y = -y; z = -z; }                        // equator tie-break (x then y)
        return (x, y, z);
    }

    /// <summary>Screen every set (planar, toppling) and every pair (wedge) against one cut face.</summary>
    public static KinematicResult Analyze(
        IReadOnlyList<double> setDip,
        IReadOnlyList<double> setDipDir,
        double slopeDipDeg,
        double slopeDipDirDeg,
        double frictionDeg,
        double lateralLimitDeg = 20.0)
    {
        var res = new KinematicResult
        {
            SlopeDip = slopeDipDeg,
            SlopeDipDir = slopeDipDirDeg,
            Friction = frictionDeg,
            LateralLimit = lateralLimitDeg
        };
        if (setDip == null || setDipDir == null) { res.Report = "No sets."; return res; }
        int n = Math.Min(setDip.Count, setDipDir.Count);

        // ---- planar sliding + flexural toppling (per set) ----
        for (int i = 0; i < n; i++)
        {
            double dp = setDip[i], dd = setDipDir[i];

            // planar: daylights if set dips out of the face and less steep than the face's apparent dip in that direction
            double appDipAlongSet = ApparentDip(slopeDipDeg, slopeDipDirDeg, dd);
            bool dirOk = Math.Abs(AzDiff(dd, slopeDipDirDeg)) <= lateralLimitDeg;
            bool daylights = dirOk && appDipAlongSet > 0 && dp < appDipAlongSet;
            bool friction = dp > frictionDeg;
            var hp = new KinematicHit
            {
                Mode = KinematicMode.PlanarSliding, SetA = i, DipDeg = dp, DipDirDeg = dd,
                Daylights = daylights, ExceedsFriction = friction, DirectionOk = dirOk,
                Feasible = daylights && friction
            };
            hp.Why = $"dip {dp:F0} vs face app-dip {appDipAlongSet:F0} (daylight {(daylights ? "Y" : "N")}), " +
                     $"vs friction {frictionDeg:F0} ({(friction ? "Y" : "N")}), " +
                     $"dip-dir off face {AzDiff(dd, slopeDipDirDeg):F0} deg ({(dirOk ? "in" : "out of")} +/-{lateralLimitDeg:F0})";
            res.Planar.Add(hp);

            // flexural toppling: dips steeply INTO the face + inter-layer slip
            bool topDir = Math.Abs(Math.Abs(AzDiff(dd, slopeDipDirDeg)) - 180.0) <= lateralLimitDeg;
            bool slip = (90.0 - dp) <= (slopeDipDeg - frictionDeg);
            var ht = new KinematicHit
            {
                Mode = KinematicMode.FlexuralToppling, SetA = i, DipDeg = dp, DipDirDeg = dd,
                Daylights = topDir, ExceedsFriction = slip, DirectionOk = topDir,
                Feasible = topDir && slip
            };
            ht.Why = $"dips into face ({(topDir ? "Y" : "N")}, off-180 {Math.Abs(AzDiff(dd, slopeDipDirDeg)) - 180.0:F0}), " +
                     $"slip (90-{dp:F0}) <= ({slopeDipDeg:F0}-{frictionDeg:F0}) ({(slip ? "Y" : "N")})";
            res.Toppling.Add(ht);
        }

        // ---- wedge sliding (per pair) ----
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                Intersection(setDip[i], setDipDir[i], setDip[j], setDipDir[j], out double plunge, out double trend);
                double appDip = ApparentDip(slopeDipDeg, slopeDipDirDeg, trend);
                bool daylights = appDip > 0 && plunge < appDip;
                bool friction = plunge > frictionDeg;
                var hw = new KinematicHit
                {
                    Mode = KinematicMode.WedgeSliding, SetA = i, SetB = j, DipDeg = plunge, DipDirDeg = trend,
                    Daylights = daylights, ExceedsFriction = friction, DirectionOk = daylights,
                    Feasible = daylights && friction
                };
                hw.Why = $"intersection plunge {plunge:F0}/trend {trend:F0}; face app-dip {appDip:F0} " +
                         $"(daylight {(daylights ? "Y" : "N")}), vs friction {frictionDeg:F0} ({(friction ? "Y" : "N")})";
                res.Wedge.Add(hw);
            }

        foreach (var h in res.Planar) if (h.Feasible) res.FeasibleCount++;
        foreach (var h in res.Wedge) if (h.Feasible) res.FeasibleCount++;
        foreach (var h in res.Toppling) if (h.Feasible) res.FeasibleCount++;

        res.Report = BuildReport(res, n);
        return res;
    }

    private static string BuildReport(KinematicResult r, int n)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kinematic feasibility (Markland / Hoek-Bray / Goodman-Bray).");
        sb.AppendLine($"  Cut face: dip {r.SlopeDip:F0} / dip-dir {r.SlopeDipDir:F0};  friction {r.Friction:F0} deg;  lateral +/-{r.LateralLimit:F0} deg");
        sb.AppendLine($"  {n} sets -> {r.FeasibleCount} feasible failure mode(s).");
        AppendMode(sb, "PLANAR sliding", r.Planar);
        AppendMode(sb, "WEDGE sliding", r.Wedge);
        AppendMode(sb, "FLEXURAL toppling", r.Toppling);
        if (r.FeasibleCount == 0) sb.AppendLine("  No kinematically feasible failure -- fabric is favourable for this face.");
        return sb.ToString().TrimEnd();
    }

    private static void AppendMode(StringBuilder sb, string title, List<KinematicHit> hits)
    {
        int feas = 0; foreach (var h in hits) if (h.Feasible) feas++;
        sb.AppendLine($"  {title}: {feas} feasible / {hits.Count} screened");
        foreach (var h in hits)
        {
            if (!h.Feasible) continue;
            string who = h.SetB >= 0 ? $"S{h.SetA + 1}xS{h.SetB + 1}" : $"S{h.SetA + 1}";
            sb.AppendLine($"    ! {who}: {h.Why}");
        }
    }
}
