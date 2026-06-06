#nullable disable
using System;
using System.IO;
using Frahan.Surface;
using Rhino.Geometry;

namespace Frahan.Tests;

// Surface packing unit tests — no Rhino runtime required.
// Tests cover: FaceCornerUvTable, OBJ parsing, barycentric math, chart scale.

static class SurfacePackingTests
{
    // ── FaceCornerUvTable ────────────────────────────────────────────────────

    public static void FaceCornerUvTable_StoresAndRetrieves()
    {
        var table = new FaceCornerUvTable();
        table.SetUv(0, 0, 0.1, 0.2);
        table.SetUv(0, 1, 0.3, 0.4);
        table.SetUv(0, 2, 0.5, 0.6);

        Assert(table.TryGetUv(0, 0, out var uv0), "face 0 corner 0 should exist");
        Assert(Math.Abs(uv0.X - 0.1) < 1e-10, "u0 should be 0.1");
        Assert(Math.Abs(uv0.Y - 0.2) < 1e-10, "v0 should be 0.2");

        Assert(table.TryGetUv(0, 2, out var uv2), "face 0 corner 2 should exist");
        Assert(Math.Abs(uv2.X - 0.5) < 1e-10, "u2 should be 0.5");

        Assert(!table.TryGetUv(1, 0, out _), "face 1 corner 0 should not exist");
        Assert(table.EntryCount == 3, "should have 3 entries");
    }

    public static void FaceCornerUvTable_OverwritesDuplicateKey()
    {
        var table = new FaceCornerUvTable();
        table.SetUv(5, 1, 0.9, 0.8);
        table.SetUv(5, 1, 0.7, 0.6); // overwrite

        Assert(table.TryGetUv(5, 1, out var uv), "should exist after overwrite");
        Assert(Math.Abs(uv.X - 0.7) < 1e-10, "latest value should win");
        Assert(table.EntryCount == 1, "overwrite should not create duplicate");
    }

    public static void FaceCornerUvTable_FaceCornerKeyEquality()
    {
        var k1 = new FaceCornerKey(3, 2);
        var k2 = new FaceCornerKey(3, 2);
        var k3 = new FaceCornerKey(3, 1);

        Assert(k1.Equals(k2), "identical keys should be equal");
        Assert(!k1.Equals(k3), "different corner index should not match");
        Assert(k1.GetHashCode() == k2.GetHashCode(), "equal keys must have equal hash");
        Assert(k1.GetHashCode() != k3.GetHashCode(),
            "different keys should (typically) have different hashes — index 2 vs 1");
    }

    // ── OBJ Parsing ─────────────────────────────────────────────────────────

    public static void MeshObjIO_ParsesValidBffOutput()
    {
        // Simulate a minimal BFF output OBJ: 2 triangular faces, 4 vertices, 6 UV coords
        string objContent =
            "# BFF output\n" +
            "v 0 0 0\n" +
            "v 1 0 0\n" +
            "v 0 1 0\n" +
            "v 1 1 0\n" +
            "vt 0.0 0.0\n" +
            "vt 1.0 0.0\n" +
            "vt 0.0 1.0\n" +
            "vt 1.0 0.0\n" +
            "vt 1.0 1.0\n" +
            "vt 0.0 1.0\n" +
            "f 1/1 2/2 3/3\n" +
            "f 2/4 4/5 3/6\n";

        string tmp = WriteTempObj(objContent);
        try
        {
            bool ok = MeshObjIO.TryParseObjWithFaceCornerUVs(
                tmp, 2, out var table, out string err);

            Assert(ok, $"parse should succeed — error: {err}");
            Assert(table.EntryCount == 6, $"expected 6 UV entries, got {table.EntryCount}");

            Assert(table.TryGetUv(0, 0, out var uv00), "face 0 corner 0 missing");
            Assert(Math.Abs(uv00.X - 0.0) < 1e-9, "face 0 corner 0 u should be 0");
            Assert(Math.Abs(uv00.Y - 0.0) < 1e-9, "face 0 corner 0 v should be 0");

            Assert(table.TryGetUv(1, 1, out var uv11), "face 1 corner 1 missing");
            Assert(Math.Abs(uv11.X - 1.0) < 1e-9, "face 1 corner 1 u should be 1");
            Assert(Math.Abs(uv11.Y - 1.0) < 1e-9, "face 1 corner 1 v should be 1");
        }
        finally { TryDelete(tmp); }
    }

    public static void MeshObjIO_RejectsMissingUVIndex()
    {
        // Face definition without UV index (plain "f v v v")
        string objContent =
            "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
            "vt 0.0 0.0\nvt 1.0 0.0\nvt 0.0 1.0\n" +
            "f 1 2 3\n"; // no UV indices

        string tmp = WriteTempObj(objContent);
        try
        {
            bool ok = MeshObjIO.TryParseObjWithFaceCornerUVs(
                tmp, 1, out _, out string err);

            Assert(!ok, "should reject face without UV indices");
            Assert(err.Length > 0, "error message should not be empty");
        }
        finally { TryDelete(tmp); }
    }

    public static void MeshObjIO_RejectsMissingFile()
    {
        bool ok = MeshObjIO.TryParseObjWithFaceCornerUVs(
            @"C:\does_not_exist_frahan_test.obj", 0, out _, out string err);

        Assert(!ok, "should reject missing file");
        Assert(err.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0,
            $"error should mention 'not found': {err}");
    }

    public static void MeshObjIO_RejectsFaceCountMismatch()
    {
        string objContent =
            "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
            "vt 0.0 0.0\nvt 1.0 0.0\nvt 0.0 1.0\n" +
            "f 1/1 2/2 3/3\n"; // 1 face

        string tmp = WriteTempObj(objContent);
        try
        {
            // Claim 2 faces expected but only 1 present
            bool ok = MeshObjIO.TryParseObjWithFaceCornerUVs(
                tmp, 2, out _, out string err);

            Assert(!ok, "should reject face count mismatch");
            Assert(err.IndexOf("mismatch", StringComparison.OrdinalIgnoreCase) >= 0,
                $"error should mention mismatch: {err}");
        }
        finally { TryDelete(tmp); }
    }

    public static void MeshObjIO_IgnoresCommentAndBlankLines()
    {
        string objContent =
            "# comment line\n\n" +
            "v 0 0 0\n" +
            "# another comment\n" +
            "v 1 0 0\n" +
            "v 0 1 0\n\n" +
            "vt 0.5 0.5\nvt 0.8 0.2\nvt 0.2 0.8\n" +
            "\n# face comment\n" +
            "f 1/1 2/2 3/3\n";

        string tmp = WriteTempObj(objContent);
        try
        {
            bool ok = MeshObjIO.TryParseObjWithFaceCornerUVs(
                tmp, 1, out var table, out string err);

            Assert(ok, $"should ignore comments and blanks — error: {err}");
            Assert(table.EntryCount == 3, "should have 3 UV entries");
        }
        finally { TryDelete(tmp); }
    }

    // ── BarycentricMapper2DTo3D ─────────────────────────────────────────────

    public static void BarycentricMapper_IdentityTriangle_MapsToSelf()
    {
        // When flat and surface are the same triangle, any interior point maps to itself.
        var flat = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));
        var surf = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));

        var startPt = new Point3d(0.2, 0.1, 0);
        var endPt   = new Point3d(0.3, 0.2, 0);
        var line    = new LineCurve(startPt, endPt);
        var result  = BarycentricMapper2DTo3D.MapCurveTo3DSurface(line, flat, surf, 0.001);

        Assert(result != null, "identity map should succeed");
        Assert(result.PointAtStart.DistanceTo(startPt) < 0.01,
            $"identity: start should match, got {result.PointAtStart}");
        Assert(result.PointAtEnd.DistanceTo(endPt) < 0.01,
            $"identity: end should match, got {result.PointAtEnd}");
    }

    public static void BarycentricMapper_ScaledTriangle_MapsCorrectly()
    {
        // Flat: unit triangle; Surface: 2× scaled and elevated to Z=5.
        // The centroid of the flat triangle (1/3, 1/3, 0) should map to (2/3, 2/3, 5).
        var flat = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));
        var surf = MakeTriangleMesh(
            new Point3d(0, 0, 5), new Point3d(2, 0, 5), new Point3d(0, 2, 5));

        var centroid2D = new Point3d(1.0 / 3.0, 1.0 / 3.0, 0);
        var line = new LineCurve(centroid2D, centroid2D + new Vector3d(0.01, 0, 0));
        var result = BarycentricMapper2DTo3D.MapCurveTo3DSurface(line, flat, surf, 0.001);

        Assert(result != null, "scaled triangle map should succeed");
        var startPt = result.PointAtStart;
        Assert(Math.Abs(startPt.X - 2.0 / 3.0) < 0.01, $"mapped X should be ~2/3, got {startPt.X:F4}");
        Assert(Math.Abs(startPt.Y - 2.0 / 3.0) < 0.01, $"mapped Y should be ~2/3, got {startPt.Y:F4}");
        Assert(Math.Abs(startPt.Z - 5.0) < 0.01, $"mapped Z should be 5.0, got {startPt.Z:F4}");
    }

    public static void BarycentricMapper_PointOutsideMesh_ReturnsNull()
    {
        // A point far outside the chart should fail — both barycentric search and
        // ClosestMeshPoint fallback (with tight tolerance) should miss.
        var flat = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));
        var surf = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));

        // Point at (10, 10) is far outside the unit triangle; fallback tolerance = 0.001 * 5 = 0.005
        var line = new LineCurve(new Point3d(10, 10, 0), new Point3d(11, 10, 0));
        var result = BarycentricMapper2DTo3D.MapCurveTo3DSurface(line, flat, surf, 0.001);

        Assert(result == null, "point far outside chart should return null");
    }

    public static void BarycentricMapper_NullCurve_ReturnsNull()
    {
        var flat = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));
        var surf = MakeTriangleMesh(
            new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0));

        var result = BarycentricMapper2DTo3D.MapCurveTo3DSurface(null, flat, surf, 0.001);
        Assert(result == null, "null curve input should return null");
    }

    private static Mesh MakeTriangleMesh(Point3d a, Point3d b, Point3d c)
    {
        var m = new Mesh();
        m.Vertices.Add(a);
        m.Vertices.Add(b);
        m.Vertices.Add(c);
        m.Faces.AddFace(0, 1, 2);
        m.FaceNormals.ComputeFaceNormals();
        return m;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string WriteTempObj(string content)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"frahan_test_{Guid.NewGuid():N}.obj");
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* non-critical in tests */ }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
