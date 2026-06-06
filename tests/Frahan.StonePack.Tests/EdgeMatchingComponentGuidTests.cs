#nullable disable
using System;
using Frahan.GH;

namespace Frahan.Tests;

// Regression guard. Two EdgeMatch components shipped on 2026-05-11
// with GUID strings containing 'G' (`...EDGE...`), which is not a
// hex digit. `new Guid(...)` threw FormatException at GH discovery
// time and the components never appeared on the ribbon. The build
// did not catch it because C# does not validate GUID string
// contents at compile time.
//
// These tests instantiate each new GH component and read its
// ComponentGuid getter, which forces Guid parsing. They run pure-
// managed (no Rhino native), so they PASS on any host where
// Grasshopper.dll is loadable — which is true in this test project
// (Grasshopper Private=true ships it into bin/).
static class EdgeMatchingComponentGuidTests
{
    public static void EdgeMatchSolveComponent_GuidParses()
    {
        var component = new EdgeMatchSolveComponent();
        var guid = component.ComponentGuid;
        Assert(guid != Guid.Empty, "EdgeMatchSolveComponent GUID parsed to Empty");
    }

    public static void EdgeMatchSegmentsComponent_GuidParses()
    {
        var component = new EdgeMatchSegmentsComponent();
        var guid = component.ComponentGuid;
        Assert(guid != Guid.Empty, "EdgeMatchSegmentsComponent GUID parsed to Empty");
    }

    public static void TrencadisEdgeMatchComponent_GuidParses()
    {
        var component = new TrencadisEdgeMatchComponent();
        var guid = component.ComponentGuid;
        Assert(guid != Guid.Empty, "TrencadisEdgeMatchComponent GUID parsed to Empty");
    }

    public static void EdgeMatchOptionsComponent_GuidParses()
    {
        var component = new EdgeMatchOptionsComponent();
        var guid = component.ComponentGuid;
        Assert(guid != Guid.Empty, "EdgeMatchOptionsComponent GUID parsed to Empty");
    }

    public static void AllFourComponents_HaveUniqueGuids()
    {
        var solveGuid = new EdgeMatchSolveComponent().ComponentGuid;
        var segGuid = new EdgeMatchSegmentsComponent().ComponentGuid;
        var trencGuid = new TrencadisEdgeMatchComponent().ComponentGuid;
        var optsGuid = new EdgeMatchOptionsComponent().ComponentGuid;
        Assert(solveGuid != segGuid, "Solve and Segments share a GUID");
        Assert(solveGuid != trencGuid, "Solve and TrencadisEM share a GUID");
        Assert(solveGuid != optsGuid, "Solve and Options share a GUID");
        Assert(segGuid != trencGuid, "Segments and TrencadisEM share a GUID");
        Assert(segGuid != optsGuid, "Segments and Options share a GUID");
        Assert(trencGuid != optsGuid, "TrencadisEM and Options share a GUID");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
