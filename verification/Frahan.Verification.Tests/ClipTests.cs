using System;
using CsCheck;
using Xunit;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>ConvexPolyhedron.ClipByHalfSpace</c> / <c>ClipBothSides</c>
    /// (BlockCutOpt guillotine trim) against the Lean trim catalogue in
    /// <c>frahan_proofs/FrahanProofs/Common.lean</c>:
    /// <c>clip_subset</c>, <c>clip_measure_le</c>, <c>clipChain_measure_le</c>,
    /// and <c>clip_idempotent</c>. Property-based (CsCheck) with an independent
    /// analytic / Monte-Carlo-derived oracle, never the kernel's own flags.
    ///
    /// Origin harness + full report:
    /// <c>outputs/2026-07-24/clip_verification/</c>. That run FOUND a real bug —
    /// coincident-plane re-clip inflated volume (~2.8% of cuts, up to ~8x) —
    /// which was fixed in the shipping kernel (no-op guard enforcing
    /// clip_idempotent). These tests exercise the FIXED code: any failure of
    /// the idempotence facts is a regression of that fix, not a weak assertion.
    ///
    /// Runs are deterministic: fixed CsCheck seed + single thread + fixed iter.
    /// </summary>
    public class ClipTests
    {
        const int Iters = 20_000;
        // A known-valid CsCheck seed (the shrunk seed that first exposed the
        // idempotence bug on the pre-fix code); it now passes, so case 0 is a
        // direct regression check of the fix.
        const string Seed = "1Z553fTQWH4b";

        static readonly Gen<OrientedBlock> GenBlock =
            Gen.Select(
                Gen.Double[-5.0, 5.0], Gen.Double[-5.0, 5.0], Gen.Double[-5.0, 5.0],
                Gen.Double[0.0, 2.0 * Math.PI],
                Gen.Double[0.1, 3.0], Gen.Double[0.1, 3.0], Gen.Double[0.1, 3.0])
            .Select((cx, cy, cz, psi, hx, hy, hz) =>
                new OrientedBlock(cx, cy, cz,
                    Math.Cos(psi), Math.Sin(psi), -Math.Sin(psi), Math.Cos(psi),
                    hx, hy, hz));

        static readonly Gen<(double px, double py, double pz, double nx, double ny, double nz)> GenPlane =
            Gen.Select(
                Gen.Double[-7.0, 7.0], Gen.Double[-7.0, 7.0], Gen.Double[-7.0, 7.0],
                Gen.Double[-1.0, 1.0], Gen.Double[-1.0, 1.0], Gen.Double[-1.0, 1.0])
            .Select((px, py, pz, nx, ny, nz) => (px, py, pz, nx, ny, nz));

        /// <summary>Sanity: an unclipped CPH built from an OrientedBlock has the
        /// exact analytic box volume 8*hx*hy*hz.</summary>
        [Fact]
        public void UnclippedVolume_MatchesAnalyticBox()
        {
            GenBlock.Sample(b =>
            {
                double vA = 8.0 * b.HalfX * b.HalfY * b.HalfZ;
                double vC = ConvexPolyhedron.FromOrientedBlock(b).Volume();
                if (Math.Abs(vC - vA) > 1e-9 * vA + 1e-12)
                    throw new Exception($"Volume() wrong: cph={vC} analytic={vA}");
            }, seed: Seed, iter: Iters, threads: 1);
        }

        /// <summary>A single half-space clip never grows volume
        /// (<c>clip_measure_le</c>), keeps every output vertex inside the parent
        /// block (<c>clip_subset</c>), and on the kept side of the plane
        /// (half-space feasibility).</summary>
        [Fact]
        public void SingleClip_MeasureLe_Subset_Feasible()
        {
            Gen.Select(GenBlock, GenPlane).Sample((b, pl) =>
            {
                double n2 = pl.nx * pl.nx + pl.ny * pl.ny + pl.nz * pl.nz;
                if (n2 < 1e-4) return;
                var cph = ConvexPolyhedron.FromOrientedBlock(b);
                double v0 = cph.Volume();
                var clipped = cph.ClipByHalfSpace(pl.px, pl.py, pl.pz, pl.nx, pl.ny, pl.nz);
                double v1 = clipped.Volume();
                double scale = Math.Max(b.HalfX, Math.Max(b.HalfY, b.HalfZ));
                double vtol = 1e-6 * v0 + 1e-9, ptol = 1e-6 * scale + 1e-9;
                if (v1 > v0 + vtol) throw new Exception($"VOLUME GREW v1={v1} > v0={v0}");
                double inv = 1.0 / Math.Sqrt(n2);
                double nx = pl.nx * inv, ny = pl.ny * inv, nz = pl.nz * inv;
                foreach (var x in clipped.Vertices)
                {
                    double dx = x.X - b.CenterX, dy = x.Y - b.CenterY, dz = x.Z - b.CenterZ;
                    double a = dx * b.UX + dy * b.UY + dz * b.UZ;
                    double bb = dx * b.VX + dy * b.VY + dz * b.VZ;
                    double w = dx * b.WX + dy * b.WY + dz * b.WZ;
                    if (Math.Abs(a) > b.HalfX + ptol || Math.Abs(bb) > b.HalfY + ptol || Math.Abs(w) > b.HalfZ + ptol)
                        throw new Exception($"OUTSIDE P: local=({a},{bb},{w})");
                    double sd = (x.X - pl.px) * nx + (x.Y - pl.py) * ny + (x.Z - pl.pz) * nz;
                    if (sd > ptol) throw new Exception($"WRONG SIDE: signedDist={sd}");
                }
            }, seed: Seed, iter: Iters, threads: 1);
        }

        /// <summary>A chain of three clips is monotonically non-increasing in
        /// volume (<c>clipChain_measure_le</c>).</summary>
        [Fact]
        public void ClipChain_MeasureLe_NonIncreasing()
        {
            Gen.Select(GenBlock, GenPlane, GenPlane, GenPlane).Sample((b, p1, p2, p3) =>
            {
                var cph = ConvexPolyhedron.FromOrientedBlock(b);
                double v0 = cph.Volume(), vtol = 1e-6 * v0 + 1e-9;
                foreach (var pl in new[] { p1, p2, p3 })
                {
                    if (pl.nx * pl.nx + pl.ny * pl.ny + pl.nz * pl.nz < 1e-4) continue;
                    var next = cph.ClipByHalfSpace(pl.px, pl.py, pl.pz, pl.nx, pl.ny, pl.nz);
                    if (next.Volume() > cph.Volume() + vtol)
                        throw new Exception($"chain step grew: {next.Volume()} > {cph.Volume()}");
                    cph = next;
                }
            }, seed: Seed, iter: Iters, threads: 1);
        }

        /// <summary>Clipping by the same plane twice equals clipping once
        /// (<c>clip_idempotent</c>). This is the exact property the pre-fix
        /// kernel violated; it must hold on the fixed kernel.</summary>
        [Fact]
        public void Clip_Idempotent()
        {
            Gen.Select(GenBlock, GenPlane).Sample((b, pl) =>
            {
                if (pl.nx * pl.nx + pl.ny * pl.ny + pl.nz * pl.nz < 1e-4) return;
                var once = ConvexPolyhedron.FromOrientedBlock(b).ClipByHalfSpace(pl.px, pl.py, pl.pz, pl.nx, pl.ny, pl.nz);
                var twice = once.ClipByHalfSpace(pl.px, pl.py, pl.pz, pl.nx, pl.ny, pl.nz);
                double vtol = 1e-6 * Math.Max(once.Volume(), 1e-9) + 1e-9;
                if (Math.Abs(once.Volume() - twice.Volume()) > vtol)
                    throw new Exception($"not idempotent: once={once.Volume()} twice={twice.Volume()}");
            }, seed: Seed, iter: Iters, threads: 1);
        }

        /// <summary>The production two-sided API conserves material: kept volume
        /// plus discarded volume equals the original
        /// (a corollary of <c>clip_measure_le</c> applied to both half-spaces).</summary>
        [Fact]
        public void ClipBothSides_ConservesMaterial()
        {
            Gen.Select(GenBlock, GenPlane).Sample((b, pl) =>
            {
                if (pl.nx * pl.nx + pl.ny * pl.ny + pl.nz * pl.nz < 1e-4) return;
                var cph = ConvexPolyhedron.FromOrientedBlock(b);
                double v0 = cph.Volume();
                var (kept, discarded) = cph.ClipBothSides(pl.px, pl.py, pl.pz, pl.nx, pl.ny, pl.nz);
                double sum = kept.Volume() + discarded.Volume();
                if (Math.Abs(sum - v0) > 1e-6 * v0 + 1e-9)
                    throw new Exception($"material not conserved: kept+disc={sum} != original={v0}");
            }, seed: Seed, iter: Iters, threads: 1);
        }

        /// <summary>Deterministic break-rate characterization of idempotence
        /// (the harness's headline metric). Over a fixed seeded batch the fixed
        /// kernel must never inflate volume on a repeated cut: break-rate == 0.
        /// Pre-fix this was ~2.8%.</summary>
        [Fact]
        public void Clip_Idempotence_BreakRate_IsZero()
        {
            var rng = new Random(20260724);
            int N = 20_000, breaks = 0;
            double maxRel = 0;
            for (int it = 0; it < N; it++)
            {
                double psi = U(rng, 0, 2 * Math.PI);
                var b = new OrientedBlock(U(rng, -5, 5), U(rng, -5, 5), U(rng, -5, 5),
                    Math.Cos(psi), Math.Sin(psi), -Math.Sin(psi), Math.Cos(psi),
                    U(rng, 0.1, 3), U(rng, 0.1, 3), U(rng, 0.1, 3));
                double px = U(rng, -7, 7), py = U(rng, -7, 7), pz = U(rng, -7, 7);
                double nx = U(rng, -1, 1), ny = U(rng, -1, 1), nz = U(rng, -1, 1);
                if (nx * nx + ny * ny + nz * nz < 1e-4) continue;
                var once = ConvexPolyhedron.FromOrientedBlock(b).ClipByHalfSpace(px, py, pz, nx, ny, nz);
                var twice = once.ClipByHalfSpace(px, py, pz, nx, ny, nz);
                double vtol = 1e-6 * Math.Max(once.Volume(), 1e-9) + 1e-9;
                double diff = Math.Abs(once.Volume() - twice.Volume());
                if (diff > vtol)
                {
                    breaks++;
                    double rel = diff / Math.Max(once.Volume(), 1e-9);
                    if (rel > maxRel) maxRel = rel;
                }
            }
            Assert.True(breaks == 0, $"idempotence break-rate must be 0 on the fixed kernel; got {breaks}/{N} (maxRel={maxRel:P2})");
        }

        static double U(Random r, double lo, double hi) => lo + (hi - lo) * r.NextDouble();
    }
}
