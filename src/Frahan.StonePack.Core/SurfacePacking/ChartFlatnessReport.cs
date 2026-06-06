using System;
using System.Collections.Generic;

namespace Frahan.Surface;

/// <summary>
/// Per-face flatness flag returned by <see cref="ChartFlatnessReport"/>.
/// </summary>
public sealed class FlatnessFaceFlag
{
    public FlatnessFaceFlag(int faceIndex, double areaRatio, bool isAboveThreshold)
    {
        FaceIndex = faceIndex;
        AreaRatio = areaRatio;
        IsAboveThreshold = isAboveThreshold;
    }

    public int FaceIndex { get; }
    public double AreaRatio { get; }
    public bool IsAboveThreshold { get; }

    public override string ToString() =>
        $"FlatnessFaceFlag(face={FaceIndex}, ratio={AreaRatio:0.###}, " +
        $"above={IsAboveThreshold})";
}

/// <summary>
/// Pure-managed flatness classifier built on top of a per-face area-ratio list
/// (typically produced by <c>ChartDistortionAnalyzer</c>). Reports which faces
/// exceed a configurable threshold, plus aggregate counts and the worst-case
/// face.
///
/// Pure managed; no Rhino.Geometry dependency, even though the file lives in
/// Frahan.Surface alongside the chart code that produces the input ratios.
/// Tests can therefore run without the Rhino runtime.
///
/// Spec 6 section 7 calls for distortion reporting; this complements
/// <c>ChartDistortionReport</c> with a thresholded view useful for "should
/// this face be subdivided / re-cut?" decisions.
/// </summary>
public sealed class ChartFlatnessReport
{
    public ChartFlatnessReport(
        IReadOnlyList<FlatnessFaceFlag> perFaceFlags,
        int aboveThresholdCount,
        double threshold,
        int worstFaceIndex,
        double worstAreaRatio)
    {
        PerFaceFlags = perFaceFlags ?? Array.Empty<FlatnessFaceFlag>();
        AboveThresholdCount = aboveThresholdCount;
        Threshold = threshold;
        WorstFaceIndex = worstFaceIndex;
        WorstAreaRatio = worstAreaRatio;
    }

    public IReadOnlyList<FlatnessFaceFlag> PerFaceFlags { get; }
    public int AboveThresholdCount { get; }
    public double Threshold { get; }
    public int WorstFaceIndex { get; }
    public double WorstAreaRatio { get; }

    public int TotalFaceCount => PerFaceFlags.Count;
    public double AboveThresholdRatio =>
        TotalFaceCount == 0 ? 0.0 : (double)AboveThresholdCount / TotalFaceCount;

    public override string ToString() =>
        $"ChartFlatnessReport(faces={TotalFaceCount}, above={AboveThresholdCount} " +
        $"({AboveThresholdRatio:P1}) > {Threshold:0.###}, " +
        $"worst face {WorstFaceIndex} @ {WorstAreaRatio:0.###})";

    /// <summary>
    /// Classify a list of per-face area ratios against a threshold. The threshold
    /// is interpreted as the maximum acceptable area ratio (faces with ratio
    /// strictly &gt; threshold are flagged "above").
    /// </summary>
    public static ChartFlatnessReport Classify(IReadOnlyList<double> perFaceAreaRatios, double threshold)
    {
        if (perFaceAreaRatios == null) throw new ArgumentNullException(nameof(perFaceAreaRatios));
        if (threshold <= 0.0) throw new ArgumentOutOfRangeException(nameof(threshold), "must be > 0");

        var flags = new List<FlatnessFaceFlag>(perFaceAreaRatios.Count);
        int aboveCount = 0;
        int worstIdx = -1;
        double worstRatio = double.NaN;

        for (int i = 0; i < perFaceAreaRatios.Count; i++)
        {
            double r = perFaceAreaRatios[i];
            // The "distortion magnitude" we care about is how far r is from 1.0
            // in either direction (0.5x or 2x are equally distorted). The threshold
            // applies to max(r, 1/r).
            double normalised = r <= 0.0 ? double.PositiveInfinity : Math.Max(r, 1.0 / r);
            bool above = normalised > threshold;
            if (above) aboveCount++;
            if (worstIdx < 0 || normalised > worstRatio)
            {
                worstIdx = i;
                worstRatio = normalised;
            }
            flags.Add(new FlatnessFaceFlag(i, r, above));
        }
        if (worstIdx < 0) worstRatio = 0.0;

        return new ChartFlatnessReport(
            perFaceFlags: flags,
            aboveThresholdCount: aboveCount,
            threshold: threshold,
            worstFaceIndex: worstIdx < 0 ? -1 : worstIdx,
            worstAreaRatio: worstRatio);
    }
}
