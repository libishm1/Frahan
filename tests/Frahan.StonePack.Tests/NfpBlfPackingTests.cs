#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.GH.TwoD;
using Frahan.Masonry.Geometry;
using Rhino.Geometry;

namespace Frahan.Tests;

// Headless verification of the exact NFP Bottom-Left-Fill solver
// (IrregularSheetFillNfpBlf). Exercises the full Rhino-curve path via
// rhcommon_c. The core invariant under test is ZERO overlap (the hard
// non-overlap guarantee) plus that the solver actually places parts.
// Python reference + 2x study: outputs/2026-06-03/pack2d_nfp_evolution/.

static class NfpBlfPackingTests
{
    public static void Pack_PlacesParts_NoOverlap()
    {
        var sheet = Rect(0, 0, 12, 12);
        var parts = new List<Curve>();
        for (int i = 0; i < 16; i++) parts.Add(Rect(0, 0, 2, 2));

        var solver = new IrregularSheetFillNfpBlf(
            new[] { (Curve)sheet },
            new List<IReadOnlyList<Curve>> { new List<Curve>() },
            spacing: 0.1,
            rotationsDeg: new[] { 0.0, 90.0, 180.0, 270.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            seed: 0);
        var res = solver.Pack(parts);

        Assert(res.PackedCurves.Count >= 8,
            $"expected >= 8 parts placed in a 12x12 sheet, got {res.PackedCurves.Count}");

        var loops = res.PackedCurves.Select(ToLoop).Where(l => l != null).ToList();
        Assert(loops.Count == res.PackedCurves.Count, "all packed curves must be polylines");

        double overlap = 0.0, totalArea = 0.0;
        foreach (var l in loops) totalArea += Math.Abs(Area(l));
        for (int i = 0; i < loops.Count; i++)
            for (int j = i + 1; j < loops.Count; j++)
            {
                var inter = Clipper2Adapter.IntersectLoops(
                    new List<IReadOnlyList<(double X, double Y)>> { loops[i] },
                    new List<IReadOnlyList<(double X, double Y)>> { loops[j] });
                foreach (var lp in inter) overlap += Math.Abs(Area(lp));
            }
        Assert(overlap < 1e-3 * Math.Max(1.0, totalArea),
            $"expected zero overlap, got {overlap} over total area {totalArea}");
    }

    public static void Pack_RespectsHole_NoOverlap()
    {
        var sheet = Rect(0, 0, 12, 12);
        var hole = Rect(5, 5, 2, 2);
        var parts = new List<Curve>();
        for (int i = 0; i < 12; i++) parts.Add(Rect(0, 0, 2, 2));

        var solver = new IrregularSheetFillNfpBlf(
            new[] { (Curve)sheet },
            new List<IReadOnlyList<Curve>> { new List<Curve> { hole } },
            spacing: 0.1,
            rotationsDeg: new[] { 0.0, 90.0, 180.0, 270.0 },
            tolerance: 0.01,
            sortMode: PackingSortMode.AreaDescending,
            seed: 0);
        var res = solver.Pack(parts);

        Assert(res.PackedCurves.Count >= 6,
            $"expected >= 6 parts placed around a hole, got {res.PackedCurves.Count}");

        // No packed part may intersect the hole.
        var holeLoop = ToLoop(hole);
        double holeHit = 0.0;
        foreach (var c in res.PackedCurves)
        {
            var l = ToLoop(c);
            if (l == null) continue;
            var inter = Clipper2Adapter.IntersectLoops(
                new List<IReadOnlyList<(double X, double Y)>> { l },
                new List<IReadOnlyList<(double X, double Y)>> { holeLoop });
            foreach (var lp in inter) holeHit += Math.Abs(Area(lp));
        }
        Assert(holeHit < 1e-3, $"parts must not enter the hole, got {holeHit}");
    }

    // ─── helpers ──────────────────────────────────────────────────────
    private static PolylineCurve Rect(double x, double y, double w, double h)
        => new PolylineCurve(new[]
        {
            new Point3d(x, y, 0), new Point3d(x + w, y, 0),
            new Point3d(x + w, y + h, 0), new Point3d(x, y + h, 0),
            new Point3d(x, y, 0),
        });

    private static List<(double X, double Y)> ToLoop(Curve c)
    {
        if (!c.TryGetPolyline(out var pl)) return null;
        int n = pl.Count;
        if (n > 1 && pl[0].DistanceTo(pl[n - 1]) < 1e-9) n--;
        if (n < 3) return null;
        var loop = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++) loop.Add((pl[i].X, pl[i].Y));
        return loop;
    }

    private static double Area(List<(double X, double Y)> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var j = (i + 1) % loop.Count;
            a += loop[i].X * loop[j].Y - loop[j].X * loop[i].Y;
        }
        return 0.5 * a;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
