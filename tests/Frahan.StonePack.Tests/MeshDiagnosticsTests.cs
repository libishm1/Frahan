#nullable disable
using System;
using Frahan.Surface;
using Rhino.Geometry;

namespace Frahan.Tests;

// Unit tests for Frahan.Surface.MeshDiagnostics.
// Most tests need the Rhino runtime to construct a Mesh; null-input tests
// run pure-managed (no Rhino runtime needed).

static class MeshDiagnosticsTests
{
    // -- Null guards (pure managed) -----------------------------------------

    public static void VertexCount_NullMesh_ReturnsZero()
    {
        Assert(MeshDiagnostics.VertexCount(null) == 0, "null mesh -> 0 vertices");
    }

    public static void FaceCount_NullMesh_ReturnsZero()
    {
        Assert(MeshDiagnostics.FaceCount(null) == 0, "null mesh -> 0 faces");
    }

    public static void IsClosed_NullMesh_ReturnsFalse()
    {
        Assert(!MeshDiagnostics.IsClosed(null), "null mesh -> not closed");
    }

    public static void IsManifold_NullMesh_ReturnsFalse()
    {
        Assert(!MeshDiagnostics.IsManifold(null), "null mesh -> not manifold");
    }

    public static void AverageEdgeLength_NullMesh_ReturnsZero()
    {
        Assert(MeshDiagnostics.AverageEdgeLength(null) == 0.0, "null mesh -> 0 avg edge");
    }

    public static void BoundingBoxVolume_NullMesh_ReturnsZero()
    {
        Assert(MeshDiagnostics.BoundingBoxVolume(null) == 0.0, "null mesh -> 0 bbox vol");
    }

    // -- Single triangle mesh (needs Rhino runtime) -------------------------

    public static void Counts_SingleTriangle_ReturnsExpected()
    {
        var mesh = MakeUnitTriangle();
        Assert(MeshDiagnostics.VertexCount(mesh) == 3, $"expected 3 verts, got {MeshDiagnostics.VertexCount(mesh)}");
        Assert(MeshDiagnostics.FaceCount(mesh) == 1, $"expected 1 face, got {MeshDiagnostics.FaceCount(mesh)}");
        Assert(MeshDiagnostics.TriangleCount(mesh) == 1, "expected 1 triangle");
        Assert(MeshDiagnostics.QuadCount(mesh) == 0, "expected 0 quads");
    }

    public static void IsClosed_SingleTriangle_IsFalse()
    {
        var mesh = MakeUnitTriangle();
        Assert(!MeshDiagnostics.IsClosed(mesh),
            "a single open triangle should not be reported closed");
    }

    public static void BoundingBoxVolume_FlatTriangle_IsZero()
    {
        var mesh = MakeUnitTriangle();
        Assert(MeshDiagnostics.BoundingBoxVolume(mesh) == 0.0,
            "a flat triangle has Z extent 0 -> bbox volume 0");
    }

    public static void AverageEdgeLength_UnitTriangle_IsExpected()
    {
        // Right triangle (0,0)-(1,0)-(0,1): edges 1, 1, sqrt(2). Average ~= 1.138.
        var mesh = MakeUnitTriangle();
        double avg = MeshDiagnostics.AverageEdgeLength(mesh);
        Assert(avg > 1.13 && avg < 1.14,
            $"unit right triangle avg edge length should be ~1.138, got {avg:0.###}");
    }

    private static Mesh MakeUnitTriangle()
    {
        var m = new Mesh();
        m.Vertices.Add(0, 0, 0);
        m.Vertices.Add(1, 0, 0);
        m.Vertices.Add(0, 1, 0);
        m.Faces.AddFace(0, 1, 2);
        m.FaceNormals.ComputeFaceNormals();
        return m;
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}
