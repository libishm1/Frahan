#nullable disable
using System;

namespace Frahan.Masonry.Packing;

/// <summary>
/// Pure-data wall envelope (width, height, thickness). Used by the Stage 2
/// Wall Frame GH component to feed multiple Ashlar Pack components without
/// re-wiring three numeric inputs each time.
/// </summary>
public sealed class WallFrame
{
    public WallFrame(double wallWidth, double wallHeight, double wallThickness)
    {
        if (!(wallWidth > 0.0))
            throw new ArgumentOutOfRangeException(nameof(wallWidth), "must be > 0");
        if (!(wallHeight > 0.0))
            throw new ArgumentOutOfRangeException(nameof(wallHeight), "must be > 0");
        if (!(wallThickness > 0.0))
            throw new ArgumentOutOfRangeException(nameof(wallThickness), "must be > 0");

        WallWidth = wallWidth;
        WallHeight = wallHeight;
        WallThickness = wallThickness;
    }

    public double WallWidth { get; }
    public double WallHeight { get; }
    public double WallThickness { get; }

    public override string ToString() =>
        $"WallFrame({WallWidth:0.###} x {WallHeight:0.###} x {WallThickness:0.###})";
}
