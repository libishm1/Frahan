using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Clipper2Lib;
using Frahan.Packing.TwoD;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>ContactNfpHoleNester.Pack</c> (CNH / NFP-BLF 2D packer)
    /// against the No-Fit-Polygon separation invariant proved as
    /// <c>nfp_separation</c> in <c>frahan_proofs/FrahanProofs/Packing.lean</c>
    /// (t not in A-B =&gt; A and B+t are interior-disjoint). The nester's core
    /// contract: placed parts are pairwise interior-disjoint AND inside the
    /// sheet. Overlap is checked INDEPENDENTLY via Clipper2 intersection area,
    /// never the nester's own validity flag; containment via vertex protrusion
    /// DEPTH (an earlier area-based check false-positived — see the report).
    ///
    /// The managed Clipper2 lane is forced (FRAHAN_NFP_NATIVE=0) so the local
    /// and CI (ubuntu, no native nfp_kernel.dll) runs exercise the same path.
    ///
    /// Origin harness + report: <c>outputs/2026-07-24/nester_verification/</c>
    /// (clean PASS: 0 overlap, 0 protrusion across thousands of pairs).
    /// </summary>
    public class NesterTests
    {
        [Fact]
        public void PlacedParts_ZeroOverlap_AndContained()
        {
            // Force the managed Clipper2 lane for deterministic local/CI parity
            // (the ubuntu runner has no native nfp_kernel.dll anyway; the probe
            // would fall back, but pinning it removes any ambiguity).
            Environment.SetEnvironmentVariable("FRAHAN_NFP_NATIVE", "0");

            int layouts = 2000;
            var rng = new Random(20260724);

            int totalPairs = 0, overlapPairs = 0, emptyLayouts = 0;
            int validLayouts = 0, invalidLayouts = 0, validOverlap = 0;
            double worstOverlapAbs = 0, worstValidOverflow = 0;
            const double snapBand = 1e-5;      // Scale=1000, Clipper precision 2 => 1e-5 caller units
            int protAboveSnap = 0; double worstProtAbs = 0;

            for (int L = 0; L < layouts; L++)
            {
                int nParts = rng.Next(4, 11);
                var parts = new List<HoleNestPart>(nParts);
                double partAreaSum = 0;
                for (int i = 0; i < nParts; i++)
                {
                    double w = U(rng, 3, 12), h = U(rng, 3, 12);
                    partAreaSum += w * h;
                    parts.Add(new HoleNestPart
                    {
                        Outer = Rect(0, 0, w, h),
                        Holes = new List<IReadOnlyList<(double X, double Y)>>()
                    });
                }
                double side = Math.Sqrt(partAreaSum * 1.6);
                var sheet = Rect(0, 0, side, side);

                HoleNestResult res = ContactNfpHoleNester.Pack(sheet, null, parts, spacing: 0.0);

                var placed = res.Placements.Where(p => p.PlacedOuter != null && p.PlacedOuter.Count >= 3).ToList();
                if (placed.Count == 0) { emptyLayouts++; continue; }
                bool valid = res.Valid;
                if (valid) validLayouts++; else invalidLayouts++;

                // independent PAIRWISE overlap check via Clipper2 intersection area
                bool anyOverlapHere = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    double ai = Math.Abs(AreaOf(placed[i].PlacedOuter));
                    for (int j = i + 1; j < placed.Count; j++)
                    {
                        totalPairs++;
                        double aj = Math.Abs(AreaOf(placed[j].PlacedOuter));
                        double inter = IntersectionArea(placed[i].PlacedOuter, placed[j].PlacedOuter);
                        double tol = 1e-4 * Math.Min(ai, aj) + 1e-6;
                        if (inter > tol)
                        {
                            overlapPairs++; anyOverlapHere = true;
                            if (inter > worstOverlapAbs) worstOverlapAbs = inter;
                        }
                    }
                }
                if (valid && anyOverlapHere) validOverlap++;

                // independent CONTAINMENT check: max protrusion DEPTH outside
                // the axis-aligned sheet [0,side]^2.
                foreach (var pl in placed)
                {
                    double prot = 0;
                    foreach (var v in pl.PlacedOuter)
                    {
                        double pdepth = Math.Max(Math.Max(-v.X, v.X - side), Math.Max(-v.Y, v.Y - side));
                        if (pdepth > prot) prot = pdepth;
                    }
                    if (valid && prot > worstValidOverflow) worstValidOverflow = prot;
                    if (prot > snapBand) protAboveSnap++;
                    if (prot > worstProtAbs) worstProtAbs = prot;
                }
            }

            Assert.True(overlapPairs == 0,
                $"nfp_separation violated: {overlapPairs}/{totalPairs} placement pairs overlap (worst area {worstOverlapAbs:G4}); empty layouts {emptyLayouts}");
            Assert.True(validOverlap == 0,
                $"{validOverlap} layouts flagged VALID by the nester but overlap independently");
            Assert.True(worstProtAbs < 1e-3,
                $"containment violated: worst protrusion depth {worstProtAbs:G4} > 1e-3 (> snap band; {protAboveSnap} parts above snap band; worst on valid layouts {worstValidOverflow:G4})");
        }

        static IReadOnlyList<(double X, double Y)> Rect(double x, double y, double w, double h) =>
            new (double, double)[] { (x, y), (x + w, y), (x + w, y + h), (x, y + h) };

        static double AreaOf(IReadOnlyList<(double X, double Y)> loop)
        {
            double s = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                s += a.X * b.Y - b.X * a.Y;
            }
            return s / 2.0;
        }

        static double IntersectionArea(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
        {
            var pa = new PathsD { ToPath(a) };
            var pb = new PathsD { ToPath(b) };
            var inter = Clipper.Intersect(pa, pb, FillRule.NonZero);
            double area = 0;
            foreach (var loop in inter) area += Math.Abs(Clipper.Area(loop));
            return area;
        }

        static PathD ToPath(IReadOnlyList<(double X, double Y)> loop)
        {
            var p = new PathD(loop.Count);
            foreach (var v in loop) p.Add(new PointD(v.X, v.Y));
            return p;
        }

        static double U(Random r, double lo, double hi) => lo + (hi - lo) * r.NextDouble();
    }
}
