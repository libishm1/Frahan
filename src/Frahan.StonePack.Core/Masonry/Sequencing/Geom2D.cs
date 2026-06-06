#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Sequencing;

// =============================================================================
// Geom2D — 2D primitives and predicates for the polygonal-masonry pipeline.
// Mirrors the Python reference at
// Template-General/outputs/2026-05-20/polygonal_masonry_sequence/polygonal_masonry/geom.py.
//
// All coordinates use the (double X, double Y) value-tuple shape that the
// existing Core code adopts (see Masonry/Geometry/RobustPolygon2D.cs). The
// predicates take an explicit epsilon because the paper's regions share
// boundary segments exactly and robust equality matters at the
// quantisation level.
// =============================================================================

public static class Geom2D
{
    public const double DefaultEps = 1e-9;

    public static bool AlmostEqual(double a, double b, double eps = DefaultEps)
        => Math.Abs(a - b) <= eps;

    public static bool PointEq((double X, double Y) a, (double X, double Y) b,
                               double eps = DefaultEps)
        => AlmostEqual(a.X, b.X, eps) && AlmostEqual(a.Y, b.Y, eps);

    public static double Cross((double X, double Y) o,
                                (double X, double Y) a,
                                (double X, double Y) b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public static int Orient((double X, double Y) p, (double X, double Y) q,
                             (double X, double Y) r, double eps = DefaultEps)
    {
        double c = Cross(p, q, r);
        if (c > eps) return 1;
        if (c < -eps) return -1;
        return 0;
    }

    public static double SignedArea(IReadOnlyList<(double X, double Y)> ring)
    {
        if (ring == null) throw new ArgumentNullException(nameof(ring));
        int n = ring.Count;
        if (n < 3) return 0.0;
        double s = 0.0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            s += ring[i].X * ring[j].Y - ring[j].X * ring[i].Y;
        }
        return 0.5 * s;
    }

    public static bool OnSegment((double X, double Y) a, (double X, double Y) b,
                                  (double X, double Y) p, double eps = DefaultEps)
    {
        if (Math.Abs(Cross(a, b, p)) > eps) return false;
        if (Math.Min(a.X, b.X) - eps <= p.X && p.X <= Math.Max(a.X, b.X) + eps)
        {
            if (Math.Min(a.Y, b.Y) - eps <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + eps)
            {
                return true;
            }
        }
        return false;
    }

    // Even-odd point-in-polygon, boundary returns true.
    public static bool PointInRing((double X, double Y) point,
                                    IReadOnlyList<(double X, double Y)> ring,
                                    double eps = DefaultEps)
    {
        int n = ring.Count;
        if (n < 3) return false;
        double x = point.X;
        double y = point.Y;
        bool inside = false;
        int j = n - 1;
        for (int i = 0; i < n; i++)
        {
            var (xi, yi) = ring[i];
            var (xj, yj) = ring[j];
            if (OnSegment((xj, yj), (xi, yi), point, eps)) return true;
            if ((yi > y) != (yj > y))
            {
                double xint = xi + (y - yi) * (xj - xi) / (yj - yi + 1e-300);
                if (xint > x - eps) inside = !inside;
            }
            j = i;
        }
        return inside;
    }

    public static (double X, double Y) RingCentroid(
        IReadOnlyList<(double X, double Y)> ring)
    {
        if (ring == null) throw new ArgumentNullException(nameof(ring));
        int n = ring.Count;
        if (n == 0) return (0.0, 0.0);
        double a = SignedArea(ring);
        if (Math.Abs(a) < DefaultEps)
        {
            double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { sx += ring[i].X; sy += ring[i].Y; }
            return (sx / n, sy / n);
        }
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++)
        {
            var (x1, y1) = ring[i];
            var (x2, y2) = ring[(i + 1) % n];
            double f = x1 * y2 - x2 * y1;
            cx += (x1 + x2) * f;
            cy += (y1 + y2) * f;
        }
        cx /= 6.0 * a;
        cy /= 6.0 * a;
        return (cx, cy);
    }

    // Strictly monotonic in x.
    public static bool ChainIsMonotoneX(IReadOnlyList<(double X, double Y)> chain,
                                         double eps = DefaultEps)
    {
        if (chain.Count < 2) return true;
        bool inc = true, dec = true;
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            if (chain[i + 1].X <= chain[i].X + eps) inc = false;
            if (chain[i + 1].X >= chain[i].X - eps) dec = false;
        }
        return inc || dec;
    }

    // Strictly monotonic in x, or constant x with strictly monotonic y
    // (purely vertical connector). Paper sec. 5.1 defines chains as
    // y = f(x); we accept vertical connectors that satisfy the planarity
    // requirements but are not functions of x.
    public static bool ChainIsMonotone(IReadOnlyList<(double X, double Y)> chain,
                                        double eps = DefaultEps)
    {
        if (chain.Count < 2) return true;
        if (ChainIsMonotoneX(chain, eps)) return true;
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i].X < minX) minX = chain[i].X;
            if (chain[i].X > maxX) maxX = chain[i].X;
        }
        if (maxX - minX > eps) return false;
        bool yInc = true, yDec = true;
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            if (chain[i + 1].Y <= chain[i].Y + eps) yInc = false;
            if (chain[i + 1].Y >= chain[i].Y - eps) yDec = false;
        }
        return yInc || yDec;
    }

    // Reorders so the dominant axis (x if it varies, otherwise y) is ascending.
    public static List<(double X, double Y)> NormaliseChain(
        IReadOnlyList<(double X, double Y)> chain)
    {
        var pts = new List<(double X, double Y)>(chain);
        if (pts.Count < 2) return pts;
        if (Math.Abs(pts[0].X - pts[pts.Count - 1].X) > DefaultEps)
        {
            if (pts[0].X > pts[pts.Count - 1].X) pts.Reverse();
        }
        else
        {
            if (pts[0].Y > pts[pts.Count - 1].Y) pts.Reverse();
        }
        return pts;
    }
}
