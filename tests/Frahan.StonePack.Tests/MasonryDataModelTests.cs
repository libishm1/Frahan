#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Tests;

// Phase A.1 unit tests for the C# port of compas_cra's data model.
// All pure-managed; no Rhino runtime needed; should run as PASS on the
// headless host.

static class MasonryDataModelTests
{
    // -- MasonryBlock -------------------------------------------------------

    public static void MasonryBlock_NullId_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryBlock(
                id: "",
                vertexCoordsXyz: new[] { 0.0, 0, 0 },
                triangleIndices: new[] { 0, 0, 0 },
                density: 1.0);
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "blank id should throw ArgumentException");
    }

    public static void MasonryBlock_NullVertexCoords_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryBlock(
                id: "b1",
                vertexCoordsXyz: null,
                triangleIndices: new[] { 0, 0, 0 },
                density: 1.0);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null vertexCoordsXyz should throw ArgumentNullException");
    }

    public static void MasonryBlock_NegativeDensity_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryBlock(
                id: "b1",
                vertexCoordsXyz: new[] { 0.0, 0, 0, 1, 0, 0, 0, 1, 0 },
                triangleIndices: new[] { 0, 1, 2 },
                density: -1.0);
        }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "non-positive density should throw ArgumentOutOfRangeException");
    }

    public static void MasonryBlock_VertexAndTriangleCounts_AreConsistent()
    {
        var block = new MasonryBlock(
            id: "b1",
            vertexCoordsXyz: new[] { 0.0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }, // 4 verts
            triangleIndices: new[] { 0, 1, 2,  0, 2, 3,  0, 3, 1,  1, 3, 2 }, // 4 tris (tetra)
            density: 2400.0);

        Assert(block.VertexCount == 4, $"VertexCount expected 4, got {block.VertexCount}");
        Assert(block.TriangleCount == 4, $"TriangleCount expected 4, got {block.TriangleCount}");
        Assert(block.Density == 2400.0, "density round-trip");
        Assert(block.Id == "b1", "id round-trip");
    }

    public static void MasonryBlock_BadTriangleIndex_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryBlock(
                id: "b1",
                vertexCoordsXyz: new[] { 0.0, 0, 0, 1, 0, 0 }, // 2 verts
                triangleIndices: new[] { 0, 1, 5 }, // index 5 out of range
                density: 1.0);
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "out-of-range triangle index should throw ArgumentException");
    }

    public static void MasonryBlock_VertexCoordsLengthNotMultipleOf3_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryBlock(
                id: "b1",
                vertexCoordsXyz: new[] { 0.0, 0 }, // length 2
                triangleIndices: new[] { 0, 0, 0 },
                density: 1.0);
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "vertexCoordsXyz length not multiple of 3 should throw");
    }

    // -- ContactVertex ------------------------------------------------------

    public static void ContactVertex_StoresCoordinates()
    {
        var v = new ContactVertex(1.5, -2.0, 0.25);
        Assert(v.X == 1.5, "X round-trip");
        Assert(v.Y == -2.0, "Y round-trip");
        Assert(v.Z == 0.25, "Z round-trip");
    }

    public static void ContactVertex_EqualityAndHash_AreConsistent()
    {
        var a = new ContactVertex(1.0, 2.0, 3.0);
        var b = new ContactVertex(1.0, 2.0, 3.0);
        var c = new ContactVertex(1.0, 2.0, 3.0001);

        Assert(a.Equals(b), "equal vertices should compare equal");
        Assert(a.GetHashCode() == b.GetHashCode(), "equal vertices should hash the same");
        Assert(!a.Equals(c), "different vertices should not be equal");
    }

    // -- MasonryInterface ---------------------------------------------------

    public static void MasonryInterface_NullPolygon_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryInterface(
                blockAId: "a", blockBId: "b",
                contactPolygon: null,
                normalX: 0, normalY: 0, normalZ: 1,
                tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
                tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null contactPolygon should throw ArgumentNullException");
    }

    public static void MasonryInterface_PolygonTooSmall_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryInterface(
                blockAId: "a", blockBId: "b",
                contactPolygon: new[]
                {
                    new ContactVertex(0, 0, 0),
                    new ContactVertex(1, 0, 0),
                },
                normalX: 0, normalY: 0, normalZ: 1,
                tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
                tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "contactPolygon with < 3 vertices should throw ArgumentException");
    }

    public static void MasonryInterface_SameBlockIds_Throws()
    {
        bool threw = false;
        try
        {
            _ = new MasonryInterface(
                blockAId: "a", blockBId: "a",
                contactPolygon: new[]
                {
                    new ContactVertex(0, 0, 0),
                    new ContactVertex(1, 0, 0),
                    new ContactVertex(0, 1, 0),
                },
                normalX: 0, normalY: 0, normalZ: 1,
                tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
                tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "same blockA and blockB should throw ArgumentException");
    }

    public static void MasonryInterface_StoresFrameVectors()
    {
        var iface = new MasonryInterface(
            blockAId: "a", blockBId: "b",
            contactPolygon: new[]
            {
                new ContactVertex(0, 0, 0),
                new ContactVertex(1, 0, 0),
                new ContactVertex(1, 1, 0),
                new ContactVertex(0, 1, 0),
            },
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);

        Assert(iface.VertexCount == 4, "VertexCount round-trip");
        Assert(iface.NormalZ == 1.0 && iface.NormalX == 0.0, "normal round-trip");
        Assert(iface.Tangent1X == 1.0, "tangent1 round-trip");
        Assert(iface.Tangent2Y == 1.0, "tangent2 round-trip");
        Assert(iface.BlockAId == "a" && iface.BlockBId == "b", "block ids round-trip");
    }

    // -- BoundaryConditions -------------------------------------------------

    public static void BoundaryConditions_IsFixed_ReturnsFalseForUnknownId()
    {
        var bc = new BoundaryConditions(new[] { "a", "b" });
        Assert(!bc.IsFixed("c"), "unknown id should report not fixed");
    }

    public static void BoundaryConditions_IsFixed_ReturnsTrueForKnownId()
    {
        var bc = new BoundaryConditions(new[] { "a", "b" });
        Assert(bc.IsFixed("a"), "known fixed id should report fixed");
        Assert(bc.IsFixed("b"), "second known fixed id should report fixed");
        Assert(bc.FixedCount == 2, $"FixedCount expected 2, got {bc.FixedCount}");
    }

    public static void BoundaryConditions_NullEnumerable_Throws()
    {
        bool threw = false;
        try { _ = new BoundaryConditions(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null fixedBlockIds should throw ArgumentNullException");
    }

    // -- MasonryAssembly ----------------------------------------------------

    public static void MasonryAssembly_StoresBlocksAndInterfaces()
    {
        var assembly = TwoBlockAssembly();

        Assert(assembly.BlockCount == 2, "block count round-trip");
        Assert(assembly.InterfaceCount == 1, "interface count round-trip");
        Assert(assembly.FreeBlockCount == 1, $"free block count expected 1, got {assembly.FreeBlockCount}");

        var ground = assembly.GetBlock("ground");
        Assert(ground.Id == "ground", "GetBlock by id");
        Assert(assembly.TryGetBlock("missing", out _) == false, "TryGetBlock missing returns false");
    }

    public static void MasonryAssembly_DuplicateBlockId_Throws()
    {
        var b1 = MakeUnitCube("dup");
        var b2 = MakeUnitCube("dup");
        bool threw = false;
        try
        {
            _ = new MasonryAssembly(
                blocks: new[] { b1, b2 },
                interfaces: Array.Empty<MasonryInterface>(),
                boundaryConditions: new BoundaryConditions(Array.Empty<string>()));
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "duplicate block id should throw ArgumentException");
    }

    public static void MasonryAssembly_InterfaceReferencingUnknownBlock_Throws()
    {
        var b1 = MakeUnitCube("a");
        var iface = QuadInterface("a", "ghost");
        bool threw = false;
        try
        {
            _ = new MasonryAssembly(
                blocks: new[] { b1 },
                interfaces: new[] { iface },
                boundaryConditions: new BoundaryConditions(Array.Empty<string>()));
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "interface referencing unknown block should throw ArgumentException");
    }

    public static void MasonryAssembly_NullBoundaryConditions_Throws()
    {
        var b1 = MakeUnitCube("a");
        bool threw = false;
        try
        {
            _ = new MasonryAssembly(
                blocks: new[] { b1 },
                interfaces: Array.Empty<MasonryInterface>(),
                boundaryConditions: null);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null boundary conditions should throw ArgumentNullException");
    }

    public static void MasonryAssembly_FreeBlocks_ExcludesFixed()
    {
        var assembly = TwoBlockAssembly();
        var freeIds = new List<string>();
        foreach (var b in assembly.FreeBlocks) freeIds.Add(b.Id);
        Assert(freeIds.Count == 1, $"FreeBlocks count expected 1, got {freeIds.Count}");
        Assert(freeIds[0] == "top", $"free block expected 'top', got '{freeIds[0]}'");
    }

    // -- Helpers ------------------------------------------------------------

    private static MasonryBlock MakeUnitCube(string id)
    {
        var verts = new double[]
        {
            0,0,0,  1,0,0,  1,1,0,  0,1,0,
            0,0,1,  1,0,1,  1,1,1,  0,1,1,
        };
        var tris = new[]
        {
            0,2,1, 0,3,2, // -Z
            4,5,6, 4,6,7, // +Z
            0,1,5, 0,5,4, // -Y
            2,3,7, 2,7,6, // +Y
            1,2,6, 1,6,5, // +X
            0,4,7, 0,7,3, // -X
        };
        return new MasonryBlock(id, verts, tris, density: 2400.0);
    }

    private static MasonryInterface QuadInterface(string a, string b)
    {
        return new MasonryInterface(
            blockAId: a, blockBId: b,
            contactPolygon: new[]
            {
                new ContactVertex(0, 0, 1),
                new ContactVertex(1, 0, 1),
                new ContactVertex(1, 1, 1),
                new ContactVertex(0, 1, 1),
            },
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
    }

    private static MasonryAssembly TwoBlockAssembly()
    {
        var ground = MakeUnitCube("ground");
        var top = MakeUnitCube("top");
        return new MasonryAssembly(
            blocks: new[] { ground, top },
            interfaces: new[] { QuadInterface("ground", "top") },
            boundaryConditions: new BoundaryConditions(new[] { "ground" }));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
