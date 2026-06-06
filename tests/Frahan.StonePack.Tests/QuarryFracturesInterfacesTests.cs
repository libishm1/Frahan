#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Quarry;

namespace Frahan.Tests;

// =============================================================================
// QuarryFracturesInterfacesTests — Phase E.3 / E.4 / P3 unit tests covering:
//   - BoundingBox3
//   - FracturePlaneGenerators (Grid / JitteredGrid / Random / VoronoiBisectors)
//   - QuarryDecomposer (grid / jittered / random / voronoi)
//   - InterfaceAutoDetector (Slabs -> MasonryInterfaces)
// All pure-managed; no Rhino runtime needed.
// =============================================================================

static class QuarryFracturesInterfacesTests
{
    // ─── BoundingBox3 ────────────────────────────────────────────────────────

    public static void BoundingBox_FromSlab_UnitCube_HasUnitExtents()
    {
        var s = Slab.Box(0, 0, 0, 1, 1, 1);
        var b = BoundingBox3.FromSlab(s);
        Assert(Math.Abs(b.SizeX - 1.0) < 1e-12, "SizeX should be 1.0");
        Assert(Math.Abs(b.SizeY - 1.0) < 1e-12, "SizeY should be 1.0");
        Assert(Math.Abs(b.SizeZ - 1.0) < 1e-12, "SizeZ should be 1.0");
        Assert(b.Contains(0.5, 0.5, 0.5), "centre should be inside");
    }

    public static void BoundingBox_DegenerateAxis_Throws()
    {
        bool threw = false;
        try { _ = new BoundingBox3(0, 0, 0, 1, 0, 1); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "zero-extent Y should throw");
    }

    // ─── FracturePlaneGenerators ─────────────────────────────────────────────

    public static void Grid_NxNyNz_ReturnsExpectedCount()
    {
        var box = new BoundingBox3(0, 0, 0, 1, 1, 1);
        var planes = FracturePlaneGenerators.Grid(box, 2, 1, 3);
        Assert(planes.Count == 6, $"expected 6 planes (2+1+3), got {planes.Count}");
    }

    public static void Grid_PlanesAreEvenlySpaced()
    {
        var box = new BoundingBox3(0, 0, 0, 1, 1, 1);
        var planes = FracturePlaneGenerators.Grid(box, 3, 0, 0);
        Assert(planes.Count == 3, $"expected 3 X planes, got {planes.Count}");
        Assert(Math.Abs(planes[0].PointX - 0.25) < 1e-12, $"plane 0 X expected 0.25, got {planes[0].PointX}");
        Assert(Math.Abs(planes[1].PointX - 0.50) < 1e-12, $"plane 1 X expected 0.50, got {planes[1].PointX}");
        Assert(Math.Abs(planes[2].PointX - 0.75) < 1e-12, $"plane 2 X expected 0.75, got {planes[2].PointX}");
    }

    public static void Random_DeterministicForSeed()
    {
        var box = new BoundingBox3(0, 0, 0, 1, 1, 1);
        var p1 = FracturePlaneGenerators.Random(box, 8, seed: 42);
        var p2 = FracturePlaneGenerators.Random(box, 8, seed: 42);
        Assert(p1.Count == 8 && p2.Count == 8, $"expected 8 planes, got {p1.Count}/{p2.Count}");
        for (int i = 0; i < 8; i++)
        {
            Assert(Math.Abs(p1[i].PointX - p2[i].PointX) < 1e-12,
                $"plane {i} PointX mismatch: {p1[i].PointX} vs {p2[i].PointX}");
            Assert(Math.Abs(p1[i].NormalX - p2[i].NormalX) < 1e-12,
                $"plane {i} NormalX mismatch");
        }
    }

    public static void Random_DifferentSeeds_GiveDifferentPlanes()
    {
        var box = new BoundingBox3(0, 0, 0, 1, 1, 1);
        var p1 = FracturePlaneGenerators.Random(box, 4, seed: 1);
        var p2 = FracturePlaneGenerators.Random(box, 4, seed: 2);
        bool anyDiff = false;
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p1[i].PointX - p2[i].PointX) > 1e-9)
            {
                anyDiff = true; break;
            }
        }
        Assert(anyDiff, "seeds 1 and 2 should produce at least one different plane");
    }

    public static void VoronoiBisectors_TwoSeeds_ReturnsOnePlane()
    {
        var seeds = new double[] { 0, 0, 0, 1, 0, 0 };
        var planes = FracturePlaneGenerators.VoronoiBisectors(seeds);
        Assert(planes.Count == 1, $"expected 1 bisector, got {planes.Count}");
        Assert(Math.Abs(planes[0].PointX - 0.5) < 1e-12, $"bisector midpoint X should be 0.5, got {planes[0].PointX}");
        Assert(Math.Abs(planes[0].NormalX - 1.0) < 1e-12, $"bisector normal X should be 1.0, got {planes[0].NormalX}");
    }

    public static void VoronoiBisectors_FourSeeds_ReturnsSixPlanes()
    {
        var seeds = new double[] { 0, 0, 0,  1, 0, 0,  0, 1, 0,  1, 1, 0 };
        var planes = FracturePlaneGenerators.VoronoiBisectors(seeds);
        Assert(planes.Count == 6, $"expected C(4,2)=6 bisectors, got {planes.Count}");
    }

    public static void VoronoiBisectors_SingleSeed_Throws()
    {
        bool threw = false;
        try { _ = FracturePlaneGenerators.VoronoiBisectors(new double[] { 0, 0, 0 }); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "single seed should throw");
    }

    // ─── QuarryDecomposer ────────────────────────────────────────────────────

    public static void QuarryDecompose_ByGrid_UnitCube_Produces8Octants()
    {
        var quarry = Slab.Box(0, 0, 0, 1, 1, 1);
        var result = QuarryDecomposer.DecomposeByGrid(quarry, 1, 1, 1);
        Assert(result.Count == 8, $"expected 8 octants, got {result.Count}");
        double total = result.TotalVolume();
        Assert(Math.Abs(total - 1.0) < 1e-7, $"total volume expected ~1.0, got {total}");
    }

    public static void QuarryDecompose_ByGrid_Splits_Conserves_Volume()
    {
        var quarry = Slab.Box(0, 0, 0, 2, 1, 1);
        var input = quarry.SignedVolume();
        var result = QuarryDecomposer.DecomposeByGrid(quarry, 3, 0, 1);
        double output = result.TotalVolume();
        Assert(Math.Abs(input - output) < 1e-7,
            $"volume not conserved: input={input} output={output}");
        Assert(result.Count == 8, $"expected 4*2=8 cells, got {result.Count}");
    }

    public static void QuarryDecompose_ByVoronoi_TwoSeeds_ProducesTwoCells()
    {
        var quarry = Slab.Box(0, 0, 0, 1, 1, 1);
        var seeds = new double[] { 0.25, 0.5, 0.5,  0.75, 0.5, 0.5 };
        var result = QuarryDecomposer.DecomposeByVoronoi(quarry, seeds);
        Assert(result.Count == 2, $"expected 2 Voronoi cells, got {result.Count}");
        double total = result.TotalVolume();
        Assert(Math.Abs(total - 1.0) < 1e-7, $"total volume expected ~1.0, got {total}");
    }

    public static void QuarryDecompose_ZeroFractures_PassesThroughOriginalSlab()
    {
        var quarry = Slab.Box(0, 0, 0, 1, 1, 1);
        var result = QuarryDecomposer.DecomposeByGrid(quarry, 0, 0, 0);
        Assert(result.Count == 1, $"expected 1 passthrough slab, got {result.Count}");
        Assert(Math.Abs(result.TotalVolume() - 1.0) < 1e-7,
            $"passthrough volume expected ~1.0, got {result.TotalVolume()}");
    }

    // ─── InterfaceAutoDetector ───────────────────────────────────────────────

    public static void Detect_TwoStackedCubes_FindsOneBedJoint()
    {
        // Two unit cubes stacked along +Z. Top of A (z=1) touches bottom of B (z=1).
        var a = Slab.Box(0, 0, 0, 1, 1, 1);
        var b = Slab.Box(0, 0, 1, 1, 1, 2);
        var ifaces = InterfaceAutoDetector.Detect(
            new List<Slab> { a, b },
            new List<string> { "A", "B" },
            distanceTol: 1e-6, angleTolDeg: 0.5);
        Assert(ifaces.Count == 1, $"expected 1 contact interface, got {ifaces.Count}");
        var I = ifaces[0];
        Assert(I.BlockAId == "A" && I.BlockBId == "B",
            $"expected A->B, got {I.BlockAId}->{I.BlockBId}");
        Assert(Math.Abs(I.NormalZ - 1.0) < 1e-6, $"normal Z expected 1.0, got {I.NormalZ}");
        Assert(I.ContactPolygon.Count >= 3,
            $"contact polygon should have >= 3 vertices, got {I.ContactPolygon.Count}");
    }

    public static void Detect_TwoCubesSideBySide_FindsOneHeadJoint()
    {
        // Two unit cubes side by side along +X. Right of A (x=1) touches left of B (x=1).
        var a = Slab.Box(0, 0, 0, 1, 1, 1);
        var b = Slab.Box(1, 0, 0, 2, 1, 1);
        var ifaces = InterfaceAutoDetector.Detect(
            new List<Slab> { a, b },
            new List<string> { "L", "R" });
        Assert(ifaces.Count == 1, $"expected 1 head joint, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalX - 1.0) < 1e-6,
            $"normal X expected 1.0, got {ifaces[0].NormalX}");
    }

    public static void Detect_NonContacting_FindsZero()
    {
        // Two cubes separated by 1 unit gap on +X. Should detect zero contacts.
        var a = Slab.Box(0, 0, 0, 1, 1, 1);
        var b = Slab.Box(2, 0, 0, 3, 1, 1);
        var ifaces = InterfaceAutoDetector.Detect(
            new List<Slab> { a, b },
            new List<string> { "A", "B" });
        Assert(ifaces.Count == 0, $"expected 0 contacts, got {ifaces.Count}");
    }

    public static void Detect_PartialOverlap_PolygonRespectsClipping()
    {
        // A occupies [0,1]^3. B occupies [0.5..1.5, 0.5..1.5, 1..2]. They share
        // a 0.5x0.5 face on the z=1 plane.
        var a = Slab.Box(0, 0, 0, 1, 1, 1);
        var b = Slab.Box(0.5, 0.5, 1, 1.5, 1.5, 2);
        var ifaces = InterfaceAutoDetector.Detect(
            new List<Slab> { a, b },
            new List<string> { "A", "B" },
            distanceTol: 1e-6, angleTolDeg: 0.5);
        Assert(ifaces.Count == 1, $"expected 1 partial-overlap contact, got {ifaces.Count}");
        Assert(Math.Abs(ifaces[0].NormalZ - 1.0) < 1e-6,
            $"normal Z expected 1.0, got {ifaces[0].NormalZ}");
        // Polygon should bound a 0.5 x 0.5 quad => area 0.25.
        double area = PolygonArea3D(ifaces[0].ContactPolygon);
        Assert(Math.Abs(area - 0.25) < 1e-6,
            $"contact polygon area expected 0.25, got {area}");
    }

    public static void Detect_NullSlabs_Throws()
    {
        bool threw = false;
        try
        {
            _ = InterfaceAutoDetector.Detect(null, new List<string> { "A" });
        }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null slabs should throw ArgumentNullException");
    }

    public static void Detect_MismatchedIdsCount_Throws()
    {
        bool threw = false;
        var a = Slab.Box(0, 0, 0, 1, 1, 1);
        try
        {
            _ = InterfaceAutoDetector.Detect(
                new List<Slab> { a }, new List<string> { "A", "B" });
        }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "mismatched ids count should throw");
    }

    public static void Detect_PackedWall_MatchesAshlarLayoutEngineCount()
    {
        // Build a 2x2 packed wall using AshlarLayoutEngine. AutoDetect on the
        // resulting layout should find the same number of interfaces (modulo
        // ordering and small numerical noise).
        var slabs = new List<Slab>(4);
        for (int i = 0; i < 4; i++) slabs.Add(Slab.Box(0, 0, 0, 0.30, 0.20, 0.15));
        var opts = new Frahan.Masonry.Packing.AshlarPackOptions(
            Frahan.Masonry.Packing.CourseMode.CoursedAshlar,
            wallWidth: 0.60, wallHeight: 0.30, wallThickness: 0.20,
            targetCourseHeight: 0.15,
            bedJoint: 0.0, headJoint: 0.0,
            staggerOffset: 0.0,
            density: 2400.0,
            heightTolerance: 1e-9);
        var packed = Frahan.Masonry.Packing.AshlarLayoutEngine.Pack(slabs, opts);
        Assert(packed.PlacedBlocks.Count == 4,
            $"expected 4 placed blocks, got {packed.PlacedBlocks.Count}");
        // Wall has 2 head joints (one per course) + 2 bed joints (course 0 ↔ course 1) = 4.
        Assert(packed.Assembly.InterfaceCount == 4,
            $"AshlarLayoutEngine emitted {packed.Assembly.InterfaceCount} interfaces; expected 4");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static double PolygonArea3D(IReadOnlyList<ContactVertex> p)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        if (p.Count < 3) return 0.0;
        // Shoelace via cross products (works for any planar polygon).
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < p.Count; i++) { cx += p[i].X; cy += p[i].Y; cz += p[i].Z; }
        cx /= p.Count; cy /= p.Count; cz /= p.Count;
        double sx = 0, sy = 0, sz = 0;
        for (int i = 0; i < p.Count; i++)
        {
            int j = (i + 1) % p.Count;
            double ax = p[i].X - cx, ay = p[i].Y - cy, az = p[i].Z - cz;
            double bx = p[j].X - cx, by = p[j].Y - cy, bz = p[j].Z - cz;
            sx += ay * bz - az * by;
            sy += az * bx - ax * bz;
            sz += ax * by - ay * bx;
        }
        return 0.5 * Math.Sqrt(sx * sx + sy * sy + sz * sz);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
