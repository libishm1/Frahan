using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Core;

public sealed class PackItem
{
    public PackItem(string id, Size3 size, object? source = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Size = size;
        Source = source;
    }

    public string Id { get; }
    public Size3 Size { get; }
    public object? Source { get; }
    public double Volume => Size.Volume;
}

public sealed class PackContainer
{
    public PackContainer(double width, double depth, double height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Depth = depth;
        Height = height;
    }

    public double Width { get; }
    public double Depth { get; }
    public double Height { get; }
    public double Volume => Width * Depth * Height;
}

public sealed class PackSettings
{
    public double CellSize { get; set; } = 10.0;
    public double Clearance { get; set; } = 0.0;
    public int MaxCandidatesPerItem { get; set; } = 5000;
    public bool AllowYaw90 { get; set; } = true;
    public bool SortByVolumeDescending { get; set; } = true;
    public double CompactnessWeight { get; set; } = 0.001;
    public double HeightWeight { get; set; } = 1.0;
}

public sealed class PackPlacement
{
    public PackPlacement(PackItem item, Box3 box, double yawDegrees, double score, int sequence)
    {
        Item = item;
        Box = box;
        YawDegrees = yawDegrees;
        Score = score;
        Sequence = sequence;
    }

    public PackItem Item { get; }
    public Box3 Box { get; }
    public double YawDegrees { get; }
    public double Score { get; }
    public int Sequence { get; }
}

public sealed class PackFailure
{
    public PackFailure(PackItem item, string reason)
    {
        Item = item;
        Reason = reason;
    }

    public PackItem Item { get; }
    public string Reason { get; }
}

public sealed class PackResult
{
    public PackResult(IReadOnlyList<PackPlacement> placements, IReadOnlyList<PackFailure> failures, Heightmap heightmap, PackContainer container)
    {
        Placements = placements;
        Failures = failures;
        Heightmap = heightmap;
        Container = container;
    }

    public IReadOnlyList<PackPlacement> Placements { get; }
    public IReadOnlyList<PackFailure> Failures { get; }
    public Heightmap Heightmap { get; }
    public PackContainer Container { get; }

    public double PackedVolume => Placements.Sum(p => p.Item.Volume);
    public double FillRatio => Container.Volume <= 0 ? 0 : PackedVolume / Container.Volume;
}
