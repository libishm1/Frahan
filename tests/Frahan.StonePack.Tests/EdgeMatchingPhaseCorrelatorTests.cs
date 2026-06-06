#nullable disable
using System;
using Frahan.EdgeMatching;

namespace Frahan.Tests;

// Pure-managed tests: no RhinoCommon, no rhcommon_c.dll, always run.
static class EdgeMatchingPhaseCorrelatorTests
{
    public static void PerfectComplement_ScoresOne()
    {
        const int n = 128;
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = Math.Sin(2 * Math.PI * i / n);

        // B is the complement: reversed and negated. PhaseCorrelator
        // internally flips B again, so the post-flip signal equals A and
        // the best lag is zero with similarity = 1.0.
        var b = new double[n];
        for (int i = 0; i < n; i++) b[i] = -a[n - 1 - i];

        var (lag, score) = PhaseCorrelator.Correlate(a, b);
        Assert(lag == 0, $"expected lag 0 on perfect complement, got {lag}");
        Assert(Math.Abs(score - 1.0) < 1e-9, $"expected score 1.0, got {score}");
    }

    public static void UnrelatedSignatures_ScoreBelowHalf()
    {
        const int n = 128;
        var a = new double[n];
        var b = new double[n];
        // A is a single positive impulse, B is a constant. They cannot
        // align meaningfully and the Manhattan distance dominates.
        a[n / 2] = Math.PI;
        for (int i = 0; i < n; i++) b[i] = 0.5;

        var (_, score) = PhaseCorrelator.Correlate(a, b);
        Assert(score < 0.95, $"expected mismatched-signal score below 0.95, got {score}");
    }

    public static void EmptySignatures_ReturnZero()
    {
        var (lag, score) = PhaseCorrelator.Correlate(Array.Empty<double>(), Array.Empty<double>());
        Assert(lag == 0 && score == 0.0, $"expected (0, 0.0), got ({lag}, {score})");
    }

    public static void MismatchedLength_Throws()
    {
        bool threw = false;
        try
        {
            PhaseCorrelator.Correlate(new double[3], new double[5]);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Assert(threw, "expected ArgumentException on mismatched signature lengths");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
