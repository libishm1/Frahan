using System;
using Xunit;
using Rhino.Geometry;
using Frahan.Core.Discontinuity;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>StereonetProjection.Project</c> (lower-hemisphere pole plot on a
    /// stereonet of radius R) against the equal-area Lambert/Schmidt theorem proved
    /// in <c>frahan_proofs/FrahanProofs/Projection.lean</c> — <c>lambert_r_dr</c> /
    /// <c>lambert_area_element</c> (tex <c>thm:lambert</c>), the radial map
    /// <c>r(θ) = √2·sin(θ/2)</c> whose area element satisfies
    /// <c>r·dr = ½·sinθ·dθ</c> (a CONSTANT Jacobian ½, i.e. area-preserving).
    ///
    /// KERNEL CONVENTION (read from StereonetProjection.cs + OrientationMath.cs):
    ///   * pole folded to the lower hemisphere (nz &lt;= 0);
    ///   * colatitude θ = acos(|nz|) ∈ [0, 90°], 0 at the net centre, 90° at rim;
    ///   * Schmidt (wulff=false): radial law  r = √2·sin(θ/2), then scaled by R.
    ///     The projected point is P = (R·r·sinφ, R·r·cosφ), so the distance from
    ///     the net centre is |P| = R·√2·sin(θ/2). θ=90° maps to |P| = R exactly
    ///     (√2·sin45° = 1), so poles always land inside the primitive circle.
    ///   * azimuth φ = atan2(nx, ny), measured CLOCKWISE from +Y (North), and the
    ///     projected point's screen bearing atan2(P.X, P.Y) returns φ.
    /// A pole is BUILT here as (sinθ·sinφ, sinθ·cosφ, −cosθ) (the
    /// NormalFromDipDipDir form) so the kernel reads colatitude=θ, azimuth=φ.
    ///
    /// Every oracle is an INDEPENDENT double-precision computation; the kernel's
    /// own OrientationMath is never used to check itself.
    ///
    /// Facts:
    ///   T1 RADIAL LAW  — |Project(pole)| == R·√2·sin(θ/2) and the (x,y) split
    ///                    matches R·r·(sinφ, cosφ), across scales R ∈ {1e-3,1,1e3}.
    ///   T2 EQUAL-AREA  — for random colatitude bands the projected annulus area
    ///                    π(r2²−r1²) over the spherical band area 2πR²(cosθ1−cosθ2)
    ///                    is the CONSTANT ½ (the lambert_area_element Jacobian);
    ///                    negative control: the Wulff (equal-angle) net does NOT
    ///                    hold this constant.
    ///   T3 AZIMUTH     — atan2(P.X, P.Y) == the pole's azimuth φ (mod 2π).
    /// </summary>
    public class StereonetTests
    {
        const double D2R = Math.PI / 180.0;

        // Independent oracle: build the lower-hemisphere unit pole for (colat θ,
        // azimuth φ) exactly as the geological convention dictates. Pure doubles.
        static Vector3d Pole(double thetaDeg, double phiDeg)
        {
            double th = thetaDeg * D2R, ph = phiDeg * D2R, s = Math.Sin(th);
            return new Vector3d(s * Math.Sin(ph), s * Math.Cos(ph), -Math.Cos(th));
        }

        /// <summary>T1 — the Schmidt radial law r = R·√2·sin(θ/2), and the
        /// azimuthal (x,y) decomposition, hold exactly across three scales.</summary>
        [Fact]
        public void T1_RadialLaw_SchmidtEqualArea()
        {
            var rnd = new Random(20260725);
            double worst = 0; string worstMsg = "(none)";
            foreach (double R in new[] { 1e-3, 1.0, 1e3 })
            {
                for (int t = 0; t < 20000; t++)
                {
                    // θ in [1°,89°] avoids the r=0 centre and the vertical-tie rim.
                    double th = 1 + rnd.NextDouble() * 88;
                    double ph = rnd.NextDouble() * 360;
                    var P = StereonetProjection.Project(Pole(th, ph), R, wulff: false);

                    double r = Math.Sqrt(2.0) * Math.Sin(th * D2R / 2.0); // unit-disk radius
                    double expX = R * r * Math.Sin(ph * D2R);
                    double expY = R * r * Math.Cos(ph * D2R);
                    double expRad = R * r;
                    double gotRad = Math.Sqrt(P.X * P.X + P.Y * P.Y);

                    double tol = 1e-9 * R + 1e-12;
                    double eRad = Math.Abs(gotRad - expRad);
                    double eXY = Math.Max(Math.Abs(P.X - expX), Math.Abs(P.Y - expY));
                    double e = Math.Max(eRad, eXY);
                    if (e > worst) { worst = e; worstMsg = $"R={R} theta={th:F3} phi={ph:F2} err={e:E3} tol={tol:E3}"; }
                    Assert.True(e <= tol, $"T1 radial/azimuth law violated: {worstMsg}");
                }
            }
        }

        /// <summary>T2 — equal-area constancy. The Schmidt ratio
        /// π(r2²−r1²) / [2πR²(cosθ1−cosθ2)] equals ½ for every band (the constant
        /// Jacobian of lambert_area_element). The Wulff net (negative control)
        /// varies by more than 0.1 across the same bands, so it is NOT equal-area.
        /// r1,r2 are read from the KERNEL; the spherical-band area is the
        /// independent cosθ oracle.</summary>
        [Fact]
        public void T2_EqualArea_ConstantJacobian_WulffFails()
        {
            const double R = 7.0; // any radius; the ratio is R-independent
            var rnd = new Random(20260725);

            // (a) Schmidt: constant ½ across many random bands.
            double sMin = double.MaxValue, sMax = double.MinValue, sWorst = 0;
            for (int t = 0; t < 4000; t++)
            {
                // Keep BOTH colatitudes strictly below 90°: the kernel only
                // represents the lower hemisphere, so a pole past 90° folds back
                // (θ=91° → θ=89°) and would not match the cosθ spherical oracle.
                double a = 5 + rnd.NextDouble() * 70;   // θ1 in [5,75]
                double b = a + 3 + rnd.NextDouble() * 8; // θ2 in [θ1+3, θ1+11], max 86
                double r1 = Rad(StereonetProjection.Project(Pole(a, 30), R, false));
                double r2 = Rad(StereonetProjection.Project(Pole(b, 30), R, false));
                double aProj = Math.PI * (r2 * r2 - r1 * r1);
                double aSphere = 2 * Math.PI * R * R * (Math.Cos(a * D2R) - Math.Cos(b * D2R));
                double ratio = aProj / aSphere;
                sMin = Math.Min(sMin, ratio); sMax = Math.Max(sMax, ratio);
                sWorst = Math.Max(sWorst, Math.Abs(ratio - 0.5));
            }
            Assert.True(sWorst < 1e-9, $"T2 Schmidt Jacobian not ½: worst |ratio-0.5|={sWorst:E3}");
            Assert.True(sMax - sMin < 1e-9, $"T2 Schmidt ratio not constant across bands: spread={sMax - sMin:E3}");

            // (b) Wulff negative control: same three bands, ratio must NOT be constant.
            double[][] bands = { new[] { 5.0, 15.0 }, new[] { 40.0, 50.0 }, new[] { 75.0, 85.0 } };
            double wMin = double.MaxValue, wMax = double.MinValue;
            foreach (var band in bands)
            {
                double r1 = Rad(StereonetProjection.Project(Pole(band[0], 30), R, true));
                double r2 = Rad(StereonetProjection.Project(Pole(band[1], 30), R, true));
                double aProj = Math.PI * (r2 * r2 - r1 * r1);
                double aSphere = 2 * Math.PI * R * R * (Math.Cos(band[0] * D2R) - Math.Cos(band[1] * D2R));
                double ratio = aProj / aSphere;
                wMin = Math.Min(wMin, ratio); wMax = Math.Max(wMax, ratio);
            }
            Assert.True(wMax - wMin > 0.1,
                $"T2 negative control failed: Wulff ratio should vary across bands (spread={wMax - wMin:E3}) but looked constant");
        }

        /// <summary>T3 — azimuth preservation: the projected point's clockwise-from-North
        /// bearing atan2(P.X, P.Y) equals the pole's azimuth φ.</summary>
        [Fact]
        public void T3_AzimuthPreserved()
        {
            var rnd = new Random(1234567);
            double worst = 0; string worstMsg = "(none)";
            for (int t = 0; t < 20000; t++)
            {
                double th = 10 + rnd.NextDouble() * 79; // away from centre (r>0)
                double ph = rnd.NextDouble() * 360;
                var P = StereonetProjection.Project(Pole(th, ph), 3.0, false);
                double bearing = Math.Atan2(P.X, P.Y);      // clockwise from +Y
                double diff = bearing - ph * D2R;
                diff = Math.Atan2(Math.Sin(diff), Math.Cos(diff)); // wrap to (−π,π]
                if (Math.Abs(diff) > worst) { worst = Math.Abs(diff); worstMsg = $"theta={th:F3} phi={ph:F2} diff={diff:E3} rad"; }
                Assert.True(Math.Abs(diff) < 1e-9, $"T3 azimuth not preserved: {worstMsg}");
            }
        }

        static double Rad(Point2d p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);
    }
}
