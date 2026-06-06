#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Quarry.BlockCutOpt;

namespace Frahan.Masonry.Quarry.CutOpt;

// =============================================================================
// BenchBlockSlabBuilder -- the Layer 7 → Layer 5 bridge.
//
// Given:
//   - one BenchBlock (footprint AABB + grade), and
//   - the fracture mesh (BlockCutOpt input format), and
//   - per-block BlockCutOpt search options,
// produce:
//   - the optimal OrientedBlock grid for that BenchBlock (via
//     BlockCutOptSolver.SolveAndExtract), and
//   - a corresponding List<Slab> ready for the Ashlar packer.
//
// The OrientedBlock → Slab conversion fan-triangulates each face by
// emitting six quadrilateral faces of the kerf-inflated box. The resulting
// Slab is convex by construction and ready for ToMasonryBlock() inside
// AshlarLayoutEngine.
//
// Spec: outputs/2026-05-14/connection_map/FRAHAN_PIPELINE_MAP.md section 8.1.
// =============================================================================

public sealed class BenchBlockCutResult
{
    public BenchBlockCutResult(
        string blockId,
        BlockCutOptResult bcoResult,
        IReadOnlyList<OrientedBlock> grid,
        IReadOnlyList<Slab> slabs)
    {
        if (string.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("blockId required", nameof(blockId));
        BlockId = blockId;
        BcoResult = bcoResult ?? throw new ArgumentNullException(nameof(bcoResult));
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Slabs = slabs ?? throw new ArgumentNullException(nameof(slabs));
    }

    public string BlockId { get; }
    public BlockCutOptResult BcoResult { get; }
    public IReadOnlyList<OrientedBlock> Grid { get; }
    public IReadOnlyList<Slab> Slabs { get; }

    public int SlabCount => Slabs.Count;

    public override string ToString() =>
        $"BenchBlockCutResult({BlockId}: {SlabCount} slabs, R={BcoResult.RecoveryPercent:0.0}%)";
}

public static class BenchBlockSlabBuilder
{
    /// <summary>
    /// Cut one BenchBlock with the optimal grid found by BlockCutOpt.
    /// </summary>
    public static BenchBlockCutResult CutOne(
        BenchBlock block,
        PlyMesh fractures,
        BlockCutOptOptions options)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var (bco, grid) = BlockCutOptSolver.SolveAndExtract(block.Footprint, fractures, options);
        var slabs = new List<Slab>(grid.Count);
        for (int i = 0; i < grid.Count; i++)
        {
            slabs.Add(OrientedBlockToSlab(grid[i]));
        }
        return new BenchBlockCutResult(block.Id, bco, grid, slabs);
    }

    /// <summary>
    /// Run CutOne over every BenchBlock in the plan, in extraction order.
    /// Returns one result per accepted block. Skipped (low-yield) blocks are
    /// not cut.
    /// </summary>
    public static IReadOnlyList<BenchBlockCutResult> CutPlan(
        ExtractionPlan plan,
        QuarryInventory inventory,
        PlyMesh fractures,
        BlockCutOptOptions options)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var byId = new Dictionary<string, BenchBlock>(inventory.Count, StringComparer.Ordinal);
        foreach (var b in inventory.Blocks) byId[b.Id] = b;

        var output = new List<BenchBlockCutResult>(plan.Accepted.Count);
        foreach (var entry in plan.Accepted)
        {
            if (!byId.TryGetValue(entry.Block.Id, out var block))
                throw new InvalidOperationException(
                    $"plan references block id '{entry.Block.Id}' missing from inventory");
            output.Add(CutOne(block, fractures, options));
        }
        return output;
    }

    /// <summary>
    /// Build a Slab (convex polyhedron) from the 8 corners of one OrientedBlock.
    /// </summary>
    public static Slab OrientedBlockToSlab(OrientedBlock obb)
    {
        // 8 corners: center +- HalfX*U +- HalfY*V +- HalfZ*W
        double cx = obb.CenterX, cy = obb.CenterY, cz = obb.CenterZ;
        double hx = obb.HalfX, hy = obb.HalfY, hz = obb.HalfZ;
        double ux = obb.UX, uy = obb.UY, uz = obb.UZ;
        double vx = obb.VX, vy = obb.VY, vz = obb.VZ;
        double wx = obb.WX, wy = obb.WY, wz = obb.WZ;

        // signs s for (U, V, W): bit 0 = U, bit 1 = V, bit 2 = W.
        var verts = new double[24];
        for (int s = 0; s < 8; s++)
        {
            double su = ((s & 1) == 0) ? -hx : hx;
            double sv = ((s & 2) == 0) ? -hy : hy;
            double sw = ((s & 4) == 0) ? -hz : hz;
            double px = cx + su * ux + sv * vx + sw * wx;
            double py = cy + su * uy + sv * vy + sw * wy;
            double pz = cz + su * uz + sv * vz + sw * wz;
            verts[3 * s + 0] = px;
            verts[3 * s + 1] = py;
            verts[3 * s + 2] = pz;
        }

        // vertex ids by sign bits (U, V, W):
        //  0: (-,-,-)  1: (+,-,-)  2: (-,+,-)  3: (+,+,-)
        //  4: (-,-,+)  5: (+,-,+)  6: (-,+,+)  7: (+,+,+)
        var faces = new IReadOnlyList<int>[]
        {
            new[] { 0, 2, 3, 1 }, // -W
            new[] { 4, 5, 7, 6 }, // +W
            new[] { 0, 1, 5, 4 }, // -V
            new[] { 2, 6, 7, 3 }, // +V
            new[] { 0, 4, 6, 2 }, // -U
            new[] { 1, 3, 7, 5 }, // +U
        };
        return new Slab(verts, faces);
    }
}
