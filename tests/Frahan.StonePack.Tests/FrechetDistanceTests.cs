#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino.Geometry;

namespace Frahan.Tests;

// Unit tests for the discrete Frechet distance primitive (R1 of the
// edge-matching theory-vs-implementation study). Pure managed, RhinoCommon-light
// (Point3d value container only), no live Rhino process needed.
static class FrechetDistanceTests
{
    private static List<Point3d> Line(double x0, double x1, int n, double y = 0.0)
    {
        var p = new List<Point3d>(n);
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0.0 : (double)i / (n - 1);
            p.Add(new Point3d(x0 + (x1 - x0) * t, y, 0.0));
        }
        return p;
    }

    public static void Identical_IsZero()
    {
        var a = Line(0, 10, 11);
        Assert(FrechetDistance.Discrete(a, a) < 1e-12, "identical curves must be 0");
    }

    public static void ParallelOffset_EqualsOffset()
    {
        var a = Line(0, 10, 11, 0.0);
        var b = Line(0, 10, 11, 2.5); // same x, shifted +2.5 in y
        double f = FrechetDistance.Discrete(a, b);
        Assert(Math.Abs(f - 2.5) < 1e-9, $"parallel offset 2.5 expected, got {f}");
    }

    public static void Symmetric()
    {
        var a = Line(0, 10, 7, 0);
        var b = Line(1, 9, 5, 3); // different length + offset to exercise the m>n swap
        double ab = FrechetDistance.Discrete(a, b);
        double ba = FrechetDistance.Discrete(b, a);
        Assert(Math.Abs(ab - ba) < 1e-9, $"must be symmetric: {ab} vs {ba}");
    }

    public static void DirectionSensitive_ReverseIsFar()
    {
        // B = A reversed (same points, opposite traversal). Hausdorff (point set)
        // is ~0, but ordered Frechet must couple a_0 (x=0) with b_0 (x=10) -> ~10.
        // This is the order/direction discrimination Hausdorff and closest-point
        // ICP residual lack.
        var a = Line(0, 10, 11);
        var b = new List<Point3d>(a); b.Reverse();
        double f = FrechetDistance.Discrete(a, b);
        Assert(f > 9.9, $"reversed curve must be ~10 apart in Frechet, got {f}");
    }

    public static void MaxNotMean_LocalBumpDominates()
    {
        // 3 collinear points; B identical except the MIDDLE point pushed out 5.
        // A mean gap would be ~5/3; Frechet is the max along the coupling -> 5.
        var a = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(2, 0, 0) };
        var b = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(1, 5, 0), new Point3d(2, 0, 0) };
        double f = FrechetDistance.Discrete(a, b);
        Assert(Math.Abs(f - 5.0) < 1e-9, $"local 5-unit bump -> Frechet 5, got {f}");
    }

    public static void GreaterOrEqualHausdorff()
    {
        var a = Line(0, 10, 9, 0);
        var b = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(5, 4, 0), new Point3d(10, 0, 0) };
        double f = FrechetDistance.Discrete(a, b);
        double hd = Math.Max(DirectedHausdorff(a, b), DirectedHausdorff(b, a));
        Assert(f >= hd - 1e-9, $"Frechet {f} must be >= Hausdorff {hd}");
    }

    public static void SinglePoints_IsDistance()
    {
        var a = new List<Point3d> { new Point3d(0, 0, 0) };
        var b = new List<Point3d> { new Point3d(3, 4, 0) };
        Assert(Math.Abs(FrechetDistance.Discrete(a, b) - 5.0) < 1e-12, "single points -> Euclidean distance");
    }

    public static void NullOrEmpty_Throws()
    {
        bool t1 = false, t2 = false;
        try { FrechetDistance.Discrete(null, new List<Point3d> { Point3d.Origin }); }
        catch (ArgumentNullException) { t1 = true; }
        try { FrechetDistance.Discrete(new List<Point3d>(), new List<Point3d> { Point3d.Origin }); }
        catch (ArgumentException) { t2 = true; }
        Assert(t1 && t2, "null and empty inputs must throw");
    }

    private static double DirectedHausdorff(IReadOnlyList<Point3d> a, IReadOnlyList<Point3d> b)
    {
        double max = 0;
        foreach (var p in a)
        {
            double min = double.MaxValue;
            foreach (var q in b) { double d = p.DistanceTo(q); if (d < min) min = d; }
            if (min > max) max = min;
        }
        return max;
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException("FrechetDistance: " + msg);
    }
}
