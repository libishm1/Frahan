#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// ScaleSpec -- one scale of the multi-scale recovery cascade (RecoveryCascade).
// Ordered coarse -> fine: scale 0 is the dimension-block scale, scale 1 the slab
// scale, scale 2 the tile scale, etc. Each scale carries its own block size +
// search options, the minimum marketable volume below which a cracked region is
// scrapped, and a value model for the downgrade ladder (tier price decreases
// with depth). See RecoveryCascade for the recursion.
// =============================================================================

/// <summary>One coarse-to-fine scale of the recovery cascade. Immutable.</summary>
public sealed class ScaleSpec
{
    public ScaleSpec(
        BlockCutOptOptions options,
        double minMarketableVolumeM3,
        BlockValueModel value = null,
        string label = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (!(minMarketableVolumeM3 >= 0.0))
            throw new ArgumentOutOfRangeException(nameof(minMarketableVolumeM3));
        MinMarketableVolumeM3 = minMarketableVolumeM3;
        Value = value ?? BlockValueModel.Default;
        Label = string.IsNullOrEmpty(label) ? "scale" : label;
    }

    /// <summary>Block size + (psi, dx, dy) search options at this scale.</summary>
    public BlockCutOptOptions Options { get; }

    /// <summary>
    /// A cracked region is routed to this scale only if its volume is at least
    /// this; below it the region becomes residual waste (CSUL usable-leftover
    /// threshold, Cherri et al. 2009).
    /// </summary>
    public double MinMarketableVolumeM3 { get; }

    /// <summary>Value model (RMV / block value / kerf time) at this tier.</summary>
    public BlockValueModel Value { get; }

    /// <summary>Human label for the tier (block / slab / tile).</summary>
    public string Label { get; }

    /// <summary>Marketable (inner) volume of one block at this scale (m^3).</summary>
    public double BlockVolume => Options.BlockSizeX * Options.BlockSizeY * Options.BlockSizeZ;

    /// <summary>Kerf-inflated OBB volume of one block at this scale (m^3).</summary>
    public double InflatedVolume =>
        (Options.BlockSizeX + Options.Kerf) * (Options.BlockSizeY + Options.Kerf) * (Options.BlockSizeZ + Options.Kerf);
}
