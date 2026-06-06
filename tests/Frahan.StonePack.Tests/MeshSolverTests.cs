#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// MeshSolverTests — sanity checks for mesh diagnostic components and the
// 4-colour block-graph colorer. The mesh-touching tests are tagged "(Rhino)"
// and SKIP headlessly without a live rhcommon_c.dll.
// =============================================================================

static class MeshSolverTests
{
    // ─── Mesh diagnostic GH components ─────────────────────────────────────

    public static void Gh_MeshAabbComponent_Metadata()
    {
        var c = new MeshAabbComponent();
        Assert(c.ComponentGuid == new Guid("ABCDEF01-2345-6789-ABCD-EF0123456789"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Mesh", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_MeshPcaComponent_Metadata()
    {
        var c = new MeshPcaComponent();
        Assert(c.ComponentGuid == new Guid("BCDEF012-3456-789A-BCDE-F0123456789A"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan" && c.SubCategory == "Mesh",
            $"tab/sub: {c.Category}/{c.SubCategory}");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_MeshDiagnosticsComponent_Metadata()
    {
        // Phase F7 (UX architecture report §5.3 + AGENTS.md §8 stability):
        // the newer Masonry MeshDiagnosticsComponent (GUID CDEF0123) was
        // dropped 2026-05-19; the older root MeshDiagnosticsComponent
        // (AB12C005, longer commit lineage) survives. Same MeshDiag nickname
        // collision pattern as MeshFix; MQ Mesh Quality Report stays as the
        // richer alternative.
        var c = new MeshDiagnosticsComponent();
        Assert(c.ComponentGuid == new Guid("AB12C005-1A2B-4C3D-9E4F-5A6B7C8D9E05"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan" && c.SubCategory == "Mesh",
            $"tab/sub: {c.Category}/{c.SubCategory}");
        // Root variant: Mesh = 1 in
        // V + F + T + Q + Ic + Im + Cw + Ae + Bv + R = 10 out.
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 10, $"Output count {c.Params.Output.Count}");
    }

    // ─── BlockGraphColorer ─────────────────────────────────────────────────

    public static void BlockGraphColorer_TwoStackedBlocks_GetTwoColours()
    {
        // Manually build a 2-block assembly with one bed-joint interface.
        var ground = MakeUnitBlock("ground", 0);
        var top = MakeUnitBlock("top", 1);
        var contact = new ContactVertex[]
        {
            new ContactVertex(0, 0, 1),
            new ContactVertex(1, 0, 1),
            new ContactVertex(1, 1, 1),
            new ContactVertex(0, 1, 1),
        };
        var iface = new MasonryInterface("ground", "top", contact,
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
        var bc = new BoundaryConditions(new[] { "ground" });
        var assembly = new MasonryAssembly(new[] { ground, top }, new[] { iface }, bc);

        var coloring = BlockGraphColorer.Color(assembly);
        Assert(coloring.Count == 2, $"expected 2 entries, got {coloring.Count}");
        Assert(coloring["ground"] != coloring["top"],
            $"adjacent blocks must differ; both got {coloring["ground"]}");
    }

    public static void BlockGraphColorer_NoInterfaces_AllZero()
    {
        // Two isolated blocks → both can be colour 0.
        var a = MakeUnitBlock("a", 0);
        var b = MakeUnitBlock("b", 2);  // far apart, no interface
        var bc = new BoundaryConditions(new[] { "a" });
        var assembly = new MasonryAssembly(
            new[] { a, b }, new MasonryInterface[0], bc);
        var coloring = BlockGraphColorer.Color(assembly);
        Assert(coloring["a"] == 0 && coloring["b"] == 0,
            $"expected both 0, got {coloring["a"]}, {coloring["b"]}");
    }

    public static void BlockGraphColorer_TriangleGraph_RequiresThree()
    {
        // 3 blocks all mutually connected (triangle) → chromatic number 3.
        var a = MakeUnitBlock("a", 0);
        var b = MakeUnitBlock("b", 2);
        var c = MakeUnitBlock("c", 4);
        var contact = new ContactVertex[]
        {
            new ContactVertex(0, 0, 0),
            new ContactVertex(1, 0, 0),
            new ContactVertex(1, 1, 0),
            new ContactVertex(0, 1, 0),
        };
        var ifaces = new[]
        {
            new MasonryInterface("a", "b", contact, 0, 0, 1, 1, 0, 0, 0, 1, 0),
            new MasonryInterface("b", "c", contact, 0, 0, 1, 1, 0, 0, 0, 1, 0),
            new MasonryInterface("a", "c", contact, 0, 0, 1, 1, 0, 0, 0, 1, 0),
        };
        var bc = new BoundaryConditions(new[] { "a" });
        var assembly = new MasonryAssembly(new[] { a, b, c }, ifaces, bc);

        var coloring = BlockGraphColorer.Color(assembly);
        var distinct = new HashSet<int>(coloring.Values);
        Assert(distinct.Count == 3,
            $"triangle graph requires 3 colours, got {distinct.Count}");
    }

    public static void BlockGraphColorer_NullAssembly_Throws()
    {
        bool threw = false;
        try { _ = BlockGraphColorer.Color(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null assembly should throw");
    }

    public static void Gh_BlockGraphColoringComponent_Metadata()
    {
        var c = new BlockGraphColoringComponent();
        Assert(c.ComponentGuid == new Guid("F2D000B0-CADC-4F2D-A0B0-7E60CADA15A0"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan" && c.SubCategory == "Masonry",
            $"tab/sub: {c.Category}/{c.SubCategory}");
        Assert(c.Params.Input.Count == 1, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 3, $"Output count {c.Params.Output.Count}");
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private static MasonryBlock MakeUnitBlock(string id, double zMin)
    {
        var verts = new double[]
        {
            0, 0, zMin,  1, 0, zMin,  1, 1, zMin,  0, 1, zMin,
            0, 0, zMin + 1,  1, 0, zMin + 1,  1, 1, zMin + 1,  0, 1, zMin + 1,
        };
        var tris = new int[]
        {
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,
            1, 5, 6, 1, 6, 2,
            2, 6, 7, 2, 7, 3,
            3, 7, 4, 3, 4, 0,
        };
        return new MasonryBlock(id, verts, tris, 2400);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
