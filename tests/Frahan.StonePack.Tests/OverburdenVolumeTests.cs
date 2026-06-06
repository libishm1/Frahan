#nullable disable
using System;
using Frahan.Core.Earthworks;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.Earthworks.OverburdenVolume (cut-and-fill -> rock-face core,
// SLM card A5 / math_functions sec 3-4). Pure managed, no Rhino. Exact-on-TIN invariants.

static class OverburdenVolumeTests
{
    // unit right triangle (0,0),(1,0),(0,1): plan area 0.5
    private static (double[] g, double[] b, int[] t) UnitTri(double zt0, double zt1, double zt2,
                                                             double zb0, double zb1, double zb2)
    {
        var g = new double[] { 0, 0, zt0, 1, 0, zt1, 0, 1, zt2 };
        var b = new double[] { zb0, zb1, zb2 };
        var t = new int[] { 0, 1, 2 };
        return (g, b, t);
    }

    public static void ConstantDepth_VolumeEqualsAreaTimesDepth()
    {
        // ground 1 above bedrock 0 everywhere -> cut = area*depth = 0.5*1 = 0.5, fill = 0
        var (g, b, t) = UnitTri(1, 1, 1, 0, 0, 0);
        var r = OverburdenVolume.Compute(g, b, t);
        Assert(Math.Abs(r.CutVolume - 0.5) < 1e-12, $"cut should be 0.5, got {r.CutVolume}");
        Assert(Math.Abs(r.FillVolume) < 1e-12, $"fill should be 0, got {r.FillVolume}");
        Assert(Math.Abs(r.PlanArea - 0.5) < 1e-12, $"plan area should be 0.5, got {r.PlanArea}");
        Assert(Math.Abs(r.Overburden - 0.5) < 1e-12, "overburden alias == cut");
    }

    public static void SlopedAllPositive_UsesMeanDepth()
    {
        // d = (3,0,0): mean = 1, area 0.5 -> cut = 0.5, fill 0
        var (g, b, t) = UnitTri(3, 0, 0, 0, 0, 0);
        var r = OverburdenVolume.Compute(g, b, t);
        Assert(Math.Abs(r.CutVolume - 0.5) < 1e-12, $"cut should be 0.5 (mean depth 1), got {r.CutVolume}");
        Assert(Math.Abs(r.FillVolume) < 1e-12, $"fill should be 0, got {r.FillVolume}");
    }

    public static void SignChange_SplitsCutAndFillExactly()
    {
        // d = (1,1,-1) over the unit tri -> d(x,y) = 1 - 2y, zero at y=0.5.
        // Analytic: cut = 0.2083333..., fill = 0.0416666..., net = 1/6.
        var (g, b, t) = UnitTri(1, 1, -1, 0, 0, 0);
        var r = OverburdenVolume.Compute(g, b, t);
        Assert(Math.Abs(r.CutVolume - 0.2083333333) < 1e-9, $"cut should be 0.208333, got {r.CutVolume}");
        Assert(Math.Abs(r.FillVolume - 0.0416666667) < 1e-9, $"fill should be 0.041667, got {r.FillVolume}");
        Assert(Math.Abs(r.NetVolume - (1.0 / 6.0)) < 1e-9, $"net should be 1/6, got {r.NetVolume}");
    }

    public static void NetEqualsMeanTimesArea_Invariant()
    {
        // net (cut-fill) must equal area * mean(d) for any d (centroid-rule consistency)
        var (g, b, t) = UnitTri(2.5, -1.0, 0.3, 0, 0, 0);
        var r = OverburdenVolume.Compute(g, b, t);
        double expectedNet = 0.5 * (2.5 - 1.0 + 0.3) / 3.0;
        Assert(Math.Abs(r.NetVolume - expectedNet) < 1e-9, $"net should be {expectedNet}, got {r.NetVolume}");
    }

    public static void QuarryScale_RecenterKeepsAreaExact()
    {
        // same unit tri translated to UTM-scale origin: area + volume must be unchanged
        // (T1 recenter inside Compute prevents cancellation).
        double ox = 466021.1, oy = 6691584.7;
        var g = new double[] { ox, oy, 1, ox + 1, oy, 1, ox, oy + 1, 1 };
        var b = new double[] { 0, 0, 0 };
        var t = new int[] { 0, 1, 2 };
        var r = OverburdenVolume.Compute(g, b, t);
        Assert(Math.Abs(r.PlanArea - 0.5) < 1e-9, $"plan area at UTM scale should be 0.5, got {r.PlanArea}");
        Assert(Math.Abs(r.CutVolume - 0.5) < 1e-9, $"cut at UTM scale should be 0.5, got {r.CutVolume}");
    }

    public static void TwoTriangleSquare_SumsBoth()
    {
        // unit square as two triangles, depth 2 -> cut = area(1)*2 = 2
        var g = new double[] { 0, 0, 2, 1, 0, 2, 1, 1, 2, 0, 1, 2 };
        var b = new double[] { 0, 0, 0, 0 };
        var t = new int[] { 0, 1, 2, 0, 2, 3 };
        var r = OverburdenVolume.Compute(g, b, t);
        Assert(Math.Abs(r.PlanArea - 1.0) < 1e-12, $"square area should be 1, got {r.PlanArea}");
        Assert(Math.Abs(r.CutVolume - 2.0) < 1e-12, $"cut should be 2, got {r.CutVolume}");
    }

    public static void NullAndBadInputs_Throw()
    {
        bool t1 = false, t2 = false;
        try { OverburdenVolume.Compute(null, new double[] { 0 }, new int[] { 0, 1, 2 }); }
        catch (ArgumentNullException) { t1 = true; }
        Assert(t1, "null groundXyz throws");
        try { OverburdenVolume.Compute(new double[] { 0, 0, 1, 1, 0, 1, 0, 1, 1 }, new double[] { 0, 0 }, new int[] { 0, 1, 2 }); }
        catch (ArgumentException) { t2 = true; }
        Assert(t2, "bedrockZ length mismatch throws");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("OverburdenVolume: " + message);
    }
}
