#nullable disable
using System;
using Frahan.Masonry.Interfaces;

namespace Frahan.Tests;

// =============================================================================
// CoplanarResolverTests — Phase C of the expand-scope robustness pass.
// Validates that the opt-in coplanar-coincidence resolver finds contacts
// in cases where the existing sweeps would otherwise produce ambiguous
// or no normals.
// =============================================================================

static class CoplanarResolverTests
{
    public static void Coplanar_PerfectlyCoincidentSquares_FindsContact()
    {
        // Two thin slabs sharing an exactly coincident top/bottom face at z=1.
        // The coplanar resolver should populate contact points even though
        // vertex sweeps land on triangle BOUNDARIES (every A-vertex projects
        // to the corner of B-face — closest-point returns a vertex with
        // ambiguous normal).
        var lower = MakeUnitCubeSnap(0, 0, 0);
        var upper = MakeUnitCubeSnap(0, 0, 1);
        var ifaces = MeshContactDetector.Detect(
            new[] { lower, upper }, new[] { "L", "U" },
            distanceTol: 1e-6, angleTolDeg: 5.0, minContactPoints: 3,
            adaptiveToleranceFactor: 0.0,
            useCoplanarResolver: true);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalZ - 1.0) < 1e-6,
            $"normal Z expected 1, got {ifaces[0].NormalZ}");
    }

    public static void Coplanar_PartialOverlap_FindsContactAtIntersection()
    {
        // A in [0..1]^3, B at [0.5..1.5, 0.5..1.5, 1..2]. They share a
        // 0.5 × 0.5 region on the z=1 plane. The coplanar resolver should
        // sample the intersection's centroid.
        var a = MakeUnitCubeSnap(0, 0, 0);
        var b = MakeUnitCubeSnap(0.5, 0.5, 1);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-6, angleTolDeg: 5.0,
            useCoplanarResolver: true);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalZ - 1.0) < 1e-6,
            $"normal Z expected 1, got {ifaces[0].NormalZ}");
        // Verify the contact polygon falls inside the overlap region
        // (centered roughly at (0.75, 0.75, 1.0)).
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < ifaces[0].ContactPolygon.Count; i++)
        {
            cx += ifaces[0].ContactPolygon[i].X;
            cy += ifaces[0].ContactPolygon[i].Y;
            cz += ifaces[0].ContactPolygon[i].Z;
        }
        cx /= ifaces[0].ContactPolygon.Count;
        cy /= ifaces[0].ContactPolygon.Count;
        cz /= ifaces[0].ContactPolygon.Count;
        Assert(cx > 0.4 && cx < 1.1, $"contact centroid X out of overlap: {cx}");
        Assert(cy > 0.4 && cy < 1.1, $"contact centroid Y out of overlap: {cy}");
        Assert(Math.Abs(cz - 1.0) < 1e-6, $"contact centroid Z {cz}");
    }

    public static void Coplanar_NonCoincidentFaces_ResolverStillSafe()
    {
        // Two cubes side-by-side along +X. Their contact face at x=1 is
        // perpendicular to the bed-joint case — the resolver should still
        // find this contact (anti-parallel +X normals at x=1 plane).
        var left = MakeUnitCubeSnap(0, 0, 0);
        var right = MakeUnitCubeSnap(1, 0, 0);
        var ifaces = MeshContactDetector.Detect(
            new[] { left, right }, new[] { "L", "R" },
            distanceTol: 1e-6, angleTolDeg: 5.0,
            useCoplanarResolver: true);
        Assert(ifaces.Count == 1, $"expected 1 contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalX - 1.0) < 1e-6,
            $"normal X expected 1, got {ifaces[0].NormalX}");
    }

    public static void Coplanar_NoContact_ReturnsZero()
    {
        // Two cubes far apart — coplanar resolver should not invent
        // contacts.
        var a = MakeUnitCubeSnap(0, 0, 0);
        var b = MakeUnitCubeSnap(5, 5, 5);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-6, angleTolDeg: 5.0,
            useCoplanarResolver: true);
        Assert(ifaces.Count == 0, $"disjoint expected 0, got {ifaces.Count}");
    }

    public static void Coplanar_BackwardCompat_DefaultFalseUnchanged()
    {
        // useCoplanarResolver = false (default) should match the prior
        // behaviour from before Phase C.
        var a = MakeUnitCubeSnap(0, 0, 0);
        var b = MakeUnitCubeSnap(0, 0, 1);
        var ifaces = MeshContactDetector.Detect(
            new[] { a, b }, new[] { "A", "B" },
            distanceTol: 1e-3);
        Assert(ifaces.Count == 1, $"baseline expected 1, got {ifaces.Count}");
    }

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

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
