#nullable disable
using System;
using Frahan.Core.Discontinuity;

namespace Frahan.Tests;

// Pins the Rhino-free KinematicAnalysis.Intersection (risk H2: this geology
// component was made RhinoCommon-free by inlining the normal + cross math as
// doubles). The values below are analytic, so they also guard the arithmetic.
static class KinematicAnalysisTests
{
    public static void Intersection_SymmetricPlanes_IsHorizontalEastWest()
    {
        // Two planes of equal dip (30 deg) dipping N and S: their line of
        // intersection is horizontal (plunge 0) and runs E-W (trend 270 as the
        // downward-pointing axis). Hand-computed from the pole cross product.
        KinematicAnalysis.Intersection(30, 0, 30, 180, out double plunge, out double trend);
        Assert(Math.Abs(plunge - 0.0) < 1e-9, $"plunge {plunge} != 0");
        Assert(Math.Abs(trend - 270.0) < 1e-9, $"trend {trend} != 270");
    }

    public static void Intersection_IdenticalPlanes_IsDegenerate()
    {
        // Parallel planes have no unique intersection line: guarded to (0,0).
        KinematicAnalysis.Intersection(45, 90, 45, 90, out double plunge, out double trend);
        Assert(plunge == 0.0 && trend == 0.0, $"degenerate case not (0,0): {plunge},{trend}");
    }

    public static void Intersection_OutputsInValidRanges()
    {
        KinematicAnalysis.Intersection(60, 45, 35, 200, out double plunge, out double trend);
        Assert(plunge >= 0.0 && plunge <= 90.0, $"plunge {plunge} out of [0,90]");
        Assert(trend >= 0.0 && trend < 360.0, $"trend {trend} out of [0,360)");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException("KinematicAnalysis: " + msg);
    }
}
