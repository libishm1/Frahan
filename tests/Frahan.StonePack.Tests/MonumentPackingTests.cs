#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.GeoPack;
using Frahan.Masonry.Quarry.Monuments;

namespace Frahan.Tests;

// =============================================================================
// MonumentPackingTests -- 2026-05-15.
//
// Covers:
//   - MonumentOrientationSampler emits 24 distinct axis-aligned rotations.
//   - Each rotation is a proper rotation (determinant +1, orthogonal).
//   - BenchMonumentPacker places at least one monument in a single-cell bench.
//   - PackBlockGraph respects fractures (a monument larger than any cell stays
//     unplaced).
// =============================================================================

static class MonumentPackingTests
{
    private static PlyMesh BoxMesh(double sx, double sy, double sz)
    {
        // axis-aligned box at origin, 12 triangles (6 quads fan-split)
        var v = new List<double>
        {
            0, 0, 0,
            sx, 0, 0,
            sx, sy, 0,
            0, sy, 0,
            0, 0, sz,
            sx, 0, sz,
            sx, sy, sz,
            0, sy, sz,
        };
        var t = new List<int>
        {
            0,2,1, 0,3,2,   // -Z
            4,5,6, 4,6,7,   // +Z
            0,1,5, 0,5,4,   // -Y
            1,2,6, 1,6,5,   // +X
            2,3,7, 2,7,6,   // +Y
            3,0,4, 3,4,7,   // -X
        };
        return new PlyMesh(v, t, null);
    }

    public static void Sampler_Has24Rotations()
    {
        var n = MonumentOrientationSampler.Count;
        if (n != 24) throw new Exception($"expected 24 rotations, got {n}");
    }

    public static void Sampler_AllRotationsAreProper()
    {
        for (int r = 0; r < MonumentOrientationSampler.Count; r++)
        {
            var R = MonumentOrientationSampler.Get(r);
            // determinant of a 3x3 integer matrix; must be +1 for proper rotation
            int det =
                  R[0] * (R[4] * R[8] - R[5] * R[7])
                - R[1] * (R[3] * R[8] - R[5] * R[6])
                + R[2] * (R[3] * R[7] - R[4] * R[6]);
            if (det != 1) throw new Exception($"rotation #{r} has det={det}, expected 1");
        }
    }

    public static void Sampler_RotatedAabbOfCubeIsCube()
    {
        // a unit cube should remain a unit cube under every rotation
        for (int r = 0; r < MonumentOrientationSampler.Count; r++)
        {
            MonumentOrientationSampler.RotatedAabb(
                r, 0, 0, 0, 1, 1, 1,
                out double dx, out double dy, out double dz);
            if (Math.Abs(dx - 1) > 1e-9 || Math.Abs(dy - 1) > 1e-9 || Math.Abs(dz - 1) > 1e-9)
                throw new Exception($"rotation #{r} of unit cube: ({dx}, {dy}, {dz})");
        }
    }

    public static void Sampler_RotatedAabbOfElongatedBoxSwapsAxes()
    {
        // a 1x2x3 box: across the 24 rotations, the rotated AABB must take one
        // of the 6 permutations (1,2,3), (1,3,2), (2,1,3), (2,3,1), (3,1,2), (3,2,1).
        var seen = new HashSet<(double, double, double)>();
        for (int r = 0; r < MonumentOrientationSampler.Count; r++)
        {
            MonumentOrientationSampler.RotatedAabb(
                r, 0, 0, 0, 1, 2, 3,
                out double dx, out double dy, out double dz);
            seen.Add((Math.Round(dx, 6), Math.Round(dy, 6), Math.Round(dz, 6)));
        }
        if (seen.Count != 6)
            throw new Exception($"expected 6 distinct rotated-AABB shapes, got {seen.Count}");
    }

    public static void BenchMonumentPacker_OneCellOneFittingMonument_Placed()
    {
        // bench = 2 x 2 x 1 axis-aligned box, no fractures → BlockGraph with one cell
        var bench = Slab.Box(0, 0, 0, 2, 2, 1);
        var graph = BlockGraphBuilder.Partition(bench, CrackGraphBuilder.FromPlanes(new List<FracturePlane>()));
        var monuments = new List<Monument>
        {
            new Monument("MON-A", BoxMesh(1.0, 1.0, 0.5)),
        };
        var inv = new MonumentInventory(monuments);

        var plan = BenchMonumentPacker.PackBlockGraph(graph, inv,
            new BenchMonumentPackerOptions(gridStride: 0.25));

        if (plan.PlacedCount != 1)
            throw new Exception($"expected 1 placement, got {plan.PlacedCount}");
        if (plan.UnplacedCount != 0)
            throw new Exception($"expected 0 unplaced, got {plan.UnplacedCount}");
        var p = plan.Placements[0];
        if (p.MonumentId != "MON-A")
            throw new Exception($"placed wrong monument: {p.MonumentId}");
        if (!(p.OriginX >= 0 && p.MaxX <= 2 + 1e-9))
            throw new Exception($"X outside cell: {p.OriginX}..{p.MaxX}");
    }

    public static void BenchMonumentPacker_MonumentTooLarge_Unplaced()
    {
        // bench = 1 x 1 x 1, one too-big monument 1.5 x 1.5 x 0.5
        var bench = Slab.Box(0, 0, 0, 1, 1, 1);
        var graph = BlockGraphBuilder.Partition(bench, CrackGraphBuilder.FromPlanes(new List<FracturePlane>()));
        var inv = new MonumentInventory(new List<Monument>
        {
            new Monument("BIG", BoxMesh(1.5, 1.5, 0.5)),
        });
        var plan = BenchMonumentPacker.PackBlockGraph(graph, inv,
            new BenchMonumentPackerOptions(gridStride: 0.25));
        if (plan.PlacedCount != 0)
            throw new Exception($"expected 0 placements, got {plan.PlacedCount}");
        if (plan.UnplacedCount != 1)
            throw new Exception($"expected 1 unplaced, got {plan.UnplacedCount}");
        if (plan.UnplacedMonumentIds[0] != "BIG")
            throw new Exception($"unplaced id mismatch: {plan.UnplacedMonumentIds[0]}");
    }

    public static void BenchMonumentPacker_FractureSplitsBench_MonumentInOneCellOnly()
    {
        // bench = 3 x 1 x 1 split by a fracture at x=1.5 (yz-plane).
        // Two cells of 1.5 x 1 x 1 each.
        // A monument of dims 1.0 x 1 x 1 fits in either cell (not both).
        // A monument of dims 2.0 x 1 x 1 fits in neither.
        var bench = Slab.Box(0, 0, 0, 3, 1, 1);
        var fracture = new FracturePlane(1.5, 0.5, 0.5, 1, 0, 0);
        var graph = BlockGraphBuilder.Partition(bench, CrackGraphBuilder.FromPlanes(new[] { fracture }));
        if (graph.Count != 2)
            throw new Exception($"expected 2 cells from one fracture, got {graph.Count}");

        var inv = new MonumentInventory(new List<Monument>
        {
            new Monument("FITS",  BoxMesh(1.0, 1.0, 1.0)),
            new Monument("STRADDLES", BoxMesh(2.0, 1.0, 1.0)),
        });

        var plan = BenchMonumentPacker.PackBlockGraph(graph, inv,
            new BenchMonumentPackerOptions(gridStride: 0.5));

        if (plan.PlacedCount != 1)
            throw new Exception($"expected 1 placement (FITS only), got {plan.PlacedCount}");
        if (plan.UnplacedCount != 1 || plan.UnplacedMonumentIds[0] != "STRADDLES")
            throw new Exception("expected STRADDLES unplaced");
        // FITS must land entirely on one side of the fracture
        var p = plan.Placements[0];
        bool leftSide = p.MaxX <= 1.5 + 1e-9;
        bool rightSide = p.OriginX >= 1.5 - 1e-9;
        if (!leftSide && !rightSide)
            throw new Exception($"FITS spans the fracture: x in [{p.OriginX}, {p.MaxX}]");
    }
}
