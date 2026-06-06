#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// OrientedBlock -- one candidate parallelepiped block in the cutting grid.
//
// Geometry: a rectangular box with axes (U, V, W) at world position Center.
// HalfExtents is the half-side along each axis. The 8 corners are at
//     Center +/- HalfExtents.X * U +/- HalfExtents.Y * V +/- HalfExtents.Z * W
//
// Conventions follow BlockCutOpt (Elkarmoty et al. 2020 Resources Policy 68):
//   - W is the vertical world axis (0, 0, 1) and is never rotated.
//   - U, V are horizontal unit vectors, U rotated by psi from world +X around W.
//   - HalfExtents.X, .Y, .Z are half of the kerf-inflated block dimensions
//     dim_block_x, dim_block_y, dim_block_z (i.e. block size + saw kerf).
//
// Reference: Equation 7-1 of the Elkarmoty thesis and Section 6.5 of
// `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md`.
// =============================================================================

/// <summary>
/// One candidate block in a BlockCutOpt cutting grid. Immutable.
///
/// Full 3D rotation is supported via the per-axis triplets (UX, UY, UZ),
/// (VX, VY, VZ), (WX, WY, WZ). The Phase 1 backwards-compatible constructor
/// keeps the original BlockCutOpt 2020 "psi-only" convention by setting
/// UZ = VZ = WX = WY = 0 and WZ = 1 (W = world +Z). The full-3D constructor
/// accepts all three axes explicitly.
///
/// Improvement I1 of `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md`.
/// </summary>
public readonly struct OrientedBlock
{
    /// <summary>
    /// Phase 1-compatible constructor: horizontal U, V (rotated by psi
    /// around world +Z) and implicit W = (0, 0, 1).
    /// </summary>
    public OrientedBlock(
        double centerX, double centerY, double centerZ,
        double uX, double uY,
        double vX, double vY,
        double halfX, double halfY, double halfZ)
        : this(centerX, centerY, centerZ,
               uX, uY, 0.0,
               vX, vY, 0.0,
               0.0, 0.0, 1.0,
               halfX, halfY, halfZ)
    { }

    /// <summary>
    /// Full-3D constructor: explicit (U, V, W) axes. Caller is responsible
    /// for orthogonality and unit length.
    /// </summary>
    public OrientedBlock(
        double centerX, double centerY, double centerZ,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ,
        double wX, double wY, double wZ,
        double halfX, double halfY, double halfZ)
    {
        if (!(halfX > 0.0)) throw new ArgumentOutOfRangeException(nameof(halfX));
        if (!(halfY > 0.0)) throw new ArgumentOutOfRangeException(nameof(halfY));
        if (!(halfZ > 0.0)) throw new ArgumentOutOfRangeException(nameof(halfZ));

        CenterX = centerX; CenterY = centerY; CenterZ = centerZ;
        UX = uX; UY = uY; UZ = uZ;
        VX = vX; VY = vY; VZ = vZ;
        WX = wX; WY = wY; WZ = wZ;
        HalfX = halfX; HalfY = halfY; HalfZ = halfZ;
    }

    public double CenterX { get; }
    public double CenterY { get; }
    public double CenterZ { get; }

    public double UX { get; }
    public double UY { get; }
    /// <summary>Z component of the U axis. Zero for the Phase 1 psi-only grid.</summary>
    public double UZ { get; }

    public double VX { get; }
    public double VY { get; }
    /// <summary>Z component of the V axis. Zero for the Phase 1 psi-only grid.</summary>
    public double VZ { get; }

    /// <summary>X component of the W axis. Zero for the Phase 1 vertical-W grid.</summary>
    public double WX { get; }
    /// <summary>Y component of the W axis. Zero for the Phase 1 vertical-W grid.</summary>
    public double WY { get; }
    /// <summary>Z component of the W axis. 1.0 for the Phase 1 vertical-W grid.</summary>
    public double WZ { get; }

    public double HalfX { get; }
    public double HalfY { get; }
    public double HalfZ { get; }

    /// <summary>
    /// True when this OBB uses the Phase 1 psi-only convention (W = world +Z).
    /// Hot-path consumers can use this to skip the full 3D code.
    /// </summary>
    public bool IsAxisAlignedZ =>
        WX == 0.0 && WY == 0.0 && WZ == 1.0 && UZ == 0.0 && VZ == 0.0;

    /// <summary>
    /// Inner volume (kerf-inflated dimensions, i.e. exactly what the OBB
    /// occupies; the saw kerf is the gap to the adjacent OBB).
    /// </summary>
    public double Volume => 8.0 * HalfX * HalfY * HalfZ;
}
