#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry;

// =============================================================================
// QuarryDecomposer — pipeline wrapper from a single convex quarry block plus
// DFN parameters to a list of cut-down slab fragments. Built on top of the
// existing SlabCutter (Phase E.1) and FracturePlaneGenerators (Phase E.3).
//
// Stage E.3 lite: assumes the input slab is already convex. A multi-shell or
// non-convex quarry mesh must be pre-processed (e.g. one Slab per shell)
// before reaching this stage; full V-HACD-style convex decomposition is the
// follow-up tracked as P2 in HANDOFF_TO_CLAUDE.md and is not in scope here.
// =============================================================================

/// <summary>
/// Static helpers that combine fracture-plane generation with slab cutting.
/// </summary>
public static class QuarryDecomposer
{
    /// <summary>
    /// Cuts <paramref name="quarry"/> by an orthogonal grid of fractures.
    /// </summary>
    public static SlabCutResult DecomposeByGrid(
        Slab quarry, int nX, int nY, int nZ, double eps = 1e-9)
    {
        if (quarry == null) throw new ArgumentNullException(nameof(quarry));
        if (nX < 0) throw new ArgumentOutOfRangeException(nameof(nX));
        if (nY < 0) throw new ArgumentOutOfRangeException(nameof(nY));
        if (nZ < 0) throw new ArgumentOutOfRangeException(nameof(nZ));

        var box = BoundingBox3.FromSlab(quarry);
        var planes = FracturePlaneGenerators.Grid(box, nX, nY, nZ);
        return SlabCutter.Cut(quarry, planes, eps);
    }

    /// <summary>
    /// Cuts <paramref name="quarry"/> by a jittered orthogonal grid.
    /// </summary>
    public static SlabCutResult DecomposeByJitteredGrid(
        Slab quarry, int nX, int nY, int nZ, double jitter, int seed, double eps = 1e-9)
    {
        if (quarry == null) throw new ArgumentNullException(nameof(quarry));
        var box = BoundingBox3.FromSlab(quarry);
        var planes = FracturePlaneGenerators.JitteredGrid(box, nX, nY, nZ, jitter, seed);
        return SlabCutter.Cut(quarry, planes, eps);
    }

    /// <summary>
    /// Cuts <paramref name="quarry"/> by a uniformly-random plane set.
    /// </summary>
    public static SlabCutResult DecomposeByRandom(
        Slab quarry, int count, int seed, double eps = 1e-9)
    {
        if (quarry == null) throw new ArgumentNullException(nameof(quarry));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var box = BoundingBox3.FromSlab(quarry);
        var planes = FracturePlaneGenerators.Random(box, count, seed);
        return SlabCutter.Cut(quarry, planes, eps);
    }

    /// <summary>
    /// Cuts <paramref name="quarry"/> by Voronoi bisector planes seeded
    /// from <paramref name="seeds"/> (flat [x,y,z,...] coords). The
    /// resulting cells approximate Voronoi cells of the seed set.
    /// </summary>
    public static SlabCutResult DecomposeByVoronoi(
        Slab quarry, IReadOnlyList<double> seeds, double eps = 1e-9)
    {
        if (quarry == null) throw new ArgumentNullException(nameof(quarry));
        if (seeds == null) throw new ArgumentNullException(nameof(seeds));
        var planes = FracturePlaneGenerators.VoronoiBisectors(seeds);
        return SlabCutter.Cut(quarry, planes, eps);
    }
}
