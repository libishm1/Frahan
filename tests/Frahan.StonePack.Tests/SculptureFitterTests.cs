#nullable disable
using System;
using Frahan.Core.Sculpt;

namespace Frahan.Tests;

// Pure-managed tests for the digital pointing-machine math (no Rhino).
static class SculptureFitterTests
{
    public static void EnlargeFactors_Factor_AllEqual()
    {
        var f = SculptureFitter.EnlargeFactors(new[] { 1.0, 2, 3 }, EnlargeMode.Factor, 2.5, null);
        Assert(Near(f[0], 2.5) && Near(f[1], 2.5) && Near(f[2], 2.5), "factor mode should be uniform 2.5");
    }

    public static void EnlargeFactors_TargetLongest_ScalesByLongestAxis()
    {
        var f = SculptureFitter.EnlargeFactors(new[] { 1.0, 2, 4 }, EnlargeMode.TargetLongest, 8, null);
        Assert(Near(f[0], 2.0) && Near(f[1], 2.0) && Near(f[2], 2.0), "longest 4->8 is 2x uniform");
    }

    public static void EnlargeFactors_TargetHeight_ScalesByZ()
    {
        var f = SculptureFitter.EnlargeFactors(new[] { 1.0, 2, 4 }, EnlargeMode.TargetHeight, 2, null);
        Assert(Near(f[2], 0.5), "height 4->2 is 0.5x");
    }

    public static void EnlargeFactors_NonUniform_PerAxis()
    {
        var f = SculptureFitter.EnlargeFactors(new[] { 2.0, 4, 5 }, EnlargeMode.NonUniformXyz, 0, new[] { 4.0, 4, 10 });
        Assert(Near(f[0], 2.0) && Near(f[1], 1.0) && Near(f[2], 2.0), "per-axis targets");
    }

    public static void FitsInBlock_Fits_PositiveClearance()
    {
        var r = SculptureFitter.FitsInBlock(new[] { 1.0, 2, 3 }, new[] { 4.0, 5, 6 }, 0);
        Assert(r.Fits && r.MinClearance > 0 && r.MaxScaleToFit > 1.0, "small piece fits with slack");
    }

    public static void FitsInBlock_TooBig_DoesNotFit()
    {
        var r = SculptureFitter.FitsInBlock(new[] { 1.0, 2, 10 }, new[] { 4.0, 5, 6 }, 0);
        Assert(!r.Fits && r.MaxScaleToFit < 1.0, "overflowing piece does not fit, scale-to-fit < 1");
    }

    public static void FitsInBlock_Margin_PushesOutOfFit()
    {
        var r0 = SculptureFitter.FitsInBlock(new[] { 3.9, 4.9, 5.9 }, new[] { 4.0, 5, 6 }, 0.0);
        var r1 = SculptureFitter.FitsInBlock(new[] { 3.9, 4.9, 5.9 }, new[] { 4.0, 5, 6 }, 0.2);
        Assert(r0.Fits && !r1.Fits, "kerf/roughing margin can turn a tight fit into no-fit");
    }

    public static void FitsInBlock_MatchesBestAxisOrientation()
    {
        // sculpture long on X, block long on Z; sorted-axis match should still fit.
        var r = SculptureFitter.FitsInBlock(new[] { 6.0, 1, 1 }, new[] { 1.0, 2, 7 }, 0);
        Assert(r.Fits, "largest-to-largest axis match fits regardless of input axis order");
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException("SculptureFitter: " + m); }
}
