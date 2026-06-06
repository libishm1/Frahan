#nullable disable
using System;
using System.IO;
using System.Text;
using Frahan.Core.ScanIngest;

namespace Frahan.Tests;

// =============================================================================
// ScanIngestTests — Phase F1+F2 multi-format scan reader. Covers the
// OBJ + STL managed parsers and the MultiFormatMeshReader dispatcher.
// Pure managed — no Rhino runtime required.
// =============================================================================

static class ScanIngestTests
{
    // ─── OBJ parser ──────────────────────────────────────────────────────

    public static void Obj_SingleTriangle_ParsesOneMeshOneTriangle()
    {
        const string objText =
            "v 0 0 0\n" +
            "v 1 0 0\n" +
            "v 0 1 0\n" +
            "f 1 2 3\n";
        var meshes = ObjMeshReader.ReadFromString(objText);
        Assert(meshes.Count == 1, $"expected 1 mesh, got {meshes.Count}");
        Assert(meshes[0].VertexCount == 3, $"expected 3 verts, got {meshes[0].VertexCount}");
        Assert(meshes[0].TriangleCount == 1, $"expected 1 tri, got {meshes[0].TriangleCount}");
    }

    public static void Obj_QuadFace_FanTriangulatesIntoTwoTriangles()
    {
        const string objText =
            "v 0 0 0\n" +
            "v 1 0 0\n" +
            "v 1 1 0\n" +
            "v 0 1 0\n" +
            "f 1 2 3 4\n";
        var meshes = ObjMeshReader.ReadFromString(objText);
        Assert(meshes[0].TriangleCount == 2, $"quad should fan-triangulate to 2 tris, got {meshes[0].TriangleCount}");
    }

    public static void Obj_TripletFaceSyntax_KeepsVertexIndex()
    {
        const string objText =
            "v 0 0 0\n" +
            "v 1 0 0\n" +
            "v 0 1 0\n" +
            "vt 0 0\n" +
            "vn 0 0 1\n" +
            "f 1/1/1 2/1/1 3/1/1\n";
        var meshes = ObjMeshReader.ReadFromString(objText);
        Assert(meshes[0].TriangleCount == 1, "v/vt/vn triplet face should still produce 1 triangle");
        Assert(meshes[0].TriangleIndices[0] == 0, "first triangle index should be 0 (1-based 1 mapped to 0-based)");
    }

    public static void Obj_TwoGroups_ProducesTwoMeshes()
    {
        const string objText =
            "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
            "g cube_a\n" +
            "f 1 2 3\n" +
            "v 2 0 0\nv 3 0 0\nv 2 1 0\n" +
            "g cube_b\n" +
            "f 4 5 6\n";
        var meshes = ObjMeshReader.ReadFromString(objText);
        Assert(meshes.Count == 2, $"expected 2 groups → 2 meshes, got {meshes.Count}");
        Assert(meshes[0].Name == "cube_a", $"first group name should be 'cube_a', got '{meshes[0].Name}'");
        Assert(meshes[1].Name == "cube_b", $"second group name should be 'cube_b', got '{meshes[1].Name}'");
        // Each group re-indexes its own vertex pool densely.
        Assert(meshes[0].VertexCount == 3 && meshes[1].VertexCount == 3,
            "each group should have 3 dense-remapped verts");
    }

    public static void Obj_NegativeFaceIndex_ResolvesRelativeToCount()
    {
        const string objText =
            "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
            "f -3 -2 -1\n";
        var meshes = ObjMeshReader.ReadFromString(objText);
        Assert(meshes[0].TriangleCount == 1, "negative indices should resolve to one triangle");
    }

    public static void Obj_NoFaces_Throws()
    {
        const string objText = "v 0 0 0\nv 1 0 0\nv 0 1 0\n";
        try
        {
            ObjMeshReader.ReadFromString(objText);
            throw new Exception("Expected FormatException for OBJ with no faces.");
        }
        catch (FormatException) { /* expected */ }
    }

    // ─── STL parser — ASCII ──────────────────────────────────────────────

    public static void Stl_AsciiTetra_ProducesWeldedMesh()
    {
        // Tetrahedron with 4 vertices, 4 faces. STL repeats vertices per
        // facet (3 verts each, 12 total) but the welder collapses them
        // back to 4 unique vertices.
        const string ascii =
            "solid tetra\n" +
            "  facet normal 0 0 1\n" +
            "    outer loop\n" +
            "      vertex 0 0 0\n" +
            "      vertex 1 0 0\n" +
            "      vertex 0 1 0\n" +
            "    endloop\n" +
            "  endfacet\n" +
            "  facet normal 1 0 0\n" +
            "    outer loop\n" +
            "      vertex 0 0 0\n" +
            "      vertex 0 1 0\n" +
            "      vertex 0 0 1\n" +
            "    endloop\n" +
            "  endfacet\n" +
            "  facet normal 0 1 0\n" +
            "    outer loop\n" +
            "      vertex 0 0 0\n" +
            "      vertex 0 0 1\n" +
            "      vertex 1 0 0\n" +
            "    endloop\n" +
            "  endfacet\n" +
            "  facet normal 0.577 0.577 0.577\n" +
            "    outer loop\n" +
            "      vertex 1 0 0\n" +
            "      vertex 0 0 1\n" +
            "      vertex 0 1 0\n" +
            "    endloop\n" +
            "  endfacet\n" +
            "endsolid tetra\n";
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ascii, Encoding.ASCII);
        try
        {
            var stl = StlMeshReader.ReadFile(path);
            Assert(stl.VertexCount == 4, $"welded tetra should have 4 unique verts, got {stl.VertexCount}");
            Assert(stl.TriangleCount == 4, $"tetra has 4 facets, got {stl.TriangleCount}");
            Assert(stl.Name == "tetra", $"name should be 'tetra', got '{stl.Name}'");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ─── STL parser — binary ──────────────────────────────────────────────

    public static void Stl_BinarySingleTriangle_Parses()
    {
        // 80-byte header + uint32 ntri + 50 bytes per triangle.
        // 1 triangle: 84 + 50 = 134 bytes.
        var bytes = new byte[134];
        // Header: zeros (some exporters write "solid" here, fine).
        // ntri = 1
        bytes[80] = 1; bytes[81] = 0; bytes[82] = 0; bytes[83] = 0;
        int o = 84;
        // Normal 0 0 1
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(1f).CopyTo(bytes, o); o += 4;
        // v0 0 0 0
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        // v1 1 0 0
        BitConverter.GetBytes(1f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        // v2 0 1 0
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(1f).CopyTo(bytes, o); o += 4;
        BitConverter.GetBytes(0f).CopyTo(bytes, o); o += 4;
        // attr (2 bytes)
        o += 2;
        Assert(o == 134, $"expected to fill 134 bytes, used {o}");

        var stl = StlMeshReader.ReadFromBytes(bytes);
        Assert(stl.TriangleCount == 1, $"expected 1 tri, got {stl.TriangleCount}");
        Assert(stl.VertexCount == 3, $"expected 3 unique verts, got {stl.VertexCount}");
    }

    // ─── MultiFormatMeshReader — extension dispatch ──────────────────────

    public static void Dispatcher_DetectsByExtension()
    {
        // Use temp files with each extension; content is enough for the
        // parser to succeed.
        var plyPath = Path.ChangeExtension(Path.GetTempFileName(), ".ply");
        File.WriteAllText(plyPath,
            "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n" +
            "element face 1\nproperty list uchar int vertex_indices\nend_header\n" +
            "0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n");
        var objPath = Path.ChangeExtension(Path.GetTempFileName(), ".obj");
        File.WriteAllText(objPath, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        try
        {
            Assert(MultiFormatMeshReader.Detect(plyPath) == ScanFormat.Ply, "PLY ext should dispatch to PLY");
            Assert(MultiFormatMeshReader.Detect(objPath) == ScanFormat.Obj, "OBJ ext should dispatch to OBJ");

            var plyMeshes = MultiFormatMeshReader.ReadFile(plyPath);
            Assert(plyMeshes.Count == 1 && plyMeshes[0].TriangleCount == 1,
                $"PLY should produce 1 mesh with 1 tri, got {plyMeshes.Count} / {plyMeshes[0].TriangleCount}");

            var objMeshes = MultiFormatMeshReader.ReadFile(objPath);
            Assert(objMeshes.Count == 1 && objMeshes[0].TriangleCount == 1,
                "OBJ should produce 1 mesh with 1 tri");
        }
        finally
        {
            File.Delete(plyPath);
            File.Delete(objPath);
        }
    }

    public static void Dispatcher_ForcedFormatOverridesExtension()
    {
        // Write OBJ content into a file with .ply extension; force OBJ.
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".ply");
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        try
        {
            var meshes = MultiFormatMeshReader.ReadFile(path, ScanFormat.Obj);
            Assert(meshes.Count == 1 && meshes[0].TriangleCount == 1,
                "Forced OBJ should override the .ply extension");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static void Dispatcher_MissingFile_Throws()
    {
        try
        {
            MultiFormatMeshReader.ReadFile("Z:/__definitely_does_not_exist__.ply");
            throw new Exception("Expected FileNotFoundException.");
        }
        catch (FileNotFoundException) { /* expected */ }
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }
}
