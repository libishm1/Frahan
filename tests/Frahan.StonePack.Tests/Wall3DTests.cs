#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Sequencing;

namespace Frahan.Tests;

// =============================================================================
// Wall3DTests — unit tests for the 3D install-order DAG. Geometry is
// hand-built rather than tessellated; the C# Wall3D is the structural
// algorithm, the Voronoi tessellation stays in the Python reference.
// =============================================================================

static class Wall3DTests
{
    // ─── Linear tower: 4 cubes stacked along +z ────────────────────────────

    public static void Tower_FourStacked_OrdersBottomToTop()
    {
        var cells = new List<Cell3D>
        {
            new Cell3D(0, (0.5, 0.5, 0.5)),
            new Cell3D(1, (0.5, 0.5, 1.5)),
            new Cell3D(2, (0.5, 0.5, 2.5)),
            new Cell3D(3, (0.5, 0.5, 3.5)),
        };
        var adj = new List<(int, int)> { (0, 1), (1, 2), (2, 3) };
        var wall = new Wall3D(cells, adj);
        var plan = wall.InstallSequence();
        Assert(plan.Order.Count == 4, $"order count {plan.Order.Count}");
        // Cell 0 bottom -> installed first.
        Assert(plan.Order[0] == 1, $"cell 0 expected order 1, got {plan.Order[0]}");
        Assert(plan.Order[1] == 2, $"cell 1 expected order 2, got {plan.Order[1]}");
        Assert(plan.Order[2] == 3, $"cell 2 expected order 3, got {plan.Order[2]}");
        Assert(plan.Order[3] == 4, $"cell 3 expected order 4, got {plan.Order[3]}");
        Assert(plan.Depth[3] == 0, $"top cell depth {plan.Depth[3]}");
        Assert(plan.Depth[0] == 3, $"bottom cell depth {plan.Depth[0]}");
    }

    // ─── Side by side: two cells at same z, no constraint ───────────────────

    public static void SideBySide_SameZ_NoEdge()
    {
        var cells = new List<Cell3D>
        {
            new Cell3D(0, (0.5, 0.5, 0.5)),
            new Cell3D(1, (1.5, 0.5, 0.5)),
        };
        var adj = new List<(int, int)> { (0, 1) };
        var wall = new Wall3D(cells, adj);
        var (graph, edges) = wall.BuildDag();
        Assert(edges.Count == 0,
            $"side-by-side at equal z must produce no edge, got {edges.Count}");
    }

    // ─── Pyramid: 4 cells at z=0, 1 on top at z=1 ───────────────────────────

    public static void Pyramid_OneOnFour_TopInstallsLast()
    {
        var cells = new List<Cell3D>
        {
            new Cell3D(0, (0.0, 0.0, 0.0)),
            new Cell3D(1, (1.0, 0.0, 0.0)),
            new Cell3D(2, (1.0, 1.0, 0.0)),
            new Cell3D(3, (0.0, 1.0, 0.0)),
            new Cell3D(4, (0.5, 0.5, 1.0)),
        };
        var adj = new List<(int, int)>
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (0, 4), (1, 4), (2, 4), (3, 4),
        };
        var wall = new Wall3D(cells, adj);
        var plan = wall.InstallSequence();
        // Cell 4 (top) must be installed after every cell at z=0.
        foreach (int low in new[] { 0, 1, 2, 3 })
        {
            Assert(plan.Order[low] < plan.Order[4],
                $"cell {low} order {plan.Order[low]} must come before " +
                $"cell 4 order {plan.Order[4]}");
        }
    }

    // ─── Holes: remove one cell, plan shrinks ───────────────────────────────

    public static void RemoveCells_SkipsRemovedFromPlan()
    {
        var cells = new List<Cell3D>
        {
            new Cell3D(0, (0.0, 0.0, 0.0)),
            new Cell3D(1, (0.0, 0.0, 1.0)),
            new Cell3D(2, (0.0, 0.0, 2.0)),
        };
        var adj = new List<(int, int)> { (0, 1), (1, 2) };
        var wall = new Wall3D(cells, adj);
        wall.RemoveCells(new[] { 1 });
        var plan = wall.InstallSequence();
        Assert(!plan.Order.ContainsKey(1),
            "removed cell must not appear in install order");
        Assert(plan.Order.ContainsKey(0) && plan.Order.ContainsKey(2),
            "remaining cells must stay in plan");
    }

    // ─── Adjacency dedup: NormaliseAdjacency drops duplicates and (a,a) ────

    public static void NormaliseAdjacency_DedupAndSelfLoops()
    {
        var pairs = new List<(int, int)>
        {
            (0, 1), (1, 0), (2, 3), (3, 2), (5, 5), (0, 1),
        };
        var norm = Wall3D.NormaliseAdjacency(pairs);
        Assert(norm.Count == 2,
            $"expected 2 unique non-self pairs, got {norm.Count}");
    }

    // ─── Unbounded cells are skipped ─────────────────────────────────────────

    public static void UnboundedCell_NotInstallable()
    {
        var cells = new List<Cell3D>
        {
            new Cell3D(0, (0.0, 0.0, 0.0)),
            new Cell3D(1, (0.0, 0.0, 1.0), isBounded: false),
            new Cell3D(2, (0.0, 0.0, 2.0)),
        };
        var adj = new List<(int, int)> { (0, 1), (1, 2) };
        var wall = new Wall3D(cells, adj);
        var plan = wall.InstallSequence();
        Assert(!plan.Order.ContainsKey(1),
            "unbounded cell must not appear in install order");
        Assert(plan.Order.ContainsKey(0) && plan.Order.ContainsKey(2),
            "bounded cells stay in plan");
    }

    // ─── Duplicate cell id throws ───────────────────────────────────────────

    public static void DuplicateCellId_Throws()
    {
        var cells = new List<Cell3D>
        {
            new Cell3D(0, (0, 0, 0)),
            new Cell3D(0, (1, 0, 0)),
        };
        var adj = new List<(int, int)>();
        bool threw = false;
        try { _ = new Wall3D(cells, adj); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "duplicate cell id must throw");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
