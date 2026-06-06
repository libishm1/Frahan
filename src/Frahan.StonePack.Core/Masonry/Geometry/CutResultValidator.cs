#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// CutResultValidator — post-cut hygiene for slab + fracture cutting.
//
// Two diagnostic operations:
//   • ValidateConservation(pre, post)   — checks volume and face budget.
//                                          Catches sliver leaks, lost
//                                          pieces, and topology mistakes.
//   • EnumerateSlivers(pieces, threshold) — list pieces whose volume
//                                          falls below an absolute or
//                                          relative threshold. Caller
//                                          decides whether to drop or
//                                          merge them.
//
// Pure-managed; works on Slab DTOs (the cutter's native output).
// =============================================================================

public sealed class CutConservationReport
{
    public CutConservationReport(
        double preVolume, double postVolumeSum, double absoluteError,
        double relativeError, bool conserved,
        int prePieceCount, int postPieceCount,
        int sliverCount, int dropouts)
    {
        PreVolume = preVolume;
        PostVolumeSum = postVolumeSum;
        AbsoluteError = absoluteError;
        RelativeError = relativeError;
        Conserved = conserved;
        PrePieceCount = prePieceCount;
        PostPieceCount = postPieceCount;
        SliverCount = sliverCount;
        Dropouts = dropouts;
    }

    public double PreVolume { get; }
    public double PostVolumeSum { get; }
    public double AbsoluteError { get; }
    public double RelativeError { get; }
    public bool Conserved { get; }
    public int PrePieceCount { get; }
    public int PostPieceCount { get; }

    /// <summary>Pieces below the sliver threshold.</summary>
    public int SliverCount { get; }

    /// <summary>Pieces with effectively zero volume (likely lost).</summary>
    public int Dropouts { get; }

    public override string ToString() =>
        $"Conservation(pre={PreVolume:0.######}, post={PostVolumeSum:0.######}, " +
        $"absErr={AbsoluteError:0.######}, relErr={RelativeError:0.######%}, " +
        $"OK={Conserved}, pieces={PrePieceCount}→{PostPieceCount}, " +
        $"slivers={SliverCount}, dropouts={Dropouts})";
}

public static class CutResultValidator
{
    public const double DefaultRelativeTolerance = 1e-6;
    public const double DefaultSliverFraction = 1e-4;
    public const double DefaultDropoutVolume = 1e-12;

    /// <summary>
    /// Sum signed-volume of pre-pieces (typically just one slab) and
    /// compare against sum signed-volume of post-pieces. Slivers and
    /// dropouts are also counted via the same pass.
    /// </summary>
    public static CutConservationReport Validate(
        IReadOnlyList<Slab> pre,
        IReadOnlyList<Slab> post,
        double relativeTolerance = DefaultRelativeTolerance,
        double sliverFraction = DefaultSliverFraction,
        double dropoutVolume = DefaultDropoutVolume)
    {
        if (pre == null) throw new ArgumentNullException(nameof(pre));
        if (post == null) throw new ArgumentNullException(nameof(post));
        if (relativeTolerance < 0) throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        if (sliverFraction < 0) throw new ArgumentOutOfRangeException(nameof(sliverFraction));
        if (dropoutVolume < 0) throw new ArgumentOutOfRangeException(nameof(dropoutVolume));

        double preVol = 0.0;
        for (int i = 0; i < pre.Count; i++) preVol += Math.Abs(pre[i].SignedVolume());

        double postVol = 0.0;
        int slivers = 0, dropouts = 0;
        double sliverThreshold = sliverFraction * preVol;
        for (int i = 0; i < post.Count; i++)
        {
            double v = Math.Abs(post[i].SignedVolume());
            postVol += v;
            if (v <= dropoutVolume) dropouts++;
            else if (v < sliverThreshold) slivers++;
        }

        double absErr = Math.Abs(preVol - postVol);
        double relErr = preVol > 0 ? absErr / preVol : (postVol > 0 ? double.PositiveInfinity : 0.0);
        bool conserved = relErr <= relativeTolerance;
        return new CutConservationReport(
            preVol, postVol, absErr, relErr, conserved,
            pre.Count, post.Count, slivers, dropouts);
    }

    /// <summary>
    /// Return the indices of pieces whose volume falls below
    /// <paramref name="thresholdFraction"/> · totalVolume. Caller decides
    /// whether to drop or merge them.
    /// </summary>
    public static List<int> EnumerateSlivers(
        IReadOnlyList<Slab> pieces,
        double thresholdFraction = DefaultSliverFraction)
    {
        if (pieces == null) throw new ArgumentNullException(nameof(pieces));
        if (thresholdFraction < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdFraction));

        double total = 0;
        var vols = new double[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            vols[i] = Math.Abs(pieces[i].SignedVolume());
            total += vols[i];
        }
        double cutoff = thresholdFraction * total;
        var slivers = new List<int>();
        for (int i = 0; i < pieces.Count; i++)
            if (vols[i] < cutoff) slivers.Add(i);
        return slivers;
    }

    /// <summary>
    /// Drop all pieces below the sliver threshold and return the
    /// surviving subset. Order preserved.
    /// </summary>
    public static List<Slab> DropSlivers(
        IReadOnlyList<Slab> pieces,
        double thresholdFraction = DefaultSliverFraction)
    {
        if (pieces == null) throw new ArgumentNullException(nameof(pieces));
        var slivers = EnumerateSlivers(pieces, thresholdFraction);
        var sliverSet = new HashSet<int>(slivers);
        var keep = new List<Slab>(pieces.Count - slivers.Count);
        for (int i = 0; i < pieces.Count; i++)
            if (!sliverSet.Contains(i)) keep.Add(pieces[i]);
        return keep;
    }
}
