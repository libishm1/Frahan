#nullable disable
using System;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// R2 global non-overlap resolve (2026-05-25). Three knobs, all default-OFF:
//   OverlapPenalty (A3), EdgeExclusivity (B6), ResolveOverlap (post-solve 2D
//   depenetration polish). Two kinds of test:
//   (1) Defaults_* : pure-managed. Lock that a fresh AssemblyOptions leaves the
//       new R2 knobs OFF, so the default solver path is byte-for-byte unchanged.
//   (2) Resolve_* : exercise OverlapResolver2D on synthetic overlapping squares.
//       These call Curve.CreateBooleanIntersection / AreaMassProperties, which
//       need rhcommon_c, so they SKIP on hosts without a live Rhino runtime
//       (same path as the other edgematch pipeline tests).
static class EdgeMatchingOverlapResolveTests
{
    // ---- (1) Pure-managed default guards ----------------------------------

    public static void Defaults_R2KnobsAreOff()
    {
        var opt = new AssemblyOptions();
        Assert(opt.OverlapPenalty == 0.0,
            $"default OverlapPenalty must be 0, got {opt.OverlapPenalty}");
        Assert(opt.EdgeExclusivity == false,
            "default EdgeExclusivity must be false");
        Assert(opt.ResolveOverlap == false,
            "default ResolveOverlap must be false");
    }

    public static void Defaults_ResolveTuningHasSaneValues()
    {
        var opt = new AssemblyOptions();
        Assert(opt.ResolveOverlapTolerance >= 0.0,
            "tolerance must be non-negative");
        Assert(opt.ResolveOverlapIterations >= 1,
            "iterations must be >= 1");
        Assert(opt.ResolveOverlapRelaxation > 0.0 && opt.ResolveOverlapRelaxation <= 1.0,
            $"relaxation must be in (0,1], got {opt.ResolveOverlapRelaxation}");
    }

    // ---- (2) OverlapResolver2D behaviour (Rhino runtime) -------------------

    // Two unit-ish squares overlapping heavily; anchor the first. After the
    // polish the second must be pushed out so pairwise overlap is within
    // tolerance, and the anchor must NOT have moved.
    public static void Resolve_ReducesSyntheticOverlap()
    {
        var a = MakeSquare("A", 0, 0, 10, 10);   // [0,10]x[0,10]
        var b = MakeSquare("B", 4, 0, 14, 10);   // [4,14]x[0,10] -> 6x10 overlap
        a.IsAnchored = true;

        var state = new AssemblyState();
        state.PlacedPanels.Add(a);
        state.PlacedPanels.Add(b);
        state.AppliedTransforms["A"] = Transform.Identity;
        state.AppliedTransforms["B"] = Transform.Identity;

        var opt = new AssemblyOptions
        {
            ResolveOverlap = true,
            ResolveOverlapTolerance = 0.005,
            ResolveOverlapIterations = 100,
            ResolveOverlapRelaxation = 0.5,
        };

        double before = OverlapAreaFraction(a, state.AppliedTransforms["A"],
                                            b, state.AppliedTransforms["B"]);
        Assert(before > 0.4, $"setup must overlap heavily, got {before:F3}");

        var (frac, iters) = OverlapResolver2D.Resolve(state, opt);

        Assert(iters >= 1, "polish must run at least one iteration");
        Assert(frac <= 0.05, $"final overlap fraction must be small, got {frac:F4}");

        // Anchor unchanged.
        Assert(TransformsClose(state.AppliedTransforms["A"], Transform.Identity, 1e-9),
            "anchored panel A must not move");

        // The moved square must actually be separated now: re-measure.
        double after = OverlapAreaFraction(a, state.AppliedTransforms["A"],
                                           b, state.AppliedTransforms["B"]);
        Assert(after < before,
            $"overlap must drop after polish: before={before:F3} after={after:F3}");
    }

    public static void Resolve_IsDeterministic()
    {
        (double frac, Transform tA, Transform tB) Run()
        {
            var a = MakeSquare("A", 0, 0, 10, 10);
            var b = MakeSquare("B", 4, 0, 14, 10);
            a.IsAnchored = true;
            var state = new AssemblyState();
            state.PlacedPanels.Add(a);
            state.PlacedPanels.Add(b);
            state.AppliedTransforms["A"] = Transform.Identity;
            state.AppliedTransforms["B"] = Transform.Identity;
            var opt = new AssemblyOptions
            {
                ResolveOverlap = true,
                ResolveOverlapTolerance = 0.005,
                ResolveOverlapIterations = 100,
                ResolveOverlapRelaxation = 0.5,
            };
            var (f, _) = OverlapResolver2D.Resolve(state, opt);
            return (f, state.AppliedTransforms["A"], state.AppliedTransforms["B"]);
        }

        var r1 = Run();
        var r2 = Run();
        Assert(r1.frac.Equals(r2.frac), $"overlap fraction drift {r1.frac} vs {r2.frac}");
        Assert(TransformsClose(r1.tA, r2.tA, 0.0), "anchor transform drift");
        Assert(TransformsClose(r1.tB, r2.tB, 0.0), "moved transform drift");
    }

    // Disjoint squares: the polish must run but move nothing (no overlap).
    public static void Resolve_NoOverlap_NoMovement()
    {
        var a = MakeSquare("A", 0, 0, 10, 10);
        var b = MakeSquare("B", 20, 0, 30, 10);   // disjoint
        a.IsAnchored = true;
        var state = new AssemblyState();
        state.PlacedPanels.Add(a);
        state.PlacedPanels.Add(b);
        state.AppliedTransforms["A"] = Transform.Identity;
        state.AppliedTransforms["B"] = Transform.Identity;

        var opt = new AssemblyOptions { ResolveOverlap = true, ResolveOverlapIterations = 20 };
        var (frac, _) = OverlapResolver2D.Resolve(state, opt);

        Assert(frac <= 1e-9, $"disjoint pieces must report ~0 overlap, got {frac:F6}");
        Assert(TransformsClose(state.AppliedTransforms["B"], Transform.Identity, 1e-9),
            "disjoint moving panel must stay put");
    }

    // ---- helpers ----------------------------------------------------------

    private static Panel MakeSquare(string id, double x0, double y0, double x1, double y1)
    {
        var poly = new Polyline
        {
            new Point3d(x0, y0, 0),
            new Point3d(x1, y0, 0),
            new Point3d(x1, y1, 0),
            new Point3d(x0, y1, 0),
            new Point3d(x0, y0, 0),
        };
        return new Panel(id, poly.ToPolylineCurve(), PanelKind.Shard);
    }

    private static double OverlapAreaFraction(Panel a, Transform ta, Panel b, Transform tb)
    {
        var ca = (PolylineCurve)a.SourceContour.DuplicateCurve(); ca.Transform(ta);
        var cb = (PolylineCurve)b.SourceContour.DuplicateCurve(); cb.Transform(tb);
        var regions = Curve.CreateBooleanIntersection(ca, cb, 1e-4);
        double overlap = 0.0;
        if (regions != null)
            foreach (var r in regions)
            {
                if (r == null || !r.IsClosed) continue;
                var amp = AreaMassProperties.Compute(r);
                if (amp != null) overlap += Math.Abs(amp.Area);
            }
        var ampB = AreaMassProperties.Compute(cb);
        double areaB = ampB == null ? 0.0 : Math.Abs(ampB.Area);
        return areaB > 1e-12 ? overlap / areaB : 0.0;
    }

    private static bool TransformsClose(Transform a, Transform b, double tol)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (Math.Abs(a[r, c] - b[r, c]) > tol) return false;
        return true;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
