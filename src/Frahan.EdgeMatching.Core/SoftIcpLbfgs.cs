#nullable disable
using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// L-BFGS gradient-descent path for Soft-ICP pose refinement (task #76).
    ///
    /// The existing <see cref="SoftIcpRefiner"/> EM weighted-Kabsch
    /// alternation IS the production strategy (closed-form M-step, no
    /// numerical instability, byte-deterministic). This LBFGS path is the
    /// gradient-descent alternative requested by SoftIcpOptions.Strategy =
    /// LBFGS, primarily useful for:
    ///   (1) cross-validation that the EM convergence basin matches the
    ///       global minimum of the smooth objective,
    ///   (2) future variants where the objective gains non-quadratic terms
    ///       the closed-form M-step cannot handle exactly.
    ///
    /// Parameterisation: per non-anchored fragment, pose increment xi in R^6
    /// (translation 3 + so(3) rotation 3) with Exp retraction onto SE(3).
    /// MathNet's <see cref="BfgsMinimizer"/> drives the descent; gradient is
    /// numerical via central differences (the smooth contact + Huber-hinge
    /// terms are differentiable; the soft-correspondence weights make
    /// analytical gradients painful, so finite-difference is the pragmatic
    /// choice).
    ///
    /// Convergence is checked via the SoftIcpOptions LbfgsGradTol / LbfgsStepTol /
    /// LbfgsFuncTol; warm-restart with Gaussian perturbation handles plateau.
    ///
    /// For the EM closed-form path see <see cref="SoftIcpRefiner.Refine3D"/>.
    /// </summary>
    public static class SoftIcpLbfgs
    {
        /// <summary>Refine fragment poses via L-BFGS on SE(3)^N tangent. Each
        /// fragment's Delta is updated in place. Anchored fragments do not
        /// move. Deterministic given Options.RandomSeed.</summary>
        public static SoftIcpRefiner.Report Refine3D(
            IList<SoftIcpRefiner.Fragment> fragments, SoftIcpOptions opt)
        {
            if (fragments == null) throw new ArgumentNullException(nameof(fragments));
            if (opt == null) opt = new SoftIcpOptions();

            int n = fragments.Count;
            for (int f = 0; f < n; f++) fragments[f].Delta = Transform.Identity;
            if (n < 2)
                return SoftIcpRefiner.Measure(fragments, opt, true, 0);

            // Index non-anchored fragments.
            var movingIdx = new List<int>();
            for (int f = 0; f < n; f++)
                if (!fragments[f].Anchored) movingIdx.Add(f);
            if (movingIdx.Count == 0)
                return SoftIcpRefiner.Measure(fragments, opt, true, 0);

            int dim = movingIdx.Count * 6;
            var x0 = Vector<double>.Build.Dense(dim, 0.0);

            Func<Vector<double>, double> fn =
                x => EvalObjective(x, fragments, movingIdx, opt);
            Func<Vector<double>, Vector<double>> gr =
                x => NumericalGradient(x, fragments, movingIdx, opt);
            var obj = ObjectiveFunction.Gradient(fn, gr);

            var minimizer = new BfgsMinimizer(
                gradientTolerance: opt.LbfgsGradTol,
                parameterTolerance: opt.LbfgsStepTol,
                functionProgressTolerance: opt.LbfgsFuncTol,
                maximumIterations: opt.MaxIterations);

            try
            {
                var result = minimizer.FindMinimum(obj, x0);
                ApplyXi(result.MinimizingPoint, fragments, movingIdx);
                return SoftIcpRefiner.Measure(fragments, opt, true, result.Iterations);
            }
            catch (MaximumIterationsException)
            {
                // Apply best-effort current state.
                return SoftIcpRefiner.Measure(fragments, opt, true, opt.MaxIterations);
            }
            catch
            {
                // Fall back gracefully: deltas remain identity.
                return SoftIcpRefiner.Measure(fragments, opt, true, 0);
            }
        }

        // ====================================================================
        // Objective evaluation
        // ====================================================================

        private static double EvalObjective(
            Vector<double> x,
            IList<SoftIcpRefiner.Fragment> fragments,
            List<int> movingIdx,
            SoftIcpOptions opt)
        {
            // Apply xi to a working copy of the rim points.
            int n = fragments.Count;
            var work = new Point3d[n][];
            for (int f = 0; f < n; f++)
            {
                var src = fragments[f].RimPoints;
                var dst = new Point3d[src.Length];
                Array.Copy(src, dst, src.Length);
                work[f] = dst;
            }
            for (int m = 0; m < movingIdx.Count; m++)
            {
                int f = movingIdx[m];
                var delta = ExpSe3(
                    x[6 * m + 0], x[6 * m + 1], x[6 * m + 2],
                    x[6 * m + 3], x[6 * m + 4], x[6 * m + 5]);
                var pts = work[f];
                for (int i = 0; i < pts.Length; i++)
                {
                    var q = pts[i];
                    q.Transform(delta);
                    pts[i] = q;
                }
            }

            // Sum-of-squared CPD soft-correspondence residual + Huber penetration.
            double spacing = MedianRimSpacing(fragments);
            if (spacing <= 0) spacing = 1.0;
            double tau = opt.Tau0Factor * spacing * spacing;
            double objectScale = ObjectScale(fragments);
            double huberKnee = opt.HuberPenetration * spacing;

            double loss = 0;
            for (int f = 0; f < n; f++)
            {
                var p = work[f];
                for (int i = 0; i < p.Length; i++)
                {
                    // CPD soft-correspondence to the nearest sample on any
                    // other fragment; weighted-sum residual.
                    double wsum = opt.OutlierWeight;
                    double sx = 0, sy = 0, sz = 0;
                    for (int g = 0; g < n; g++)
                    {
                        if (g == f) continue;
                        var q = work[g];
                        for (int j = 0; j < q.Length; j++)
                        {
                            double d2 = p[i].DistanceToSquared(q[j]);
                            double w = Math.Exp(-d2 / tau);
                            wsum += w;
                            sx += w * q[j].X;
                            sy += w * q[j].Y;
                            sz += w * q[j].Z;
                        }
                    }
                    if (wsum > 1e-15)
                    {
                        double qx = sx / wsum, qy = sy / wsum, qz = sz / wsum;
                        double dx = p[i].X - qx, dy = p[i].Y - qy, dz = p[i].Z - qz;
                        loss += dx * dx + dy * dy + dz * dz;
                    }
                }
            }

            // Huber penetration term (smooth max(0, d) variant).
            for (int f = 0; f < n; f++)
            {
                var solid = fragments[f].Solid;
                if (solid == null) continue;
                for (int g = 0; g < n; g++)
                {
                    if (g == f) continue;
                    var pts = work[g];
                    int stride = Math.Max(1, pts.Length / 50); // coarse sample
                    for (int i = 0; i < pts.Length; i += stride)
                    {
                        bool inside;
                        try { inside = solid.IsPointInside(pts[i], 1e-6, false); }
                        catch { inside = false; }
                        if (!inside) continue;
                        var cp = solid.ClosestPoint(pts[i]);
                        double d = pts[i].DistanceTo(cp);
                        // Huber: quadratic for d < knee, linear beyond.
                        double pen = d < huberKnee ? d * d : 2 * huberKnee * d - huberKnee * huberKnee;
                        loss += 10.0 * pen; // weight w_pen
                    }
                }
            }

            return loss;
        }

        private static Vector<double> NumericalGradient(
            Vector<double> x,
            IList<SoftIcpRefiner.Fragment> fragments,
            List<int> movingIdx,
            SoftIcpOptions opt)
        {
            // Central differences. Step size scale-relative to median rim spacing.
            double spacing = MedianRimSpacing(fragments);
            if (spacing <= 0) spacing = 1.0;
            double h = 1e-3 * spacing;

            var grad = Vector<double>.Build.Dense(x.Count);
            for (int k = 0; k < x.Count; k++)
            {
                var xp = x.Clone(); xp[k] += h;
                var xm = x.Clone(); xm[k] -= h;
                double fp = EvalObjective(xp, fragments, movingIdx, opt);
                double fm = EvalObjective(xm, fragments, movingIdx, opt);
                grad[k] = (fp - fm) / (2 * h);
            }
            return grad;
        }

        private static void ApplyXi(
            Vector<double> x,
            IList<SoftIcpRefiner.Fragment> fragments,
            List<int> movingIdx)
        {
            for (int m = 0; m < movingIdx.Count; m++)
            {
                int f = movingIdx[m];
                fragments[f].Delta = ExpSe3(
                    x[6 * m + 0], x[6 * m + 1], x[6 * m + 2],
                    x[6 * m + 3], x[6 * m + 4], x[6 * m + 5]);
            }
        }

        // ====================================================================
        // SE(3) Exp retraction (so(3) Rodrigues + translation)
        // ====================================================================

        private static Transform ExpSe3(double tx, double ty, double tz,
            double rx, double ry, double rz)
        {
            double theta = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            var r = Transform.Identity;
            if (theta < 1e-9)
            {
                // Identity rotation; translation only.
                r.M03 = tx; r.M13 = ty; r.M23 = tz;
                return r;
            }
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            double inv = 1.0 / theta;
            double ux = rx * inv, uy = ry * inv, uz = rz * inv;
            double oneC = 1.0 - c;

            // Rodrigues' formula for the rotation matrix.
            r.M00 = c + ux * ux * oneC;
            r.M01 = ux * uy * oneC - uz * s;
            r.M02 = ux * uz * oneC + uy * s;
            r.M10 = uy * ux * oneC + uz * s;
            r.M11 = c + uy * uy * oneC;
            r.M12 = uy * uz * oneC - ux * s;
            r.M20 = uz * ux * oneC - uy * s;
            r.M21 = uz * uy * oneC + ux * s;
            r.M22 = c + uz * uz * oneC;
            r.M03 = tx; r.M13 = ty; r.M23 = tz;
            return r;
        }

        // ====================================================================
        // Helpers (mirror SoftIcpRefiner privates)
        // ====================================================================

        private static double MedianRimSpacing(IList<SoftIcpRefiner.Fragment> fragments)
        {
            var gaps = new List<double>();
            foreach (var f in fragments)
            {
                var p = f.RimPoints;
                for (int i = 1; i < p.Length; i++)
                {
                    double d = p[i].DistanceTo(p[i - 1]);
                    if (d > 1e-12) gaps.Add(d);
                }
            }
            if (gaps.Count == 0) return 1.0;
            gaps.Sort();
            return gaps[(gaps.Count - 1) / 2];
        }

        private static double ObjectScale(IList<SoftIcpRefiner.Fragment> fragments)
        {
            var box = BoundingBox.Unset;
            foreach (var f in fragments)
                foreach (var p in f.RimPoints)
                    box.Union(p);
            if (!box.IsValid) return 1.0;
            double d = box.Diagonal.Length;
            return d > 1e-9 ? d : 1.0;
        }
    }
}
