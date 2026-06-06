using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using Rhino;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Spatial-3D ICP. Each iteration:
    ///   (1) push A through current transform,
    ///   (2) find nearest 3D point on B's polyline curve,
    ///   (3) solve 6-DoF rigid alignment via 3x3 Kabsch SVD,
    ///   (4) reject the step if A's centroid would lie inside B's
    ///       projected interior (sibling penetration) or, when a
    ///       substrate Brep is provided, if any sample lies on the
    ///       wrong side of the substrate normal.
    /// The reflection guard (D[2,2] sign flip) is mandatory; without
    /// it mirror-image alignments are returned in place of rotations.
    /// </summary>
    public sealed class ConstrainedIcp3D
    {
        private readonly IcpOptions _opt;

        public ConstrainedIcp3D(IcpOptions? opt = null)
        {
            _opt = opt ?? new IcpOptions();
        }

        public MatchResult Refine(
            Segment a,
            Segment b,
            Panel panelB,
            Brep? substrate,
            Transform initial)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (panelB == null) throw new ArgumentNullException(nameof(panelB));

            var aPts = SamplePolyline(a.LocalPolyline, _opt.SamplesPerSegment);
            var bCurve = b.LocalPolyline.ToPolylineCurve();

            // Order-preserving correspondence (opt-in) samples B once into a
            // fixed ordered sequence; free nearest-neighbour does not need it.
            var bPts = _opt.NonCrossingCorrespondence
                ? SamplePolyline(b.LocalPolyline, _opt.SamplesPerSegment)
                : null;

            // Degenerate-input guard (safety-preserving): with no samples the
            // Kabsch SVD is undefined. Return the initial transform untouched.
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
                    // ORDER-PRESERVING correspondence in 3D. Build a monotone,
                    // non-crossing pairing between the transformed A samples
                    // and the (fixed) B samples, then run Kabsch on only the
                    // matched subset. MatchClosed tries the reversed
                    // orientation too (complementary rims run oppositely).
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

                Transform delta = Kabsch3D(srcPts, bMatch);

                Transform trial = Transform.Multiply(delta, current);
                bool ok = NotPenetrating(aPts, panelB, trial)
                       && (substrate == null || OnCorrectSubstrateSide(aPts, substrate, trial));
                if (ok)
                {
                    current = trial;
                }
                else
                {
                    residual *= _opt.PenetrationPenalty;
                }

                Transform3DChange(delta, out double dt, out double drDeg);
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

        private static Transform Kabsch3D(Point3d[] src, Point3d[] dst)
        {
            int n = src.Length;
            double sx = 0, sy = 0, sz = 0, dx = 0, dy = 0, dz = 0;
            for (int i = 0; i < n; i++)
            {
                sx += src[i].X; sy += src[i].Y; sz += src[i].Z;
                dx += dst[i].X; dy += dst[i].Y; dz += dst[i].Z;
            }
            sx /= n; sy /= n; sz /= n; dx /= n; dy /= n; dz /= n;

            var H = Matrix<double>.Build.Dense(3, 3);
            for (int i = 0; i < n; i++)
            {
                double ax = src[i].X - sx, ay = src[i].Y - sy, az = src[i].Z - sz;
                double bx = dst[i].X - dx, by = dst[i].Y - dy, bz = dst[i].Z - dz;
                H[0, 0] += ax * bx; H[0, 1] += ax * by; H[0, 2] += ax * bz;
                H[1, 0] += ay * bx; H[1, 1] += ay * by; H[1, 2] += ay * bz;
                H[2, 0] += az * bx; H[2, 1] += az * by; H[2, 2] += az * bz;
            }

            var svd = H.Svd(true);
            var U = svd.U;
            var V = svd.VT.Transpose();

            // Reflection guard: must use Determinant(), not Math.Sign(det) —
            // see spec D8 (the sign function returns 0 on degenerate input
            // and would skip the flip).
            double detVUt = (V * U.Transpose()).Determinant();
            var D = Matrix<double>.Build.DenseDiagonal(3, 3, 1.0);
            D[2, 2] = detVUt >= 0 ? 1.0 : -1.0;

            var R = V * D * U.Transpose();

            var t = Transform.Identity;
            t.M00 = R[0, 0]; t.M01 = R[0, 1]; t.M02 = R[0, 2];
            t.M10 = R[1, 0]; t.M11 = R[1, 1]; t.M12 = R[1, 2];
            t.M20 = R[2, 0]; t.M21 = R[2, 1]; t.M22 = R[2, 2];
            t.M03 = dx - (R[0, 0] * sx + R[0, 1] * sy + R[0, 2] * sz);
            t.M13 = dy - (R[1, 0] * sx + R[1, 1] * sy + R[1, 2] * sz);
            t.M23 = dz - (R[2, 0] * sx + R[2, 1] * sy + R[2, 2] * sz);
            return t;
        }

        private static bool NotPenetrating(IList<Point3d> aPts, Panel panelB, Transform trial)
        {
            double cx = 0, cy = 0, cz = 0;
            foreach (var p in aPts) { cx += p.X; cy += p.Y; cz += p.Z; }
            cx /= aPts.Count; cy /= aPts.Count; cz /= aPts.Count;
            var centroid = new Point3d(cx, cy, cz);
            centroid.Transform(trial);

            var bFrame = panelB.LocalFrame;
            if (!bFrame.RemapToPlaneSpace(centroid, out Point3d projected))
                return true;

            var flatten = Transform.PlaneToPlane(bFrame, Plane.WorldXY);
            var flatB = (Curve)panelB.SourceContour.DuplicateCurve();
            flatB.Transform(flatten);
            if (!flatB.IsClosed) return true;

            var test = flatB.Contains(
                new Point3d(projected.X, projected.Y, 0),
                Plane.WorldXY,
                RhinoMath.SqrtEpsilon);
            return test != PointContainment.Inside;
        }

        private static bool OnCorrectSubstrateSide(
            IList<Point3d> aPts, Brep substrate, Transform trial)
        {
            int n = Math.Min(8, aPts.Count);
            double tol = RhinoMath.SqrtEpsilon;
            for (int i = 0; i < n; i++)
            {
                var p = aPts[i * aPts.Count / Math.Max(1, n)];
                p.Transform(trial);
                if (!substrate.ClosestPoint(
                        p,
                        out Point3d cp,
                        out _,
                        out _,
                        out _,
                        double.MaxValue,
                        out Vector3d normal))
                    continue;
                Vector3d delta = p - cp;
                if ((delta * normal) < -tol) return false;
            }
            return true;
        }

        private static List<Point3d> SamplePolyline(Polyline p, int n)
        {
            var crv = p.ToPolylineCurve();
            var ts = crv.DivideByCount(Math.Max(1, n - 1), true);
            var pts = new List<Point3d>(ts.Length);
            foreach (var t in ts) pts.Add(crv.PointAt(t));
            return pts;
        }

        private static void Transform3DChange(Transform t, out double dTrans, out double dRotDeg)
        {
            dTrans = Math.Sqrt(t.M03 * t.M03 + t.M13 * t.M13 + t.M23 * t.M23);
            double trace = t.M00 + t.M11 + t.M22;
            double cos = Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0));
            dRotDeg = RhinoMath.ToDegrees(Math.Acos(cos));
        }
    }
}
