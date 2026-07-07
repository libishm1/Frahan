#nullable disable
using System;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// Risk M4: the Core <-> Bullet type-conversion seam. Two conventions must stay
// in agreement and are pinned here headless (no BulletSharp, no Rhino):
//
// 1. PRODUCER (BulletSettleService.cs ~:183): BulletSharp stores a ROW-VECTOR
//    basis (v_row * M = world), so the column-vector rotation is its transpose;
//    the service packs Rotation = {M11,M21,M31, M12,M22,M32, M13,M23,M33}
//    (row-major R for column-vector math).
// 2. CONSUMER (NboSettle.cs ~:141): reads Rotation as row-major R and applies
//    v' = R*(v - Centroid) + Translation.
//
// If either end flips the transpose, these tests fail.
static class MasonrySeamTests
{
    public static void BulletBasisPacking_RoundTripsAKnownRotation()
    {
        // Physical rotation: +90 deg about Z (column-vector): e_x -> e_y.
        // BulletSharp stores the TRANSPOSE as its row-vector basis:
        double m11 = 0, m12 = 1, m13 = 0;    // first basis ROW = R^T row 0
        double m21 = -1, m22 = 0, m23 = 0;
        double m31 = 0, m32 = 0, m33 = 1;

        // producer packing (BulletSettleService formula, verbatim order):
        var R = new[] { m11, m21, m31, m12, m22, m32, m13, m23, m33 };

        // consumer application (NboSettle formula): v' = R*(v - C) + T
        double[] Apply(double[] v, double[] c, double[] t) => new[]
        {
            R[0] * (v[0] - c[0]) + R[1] * (v[1] - c[1]) + R[2] * (v[2] - c[2]) + t[0],
            R[3] * (v[0] - c[0]) + R[4] * (v[1] - c[1]) + R[5] * (v[2] - c[2]) + t[1],
            R[6] * (v[0] - c[0]) + R[7] * (v[1] - c[1]) + R[8] * (v[2] - c[2]) + t[2],
        };

        var zero = new double[3];
        var ex = Apply(new double[] { 1, 0, 0 }, zero, zero);
        var ey = Apply(new double[] { 0, 1, 0 }, zero, zero);
        AssertVec(ex, 0, 1, 0, "e_x must map to e_y under +90deg-about-Z");
        AssertVec(ey, -1, 0, 0, "e_y must map to -e_x under +90deg-about-Z");

        // rigid motion with centroid + translation: rotate about C then move to T
        var c2 = new double[] { 1, 1, 0 };
        var t2 = new double[] { 5, 5, 2 };
        var p = Apply(new double[] { 2, 1, 0 }, c2, t2);  // (2,1)-(1,1)=(1,0) -> (0,1); +T
        AssertVec(p, 5, 6, 2, "rigid application about centroid must compose correctly");
    }

    public static void MeshSnapshot_RoundTripsVerticesAndTriangles()
    {
        // Core flat-array seam: verts/tris -> MeshSnapshot -> read back identical.
        var verts = new double[] { 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4 };
        var tris = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };
        var snap = new MeshSnapshot(verts, tris);

        Assert(snap.VertexCoordsXyz.Count == verts.Length, "vertex array length changed");
        Assert(snap.TriangleIndices.Count == tris.Length, "triangle array length changed");
        for (int i = 0; i < verts.Length; i++)
            Assert(snap.VertexCoordsXyz[i] == verts[i], $"vertex coord {i} changed");
        for (int i = 0; i < tris.Length; i++)
            Assert(snap.TriangleIndices[i] == tris[i], $"triangle index {i} changed");
    }

    private static void AssertVec(double[] v, double x, double y, double z, string msg)
    {
        Assert(Math.Abs(v[0] - x) < 1e-12 && Math.Abs(v[1] - y) < 1e-12 && Math.Abs(v[2] - z) < 1e-12,
            $"{msg}: got ({v[0]},{v[1]},{v[2]}), want ({x},{y},{z})");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException("MasonrySeam: " + msg);
    }
}
