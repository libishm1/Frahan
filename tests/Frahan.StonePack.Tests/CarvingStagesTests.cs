#nullable disable
using System;
using Frahan.Core.Sculpt;

namespace Frahan.Tests;

// Pure-managed tests for the carving-pass offset schedule (no Rhino).
static class CarvingStagesTests
{
    public static void OffsetSchedule_DescendsMaxToFinish()
    {
        var d = CarvingStages.OffsetSchedule(4, 0.12, 0.0);
        Assert(d.Length == 4, "4 stages");
        Assert(Math.Abs(d[0] - 0.12) < 1e-9, "stage 0 = max offset");
        Assert(Math.Abs(d[3] - 0.0) < 1e-9, "last stage = finish allowance");
        Assert(d[0] > d[1] && d[1] > d[2] && d[2] > d[3], "monotonic descending");
    }

    public static void OffsetSchedule_SingleStage_IsFinish()
    {
        var d = CarvingStages.OffsetSchedule(1, 0.1, 0.02);
        Assert(d.Length == 1 && Math.Abs(d[0] - 0.02) < 1e-9, "single stage = finish allowance");
    }

    public static void OffsetSchedule_RespectsFinishAllowance()
    {
        var d = CarvingStages.OffsetSchedule(3, 0.1, 0.02);
        Assert(Math.Abs(d[0] - 0.1) < 1e-9 && Math.Abs(d[2] - 0.02) < 1e-9, "ends at max and finish");
    }

    public static void OffsetSchedule_InvalidStages_Throws()
    {
        bool threw = false;
        try { CarvingStages.OffsetSchedule(0, 0.1, 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "stages < 1 throws");
    }

    public static void OffsetSchedule_MaxBelowFinish_Throws()
    {
        bool threw = false;
        try { CarvingStages.OffsetSchedule(3, 0.01, 0.05); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "maxOffset < finishAllowance throws");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException("CarvingStages: " + m); }
}
