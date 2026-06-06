#nullable disable
using System;

namespace Frahan.Masonry.Cutting;

/// <summary>
/// Tuning for <see cref="FractureCutter"/>. All fields default to
/// conservative values that match Phase E.2 behaviour.
/// </summary>
public sealed class FractureCutOptions
{
    public FractureCutOptions(
        bool extendPartialToInfinitePlane = false,
        double epsilon = 1e-9,
        double containmentTolerance = 1e-7)
    {
        if (epsilon < 0.0)
            throw new ArgumentOutOfRangeException(nameof(epsilon), "must be >= 0");
        if (containmentTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(containmentTolerance), "must be >= 0");

        ExtendPartialToInfinitePlane = extendPartialToInfinitePlane;
        Epsilon = epsilon;
        ContainmentTolerance = containmentTolerance;
    }

    /// <summary>
    /// When true, a fracture polygon that only partially covers the slab
    /// cross-section is treated as if it were an infinite plane (so the slab
    /// is split fully). The reported outcome is
    /// <see cref="FractureCutOutcome.PartialExtended"/> in that case so the
    /// caller can distinguish from a true <see cref="FractureCutOutcome.Spans"/>.
    /// Default: false (partial fractures pass through untouched).
    /// </summary>
    public bool ExtendPartialToInfinitePlane { get; }

    /// <summary>Vertex classification epsilon for the underlying SlabCutter.</summary>
    public double Epsilon { get; }

    /// <summary>
    /// Tolerance for the 2D point-in-polygon containment test that decides
    /// Spans vs Partial. Larger values are more lenient (more "Spans"
    /// outcomes); set to a few times the machine-precision cross-section
    /// vertex error to handle near-boundary cases.
    /// </summary>
    public double ContainmentTolerance { get; }

    public static FractureCutOptions Default { get; } = new FractureCutOptions();

    public override string ToString() =>
        $"FractureCutOptions(extend={ExtendPartialToInfinitePlane}, " +
        $"eps={Epsilon:0.###e+00}, contain={ContainmentTolerance:0.###e+00})";
}
