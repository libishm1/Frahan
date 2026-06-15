#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptOmniSolver -- single entry point that glues together every
// improvement shipped so far:
//
//   - Phase 3 (sub-division)           by SubdivisionPartition.Uniform or
//                                          DensityWatershedPartition.Partition
//   - Phase 4 (coarse-to-fine search)  Pareto-aware 12->3->0.5 deg refine
//                                          (SolveZoneCoarseToFine, top-K seeded)
//   - Phase 6 (Pareto multi-objective) via BlockCutOptParetoSolver per zone
//   - Phase 8 (Fisher-robust)          available as an optional wrapper
//   - I11 (BCSdbBV cost axis)          included in the Pareto front
//
// Each sub-zone returns its own 4-axis Pareto front; the omni-solver also
// reports the aggregate "BestRecovery", "BestRevenue", and "BestBcsdbBv"
// rolled up across zones, plus the elapsed time and total evaluation count.
// =============================================================================

public sealed class OmniZoneResult
{
    public OmniZoneResult(SubZone zone, ParetoFront front, long evaluations)
    {
        Zone = zone;
        Front = front;
        Evaluations = evaluations;
    }

    public SubZone Zone { get; }
    public ParetoFront Front { get; }
    public long Evaluations { get; }

    public ParetoPoint BestRecovery => Front.BestRecovery();
    public ParetoPoint BestRevenue => Front.BestRevenue();
    public ParetoPoint BestBcsdbBv => Front.BestBcsdbBv();
}

public sealed class OmniSolveResult
{
    public OmniSolveResult(
        IReadOnlyList<OmniZoneResult> perZone,
        long totalEvaluations,
        TimeSpan elapsed)
    {
        PerZone = perZone;
        TotalEvaluations = totalEvaluations;
        Elapsed = elapsed;
    }

    public IReadOnlyList<OmniZoneResult> PerZone { get; }
    public long TotalEvaluations { get; }
    public TimeSpan Elapsed { get; }

    /// <summary>Sum of recovery counts across all sub-zones (best per zone).</summary>
    public int AggregateRecoveryCount
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < PerZone.Count; i++) sum += PerZone[i].BestRecovery.RecoveryCount;
            return sum;
        }
    }

    /// <summary>Sum of revenues across all sub-zones (best per zone).</summary>
    public double AggregateRevenue
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < PerZone.Count; i++) sum += PerZone[i].BestRevenue.Revenue;
            return sum;
        }
    }

    public override string ToString() =>
        $"OmniSolveResult(zones={PerZone.Count}, R_agg={AggregateRecoveryCount}, " +
        $"Pi_agg={AggregateRevenue:0.000}, evals={TotalEvaluations}, " +
        $"elapsed={Elapsed.TotalMilliseconds:0} ms)";
}

public enum SubdivisionMode
{
    Uniform,
    DensityWatershed,
}

public sealed class OmniSolverOptions
{
    public BlockCutOptOptions Search { get; set; }
    public BlockValueModel ValueModel { get; set; } = BlockValueModel.Default;
    public SubdivisionMode SubdivMode { get; set; } = SubdivisionMode.Uniform;
    public int Mx { get; set; } = 1;
    public int My { get; set; } = 1;

    /// <summary>Bandwidth for the density-watershed mode (metres).</summary>
    public double WatershedBandwidth { get; set; } = 5.0;

    /// <summary>When true, uses coarse-to-fine angular search within each zone instead of the uniform sweep.</summary>
    public bool UseCoarseToFine { get; set; } = true;

    public double CoarseStepRad { get; set; } = BlockCutOptTolerances.PsiStepCoarseRad;
    public double MediumStepRad { get; set; } = BlockCutOptTolerances.PsiStepMediumRad;
    public double FineStepRad { get; set; } = BlockCutOptTolerances.PsiStepFineRad;
    public int CoarseToFineTopK { get; set; } = 3;
}

public static class BlockCutOptOmniSolver
{
    public static OmniSolveResult Solve(
        BoundingBox3 testedArea,
        PlyMesh fractures,
        OmniSolverOptions options,
        IReadOnlyList<FracturePlane> watershedPlanes = null)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (fractures == null) throw new ArgumentNullException(nameof(fractures));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (options.Search == null) throw new ArgumentException("Search options must be set");

        var sw = Stopwatch.StartNew();

        IReadOnlyList<SubZone> zones;
        if (options.SubdivMode == SubdivisionMode.DensityWatershed)
        {
            if (watershedPlanes == null)
                throw new ArgumentException("watershedPlanes required for DensityWatershed mode");
            zones = DensityWatershedPartition.Partition(testedArea, watershedPlanes, options.WatershedBandwidth);
        }
        else
        {
            zones = SubdivisionPartition.Uniform(testedArea, options.Mx, options.My);
        }

        var perZone = new List<OmniZoneResult>(zones.Count);
        long totalEvals = 0;

        foreach (var zone in zones)
        {
            ParetoFront front;
            long zoneEvals;

            if (options.UseCoarseToFine)
            {
                (front, zoneEvals) = SolveZoneCoarseToFine(zone.Aabb, fractures, options);
            }
            else
            {
                var (frontInternal, evals, _) =
                    BlockCutOptParetoSolver.Solve(zone.Aabb, fractures, options.Search, options.ValueModel);
                front = frontInternal;
                zoneEvals = evals;
            }

            perZone.Add(new OmniZoneResult(zone, front, zoneEvals));
            totalEvals += zoneEvals;
        }

        sw.Stop();
        return new OmniSolveResult(perZone, totalEvals, sw.Elapsed);
    }

    // -------------------------------------------------------------------------
    // Pareto-aware coarse-to-fine angular search (Frahan I4). Sweeps psi at the
    // CoarseStepRad over the full range, then refines a +/- (parent step) window
    // around the top-K recovery angles at MediumStepRad, then FineStepRad. Every
    // non-dominated point found at any tier is kept in one merged front, so the
    // multi-objective front never regresses below the coarse pass. Cuts psi
    // evaluations ~3-5x vs the exhaustive fine uniform sweep on the limestone
    // problems. Deterministic: top-K tie-break is recovery desc then psi asc.
    // -------------------------------------------------------------------------
    private static (ParetoFront front, long evals) SolveZoneCoarseToFine(
        BoundingBox3 aabb, PlyMesh fractures, OmniSolverOptions o)
    {
        var search = o.Search;
        double[] steps = { o.CoarseStepRad, o.MediumStepRad, o.FineStepRad };
        var windows = new List<(double lo, double hi)> { (search.PsiStartRad, search.PsiStopRad) };
        var merged = new ParetoFront();
        long evals = 0;

        for (int t = 0; t < steps.Length; t++)
        {
            double step = steps[t];
            if (step <= 0) continue;
            var seeds = new List<double>();
            foreach (var w in windows)
            {
                var opts = WithPsiRange(search, w.lo, w.hi, step);
                var (front, e, _) = BlockCutOptParetoSolver.Solve(aabb, fractures, opts, o.ValueModel);
                evals += e;
                var pts = front.Points;
                for (int i = 0; i < pts.Count; i++) { var p = pts[i]; merged.Insert(in p); }
                CollectTopKPsi(front, o.CoarseToFineTopK, seeds);
            }
            if (t + 1 < steps.Length)
                windows = BuildChildWindows(seeds, step, search.PsiStartRad, search.PsiStopRad, steps[t + 1]);
        }
        return (merged, evals);
    }

    private static BlockCutOptOptions WithPsiRange(BlockCutOptOptions s, double lo, double hi, double step)
    {
        if (hi < lo) hi = lo;
        return new BlockCutOptOptions(
            s.BlockSizeX, s.BlockSizeY, s.BlockSizeZ, s.Kerf,
            lo, hi, step, s.DxMax, s.DxStep, s.DyMax, s.DyStep,
            s.ThetaMaxRad, s.ThetaStepRad, s.PhiMaxRad, s.PhiStepRad);
    }

    // Append up to k distinct psi seeds (recovery desc, then psi asc) to <paramref name="into"/>.
    private static void CollectTopKPsi(ParetoFront front, int k, List<double> into)
    {
        if (k <= 0) return;
        var pts = new List<ParetoPoint>(front.Points);
        pts.Sort((a, b) =>
        {
            if (a.RecoveryCount != b.RecoveryCount) return b.RecoveryCount.CompareTo(a.RecoveryCount);
            return a.PsiRad.CompareTo(b.PsiRad);
        });
        int added = 0;
        for (int i = 0; i < pts.Count && added < k; i++)
        {
            double psi = pts[i].PsiRad;
            bool dup = false;
            for (int j = 0; j < into.Count; j++)
                if (Math.Abs(into[j] - psi) < 1e-9) { dup = true; break; }
            if (!dup) { into.Add(psi); added++; }
        }
    }

    // +/- parentStep windows around each seed, clamped to [psiMin, psiMax], deduped at childStep resolution.
    private static List<(double lo, double hi)> BuildChildWindows(
        List<double> seeds, double parentStep, double psiMin, double psiMax, double childStep)
    {
        var windows = new List<(double lo, double hi)>();
        var seenCenters = new List<long>();
        foreach (var c in seeds)
        {
            long key = (long)Math.Round(c / Math.Max(childStep, 1e-9));
            bool dup = false;
            for (int j = 0; j < seenCenters.Count; j++) if (seenCenters[j] == key) { dup = true; break; }
            if (dup) continue;
            seenCenters.Add(key);
            windows.Add((Math.Max(psiMin, c - parentStep), Math.Min(psiMax, c + parentStep)));
        }
        if (windows.Count == 0) windows.Add((psiMin, psiMax));
        return windows;
    }
}
