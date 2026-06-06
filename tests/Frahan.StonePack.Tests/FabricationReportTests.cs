#nullable disable
using System;
using Frahan.Core.Fabrication;

namespace Frahan.Tests;

// Pure-managed tests for fabrication weight + lift-class logic (no Rhino).
static class FabricationReportTests
{
    public static void WeightKg_VolumeTimesDensity()
    {
        Assert(Math.Abs(FabricationReport.WeightKg(1.0, 2700.0) - 2700.0) < 1e-6, "1 m^3 granite = 2700 kg");
        Assert(Math.Abs(FabricationReport.WeightKg(0.0, 2700.0)) < 1e-9, "zero volume = 0 kg");
    }

    public static void Classify_Thresholds()
    {
        Assert(FabricationReport.Classify(10) == LiftClass.Hand, "10 kg -> Hand");
        Assert(FabricationReport.Classify(30) == LiftClass.TwoPerson, "30 kg -> TwoPerson");
        Assert(FabricationReport.Classify(500) == LiftClass.Mechanical, "500 kg -> Mechanical");
        Assert(FabricationReport.Classify(5000) == LiftClass.Crane, "5000 kg -> Crane");
    }

    public static void Classify_Boundaries()
    {
        Assert(FabricationReport.Classify(24.999) == LiftClass.Hand, "just under 25 -> Hand");
        Assert(FabricationReport.Classify(25.0) == LiftClass.TwoPerson, "exactly 25 -> TwoPerson");
        Assert(FabricationReport.Classify(50.0) == LiftClass.Mechanical, "exactly 50 -> Mechanical");
        Assert(FabricationReport.Classify(2000.0) == LiftClass.Crane, "exactly 2000 -> Crane");
    }

    public static void Classify_DefaultDensityConstant()
    {
        Assert(Math.Abs(FabricationReport.GraniteDensityKgM3 - 2700.0) < 1e-9, "granite default 2700");
    }

    private static void Assert(bool c, string m) { if (!c) throw new InvalidOperationException("FabricationReport: " + m); }
}
