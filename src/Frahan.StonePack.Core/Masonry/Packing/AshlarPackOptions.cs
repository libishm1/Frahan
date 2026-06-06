#nullable disable
using System;

namespace Frahan.Masonry.Packing;

/// <summary>
/// Immutable set of inputs to <see cref="AshlarLayoutEngine.Pack"/>. All
/// dimensions positive; StaggerOffset in [0, 1].
/// </summary>
public sealed class AshlarPackOptions
{
    public AshlarPackOptions(
        CourseMode mode,
        double wallWidth,
        double wallHeight,
        double wallThickness,
        double targetCourseHeight,
        double bedJoint,
        double headJoint,
        double staggerOffset,
        double density,
        double heightTolerance,
        bool allowYaw = false,
        bool allowTrim = false)
    {
        if (!(wallWidth > 0.0))
            throw new ArgumentOutOfRangeException(nameof(wallWidth), "must be > 0");
        if (!(wallHeight > 0.0))
            throw new ArgumentOutOfRangeException(nameof(wallHeight), "must be > 0");
        if (!(wallThickness > 0.0))
            throw new ArgumentOutOfRangeException(nameof(wallThickness), "must be > 0");
        if (!(targetCourseHeight > 0.0))
            throw new ArgumentOutOfRangeException(nameof(targetCourseHeight), "must be > 0");
        if (bedJoint < 0.0)
            throw new ArgumentOutOfRangeException(nameof(bedJoint), "must be >= 0");
        if (headJoint < 0.0)
            throw new ArgumentOutOfRangeException(nameof(headJoint), "must be >= 0");
        if (!(staggerOffset >= 0.0 && staggerOffset <= 1.0))
            throw new ArgumentOutOfRangeException(nameof(staggerOffset), "must be in [0, 1]");
        if (!(density > 0.0))
            throw new ArgumentOutOfRangeException(nameof(density), "must be > 0");
        if (heightTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(heightTolerance), "must be >= 0");

        Mode = mode;
        WallWidth = wallWidth;
        WallHeight = wallHeight;
        WallThickness = wallThickness;
        TargetCourseHeight = targetCourseHeight;
        BedJoint = bedJoint;
        HeadJoint = headJoint;
        StaggerOffset = staggerOffset;
        Density = density;
        HeightTolerance = heightTolerance;
        AllowYaw = allowYaw;
        AllowTrim = allowTrim;
    }

    public CourseMode Mode { get; }
    public double WallWidth { get; }
    public double WallHeight { get; }
    public double WallThickness { get; }
    public double TargetCourseHeight { get; }
    public double BedJoint { get; }
    public double HeadJoint { get; }
    public double StaggerOffset { get; }
    public double Density { get; }
    public double HeightTolerance { get; }

    /// <summary>
    /// When true, the layout engine may yaw a slab 90° around +Z to make it
    /// fit a course slot whose remaining width is too narrow for the slab's
    /// natural width. Default false (translation-only).
    /// </summary>
    public bool AllowYaw { get; }

    /// <summary>
    /// When true, the layout engine may cut an oversized slab with
    /// SlabCutter to produce a piece of the exact remaining width, place
    /// the piece, and return the offcut to the inventory pool. Default
    /// false (gaps are recorded as Notes instead).
    /// </summary>
    public bool AllowTrim { get; }

    public override string ToString() =>
        $"AshlarPackOptions(mode={Mode}, wall={WallWidth:0.###}x{WallHeight:0.###}x{WallThickness:0.###}, " +
        $"course={TargetCourseHeight:0.###}, head={HeadJoint:0.###}, bed={BedJoint:0.###})";
}
