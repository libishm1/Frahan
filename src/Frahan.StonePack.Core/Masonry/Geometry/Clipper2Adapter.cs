#nullable disable
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// Clipper2Adapter — production-grade 2D polygon Boolean operations backed by
// Angus Johnson's Clipper2 library (BSL-1.0). Used for non-convex
// intersection / union / difference / xor when robustness matters more than
// algorithmic transparency. The in-tree GreinerHormannClipper stays as a
// pure-managed fallback for environments that can't pull NuGet packages
// and as documentation of the algorithm.
//
// Why Clipper2 over Greiner-Hormann:
//   • Handles vertex-on-edge and fully-coincident-edge cases correctly,
//     which Foster-Hormann partially addresses but the in-tree GH does not.
//   • Polygons with holes via PathsD (each PathD = one loop; positive
//     orientation = outer, negative = hole) — no caller-side juggling.
//   • XOR support out of the box.
//   • Battle-tested in CAD / CAM since 2010; standard back-end for
//     OpenSCAD, Inkscape, etc.
//
// API design: take and return our existing tuple-based polygon
// representation so callers don't need to learn Clipper2's PathD type.
// PathsD (multi-loop) maps to List<List<(double, double)>>.
// =============================================================================

public static class Clipper2Adapter
{
    /// <summary>
    /// Intersection of two polygons. Each input may be non-convex.
    /// Returns zero or more disjoint result loops.
    /// </summary>
    public static List<List<(double X, double Y)>> Intersect(
        IReadOnlyList<(double X, double Y)> subject,
        IReadOnlyList<(double X, double Y)> clip,
        FillRule fillRule = FillRule.NonZero)
    {
        return Run(subject, clip, ClipType.Intersection, fillRule);
    }

    /// <summary>Union (boolean OR) of two polygons.</summary>
    public static List<List<(double X, double Y)>> Union(
        IReadOnlyList<(double X, double Y)> subject,
        IReadOnlyList<(double X, double Y)> clip,
        FillRule fillRule = FillRule.NonZero)
    {
        return Run(subject, clip, ClipType.Union, fillRule);
    }

    /// <summary>Difference: subject minus clip.</summary>
    public static List<List<(double X, double Y)>> Difference(
        IReadOnlyList<(double X, double Y)> subject,
        IReadOnlyList<(double X, double Y)> clip,
        FillRule fillRule = FillRule.NonZero)
    {
        return Run(subject, clip, ClipType.Difference, fillRule);
    }

    /// <summary>Exclusive-or (symmetric difference).</summary>
    public static List<List<(double X, double Y)>> Xor(
        IReadOnlyList<(double X, double Y)> subject,
        IReadOnlyList<(double X, double Y)> clip,
        FillRule fillRule = FillRule.NonZero)
    {
        return Run(subject, clip, ClipType.Xor, fillRule);
    }

    // ─── Polygons-with-holes overloads ──────────────────────────────────

    /// <summary>
    /// Boolean over polygons that may carry holes. Each input is a list of
    /// loops; the FIRST loop is the outer boundary (CCW = positive area)
    /// and subsequent loops are holes (CW = negative area). Clipper2 sorts
    /// out the topology internally via the supplied fill rule.
    /// </summary>
    public static List<List<(double X, double Y)>> Boolean(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subject,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> clip,
        ClipType op,
        FillRule fillRule = FillRule.NonZero)
    {
        if (subject == null) throw new ArgumentNullException(nameof(subject));
        if (clip == null) throw new ArgumentNullException(nameof(clip));

        var subj = ToPathsD(subject);
        var clp = ToPathsD(clip);
        PathsD result = Clipper.BooleanOp(op, subj, clp, fillRule);
        return FromPathsD(result);
    }

    // ─── Internals ──────────────────────────────────────────────────────

    private static List<List<(double X, double Y)>> Run(
        IReadOnlyList<(double X, double Y)> subject,
        IReadOnlyList<(double X, double Y)> clip,
        ClipType op,
        FillRule fillRule)
    {
        if (subject == null) throw new ArgumentNullException(nameof(subject));
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (subject.Count < 3 || clip.Count < 3)
            return new List<List<(double, double)>>();

        var subj = new PathsD { ToPathD(subject) };
        var clp = new PathsD { ToPathD(clip) };
        PathsD result = Clipper.BooleanOp(op, subj, clp, fillRule);
        return FromPathsD(result);
    }

    private static PathD ToPathD(IReadOnlyList<(double X, double Y)> p)
    {
        var path = new PathD(p.Count);
        for (int i = 0; i < p.Count; i++) path.Add(new PointD(p[i].X, p[i].Y));
        return path;
    }

    private static PathsD ToPathsD(IReadOnlyList<IReadOnlyList<(double X, double Y)>> ps)
    {
        var paths = new PathsD(ps.Count);
        for (int i = 0; i < ps.Count; i++) paths.Add(ToPathD(ps[i]));
        return paths;
    }

    private static List<List<(double X, double Y)>> FromPathsD(PathsD result)
    {
        var loops = new List<List<(double X, double Y)>>(result.Count);
        for (int i = 0; i < result.Count; i++)
        {
            var path = result[i];
            var loop = new List<(double X, double Y)>(path.Count);
            for (int j = 0; j < path.Count; j++)
                loop.Add((path[j].x, path[j].y));
            loops.Add(loop);
        }
        return loops;
    }

    // ─── Minkowski sum + tuple-only boolean wrappers ────────────────────
    // Added 2026-06-03 for the exact No-Fit-Polygon Bottom-Left-Fill nester
    // (IrregularSheetFillNfpBlf). These expose Clipper2 to callers that do NOT
    // reference Clipper2Lib (the GH project): every signature is plain tuples,
    // no Clipper2Lib types leak out. Minkowski semantics verified against the
    // pyclipper study probe (two unit squares -> NFP = [-1,1]^2, area 4).

    /// <summary>
    /// Minkowski sum (pattern (+) path) of two simple closed polygons. Returns
    /// the merged outer region as zero or more loops. Used to build no-fit
    /// polygons NFP(A, B) = A (+) (-B): B placed at p overlaps A iff p is in the
    /// interior of the returned region.
    /// </summary>
    public static List<List<(double X, double Y)>> MinkowskiSum(
        IReadOnlyList<(double X, double Y)> pattern,
        IReadOnlyList<(double X, double Y)> path)
    {
        if (pattern == null || path == null) return new List<List<(double X, double Y)>>();
        if (pattern.Count < 3 || path.Count < 3) return new List<List<(double X, double Y)>>();
        // Clipper2's PathsD MinkowskiSum already NonZero-unions the swept quads
        // internally (verified behaviorally on 2.0.0: a re-union is a no-op on
        // loop count and area), so no second union here — it doubled the cost
        // of every NFP build (2026-06-12 profiling: Minkowski was 95% of the
        // hole-nester's general-engine solve time).
        PathsD raw = Clipper.MinkowskiSum(ToPathD(pattern), ToPathD(path), true);
        return FromPathsD(raw);
    }

    /// <summary>Difference (subject minus clip) over multi-loop polygons; tuple-only.</summary>
    public static List<List<(double X, double Y)>> DifferenceLoops(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subject,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> clip)
        => Boolean(subject, clip, ClipType.Difference, FillRule.NonZero);

    /// <summary>Intersection over multi-loop polygons; tuple-only.</summary>
    public static List<List<(double X, double Y)>> IntersectLoops(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subject,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> clip)
        => Boolean(subject, clip, ClipType.Intersection, FillRule.NonZero);

    /// <summary>Union over multi-loop polygons; tuple-only.</summary>
    public static List<List<(double X, double Y)>> UnionLoops(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subject,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> clip)
        => Boolean(subject, clip, ClipType.Union, FillRule.NonZero);

    /// <summary>
    /// Offset (inflate when delta &gt; 0, erode when delta &lt; 0) multi-loop
    /// polygons by a fixed distance. Used to enforce part spacing (inflate the
    /// blocked NFP union) and boundary clearance (erode the inner-fit region).
    /// Mitre joins keep the result polygonal. Tuple-only signature.
    /// </summary>
    public static List<List<(double X, double Y)>> InflateLoops(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> loops, double delta)
    {
        if (loops == null || loops.Count == 0 || delta == 0.0)
            return loops == null ? new List<List<(double X, double Y)>>() : FromTuples(loops);
        var paths = ToPathsD(loops);
        PathsD r = Clipper.InflatePaths(paths, delta, JoinType.Miter, EndType.Polygon, 4.0, 6, 0.0);
        return FromPathsD(r);
    }

    private static List<List<(double X, double Y)>> FromTuples(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> loops)
    {
        var outl = new List<List<(double X, double Y)>>(loops.Count);
        for (int i = 0; i < loops.Count; i++)
        {
            var src = loops[i];
            var dst = new List<(double X, double Y)>(src.Count);
            for (int j = 0; j < src.Count; j++) dst.Add(src[j]);
            outl.Add(dst);
        }
        return outl;
    }
}
