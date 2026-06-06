using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Iterative-closest-point alignment of segment A onto segment B
    /// in the XY plane. Each iteration:
    ///   (1) push A through the current transform,
    ///   (2) find nearest point on B for each A sample,
    ///   (3) solve closed-form 2D rigid alignment (Kabsch/Umeyama in 2D),
    ///   (4) reject the step if A's centroid would lie inside B's panel
    ///       (penetration), penalising the residual instead of accepting.
    /// </summary>
    public sealed class ConstrainedIcp2D
    {
        private readonly IcpOptions _opt;

        public ConstrainedIcp2D(IcpOptions? opt = null)
        {
            _opt = opt ?? new IcpOptions();
        }

        public MatchResult Refine(Segment a, Segment b, Panel panelB, Transform initial)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (panelB == null) throw new ArgumentNullException(nameof(panelB));

            var aPts = SamplePolyline(a.LocalPolyline, _opt.SamplesPerSegment);
            var bCurve = b.LocalPolyline.ToPolylineCurve();
            var bInteriorCurve = panelB.SourceContour;

            // Order-preserving correspondence (opt-in) samples B once into a
            // fixed ordered sequence; free nearest-neighbour does not need it.
            var bPts = _opt.NonCrossingCorrespondence
                ? SamplePolyline(b.LocalPolyline, _opt.SamplesPerSegment)
                : null;

            // Degenerate-input guard (safety-preserving): with no samples the
            // SVD step is undefined. Return the initial transform untouched.
            if (aPts.Count == 0)
                return new MatchResult(a, b, initial, double.PositiveInfinity, false, 0);

            Transform current = initial;
            double prevResidual = double.PositiveInfinity;
            bool converged = false;
            int iter = 0;

            for (iter = 0; iter < _opt.MaxIterations; iter++)
            {
                var aWorld = new Point3d[aPts.Count];
                for (int i = 0; i < aPts.Count; i++)
                {
                    var p = aPts[i];
                    p.Transform(current);
                    aWorld[i] = p;
                }

                Point3d[] srcPts;
                Point3d[] bMatch;
                double residual;

                if (_opt.NonCrossingCorrespondence && bPts != null && bPts.Count > 0)
                {
                    // ORDER-PRESERVING correspondence. Build a monotone,
                    // non-crossing pairing between the transformed A samples
                    // and the (fixed) B samples, then align only the matched
                    // subset. MatchClosed also tries the reversed orientation,
                    // which is the correct sense for complementary fracture
                    // rims (they traverse the shared curve oppositely).
                    var pairs = OrderedBoundaryMatcher.MatchClosed(
                        aWorld, bPts,
                        phaseOffset: 0,
                        offsetBracket: 0,
                        maxGap: _opt.NonCrossingMaxGap);

                    int k = pairs.Count;
                    if (k == 0)
                        return new MatchResult(a, b, current, prevResidual, converged, iter + 1);

                    srcPts = new Point3d[k];
                    bMatch = new Point3d[k];
                    residual = 0.0;
                    for (int i = 0; i < k; i++)
                    {
                        srcPts[i] = aWorld[pairs[i].A];
                        bMatch[i] = bPts[pairs[i].B];
                        residual += srcPts[i].DistanceTo(bMatch[i]);
                    }
                    residual /= k;
                }
                else
                {
                    srcPts = aWorld;
                    bMatch = new Point3d[aWorld.Length];
                    residual = 0.0;
                    for (int i = 0; i < aWorld.Length; i++)
                    {
                        bCurve.ClosestPoint(aWorld[i], out double t);
                        bMatch[i] = bCurve.PointAt(t);
                        residual += aWorld[i].DistanceTo(bMatch[i]);
                    }
                    residual /= aWorld.Length;
                }

                Transform delta = SvdRigid2D(srcPts, bMatch);

                Transform trial = Transform.Multiply(delta, current);
                Point3d aCentroidTrial = Centroid(aPts);
                aCentroidTrial.Transform(trial);
                if (PointInsideClosed(aCentroidTrial, bInteriorCurve))
                {
                    residual *= _opt.PenetrationPenalty;
                }
                else
                {
                    current = trial;
                }

                TransformChange(delta, out double dt, out double drDeg);
                if (dt < _opt.TranslationTol && drDeg < _opt.RotationTolDeg)
                {
                    converged = true;
                    break;
                }
                if (Math.Abs(prevResidual - residual) < _opt.TranslationTol)
                {
                    converged = true;
                    break;
                }
                prevResidual = residual;
            }

            return new MatchResult(a, b, current, prevResidual, converged, iter + 1);
        }

        private static List<Point3d> SamplePolyline(Polyline p, int n)
        {
            var crv = p.ToPolylineCurve();
            var ts = crv.DivideByCount(Math.Max(1, n - 1), true);
            var pts = new List<Point3d>(ts.Length);
            foreach (var t in ts) pts.Add(crv.PointAt(t));
            return pts;
        }

        private static Point3d Centroid(IList<Point3d> pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
            return new Point3d(x / pts.Count, y / pts.Count, z / pts.Count);
        }

        private static bool PointInsideClosed(Point3d p, Curve closed)
        {
            if (closed == null || !closed.IsClosed) return false;
            var test = closed.Contains(p, Plane.WorldXY, RhinoMath.SqrtEpsilon);
            return test == PointContainment.Inside;
        }

        /// <summary>Closed-form 2D rigid alignment via Kabsch/Umeyama reduced to atan2.</summary>
        private static Transform SvdRigid2D(Point3d[] src, Point3d[] dst)
        {
            int n = src.Length;
            double sx = 0, sy = 0, dx = 0, dy = 0;
            for (int i = 0; i < n; i++)
            {
                sx += src[i].X; sy += src[i].Y;
                dx += dst[i].X; dy += dst[i].Y;
            }
            sx /= n; sy /= n; dx /= n; dy /= n;

            double sxx = 0, sxy = 0, syx = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double ax = src[i].X - sx, ay = src[i].Y - sy;
                double bx = dst[i].X - dx, by = dst[i].Y - dy;
                sxx += ax * bx; sxy += ax * by;
                syx += ay * bx; syy += ay * by;
            }

            double theta = Math.Atan2(sxy - syx, sxx + syy);
            double c = Math.Cos(theta), s = Math.Sin(theta);

            var t = Transform.Identity;
            t.M00 = c; t.M01 = -s;
            t.M10 = s; t.M11 = c;
            t.M03 = dx - (c * sx - s * sy);
            t.M13 = dy - (s * sx + c * sy);
            return t;
        }

        private static void TransformChange(Transform t, out double dTrans, out double dRotDeg)
        {
            dTrans = Math.Sqrt(t.M03 * t.M03 + t.M13 * t.M13);
            double cos = Math.Max(-1.0, Math.Min(1.0, t.M00));
            dRotDeg = RhinoMath.ToDegrees(Math.Acos(cos));
        }
    }
}
