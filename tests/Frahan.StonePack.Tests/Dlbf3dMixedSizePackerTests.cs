#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Tests;

// =============================================================================
// Dlbf3dMixedSizePackerTests -- 2026-05-15.
//
// Cover the 3D generalisation of I7:
//   - Single-size 3D packing fills a cuboidal bench densely (floor-only).
//   - Variable-height mixed catalogue places tall + short pieces correctly.
//   - Forbidden region (e.g. a fracture column) leaves a hole.
//   - Floor-only mode never stacks; non-floor-only mode stacks at higher Z.
//   - Higher revenue-per-volume piece is preferred when capacity is tight.
// =============================================================================

static class Dlbf3dMixedSizePackerTests
{
    private static void Assert(bool cond, string msg)
    { if (!cond) throw new Exception(msg); }

    public static void Dlbf3D_SingleSize_FloorOnly_FillsFloor()
    {
        // 10 x 6 x 5 bench, piece 2 x 1 x 1, floor-only → 30 pieces on the floor
        var area = new BoundingBox3(0, 0, 0, 10, 6, 5);
        var catalog = new List<PieceSize3D>
        {
            new PieceSize3D("brick", 2.0, 1.0, 1.0, revenue: 5.0),
        };
        var result = Dlbf3dMixedSizePacker.Pack(area, catalog, floorOnly: true);
        Assert(result.Placed.Count == 30,
            $"expected 30 floor pieces, got {result.Placed.Count}");
        foreach (var p in result.Placed)
        {
            Assert(Math.Abs(p.ZMin - 0.0) < 1e-9,
                $"floor-only piece at zMin={p.ZMin}");
        }
    }

    public static void Dlbf3D_SingleSize_Stacked_FillsVolume()
    {
        // Same bench + piece, with stacking enabled → 5 layers = 150 pieces
        var area = new BoundingBox3(0, 0, 0, 10, 6, 5);
        var catalog = new List<PieceSize3D>
        {
            new PieceSize3D("brick", 2.0, 1.0, 1.0, revenue: 5.0),
        };
        var result = Dlbf3dMixedSizePacker.Pack(area, catalog, floorOnly: false);
        Assert(result.Placed.Count == 150,
            $"expected 150 stacked pieces, got {result.Placed.Count}");
    }

    public static void Dlbf3D_HeterogeneousHeights_AllPlaced()
    {
        // bench 4 x 4 x 2; three pieces of different heights, all fit on floor
        var area = new BoundingBox3(0, 0, 0, 4, 4, 2);
        var catalog = new List<PieceSize3D>
        {
            new PieceSize3D("monument", 1.5, 1.0, 1.8, revenue: 20.0),
            new PieceSize3D("dim_stone", 1.0, 1.0, 1.0, revenue: 5.0),
            new PieceSize3D("slab",     1.0, 1.0, 0.3, revenue: 1.0),
        };
        var result = Dlbf3dMixedSizePacker.Pack(area, catalog, cellSize: 0.25, floorOnly: true);
        Assert(result.Placed.Count >= 3,
            $"expected at least 3 placements, got {result.Placed.Count}");
        bool sawMonument = false;
        foreach (var p in result.Placed) if (p.Size.Id == "monument") { sawMonument = true; break; }
        Assert(sawMonument, "monument piece must be among the placements");
    }

    public static void Dlbf3D_PrefersHigherRevenuePerVolume()
    {
        // bench too small to hold both; small piece has higher rev/vol so it wins
        var area = new BoundingBox3(0, 0, 0, 2, 2, 1);
        var catalog = new List<PieceSize3D>
        {
            new PieceSize3D("big",   2.0, 2.0, 1.0, revenue: 3.0),   // V=4, rev/V=0.75
            new PieceSize3D("small", 1.0, 1.0, 1.0, revenue: 1.0),   // V=1, rev/V=1.0
        };
        var result = Dlbf3dMixedSizePacker.Pack(area, catalog, cellSize: 0.5, floorOnly: true);
        int smalls = 0, bigs = 0;
        foreach (var p in result.Placed)
        { if (p.Size.Id == "small") smalls++; else bigs++; }
        Assert(smalls >= 1, $"expected at least 1 small (higher rev/vol), got {smalls}");
    }

    public static void Dlbf3D_ForbiddenColumn_LeavesHole()
    {
        // bench 4 x 4 x 1, forbid a 1x1 column over the entire height.
        // No 1x1x1 piece may overlap it.
        var area = new BoundingBox3(0, 0, 0, 4, 4, 1);
        var catalog = new List<PieceSize3D>
        {
            new PieceSize3D("unit", 1.0, 1.0, 1.0, revenue: 1.0),
        };
        var forbidden = new List<BoundingBox3>
        {
            new BoundingBox3(1, 1, 0, 2, 2, 1),
        };
        var result = Dlbf3dMixedSizePacker.Pack(area, catalog, forbidden, cellSize: 0.25, floorOnly: true);
        // 16 cells minus 1 forbidden = 15 placements
        Assert(result.Placed.Count == 15,
            $"expected 15 placements around forbidden column, got {result.Placed.Count}");
        // none should intersect the forbidden region [1,2] x [1,2]
        foreach (var p in result.Placed)
        {
            bool overlapsX = p.XMax > 1.0 + 1e-9 && p.XMin < 2.0 - 1e-9;
            bool overlapsY = p.YMax > 1.0 + 1e-9 && p.YMin < 2.0 - 1e-9;
            Assert(!(overlapsX && overlapsY),
                $"placement {p.Size.Id} at ({p.XMin},{p.YMin}) overlaps forbidden");
        }
    }

    public static void Dlbf3D_OversizedPiece_StaysUnplaced()
    {
        // piece bigger than the bench → 0 placements (not an exception)
        var area = new BoundingBox3(0, 0, 0, 1, 1, 1);
        var catalog = new List<PieceSize3D>
        {
            new PieceSize3D("toobig", 2.0, 2.0, 2.0, revenue: 100.0),
            new PieceSize3D("fits", 0.5, 0.5, 0.5, revenue: 1.0),
        };
        var result = Dlbf3dMixedSizePacker.Pack(area, catalog, cellSize: 0.25, floorOnly: true);
        int oversized = 0;
        foreach (var p in result.Placed) if (p.Size.Id == "toobig") oversized++;
        Assert(oversized == 0, $"oversized piece must not be placed, got {oversized}");
        Assert(result.Placed.Count > 0, "fits piece should still be placed");
    }
}
