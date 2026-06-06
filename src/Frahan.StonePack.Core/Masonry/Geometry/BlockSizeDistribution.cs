#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// BlockSizeDistribution — histogram + percentiles + Tukey-fence outlier
// detection over a list of slab volumes. Used to QA quarry-decomposition
// outputs: extreme small or large blocks indicate the joint-set
// parameters are off (too tight or too loose).
//
// Coefficient of variation (stddev/mean) is the single most diagnostic
// number. CV < 0.3 is tight, well-controlled set. CV > 1 means the
// distribution has a long tail and the joint-set parameters need
// retuning.
// =============================================================================

public sealed class BlockSizeReport
{
    public BlockSizeReport(
        int count,
        double total, double min, double max, double mean,
        double median, double stdDev, double cv,
        double p10, double p25, double p50, double p75, double p90,
        IReadOnlyList<int> outlierIndices,
        IReadOnlyList<int> binCounts, double binWidth, double binMin)
    {
        Count = count;
        TotalVolume = total;
        Min = min;
        Max = max;
        Mean = mean;
        Median = median;
        StdDev = stdDev;
        CoefficientOfVariation = cv;
        P10 = p10; P25 = p25; P50 = p50; P75 = p75; P90 = p90;
        OutlierIndices = outlierIndices;
        BinCounts = binCounts;
        BinWidth = binWidth;
        BinMin = binMin;
    }

    public int Count { get; }
    public double TotalVolume { get; }
    public double Min { get; }
    public double Max { get; }
    public double Mean { get; }
    public double Median { get; }
    public double StdDev { get; }

    /// <summary>Stddev / mean. > 1 signals long-tail / retune joint set.</summary>
    public double CoefficientOfVariation { get; }

    public double P10 { get; }
    public double P25 { get; }
    public double P50 { get; }
    public double P75 { get; }
    public double P90 { get; }

    /// <summary>0-based indices outside the Tukey fence Q1-1.5·IQR / Q3+1.5·IQR.</summary>
    public IReadOnlyList<int> OutlierIndices { get; }

    public IReadOnlyList<int> BinCounts { get; }
    public double BinWidth { get; }
    public double BinMin { get; }

    public override string ToString() =>
        $"BlockSize(n={Count}, total={TotalVolume:0.###}, " +
        $"[{Min:0.###}, {Median:0.###}, {Max:0.###}], " +
        $"mean={Mean:0.###}, sd={StdDev:0.###}, cv={CoefficientOfVariation:0.##}, " +
        $"outliers={OutlierIndices.Count})";
}

public static class BlockSizeDistribution
{
    /// <summary>
    /// Compute distribution stats. <paramref name="binCount"/> is the
    /// histogram resolution; if &lt;= 0, defaults to ceil(sqrt(N)).
    /// </summary>
    public static BlockSizeReport Analyse(IReadOnlyList<Slab> pieces, int binCount = 0)
    {
        if (pieces == null) throw new ArgumentNullException(nameof(pieces));
        int n = pieces.Count;
        if (n == 0)
            return new BlockSizeReport(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                Array.Empty<int>(), Array.Empty<int>(), 0, 0);

        var vols = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            vols[i] = Math.Abs(pieces[i].SignedVolume());
            sum += vols[i];
        }
        var sorted = (double[])vols.Clone();
        Array.Sort(sorted);
        double min = sorted[0], max = sorted[n - 1];
        double mean = sum / n;
        double median = Percentile(sorted, 0.5);
        double sumSq = 0;
        for (int i = 0; i < n; i++) sumSq += (vols[i] - mean) * (vols[i] - mean);
        double stdDev = Math.Sqrt(sumSq / n);
        double cv = mean > 0 ? stdDev / mean : 0.0;

        double p10 = Percentile(sorted, 0.10);
        double p25 = Percentile(sorted, 0.25);
        double p50 = median;
        double p75 = Percentile(sorted, 0.75);
        double p90 = Percentile(sorted, 0.90);
        double iqr = p75 - p25;
        double lo = p25 - 1.5 * iqr;
        double hi = p75 + 1.5 * iqr;

        var outliers = new List<int>();
        for (int i = 0; i < n; i++)
            if (vols[i] < lo || vols[i] > hi) outliers.Add(i);

        // Histogram.
        int bins = binCount > 0 ? binCount : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));
        var counts = new int[bins];
        double binWidth = (max - min) / bins;
        if (binWidth <= 0) { counts[0] = n; binWidth = 1; }
        else
        {
            for (int i = 0; i < n; i++)
            {
                int b = (int)((vols[i] - min) / binWidth);
                if (b >= bins) b = bins - 1;
                if (b < 0) b = 0;
                counts[b]++;
            }
        }

        return new BlockSizeReport(n, sum, min, max, mean, median, stdDev, cv,
            p10, p25, p50, p75, p90, outliers, counts, binWidth, min);
    }

    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0.0;
        if (sorted.Length == 1) return sorted[0];
        double idx = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(idx);
        int hi = (int)Math.Ceiling(idx);
        if (lo == hi) return sorted[lo];
        double w = idx - lo;
        return sorted[lo] * (1 - w) + sorted[hi] * w;
    }
}
