#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockValueModel -- maps one non-intersected OBB to its revenue and block
// value. The defaults match Jalalian 2023 / SlabCutOpt revenue convention; the
// caller can override with a domain-specific model.
//
// Phase 6 of the synthesis roadmap.
// =============================================================================

public sealed class BlockValueModel
{
    /// <summary>
    /// Relative money value of one full block (Elkarmoty thesis Eq. 6-2 RMV).
    /// Default 1.0 means "max class price" since the abstraction is currency-free.
    /// </summary>
    public double RmvPerBlock { get; }

    /// <summary>
    /// Block value (Jalalian 2023) used as the BCSdbBV denominator. Default
    /// = block volume in m^3; a richer implementation would multiply by a
    /// class-A / B / C factor.
    /// </summary>
    public double BvPerBlock { get; }

    /// <summary>
    /// Saw kerf time per block (minutes per cube). Default approximates a
    /// 5 m^3 block at 50 mm/min feed: ~24 min.
    /// </summary>
    public double KerfTimeMinPerBlock { get; }

    public BlockValueModel(
        double rmvPerBlock = 1.0,
        double bvPerBlock = 1.0,
        double kerfTimeMinPerBlock = 24.0)
    {
        if (!(rmvPerBlock >= 0)) throw new ArgumentOutOfRangeException(nameof(rmvPerBlock));
        if (!(bvPerBlock > 0)) throw new ArgumentOutOfRangeException(nameof(bvPerBlock));
        if (!(kerfTimeMinPerBlock >= 0)) throw new ArgumentOutOfRangeException(nameof(kerfTimeMinPerBlock));

        RmvPerBlock = rmvPerBlock;
        BvPerBlock = bvPerBlock;
        KerfTimeMinPerBlock = kerfTimeMinPerBlock;
    }

    public static BlockValueModel Default { get; } = new BlockValueModel();

    /// <summary>
    /// Surface area of one OBB (m^2). Used to accumulate the BCSdbBV numerator.
    /// </summary>
    public static double SurfaceArea(in OrientedBlock obb)
    {
        double Lx = 2 * obb.HalfX, Ly = 2 * obb.HalfY, Lz = 2 * obb.HalfZ;
        return 2.0 * (Lx * Ly + Ly * Lz + Lx * Lz);
    }
}
