#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.GH.TwoD;
using Rhino.Geometry;

namespace Frahan.Tests;

// Smoke + helper tests for the Trencadís solver (F-2D-002).
// Pure-managed where possible (CvdLloyd2d, Gvf2d). Component smoke tests
// require Grasshopper.dll → SKIP outside Rhino runtime.

static class TrencadisFillTests
{
    // ─── Component smoke tests ───────────────────────────────────────────

    public static void TrencadisComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new Pack2DTrencadisComponent();
        var expected = new Guid("F2D00002-CADC-4F2D-9001-7E60CADA15A0");
        Assert(c.ComponentGuid == expected,
            $"ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void TrencadisComponent_Metadata_IsCorrect()
    {
        var c = new Pack2DTrencadisComponent();
        Assert(c.Name == "Frahan Trencadís Pack",
            $"Name should be 'Frahan Trencadís Pack', got '{c.Name}'");
        Assert(c.NickName == "Trencadis",
            $"NickName should be 'Trencadis', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Trencadis",
            $"SubCategory should be 'Trencadis', got '{c.SubCategory}'");
    }

    public static void TrencadisComponent_HasExpectedInputAndOutputCount()
    {
        var c = new Pack2DTrencadisComponent();
        // 17 inputs: Parts, Sheet Outlines, Sheet Holes, Spacing, Rotations,
        // Tolerance, Seed, Run, Max Candidates, Trim Tolerance, Grout,
        // Boundary Mode, Min Boundary Affinity, Cut Budget, Use CVD Seeds,
        // Use GVF Orientation, GVF Smoothness.
        Assert(c.Params.Input.Count == 17,
            $"Input count should be 17, got {c.Params.Input.Count}");
        // 10 outputs: Trencadís Pieces, Pre-Trim Pieces, Transforms,
        // Source Indices, Sheet Indices, Trim Adjacency, Unplaced,
        // Failure Reasons, Sheet Preview, Report.
        Assert(c.Params.Output.Count == 10,
            $"Output count should be 10, got {c.Params.Output.Count}");
    }

    public static void TrencadisComponent_NewInputs_HaveExpectedNames()
    {
        var c = new Pack2DTrencadisComponent();
        Assert(c.Params.Input[13].Name == "Cut Budget",
            $"Input 13 should be 'Cut Budget', got '{c.Params.Input[13].Name}'");
        Assert(c.Params.Input[14].Name == "Use CVD Seeds",
            $"Input 14 should be 'Use CVD Seeds', got '{c.Params.Input[14].Name}'");
        Assert(c.Params.Input[15].Name == "Use GVF Orientation",
            $"Input 15 should be 'Use GVF Orientation', got '{c.Params.Input[15].Name}'");
        Assert(c.Params.Input[16].Name == "GVF Smoothness",
            $"Input 16 should be 'GVF Smoothness', got '{c.Params.Input[16].Name}'");
    }

    // ─── CvdLloyd2d (pure managed) ───────────────────────────────────────

    public static void CvdLloyd_GenerateSeeds_SquareDomain_AllInside()
    {
        // 10×10 square.
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var seeds = CvdLloyd2d.GenerateSeeds(vx, vy,
            new List<(double[], double[])>(),
            0, 0, 10, 10, K: 16, iterations: 10, gridRes: 32, seed: 42);
        Assert(seeds.Count == 16, $"Expected 16 seeds, got {seeds.Count}");
        foreach (var (x, y) in seeds)
        {
            Assert(x > 0 && x < 10, $"Seed x={x} out of range");
            Assert(y > 0 && y < 10, $"Seed y={y} out of range");
        }
    }

    public static void CvdLloyd_GenerateSeeds_DeterministicForSeed()
    {
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var a = CvdLloyd2d.GenerateSeeds(vx, vy, new List<(double[], double[])>(),
            0, 0, 10, 10, K: 8, iterations: 10, gridRes: 32, seed: 7);
        var b = CvdLloyd2d.GenerateSeeds(vx, vy, new List<(double[], double[])>(),
            0, 0, 10, 10, K: 8, iterations: 10, gridRes: 32, seed: 7);
        Assert(a.Count == b.Count, "Seed counts differ");
        for (int i = 0; i < a.Count; i++)
        {
            Assert(Math.Abs(a[i].x - b[i].x) < 1e-9, $"Seed[{i}].x differs: {a[i].x} vs {b[i].x}");
            Assert(Math.Abs(a[i].y - b[i].y) < 1e-9, $"Seed[{i}].y differs: {a[i].y} vs {b[i].y}");
        }
    }

    public static void CvdLloyd_GenerateSeeds_LloydMovesToUniform()
    {
        // 10×10 square, K=4. After Lloyd convergence, seeds should be
        // roughly at the four quadrant centres: (2.5, 2.5), (7.5, 2.5),
        // (2.5, 7.5), (7.5, 7.5).
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var seeds = CvdLloyd2d.GenerateSeeds(vx, vy, new List<(double[], double[])>(),
            0, 0, 10, 10, K: 4, iterations: 50, gridRes: 64, seed: 3);
        Assert(seeds.Count == 4, $"Expected 4 seeds, got {seeds.Count}");
        // Each seed should be near a quadrant centre (within 1.5 units).
        var expected = new (double x, double y)[]
        {
            (2.5, 2.5), (7.5, 2.5), (2.5, 7.5), (7.5, 7.5)
        };
        foreach (var s in seeds)
        {
            var minDist = double.MaxValue;
            foreach (var e in expected)
            {
                var d = Math.Sqrt((s.x - e.x) * (s.x - e.x) + (s.y - e.y) * (s.y - e.y));
                if (d < minDist) minDist = d;
            }
            Assert(minDist < 1.5, $"Seed ({s.x}, {s.y}) > 1.5 from any quadrant centre");
        }
    }

    public static void CvdLloyd_GenerateSeeds_HoleExcluded()
    {
        // 10×10 square with a 4×4 hole at (3,3)-(7,7). All seeds must
        // be inside the square AND outside the hole.
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var hvx = new double[] { 3, 7, 7, 3 };
        var hvy = new double[] { 3, 3, 7, 7 };
        var holes = new List<(double[], double[])> { (hvx, hvy) };
        var seeds = CvdLloyd2d.GenerateSeeds(vx, vy, holes,
            0, 0, 10, 10, K: 12, iterations: 20, gridRes: 64, seed: 11);
        Assert(seeds.Count == 12, $"Expected 12 seeds, got {seeds.Count}");
        foreach (var (x, y) in seeds)
        {
            var inHole = x > 3 && x < 7 && y > 3 && y < 7;
            Assert(!inHole, $"Seed ({x}, {y}) inside hole");
        }
    }

    // ─── Gvf2d (pure managed) ────────────────────────────────────────────

    public static void Gvf_Compute_DegenerateInput_ReturnsEmptyField()
    {
        var field = Gvf2d.Compute(null, null, null, 0, 0, 1, 1);
        Assert(field.GridX == 0 && field.GridY == 0, "Expected empty field for null input");
    }

    public static void Gvf_Sample_OutsideBbox_ReturnsZero()
    {
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var f = Gvf2d.Compute(vx, vy, new List<(double[], double[])>(),
            0, 0, 10, 10, gridRes: 16, mu: 0.2, iterations: 10);
        var (u, w) = f.Sample(-1, -1);
        Assert(u == 0 && w == 0, $"Expected (0, 0) outside bbox, got ({u}, {w})");
        var (u2, w2) = f.Sample(11, 11);
        Assert(u2 == 0 && w2 == 0, $"Expected (0, 0) outside bbox, got ({u2}, {w2})");
    }

    public static void Gvf_Sample_InsideDomain_NonZero()
    {
        // Field magnitude should be non-trivial somewhere inside the domain.
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var f = Gvf2d.Compute(vx, vy, new List<(double[], double[])>(),
            0, 0, 10, 10, gridRes: 24, mu: 0.2, iterations: 50);
        var sumMag = 0.0;
        for (var x = 1.0; x <= 9.0; x += 1.0)
        for (var y = 1.0; y <= 9.0; y += 1.0)
        {
            var (u, w) = f.Sample(x, y);
            sumMag += Math.Sqrt(u * u + w * w);
        }
        Assert(sumMag > 1e-6, $"GVF field magnitude near zero everywhere: {sumMag}");
    }

    public static void Gvf_OrientationDeg_InRange0to180()
    {
        var vx = new double[] { 0, 10, 10, 0 };
        var vy = new double[] { 0, 0, 10, 10 };
        var f = Gvf2d.Compute(vx, vy, new List<(double[], double[])>(),
            0, 0, 10, 10, gridRes: 24, mu: 0.2, iterations: 30);
        var sample = f.OrientationDeg(5, 5);
        if (sample.HasValue)
        {
            var d = sample.Value;
            Assert(d >= 0 && d < 180, $"Orientation {d} out of [0, 180)");
        }
        // Outside domain: should be null.
        var outside = f.OrientationDeg(-1, -1);
        Assert(outside == null, $"Expected null orientation outside domain, got {outside}");
    }

    // ─── Boundary-mode construction smoke (Rhino) ───────────────────────

    public static void TrencadisFill_BoundaryMode1_Construction_DoesNotThrow()
    {
        var solver = new TrencadisFill(
            new[] { Rect(0, 0, 10, 10) },
            new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() },
            spacing: 0.05, rotationsDeg: new[] { 0.0, 45.0, 90.0, 135.0 },
            tolerance: 0.01, seed: 0, maxCandidates: 100,
            trimTolerance: 0.1, grout: 0.0,
            boundaryMode: 1, minBoundaryAffinity: 0.3);
        Assert(solver != null, "ctor should succeed with boundaryMode=1");
    }

    public static void TrencadisFill_BoundaryMode2_Construction_DoesNotThrow()
    {
        var solver = new TrencadisFill(
            new[] { Rect(0, 0, 100, 100) },
            new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() },
            spacing: 0.5, rotationsDeg: new[] { 0.0, 45.0, 90.0, 135.0 },
            tolerance: 0.01, seed: 0, maxCandidates: 100,
            trimTolerance: 0.5, grout: 0.0,
            boundaryMode: 2, minBoundaryAffinity: 0.3);
        Assert(solver != null, "ctor should succeed with boundaryMode=2");
    }

    public static void TrencadisFill_BoundaryMode3_Construction_DoesNotThrow()
    {
        var solver = new TrencadisFill(
            new[] { Rect(0, 0, 100, 100) },
            new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() },
            spacing: 0.5, rotationsDeg: new[] { 0.0 },
            tolerance: 0.01, seed: 0, maxCandidates: 200,
            trimTolerance: 0.5, grout: 0.0,
            boundaryMode: 3, minBoundaryAffinity: 0.3);
        Assert(solver != null, "ctor should succeed with boundaryMode=3");
    }

    public static void TrencadisFill_BoundaryMode2_PacksWithoutCrash()
    {
        // 100×100 sheet + 3 small parts with at least one strip-shaped
        // boundary-worthy part. Verify the solver runs end-to-end and
        // places at least one part.
        var sheets = new List<Curve> { Rect(0, 0, 100, 100) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        var parts = new List<Curve>
        {
            Rect(0, 0, 80, 5),
            Rect(0, 0, 25, 25),
            Rect(0, 0, 20, 20),
        };
        var solver = new TrencadisFill(
            sheets, holes,
            spacing: 0.5, rotationsDeg: new[] { 0.0, 90.0 },
            tolerance: 0.01, seed: 0, maxCandidates: 200,
            trimTolerance: 0.5, grout: 0.0,
            boundaryMode: 2, minBoundaryAffinity: 0.3,
            cutBudget: 0.35, useCvdSeeds: false, useGvf: false);
        var res = solver.Pack(parts);
        Assert(res.PackedCurves.Count >= 1,
            $"expected at least one placement, got {res.PackedCurves.Count}");
    }

    public static void TrencadisFill_BoundaryMode3_PacksOnRing()
    {
        // Mode 3 should place parts AROUND the boundary curve.
        var sheets = new List<Curve> { Rect(0, 0, 100, 100) };
        var holes = new List<IReadOnlyList<Curve>> { Array.Empty<Curve>() };
        var parts = new List<Curve>
        {
            Rect(0, 0, 10, 5), Rect(0, 0, 10, 5), Rect(0, 0, 10, 5),
            Rect(0, 0, 10, 5), Rect(0, 0, 10, 5), Rect(0, 0, 10, 5),
        };
        var solver = new TrencadisFill(
            sheets, holes,
            spacing: 0.5, rotationsDeg: new[] { 0.0 },
            tolerance: 0.01, seed: 0, maxCandidates: 200,
            trimTolerance: 0.5, grout: 0.0,
            boundaryMode: 3, minBoundaryAffinity: 0.3,
            cutBudget: 0.35, useCvdSeeds: false, useGvf: false);
        var res = solver.Pack(parts);
        // Mode 3 distributes uniformly along arc length, expect most to land.
        Assert(res.PackedCurves.Count >= 4,
            $"expected at least 4 of 6 ring placements, got {res.PackedCurves.Count}");
    }

    // ─── Hungarian (F-2D-002.F7, pure managed) ──────────────────────────

    public static void Hungarian_2x2_OptimalAssignment()
    {
        // cost[i,j]: row 0 prefers col 1; row 1 prefers col 0.
        var cost = new double[,] { { 5, 1 }, { 1, 5 } };
        var assign = HungarianAssignmentForTest(cost);
        Assert(assign[0] == 1 && assign[1] == 0,
            $"Expected (1,0) got ({assign[0]},{assign[1]})");
    }

    public static void Hungarian_3x3_KnownOptimum()
    {
        // Classic textbook example: Bourgeois-Lassalle 1971.
        // cost matrix:
        //   1 2 3
        //   2 4 6
        //   3 6 9
        // Optimum: (0->2)+(1->1)+(2->0) = 3+4+3 = 10
        // BUT (0->0)+(1->1)+(2->2) = 1+4+9 = 14  worse
        //     (0->2)+(1->0)+(2->1) = 3+2+6 = 11  worse
        // Confirm Hungarian finds the (2,1,0) permutation cost=10.
        var cost = new double[,] { { 1, 2, 3 }, { 2, 4, 6 }, { 3, 6, 9 } };
        var assign = HungarianAssignmentForTest(cost);
        double sum = 0;
        for (int i = 0; i < 3; i++) sum += cost[i, assign[i]];
        Assert(Math.Abs(sum - 10.0) < 1e-9,
            $"Expected optimum cost 10, got {sum} (assignment ({assign[0]},{assign[1]},{assign[2]}))");
    }

    public static void Hungarian_4x4_Permutation()
    {
        // Diagonal-zero matrix: optimum is identity, cost = 0.
        var cost = new double[,]
        {
            { 0, 1, 2, 3 },
            { 1, 0, 1, 2 },
            { 2, 1, 0, 1 },
            { 3, 2, 1, 0 },
        };
        var assign = HungarianAssignmentForTest(cost);
        for (int i = 0; i < 4; i++)
            Assert(assign[i] == i, $"Identity expected, got assign[{i}]={assign[i]}");
    }

    // Internal class is internal to the GH project. Use reflection to invoke
    // for the test, since the test project references the GH assembly.
    private static int[] HungarianAssignmentForTest(double[,] cost)
    {
        var t = typeof(Pack2DTrencadisCatalogComponent).Assembly
            .GetType("Frahan.GH.TwoD.HungarianAssignment");
        Assert(t != null, "HungarianAssignment type not found in GH assembly");
        var m = t.GetMethod("Solve",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert(m != null, "Solve method not found");
        return (int[])m.Invoke(null, new object[] { cost });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    static Curve Rect(double x, double y, double w, double h)
    {
        var poly = new Polyline(new[]
        {
            new Point3d(x, y, 0),
            new Point3d(x + w, y, 0),
            new Point3d(x + w, y + h, 0),
            new Point3d(x, y + h, 0),
            new Point3d(x, y, 0),
        });
        return poly.ToNurbsCurve();
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
