#nullable disable
using System;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Packing;

/// <summary>
/// Internal record of one placement decision the engine made: which slab
/// went where in the wall frame, plus the AABB it occupies after translation.
/// Internal because the GH layer never needs it; <see cref="AshlarPackResult"/>
/// surfaces the resulting <c>MasonryBlock</c>s instead.
/// </summary>
internal sealed class PlacedBlock
{
    public PlacedBlock(
        string blockId,
        Slab source,
        int courseIndex,
        int slotIndex,
        double originX,
        double originY,
        double originZ,
        double bboxWidth,
        double bboxHeight,
        double bboxDepth,
        bool rotated = false)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            throw new ArgumentException("blockId must be non-blank", nameof(blockId));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (courseIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(courseIndex), "must be >= 0");
        if (slotIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), "must be >= 0");
        if (!(bboxWidth > 0.0))
            throw new ArgumentOutOfRangeException(nameof(bboxWidth), "must be > 0");
        if (!(bboxHeight > 0.0))
            throw new ArgumentOutOfRangeException(nameof(bboxHeight), "must be > 0");
        if (!(bboxDepth > 0.0))
            throw new ArgumentOutOfRangeException(nameof(bboxDepth), "must be > 0");

        BlockId = blockId;
        Source = source;
        CourseIndex = courseIndex;
        SlotIndex = slotIndex;
        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
        BBoxWidth = bboxWidth;
        BBoxHeight = bboxHeight;
        BBoxDepth = bboxDepth;
        Rotated = rotated;
    }

    public string BlockId { get; }
    public Slab Source { get; }
    public int CourseIndex { get; }
    public int SlotIndex { get; }

    public double OriginX { get; }
    public double OriginY { get; }
    public double OriginZ { get; }

    public double BBoxWidth { get; }
    public double BBoxHeight { get; }
    public double BBoxDepth { get; }

    /// <summary>
    /// True when the source slab was yawed 90° around +Z before placement.
    /// BuildBlocks consults this flag to apply the rotation before
    /// translating to the placed origin.
    /// </summary>
    public bool Rotated { get; }

    public double MaxX => OriginX + BBoxWidth;
    public double MaxY => OriginY + BBoxDepth;
    public double MaxZ => OriginZ + BBoxHeight;
}
