using System;
using System.Collections.Generic;
using Xunit;
using Clipper2Lib;
using Frahan.Masonry.Sequencing;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies <c>PolygonalWallGenerator.Generate</c> against the power-diagram
    /// convexity theorem proved in <c>frahan_proofs/FrahanProofs/Power.lean</c> —
    /// <c>powerCell_convex</c> (tex <c>prop:power</c>): a power (additively
    /// weighted Voronoi) cell is an intersection of half-spaces, hence convex.
    ///
    /// ALGORITHM (read from PolygonalWallGenerator.cs): the cells are a genuine
    /// POWER DIAGRAM of jittered-grid seeds with per-seed weights, computed
    /// exactly by Sutherland–Hodgman half-plane clipping of the wall rectangle —
    /// cell(i) = rect ∩ { x : 2(s_j−s_i)·x &lt;= |s_j|²−|s_i|²+w_i−w_j , ∀ j≠i }.
    /// Lloyd relaxation, a coursing morph, and sliver culling only relocate/drop
    /// seeds; the FINAL cells are still power cells of the surviving seed set, so
    /// the powerCell_convex framing is the honest contract and both convexity and
    /// the rectangle partition must hold. This IS a power diagram, not a brick
    /// subdivision.
    ///
    /// Independent oracles only: convexity from consecutive-edge cross-product
    /// signs, disjointness from Clipper2 intersection area (the suite's Clipper2
    /// NuGet), completeness from a shoelace area sum — never the generator's own
    /// AreaCoverage/metrics.
    ///
    /// Facts:
    ///   T1 CONVEX      — every returned cell is a convex polygon.
    ///   T2 PARTITION   — cells are pairwise interior-disjoint AND tile the wall
    ///                    rectangle (Σ area ≈ W·H).
    ///   T3 DETERMINISM — identical Seed ⇒ bit-identical cell geometry.
    /// All three run across seeds × grid sizes × Lloyd iterations {0,1,2}.
    /// </summary>
    public class PolygonalWallTests
    {
        static IEnumerable<WallGenOptions> Combos()
        {
            int[] seeds = { 1, 7, 42, 100 };
            (int gx, int gy)[] grids = { (4, 3), (8, 5), (12, 7) };
            int[] lloyd = { 0, 1, 2 };
            foreach (var s in seeds)
                foreach (var g in grids)
                    foreach (var it in lloyd)
                        yield return new WallGenOptions
                        {
                            Width = 3.0, Height = 1.8, Coursing = 0.4, Courses = 5,
                            GridX = g.gx, GridY = g.gy, Seed = s, LloydIterations = it,
                            SizeGradeCv = 0.30, SliverMinInradiusFrac = 0.18, MaxSliverPasses = 3
                        };
        }

        /// <summary>T1 — powerCell_convex: each cell's turn directions all agree in
        /// sign (normalized cross product = sine of the turn angle), allowing
        /// near-collinear vertices within tolerance.</summary>
        [Fact]
        public void T1_EveryCell_IsConvex()
        {
            const double EPS = 1e-6; // allowed |sin(turn)| slack for rounding/collinearity
            int combos = 0, cells = 0, bad = 0;
            double worst = 0; string worstMsg = "(none)";
            foreach (var opt in Combos())
            {
                combos++;
                var res = PolygonalWallGenerator.Generate(opt);
                foreach (var c in res.Cells)
                {
                    cells++;
                    int m = c.VertexCount;
                    if (m < 3) { bad++; continue; }
                    // Orientation from the signed area; convex ⇒ all turns same sign.
                    double area2 = 0;
                    for (int k = 0; k < m; k++)
                    {
                        int k1 = (k + 1) % m;
                        area2 += c.Us[k] * c.Vs[k1] - c.Us[k1] * c.Vs[k];
                    }
                    double sign = area2 >= 0 ? 1.0 : -1.0; // CCW => +1
                    for (int k = 0; k < m; k++)
                    {
                        int kp = (k + m - 1) % m, kn = (k + 1) % m;
                        double e1u = c.Us[k] - c.Us[kp], e1v = c.Vs[k] - c.Vs[kp];
                        double e2u = c.Us[kn] - c.Us[k], e2v = c.Vs[kn] - c.Vs[k];
                        double l1 = Math.Sqrt(e1u * e1u + e1v * e1v);
                        double l2 = Math.Sqrt(e2u * e2u + e2v * e2v);
                        if (l1 < 1e-12 || l2 < 1e-12) continue; // duplicate vertex, no turn
                        double cross = (e1u * e2v - e1v * e2u) / (l1 * l2); // sin(turn) in [-1,1]
                        double signed = sign * cross;
                        if (signed < -EPS)
                        {
                            bad++;
                            if (-signed > worst) { worst = -signed; worstMsg = $"seed={opt.Seed} grid={opt.GridX}x{opt.GridY} lloyd={opt.LloydIterations} sin(turn)={cross:E3}"; }
                            break;
                        }
                    }
                }
            }
            Assert.True(bad == 0, $"T1 non-convex cell(s): {bad} across {cells} cells / {combos} combos; worst reflex sin={worst:E3} @ {worstMsg}");
        }

        /// <summary>T2 — the cells partition the wall rectangle: pairwise
        /// intersection area ≈ 0 (Clipper2), and total area ≈ W·H.</summary>
        [Fact]
        public void T2_Cells_TileRectangle_Disjoint()
        {
            int combos = 0;
            double worstOverlapFrac = 0, worstCoverErr = 0;
            string worstOverlapMsg = "(none)", worstCoverMsg = "(none)";
            foreach (var opt in Combos())
            {
                combos++;
                double wh = opt.Width * opt.Height;
                var res = PolygonalWallGenerator.Generate(opt);

                // completeness: independent shoelace area sum ≈ W·H
                double sum = 0;
                var polys = new List<PathD>(res.Cells.Count);
                foreach (var c in res.Cells)
                {
                    sum += ShoelaceArea(c);
                    polys.Add(ToPath(c));
                }
                double coverErr = Math.Abs(sum - wh) / wh;
                if (coverErr > worstCoverErr) { worstCoverErr = coverErr; worstCoverMsg = $"seed={opt.Seed} grid={opt.GridX}x{opt.GridY} lloyd={opt.LloydIterations} sum={sum:F6} wh={wh:F6}"; }

                // pairwise interior-disjointness: Clipper2 intersection area ~ 0
                for (int i = 0; i < polys.Count; i++)
                    for (int j = i + 1; j < polys.Count; j++)
                    {
                        double inter = IntersectionArea(polys[i], polys[j]);
                        double frac = inter / wh;
                        if (frac > worstOverlapFrac) { worstOverlapFrac = frac; worstOverlapMsg = $"seed={opt.Seed} grid={opt.GridX}x{opt.GridY} lloyd={opt.LloydIterations} inter={inter:E3}"; }
                    }
            }
            Assert.True(worstOverlapFrac < 1e-6, $"T2 cells overlap: worst pairwise intersection {worstOverlapFrac:E3} of W·H @ {worstOverlapMsg}");
            Assert.True(worstCoverErr < 1e-6, $"T2 cells do not tile: worst |Σarea−W·H|/W·H = {worstCoverErr:E3} @ {worstCoverMsg}");
        }

        /// <summary>T3 — determinism: same Seed and options ⇒ identical output
        /// (bit-exact vertices), the reproducibility contract WallGenOptions.Seed
        /// documents.</summary>
        [Fact]
        public void T3_Deterministic_SameSeed()
        {
            foreach (var opt in Combos())
            {
                var a = PolygonalWallGenerator.Generate(opt);
                var b = PolygonalWallGenerator.Generate(new WallGenOptions
                {
                    Width = opt.Width, Height = opt.Height, Coursing = opt.Coursing,
                    Courses = opt.Courses, GridX = opt.GridX, GridY = opt.GridY,
                    Seed = opt.Seed, LloydIterations = opt.LloydIterations,
                    SizeGradeCv = opt.SizeGradeCv, SliverMinInradiusFrac = opt.SliverMinInradiusFrac,
                    MaxSliverPasses = opt.MaxSliverPasses
                });
                Assert.True(a.Cells.Count == b.Cells.Count,
                    $"T3 cell count differs for seed={opt.Seed} grid={opt.GridX}x{opt.GridY} lloyd={opt.LloydIterations}: {a.Cells.Count} vs {b.Cells.Count}");
                for (int i = 0; i < a.Cells.Count; i++)
                {
                    var ca = a.Cells[i]; var cb = b.Cells[i];
                    Assert.True(ca.VertexCount == cb.VertexCount,
                        $"T3 vertex count differs at cell {i} (seed={opt.Seed})");
                    for (int k = 0; k < ca.VertexCount; k++)
                        Assert.True(ca.Us[k] == cb.Us[k] && ca.Vs[k] == cb.Vs[k],
                            $"T3 vertex {k} of cell {i} differs (seed={opt.Seed}): ({ca.Us[k]},{ca.Vs[k]}) vs ({cb.Us[k]},{cb.Vs[k]})");
                }
            }
        }

        // ---- independent geometry oracles ------------------------------------
        static double ShoelaceArea(WallCell c)
        {
            double s = 0; int m = c.VertexCount;
            for (int k = 0; k < m; k++)
            {
                int k1 = (k + 1) % m;
                s += c.Us[k] * c.Vs[k1] - c.Us[k1] * c.Vs[k];
            }
            return Math.Abs(s) / 2.0;
        }

        static double IntersectionArea(PathD a, PathD b)
        {
            var pa = new PathsD { a };
            var pb = new PathsD { b };
            var inter = Clipper.Intersect(pa, pb, FillRule.NonZero);
            double area = 0;
            foreach (var loop in inter) area += Math.Abs(Clipper.Area(loop));
            return area;
        }

        static PathD ToPath(WallCell c)
        {
            var p = new PathD(c.VertexCount);
            for (int k = 0; k < c.VertexCount; k++) p.Add(new PointD(c.Us[k], c.Vs[k]));
            return p;
        }
    }
}
