#nullable disable
using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using Rhino;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Differentiable-style Soft-ICP (gradient-descent / EM) pose refiner for
    /// OPEN-mesh fragment reassembly (roadmap Pillar A, Phase 2+4; design basis
    /// <c>wiki/research/differentiable_edge_matching.md §3</c>). Refines the
    /// placed fragment poses so their fracture RIMS come into CONTACT while their
    /// SOLIDS do not interpenetrate, by minimising one smooth objective
    ///
    ///   L(poses) = w_contact * SoftRimCorrespondenceSSD   (pull matched rims to touch)
    ///            + w_pen     * PenetrationHinge            (push solids apart, no overlap)
    ///
    /// with the contact term balancing the penetration term so rims meet at the
    /// surface and solids stop overlapping.
    ///
    /// SOFT CORRESPONDENCE (CPD, Myronenko-Song 2010). Between a fragment's rim
    /// samples p_i and the NEIGHBOURING fragments' rim samples q_j, the weights
    /// are w_ij = softmax(-||T p_i - q_j||^2 / tau) with a uniform OUTLIER term
    /// that auto-downweights non-overlapping rim tails. The soft target is
    /// q-bar_i = sum_j w_ij q_j and the confidence c_i = sum_j w_ij. tau is
    /// SCALE-RELATIVE: tau0 = (median rim-sample spacing)^2 and is geometrically
    /// annealed each outer iteration (the cross-cutting scale rule).
    ///
    /// PENETRATION HINGE (smooth, non-penetration). Interpenetration of a
    /// fragment's solid against its neighbours is measured exactly as
    /// SettleContactComponent / OverlapResolver2D do: close OPEN meshes with
    /// FillHoles for the inside test (3D) or use the closed contour (2D), find
    /// the deepest inside sample, and penalise positive penetration depth with a
    /// smooth hinge lambda*max(0, depth)^2. The hinge gradient is a translation
    /// that pushes the overlapping solid out along the minimum-translation
    /// direction.
    ///
    /// POSE ON THE LIE ALGEBRA. Poses live on SE(2) [vx,vy,theta] / SE(3)
    /// [rho; omega(so3)] and retract through Exp, left-composed into the existing
    /// Transform (the Panel/Solve convention: T_new = Exp(xi) * T_old). The
    /// translation part of every pose increment is the weighted-Kabsch / hinge
    /// step; <see cref="LieSe2.Exp"/> / <see cref="LieSe3.Exp"/> are the
    /// retraction primitives (also used for the perturbed-ground-truth harness
    /// demo and the unit tests).
    ///
    /// OPTIMISER. EM weighted-Kabsch alternation (the managed default; NO new
    /// dependency, byte-stable, runs in the bare test host):
    ///   E-step: soft weights w_ij and targets q-bar_i at the current pose.
    ///   M-step: weighted Kabsch on (p_i, q-bar_i) with confidence weights c_i
    ///           (the contact term), then a depenetration translation from the
    ///           hinge gradient (the non-penetration term).
    /// iterated with the tau-anneal. Anchor-locked (fragment 0 fixed) like
    /// Contact Settle. Deterministic: fixed iteration order, no randomness.
    ///
    /// OPT-IN. The solver default path never calls this; the caller (harness /
    /// later a GH component) invokes it on the placed <see cref="AssemblyState"/>.
    /// With <see cref="AssemblyOptions.SoftIcpRefine"/> false (default) nothing
    /// here runs, so the default path is byte-for-byte unchanged.
    ///
    /// This type uses RhinoCommon (Point3d, Transform, Mesh.IsPointInside for the
    /// 3D hinge) and MathNet (3x3 SVD for weighted Kabsch), the same dependencies
    /// ConstrainedIcp3D already pulls in.
    /// </summary>
    public static class SoftIcpRefiner
    {
        /// <summary>Per-fragment rim sample bundle for the refiner.</summary>
        public sealed class Fragment
        {
            /// <summary>Stable identity (used for the deterministic anchor order).</summary>
            public string Id { get; }

            /// <summary>
            /// Rim sample points in WORLD coordinates at the fragment's CURRENT
            /// pose (the placed pose). The refiner solves for a pose increment
            /// applied on top of these.
            /// </summary>
            public Point3d[] RimPoints { get; }

            /// <summary>
            /// CLOSED solid copy for the penetration inside-test, at the same
            /// CURRENT pose as <see cref="RimPoints"/>. For OPEN meshes the caller
            /// closes with FillHoles (mirror SettleContactComponent); may be null
            /// (then this fragment contributes no penetration term as the inside
            /// solid, only rim contact). 2D callers pass null and rely on the 2D
            /// contour hinge variant.
            /// </summary>
            public Mesh Solid { get; }

            /// <summary>
            /// CLOSED 2D contour for the planar penetration inside-test, at the
            /// CURRENT pose. Used by the 2D entry point; null in 3D.
            /// </summary>
            public PolylineCurve Contour2D { get; }

            /// <summary>When true the fragment is fixed (anchor); its pose never moves.</summary>
            public bool Anchored { get; set; }

            /// <summary>
            /// Accumulated pose increment the refiner has applied so far, world-
            /// left-composed: the placed pose becomes Delta * placedPose. Starts
            /// at identity; read back by the caller after <c>Refine*</c>.
            /// </summary>
            public Transform Delta { get; internal set; } = Transform.Identity;

            public Fragment(string id, Point3d[] rimPoints, Mesh solid = null,
                PolylineCurve contour2D = null, bool anchored = false)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                RimPoints = rimPoints ?? new Point3d[0];
                Solid = solid;
                Contour2D = contour2D;
                Anchored = anchored;
            }
        }

        /// <summary>Before/after diagnostics for the harness report.</summary>
        public readonly struct Report
        {
            /// <summary>Mean nearest-neighbour rim gap across matched neighbour rims.</summary>
            public readonly double MeanRimGap;
            /// <summary>Max penetration depth (3D model units) / overlap proxy (2D).</summary>
            public readonly double MaxPenetration;
            /// <summary>
            /// Count of rim samples that lie on a MATING interface (nearest
            /// opposing rim sample within the interface band). This is the number
            /// of samples driving the contact term; it grows as a perturbed rim is
            /// pulled back so more of it participates in the interface.
            /// </summary>
            public readonly int ContactSamples;
            /// <summary>Outer EM iterations actually run.</summary>
            public readonly int Iterations;

            public Report(double meanRimGap, double maxPenetration, int contactSamples, int iterations)
            {
                MeanRimGap = meanRimGap;
                MaxPenetration = maxPenetration;
                ContactSamples = contactSamples;
                Iterations = iterations;
            }
        }

        // ====================================================================
        // 3D entry point
        // ====================================================================

        /// <summary>
        /// Refine the poses of <paramref name="fragments"/> (3D). Each fragment's
        /// <see cref="Fragment.Delta"/> is updated in place. Anchored fragments do
        /// not move. Deterministic.
        /// </summary>
        public static Report Refine3D(IList<Fragment> fragments, SoftIcpOptions opt)
        {
            return Refine(fragments, opt, threeD: true);
        }

        // ====================================================================
        // 2D entry point
        // ====================================================================

        /// <summary>
        /// Refine the poses of <paramref name="fragments"/> (2D, XY plane). The
        /// rim samples are the closed-contour boundary; the penetration term uses
        /// the 2D contour inside-test. Each fragment's <see cref="Fragment.Delta"/>
        /// is updated in place. Anchored fragments do not move. Deterministic.
        /// </summary>
        public static Report Refine2D(IList<Fragment> fragments, SoftIcpOptions opt)
        {
            return Refine(fragments, opt, threeD: false);
        }

        // ====================================================================
        // Core EM loop (shared 2D / 3D)
        // ====================================================================

        private static Report Refine(IList<Fragment> fragments, SoftIcpOptions opt, bool threeD)
        {
            if (fragments == null) throw new ArgumentNullException(nameof(fragments));
            if (opt == null) opt = new SoftIcpOptions();

            int n = fragments.Count;
            // Reset increments so the call is idempotent on re-run.
            for (int f = 0; f < n; f++) fragments[f].Delta = Transform.Identity;

            if (n < 2)
                return Measure(fragments, opt, threeD, 0);

            // Scale-relative tau0 = (median rim-sample spacing)^2.
            double spacing = MedianRimSpacing(fragments);
            if (spacing <= 0) spacing = 1.0;
            double objectScale = ObjectScale(fragments);
            double contactBand = opt.ContactBandFactor * spacing;
            // Correspondence radius^2 (robust-ICP cutoff). Beyond this a neighbour
            // sample contributes zero contact weight, so only the true mating rim
            // drives alignment (a piece's far boundary is not dragged inward). It
            // is COARSE-TO-FINE: it starts wide enough to CATCH a perturbed rim
            // (CorrespondenceRadiusFactor * the larger of the rim spacing and the
            // current separation) and is annealed toward the spacing-relative floor
            // each iteration alongside tau, so early iterations capture the piece
            // and late iterations sharpen to the true contact.
            double corrFloor = opt.CorrespondenceRadiusFactor * spacing;
            double initialGap = InitialSeparation(fragments);
            double corrR = corrFloor;
            if (initialGap > corrR) corrR = initialGap + corrFloor; // catch the perturbation
            double corrR2 = corrR > 0 ? corrR * corrR : double.PositiveInfinity;
            double corrFloor2 = corrFloor > 0 ? corrFloor * corrFloor : double.PositiveInfinity;

            // Initial temperature is tied to the catch radius so the softmax has
            // meaningful weight at the current separation (tau0 ~ (catch radius/2)^2
            // means a neighbour at the catch radius still gets exp(-4) weight),
            // floored by the spacing-relative Tau0Factor for the already-near case.
            double tau = Math.Max(opt.Tau0Factor * (spacing * spacing), 0.25 * corrR2);
            if (tau <= 0) tau = spacing * spacing;

            // Current world pose of each fragment is identity-relative to the
            // sample arrays (the samples are already at the placed pose). The
            // increment we solve is composed on top. We keep moving points in a
            // working buffer updated each iteration.
            var work = new Point3d[n][];
            for (int f = 0; f < n; f++)
            {
                var src = fragments[f].RimPoints;
                var dst = new Point3d[src.Length];
                Array.Copy(src, dst, src.Length);
                work[f] = dst;
            }

            int iters = 0;
            for (iters = 0; iters < opt.MaxIterations; iters++)
            {
                bool anyMoved = false;
                // Deterministic fragment order: by index (stable input order).
                for (int f = 0; f < n; f++)
                {
                    if (fragments[f].Anchored) continue;
                    var p = work[f];
                    if (p.Length == 0) continue;

                    // ---- E-step: soft correspondence to all OTHER fragments'
                    //      current rim samples. q-bar_i, c_i (confidence). ----
                    var qbar = new Point3d[p.Length];
                    var conf = new double[p.Length];
                    SoftTargets(f, work, fragments, p, tau, corrR2, opt, threeD, qbar, conf);

                    // ---- Non-penetration coupling (UNIFIED into the contact
                    //      solve): any moving rim sample found INSIDE a neighbour
                    //      solid has its soft target REDIRECTED to the neighbour
                    //      SURFACE (the contact location) with full confidence, and
                    //      its current position pulled to that surface. Folding the
                    //      hinge into the SAME weighted-Kabsch target (rather than a
                    //      separate ejecting translation) means the contact pull and
                    //      the non-penetration push cannot fight: the solve finds the
                    //      rigid motion that best brings rims to their surface /
                    //      contact targets at once. This is the smooth
                    //      contact-vs-penetration balance (rims touch the surface,
                    //      solids stop overlapping). ----
                    ApplyPenetrationTargets(f, fragments, work, p, threeD, opt, objectScale, qbar, conf);

                    // ---- M-step: confidence-weighted Kabsch on the samples whose
                    //      confidence is above the floor, damped by the contact
                    //      relaxation (fractional step, stable). ----
                    Transform delta = WeightedRigid(p, qbar, conf, threeD, opt.MinConfidence);
                    delta = DampDelta(delta, opt.ContactStep);

                    // Retraction: left-compose the increment, mirror Panel.Apply.
                    fragments[f].Delta = Transform.Multiply(delta, fragments[f].Delta);

                    // Update the working buffer with the just-applied increment.
                    for (int i = 0; i < p.Length; i++)
                    {
                        var q = p[i];
                        q.Transform(delta);
                        p[i] = q;
                    }

                    TransformMagnitude(delta, objectScale, out double dt, out double dr);
                    if (dt > opt.ConvergeTransFactor * objectScale || dr > opt.ConvergeRotDeg)
                        anyMoved = true;
                }

                // Geometric tau anneal toward the contact floor.
                tau *= opt.TauAnneal;
                double tauFloor = opt.TauFloorFactor * (spacing * spacing);
                if (tau < tauFloor) tau = tauFloor;
                // Anneal the correspondence radius coarse-to-fine toward its floor.
                corrR2 *= opt.TauAnneal * opt.TauAnneal; // radius shrinks ~sqrt(tau anneal)
                if (corrR2 < corrFloor2) corrR2 = corrFloor2;

                if (!anyMoved) { iters++; break; }
            }

            return Measure(fragments, opt, threeD, iters, contactBand);
        }

        // --------------------------------------------------------------------
        // E-step: CPD soft targets and confidence.
        // --------------------------------------------------------------------
        private static void SoftTargets(
            int self, Point3d[][] work, IList<Fragment> fragments, Point3d[] p,
            double tau, double corrR2, SoftIcpOptions opt, bool threeD,
            Point3d[] qbar, double[] conf)
        {
            int n = fragments.Count;
            // Uniform outlier mass (Myronenko-Song): a constant pseudo-weight that
            // auto-downweights samples with no close neighbour (non-overlapping
            // rim tails). Scaled by the outlier fraction.
            double outlier = opt.OutlierWeight;
            double invTau = tau > 0 ? 1.0 / tau : 0.0;

            // V3 batch 2 (speed, bit-identical): spatial-hash the other-fragment points so each
            // p_i sums only its in-radius neighbours, O(N P k) instead of O(N P^2). flatActual
            // holds true coords (used in the q-bar sum, including Z); flatHash zeroes Z in 2D so
            // the 3D radius query reproduces the XY (Dist2XY) correspondence exactly. Neighbours
            // come back in sorted global-index order = the original fragment-major scan order, so
            // the float reduction and q-bar are bit-identical to the brute scan. Proven on real
            // ETH1100 geometry (outputs/2026-06-04/algo_review_v3/evo_softicp): 2.95x, diff 0.0.
            int total = 0;
            for (int g = 0; g < n; g++) if (g != self) total += work[g].Length;
            var flatActual = new double[total * 3];
            var flatHash = new double[total * 3];
            int w0 = 0;
            for (int g = 0; g < n; g++)
            {
                if (g == self) continue;
                var q = work[g];
                for (int j = 0; j < q.Length; j++)
                {
                    flatActual[3 * w0] = q[j].X; flatActual[3 * w0 + 1] = q[j].Y; flatActual[3 * w0 + 2] = q[j].Z;
                    flatHash[3 * w0] = q[j].X; flatHash[3 * w0 + 1] = q[j].Y; flatHash[3 * w0 + 2] = threeD ? q[j].Z : 0.0;
                    w0++;
                }
            }
            double r = Math.Sqrt(corrR2);
            var hash = total > 0 ? new SpatialHash3D(flatHash, r) : null;

            for (int i = 0; i < p.Length; i++)
            {
                var pi = p[i];
                double wsum = outlier;
                double sx = 0, sy = 0, sz = 0;
                if (hash != null)
                {
                    var nbr = hash.QueryRadius(pi.X, pi.Y, threeD ? pi.Z : 0.0, r);
                    for (int t = 0; t < nbr.Count; t++)
                    {
                        int gi = nbr[t] * 3;
                        double ex = flatActual[gi] - pi.X;
                        double ey = flatActual[gi + 1] - pi.Y;
                        double ez = threeD ? flatActual[gi + 2] - pi.Z : 0.0;
                        double d2 = ex * ex + ey * ey + ez * ez;
                        if (d2 > corrR2) continue; // robust-ICP correspondence cutoff (boundary-exact)
                        double w = Math.Exp(-d2 * invTau);
                        wsum += w;
                        sx += w * flatActual[gi];
                        sy += w * flatActual[gi + 1];
                        sz += w * flatActual[gi + 2];
                    }
                }
                // q-bar over the matched neighbours only (outlier mass is in the
                // denominator so a sample with no real neighbour gets low
                // confidence and is effectively ignored by the weighted Kabsch).
                if (wsum > 1e-15)
                {
                    qbar[i] = new Point3d(sx / wsum, sy / wsum, sz / wsum);
                    conf[i] = (wsum - outlier) / wsum;
                }
                else
                {
                    qbar[i] = pi;
                    conf[i] = 0.0;
                }
            }
        }

        // --------------------------------------------------------------------
        // M-step (contact): confidence-weighted rigid alignment p -> qbar.
        // 2D reduces the rotation to atan2; 3D uses the 3x3 Kabsch SVD with the
        // mandatory reflection guard (mirror ConstrainedIcp3D).
        // --------------------------------------------------------------------
        private static Transform WeightedRigid(
            Point3d[] p, Point3d[] qbar, double[] conf, bool threeD, double minConf)
        {
            // Sum of weights; skip degenerate (no confident samples) -> identity.
            double wsum = 0;
            for (int i = 0; i < conf.Length; i++)
                if (conf[i] >= minConf) wsum += conf[i];
            if (wsum <= 1e-12) return Transform.Identity;

            // Weighted centroids.
            double sx = 0, sy = 0, sz = 0, dx = 0, dy = 0, dz = 0;
            for (int i = 0; i < p.Length; i++)
            {
                double w = conf[i] >= minConf ? conf[i] : 0.0;
                if (w <= 0) continue;
                sx += w * p[i].X; sy += w * p[i].Y; sz += w * p[i].Z;
                dx += w * qbar[i].X; dy += w * qbar[i].Y; dz += w * qbar[i].Z;
            }
            sx /= wsum; sy /= wsum; sz /= wsum;
            dx /= wsum; dy /= wsum; dz /= wsum;

            if (!threeD)
            {
                double sxx = 0, sxy = 0, syx = 0, syy = 0;
                for (int i = 0; i < p.Length; i++)
                {
                    double w = conf[i] >= minConf ? conf[i] : 0.0;
                    if (w <= 0) continue;
                    double ax = p[i].X - sx, ay = p[i].Y - sy;
                    double bx = qbar[i].X - dx, by = qbar[i].Y - dy;
                    sxx += w * ax * bx; sxy += w * ax * by;
                    syx += w * ay * bx; syy += w * ay * by;
                }
                double theta = Math.Atan2(sxy - syx, sxx + syy);
                double c = Math.Cos(theta), s = Math.Sin(theta);
                var t2 = Transform.Identity;
                t2.M00 = c; t2.M01 = -s;
                t2.M10 = s; t2.M11 = c;
                t2.M03 = dx - (c * sx - s * sy);
                t2.M13 = dy - (s * sx + c * sy);
                return t2;
            }

            var H = Matrix<double>.Build.Dense(3, 3);
            for (int i = 0; i < p.Length; i++)
            {
                double w = conf[i] >= minConf ? conf[i] : 0.0;
                if (w <= 0) continue;
                double ax = p[i].X - sx, ay = p[i].Y - sy, az = p[i].Z - sz;
                double bx = qbar[i].X - dx, by = qbar[i].Y - dy, bz = qbar[i].Z - dz;
                H[0, 0] += w * ax * bx; H[0, 1] += w * ax * by; H[0, 2] += w * ax * bz;
                H[1, 0] += w * ay * bx; H[1, 1] += w * ay * by; H[1, 2] += w * ay * bz;
                H[2, 0] += w * az * bx; H[2, 1] += w * az * by; H[2, 2] += w * az * bz;
            }

            var svd = H.Svd(true);
            var U = svd.U;
            var V = svd.VT.Transpose();
            // Reflection guard: use Determinant(), not Math.Sign (mirror Icp3D D8).
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

        // --------------------------------------------------------------------
        // Non-penetration coupling, folded into the contact target. For every
        // moving rim sample of `self` that lies INSIDE a neighbour solid (3D) or
        // contour (2D), its soft target q-bar is REDIRECTED to the neighbour
        // SURFACE point (the nearest boundary), with full confidence. The single
        // weighted-Kabsch then finds the rigid motion that best brings rims to
        // contact AND lifts penetrating samples to the surface at once, so the two
        // terms cannot fight (no separate ejecting translation). This realises the
        // smooth hinge max(0, depth) by pulling penetrating samples exactly to the
        // boundary (depth -> 0) rather than overshooting outward. 3D uses the
        // closed-solid inside-test (SettleContactComponent approach); 2D uses the
        // closed-contour Contains() test (OverlapResolver2D approach).
        // --------------------------------------------------------------------
        private static void ApplyPenetrationTargets(
            int self, IList<Fragment> fragments, Point3d[][] work, Point3d[] p,
            bool threeD, SoftIcpOptions opt, double objectScale,
            Point3d[] qbar, double[] conf)
        {
            int n = fragments.Count;
            double tol = opt.PenetrationTolFactor * objectScale;
            int stride = Math.Max(1, p.Length / opt.PenetrationSampleCap);

            for (int g = 0; g < n; g++)
            {
                if (g == self) continue;
                Transform otherDelta = fragments[g].Delta;

                if (threeD)
                {
                    var other = fragments[g].Solid;
                    if (other == null) continue;
                    for (int i = 0; i < p.Length; i += stride)
                    {
                        var pw = p[i];
                        var pl = pw;
                        if (!otherDelta.IsIdentity)
                        {
                            Transform inv;
                            if (otherDelta.TryGetInverse(out inv)) pl.Transform(inv);
                        }
                        bool inside;
                        try { inside = other.IsPointInside(pl, tol * 0.5, false); }
                        catch { inside = false; }
                        if (!inside) continue;
                        var cp = other.ClosestPoint(pl);            // surface in other's local
                        var surf = cp;
                        if (!otherDelta.IsIdentity) surf.Transform(otherDelta); // -> world
                        // Redirect this sample's target to the surface, full weight:
                        // the contact location it must touch (not penetrate).
                        qbar[i] = surf;
                        conf[i] = 1.0;
                    }
                }
                else
                {
                    var cc = fragments[g].Contour2D;
                    if (cc == null) continue;
                    var contour = (PolylineCurve)cc.DuplicateCurve();
                    if (!otherDelta.IsIdentity) contour.Transform(otherDelta);
                    if (!contour.IsClosed) continue;
                    for (int i = 0; i < p.Length; i += stride)
                    {
                        var test = contour.Contains(
                            new Point3d(p[i].X, p[i].Y, 0), Plane.WorldXY, RhinoMath.SqrtEpsilon);
                        if (test != PointContainment.Inside) continue;
                        contour.ClosestPoint(p[i], out double tt);
                        var surf = contour.PointAt(tt);
                        qbar[i] = new Point3d(surf.X, surf.Y, p[i].Z);
                        conf[i] = 1.0;
                    }
                }
            }
        }

        // ====================================================================
        // Measurement (before/after harness numbers)
        // ====================================================================

        /// <summary>
        /// Measure the current state (no pose change): mean nearest-neighbour rim
        /// gap, max penetration, contact-sample count. Call BEFORE refine (with
        /// every Delta=identity) and AFTER refine for the before/after report.
        /// </summary>
        public static Report Measure(IList<Fragment> fragments, SoftIcpOptions opt, bool threeD,
            int iterations = 0, double contactBand = -1)
        {
            int n = fragments.Count;
            // Apply current deltas to a working copy of the rim samples.
            var work = new Point3d[n][];
            for (int f = 0; f < n; f++)
            {
                var src = fragments[f].RimPoints;
                var dst = new Point3d[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var q = src[i];
                    q.Transform(fragments[f].Delta);
                    dst[i] = q;
                }
                work[f] = dst;
            }

            double spacing0 = MedianRimSpacing(fragments);
            if (spacing0 <= 0) spacing0 = 1.0;
            if (contactBand < 0)
                contactBand = (opt?.ContactBandFactor ?? 0.25) * spacing0;
            // Interface band: only rim samples whose nearest neighbour lies within
            // this distance are part of a MATING interface and count toward the
            // mean rim gap. A piece's far-side / outer boundary (no near neighbour)
            // is not a mating rim, so including it would mask whether the true rims
            // touch. Uses the correspondence radius (the loss's own definition of
            // "near"); wider if the radius is 0 (no cutoff).
            double interfaceBand = (opt != null && opt.CorrespondenceRadiusFactor > 0)
                ? opt.CorrespondenceRadiusFactor * spacing0
                : double.PositiveInfinity;

            // Mean nearest-neighbour rim gap over the MATING interface: for each
            // rim sample with a neighbour within the interface band, the distance
            // to the nearest rim sample on ANY other fragment, averaged. Drops
            // toward 0 as rims come into contact.
            double gapSum = 0; int gapCount = 0; int contact = 0;
            for (int f = 0; f < n; f++)
            {
                var p = work[f];
                for (int i = 0; i < p.Length; i++)
                {
                    double best = double.PositiveInfinity;
                    for (int g = 0; g < n; g++)
                    {
                        if (g == f) continue;
                        var q = work[g];
                        for (int j = 0; j < q.Length; j++)
                        {
                            double d = threeD ? p[i].DistanceTo(q[j]) : Math.Sqrt(Dist2XY(p[i], q[j]));
                            if (d < best) best = d;
                        }
                    }
                    if (best <= interfaceBand)
                    {
                        gapSum += best; gapCount++;
                        if (best <= contactBand) contact++;
                    }
                }
            }
            // If no sample lies on a mating interface (the pieces have drifted
            // apart), report the GLOBAL mean nearest-neighbour distance instead of
            // a false 0, so divergence is visible rather than masked.
            double meanGap;
            if (gapCount > 0)
            {
                meanGap = gapSum / gapCount;
            }
            else
            {
                double allSum = 0; int allCount = 0;
                for (int f = 0; f < n; f++)
                {
                    var p = work[f];
                    for (int i = 0; i < p.Length; i++)
                    {
                        double best = double.PositiveInfinity;
                        for (int g = 0; g < n; g++)
                        {
                            if (g == f) continue;
                            var q = work[g];
                            for (int j = 0; j < q.Length; j++)
                            {
                                double d = threeD ? p[i].DistanceTo(q[j]) : Math.Sqrt(Dist2XY(p[i], q[j]));
                                if (d < best) best = d;
                            }
                        }
                        if (!double.IsPositiveInfinity(best)) { allSum += best; allCount++; }
                    }
                }
                meanGap = allCount > 0 ? allSum / allCount : 0.0;
            }
            // Report the interface-sample count (samples participating in the
            // mating interface) -- grows as a perturbed rim is pulled back in.
            contact = gapCount;

            double maxPen = MeasureMaxPenetration(fragments, work, threeD, opt);
            return new Report(meanGap, maxPen, contact, iterations);
        }

        private static double MeasureMaxPenetration(
            IList<Fragment> fragments, Point3d[][] work, bool threeD, SoftIcpOptions opt)
        {
            int n = fragments.Count;
            double objectScale = ObjectScale(fragments);
            double tol = (opt?.PenetrationTolFactor ?? 0.002) * objectScale;
            double maxPen = 0;
            if (threeD)
            {
                for (int f = 0; f < n; f++)
                {
                    for (int g = 0; g < n; g++)
                    {
                        if (g == f) continue;
                        var other = fragments[g].Solid;
                        if (other == null) continue;
                        Transform otherDelta = fragments[g].Delta;
                        var sp = work[f];
                        int stride = Math.Max(1, sp.Length / (opt?.PenetrationSampleCap ?? 200));
                        for (int v = 0; v < sp.Length; v += stride)
                        {
                            var pl = sp[v];
                            if (!otherDelta.IsIdentity)
                            {
                                Transform inv;
                                if (otherDelta.TryGetInverse(out inv)) pl.Transform(inv);
                            }
                            bool inside;
                            try { inside = other.IsPointInside(pl, tol * 0.5, false); }
                            catch { inside = false; }
                            if (!inside) continue;
                            var cp = other.ClosestPoint(pl);
                            double d = pl.DistanceTo(cp);
                            if (d > maxPen) maxPen = d;
                        }
                    }
                }
            }
            else
            {
                for (int f = 0; f < n; f++)
                {
                    for (int g = 0; g < n; g++)
                    {
                        if (g == f) continue;
                        var cc = fragments[g].Contour2D;
                        if (cc == null) continue;
                        var contour = (PolylineCurve)cc.DuplicateCurve();
                        if (!fragments[g].Delta.IsIdentity) contour.Transform(fragments[g].Delta);
                        if (!contour.IsClosed) continue;
                        var sp = work[f];
                        for (int i = 0; i < sp.Length; i++)
                        {
                            var test = contour.Contains(
                                new Point3d(sp[i].X, sp[i].Y, 0), Plane.WorldXY, RhinoMath.SqrtEpsilon);
                            if (test != PointContainment.Inside) continue;
                            contour.ClosestPoint(sp[i], out double tt);
                            double d = sp[i].DistanceTo(contour.PointAt(tt));
                            if (d > maxPen) maxPen = d;
                        }
                    }
                }
            }
            return maxPen;
        }

        // ====================================================================
        // Geometry helpers
        // ====================================================================

        private static double Dist2XY(Point3d a, Point3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static Point3d Centroid(Point3d[] pts)
        {
            if (pts.Length == 0) return Point3d.Origin;
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < pts.Length; i++) { x += pts[i].X; y += pts[i].Y; z += pts[i].Z; }
            return new Point3d(x / pts.Length, y / pts.Length, z / pts.Length);
        }

        private static double MedianRimSpacing(IList<Fragment> fragments)
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

        // Median nearest-neighbour distance from each non-anchored fragment's rim
        // to ANY other fragment's rim, at the current (start) pose. A robust proxy
        // for how far a perturbed piece sits from its mating partner, used to set
        // the initial coarse correspondence radius so the contact term can catch it.
        private static double InitialSeparation(IList<Fragment> fragments)
        {
            int n = fragments.Count;
            var nn = new List<double>();
            for (int f = 0; f < n; f++)
            {
                if (fragments[f].Anchored) continue;
                var p = fragments[f].RimPoints;
                for (int i = 0; i < p.Length; i++)
                {
                    double best = double.PositiveInfinity;
                    for (int g = 0; g < n; g++)
                    {
                        if (g == f) continue;
                        var q = fragments[g].RimPoints;
                        for (int j = 0; j < q.Length; j++)
                        {
                            double d = p[i].DistanceTo(q[j]);
                            if (d < best) best = d;
                        }
                    }
                    if (!double.IsPositiveInfinity(best)) nn.Add(best);
                }
            }
            if (nn.Count == 0) return 0.0;
            nn.Sort();
            return nn[(nn.Count - 1) / 2];
        }

        private static double ObjectScale(IList<Fragment> fragments)
        {
            var box = BoundingBox.Unset;
            foreach (var f in fragments)
                foreach (var p in f.RimPoints)
                    box.Union(p);
            if (!box.IsValid) return 1.0;
            double d = box.Diagonal.Length;
            return d > 1e-9 ? d : 1.0;
        }

        // Fractional (damped) step of a rigid increment: scale the rotation angle
        // and the translation by `step` in (0,1]. Rebuilds through the Lie Exp map
        // so the damped increment is a proper SE(2)/SE(3) element (the Lie-algebra
        // retraction the design specifies), not a non-rigid matrix lerp.
        private static Transform DampDelta(Transform t, double step)
        {
            if (step >= 1.0) return t;
            if (step <= 0.0) return Transform.Identity;

            // Rotation -> axis-angle via the trace; translation is the M0x column.
            double trace = t.M00 + t.M11 + t.M22;
            double cos = Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0));
            double angle = Math.Acos(cos);
            double tx = t.M03, ty = t.M13, tz = t.M23;

            if (angle < 1e-9)
            {
                // Pure (or near-pure) translation: just scale it.
                return Transform.Translation(tx * step, ty * step, tz * step);
            }

            // Axis from the skew part R - R^T = 2 sin(angle) [axis]_x.
            double ax = t.M21 - t.M12;
            double ay = t.M02 - t.M20;
            double az = t.M10 - t.M01;
            double s2 = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (s2 < 1e-12)
            {
                // angle ~ pi (rare in a damping step); fall back to scaling
                // translation only, keep the full rotation (degenerate axis).
                var tt = t;
                tt.M03 = tx * step; tt.M13 = ty * step; tt.M23 = tz * step;
                return tt;
            }
            double inv = 1.0 / s2;
            double ux = ax * inv, uy = ay * inv, uz = az * inv;
            double scaledAngle = angle * step;

            // so(3) Exp of the scaled axis-angle; translation scaled linearly.
            var r = LieSe3.ExpSo3(ux * scaledAngle, uy * scaledAngle, uz * scaledAngle);
            r.M03 = tx * step;
            r.M13 = ty * step;
            r.M23 = tz * step;
            return r;
        }

        private static void TransformMagnitude(Transform t, double scale, out double dTrans, out double dRotDeg)
        {
            dTrans = Math.Sqrt(t.M03 * t.M03 + t.M13 * t.M13 + t.M23 * t.M23);
            double trace = t.M00 + t.M11 + t.M22;
            double cos = Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0));
            dRotDeg = RhinoMath.ToDegrees(Math.Acos(cos));
        }
    }
}
