#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Geometry;

// =============================================================================
// RobustPolygon2D — pure-managed 2D polygon utilities for the packing
// pipeline. Three independent operations:
//
//   • Sanitize           — drop duplicate / collinear vertices and
//                          sliver edges below a tolerance. The single
//                          most useful pre-processing step before
//                          area-sensitive algorithms.
//   • SignedArea, Centroid, IsClockwise — basic shape queries.
//   • SutherlandHodgmanClip — convex-clip subject against a convex
//                          clipper, with a tolerance-aware intersection
//                          that avoids the sign-confusion bug that bit
//                          TrencadisFill earlier in the project.
//
// All polygons are List<(double X, double Y)> in CCW order (or clockwise —
// the SignedArea sign tells you which). No degenerate "polygons" with
// fewer than 3 vertices are produced by Sanitize; Clip can return < 3
// when the result is empty.
// =============================================================================

public static class RobustPolygon2D
{
    public const double DefaultDedupTol = 1e-9;
    public const double DefaultCollinearTol = 1e-9;

    public static double SignedArea(IReadOnlyList<(double X, double Y)> p)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        int n = p.Count;
        if (n < 3) return 0.0;
        double a = 0.0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += p[i].X * p[j].Y - p[j].X * p[i].Y;
        }
        return 0.5 * a;
    }

    public static double Area(IReadOnlyList<(double X, double Y)> p) =>
        Math.Abs(SignedArea(p));

    public static bool IsClockwise(IReadOnlyList<(double X, double Y)> p) =>
        SignedArea(p) < 0.0;

    public static (double X, double Y) Centroid(IReadOnlyList<(double X, double Y)> p)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        int n = p.Count;
        if (n == 0) return (0, 0);
        if (n < 3)
        {
            double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { sx += p[i].X; sy += p[i].Y; }
            return (sx / n, sy / n);
        }
        double cx = 0, cy = 0, a = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double cross = p[i].X * p[j].Y - p[j].X * p[i].Y;
            cx += (p[i].X + p[j].X) * cross;
            cy += (p[i].Y + p[j].Y) * cross;
            a += cross;
        }
        if (Math.Abs(a) < 1e-20) return p[0];
        a *= 0.5;
        return (cx / (6.0 * a), cy / (6.0 * a));
    }

    /// <summary>
    /// Drop duplicate-adjacent vertices, drop collinear-chain vertices,
    /// drop sliver edges below the length tolerance. Returns a fresh list
    /// in the same winding order. May return fewer than 3 vertices when
    /// the input was a degenerate polygon to begin with — caller should
    /// check before treating the result as a real polygon.
    /// </summary>
    public static List<(double X, double Y)> Sanitize(
        IReadOnlyList<(double X, double Y)> p,
        double dedupTol = DefaultDedupTol,
        double collinearTol = DefaultCollinearTol)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        if (dedupTol < 0) throw new ArgumentOutOfRangeException(nameof(dedupTol));
        if (collinearTol < 0) throw new ArgumentOutOfRangeException(nameof(collinearTol));

        int n = p.Count;
        if (n < 2) return new List<(double, double)>(p);

        // Pass 1: drop adjacent duplicates (closed loop).
        var stage1 = new List<(double X, double Y)>(n);
        var prev = p[n - 1];
        for (int i = 0; i < n; i++)
        {
            var cur = p[i];
            double dx = cur.X - prev.X, dy = cur.Y - prev.Y;
            if (dx * dx + dy * dy > dedupTol * dedupTol)
            {
                stage1.Add(cur);
                prev = cur;
            }
        }
        if (stage1.Count < 2) return stage1;
        // After pass 1, last vertex may equal first — strip it if so.
        if (Sq(stage1[0].X - stage1[stage1.Count - 1].X,
               stage1[0].Y - stage1[stage1.Count - 1].Y) <= dedupTol * dedupTol)
            stage1.RemoveAt(stage1.Count - 1);

        // Pass 2: drop collinear-chain vertices. A vertex v with neighbours
        // u, w is dropped when the triangle (u, v, w) has area below the
        // tolerance — meaning v lies on the line uw.
        bool changed = true;
        var stage2 = new List<(double X, double Y)>(stage1);
        while (changed && stage2.Count >= 3)
        {
            changed = false;
            var next = new List<(double X, double Y)>(stage2.Count);
            for (int i = 0; i < stage2.Count; i++)
            {
                var u = stage2[(i - 1 + stage2.Count) % stage2.Count];
                var v = stage2[i];
                var w = stage2[(i + 1) % stage2.Count];
                double cross = (v.X - u.X) * (w.Y - u.Y) - (v.Y - u.Y) * (w.X - u.X);
                if (Math.Abs(cross) < collinearTol * 2.0)
                {
                    changed = true;
                    continue; // drop v
                }
                next.Add(v);
            }
            stage2 = next;
        }
        return stage2;
    }

    private static double Sq(double x, double y) => x * x + y * y;

    // ─── Sutherland-Hodgman with tolerance-aware intersection ───────────

    /// <summary>
    /// Clip <paramref name="subject"/> against the CONVEX polygon
    /// <paramref name="convexClip"/>. CCW windings expected on both. The
    /// intersection formula uses dot products (no division by numerator
    /// sign) so the sign-confusion bug that hit TrencadisFill earlier
    /// can't recur. Returns the clipped polygon (possibly empty when
    /// disjoint).
    /// </summary>
    public static List<(double X, double Y)> SutherlandHodgmanClip(
        IReadOnlyList<(double X, double Y)> subject,
        IReadOnlyList<(double X, double Y)> convexClip)
    {
        if (subject == null) throw new ArgumentNullException(nameof(subject));
        if (convexClip == null) throw new ArgumentNullException(nameof(convexClip));
        if (convexClip.Count < 3) return new List<(double, double)>();

        var output = new List<(double X, double Y)>(subject);
        int m = convexClip.Count;
        for (int e = 0; e < m && output.Count > 0; e++)
        {
            var a = convexClip[e];
            var b = convexClip[(e + 1) % m];
            var input = output;
            output = new List<(double X, double Y)>(input.Count + 2);
            int k = input.Count;
            var prev = input[k - 1];
            bool prevInside = IsLeftOrOn(a, b, prev);
            for (int i = 0; i < k; i++)
            {
                var cur = input[i];
                bool curInside = IsLeftOrOn(a, b, cur);
                if (curInside)
                {
                    if (!prevInside)
                        output.Add(IntersectEdge(prev, cur, a, b));
                    output.Add(cur);
                }
                else if (prevInside)
                {
                    output.Add(IntersectEdge(prev, cur, a, b));
                }
                prev = cur;
                prevInside = curInside;
            }
        }
        return output;
    }

    /// <summary>
    /// Cross-product side test. >= 0 means p is on the left or exactly on
    /// the directed edge (a → b).
    /// </summary>
    private static bool IsLeftOrOn(
        (double X, double Y) a, (double X, double Y) b, (double X, double Y) p)
    {
        double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        return cross >= 0.0;
    }

    /// <summary>
    /// Intersect the segment (s0, s1) with the line through (a, b).
    /// Uses the line-line parametric form: t = ((s0-a) × (b-a)) / ((s1-s0) × (b-a)).
    /// The sign survives consistently as long as we use the same direction
    /// for both crosses. Falls back to s0 when the segment is parallel to
    /// the clip edge (rare in S-H — caller has just decided one endpoint
    /// is inside and the other outside, so a non-zero cross is implied).
    /// </summary>
    private static (double X, double Y) IntersectEdge(
        (double X, double Y) s0, (double X, double Y) s1,
        (double X, double Y) a, (double X, double Y) b)
    {
        double dx = s1.X - s0.X, dy = s1.Y - s0.Y;
        double ex = b.X - a.X, ey = b.Y - a.Y;
        double denom = dx * ey - dy * ex;
        if (Math.Abs(denom) < 1e-20) return s0;
        double num = (a.X - s0.X) * ey - (a.Y - s0.Y) * ex;
        double t = num / denom;
        return (s0.X + t * dx, s0.Y + t * dy);
    }
}
