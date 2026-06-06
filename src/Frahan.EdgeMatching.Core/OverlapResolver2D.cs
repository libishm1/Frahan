using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// R2 post-solve 2D rigid depenetration polish — the 2D analogue of
    /// <c>SettleContactComponent</c> (Frahan &gt; Kintsugi &gt; Contact Settle).
    /// Takes the placed contours from an <see cref="AssemblyState"/> and nudges
    /// overlapping ones apart with rigid TRANSLATIONS until pairwise overlap is
    /// within tolerance, so reassembled pieces end up touching at their cut edges
    /// but not interpenetrating.
    ///
    /// Algorithm (deterministic Jacobi relaxation, mirrors the 3D settle):
    ///   each iteration measures every pair's overlap area (2D curve boolean
    ///   intersection); for an overlapping pair it computes a separation
    ///   direction (difference of the two intersection-region / contour
    ///   centroids) and a step magnitude proportional to the penetration extent,
    ///   and accumulates half the correction on each piece (all of it on the
    ///   moving piece when its partner is anchor-locked). Corrections vanish as
    ///   soon as a pair stops overlapping, so touching pairs settle into contact
    ///   while overlapping pairs are pushed out. Anchored panels never move.
    ///
    /// Translation only: the matched orientation from the assembler is preserved.
    /// Pure geometry, no randomness: same inputs -&gt; same result. Opt-in via
    /// <see cref="AssemblyOptions.ResolveOverlap"/>; the solver never calls it.
    /// </summary>
    public static class OverlapResolver2D
    {
        /// <summary>
        /// Resolve overlaps in <paramref name="state"/> in place. Reads each
        /// placed panel's contour at its <see cref="AssemblyState.AppliedTransforms"/>
        /// pose, runs the relaxation, and writes the net translation back into
        /// <c>AppliedTransforms</c> (post-multiplied so the matched orientation is
        /// kept). Anchored panels (<see cref="Panel.IsAnchored"/>) are locked.
        /// </summary>
        /// <returns>The final maximum pairwise overlap fraction (overlap area over
        /// the smaller contour area), and the number of iterations run.</returns>
        public static (double maxOverlapFraction, int iterations) Resolve(
            AssemblyState state, AssemblyOptions opt)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (opt == null) throw new ArgumentNullException(nameof(opt));

            int n = state.PlacedPanels.Count;
            if (n < 2) return (0.0, 0);

            double tolFrac = Math.Max(0.0, opt.ResolveOverlapTolerance);
            int maxIter = Math.Max(1, opt.ResolveOverlapIterations);
            double relax = opt.ResolveOverlapRelaxation;
            if (relax <= 0.0) relax = 0.5;
            if (relax > 1.0) relax = 1.0;

            // Base (already-placed) contours and their areas. Indices follow
            // PlacedPanels order, which the solver builds deterministically.
            var baseCurves = new PolylineCurve[n];
            var areas = new double[n];
            var locked = new bool[n];
            var off = new Vector3d[n]; // accumulated translation per panel
            for (int i = 0; i < n; i++)
            {
                var panel = state.PlacedPanels[i];
                Transform t = state.AppliedTransforms.TryGetValue(panel.Id, out var xf)
                    ? xf : Transform.Identity;
                var c = (PolylineCurve)panel.SourceContour.DuplicateCurve();
                c.Transform(t);
                baseCurves[i] = c;
                areas[i] = ClosedArea(c);
                locked[i] = panel.IsAnchored;
            }

            double maxFrac = 0.0;
            int it = 0;
            for (; it < maxIter; it++)
            {
                var corr = new Vector3d[n]; // Jacobi accumulation this iteration
                maxFrac = 0.0;

                for (int i = 0; i < n; i++)
                {
                    var ci = Translated(baseCurves[i], off[i]);
                    for (int j = i + 1; j < n; j++)
                    {
                        var cj = Translated(baseCurves[j], off[j]);

                        // Bbox pre-filter at current offsets.
                        var bi = ci.GetBoundingBox(false);
                        var bj = cj.GetBoundingBox(false);
                        if (!Overlap1D(bi.Min.X, bi.Max.X, bj.Min.X, bj.Max.X)) continue;
                        if (!Overlap1D(bi.Min.Y, bi.Max.Y, bj.Min.Y, bj.Max.Y)) continue;

                        double overlapArea = IntersectionArea(ci, cj, out Point3d overlapCentroid);
                        if (overlapArea <= 1e-12) continue;

                        double smaller = Math.Min(
                            areas[i] > 1e-12 ? areas[i] : double.MaxValue,
                            areas[j] > 1e-12 ? areas[j] : double.MaxValue);
                        double frac = smaller < double.MaxValue ? overlapArea / smaller : 0.0;
                        if (frac > maxFrac) maxFrac = frac;
                        if (frac <= tolFrac) continue;

                        // Separation direction: from i's centroid to j's centroid
                        // (push j away from i). If centroids coincide, fall back to
                        // a deterministic axis so the pair still separates.
                        Point3d gi = CurveCentroid(ci);
                        Point3d gj = CurveCentroid(cj);
                        Vector3d dir = gj - gi;
                        dir.Z = 0.0;
                        if (dir.SquareLength < 1e-18)
                        {
                            // Degenerate: use the vector from i's centroid to the
                            // overlap-region centroid, else +X.
                            dir = overlapCentroid - gi; dir.Z = 0.0;
                            if (dir.SquareLength < 1e-18) dir = Vector3d.XAxis;
                        }
                        dir.Unitize();

                        // Step magnitude: move apart by a length whose swept band
                        // (extent across the contour width) clears the overlap
                        // area. Approximate the needed separation as overlapArea /
                        // contactWidth, with the contact width estimated from the
                        // overlap region's bbox extent perpendicular to dir. A
                        // robust, scale-relative fallback is sqrt(overlapArea).
                        double sep = Math.Sqrt(overlapArea);
                        var step = dir * (sep * relax);

                        bool li = locked[i], lj = locked[j];
                        if (li && lj)
                        {
                            // Both anchored: cannot move either. Leave it; reported
                            // in maxFrac so the caller knows it was not resolved.
                        }
                        else if (li)
                        {
                            corr[j] += step;          // all to j
                        }
                        else if (lj)
                        {
                            corr[i] -= step;          // all to i
                        }
                        else
                        {
                            corr[i] -= step * 0.5;
                            corr[j] += step * 0.5;
                        }
                    }
                }

                for (int k = 0; k < n; k++)
                    if (!locked[k]) off[k] += corr[k];

                if (maxFrac <= tolFrac) { it++; break; }
            }

            // Write the net translation back into the state's transforms (post-
            // multiply: T_new = Translation(off) * T_old, so the rim orientation
            // the solver produced is preserved and only the position shifts).
            for (int i = 0; i < n; i++)
            {
                if (off[i].IsZero) continue;
                var panel = state.PlacedPanels[i];
                Transform t = state.AppliedTransforms.TryGetValue(panel.Id, out var xf)
                    ? xf : Transform.Identity;
                state.AppliedTransforms[panel.Id] = Transform.Multiply(Transform.Translation(off[i]), t);
            }

            return (maxFrac, it);
        }

        private static PolylineCurve Translated(PolylineCurve c, Vector3d off)
        {
            if (off.IsZero) return c;
            var d = (PolylineCurve)c.DuplicateCurve();
            d.Transform(Transform.Translation(off));
            return d;
        }

        private static bool Overlap1D(double aMin, double aMax, double bMin, double bMax)
            => aMax >= bMin && bMax >= aMin;

        private static double ClosedArea(Curve c)
        {
            if (c == null || !c.IsClosed) return 0.0;
            var amp = AreaMassProperties.Compute(c);
            return amp == null ? 0.0 : Math.Abs(amp.Area);
        }

        private static Point3d CurveCentroid(Curve c)
        {
            if (c != null && c.IsClosed)
            {
                var amp = AreaMassProperties.Compute(c);
                if (amp != null) return amp.Centroid;
            }
            var bb = c?.GetBoundingBox(false) ?? BoundingBox.Unset;
            return bb.IsValid ? bb.Center : Point3d.Origin;
        }

        private static double IntersectionArea(Curve a, Curve b, out Point3d centroid)
        {
            centroid = Point3d.Origin;
            const double tol = 1e-4;
            Curve[]? regions = null;
            try { regions = Curve.CreateBooleanIntersection(a, b, tol); }
            catch { regions = null; }
            if (regions == null || regions.Length == 0) return 0.0;

            double area = 0.0;
            var weighted = Point3d.Origin;
            foreach (var r in regions)
            {
                if (r == null || !r.IsClosed) continue;
                var amp = AreaMassProperties.Compute(r);
                if (amp == null) continue;
                double ra = Math.Abs(amp.Area);
                area += ra;
                weighted += amp.Centroid * ra;
            }
            if (area > 1e-12) centroid = weighted / area;
            return area;
        }
    }
}
