#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// FisherRobustSampler -- Phase 8 of the synthesis roadmap; improvement I8.
//
// Runs BlockCutOpt M times, each with a different random seed driving the
// JointSetDfnGenerator's Fisher-approximation scatter on fracture normals.
// Reports per-sample (psi, recovery) and aggregate p10 / p50 / p90 of recovery,
// plus the most-common psi bin -- the "robust optimum direction" that
// performs well under fracture-mapping uncertainty.
//
// This addresses open problem 8 of `08_synthesis_and_optimum_algorithm.md`
// section 5: no closed-form sensitivity to fracture-mapping error in any prior
// paper. The Monte Carlo wrapper gives an empirical sensitivity for free.
//
// Source for the Fisher distribution and the scatter approximation:
//   - Azarafza et al. 2016 (`01_sousa_2016_mondim_granite.md`, actually
//     contains the Fisher fracture sampling we repurpose here).
//   - JointSet.ScatterDeg already implements the Gaussian small-angle Fisher
//     approximation (see JointSetDfnGenerator.cs in the Quarry namespace).
// =============================================================================

public sealed class FisherRobustResult
{
    public FisherRobustResult(
        int sampleCount,
        IReadOnlyList<BlockCutOptResult> perSample,
        double recoveryP10, double recoveryP50, double recoveryP90,
        double recoveryMean, double recoveryStdDev,
        double medianPsiRad)
    {
        SampleCount = sampleCount;
        PerSample = perSample;
        RecoveryP10 = recoveryP10;
        RecoveryP50 = recoveryP50;
        RecoveryP90 = recoveryP90;
        RecoveryMean = recoveryMean;
        RecoveryStdDev = recoveryStdDev;
        MedianPsiRad = medianPsiRad;
    }

    public int SampleCount { get; }
    public IReadOnlyList<BlockCutOptResult> PerSample { get; }
    public double RecoveryP10 { get; }   // 10th-percentile recovery (robust score)
    public double RecoveryP50 { get; }   // median recovery
    public double RecoveryP90 { get; }   // 90th-percentile recovery
    public double RecoveryMean { get; }
    public double RecoveryStdDev { get; }
    public double MedianPsiRad { get; }  // most-common psi across samples
    public double MedianPsiDeg => MedianPsiRad * 180.0 / Math.PI;

    public override string ToString() =>
        $"FisherRobustResult(N={SampleCount}, " +
        $"R[p10/p50/p90]=[{RecoveryP10:0.00},{RecoveryP50:0.00},{RecoveryP90:0.00}]%, " +
        $"mean={RecoveryMean:0.00}+/-{RecoveryStdDev:0.00}, " +
        $"medianPsi={MedianPsiDeg:0.0} deg)";
}

public static class FisherRobustSampler
{
    /// <summary>
    /// Run M Monte Carlo BlockCutOpt samples, each with a different DFN
    /// realisation drawn from the same joint-set distribution.
    /// </summary>
    public static FisherRobustResult Solve(
        BoundingBox3 testedArea,
        IReadOnlyList<JointSet> jointSets,
        BlockCutOptOptions options,
        int monteCarloSamples = 16,
        int baseSeed = 12345)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (jointSets == null) throw new ArgumentNullException(nameof(jointSets));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (monteCarloSamples < 1) throw new ArgumentOutOfRangeException(nameof(monteCarloSamples));

        var perSample = new List<BlockCutOptResult>(monteCarloSamples);
        var recoveries = new double[monteCarloSamples];
        var psis = new double[monteCarloSamples];

        for (int m = 0; m < monteCarloSamples; m++)
        {
            var planes = JointSetDfnGenerator.Generate(jointSets, testedArea, seed: baseSeed + m);
            var ply = JointSetDfnPlyEmitter.Emit(planes, testedArea);
            var r = BlockCutOptSolver.Solve(testedArea, ply, options);
            perSample.Add(r);
            recoveries[m] = r.RecoveryPercent;
            psis[m] = r.BestPsiRad;
        }

        var sorted = (double[])recoveries.Clone();
        Array.Sort(sorted);
        double p10 = Percentile(sorted, 0.10);
        double p50 = Percentile(sorted, 0.50);
        double p90 = Percentile(sorted, 0.90);

        double mean = 0;
        for (int i = 0; i < recoveries.Length; i++) mean += recoveries[i];
        mean /= recoveries.Length;
        double var = 0;
        for (int i = 0; i < recoveries.Length; i++)
        {
            double d = recoveries[i] - mean;
            var += d * d;
        }
        double stddev = (recoveries.Length > 1) ? Math.Sqrt(var / (recoveries.Length - 1)) : 0.0;

        double medianPsi = MedianPsi(psis);

        return new FisherRobustResult(
            monteCarloSamples, perSample,
            p10, p50, p90, mean, stddev, medianPsi);
    }

    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0.0;
        double pos = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        double t = pos - lo;
        return sorted[lo] * (1.0 - t) + sorted[hi] * t;
    }

    private static double MedianPsi(double[] psis)
    {
        if (psis.Length == 0) return 0.0;
        var sorted = (double[])psis.Clone();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return (sorted.Length % 2 == 0)
            ? 0.5 * (sorted[mid - 1] + sorted[mid])
            : sorted[mid];
    }
}
