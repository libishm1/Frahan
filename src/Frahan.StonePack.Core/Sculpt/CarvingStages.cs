#nullable disable
using System;

namespace Frahan.Core.Sculpt;

// =============================================================================
// CarvingStages — roughing-pass schedule for the digital pointing machine.
//
// A carver (or robotic mill) removes stock in steady passes, not one cut. The
// digital equivalent is a stack of offset shells from the rough block down to
// the finished sculpture surface: shell_k = the target offset OUTWARD by r_k,
// with r_k stepping from a coarse max-offset down to a finish allowance. The GH
// layer realises each shell with RhinoCommon Mesh.Offset (no booleans, so no
// large-mesh boolean blow-up). This class is just the (pure-managed, testable)
// distance schedule.
// =============================================================================

public static class CarvingStages
{
    /// <summary>
    /// Outward-offset distances for <paramref name="stages"/> roughing passes,
    /// stepping linearly from <paramref name="maxOffset"/> (stage 0, roughest)
    /// down to <paramref name="finishAllowance"/> (last stage, finish surface).
    /// </summary>
    public static double[] OffsetSchedule(int stages, double maxOffset, double finishAllowance = 0.0)
    {
        if (stages < 1) throw new ArgumentOutOfRangeException(nameof(stages), "need at least 1 stage");
        if (maxOffset < finishAllowance)
            throw new ArgumentException("maxOffset must be >= finishAllowance", nameof(maxOffset));

        var d = new double[stages];
        if (stages == 1) { d[0] = finishAllowance; return d; }
        for (int i = 0; i < stages; i++)
        {
            double t = (double)i / (stages - 1);          // 0 .. 1
            d[i] = maxOffset + t * (finishAllowance - maxOffset);  // max -> finish
        }
        return d;
    }
}
