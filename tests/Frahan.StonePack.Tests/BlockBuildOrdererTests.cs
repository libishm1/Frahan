#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// BlockBuildOrdererTests — topological sort over the support DAG built
// from a MasonryAssembly's interface set. Pure-managed.
// =============================================================================

static class BlockBuildOrdererTests
{
    // ─── Single column: 4 cubes stacked along Z ─────────────────────────────

    public static void Solve_SingleColumn_OrdersBottomToTop()
    {
        var blocks = new List<MasonryBlock>(4);
        for (int i = 0; i < 4; i++) blocks.Add(MakeCube($"c{i}", 0, 0, i));
        var ifaces = new List<MasonryInterface>(3);
        for (int i = 0; i < 3; i++)
            ifaces.Add(MakeBedJoint($"c{i}", $"c{i + 1}", 0.0 + 0.5, 0.5, i + 1.0));

        var asm = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(Array.Empty<string>()));
        var order = BlockBuildOrderer.Solve(asm);

        Assert(order.Count == 4, $"expected 4 steps, got {order.Count}");
        for (int i = 0; i < 4; i++)
        {
            Assert(order[i].BlockId == $"c{i}", $"step {i} id expected c{i}, got {order[i].BlockId}");
            Assert(order[i].OrderIndex == i, $"step {i} OrderIndex");
            Assert(order[i].Layer == i, $"step {i} layer expected {i}, got {order[i].Layer}");
        }
    }

    // ─── Two-course wall: 4 cubes (2 bottom + 2 top) ───────────────────────

    public static void Solve_TwoCourses_BottomBeforeTop()
    {
        var blocks = new List<MasonryBlock>
        {
            MakeCube("b0", 0, 0, 0),
            MakeCube("b1", 1, 0, 0),
            MakeCube("t0", 0, 0, 1),
            MakeCube("t1", 1, 0, 1),
        };
        var ifaces = new List<MasonryInterface>
        {
            MakeBedJoint("b0", "t0", 0.5, 0.5, 1.0),
            MakeBedJoint("b1", "t1", 1.5, 0.5, 1.0),
        };
        var asm = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(Array.Empty<string>()));
        var order = BlockBuildOrderer.Solve(asm);

        Assert(order.Count == 4, $"expected 4 steps, got {order.Count}");
        // Bottoms (layer 0) must come before tops (layer 1).
        int b0 = -1, b1 = -1, t0 = -1, t1 = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].BlockId == "b0") b0 = i;
            if (order[i].BlockId == "b1") b1 = i;
            if (order[i].BlockId == "t0") t0 = i;
            if (order[i].BlockId == "t1") t1 = i;
        }
        Assert(b0 < t0, $"b0 (i={b0}) must come before t0 (i={t0})");
        Assert(b1 < t1, $"b1 (i={b1}) must come before t1 (i={t1})");
        // Layers correct.
        for (int i = 0; i < order.Count; i++)
        {
            int expected = order[i].BlockId.StartsWith("b") ? 0 : 1;
            Assert(order[i].Layer == expected,
                $"{order[i].BlockId} layer expected {expected}, got {order[i].Layer}");
        }
    }

    // ─── Head joint only: side-by-side, both at layer 0 ────────────────────

    public static void Solve_SideBySide_NoSupport_BothLayerZero()
    {
        var blocks = new List<MasonryBlock>
        {
            MakeCube("L", 0, 0, 0),
            MakeCube("R", 1, 0, 0),
        };
        // Head joint: normal points +X (A=L → B=R). Not aligned with up.
        var ifaces = new List<MasonryInterface>
        {
            MakeHeadJoint("L", "R", 1.0, 0.5, 0.5),
        };
        var asm = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(Array.Empty<string>()));
        var order = BlockBuildOrderer.Solve(asm);

        Assert(order.Count == 2, $"expected 2 steps, got {order.Count}");
        Assert(order[0].Layer == 0, $"step 0 layer 0");
        Assert(order[1].Layer == 0, $"step 1 layer 0");
        // Tiebreak by id (ordinal): "L" < "R".
        Assert(order[0].BlockId == "L", $"first should be L, got {order[0].BlockId}");
        Assert(order[1].BlockId == "R", $"second should be R, got {order[1].BlockId}");
    }

    // ─── Custom up vector: stacking along +X ───────────────────────────────

    public static void Solve_CustomUpAxis_OrdersAlongThatAxis()
    {
        var blocks = new List<MasonryBlock>
        {
            MakeCube("a", 2, 0, 0),
            MakeCube("b", 1, 0, 0),
            MakeCube("c", 0, 0, 0),
        };
        // Bed joints normal +X (looking like head joints under default up,
        // but with up = +X they're bed joints).
        var ifaces = new List<MasonryInterface>
        {
            MakeHeadJoint("c", "b", 1.0, 0.5, 0.5),  // c → b, normal +X
            MakeHeadJoint("b", "a", 2.0, 0.5, 0.5),  // b → a, normal +X
        };
        var asm = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(Array.Empty<string>()));

        // up = +X.
        var order = BlockBuildOrderer.Solve(asm, upX: 1.0, upY: 0.0, upZ: 0.0);

        Assert(order.Count == 3, $"expected 3, got {order.Count}");
        Assert(order[0].BlockId == "c", $"first should be c, got {order[0].BlockId}");
        Assert(order[1].BlockId == "b", $"second should be b, got {order[1].BlockId}");
        Assert(order[2].BlockId == "a", $"third should be a, got {order[2].BlockId}");
    }

    // ─── Cycle: throw ───────────────────────────────────────────────────────

    public static void Solve_Cycle_Throws()
    {
        var blocks = new List<MasonryBlock>
        {
            MakeCube("x", 0, 0, 0),
            MakeCube("y", 0, 0, 1),
        };
        // x → y AND y → x — physically nonsensical, but algorithmically a cycle.
        var ifaces = new List<MasonryInterface>
        {
            MakeBedJoint("x", "y", 0.5, 0.5, 1.0),  // x supports y
            MakeBedJoint("y", "x", 0.5, 0.5, 1.0),  // y supports x — cycle
        };
        var asm = new MasonryAssembly(blocks, ifaces, new BoundaryConditions(Array.Empty<string>()));

        bool threw = false;
        try { _ = BlockBuildOrderer.Solve(asm); }
        catch (InvalidOperationException) { threw = true; }
        Assert(threw, "cycle in support DAG must throw");
    }

    // ─── Argument validation ───────────────────────────────────────────────

    public static void Solve_NullAssembly_Throws()
    {
        bool threw = false;
        try { _ = BlockBuildOrderer.Solve(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null assembly must throw");
    }

    public static void Solve_DegenerateUp_Throws()
    {
        var blocks = new List<MasonryBlock> { MakeCube("a", 0, 0, 0) };
        var asm = new MasonryAssembly(
            blocks, new List<MasonryInterface>(), new BoundaryConditions(Array.Empty<string>()));
        bool threw = false;
        try { _ = BlockBuildOrderer.Solve(asm, 0, 0, 0); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "zero up vector must throw");
    }

    // ─── GH metadata ────────────────────────────────────────────────────────

    public static void Gh_BlockBuildOrderComponent_Metadata()
    {
        var c = new BlockBuildOrderComponent();
        Assert(c.ComponentGuid == new Guid("3456789A-BCDE-F012-3456-789ABCDEF012"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 3, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4, $"Output count {c.Params.Output.Count}");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static MasonryBlock MakeCube(string id, double x0, double y0, double z0)
    {
        var verts = new double[]
        {
            x0,     y0,     z0,
            x0 + 1, y0,     z0,
            x0 + 1, y0 + 1, z0,
            x0,     y0 + 1, z0,
            x0,     y0,     z0 + 1,
            x0 + 1, y0,     z0 + 1,
            x0 + 1, y0 + 1, z0 + 1,
            x0,     y0 + 1, z0 + 1,
        };
        var tris = new int[]
        {
            0, 3, 2,  0, 2, 1,
            4, 5, 6,  4, 6, 7,
            0, 1, 5,  0, 5, 4,
            1, 2, 6,  1, 6, 5,
            2, 3, 7,  2, 7, 6,
            3, 0, 4,  3, 4, 7,
        };
        return new MasonryBlock(id, verts, tris, 1.0);
    }

    private static MasonryInterface MakeBedJoint(
        string aId, string bId, double cx, double cy, double cz)
    {
        // Square contact polygon centered at (cx, cy, cz) on the +Z plane.
        var poly = new List<ContactVertex>
        {
            new ContactVertex(cx - 0.5, cy - 0.5, cz),
            new ContactVertex(cx + 0.5, cy - 0.5, cz),
            new ContactVertex(cx + 0.5, cy + 0.5, cz),
            new ContactVertex(cx - 0.5, cy + 0.5, cz),
        };
        // Normal A→B = +Z.
        return new MasonryInterface(
            aId, bId, poly,
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
    }

    private static MasonryInterface MakeHeadJoint(
        string aId, string bId, double cx, double cy, double cz)
    {
        // Vertical contact polygon at the +X face.
        var poly = new List<ContactVertex>
        {
            new ContactVertex(cx, cy - 0.5, cz - 0.5),
            new ContactVertex(cx, cy + 0.5, cz - 0.5),
            new ContactVertex(cx, cy + 0.5, cz + 0.5),
            new ContactVertex(cx, cy - 0.5, cz + 0.5),
        };
        // Normal A→B = +X.
        return new MasonryInterface(
            aId, bId, poly,
            normalX: 1, normalY: 0, normalZ: 0,
            tangent1X: 0, tangent1Y: 1, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 0, tangent2Z: 1);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
