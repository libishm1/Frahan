#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptDemo -- end-to-end usage example, the "how to consume the library"
// reference. Documents the canonical pipeline for the Tamil Nadu granite
// use case in `D:\code_ws\wiki\papers\equations_and_diagrams\09_dataset_reproduction_report.md`
// section 11.
//
// Pipeline:
//   1. Define a tested-area AABB in metres.
//   2. Choose a source of fracture traces:
//        a. CSV from disk (manual digitisation or external CNN output).
//        b. Synthetic DFN via JointSetDfnGenerator.
//        c. Python subprocess detector (GeoFractNet, etc.).
//   3. Vertical-extrude or DFN-emit the traces into a PLY.
//   4. Configure a BlockCutOptOptions with paper-default tolerances.
//   5. Run OmniSolver (sub-division + Pareto + BCSdbBV).
//   6. Write a ParaView VTU for visual validation.
//   7. (optional) Run AmrrPlanner on the best non-intersected block.
//
// Pure managed; no Rhino or Grasshopper references. Drop the result VTUs into
// ParaView to inspect.
// =============================================================================

public static class BlockCutOptDemo
{
    /// <summary>
    /// Run a complete BlockCutOpt v2 pipeline against a CSV of fracture
    /// traces. Writes one VTU per sub-zone and returns the aggregate result.
    /// </summary>
    public static OmniSolveResult RunCsvDrivenDemo(
        string csvPath,
        BoundingBox3 testedArea,
        BlockCutOptOptions search = null,
        int mx = 1, int my = 1,
        string vtuOutputDir = null)
    {
        if (string.IsNullOrWhiteSpace(csvPath)) throw new ArgumentException(nameof(csvPath));
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        search = search ?? BlockCutOptOptions.LimestoneStratumA();

        // Step 2a + 3: ingest traces + extrude to PLY
        var traces = CsvFractureTraceSource.ReadCsv(csvPath);
        var asTuples = new List<(double X1, double Y1, double X2, double Y2)>(traces.Count);
        for (int i = 0; i < traces.Count; i++)
        {
            var t = traces[i];
            asTuples.Add((t.X1, t.Y1, t.X2, t.Y2));
        }
        var ply = TraceVerticalExtruder.Extrude(asTuples, testedArea.MinZ, testedArea.MaxZ);

        return RunPlyDrivenDemo(ply, testedArea, search, mx, my, vtuOutputDir);
    }

    /// <summary>
    /// Run a complete pipeline against a synthetic DFN drawn from a list of
    /// joint sets. Convenient for offline testing.
    /// </summary>
    public static OmniSolveResult RunSyntheticDfnDemo(
        IReadOnlyList<JointSet> jointSets,
        BoundingBox3 testedArea,
        int seed = 42,
        BlockCutOptOptions search = null,
        int mx = 1, int my = 1,
        string vtuOutputDir = null)
    {
        if (jointSets == null) throw new ArgumentNullException(nameof(jointSets));
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        search = search ?? BlockCutOptOptions.LimestoneStratumA();

        var planes = JointSetDfnGenerator.Generate(jointSets, testedArea, seed);
        var ply = JointSetDfnPlyEmitter.Emit(planes, testedArea);
        return RunPlyDrivenDemo(ply, testedArea, search, mx, my, vtuOutputDir);
    }

    /// <summary>Core entrypoint shared by both demos.</summary>
    public static OmniSolveResult RunPlyDrivenDemo(
        PlyMesh ply,
        BoundingBox3 testedArea,
        BlockCutOptOptions search,
        int mx, int my,
        string vtuOutputDir)
    {
        var omniOpts = new OmniSolverOptions
        {
            Search = search,
            SubdivMode = SubdivisionMode.Uniform,
            Mx = mx, My = my,
        };
        var result = BlockCutOptOmniSolver.Solve(testedArea, ply, omniOpts);

        if (!string.IsNullOrEmpty(vtuOutputDir))
        {
            Directory.CreateDirectory(vtuOutputDir);
            var bvh = TriangleAabbBvh.Build(ply);
            foreach (var zr in result.PerZone)
            {
                var best = zr.Front.BestRecovery();
                var grid = CuttingGrid.Generate(
                    zr.Zone.Aabb,
                    search.BlockSizeX, search.BlockSizeY, search.BlockSizeZ,
                    search.Kerf, best.PsiRad, best.Dx, best.Dy);
                string vtuPath = Path.Combine(vtuOutputDir,
                    $"zone_{zr.Zone.I}_{zr.Zone.J}.vtu");
                VtuWriter.WriteFromGridAndBvh(vtuPath, grid, bvh);
            }
        }

        return result;
    }

    /// <summary>
    /// Couple a single best non-intersected block to a Shao 2022 AMRR
    /// plane-sequence cut toward a bounding sphere. Demonstrates the
    /// quarry-to-in-block scale handoff (Phase 9, improvement I9).
    /// </summary>
    public static AmrrPlanResult CoupleToAmrrAtBestBlock(
        OmniSolveResult quarryResult,
        BlockCutOptOptions search,
        double targetSphereFraction = 0.45,
        AmrrPlannerOptions amrrOpts = null,
        string amrrVtuPath = null)
    {
        if (quarryResult == null) throw new ArgumentNullException(nameof(quarryResult));
        if (search == null) throw new ArgumentNullException(nameof(search));
        if (quarryResult.PerZone.Count == 0)
            throw new InvalidOperationException("quarry result has no zones");

        var firstZone = quarryResult.PerZone[0];
        var best = firstZone.Front.BestRecovery();
        var grid = CuttingGrid.Generate(
            firstZone.Zone.Aabb,
            search.BlockSizeX, search.BlockSizeY, search.BlockSizeZ,
            search.Kerf, best.PsiRad, best.Dx, best.Dy);
        if (grid.Count == 0) throw new InvalidOperationException("empty cutting grid");

        var obb = grid[0];
        var blank = ConvexPolyhedron.FromOrientedBlock(in obb);
        double r = targetSphereFraction * Math.Min(obb.HalfX, Math.Min(obb.HalfY, obb.HalfZ));
        var plan = AmrrPlanner.PlanBoundingSphere(
            blank, obb.CenterX, obb.CenterY, obb.CenterZ, r, amrrOpts);

        if (!string.IsNullOrEmpty(amrrVtuPath))
        {
            VtuWriter.WriteAmrrSequence(amrrVtuPath, plan,
                quadSizeMetres: Math.Min(obb.HalfX, obb.HalfY));
        }
        return plan;
    }
}
