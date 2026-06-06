#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Geometry
{
    // =========================================================================
    // GeometryNumerics -- the shared numeric-hygiene layer (V3 ROSES roadmap #1/#2).
    //
    // The top-10 algorithm review found one cross-cutting defect across 9 of 10
    // algorithms: geometry computed in raw world coordinates (quarry / UTM scale,
    // 1e5..1e6 mm) loses 7 to 9 mantissa digits before any algorithm runs, and
    // fixed absolute epsilons (1e-6) are meaningless at architectural scale. The
    // fix recurs identically everywhere, so it lives here once:
    //
    //   1. Recenter: translate to the centroid (or bbox center) before computing,
    //      add back on emit. Recovers full float64 precision near the origin.
    //   2. Scale-relative epsilon: eps_rel = eps * max(scale, 1), where scale is
    //      the bbox diagonal, so a tolerance means the same thing at mm and at m.
    //   3. One tolerance budget derived from a single ModelAbsoluteTolerance, so a
    //      pipeline does not run several unreconciled tolerance systems (the
    //      standing Frahan three-tolerance bug, gotcha T5).
    //
    // Rhino-free: plain double arrays / tuples, headless-testable.
    // =========================================================================
    public static class GeometryNumerics
    {
        /// <summary>Centroid (mean) of a flat xyz coordinate array.</summary>
        public static (double X, double Y, double Z) Centroid(IReadOnlyList<double> coordsXyz)
        {
            if (coordsXyz == null || coordsXyz.Count < 3) return (0, 0, 0);
            double sx = 0, sy = 0, sz = 0;
            long n = coordsXyz.Count / 3;
            for (int i = 0; i + 2 < coordsXyz.Count; i += 3) { sx += coordsXyz[i]; sy += coordsXyz[i + 1]; sz += coordsXyz[i + 2]; }
            return (sx / n, sy / n, sz / n);
        }

        /// <summary>Recenter a flat xyz array to its centroid. Returns the shifted copy and
        /// the centroid (add it back to emit). The headline T1 fix.</summary>
        public static double[] Recenter(IReadOnlyList<double> coordsXyz, out (double X, double Y, double Z) centroid)
        {
            centroid = Centroid(coordsXyz);
            var outc = new double[coordsXyz.Count];
            for (int i = 0; i + 2 < coordsXyz.Count; i += 3)
            {
                outc[i] = coordsXyz[i] - centroid.X;
                outc[i + 1] = coordsXyz[i + 1] - centroid.Y;
                outc[i + 2] = coordsXyz[i + 2] - centroid.Z;
            }
            return outc;
        }

        /// <summary>Axis-aligned bbox of a flat xyz array.</summary>
        public static (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
            BoundingBox(IReadOnlyList<double> coordsXyz)
        {
            double mnx = double.PositiveInfinity, mny = double.PositiveInfinity, mnz = double.PositiveInfinity;
            double mxx = double.NegativeInfinity, mxy = double.NegativeInfinity, mxz = double.NegativeInfinity;
            for (int i = 0; i + 2 < coordsXyz.Count; i += 3)
            {
                double x = coordsXyz[i], y = coordsXyz[i + 1], z = coordsXyz[i + 2];
                if (x < mnx) mnx = x; if (y < mny) mny = y; if (z < mnz) mnz = z;
                if (x > mxx) mxx = x; if (y > mxy) mxy = y; if (z > mxz) mxz = z;
            }
            return (mnx, mny, mnz, mxx, mxy, mxz);
        }

        /// <summary>Bbox diagonal length = the natural scale of a point set.</summary>
        public static double BoundingBoxDiagonal(IReadOnlyList<double> coordsXyz)
        {
            if (coordsXyz == null || coordsXyz.Count < 3) return 0.0;
            var b = BoundingBox(coordsXyz);
            double dx = b.MaxX - b.MinX, dy = b.MaxY - b.MinY, dz = b.MaxZ - b.MinZ;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return double.IsNaN(d) || double.IsInfinity(d) ? 0.0 : d;
        }

        /// <summary>Scale-relative epsilon: baseEps * max(scale, 1). Use the bbox diagonal as
        /// the scale so a tolerance is meaningful at mm and at m (T2). A fixed absolute eps at
        /// architectural scale is the bug this prevents.</summary>
        public static double ScaleRelativeEpsilon(double baseEps, double scale)
            => baseEps * Math.Max(Math.Abs(scale), 1.0);

        /// <summary>Relative comparison: |a-b| &lt;= eps * max(1, |a|, |b|). The correct
        /// near-equal test at any coordinate magnitude.</summary>
        public static bool ApproxEqual(double a, double b, double relEps)
            => Math.Abs(a - b) <= relEps * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));

        /// <summary>
        /// One tolerance budget derived from a single model absolute tolerance, so a pipeline
        /// runs ONE reconciled tolerance system instead of several (gotcha T5). All members
        /// scale-relative to the supplied geometry scale.
        /// </summary>
        public readonly struct ToleranceBudget
        {
            public double Model { get; }          // base model absolute tolerance, scaled
            public double Join { get; }           // edge/vertex join (looser)
            public double Intersection { get; }   // boolean/intersection decisions (tighter)
            public double Snap { get; }           // snap-rounding grid

            private ToleranceBudget(double model, double join, double intersection, double snap)
            { Model = model; Join = join; Intersection = intersection; Snap = snap; }

            /// <summary>Build from one model absolute tolerance and a geometry scale (bbox
            /// diagonal). Join = 10x model, Intersection = 0.1x model, Snap = model/100, all
            /// scale-relative. Tune the multipliers per algorithm, but keep ONE source.</summary>
            public static ToleranceBudget From(double modelAbsoluteTolerance, double scale)
            {
                double m = ScaleRelativeEpsilon(modelAbsoluteTolerance, scale);
                return new ToleranceBudget(m, m * 10.0, m * 0.1, m * 0.01);
            }
        }

        /// <summary>
        /// Largest scaling factor that keeps scale*maxCoord under the int64 ceiling with a
        /// safety margin (gotcha T6, Clipper2 int64 overflow at large scale). Recenter first
        /// (so maxCoord is the bbox extent, not the world position), then use this to pick the
        /// integer scale instead of a fixed 1e6.</summary>
        public static double SafeIntegerScale(double maxAbsCoord, double requested = 1e6, double margin = 0.01)
        {
            if (maxAbsCoord <= 0) return requested;
            double ceiling = long.MaxValue * margin / maxAbsCoord;
            return Math.Min(requested, ceiling);
        }
    }
}
