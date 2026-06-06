#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptSolver -- brute-force search over (psi, dx, dy) for the maximum
// number of non-intersected blocks in a rectangular tested area, given a
// PLY fracture mesh.
//
// Implements:
//   - Phase 1.A scaffold: PLY ingest via existing PlyMeshReader.
//   - Phase 1.B: CuttingGrid generation per (psi, dx, dy).
//   - Phase 1.C: brute-force scalar recovery via Eq. 7-1 (kerf-aware).
//
// Future phases per `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md`:
//   - I2: AABB-tree pruning of fracture triangles per OBB.
//   - I3: coarse-to-fine angular search.
//   - I4: SIMD or Guigue-Devillers replacement of the SAT inner loop.
//   - I5-I9: sub-division, Pareto, mixed-size, Fisher-robust, Shao coupling.
// =============================================================================

public static class BlockCutOptSolver
{
    /// <summary>
    /// Run BlockCutOpt brute-force over the (psi, dx, dy) search space for
    /// each (mx, my) sub-zone independently. Returns one result per sub-zone.
    /// Phase 3 of the synthesis roadmap.
    /// </summary>
    public static IReadOnlyList<(SubZone Zone, BlockCutOptResult Result)> SolveSubdivided(
        BoundingBox3 testedArea,
        PlyMesh fractures,
        BlockCutOptOptions options,
        int mx,
        int my)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (mx < 1) throw new ArgumentOutOfRangeException(nameof(mx));
        if (my < 1) throw new ArgumentOutOfRangeException(nameof(my));

        var zones = SubdivisionPartition.Uniform(testedArea, mx, my);
        // build the BVH once and reuse across zones -- fracture geometry is shared
        var bvh = TriangleAabbBvh.Build(fractures);
        var output = new List<(SubZone, BlockCutOptResult)>(zones.Count);
        foreach (var z in zones)
        {
            var r = SolveInternal(z.Aabb, bvh, options);
            output.Add((z, r));
        }
        return output;
    }

    /// <summary>
    /// Run BlockCutOpt brute-force over the (psi, dx, dy) search space.
    /// Returns the (psi, dx, dy) that maximises the count of non-intersected
    /// kerf-inflated candidate blocks fully inside the tested area.
    /// </summary>
    /// <param name="testedArea">tested-area AABB.</param>
    /// <param name="fractures">PLY fracture mesh (one or many fractures fan-triangulated).</param>
    /// <param name="options">search parameters.</param>
    public static BlockCutOptResult Solve(
        BoundingBox3 testedArea,
        PlyMesh fractures,
        BlockCutOptOptions options,
        bool parallel = true)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var bvh = TriangleAabbBvh.Build(fractures);
        // parallel default; the serial core is the reference for the determinism/speedup test.
        return parallel ? SolveInternal(testedArea, bvh, options)
                        : SolveInternalSerial(testedArea, bvh, options);
    }

    /// <summary>
    /// Internal solver core (I3/P5 parallel). Enumerates the (psi,theta,phi,dx,dy) pose grid,
    /// computes each pose's non-intersected count in PARALLEL (every pose builds its own grid and
    /// only READS the immutable BVH), then takes a serial strict-greater argmax in the original
    /// enumeration order -> the chosen pose is BIT-IDENTICAL to SolveInternalSerial (ties go to
    /// the earliest pose) while the expensive BVH work runs across all cores. Determinism +
    /// equality validated headless in BlockCutOptParallelTests.
    /// </summary>
    internal static BlockCutOptResult SolveInternal(
        BoundingBox3 testedArea,
        TriangleAabbBvh bvh,
        BlockCutOptOptions options)
    {
        var sw = Stopwatch.StartNew();

        double thetaStep0 = options.ThetaStepRad > 0 ? options.ThetaStepRad : 1.0;
        double phiStep0 = options.PhiStepRad > 0 ? options.PhiStepRad : 1.0;
        double thetaLo0 = options.ThetaMaxRad > 0 ? -options.ThetaMaxRad : 0.0;
        double thetaHi0 = options.ThetaMaxRad > 0 ? options.ThetaMaxRad : 0.0;
        double phiLo0 = options.PhiMaxRad > 0 ? -options.PhiMaxRad : 0.0;
        double phiHi0 = options.PhiMaxRad > 0 ? options.PhiMaxRad : 0.0;

        // Enumerate poses in the EXACT serial loop order (so the argmax tie-break matches).
        var poses = new List<(double psi, double theta, double phi, double dx, double dy)>();
        for (double psi = options.PsiStartRad; psi <= options.PsiStopRad + 1e-9; psi += options.PsiStepRad)
        for (double theta = thetaLo0; theta <= thetaHi0 + 1e-9; theta += thetaStep0)
        for (double phi = phiLo0; phi <= phiHi0 + 1e-9; phi += phiStep0)
        for (double dx = -options.DxMax; dx <= options.DxMax + 1e-9; dx += options.DxStep)
        for (double dy = -options.DyMax; dy <= options.DyMax + 1e-9; dy += options.DyStep)
            poses.Add((psi, theta, phi, dx, dy));

        var counts = new int[poses.Count];
        Parallel.For(0, poses.Count, i =>
        {
            var pz = poses[i];
            var grid = CuttingGrid.GenerateTilted(
                testedArea, options.BlockSizeX, options.BlockSizeY, options.BlockSizeZ,
                options.Kerf, pz.psi, pz.theta, pz.phi, pz.dx, pz.dy);
            counts[i] = CountNonIntersected(grid, bvh);
        });

        int bestCount = -1;
        double bestPsi = options.PsiStartRad, bestTheta = 0.0, bestPhi = 0.0, bestDx = 0, bestDy = 0;
        for (int i = 0; i < poses.Count; i++)
        {
            if (counts[i] > bestCount)
            {
                bestCount = counts[i];
                bestPsi = poses[i].psi; bestTheta = poses[i].theta; bestPhi = poses[i].phi;
                bestDx = poses[i].dx; bestDy = poses[i].dy;
            }
        }
        long evals = poses.Count;

        double blockVolume0 = options.BlockSizeX * options.BlockSizeY * options.BlockSizeZ;
        double testedVolume0 = testedArea.SizeX * testedArea.SizeY * testedArea.SizeZ;
        double kerfVolume0 = ApproximateKerfVolume(testedArea, options);
        double recoveryDen0 = Math.Max(testedVolume0 - kerfVolume0, 1e-12);
        double recovery0 = bestCount * blockVolume0 / recoveryDen0 * 100.0;
        sw.Stop();
        return new BlockCutOptResult(bestCount, recovery0, bestPsi, bestTheta, bestPhi, bestDx, bestDy, evals, sw.Elapsed);
    }

    /// <summary>
    /// Serial reference (the original brute loop). Kept for the determinism/equality benchmark
    /// against the parallel SolveInternal. Not on the hot path.
    /// </summary>
    internal static BlockCutOptResult SolveInternalSerial(
        BoundingBox3 testedArea,
        TriangleAabbBvh bvh,
        BlockCutOptOptions options)
    {
        var sw = Stopwatch.StartNew();

        int bestCount = -1;
        double bestPsi = options.PsiStartRad;
        double bestTheta = 0.0, bestPhi = 0.0;
        double bestDx = 0, bestDy = 0;
        long evals = 0;

        // I1: theta and phi sweep. When ThetaMaxRad / PhiMaxRad = 0 the
        // inner loops collapse to a single 0-rad iteration.
        double thetaStep = options.ThetaStepRad > 0 ? options.ThetaStepRad : 1.0;
        double phiStep = options.PhiStepRad > 0 ? options.PhiStepRad : 1.0;
        double thetaLo = options.ThetaMaxRad > 0 ? -options.ThetaMaxRad : 0.0;
        double thetaHi = options.ThetaMaxRad > 0 ? options.ThetaMaxRad : 0.0;
        double phiLo = options.PhiMaxRad > 0 ? -options.PhiMaxRad : 0.0;
        double phiHi = options.PhiMaxRad > 0 ? options.PhiMaxRad : 0.0;

        for (double psi = options.PsiStartRad; psi <= options.PsiStopRad + 1e-9; psi += options.PsiStepRad)
        for (double theta = thetaLo; theta <= thetaHi + 1e-9; theta += thetaStep)
        for (double phi = phiLo; phi <= phiHi + 1e-9; phi += phiStep)
        {
            for (double dx = -options.DxMax; dx <= options.DxMax + 1e-9; dx += options.DxStep)
            {
                for (double dy = -options.DyMax; dy <= options.DyMax + 1e-9; dy += options.DyStep)
                {
                    evals++;
                    var grid = CuttingGrid.GenerateTilted(
                        testedArea,
                        options.BlockSizeX, options.BlockSizeY, options.BlockSizeZ,
                        options.Kerf, psi, theta, phi, dx, dy);

                    int count = CountNonIntersected(grid, bvh);
                    if (count > bestCount)
                    {
                        bestCount = count;
                        bestPsi = psi;
                        bestTheta = theta;
                        bestPhi = phi;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }
        }

        double blockVolume = options.BlockSizeX * options.BlockSizeY * options.BlockSizeZ;
        double testedVolume = testedArea.SizeX * testedArea.SizeY * testedArea.SizeZ;
        double kerfVolume = ApproximateKerfVolume(testedArea, options);
        double recoveryDen = Math.Max(testedVolume - kerfVolume, 1e-12);
        double recovery = bestCount * blockVolume / recoveryDen * 100.0;

        sw.Stop();
        return new BlockCutOptResult(
            bestCount, recovery, bestPsi, bestTheta, bestPhi,
            bestDx, bestDy, evals, sw.Elapsed);
    }

    /// <summary>
    /// Convenience overload: run <see cref="Solve"/> and additionally return
    /// the non-intersected OrientedBlocks at the winning (psi, theta, phi,
    /// dx, dy). Useful for callers that need the actual block geometry (not
    /// just the count + recovery percent). Re-generates the grid once at the
    /// winning parameters and filters by intersection.
    /// </summary>
    public static (BlockCutOptResult Result, System.Collections.Generic.IReadOnlyList<OrientedBlock> Grid) SolveAndExtract(
        BoundingBox3 testedArea,
        PlyMesh fractures,
        BlockCutOptOptions options)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var bvh = TriangleAabbBvh.Build(fractures);
        var result = SolveInternal(testedArea, bvh, options);

        var bestGrid = CuttingGrid.GenerateTilted(
            testedArea,
            options.BlockSizeX, options.BlockSizeY, options.BlockSizeZ,
            options.Kerf,
            result.BestPsiRad, result.BestThetaRad, result.BestPhiRad,
            result.BestDx, result.BestDy);

        var kept = new System.Collections.Generic.List<OrientedBlock>(bestGrid.Count);
        for (int i = 0; i < bestGrid.Count; i++)
        {
            var obb = bestGrid[i];
            if (!bvh.AnyTriangleIntersects(in obb)) kept.Add(obb);
        }
        return (result, kept);
    }

    private static int CountNonIntersected(
        System.Collections.Generic.IReadOnlyList<OrientedBlock> grid,
        TriangleAabbBvh bvh)
    {
        int count = 0;
        for (int b = 0; b < grid.Count; b++)
        {
            var obb = grid[b];
            if (!bvh.AnyTriangleIntersects(in obb)) count++;
        }
        return count;
    }

    private static double ApproximateKerfVolume(BoundingBox3 area, BlockCutOptOptions o)
    {
        // approximate kerf volume as a uniform thin film of thickness ~ kerf/2
        // across the tested area's footprint. Good enough for the recovery
        // denominator in Phase 1; refined in Phase 3 alongside sub-division.
        if (o.Kerf <= 0) return 0.0;
        return area.SizeX * area.SizeY * (o.Kerf * 0.5);
    }
}
