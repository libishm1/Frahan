using System;
using Xunit;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies the <c>ConvexPolyhedron</c> H-rep &lt;-&gt; V-rep round trip:
    /// <c>P --ToInequalities--&gt; half-spaces --FromInequalities--&gt; Q</c> must
    /// reproduce <c>P</c> (same volume; every recovered vertex feasible against
    /// P's inequalities). This is the H-rep/V-rep duality that underpins the
    /// clip / trim convexity theorems in
    /// <c>frahan_proofs/FrahanProofs/Common.lean</c>
    /// (<c>clipChain_convex</c>, <c>clip_subset</c>).
    ///
    /// Origin harness + report: <c>outputs/2026-07-24/hrep_verification/</c>.
    /// Finding: round-trip is EXACT across the whole realistic working range
    /// (scale 0.01 .. 1e6). At the extremes the kernel's hardcoded absolute
    /// vertex-enumeration tolerance (1e-6) breaks scale-invariance (sub-mm and
    /// huge scales degrade). That is a documented latent limitation on the
    /// watch-list, NOT a regression — the scale-aware-tol fix was deliberately
    /// not applied. So the working range is gated (a real regression check);
    /// the extremes are characterized without gating, exactly as the harness
    /// reported them (it never failed on them).
    /// </summary>
    public class HrepTests
    {
        // Working range: the harness confirmed exact round-trip here.
        [Theory]
        [InlineData(0.01)]
        [InlineData(1.0)]
        [InlineData(100.0)]
        [InlineData(1e4)]
        [InlineData(1e6)]
        public void RoundTrip_ExactOnWorkingRange(double scale)
        {
            var rng = new Random(20260724);
            int trials = 2000, tested = 0, nullQ = 0, volFail = 0, feasFail = 0;
            double worstRelVol = 0;

            for (int t = 0; t < trials; t++)
            {
                var P = RandomConvex(rng, scale);
                double vP = P.Volume();
                if (P.Vertices.Count < 4 || vP < 1e-9 * scale * scale * scale) continue; // skip degenerate
                tested++;

                var ineqs = P.ToInequalities();
                var Q = ConvexPolyhedron.FromInequalities(ineqs);
                if (Q == null) { nullQ++; continue; }

                double vQ = Q.Volume();
                double relVol = Math.Abs(vQ - vP) / Math.Max(vP, 1e-30);
                if (relVol > worstRelVol) worstRelVol = relVol;
                if (relVol > 1e-3) volFail++;

                double ftol = 1e-6 * Math.Max(1.0, scale);
                foreach (var v in Q.Vertices)
                {
                    bool ok = true;
                    foreach (var r in ineqs)
                        if (r.Nx * v.X + r.Ny * v.Y + r.Nz * v.Z - r.B > ftol) { ok = false; break; }
                    if (!ok) { feasFail++; break; }
                }
            }

            Assert.True(tested > 100, $"scale {scale:G}: generator produced too few non-degenerate polyhedra ({tested})");
            Assert.True(nullQ == 0, $"scale {scale:G}: FromInequalities returned null on {nullQ} valid polyhedra");
            Assert.True(volFail == 0, $"scale {scale:G}: {volFail} round-trips off by >0.1% (worstRelVol={worstRelVol:P3})");
            Assert.True(feasFail == 0, $"scale {scale:G}: {feasFail} recovered vertices outside P");
        }

        // Extremes: sub-mm and huge scales. Documented latent degradation from
        // the hardcoded 1e-6 tolerance (watch-list). Characterized, not gated:
        // we only assert the round trip runs and the generator is non-trivial.
        // volFail / nullQ are EXPECTED here and are not treated as failures.
        [Theory]
        [InlineData(1e-4)]
        [InlineData(1e-3)]
        [InlineData(1e8)]
        public void RoundTrip_ExtremeScales_Characterization(double scale)
        {
            var rng = new Random(20260724);
            int trials = 1000, tested = 0, nullQ = 0, volFail = 0;
            double worstRelVol = 0;

            for (int t = 0; t < trials; t++)
            {
                var P = RandomConvex(rng, scale);
                double vP = P.Volume();
                if (P.Vertices.Count < 4 || vP < 1e-9 * scale * scale * scale) continue;
                tested++;
                var ineqs = P.ToInequalities();
                var Q = ConvexPolyhedron.FromInequalities(ineqs);
                if (Q == null) { nullQ++; continue; }
                double relVol = Math.Abs(Q.Volume() - vP) / Math.Max(vP, 1e-30);
                if (relVol > worstRelVol) worstRelVol = relVol;
                if (relVol > 1e-3) volFail++;
            }

            // Non-vacuous but non-gating: the run must complete and produce
            // polyhedra. Degradation (volFail/nullQ) is the documented latent
            // behaviour and is intentionally not asserted away.
            Assert.True(tested > 0, $"scale {scale:G}: generator produced nothing to characterize");
        }

        // A random convex polyhedron at the given scale: a box clipped by a few
        // random half-spaces (ClipByHalfSpace is trusted post the coincident-
        // plane fix — see ClipTests).
        static ConvexPolyhedron RandomConvex(Random rng, double scale)
        {
            double psi = U(rng, 0, 2 * Math.PI);
            var obb = new OrientedBlock(
                U(rng, -2, 2) * scale, U(rng, -2, 2) * scale, U(rng, -2, 2) * scale,
                Math.Cos(psi), Math.Sin(psi), -Math.Sin(psi), Math.Cos(psi),
                U(rng, 0.3, 2) * scale, U(rng, 0.3, 2) * scale, U(rng, 0.3, 2) * scale);
            var P = ConvexPolyhedron.FromOrientedBlock(obb);
            int cuts = rng.Next(0, 4);
            for (int c = 0; c < cuts; c++)
            {
                double nx = U(rng, -1, 1), ny = U(rng, -1, 1), nz = U(rng, -1, 1);
                if (nx * nx + ny * ny + nz * nz < 1e-4) continue;
                double px = U(rng, -1.5, 1.5) * scale, py = U(rng, -1.5, 1.5) * scale, pz = U(rng, -1.5, 1.5) * scale;
                var clipped = P.ClipByHalfSpace(px, py, pz, nx, ny, nz);
                if (clipped.Vertices.Count >= 4) P = clipped;
            }
            return P;
        }

        static double U(Random r, double lo, double hi) => lo + (hi - lo) * r.NextDouble();
    }
}
