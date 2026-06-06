#nullable disable
using System;
using Frahan.Masonry.Geometry;
using Frahan.EdgeMatching;

namespace Frahan.Tests;

// Headless tests for the V3 evolution batch 1: the shared numeric-hygiene utility
// (GeometryNumerics, roadmap #1/#2) and the Hungarian big-M sentinel fix (T2/T3).
static class NumericsAndHungarianTests
{
    // ── GeometryNumerics ──────────────────────────────────────────────
    public static void Recenter_FarFromOrigin_CentroidNearZero()
    {
        // A small feature (~2 units) parked at quarry/UTM scale (~1e6).
        double b = 1_000_000.0;
        var pts = new double[] { b, b, b, b + 2, b, b, b + 2, b + 2, b, b, b + 2, b };
        var rec = GeometryNumerics.Recenter(pts, out var c);
        var rc = GeometryNumerics.Centroid(rec);
        Assert(Math.Abs(rc.X) < 1e-9 && Math.Abs(rc.Y) < 1e-9 && Math.Abs(rc.Z) < 1e-9,
            $"recentered centroid should be ~0, got ({rc.X},{rc.Y},{rc.Z})");
        // round-trip: add centroid back -> original within float64 precision (impossible at 1e6 without recenter)
        Assert(Math.Abs((rec[0] + c.X) - pts[0]) < 1e-6, "round-trip must restore the original coordinate");
        double diag = GeometryNumerics.BoundingBoxDiagonal(rec);
        Assert(diag > 2.0 && diag < 4.0, $"bbox diagonal of a 2x2 feature should be ~2.83, got {diag}");
    }

    public static void ScaleRelativeEpsilon_Scales()
    {
        double e1 = GeometryNumerics.ScaleRelativeEpsilon(1e-6, 1.0);
        double e6 = GeometryNumerics.ScaleRelativeEpsilon(1e-6, 1e6);
        Assert(Math.Abs(e1 - 1e-6) < 1e-18, $"at scale 1 epsilon stays base, got {e1}");
        Assert(Math.Abs(e6 - 1.0) < 1e-9, $"at scale 1e6 epsilon should be ~1.0, got {e6}");
        Assert(GeometryNumerics.ApproxEqual(1e6, 1e6 + 0.5, GeometryNumerics.ScaleRelativeEpsilon(1e-6, 1e6)),
            "scale-relative ApproxEqual should treat 0.5 mm at 1e6 as equal");
        Assert(!GeometryNumerics.ApproxEqual(1.0, 1.5, 1e-6), "tight eps at unit scale should distinguish 1.0 and 1.5");
    }

    public static void SafeIntegerScale_NoOverflow()
    {
        double s = GeometryNumerics.SafeIntegerScale(5_000_000.0, requested: 1e6);
        Assert(s * 5_000_000.0 < long.MaxValue, $"scale*maxCoord must stay under int64 max, got {s * 5e6}");
        double small = GeometryNumerics.SafeIntegerScale(1.0, requested: 1e6);
        Assert(Math.Abs(small - 1e6) < 1e-3, $"small coords keep the requested scale, got {small}");
    }

    public static void ToleranceBudget_OneSource()
    {
        var t = GeometryNumerics.ToleranceBudget.From(1e-3, 1e3);   // model 1e-3 at scale 1000 -> 1.0
        Assert(Math.Abs(t.Model - 1.0) < 1e-9, $"model tol should be 1.0, got {t.Model}");
        Assert(t.Join > t.Model && t.Intersection < t.Model && t.Snap < t.Intersection,
            "budget ordering: join > model > intersection > snap");
    }

    // ── Hungarian big-M sentinel fix ──────────────────────────────────
    public static void Hungarian_Rectangular_OptimalWithPadding()
    {
        // rows=2, cols=4 -> padded to 4x4 with big-M. Min assignment: row0->col1(3), row1->col2(2).
        var cost = new double[] { 5, 3, 9, 7,  8, 6, 2, 4 };
        var r = HungarianAssigner.Solve(cost, 2, 4);
        Assert(r.Length == 2, $"result length {r.Length}");
        Assert(r[0] == 1 && r[1] == 2, $"optimal should be row0->1, row1->2; got [{r[0]},{r[1]}]");
    }

    public static void Hungarian_DenseInfeasible_LargeCosts_FindsUniqueFeasible()
    {
        // Mostly infeasible, large feasible costs near 1e6 (where a fixed-1e18 sentinel poisons
        // the duals). Unique feasible assignment must be recovered exactly.
        double INF = HungarianAssigner.Infeasible;
        var cost = new double[]
        {
            1_000_000, INF, INF, INF,
            INF, 2_000_000, INF, INF,
            INF, INF, 3_000_000, INF,
            INF, INF, INF, 4_000_000,
        };
        var r = HungarianAssigner.Solve(cost, 4, 4);
        Assert(r[0] == 0 && r[1] == 1 && r[2] == 2 && r[3] == 3,
            $"unique feasible assignment must be [0,1,2,3]; got [{r[0]},{r[1]},{r[2]},{r[3]}]");
    }

    public static void Hungarian_InfeasibleRow_Unassigned()
    {
        double INF = HungarianAssigner.Infeasible;
        var cost = new double[] { 1, INF, INF, 2,  INF, INF, INF, INF };  // row1 all infeasible
        var r = HungarianAssigner.Solve(cost, 2, 4);
        Assert(r[1] == HungarianAssigner.Unassigned, $"all-infeasible row must be Unassigned, got {r[1]}");
        Assert(r[0] == 0 || r[0] == 3, $"row0 must take a feasible col (0 or 3), got {r[0]}");
    }

    // ── SpatialHash3D (soft-ICP q-bar speedup primitive) ──────────────
    public static void SpatialHash_RadiusQuery_MatchesBruteForce()
    {
        var rng = new Random(7);
        int n = 2000;
        var xyz = new double[n * 3];
        for (int i = 0; i < xyz.Length; i++) xyz[i] = rng.NextDouble();   // unit cube
        double r = 0.08;
        var hash = new SpatialHash3D(xyz, r);
        Assert(hash.Count == n, "count");
        for (int t = 0; t < 40; t++)
        {
            double px = rng.NextDouble(), py = rng.NextDouble(), pz = rng.NextDouble();
            var got = hash.QueryRadius(px, py, pz, r);
            // brute force
            var expect = new System.Collections.Generic.List<int>();
            double r2 = r * r;
            for (int i = 0; i < n; i++)
            {
                double ex = xyz[3 * i] - px, ey = xyz[3 * i + 1] - py, ez = xyz[3 * i + 2] - pz;
                if (ex * ex + ey * ey + ez * ez <= r2) expect.Add(i);
            }
            Assert(got.Count == expect.Count, $"radius query count {got.Count} vs brute {expect.Count}");
            for (int i = 0; i < got.Count; i++)
            {
                Assert(got[i] == expect[i], "radius query must match brute force (and be sorted)");
                if (i > 0) Assert(got[i] > got[i - 1], "indices must be strictly ascending (deterministic)");
            }
        }
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
