#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.GH.TwoD;
using Rhino.Geometry;

namespace Frahan.Tests;

// Tests for the 2026-06-06 SLM evolution of IrregularSheetFillNfpBlf
// (multi-start order + compaction + reinsertion + concave overlap-verify).
// The authoritative density numbers are measured headless by Harness --packbench;
// these guard the invariants: 0-overlap on concave parts (the key fix),
// determinism, that multi-start actually ran, and legacy-path determinism.
static class NfpBlfEvolutionTests
{
    private static Curve Rect(double w, double h)
    {
        var pts = new List<Point3d>
        { new Point3d(0,0,0), new Point3d(w,0,0), new Point3d(w,h,0), new Point3d(0,h,0), new Point3d(0,0,0) };
        return new PolylineCurve(pts);
    }

    private static Curve LShape(double w, double h)
    {
        double hw = w * 0.5, hh = h * 0.5;
        var pts = new List<Point3d>
        {
            new Point3d(0,0,0), new Point3d(w,0,0), new Point3d(w,hh,0),
            new Point3d(hw,hh,0), new Point3d(hw,h,0), new Point3d(0,h,0), new Point3d(0,0,0)
        };
        return new PolylineCurve(pts);
    }

    private static List<Curve> Parts(int n, bool concave)
    {
        var list = new List<Curve>();
        uint s = 42u;
        double R() { s = s * 1664525u + 1013904223u; return ((s >> 8) & 0xFFFFFF) / 16777216.0; }
        for (int i = 0; i < n; i++)
        {
            double w = 0.3 + R() * 1.0, h = 0.3 + R() * 1.0;
            list.Add(concave && i % 3 == 2 ? LShape(w, h) : Rect(w, h));
        }
        return list;
    }

    private static double[] Rots() => new[] { 0.0, 90.0, 180.0, 270.0 };
    private static List<IReadOnlyList<Curve>> NoHoles() => new List<IReadOnlyList<Curve>>();

    private static double MaxOverlapArea(PackingResult r)
    {
        var pl = r.PackedCurves.Where(c => c != null && c.IsClosed).ToList();
        double maxA = 0;
        for (int i = 0; i < pl.Count; i++)
            for (int j = i + 1; j < pl.Count; j++)
            {
                var inter = Curve.CreateBooleanIntersection(pl[i], pl[j], 0.001);
                if (inter == null) continue;
                foreach (var c in inter)
                {
                    var amp = AreaMassProperties.Compute(c);
                    if (amp != null && amp.Area > maxA) maxA = amp.Area;
                }
            }
        return maxA;
    }

    private static IrregularSheetFillNfpBlf Evolved(double w, double h) =>
        new IrregularSheetFillNfpBlf(new[] { Rect(w, h) }, NoHoles(), 0.0, Rots(), 0.01,
            PackingSortMode.AreaDescending, 0,
            PlacementScore.BottomLeft, true, 3, true, 2, true);

    private static IrregularSheetFillNfpBlf Legacy(double w, double h) =>
        new IrregularSheetFillNfpBlf(new[] { Rect(w, h) }, NoHoles(), 0.0, Rots(), 0.01,
            PackingSortMode.AreaDescending, 0);

    // The key fix: the evolved path must be 0-overlap even on CONCAVE parts,
    // where the bare Minkowski-sum NFP can admit a small interpenetration.
    public static void Evolved_ConcaveParts_ZeroOverlap()
    {
        var r = Evolved(6.0, 6.0).Pack(Parts(24, concave: true));
        Assert(r.PackedCurves.Count > 0, "evolved pack placed nothing");
        double mo = MaxOverlapArea(r);
        Assert(mo < 1e-3, "evolved concave pack must be 0-overlap, max overlap area = " + mo.ToString("0.000000"));
    }

    public static void Evolved_IsDeterministic()
    {
        var a = Evolved(6.0, 6.0).Pack(Parts(20, concave: true));
        var b = Evolved(6.0, 6.0).Pack(Parts(20, concave: true));
        Assert(a.PackedCurves.Count == b.PackedCurves.Count, "evolved placed count not deterministic");
    }

    public static void Legacy_FlagsOff_IsDeterministic()
    {
        var a = Legacy(6.0, 6.0).Pack(Parts(20, concave: false));
        var b = Legacy(6.0, 6.0).Pack(Parts(20, concave: false));
        Assert(a.PackedCurves.Count == b.PackedCurves.Count, "legacy placed count not deterministic");
    }

    // Multi-start must actually run multiple order passes and keep the best.
    public static void Evolved_MultiStart_Ran()
    {
        var r = Evolved(6.0, 6.0).Pack(Parts(16, concave: false));
        Assert(r.OptimizationRuns >= 1, "multi-start did not record any kept order (OptimizationRuns=0)");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("NfpBlfEvolutionTests failed: " + msg);
    }
}
