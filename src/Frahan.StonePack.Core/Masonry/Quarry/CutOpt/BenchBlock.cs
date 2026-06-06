#nullable disable
using System;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// BenchBlock -- one candidate quarry block on a bench. Layer 7 data unit.
//
// Spec: wiki/specs/10_frahan_quarrycutopt_spec.md section 5.
//
// A BenchBlock is the upstream peer of GeoCut / BlockCutOpt: GeoCut answers
// "given a block, what is the best cut plan?"; the QuarryCutOpt layer asks
// "given a bench full of these, what extraction order maximises total yield?".
//
// Geometry is stored as an axis-aligned BoundingBox3 (in metres). Oriented
// bench geometry is left to the GeoCut psi search; the bench-scale layer only
// needs the gross volume + a footprint.
// =============================================================================

public sealed class BenchBlock
{
    public BenchBlock(
        string id,
        BoundingBox3 footprint,
        double geologyGrade = 1.0,
        double accessCost = 0.0)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
        if (footprint == null) throw new ArgumentNullException(nameof(footprint));
        if (geologyGrade < 0.0 || geologyGrade > 1.0)
            throw new ArgumentOutOfRangeException(nameof(geologyGrade), "0..1");
        if (accessCost < 0.0)
            throw new ArgumentOutOfRangeException(nameof(accessCost), ">= 0");

        Id = id;
        Footprint = footprint;
        GeologyGrade = geologyGrade;
        AccessCost = accessCost;
    }

    public string Id { get; }
    public BoundingBox3 Footprint { get; }

    /// <summary>0..1 multiplier on recoverable volume (1.0 = perfect grade).</summary>
    public double GeologyGrade { get; }

    /// <summary>Per-block access cost in arbitrary units (haul distance, ramp prep, etc.).</summary>
    public double AccessCost { get; }

    public double GrossVolume => Footprint.SizeX * Footprint.SizeY * Footprint.SizeZ;

    public override string ToString() =>
        $"BenchBlock({Id}, vol={GrossVolume:0.###} m3, grade={GeologyGrade:0.00}, access={AccessCost:0.##})";
}
