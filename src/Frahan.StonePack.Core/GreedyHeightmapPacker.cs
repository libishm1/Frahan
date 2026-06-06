using System;
using System.Collections.Generic;
using System.Linq;

namespace Frahan.Core;

public sealed class GreedyHeightmapPacker
{
    public PackResult Pack(IEnumerable<PackItem> inputItems, PackContainer container, PackSettings settings)
    {
        if (inputItems == null) throw new ArgumentNullException(nameof(inputItems));
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (settings.CellSize <= 0) throw new ArgumentOutOfRangeException(nameof(settings.CellSize));

        var items = inputItems.ToList();
        if (settings.SortByVolumeDescending)
        {
            items = items.OrderByDescending(i => i.Volume).ToList();
        }

        var heightmap = new Heightmap(container, settings.CellSize);
        var placements = new List<PackPlacement>();
        var failures = new List<PackFailure>();
        var sequence = 0;

        foreach (var item in items)
        {
            var candidate = FindBestPlacement(item, heightmap, settings);
            if (candidate == null)
            {
                failures.Add(new PackFailure(item, "No containment-safe heightmap candidate fit inside the container."));
                continue;
            }

            var placement = new PackPlacement(item, candidate.Value.Box, candidate.Value.YawDegrees, candidate.Value.Score, sequence++);
            placements.Add(placement);
            heightmap.Add(candidate.Value.Box);
        }

        return new PackResult(placements, failures, heightmap, container);
    }

    private static Candidate? FindBestPlacement(PackItem item, Heightmap heightmap, PackSettings settings)
    {
        Candidate? best = null;
        var evaluated = 0;

        foreach (var orientation in GetOrientations(item.Size, settings.AllowYaw90))
        {
            var maxX = heightmap.WidthCells - heightmap.CellsFor(orientation.Size.Width);
            var maxY = heightmap.DepthCells - heightmap.CellsFor(orientation.Size.Depth);

            for (var x = 0; x <= maxX; x++)
            {
                for (var y = 0; y <= maxY; y++)
                {
                    if (++evaluated > settings.MaxCandidatesPerItem && best != null)
                    {
                        return best;
                    }

                    if (!heightmap.TryGetLowestZ(orientation.Size, x, y, out var z))
                    {
                        continue;
                    }

                    var min = new Vec3(x * heightmap.CellSize + settings.Clearance, y * heightmap.CellSize + settings.Clearance, z);
                    var score = heightmap.ScorePlacement(orientation.Size, x, y, z, settings);
                    var box = new Box3(min, orientation.Size);
                    var candidate = new Candidate(box, orientation.YawDegrees, score);

                    if (best == null || candidate.Score < best.Value.Score)
                    {
                        best = candidate;
                    }
                }
            }
        }

        return best;
    }

    private static IEnumerable<Orientation> GetOrientations(Size3 original, bool allowYaw90)
    {
        yield return new Orientation(original, 0);
        if (allowYaw90 && Math.Abs(original.Width - original.Depth) > 1e-9)
        {
            yield return new Orientation(original.RotatedYaw90(), 90);
        }
    }

    private readonly struct Candidate
    {
        public Candidate(Box3 box, double yawDegrees, double score)
        {
            Box = box;
            YawDegrees = yawDegrees;
            Score = score;
        }

        public Box3 Box { get; }
        public double YawDegrees { get; }
        public double Score { get; }
    }

    private readonly struct Orientation
    {
        public Orientation(Size3 size, double yawDegrees)
        {
            Size = size;
            YawDegrees = yawDegrees;
        }

        public Size3 Size { get; }
        public double YawDegrees { get; }
    }
}
