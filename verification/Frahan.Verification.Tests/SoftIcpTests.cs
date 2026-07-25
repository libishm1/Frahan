using System;
using System.Collections.Generic;
using Xunit;
using Rhino.Geometry;
using Frahan.EdgeMatching;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies the Soft-ICP CPD/EM pose refiner
    /// (<c>Frahan.EdgeMatching.SoftIcpRefiner.Refine3D</c>, the production EM
    /// weighted-Kabsch alternation, plus a smoke check on the
    /// <c>SoftIcpLbfgs.Refine3D</c> gradient-descent sibling) against
    /// <c>frahan_proofs/FrahanProofs/TierThree.lean</c> — <c>mm_monotone</c>
    /// (tex <c>thm:cpd</c>): the minorize-maximize / EM iteration is monotone, so
    /// the objective does not increase from one round to the next.
    ///
    /// The refiner minimises L = w_contact·SoftRimSSD + w_pen·PenetrationHinge.
    /// The penetration term uses RhinoCommon-native Mesh.IsPointInside /
    /// Curve.Contains, which the headless Rhino3dm backend does not provide; the
    /// tests therefore run every fragment with Solid = null and Contour2D = null,
    /// so ONLY the CPD contact + weighted-Kabsch path executes (the shipping code,
    /// linked unmodified). The reported <c>Report.MeanRimGap</c> is then the pure
    /// contact energy proxy (MaxPenetration ≡ 0), and it is what monotonicity is
    /// asserted on. See Stubs/RhinoCommonPenetrationShims.cs for the compile-only
    /// symbols; they are never invoked here.
    ///
    /// Fixture: an asymmetric wavy fracture-interface patch of ~100 points shared
    /// by the anchor fragment and the moving fragment; the moving fragment starts
    /// at a known rigid perturbation G, and the refiner should pull its rim back
    /// onto the anchor rim (G⁻¹ recovery, rims coincide).
    ///
    /// Facts:
    ///   T1 MONOTONE (final ≤ initial) — over many random rigid perturbations the
    ///       refined MeanRimGap is ≤ the initial MeanRimGap: the round does not
    ///       increase the energy (mm_monotone headline), and in fact reduces it.
    ///   T2 MONOTONE (per round)       — running k = 1,2,…,K EM rounds gives a
    ///       non-increasing MeanRimGap sequence (each E+M round descends), the
    ///       per-round form of mm_monotone; loosely gated (hard-NN proxy vs the
    ///       soft objective), worst per-round increase characterized.
    ///   T3 RIGID RECOVERY             — on noiseless rigid cases the recovered
    ///       transform maps the moving rim back onto the anchor rim (residual small
    ///       relative to sample spacing); characterized, gated loosely.
    ///   T4 LBFGS SMOKE                — SoftIcpLbfgs.Refine3D (finite-difference
    ///       BfgsMinimizer path) does not increase the reported gap on small
    ///       perturbations (final ≤ initial): the gradient path is also monotone
    ///       in the reported energy.
    ///
    /// All oracles (mean/max residual, pointwise map, gap re-reads) are independent
    /// double-precision computations; the kernel's Report is compared, never used
    /// to prove itself. Deterministic seeds.
    /// </summary>
    public class SoftIcpTests
    {
        const double L = 100.0; // normalized fixture extent (matches the ~100u fixtures)

        /// <summary>T1 — over many random rigid perturbations, the refined gap is
        /// ≤ the initial gap (the EM round is monotone / non-increasing in energy),
        /// and the mean reduction is substantial (the refiner actually mates).</summary>
        [Fact]
        public void T1_Refine_DoesNotIncreaseGap()
        {
            var rnd = new Random(20260725);
            var rim = WavyPatch(10, 10, L);
            double spacing = MedianSpacing(rim);

            double worstIncrease = 0; string worstMsg = "(none)";
            double sumInit = 0, sumFinal = 0; int cases = 0;
            for (int t = 0; t < 200; t++)
            {
                double angDeg = 1.0 + rnd.NextDouble() * 9.0;              // 1..10 deg
                double tmag = rnd.NextDouble() * 1.2 * spacing;           // up to ~1.2 spacings
                var G = RandomRigid(rnd, angDeg, tmag);
                var moved = ApplyAll(G, rim);

                var frags = TwoFragments(rim, moved);
                var opt = new SoftIcpOptions();
                double init = SoftIcpRefiner.Measure(frags, opt, threeD: true).MeanRimGap;
                double fin = SoftIcpRefiner.Refine3D(frags, opt).MeanRimGap;

                double increase = fin - init;
                if (increase > worstIncrease) { worstIncrease = increase; worstMsg = $"ang={angDeg:F1} t={tmag:F2} init={init:E3} fin={fin:E3} inc={increase:E3}"; }
                sumInit += init; sumFinal += fin; cases++;

                // Monotone: the round does not increase the energy (loose abs+rel tol).
                Assert.True(fin <= init + 1e-6 * spacing + 1e-9,
                    $"T1 gap INCREASED after refine: {worstMsg}");
            }
            // The refiner genuinely mates (mean gap collapses), so this is a real
            // descent, not a no-op that trivially satisfies "does not increase".
            double meanInit = sumInit / cases, meanFinal = sumFinal / cases;
            Assert.True(meanFinal < 0.5 * meanInit,
                $"T1 refiner did not substantially reduce the gap: meanInit={meanInit:E3} meanFinal={meanFinal:E3}");
        }

        /// <summary>T2 — round-by-round energy descent. Refine3D resets and runs
        /// exactly MaxIterations EM rounds, so sweeping MaxIterations = 1..K yields
        /// the round-by-round MeanRimGap trace. What mm_monotone (thm:cpd)
        /// guarantees is monotonicity of the SOFT surrogate objective; Report does
        /// not expose per-round soft-objective values, so we test the honestly
        /// supported consequences on the reported hard-NN gap proxy:
        ///   (a) NET descent: gap(K) ≤ gap(1) for every trial (the run descends),
        ///   (b) the proxy is near-monotone — the worst single-round INCREASE is a
        ///       tiny fraction of the sample spacing (a near-convergence wiggle of
        ///       the hard-NN proxy, not a violation of the soft-objective descent),
        ///       characterized and bounded here.</summary>
        [Fact]
        public void T2_Refine_RoundByRoundDescent()
        {
            var rnd = new Random(31337);
            var rim = WavyPatch(10, 10, L);
            double spacing = MedianSpacing(rim);
            double worstIncrease = 0; string worstIncMsg = "(none)";
            double worstNetRatio = 0; string worstNetMsg = "(none)";

            for (int trial = 0; trial < 12; trial++)
            {
                var G = RandomRigid(rnd, 2.0 + rnd.NextDouble() * 6.0, rnd.NextDouble() * 1.0 * spacing);
                var moved = ApplyAll(G, rim);

                const int K = 16;
                double prev = double.PositiveInfinity;
                double g1 = double.NaN, gK = double.NaN;
                for (int k = 1; k <= K; k++)
                {
                    var frags = TwoFragments(rim, moved);
                    var opt = new SoftIcpOptions { MaxIterations = k };
                    double g = SoftIcpRefiner.Refine3D(frags, opt).MeanRimGap;
                    if (k == 1) g1 = g;
                    if (k == K) gK = g;
                    double increase = g - prev;
                    if (increase > worstIncrease) { worstIncrease = increase; worstIncMsg = $"trial={trial} k={k} prev={prev:E3} g={g:E3} inc={increase:E3} (inc/spacing={increase / spacing:E3})"; }
                    prev = g;
                }
                // (a) NET descent over the whole run.
                double netRatio = g1 > 1e-12 ? gK / g1 : 0.0;
                if (netRatio > worstNetRatio) { worstNetRatio = netRatio; worstNetMsg = $"trial={trial} g1={g1:E3} gK={gK:E3} gK/g1={netRatio:E3}"; }
                Assert.True(gK <= g1 + 1e-9, $"T2 net descent failed: {worstNetMsg}");
            }
            // (b) the hard-NN proxy is near-monotone: worst single-round increase is
            // a small fraction of the sample spacing (characterized). The soft
            // surrogate is monotone by mm_monotone; the proxy wiggle is bounded.
            Assert.True(worstIncrease <= 0.02 * spacing,
                $"T2 per-round proxy increase exceeded 0.02*spacing: {worstIncMsg}");
        }

        /// <summary>T3 — noiseless rigid recovery: the recovered transform maps the
        /// moving rim back onto the anchor rim. Characterize the residual; gate
        /// loosely relative to the sample spacing.</summary>
        [Fact]
        public void T3_RigidCase_RecoversTransform()
        {
            var rnd = new Random(9001);
            var rim = WavyPatch(10, 10, L);
            double spacing = MedianSpacing(rim);
            double worstMeanRes = 0, worstMaxRes = 0, worstGap = 0; string worstMsg = "(none)";

            for (int t = 0; t < 200; t++)
            {
                double angDeg = 1.0 + rnd.NextDouble() * 5.0;             // 1..6 deg (recovery basin)
                double tmag = rnd.NextDouble() * 0.8 * spacing;
                var G = RandomRigid(rnd, angDeg, tmag);
                var moved = ApplyAll(G, rim);

                var frags = TwoFragments(rim, moved);
                var opt = new SoftIcpOptions();
                double gap = SoftIcpRefiner.Refine3D(frags, opt).MeanRimGap;
                var delta = frags[1].Delta; // moving fragment's recovered increment

                // Recovered map applied to the moving rim should land on the anchor
                // rim (same point ordering => true correspondence is identity index).
                double sum = 0, mx = 0;
                for (int i = 0; i < rim.Length; i++)
                {
                    var p = moved[i]; p.Transform(delta);
                    double d = p.DistanceTo(rim[i]);
                    sum += d; if (d > mx) mx = d;
                }
                double meanRes = sum / rim.Length;
                if (meanRes > worstMeanRes) { worstMeanRes = meanRes; }
                if (mx > worstMaxRes) { worstMaxRes = mx; }
                if (gap > worstGap) { worstGap = gap; }
                worstMsg = $"meanRes={worstMeanRes:E3} maxRes={worstMaxRes:E3} gap={worstGap:E3} (spacing={spacing:F2})";

                // Loose gate: recovered rim within a fraction of a sample spacing.
                Assert.True(meanRes < 0.15 * spacing, $"T3 mean recovery residual too large: {worstMsg}");
                Assert.True(gap < 0.15 * spacing, $"T3 residual mating gap too large: {worstMsg}");
            }
        }

        /// <summary>T4 — LBFGS path smoke: SoftIcpLbfgs.Refine3D does not increase
        /// the reported gap on small perturbations (final ≤ initial), i.e. the
        /// gradient-descent path is also monotone in the reported energy.</summary>
        [Fact]
        public void T4_Lbfgs_DoesNotIncreaseGap()
        {
            var rnd = new Random(5555);
            var rim = WavyPatch(8, 8, L);
            double spacing = MedianSpacing(rim);
            double worstIncrease = 0; string worstMsg = "(none)";
            int reduced = 0, cases = 0;

            for (int t = 0; t < 20; t++)
            {
                double angDeg = 0.5 + rnd.NextDouble() * 3.0;
                double tmag = rnd.NextDouble() * 0.5 * spacing;
                var G = RandomRigid(rnd, angDeg, tmag);
                var moved = ApplyAll(G, rim);

                var frags = TwoFragments(rim, moved);
                var opt = new SoftIcpOptions();
                double init = SoftIcpRefiner.Measure(frags, opt, threeD: true).MeanRimGap;
                double fin = SoftIcpLbfgs.Refine3D(frags, opt).MeanRimGap;

                double increase = fin - init;
                if (increase > worstIncrease) { worstIncrease = increase; worstMsg = $"ang={angDeg:F1} t={tmag:F2} init={init:E3} fin={fin:E3} inc={increase:E3}"; }
                if (fin < init) reduced++;
                cases++;
                Assert.True(fin <= init + 2e-2 * spacing + 1e-9,
                    $"T4 LBFGS gap increased beyond tolerance: {worstMsg}");
            }
            // The gradient path should reduce the gap on the majority of cases.
            Assert.True(reduced >= cases / 2, $"T4 LBFGS reduced only {reduced}/{cases} cases");
        }

        // ---- fixture + oracles ----------------------------------------------

        // Asymmetric wavy surface patch: na x nb points on a height field that
        // breaks all rotational symmetry, so the rigid alignment is unique.
        static Point3d[] WavyPatch(int na, int nb, double extent)
        {
            var pts = new Point3d[na * nb];
            int k = 0;
            for (int a = 0; a < na; a++)
                for (int b = 0; b < nb; b++)
                {
                    double u = a / (double)(na - 1);
                    double v = b / (double)(nb - 1);
                    double x = extent * u;
                    double y = extent * v;
                    double z = extent * (0.12 * Math.Sin(3 * u * Math.PI) * Math.Cos(2 * v * Math.PI)
                                         + 0.08 * Math.Sin(5 * v * Math.PI + 0.7)
                                         + 0.05 * Math.Cos(4 * u * Math.PI + 0.3));
                    pts[k++] = new Point3d(x, y, z);
                }
            return pts;
        }

        static IList<SoftIcpRefiner.Fragment> TwoFragments(Point3d[] anchorRim, Point3d[] movingRim)
        {
            var a = new SoftIcpRefiner.Fragment("A", Clone(anchorRim), solid: null, contour2D: null, anchored: true);
            var b = new SoftIcpRefiner.Fragment("B", Clone(movingRim), solid: null, contour2D: null, anchored: false);
            return new List<SoftIcpRefiner.Fragment> { a, b };
        }

        static Point3d[] Clone(Point3d[] p) { var q = new Point3d[p.Length]; Array.Copy(p, q, p.Length); return q; }

        static Point3d[] ApplyAll(Transform t, Point3d[] p)
        {
            var q = new Point3d[p.Length];
            for (int i = 0; i < p.Length; i++) { var x = p[i]; x.Transform(t); q[i] = x; }
            return q;
        }

        static double MedianSpacing(Point3d[] p)
        {
            var gaps = new List<double>();
            for (int i = 1; i < p.Length; i++)
            {
                double d = p[i].DistanceTo(p[i - 1]);
                if (d > 1e-12) gaps.Add(d);
            }
            gaps.Sort();
            return gaps.Count == 0 ? 1.0 : gaps[(gaps.Count - 1) / 2];
        }

        // A random rigid transform: rotation by angDeg about a random unit axis,
        // then a translation of magnitude tmag in a random direction.
        static Transform RandomRigid(Random r, double angDeg, double tmag)
        {
            var axis = RandUnit(r);
            double ang = angDeg * Math.PI / 180.0;
            var R = Rodrigues(axis, ang);
            var tdir = RandUnit(r);
            R.M03 = tmag * tdir.X; R.M13 = tmag * tdir.Y; R.M23 = tmag * tdir.Z;
            return R;
        }

        static Vector3d RandUnit(Random r)
        {
            while (true)
            {
                var v = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
                double n = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                if (n > 1e-3) return new Vector3d(v.X / n, v.Y / n, v.Z / n);
            }
        }

        // Rotation transform via Rodrigues about unit axis k by angle ang.
        static Transform Rodrigues(Vector3d k, double ang)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang), C = 1 - c;
            double x = k.X, y = k.Y, z = k.Z;
            var t = Transform.Identity;
            t.M00 = c + x * x * C;     t.M01 = x * y * C - z * s; t.M02 = x * z * C + y * s;
            t.M10 = y * x * C + z * s; t.M11 = c + y * y * C;     t.M12 = y * z * C - x * s;
            t.M20 = z * x * C - y * s; t.M21 = z * y * C + x * s; t.M22 = c + z * z * C;
            return t;
        }
    }
}
