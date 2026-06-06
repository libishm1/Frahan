#nullable disable
using System;
using Frahan.Core.ScanIngest;

namespace Frahan.Tests;

// Unit tests for Frahan.Core.ScanIngest.ReconstructionCleanup -- the alpha-shape
// "weird mesh" fix (drop dangling/isolated SINGULAR triangles, keep largest
// connected component) + the recenter restore Translate. Pure managed, no Rhino.

static class ReconstructionCleanupTests
{
    // A welded square (2 triangles sharing an edge) = one component of 4 verts,
    // PLUS a far-away isolated triangle (3 separate verts) = the SINGULAR spike.
    private static void MakeSoupWithSpike(out double[] v, out int[] t)
    {
        v = new double[]
        {
            0,0,0,  1,0,0,  1,1,0,  0,1,0,      // 0..3 : unit square
            50,50,50, 51,50,50, 50,51,50,        // 4..6 : isolated spike triangle
        };
        t = new int[]
        {
            0,1,2,  0,2,3,    // square (share edge 0-2) -> one component
            4,5,6,            // isolated spike -> its own component
        };
    }

    public static void Clean_DropsIsolatedSpike_KeepsLargestComponent()
    {
        MakeSoupWithSpike(out var v, out var t);
        var (cv, ct) = ReconstructionCleanup.Clean(v, t);
        Assert(ct.Length / 3 == 2, $"should keep the 2-tri square, dropped the spike; got {ct.Length / 3} tris");
        Assert(cv.Length / 3 == 4, $"should keep 4 verts (square), spike verts compacted out; got {cv.Length / 3}");
        // every surviving index must be in range of the compacted verts
        foreach (var idx in ct) Assert(idx >= 0 && idx < cv.Length / 3, "index in range after compaction");
    }

    public static void Clean_DropsDegenerateAndDuplicateTriangles()
    {
        double[] v = { 0,0,0, 1,0,0, 1,1,0, 0,1,0 };
        int[] t =
        {
            0,1,2,   // good
            0,2,3,   // good (shares edge 0-2)
            0,1,2,   // duplicate of first
            1,1,2,   // degenerate (repeated index)
        };
        var (cv, ct) = ReconstructionCleanup.Clean(v, t);
        Assert(ct.Length / 3 == 2, $"degenerate + duplicate removed -> 2 tris; got {ct.Length / 3}");
    }

    public static void Translate_AddsCentroidBack()
    {
        double[] v = { 0,0,0, 1,2,3 };
        ReconstructionCleanup.Translate(v, 100.0, 200.0, 300.0);
        Assert(Math.Abs(v[0] - 100) < 1e-12 && Math.Abs(v[1] - 200) < 1e-12 && Math.Abs(v[2] - 300) < 1e-12, "vert0 translated");
        Assert(Math.Abs(v[3] - 101) < 1e-12 && Math.Abs(v[4] - 202) < 1e-12 && Math.Abs(v[5] - 303) < 1e-12, "vert1 translated");
    }

    public static void RecenterTranslate_RoundTrips_AtQuarryScale()
    {
        // recenter (GeometryNumerics) then Translate(centroid) must restore originals
        double[] v = { 466021.1, 6691584.7, 1.5,  466022.0, 6691585.0, 2.0,  466020.0, 6691584.0, 1.0 };
        var orig = (double[])v.Clone();
        var c = Frahan.Masonry.Geometry.GeometryNumerics.Recenter(v, out var ctr);
        ReconstructionCleanup.Translate(c, ctr.X, ctr.Y, ctr.Z);
        for (int i = 0; i < v.Length; i++)
        {
            double rel = Math.Abs(c[i] - orig[i]) / Math.Max(1.0, Math.Abs(orig[i]));
            Assert(rel < 1e-12, $"round-trip index {i}: {c[i]} vs {orig[i]}");
        }
    }

    public static void Clean_EmptyOrNull_IsSafe()
    {
        var (v1, t1) = ReconstructionCleanup.Clean(null, null);
        Assert(v1 != null && t1 != null && t1.Length == 0, "null -> empty, no throw");
        var (v2, t2) = ReconstructionCleanup.Clean(new double[0], new int[0]);
        Assert(t2.Length == 0, "empty -> empty");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ReconstructionCleanup: " + message);
    }
}
