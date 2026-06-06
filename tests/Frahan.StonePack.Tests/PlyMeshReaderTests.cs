#nullable disable
using System;
using System.IO;
using System.Text;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// PlyMeshReaderTests — pure-managed PLY parser. ASCII and binary
// little-endian round-trips, error cases.
// =============================================================================

static class PlyMeshReaderTests
{
    // ─── ASCII round-trip ──────────────────────────────────────────────────

    public static void Read_AsciiUnitCube_ParsesVertsAndTris()
    {
        // 4 verts + 2 tris in the simplest legal form (a square split).
        var ply = new StringBuilder();
        ply.AppendLine("ply");
        ply.AppendLine("format ascii 1.0");
        ply.AppendLine("element vertex 4");
        ply.AppendLine("property float x");
        ply.AppendLine("property float y");
        ply.AppendLine("property float z");
        ply.AppendLine("element face 2");
        ply.AppendLine("property list uchar int vertex_indices");
        ply.AppendLine("end_header");
        ply.AppendLine("0.0 0.0 0.0");
        ply.AppendLine("1.0 0.0 0.0");
        ply.AppendLine("1.0 1.0 0.0");
        ply.AppendLine("0.0 1.0 0.0");
        ply.AppendLine("3 0 1 2");
        ply.AppendLine("3 0 2 3");

        var m = ReadString(ply.ToString());
        Assert(m.VertexCount == 4, $"V expected 4, got {m.VertexCount}");
        Assert(m.TriangleCount == 2, $"T expected 2, got {m.TriangleCount}");
        AssertNear(m.VertexCoordsXyz[0], 0.0, 1e-9, "v0.x");
        AssertNear(m.VertexCoordsXyz[3], 1.0, 1e-9, "v1.x");
        AssertNear(m.VertexCoordsXyz[6], 1.0, 1e-9, "v2.x");
        AssertNear(m.VertexCoordsXyz[7], 1.0, 1e-9, "v2.y");
        Assert(m.TriangleIndices[0] == 0 && m.TriangleIndices[1] == 1 && m.TriangleIndices[2] == 2,
            "tri 0 indices");
    }

    public static void Read_AsciiQuadFace_FanTriangulates()
    {
        var ply = new StringBuilder();
        ply.AppendLine("ply");
        ply.AppendLine("format ascii 1.0");
        ply.AppendLine("element vertex 4");
        ply.AppendLine("property float x");
        ply.AppendLine("property float y");
        ply.AppendLine("property float z");
        ply.AppendLine("element face 1");
        ply.AppendLine("property list uchar int vertex_indices");
        ply.AppendLine("end_header");
        ply.AppendLine("0 0 0");
        ply.AppendLine("1 0 0");
        ply.AppendLine("1 1 0");
        ply.AppendLine("0 1 0");
        ply.AppendLine("4 0 1 2 3");

        var m = ReadString(ply.ToString());
        // Fan: (0,1,2) and (0,2,3).
        Assert(m.TriangleCount == 2, $"quad → 2 triangles, got {m.TriangleCount}");
        Assert(m.TriangleIndices[0] == 0 && m.TriangleIndices[1] == 1 && m.TriangleIndices[2] == 2,
            "first fan tri");
        Assert(m.TriangleIndices[3] == 0 && m.TriangleIndices[4] == 2 && m.TriangleIndices[5] == 3,
            "second fan tri");
    }

    public static void Read_AsciiWithVertexColors_PreservesRgb()
    {
        var ply = new StringBuilder();
        ply.AppendLine("ply");
        ply.AppendLine("format ascii 1.0");
        ply.AppendLine("element vertex 3");
        ply.AppendLine("property float x");
        ply.AppendLine("property float y");
        ply.AppendLine("property float z");
        ply.AppendLine("property uchar red");
        ply.AppendLine("property uchar green");
        ply.AppendLine("property uchar blue");
        ply.AppendLine("element face 1");
        ply.AppendLine("property list uchar int vertex_indices");
        ply.AppendLine("end_header");
        ply.AppendLine("0 0 0 255 0 0");
        ply.AppendLine("1 0 0 0 255 0");
        ply.AppendLine("0 1 0 0 0 255");
        ply.AppendLine("3 0 1 2");

        var m = ReadString(ply.ToString());
        Assert(m.HasColors, "expected colours");
        Assert(m.VertexColorsRgb.Count == 9, $"colour count expected 9, got {m.VertexColorsRgb.Count}");
        Assert(m.VertexColorsRgb[0] == 255 && m.VertexColorsRgb[1] == 0 && m.VertexColorsRgb[2] == 0,
            "v0 red");
        Assert(m.VertexColorsRgb[3] == 0 && m.VertexColorsRgb[4] == 255 && m.VertexColorsRgb[5] == 0,
            "v1 green");
    }

    // ─── Binary little-endian ───────────────────────────────────────────────

    public static void Read_BinaryLEUnitCube_ParsesVertsAndTris()
    {
        // Build a binary LE PLY in-memory: header + binary body.
        var header = new StringBuilder();
        header.AppendLine("ply");
        header.AppendLine("format binary_little_endian 1.0");
        header.AppendLine("element vertex 4");
        header.AppendLine("property float x");
        header.AppendLine("property float y");
        header.AppendLine("property float z");
        header.AppendLine("element face 2");
        header.AppendLine("property list uchar int vertex_indices");
        header.AppendLine("end_header");

        using (var ms = new MemoryStream())
        {
            // Header is ASCII text.
            var hb = Encoding.ASCII.GetBytes(header.ToString());
            ms.Write(hb, 0, hb.Length);
            // 4 vertices × 3 floats.
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                bw.Write(0f); bw.Write(0f); bw.Write(0f);
                bw.Write(1f); bw.Write(0f); bw.Write(0f);
                bw.Write(1f); bw.Write(1f); bw.Write(0f);
                bw.Write(0f); bw.Write(1f); bw.Write(0f);
                // 2 faces, each: uchar count=3, then 3 int32.
                bw.Write((byte)3); bw.Write(0); bw.Write(1); bw.Write(2);
                bw.Write((byte)3); bw.Write(0); bw.Write(2); bw.Write(3);
            }
            ms.Position = 0;
            var m = PlyMeshReader.Read(ms);
            Assert(m.VertexCount == 4, $"V expected 4, got {m.VertexCount}");
            Assert(m.TriangleCount == 2, $"T expected 2, got {m.TriangleCount}");
            AssertNear(m.VertexCoordsXyz[3], 1.0, 1e-6, "v1.x");
            AssertNear(m.VertexCoordsXyz[7], 1.0, 1e-6, "v2.y");
        }
    }

    // ─── Error cases ────────────────────────────────────────────────────────

    public static void Read_MissingPlyMagic_Throws()
    {
        bool threw = false;
        try { _ = ReadString("not a ply file\n"); }
        catch (FormatException) { threw = true; }
        Assert(threw, "missing 'ply' magic must throw FormatException");
    }

    public static void Read_BinaryBigEndian_ParsesVertsAndTris()
    {
        // binary_big_endian IS supported (ReadScalarLE byte-swaps via RevBytes),
        // e.g. for Stanford-style BE .ply scans. Build a BE PLY and verify it
        // parses. (The old test asserted a NotSupportedException that the reader
        // never throws — it was outdated; updated 2026-05-29.)
        var header = new StringBuilder();
        header.AppendLine("ply");
        header.AppendLine("format binary_big_endian 1.0");
        header.AppendLine("element vertex 4");
        header.AppendLine("property float x");
        header.AppendLine("property float y");
        header.AppendLine("property float z");
        header.AppendLine("element face 2");
        header.AppendLine("property list uchar int vertex_indices");
        header.AppendLine("end_header");

        using (var ms = new MemoryStream())
        {
            var hb = Encoding.ASCII.GetBytes(header.ToString());
            ms.Write(hb, 0, hb.Length);
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                WriteBe(bw, 0f); WriteBe(bw, 0f); WriteBe(bw, 0f);
                WriteBe(bw, 1f); WriteBe(bw, 0f); WriteBe(bw, 0f);
                WriteBe(bw, 1f); WriteBe(bw, 1f); WriteBe(bw, 0f);
                WriteBe(bw, 0f); WriteBe(bw, 1f); WriteBe(bw, 0f);
                bw.Write((byte)3); WriteBe(bw, 0); WriteBe(bw, 1); WriteBe(bw, 2);
                bw.Write((byte)3); WriteBe(bw, 0); WriteBe(bw, 2); WriteBe(bw, 3);
            }
            ms.Position = 0;
            var m = PlyMeshReader.Read(ms);
            Assert(m.VertexCount == 4, $"V expected 4, got {m.VertexCount}");
            Assert(m.TriangleCount == 2, $"T expected 2, got {m.TriangleCount}");
            AssertNear(m.VertexCoordsXyz[3], 1.0, 1e-6, "v1.x (big-endian)");
            AssertNear(m.VertexCoordsXyz[7], 1.0, 1e-6, "v2.y (big-endian)");
        }
    }

    private static void WriteBe(BinaryWriter bw, float v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        bw.Write(b);
    }

    private static void WriteBe(BinaryWriter bw, int v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        bw.Write(b);
    }

    public static void Read_NoVertexElement_Throws()
    {
        var s = "ply\nformat ascii 1.0\nelement face 0\n" +
                "property list uchar int vertex_indices\nend_header\n";
        bool threw = false;
        try { _ = ReadString(s); }
        catch (FormatException) { threw = true; }
        Assert(threw, "no vertex element must throw FormatException");
    }

    public static void Read_MissingXyzProperties_Throws()
    {
        var s = "ply\nformat ascii 1.0\nelement vertex 1\n" +
                "property float u\nproperty float v\nproperty float w\n" +
                "end_header\n0.5 0.5 0.5\n";
        bool threw = false;
        try { _ = ReadString(s); }
        catch (FormatException) { threw = true; }
        Assert(threw, "missing x/y/z must throw FormatException");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static PlyMesh ReadString(string s)
    {
        using (var ms = new MemoryStream(Encoding.ASCII.GetBytes(s)))
            return PlyMeshReader.Read(ms);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertNear(double a, double b, double tol, string label)
    {
        if (Math.Abs(a - b) > tol)
            throw new InvalidOperationException(
                $"{label}: expected {b}, got {a} (|diff|={Math.Abs(a - b)} > {tol})");
    }
}
