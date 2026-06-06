using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

public enum PackingSortMode
{
    UserOrder,
    AreaDescending,
    WidthDescending,
    HeightDescending,
    MaxDimensionDescending
}

public enum PackingCornerMode
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight
}

// Placement-scoring rule for the exact NFP-BLF nester (2026-06-06 SLM evolution).
// BottomLeft = classic lexicographic (y, x) minimum (the legacy default).
// LowestGravityCenter = rank rotations/positions by absolute part centroid Y
// (cY(theta) + p_y) for a flatter, lower-gravity boundary. Still linear over the
// feasible region, so the minimizer is a vertex. See
// outputs/2026-06-06/packing_slm_evolution/SYNTHESIS_2D.md (PRISMA Rank 5).
public enum PlacementScore
{
    BottomLeft,
    LowestGravityCenter
}

public sealed class PackingResult
{
    public List<Curve> PackedCurves { get; } = new();
    public List<Transform> Transforms { get; } = new();
    public List<Curve> UnplacedCurves { get; } = new();
    public List<Curve> DiagnosticCurves { get; } = new();
    public List<int> SourceIndices { get; } = new();
    public List<int> SheetIndices { get; } = new();
    public List<string> FailureReasons { get; } = new();
    public List<Curve> SheetPreviewCurves { get; } = new();
    public Curve? SheetPreview { get; set; }
    public double UsedLength { get; set; }
    public double Utilization { get; set; }
    public int InputCount { get; set; }
    public int PreparedCount { get; set; }
    public int InvalidCount { get; set; }
    public int CandidateCount { get; set; }
    public int CollisionCheckCount { get; set; }
    public int NfpRejectCount { get; set; }
    public int FeasibleRegionCount { get; set; }
    public int FeasibleRegionCandidateCount { get; set; }
    public int FeasibleRegionFallbackCount { get; set; }
    public int NfpCacheHits { get; set; }
    public int NfpCacheMisses { get; set; }
    public int OptimizationRuns { get; set; }
    public long RuntimeMilliseconds { get; set; }
    public string Report { get; set; } = string.Empty;

    // Half J (2026-05-06): trim-aware mode. When TrimTolerance > 0 the
    // solver allows part-to-part overlaps up to that depth, then in a
    // post-pass subtracts each earlier-placed part's geometry from any
    // later-placed part it overlaps. Each entry parallels PackedCurves —
    // it is either the original packed curve (no trim happened) or the
    // boolean-difference result. Empty when TrimTolerance == 0.
    public List<Curve> TrimmedCurves { get; } = new();

    // Trim adjacency: branch i is the list of source indices of EARLIER-
    // placed parts that trimmed PackedCurves[i]. Empty branches indicate
    // parts that were not trimmed.
    public List<List<int>> TrimAdjacency { get; } = new();

    public int TrimEventCount { get; set; }

    // F-2D-002.F5 annealing post-pass: count of pieces translated by the
    // settle pass. Reported in the Trencadís report.
    public int AnnealingMoves { get; set; }

    // 2026-06-06 SLM evolution (exact NFP-BLF): count of parts moved by the
    // gravitational compaction sweep (Li-Milenkovic discrete redrop) and the
    // number of previously-unplaced parts recovered by the reinsertion sweep.
    // Both are 0 when the evolution flags are off (byte-identical legacy path).
    public int CompactionMoves { get; set; }
    public int ReinsertionGains { get; set; }
}

internal sealed class Point3dToleranceComparer : IEqualityComparer<Point3d>
{
    private readonly double _tolerance;

    public Point3dToleranceComparer(double tolerance)
    {
        _tolerance = Math.Max(tolerance, 1e-9);
    }

    public bool Equals(Point3d a, Point3d b) => a.DistanceTo(b) <= _tolerance;

    public int GetHashCode(Point3d point)
    {
        return (((17 * 31) + (int)Math.Round(point.X / _tolerance)) * 31
            + (int)Math.Round(point.Y / _tolerance)) * 31
            + (int)Math.Round(point.Z / _tolerance);
    }
}
