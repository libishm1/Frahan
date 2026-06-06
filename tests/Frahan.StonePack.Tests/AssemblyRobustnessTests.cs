#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// AssemblyRobustnessTests — Phase 2 of the robustness pass.
// Adaptive tolerance + build-order partial assembler + GH metadata.
// =============================================================================

static class AssemblyRobustnessTests
{
    // ─── Adaptive tolerance ─────────────────────────────────────────────

    public static void Adaptive_TightStaticTolFailsButAdaptiveSucceeds_LargeBlocks()
    {
        // Two large unit-scale blocks with a 1% gap (= 0.01 = 1 cm relative
        // to a 1m block). With distanceTol = 1e-5, no contact is found.
        // With adaptiveToleranceFactor = 0.05, the per-pair tolerance
        // becomes ~0.05 * 1.0 = 0.05 (median edge ~1m for this cube), so
        // the gap is well within tolerance.
        const double gap = 0.01;
        var a = MakeUnitCubeSnap(0, 0, 0);
        var b = MakeUnitCubeSnap(0, 0, 1.0 + gap);

        var staticOnly = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-5, angleTolDeg: 5.0, minContactPoints: 3,
            adaptiveToleranceFactor: 0.0);
        Assert(staticOnly.Count == 0,
            $"static tol 1e-5 expected 0 contacts on 1cm gap, got {staticOnly.Count}");

        var adaptive = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-5, angleTolDeg: 5.0, minContactPoints: 3,
            adaptiveToleranceFactor: 0.05);
        Assert(adaptive.Count == 1,
            $"adaptive tol expected 1 contact on 1cm gap, got {adaptive.Count}");
    }

    public static void Adaptive_BackwardCompat_Default0FactorMatchesOldBehavior()
    {
        // adaptiveToleranceFactor defaults to 0 → behaviour identical to
        // the previous Detect signature.
        var a = MakeUnitCubeSnap(0, 0, 0);
        var b = MakeUnitCubeSnap(0, 0, 1);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-3);
        Assert(ifaces.Count == 1, $"baseline expected 1, got {ifaces.Count}");
    }

    // ─── BuildOrderPartialAssembler ────────────────────────────────────

    public static void Partial_SingleColumn_GrowsOneBlockAtATime()
    {
        var blocks = new List<MasonryBlock>(4);
        for (int i = 0; i < 4; i++) blocks.Add(MakeBlock($"c{i}", 0, 0, i));
        var ifaces = new List<MasonryInterface>(3);
        for (int i = 0; i < 3; i++)
            ifaces.Add(MakeBedJoint($"c{i}", $"c{i + 1}", 0.5, 0.5, i + 1.0));
        var asm = new MasonryAssembly(
            blocks, ifaces, new BoundaryConditions(new[] { "c0" }));

        var order = new[] { "c0", "c1", "c2", "c3" };
        int step = 0;
        foreach (var p in BuildOrderPartialAssembler.EnumeratePartials(asm, order))
        {
            Assert(p.BlockCount == step + 1, $"step {step} block count {p.BlockCount}");
            // Interfaces among present blocks: 0..step-1 of the chain.
            Assert(p.InterfaceCount == step, $"step {step} interface count {p.InterfaceCount}");
            // Fixed boundary preserved.
            Assert(p.BoundaryConditions.IsFixed("c0"), "c0 must be fixed");
            step++;
        }
        Assert(step == 4, $"expected 4 steps, got {step}");
    }

    public static void Partial_FixedBlocksAlwaysIncluded_EvenAtStepZero()
    {
        // Fixed block "g" (ground), free blocks "a", "b". Build order
        // doesn't list "g"; it should still appear in every partial.
        var blocks = new List<MasonryBlock>
        {
            MakeBlock("g", 0, 0, 0),
            MakeBlock("a", 0, 0, 1),
            MakeBlock("b", 0, 0, 2),
        };
        var ifaces = new List<MasonryInterface>
        {
            MakeBedJoint("g", "a", 0.5, 0.5, 1.0),
            MakeBedJoint("a", "b", 0.5, 0.5, 2.0),
        };
        var asm = new MasonryAssembly(
            blocks, ifaces, new BoundaryConditions(new[] { "g" }));

        var order = new[] { "a", "b" };
        var partial0 = BuildOrderPartialAssembler.BuildPartial(asm, order, 0);
        // Step 0: g + a present.
        Assert(partial0.BlockCount == 2, $"step 0 V {partial0.BlockCount}");
        Assert(partial0.BoundaryConditions.IsFixed("g"), "g fixed at step 0");
        Assert(partial0.InterfaceCount == 1, $"step 0 ifaces {partial0.InterfaceCount}");
    }

    public static void Partial_OrderedIdNotInAssembly_Throws()
    {
        var asm = new MasonryAssembly(
            new[] { MakeBlock("a", 0, 0, 0) },
            Array.Empty<MasonryInterface>(),
            new BoundaryConditions(Array.Empty<string>()));
        bool threw = false;
        try { _ = BuildOrderPartialAssembler.BuildPartial(asm, new[] { "missing" }, 0); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "unknown id must throw");
    }

    public static void Partial_DuplicateIdInOrder_Throws()
    {
        var asm = new MasonryAssembly(
            new[] { MakeBlock("a", 0, 0, 0), MakeBlock("b", 0, 0, 1) },
            Array.Empty<MasonryInterface>(),
            new BoundaryConditions(Array.Empty<string>()));
        bool threw = false;
        try
        {
            foreach (var _ in BuildOrderPartialAssembler.EnumeratePartials(
                asm, new[] { "a", "a" })) { }
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "duplicate id must throw");
    }

    // ─── GH metadata ────────────────────────────────────────────────────

    public static void Gh_BuildOrderStabilityStreamComponent_Metadata()
    {
        var c = new BuildOrderStabilityStreamComponent();
        Assert(c.ComponentGuid == new Guid("F2D000B3-CADC-4F2D-A0B3-7E60CADA15A0"),
            $"GUID {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 8, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5, $"Output count {c.Params.Output.Count}");
    }

    // ─── helpers ────────────────────────────────────────────────────────

    private static MeshSnapshot MakeUnitCubeSnap(double x0, double y0, double z0)
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
        return new MeshSnapshot(verts, tris);
    }

    private static MasonryBlock MakeBlock(string id, double x0, double y0, double z0)
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
        var poly = new List<ContactVertex>
        {
            new ContactVertex(cx - 0.5, cy - 0.5, cz),
            new ContactVertex(cx + 0.5, cy - 0.5, cz),
            new ContactVertex(cx + 0.5, cy + 0.5, cz),
            new ContactVertex(cx - 0.5, cy + 0.5, cz),
        };
        return new MasonryInterface(
            aId, bId, poly,
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
