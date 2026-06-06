#nullable disable
using System;
using Frahan.GH.Masonry;
using Frahan.Masonry.Geometry;

namespace Frahan.Tests;

// =============================================================================
// RigidTransformRecoveryTests — Horn 1987 absolute orientation, pure-managed.
// Verifies (R, t) recovery for identity, pure translation, pure rotation,
// and combined transforms, plus the GH component metadata.
// =============================================================================

static class RigidTransformRecoveryTests
{
    // ─── Core: identity ─────────────────────────────────────────────────────

    public static void Solve_Identity_RecoversIdentityRotationAndZeroTranslation()
    {
        var src = MakeUnitCubeVerts();
        var r = RigidTransformRecovery.Solve(src, src);
        AssertNear(r.Rotation[0, 0], 1.0, 1e-9, "R[0,0]");
        AssertNear(r.Rotation[1, 1], 1.0, 1e-9, "R[1,1]");
        AssertNear(r.Rotation[2, 2], 1.0, 1e-9, "R[2,2]");
        AssertNear(r.Translation[0], 0.0, 1e-9, "t.x");
        AssertNear(r.Translation[1], 0.0, 1e-9, "t.y");
        AssertNear(r.Translation[2], 0.0, 1e-9, "t.z");
        Assert(r.RmsError < 1e-9, $"RMS should be ~0, got {r.RmsError}");
    }

    // ─── Core: pure translation ─────────────────────────────────────────────

    public static void Solve_PureTranslation_RecoversIdentityRotationAndCorrectT()
    {
        var src = MakeUnitCubeVerts();
        var dst = Translate(src, 5.0, -3.0, 7.0);
        var r = RigidTransformRecovery.Solve(src, dst);
        AssertNear(r.Rotation[0, 0], 1.0, 1e-9, "R[0,0]");
        AssertNear(r.Rotation[1, 1], 1.0, 1e-9, "R[1,1]");
        AssertNear(r.Rotation[2, 2], 1.0, 1e-9, "R[2,2]");
        AssertNear(r.Translation[0], 5.0, 1e-9, "t.x");
        AssertNear(r.Translation[1], -3.0, 1e-9, "t.y");
        AssertNear(r.Translation[2], 7.0, 1e-9, "t.z");
        Assert(r.RmsError < 1e-9, $"RMS should be ~0, got {r.RmsError}");
    }

    // ─── Core: pure rotation around Z ──────────────────────────────────────

    public static void Solve_PureRotationZ_Recovers90DegRotation()
    {
        var src = MakeUnitCubeVerts();
        // Rotate +90° around Z: (x, y, z) → (-y, x, z).
        var dst = (double[])src.Clone();
        for (int i = 0; i < src.Length / 3; i++)
        {
            double x = src[3 * i + 0], y = src[3 * i + 1];
            dst[3 * i + 0] = -y;
            dst[3 * i + 1] = x;
            // z unchanged
        }
        var r = RigidTransformRecovery.Solve(src, dst);
        AssertNear(r.Rotation[0, 0], 0.0, 1e-9, "R[0,0]");
        AssertNear(r.Rotation[0, 1], -1.0, 1e-9, "R[0,1]");
        AssertNear(r.Rotation[1, 0], 1.0, 1e-9, "R[1,0]");
        AssertNear(r.Rotation[1, 1], 0.0, 1e-9, "R[1,1]");
        AssertNear(r.Rotation[2, 2], 1.0, 1e-9, "R[2,2]");
        Assert(r.RmsError < 1e-9, $"RMS should be ~0, got {r.RmsError}");
    }

    // ─── Core: combined rotation + translation ─────────────────────────────

    public static void Solve_RotationPlusTranslation_RoundTrips()
    {
        var src = MakeUnitCubeVerts();
        // Rotate 45° around Y, then translate (1, 2, 3).
        double a = Math.PI / 4.0;
        double cos = Math.Cos(a), sin = Math.Sin(a);
        var dst = new double[src.Length];
        for (int i = 0; i < src.Length / 3; i++)
        {
            double x = src[3 * i + 0], y = src[3 * i + 1], z = src[3 * i + 2];
            // Ry(a): (x*cos + z*sin, y, -x*sin + z*cos)
            double nx = x * cos + z * sin;
            double ny = y;
            double nz = -x * sin + z * cos;
            dst[3 * i + 0] = nx + 1.0;
            dst[3 * i + 1] = ny + 2.0;
            dst[3 * i + 2] = nz + 3.0;
        }
        var r = RigidTransformRecovery.Solve(src, dst);
        // R · src + t should match dst within numeric noise.
        for (int i = 0; i < src.Length / 3; i++)
        {
            double x = src[3 * i + 0], y = src[3 * i + 1], z = src[3 * i + 2];
            double rx = r.Rotation[0, 0] * x + r.Rotation[0, 1] * y + r.Rotation[0, 2] * z + r.Translation[0];
            double ry = r.Rotation[1, 0] * x + r.Rotation[1, 1] * y + r.Rotation[1, 2] * z + r.Translation[1];
            double rz = r.Rotation[2, 0] * x + r.Rotation[2, 1] * y + r.Rotation[2, 2] * z + r.Translation[2];
            AssertNear(rx, dst[3 * i + 0], 1e-9, $"vert {i}.x");
            AssertNear(ry, dst[3 * i + 1], 1e-9, $"vert {i}.y");
            AssertNear(rz, dst[3 * i + 2], 1e-9, $"vert {i}.z");
        }
        Assert(r.RmsError < 1e-9, $"RMS should be ~0, got {r.RmsError}");
    }

    // ─── Core: argument validation ─────────────────────────────────────────

    public static void Solve_NullSource_Throws()
    {
        bool threw = false;
        try { _ = RigidTransformRecovery.Solve(null, MakeUnitCubeVerts()); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null source must throw");
    }

    public static void Solve_MismatchedLengths_Throws()
    {
        bool threw = false;
        try { _ = RigidTransformRecovery.Solve(new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, new double[] { 0, 0, 0, 1, 0, 0 }); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "mismatched lengths must throw");
    }

    public static void Solve_FewerThanThreePairs_Throws()
    {
        bool threw = false;
        try
        {
            _ = RigidTransformRecovery.Solve(
                new double[] { 0, 0, 0, 1, 0, 0 },
                new double[] { 0, 0, 0, 1, 0, 0 });
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "fewer than 3 pairs must throw");
    }

    // ─── GH component metadata ─────────────────────────────────────────────

    public static void Gh_BlockGroundTransformsComponent_Metadata()
    {
        var c = new BlockGroundTransformsComponent();
        Assert(c.ComponentGuid == new Guid("23456789-ABCD-EF01-2345-6789ABCDEF01"),
            $"GUID mismatch: {c.ComponentGuid}");
        Assert(c.Category == "Frahan", $"Category '{c.Category}'");
        Assert(c.SubCategory == "Masonry", $"SubCategory '{c.SubCategory}'");
        Assert(c.Params.Input.Count == 4, $"Input count {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4, $"Output count {c.Params.Output.Count}");
    }

    public static void Gh_BlockGroundTransformsComponent_OptionalInputs()
    {
        var c = new BlockGroundTransformsComponent();
        // Source Meshes (1), Existing Transforms (2), Ground Plane (3) optional.
        Assert(c.Params.Input[1].Optional, "Source Meshes must be optional");
        Assert(c.Params.Input[2].Optional, "Existing Transforms must be optional");
        Assert(c.Params.Input[3].Optional, "Ground Plane must be optional");
        // Placed Meshes (0) is required.
        Assert(!c.Params.Input[0].Optional, "Placed Meshes must be required");
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private static double[] MakeUnitCubeVerts()
    {
        // 8 corners of [0,1]^3 — non-coplanar, gives a well-conditioned fit.
        return new double[]
        {
            0, 0, 0,  1, 0, 0,  1, 1, 0,  0, 1, 0,
            0, 0, 1,  1, 0, 1,  1, 1, 1,  0, 1, 1,
        };
    }

    private static double[] Translate(double[] src, double dx, double dy, double dz)
    {
        var dst = new double[src.Length];
        for (int i = 0; i < src.Length / 3; i++)
        {
            dst[3 * i + 0] = src[3 * i + 0] + dx;
            dst[3 * i + 1] = src[3 * i + 1] + dy;
            dst[3 * i + 2] = src[3 * i + 2] + dz;
        }
        return dst;
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
