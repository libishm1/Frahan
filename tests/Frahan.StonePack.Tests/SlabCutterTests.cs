#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;

namespace Frahan.Tests;

// Phase A.2 unit tests for the SlabCutter (convex polyhedral splitting along
// oriented fracture planes). All pure-managed; no Rhino runtime needed; should
// run as PASS on the headless host. Behavioural contract follows the public
// API in Frahan.Masonry.Cutting.SlabCutter.

static class SlabCutterTests
{
    // -- Single-plane: missing plane (passthrough) -------------------------

    public static void Cut_PlaneMissesSlab_ReturnsSingleSlabPassthrough()
    {
        var slab = MakeUnitCube();
        // Plane far above the cube on +Z; cube is fully below.
        var plane = new FracturePlane(0, 0, 2.0, 0, 0, 1);

        var result = SlabCutter.Cut(slab, plane);

        Assert(result != null, "result should not be null");
        Assert(result.Count == 1, $"expected 1 output slab, got {result.Count}");
        Assert(result.ParentIndices.Count == 1, "ParentIndices should have 1 entry");
        Assert(result.ParentIndices[0] == 0, "ParentIndices[0] should be 0");

        double inputVol = slab.SignedVolume();
        double outputVol = result.TotalVolume();
        Assert(Math.Abs(outputVol - inputVol) < 1e-7,
            $"volume mismatch: input={inputVol}, output={outputVol}");
    }

    // -- Single-plane: clean axis-aligned bisection ------------------------

    public static void Cut_AxisAlignedBisectingPlane_ProducesTwoEqualHalves()
    {
        var slab = MakeUnitCube();
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);

        var result = SlabCutter.Cut(slab, plane);

        Assert(result.Count == 2, $"expected 2 output slabs, got {result.Count}");

        double total = 0.0;
        for (int i = 0; i < result.Slabs.Count; i++)
        {
            double v = result.Slabs[i].SignedVolume();
            Assert(Math.Abs(v - 0.5) < 1e-7,
                $"piece {i} volume expected ~0.5, got {v}");
            total += v;
        }
        Assert(Math.Abs(total - 1.0) < 1e-7,
            $"total volume expected ~1.0, got {total}");
    }

    // -- Single-plane: diagonal bisection through centroid -----------------

    public static void Cut_DiagonalBisectingPlane_ProducesTwoEqualVolumes()
    {
        var slab = MakeUnitCube();
        // normal (1,1,0) constructor will normalise.
        var plane = new FracturePlane(0.5, 0.5, 0.5, 1, 1, 0);

        var result = SlabCutter.Cut(slab, plane);

        Assert(result.Count == 2, $"expected 2 output slabs, got {result.Count}");

        double v0 = result.Slabs[0].SignedVolume();
        double v1 = result.Slabs[1].SignedVolume();
        Assert(Math.Abs(v0 - 0.5) < 1e-6,
            $"diagonal piece 0 expected ~0.5, got {v0}");
        Assert(Math.Abs(v1 - 0.5) < 1e-6,
            $"diagonal piece 1 expected ~0.5, got {v1}");
        Assert(Math.Abs((v0 + v1) - 1.0) < 1e-6,
            $"diagonal total expected ~1.0, got {v0 + v1}");
    }

    // -- Single-plane: plane on a face (no degenerate sliver) --------------

    public static void Cut_PlaneOnFace_DoesNotProduceDegenerateSliver()
    {
        var slab = MakeUnitCube();
        // The cube's bottom face is at z=0. A plane there should yield only
        // the cube itself; nothing exists below z=0.
        var plane = new FracturePlane(0, 0, 0.0, 0, 0, 1);

        var result = SlabCutter.Cut(slab, plane);

        Assert(result.Count == 1,
            $"plane-on-face expected 1 slab (no zero-volume sliver), got {result.Count}");

        double inputVol = slab.SignedVolume();
        double outputVol = result.TotalVolume();
        Assert(Math.Abs(outputVol - inputVol) < 1e-7,
            $"plane-on-face volume mismatch: input={inputVol}, output={outputVol}");
    }

    // -- Multi-plane: 3 orthogonal bisectors -> 8 octants ------------------

    public static void Cut_ThreeOrthogonalBisectors_ProducesEightOctants()
    {
        var slab = MakeUnitCube();
        var planes = new FracturePlane[]
        {
            new FracturePlane(0.5, 0.5, 0.5, 1, 0, 0),
            new FracturePlane(0.5, 0.5, 0.5, 0, 1, 0),
            new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1),
        };

        var result = SlabCutter.Cut(slab, planes);

        Assert(result.Count == 8,
            $"three orthogonal bisectors should produce 8 octants, got {result.Count}");

        double total = 0.0;
        for (int i = 0; i < result.Slabs.Count; i++)
        {
            double v = result.Slabs[i].SignedVolume();
            Assert(Math.Abs(v - 0.125) < 1e-7,
                $"octant {i} volume expected ~0.125, got {v}");
            total += v;
        }
        Assert(Math.Abs(total - 1.0) < 1e-7,
            $"octant total expected ~1.0, got {total}");
    }

    // -- Argument validation -----------------------------------------------

    public static void Cut_NullSlab_Throws()
    {
        bool threw = false;
        var plane = new FracturePlane(0, 0, 0, 0, 0, 1);
        try
        {
            _ = SlabCutter.Cut((Slab)null, plane);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null slab should throw ArgumentNullException");
    }

    public static void Cut_NullPlane_Throws()
    {
        bool threw = false;
        var slab = MakeUnitCube();
        try
        {
            _ = SlabCutter.Cut(slab, (FracturePlane)null);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null plane should throw ArgumentNullException");
    }

    public static void Cut_NullPlaneList_Throws()
    {
        bool threw = false;
        var slab = MakeUnitCube();
        try
        {
            _ = SlabCutter.Cut(slab, (IReadOnlyList<FracturePlane>)null);
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null plane list should throw ArgumentNullException");
    }

    public static void Cut_NegativeEps_Throws()
    {
        bool threw = false;
        var slab = MakeUnitCube();
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);
        try
        {
            _ = SlabCutter.Cut(slab, plane, eps: -1.0);
        }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "negative eps should throw ArgumentOutOfRangeException");
    }

    // -- Multi-slab + single plane: parent-index provenance ----------------

    public static void Cut_MultiSlabSinglePlane_ParentIndicesRecorded()
    {
        var s0 = MakeUnitCube();
        // Translate the second cube along +X by 5 so the same-z plane still
        // bisects it cleanly into two halves.
        var s1 = MakeShiftedUnitCube(5.0, 0.0, 0.0);

        // Plane at z=0.5 bisects both cubes (they share the same Z range).
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);

        var result = SlabCutter.Cut(
            new Slab[] { s0, s1 },
            new FracturePlane[] { plane });

        Assert(result.Count == 4,
            $"two cubes each bisected -> 4 outputs, got {result.Count}");
        Assert(result.ParentIndices.Count == 4,
            $"ParentIndices count expected 4, got {result.ParentIndices.Count}");

        // Multiset match: sort and compare to {0,0,1,1}.
        var sorted = new List<int>(result.ParentIndices);
        sorted.Sort();
        Assert(sorted.Count == 4 && sorted[0] == 0 && sorted[1] == 0
            && sorted[2] == 1 && sorted[3] == 1,
            "ParentIndices multiset should be {0,0,1,1}");

        // Each pair sums to ~1.0 as a sanity check on the volume conservation.
        double total = result.TotalVolume();
        Assert(Math.Abs(total - 2.0) < 1e-7,
            $"total volume of two bisected cubes expected ~2.0, got {total}");
    }

    // -- Output integrity: vertex pool + face indices ----------------------

    public static void Cut_OutputSlab_VertexPoolIntegrity()
    {
        var slab = MakeUnitCube();
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);
        var result = SlabCutter.Cut(slab, plane);

        Assert(result.Count == 2, $"expected 2 halves, got {result.Count}");

        for (int i = 0; i < result.Slabs.Count; i++)
        {
            var s = result.Slabs[i];
            Assert(s.VertexCount * 3 == s.VertexCoordsXyz.Count,
                $"piece {i}: VertexCount*3 ({s.VertexCount * 3}) != VertexCoordsXyz.Count ({s.VertexCoordsXyz.Count})");

            int vc = s.VertexCount;
            for (int fi = 0; fi < s.Faces.Count; fi++)
            {
                var face = s.Faces[fi];
                Assert(face.Count >= 3,
                    $"piece {i} face {fi} has {face.Count} vertices; need >=3");
                for (int k = 0; k < face.Count; k++)
                {
                    int idx = face[k];
                    Assert(idx >= 0 && idx < vc,
                        $"piece {i} face {fi}[{k}] = {idx} out of range [0, {vc})");
                }
            }
        }
    }

    // -- Output integrity: ToMasonryBlock conversion -----------------------

    public static void Cut_OutputSlab_ToMasonryBlock_RoundTrips()
    {
        var slab = MakeUnitCube();
        var plane = new FracturePlane(0.5, 0.5, 0.5, 0, 0, 1);
        var result = SlabCutter.Cut(slab, plane);

        Assert(result.Count == 2, $"expected 2 halves, got {result.Count}");

        var half = result.Slabs[0];
        var block = half.ToMasonryBlock("test", 2400.0);

        Assert(block.Id == "test", "id round-trip");
        Assert(block.Density == 2400.0, "density round-trip");

        int triCount = block.TriangleIndices.Count;
        Assert(triCount % 3 == 0,
            $"triangle index list length {triCount} should be a multiple of 3");

        int vc = block.VertexCount;
        for (int i = 0; i < triCount; i++)
        {
            int idx = block.TriangleIndices[i];
            Assert(idx >= 0 && idx < vc,
                $"triangle index [{i}] = {idx} out of range [0, {vc})");
        }
    }

    // -- Helpers ------------------------------------------------------------

    private static Slab MakeUnitCube()
    {
        return Slab.Box(0, 0, 0, 1, 1, 1);
    }

    private static Slab MakeShiftedUnitCube(double dx, double dy, double dz)
    {
        return Slab.Box(dx, dy, dz, dx + 1.0, dy + 1.0, dz + 1.0);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
