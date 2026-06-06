#nullable disable
using System;

namespace Frahan.Core.Fabrication;

// =============================================================================
// FabricationReport — turn block geometry into shop-floor handling facts:
// weight (volume x density), and a lift class so the crate / hoist / crane plan
// follows from the cut. Closes part of the "fabrication-prep" market gap. Pure-
// managed; the GH layer supplies volumes from RhinoCommon VolumeMassProperties.
// =============================================================================

public enum LiftClass
{
    /// <summary>&lt; 25 kg — one person.</summary>
    Hand,
    /// <summary>&lt; 50 kg — two people.</summary>
    TwoPerson,
    /// <summary>&lt; 2000 kg — forklift / hoist / gantry.</summary>
    Mechanical,
    /// <summary>&gt;= 2000 kg — crane.</summary>
    Crane,
}

public static class FabricationReport
{
    /// <summary>Typical granite bulk density (kg/m^3); a sensible default.</summary>
    public const double GraniteDensityKgM3 = 2700.0;

    public static double WeightKg(double volumeCubicMetres, double densityKgM3)
        => volumeCubicMetres * densityKgM3;

    /// <summary>Lift class by piece weight in kilograms.</summary>
    public static LiftClass Classify(double weightKg)
    {
        if (weightKg < 25.0) return LiftClass.Hand;
        if (weightKg < 50.0) return LiftClass.TwoPerson;
        if (weightKg < 2000.0) return LiftClass.Mechanical;
        return LiftClass.Crane;
    }
}
