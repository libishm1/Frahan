#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Quarry.GeoPack;

namespace Frahan.Masonry.Quarry.Monuments;

// =============================================================================
// MonumentPlacement -- one accepted placement of a Monument into a BlockCell.
//
// Carries the rotation index (0..23) into MonumentOrientationSampler, the
// world-space min corner of the rotated AABB, and the parent cell id. The
// rotated AABB extents (Dx, Dy, Dz) are stored so consumers don't have to
// re-rotate.
// =============================================================================

public sealed class MonumentPlacement
{
    public MonumentPlacement(
        string monumentId,
        string cellId,
        int orientationIndex,
        double originX, double originY, double originZ,
        double dx, double dy, double dz)
    {
        if (string.IsNullOrWhiteSpace(monumentId)) throw new ArgumentException("monumentId required", nameof(monumentId));
        if (string.IsNullOrWhiteSpace(cellId)) throw new ArgumentException("cellId required", nameof(cellId));
        if (orientationIndex < 0 || orientationIndex >= MonumentOrientationSampler.Count)
            throw new ArgumentOutOfRangeException(nameof(orientationIndex));
        if (!(dx > 0 && dy > 0 && dz > 0))
            throw new ArgumentOutOfRangeException(nameof(dx), $"all extents must be > 0, got ({dx}, {dy}, {dz})");

        MonumentId = monumentId;
        CellId = cellId;
        OrientationIndex = orientationIndex;
        OriginX = originX; OriginY = originY; OriginZ = originZ;
        Dx = dx; Dy = dy; Dz = dz;
    }

    public string MonumentId { get; }
    public string CellId { get; }
    public int OrientationIndex { get; }
    public double OriginX { get; } public double OriginY { get; } public double OriginZ { get; }
    public double Dx { get; } public double Dy { get; } public double Dz { get; }

    public double MaxX => OriginX + Dx;
    public double MaxY => OriginY + Dy;
    public double MaxZ => OriginZ + Dz;
    public double Volume => Dx * Dy * Dz;

    public override string ToString() =>
        $"MonumentPlacement({MonumentId} in {CellId} @ ({OriginX:0.###},{OriginY:0.###},{OriginZ:0.###}) dims {Dx:0.###}x{Dy:0.###}x{Dz:0.###} rot#{OrientationIndex})";
}

public sealed class BenchMonumentPlan
{
    public BenchMonumentPlan(
        IReadOnlyList<MonumentPlacement> placements,
        IReadOnlyList<string> unplacedMonumentIds,
        double benchAabbVolume)
    {
        Placements = placements ?? throw new ArgumentNullException(nameof(placements));
        UnplacedMonumentIds = unplacedMonumentIds ?? throw new ArgumentNullException(nameof(unplacedMonumentIds));
        if (benchAabbVolume < 0) throw new ArgumentOutOfRangeException(nameof(benchAabbVolume));
        BenchAabbVolume = benchAabbVolume;
    }

    public IReadOnlyList<MonumentPlacement> Placements { get; }
    public IReadOnlyList<string> UnplacedMonumentIds { get; }
    public double BenchAabbVolume { get; }

    public int PlacedCount => Placements.Count;
    public int UnplacedCount => UnplacedMonumentIds.Count;

    public double TotalPlacedVolume
    {
        get
        {
            double t = 0.0;
            for (int i = 0; i < Placements.Count; i++) t += Placements[i].Volume;
            return t;
        }
    }

    public double FillRatio =>
        BenchAabbVolume > 0 ? TotalPlacedVolume / BenchAabbVolume : 0.0;

    public override string ToString() =>
        $"BenchMonumentPlan(placed={PlacedCount}, unplaced={UnplacedCount}, fill={FillRatio:0.00})";
}
