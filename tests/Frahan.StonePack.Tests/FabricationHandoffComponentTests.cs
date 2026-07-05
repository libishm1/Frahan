#nullable disable
using System;
using Frahan.GH.Fabrication;

namespace Frahan.Tests;

// Metadata smoke tests for the robot-handoff wrappers. Pins the release bug
// found 2026-07-05: inputs NAMED "(optional)" were registered required, so the
// components never solved in the plane-only use case (GH skips SolveInstance
// when a required input is empty — no error, just silent empty outputs).
static class FabricationHandoffComponentTests
{
    public static void RobotTargets_OptionalInputs_AreOptional()
    {
        var c = new PlanesToRobotTargetsComponent();
        for (int i = 0; i < c.Params.Input.Count; i++)
        {
            var p = c.Params.Input[i];
            if (p.Name.IndexOf("optional", StringComparison.OrdinalIgnoreCase) >= 0)
                Assert(p.Optional, $"'{p.Name}' is named optional but registered required");
        }
        Assert(c.Params.Input.Count == 6, $"input count {c.Params.Input.Count}");
    }

    public static void KukaPrc_OptionalInputs_AreOptional()
    {
        var c = new PlanesToKukaPrcCommandsComponent();
        for (int i = 0; i < c.Params.Input.Count; i++)
        {
            var p = c.Params.Input[i];
            if (p.Name.IndexOf("optional", StringComparison.OrdinalIgnoreCase) >= 0)
                Assert(p.Optional, $"'{p.Name}' is named optional but registered required");
        }
        Assert(c.Params.Input.Count == 5, $"input count {c.Params.Input.Count}");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException("FabricationHandoff: " + msg);
    }
}
