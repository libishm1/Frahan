#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// 2.5D per-facet PROJECTION BOOTSTRAP for 3D fragment reassembly (design
    /// basis <c>wiki/algorithms/edge_matching/projection_bootstrap_3d.md</c>).
    /// Opt-in; the geometric 3D path only. See <see cref="AssemblyOptions.ProjectionBootstrap"/>.
    ///
    /// R0 proved the geometric 3D path has an EMPTY pair graph: independently-
    /// tessellated shard rims never hash-match (hash hits self=172, cross-panel=0),
    /// so the agglomerative solver has nothing to assemble. KEY FACT: for the
    /// open-shell shards the naked rim along a cut facet LIES IN that facet plane
    /// (planar), so two mating shards share a plane -> per-facet projection reduces
    /// 3D rim matching to the WORKING 2D edge matcher.
    ///
    /// PIPELINE
    ///  1. Per rim loop: fit the facet plane by PCA (3x3 covariance, symmetric EVD;
    ///     normal = smallest-eigenvalue eigenvector), record a planarity residual,
    ///     FLAG low-planarity loops. Build a right-handed plane frame.
    ///  2. Project the loop into the facet plane (PlaneToPlane -> WorldXY) and
    ///     RESAMPLE at a scale-relative spacing (tessellation-invariant). Build a
    ///     2D <see cref="Panel"/> from the resampled closed contour.
    ///  3. Match the projected rims ACROSS fragments with the existing 2D path
    ///     (<see cref="BoundarySegmenter"/> + <see cref="SegmentHashIndex"/> +
    ///     <see cref="ConstrainedIcp2D"/>) -> complementary pairs + a 2D in-plane
    ///     relative transform M2D = AontoB.
    ///  4. LIFT each 2D match to a 3D candidate relative pose:
    ///         T_rel = Lift_Bflip * M2D * Proj_A
    ///     with Proj_A = PlaneToPlane(facetA, WorldXY) and Lift_Bflip =
    ///     PlaneToPlane(WorldXY, facetB-with-normal-flipped) so the lifted A facet
    ///     normal is ANTIPARALLEL to B's outward facet normal (opposite sides of
    ///     the fracture). Produces a <see cref="MatchResult"/> candidate carrying
    ///     the relative SE(3).
    ///
    /// The emitted candidates feed the AGGLOMERATIVE solver (R0
    /// <see cref="AssemblySolver"/> with <see cref="AssemblyOptions.ProjectionBootstrap"/>):
    /// the candidate edges build the pair graph the 3D hash could not, the MST
    /// resolves the global poses, and the caller REFINES each placed fragment with
    /// <see cref="SoftIcpRefiner"/> (rim contact + non-penetration), dropping pairs
    /// whose refined residual / penetration is high (projection-ambiguous false
    /// positives) -> full 3D recovered, not 2.5D-only.
    ///
    /// Determinism: id-sorted loops, fixed unordered-pair order, strict
    /// (residual, panel-id, segment-index) tie-breaks. No randomness.
    /// Uses RhinoCommon (Plane, Transform, PolylineCurve) + MathNet (3x3 EVD), the
    /// same dependencies the rest of EdgeMatching.Core already pulls in.
    /// </summary>
    public static class ProjectionPairFinder
    {
        /// <summary>
        /// One per-facet projected rim: the source fragment + loop ids, the fitted
        /// facet plane, the planarity residual (RMS out-of-plane deviation), the
        /// low-planarity flag, and the projected-and-resampled 2D panel used for
        /// the 2D match.
        /// </summary>
        public sealed class ProjectedRim
        {
            public string FragmentId { get; }
            public int LoopIndex { get; }
            /// <summary>"PanelId" of the 2D panel: <c>FragmentId#LoopIndex</c>.</summary>
            public string PanelId { get; }
            public Plane Facet { get; }
            public double PlanarityResidual { get; }
            public double LoopScale { get; }
            public bool LowPlanarity { get; }
            public Panel Panel2D { get; }
            /// <summary>The facet arc's sample points in WORLD coordinates (3D), used
            /// by the 3D verification of a lifted candidate ("projection disposes").</summary>
            public Point3d[] WorldArc { get; }

            public ProjectedRim(string fragmentId, int loopIndex, string panelId,
                Plane facet, double planarityResidual, double loopScale,
                bool lowPlanarity, Panel panel2D, Point3d[] worldArc)
            {
                FragmentId = fragmentId;
                LoopIndex = loopIndex;
                PanelId = panelId;
                Facet = facet;
                PlanarityResidual = planarityResidual;
                LoopScale = loopScale;
                LowPlanarity = lowPlanarity;
                Panel2D = panel2D;
                WorldArc = worldArc ?? new Point3d[0];
            }
        }

        /// <summary>
        /// One 3D candidate pair lifted from a per-facet 2D match. <see cref="Match"/>
        /// carries the relative SE(3) in <c>AontoB</c> (maps the CHILD fragment's
        /// world rim onto the PARENT fragment's facet plane); <see cref="ChildFragmentId"/>
        /// / <see cref="ParentFragmentId"/> name the fragments. <see cref="Residual"/>
        /// is the 2D match residual.
        /// </summary>
        public sealed class CandidatePair
        {
            public string ChildFragmentId { get; }
            public string ParentFragmentId { get; }
            public string ChildRimPanelId { get; }
            public string ParentRimPanelId { get; }
            public Transform Relative { get; }
            public double Residual { get; }
            public MatchResult Match { get; }

            public CandidatePair(string childFragmentId, string parentFragmentId,
                string childRimPanelId, string parentRimPanelId,
                Transform relative, double residual, MatchResult match)
            {
                ChildFragmentId = childFragmentId;
                ParentFragmentId = parentFragmentId;
                ChildRimPanelId = childRimPanelId;
                ParentRimPanelId = parentRimPanelId;
                Relative = relative;
                Residual = residual;
                Match = match;
            }
        }

        /// <summary>Full result: the projected rims (with planarity diagnostics) and the lifted pairs.</summary>
        public sealed class Result
        {
            public List<ProjectedRim> Rims { get; } = new List<ProjectedRim>();
            public List<CandidatePair> Pairs { get; } = new List<CandidatePair>();
            /// <summary>Cross-fragment 2D matches that passed the 2D gates (target (a)).</summary>
            public int CrossPanelMatches { get; set; }
            /// <summary>Lifted pairs that also passed the 3D verification gate (kept as edges).</summary>
            public int VerifiedPairs { get; set; }
            /// <summary>Diagnostic: best 3D-verification residual per unordered FRAGMENT
            /// pair "childFrag|parentFrag" (pre-gate), for calibration / connectivity reports.</summary>
            public Dictionary<string, double> BestFragPairResidual { get; } =
                new Dictionary<string, double>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Build per-facet projected rims and lifted 3D candidate pairs from a set
        /// of fragment rim loops. <paramref name="rimsPerFragment"/> maps fragment
        /// id -> its naked-edge rim loops (closed polylines, WORLD coordinates at
        /// the fragment's CURRENT pose). Deterministic.
        /// </summary>
        public static Result FindPairs(
            IReadOnlyDictionary<string, List<PolylineCurve>> rimsPerFragment,
            AssemblyOptions opt,
            SegmenterOptions segOpt = null)
        {
            if (rimsPerFragment == null) throw new ArgumentNullException(nameof(rimsPerFragment));
            opt = opt ?? new AssemblyOptions();
            segOpt = segOpt ?? new SegmenterOptions();

            var result = new Result();

            // --- 1+2. Split every naked rim loop into maximal PLANAR FACET ARCS
            // (the open-shell shard's naked boundary traces several cut facets,
            // joined at the outer block surface; each cut facet is planar and is the
            // matchable unit), project each arc into its facet plane, and build a 2D
            // panel. Deterministic fragment order (id-sorted), loop order as given.
            // A whole-loop facet is kept as the single arc when the loop is already
            // planar. ---
            foreach (var fragId in rimsPerFragment.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var loops = rimsPerFragment[fragId];
                if (loops == null) continue;
                int facetIdx = 0;
                for (int l = 0; l < loops.Count; l++)
                {
                    foreach (var rim in BuildProjectedFacets(fragId, ref facetIdx, loops[l], opt, segOpt))
                        result.Rims.Add(rim);
                }
            }

            // --- 3. Match the projected 2D panels ACROSS fragments. Build one hash
            // index over ALL projected panels (the 2D path) so QueryComplement can
            // find complementary rims; then for every unordered cross-fragment rim
            // pair run BestMatch2D in both directions and keep the lower-residual
            // passing direction. ---
            var rims = result.Rims;
            // Index segments per projected panel, with the (default-off) partial
            // emission threaded the same way AssemblySolver does.
            var localSeg = CloneSeg(segOpt, opt);
            var index = new SegmentHashIndex();
            var segsByPanel = new Dictionary<string, List<Segment>>(StringComparer.Ordinal);
            foreach (var r in rims)
            {
                var segs = BoundarySegmenter.Segment(r.Panel2D, localSeg);
                segsByPanel[r.PanelId] = segs;
                foreach (var s in segs) index.Add(s);
            }

            var icpOpt = new IcpOptions
            {
                NonCrossingCorrespondence = opt.NonCrossingCorrespondence,
                NonCrossingMaxGap = opt.NonCrossingMaxGap,
            };
            var icp2d = new ConstrainedIcp2D(icpOpt);

            // Scale-relative residual gate (median projected-panel bbox diagonal).
            double scale = ComputeScale(rims);
            double residualGate = opt.ResidualThresholdFactor > 0.0
                ? opt.ResidualThresholdFactor * scale
                : opt.ResidualThreshold;

            // The 3D-verification gate is relative to the OBJECT scale (median rim-
            // LOOP diagonal), not the per-arc diagonal, so the gate is stable across
            // arcs of different length on the same fragment.
            double verifyScale = MedianLoopScale(rims);

            // Skip low-planarity rims as match participants (projection error would
            // dominate); they remain in result.Rims for the diagnostic report.
            for (int i = 0; i < rims.Count; i++)
            {
                for (int j = i + 1; j < rims.Count; j++)
                {
                    var ri = rims[i];
                    var rj = rims[j];
                    // Only CROSS-fragment pairs (a fragment's two rims never mate).
                    if (string.Equals(ri.FragmentId, rj.FragmentId, StringComparison.Ordinal))
                        continue;
                    if (ri.LowPlanarity || rj.LowPlanarity) continue;

                    // Direction A: ri candidate (child), rj hit (parent).
                    var mA = BestMatch2D(ri, rj, index, segsByPanel, icp2d,
                        opt.PhaseScoreThreshold, residualGate);
                    // Direction B: rj candidate (child), ri hit (parent).
                    var mB = BestMatch2D(rj, ri, index, segsByPanel, icp2d,
                        opt.PhaseScoreThreshold, residualGate);

                    // Keep the lower-residual direction (strict id tie-break).
                    ProjectedRim child = null, parent = null;
                    MatchResult chosen = null;
                    if (mA != null) { child = ri; parent = rj; chosen = mA; }
                    if (mB != null)
                    {
                        bool takeB = chosen == null
                            || mB.Residual < chosen.Residual
                            || (mB.Residual == chosen.Residual
                                && string.CompareOrdinal(rj.PanelId, child.PanelId) < 0);
                        if (takeB) { child = rj; parent = ri; chosen = mB; }
                    }
                    if (chosen == null) continue;

                    result.CrossPanelMatches++;

                    // --- 4. LIFT the 2D match to a 3D relative pose. The 2D shadow
                    // match has a normal-sign / reflection ambiguity, so try BOTH
                    // lift senses (antiparallel-flip on the parent OR on the child)
                    // and keep whichever gives the lower ACTUAL 3D rim residual --
                    // this is "projection PROPOSES, 3D DISPOSES": a false-positive
                    // 2D-shadow match has a large 3D residual and is rejected by the
                    // gate below. ---
                    Transform tRel = Lift(child.Facet, parent.Facet, chosen.AontoB);
                    double res3d = Residual3D(child.WorldArc, parent.WorldArc, tRel);

                    Transform tRelB = LiftChildFlip(child.Facet, parent.Facet, chosen.AontoB);
                    double res3dB = Residual3D(child.WorldArc, parent.WorldArc, tRelB);
                    if (res3dB < res3d) { tRel = tRelB; res3d = res3dB; }

                    // Diagnostic: track the best 3D residual per unordered fragment pair.
                    string fa = child.FragmentId, fb = parent.FragmentId;
                    string fk = string.CompareOrdinal(fa, fb) <= 0 ? fa + "|" + fb : fb + "|" + fa;
                    if (!result.BestFragPairResidual.TryGetValue(fk, out double cur) || res3d < cur)
                        result.BestFragPairResidual[fk] = res3d;

                    // 3D verification gate: reject projection-ambiguous false pairs.
                    double verifyGate = opt.ProjectionVerifyFactor * verifyScale;
                    if (res3d > verifyGate) continue;

                    var pair = new CandidatePair(
                        childFragmentId: child.FragmentId,
                        parentFragmentId: parent.FragmentId,
                        childRimPanelId: child.PanelId,
                        parentRimPanelId: parent.PanelId,
                        relative: tRel,
                        residual: res3d,
                        match: StampResidual(chosen, res3d));
                    result.Pairs.Add(pair);
                }
            }

            // Count verified pairs (those that passed the 3D gate) for the report.
            result.VerifiedPairs = result.Pairs.Count;

            // Deterministic ordering of the emitted pairs: by (child fragment id,
            // parent fragment id) ordinal.
            result.Pairs.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.ChildFragmentId, b.ChildFragmentId);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.ParentFragmentId, b.ParentFragmentId);
                if (c != 0) return c;
                return a.Residual.CompareTo(b.Residual);
            });
            return result;
        }

        // ====================================================================
        // 1+2. Per-facet projection
        // ====================================================================

        // Split one naked rim loop into maximal planar facet arcs and project each
        // into its own facet plane. Yields a ProjectedRim per arc (open Frame panel,
        // since a cut-facet arc connects to the rest of the boundary at the block
        // surface). Deterministic; facet indices advance via the shared counter.
        private static IEnumerable<ProjectedRim> BuildProjectedFacets(
            string fragId, ref int facetIdx, PolylineCurve loop,
            AssemblyOptions opt, SegmenterOptions segOpt)
        {
            var rims = new List<ProjectedRim>();
            if (loop == null) return rims;
            Polyline poly = loop.ToPolyline();
            if (poly == null || poly.Count < 4) return rims;

            // De-duplicate the closing point so PCA is not biased by the repeated
            // first/last vertex of a closed polyline.
            var pts = new List<Point3d>(poly.Count);
            for (int i = 0; i < poly.Count; i++)
            {
                if (i == poly.Count - 1 && poly.Count > 1 &&
                    poly[i].DistanceTo(poly[0]) < 1e-9) continue;
                pts.Add(poly[i]);
            }
            if (pts.Count < 3) return rims;

            // Loop scale = bbox diagonal (for scale-relative spacing / planarity).
            var bb = BoundingBox.Unset;
            foreach (var p in pts) bb.Union(p);
            double loopScale = bb.IsValid ? bb.Diagonal.Length : 1.0;
            if (loopScale <= 1e-9) loopScale = 1.0;
            double planarTol = opt.ProjectionPlanarityFactor * loopScale;

            // Split into maximal planar arcs (cyclic). Each arc is a run of
            // consecutive rim vertices that stay within planarTol of the arc's PCA
            // plane; a run breaks when the next vertex would leave the plane (a new
            // cut facet starts). The split is on the CYCLIC sequence: rotate the
            // start to the largest plane-break so an arc is not severed mid-facet.
            var arcs = SplitPlanarArcs(pts, planarTol);
            if (arcs.Count == 0)
            {
                // No clean split (e.g. already planar): treat the whole loop as one arc.
                arcs.Add(pts);
            }

            double spacing = Math.Max(opt.ProjectionSampleSpacingFactor * loopScale, 1e-6);
            double minArcLen = 4.0 * spacing; // need a few samples to match on

            foreach (var arc in arcs)
            {
                if (arc.Count < 3) continue;
                if (!FitFacetPlane(arc, out Plane facet, out double residual)) continue;

                // Arc length (open polyline).
                double arcLen = 0.0;
                for (int i = 1; i < arc.Count; i++) arcLen += arc[i].DistanceTo(arc[i - 1]);
                if (arcLen < minArcLen) continue;

                bool lowPlanarity = residual > planarTol;

                // Project the arc into its facet plane -> WorldXY 2D open polyline.
                Transform proj = Transform.PlaneToPlane(facet, Plane.WorldXY);
                var projected = new Polyline();
                foreach (var p in arc)
                {
                    var q = p; q.Transform(proj);
                    projected.Add(new Point3d(q.X, q.Y, 0.0));
                }

                // RESAMPLE at a scale-relative spacing (tessellation-invariant), open.
                var resampled = ResampleOpen(projected, spacing);
                if (resampled == null || resampled.Count < 3) continue;

                // World-coordinate arc = the resampled 2D arc lifted back into the
                // facet plane (so the 3D verification uses the SAME sampling the 2D
                // matcher saw).
                Transform lift = Transform.PlaneToPlane(Plane.WorldXY, facet);
                var worldArc = new Point3d[resampled.Count];
                for (int i = 0; i < resampled.Count; i++)
                {
                    var q = resampled[i]; q.Transform(lift); worldArc[i] = q;
                }

                string panelId = fragId + "#" + facetIdx;
                facetIdx++;
                Panel panel2D;
                try
                {
                    // PanelKind.Frame allows an OPEN contour (a cut-facet arc is open).
                    // PlanarityTolerance large so the panel is always Planar2D (flat by
                    // construction) -> takes the 2D segmenter / ICP path.
                    panel2D = new Panel(panelId, resampled.ToPolylineCurve(), PanelKind.Frame,
                        planarityTolerance: double.PositiveInfinity);
                }
                catch { continue; }

                rims.Add(new ProjectedRim(fragId, facetIdx - 1, panelId, facet, residual,
                    loopScale, lowPlanarity, panel2D, worldArc));
            }
            return rims;
        }

        // Split a CYCLIC vertex sequence into maximal planar arcs. Greedy: grow a run
        // while every vertex is within tol of the run's current PCA plane; when a
        // vertex would leave the plane, close the run and start a new one. To avoid
        // severing a facet across the array seam, the scan starts at the index of the
        // sharpest plane break (largest deviation of v from the plane fit of its
        // neighbourhood). Returns open arcs (each a list of >=3 points). If the loop
        // is essentially planar (one run covers all) returns a single arc.
        private static List<List<Point3d>> SplitPlanarArcs(List<Point3d> pts, double tol)
        {
            int n = pts.Count;
            var result = new List<List<Point3d>>();
            if (n < 3) return result;

            // Rotate start to the sharpest corner so arcs are not split mid-facet.
            int start = FindSharpestCorner(pts, tol);
            var seq = new List<Point3d>(n);
            for (int k = 0; k < n; k++) seq.Add(pts[(start + k) % n]);

            int i = 0;
            while (i < n)
            {
                var run = new List<Point3d> { seq[i] };
                int j = i + 1;
                while (j < n)
                {
                    run.Add(seq[j]);
                    // Fit plane to the current run; if any point exceeds tol, the
                    // last add broke planarity -> drop it and close the run.
                    if (run.Count >= 3 && FitFacetPlane(run, out Plane pl, out double res))
                    {
                        double maxd = 0.0;
                        foreach (var p in run) { double d = Math.Abs(pl.DistanceTo(p)); if (d > maxd) maxd = d; }
                        if (maxd > tol)
                        {
                            run.RemoveAt(run.Count - 1); // backtrack the breaking vertex
                            break;
                        }
                    }
                    j++;
                }
                if (run.Count >= 3) result.Add(run);
                if (j <= i) j = i + 1;
                i = j;
            }
            return result;
        }

        // Index of the vertex whose local neighbourhood is the sharpest non-planar
        // corner (largest out-of-plane deviation of v from the plane of its 4
        // neighbours). Used as a deterministic cyclic start so planar facet arcs are
        // not severed at the array seam.
        private static int FindSharpestCorner(List<Point3d> pts, double tol)
        {
            int n = pts.Count;
            int best = 0; double bestDev = -1.0;
            for (int i = 0; i < n; i++)
            {
                var nb = new List<Point3d>
                {
                    pts[(i - 2 + n) % n], pts[(i - 1 + n) % n],
                    pts[(i + 1) % n], pts[(i + 2) % n],
                };
                if (!FitFacetPlane(nb, out Plane pl, out _)) continue;
                double dev = Math.Abs(pl.DistanceTo(pts[i]));
                if (dev > bestDev) { bestDev = dev; best = i; }
            }
            return best;
        }

        /// <summary>
        /// PCA best-fit plane of a 3D point set via the symmetric eigendecomposition
        /// of the 3x3 covariance matrix. Normal = eigenvector of the SMALLEST
        /// eigenvalue; in-plane x = largest-eigenvalue eigenvector; y = n x x (right-
        /// handed). Residual = sqrt(smallest eigenvalue) = RMS out-of-plane deviation.
        /// Pure managed (Vector3d value math + MathNet EVD), so it runs in the bare
        /// test host. Returns false on a degenerate set.
        /// </summary>
        public static bool FitFacetPlane(IList<Point3d> pts, out Plane plane, out double residual)
        {
            plane = Plane.WorldXY;
            residual = 0.0;
            int n = pts?.Count ?? 0;
            if (n < 3) return false;

            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < n; i++) { cx += pts[i].X; cy += pts[i].Y; cz += pts[i].Z; }
            cx /= n; cy /= n; cz /= n;

            double sxx = 0, sxy = 0, sxz = 0, syy = 0, syz = 0, szz = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = pts[i].X - cx, dy = pts[i].Y - cy, dz = pts[i].Z - cz;
                sxx += dx * dx; sxy += dx * dy; sxz += dx * dz;
                syy += dy * dy; syz += dy * dz; szz += dz * dz;
            }
            double inv = 1.0 / n;
            sxx *= inv; sxy *= inv; sxz *= inv; syy *= inv; syz *= inv; szz *= inv;

            var cov = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { sxx, sxy, sxz },
                { sxy, syy, syz },
                { sxz, syz, szz },
            });

            Evd<double> evd;
            try { evd = cov.Evd(Symmetricity.Symmetric); }
            catch { return false; }

            var eval = evd.EigenValues;     // ascending for symmetric matrices
            var evec = evd.EigenVectors;

            // Find ascending order explicitly (do not rely on the library order).
            double e0 = eval[0].Real, e1 = eval[1].Real, e2 = eval[2].Real;
            int iMin = 0, iMax = 0;
            double vMin = e0, vMax = e0;
            double[] es = { e0, e1, e2 };
            for (int k = 1; k < 3; k++)
            {
                if (es[k] < vMin) { vMin = es[k]; iMin = k; }
                if (es[k] > vMax) { vMax = es[k]; iMax = k; }
            }
            if (iMin == iMax) return false; // fully degenerate (all equal)

            var normal = new Vector3d(evec[0, iMin], evec[1, iMin], evec[2, iMin]);
            var xAxis = new Vector3d(evec[0, iMax], evec[1, iMax], evec[2, iMax]);
            if (!normal.Unitize() || !xAxis.Unitize()) return false;

            // Right-handed in-plane y = normal x x.
            var yAxis = Vector3d.CrossProduct(normal, xAxis);
            if (!yAxis.Unitize()) return false;
            // Re-orthogonalise x against (y,normal) for a clean frame.
            xAxis = Vector3d.CrossProduct(yAxis, normal);
            if (!xAxis.Unitize()) return false;

            plane = new Plane(new Point3d(cx, cy, cz), xAxis, yAxis);
            if (!plane.IsValid) return false;
            residual = Math.Sqrt(Math.Max(0.0, vMin));
            return true;
        }

        // ====================================================================
        // 3. Per-facet 2D match (reuse the working 2D path)
        // ====================================================================

        // Best directed 2D match of candidate's projected panel onto hit's, using
        // the shared hash index. Mirrors AssemblySolver.BestMatchOrdered but
        // constrained to one cross-fragment rim pair and always 2D.
        private static MatchResult BestMatch2D(
            ProjectedRim cand, ProjectedRim hit, SegmentHashIndex index,
            Dictionary<string, List<Segment>> segsByPanel, ConstrainedIcp2D icp2d,
            double phaseGate, double residualGate)
        {
            if (!segsByPanel.TryGetValue(cand.PanelId, out var candSegs)) return null;
            MatchResult best = null;
            foreach (var cs in candSegs)
            {
                var hits = index.QueryComplement(cs);
                foreach (var h in hits)
                {
                    if (!string.Equals(h.PanelId, hit.PanelId, StringComparison.Ordinal)) continue;
                    var (lag, score) = PhaseCorrelator.Correlate(cs.TurningSignature, h.TurningSignature);
                    if (score < phaseGate) continue;
                    var init = InitialTransformBuilder.FromLag2D(cs, h, lag);
                    var refined = icp2d.Refine(cs, h, hit.Panel2D, init);
                    if (refined.Residual > residualGate) continue;
                    if (best == null
                        || refined.Residual < best.Residual
                        || (refined.Residual == best.Residual && SegTieBreak(refined, best) < 0))
                        best = refined;
                }
            }
            return best;
        }

        private static int SegTieBreak(MatchResult x, MatchResult y)
        {
            int c = string.CompareOrdinal(x.A.PanelId, y.A.PanelId);
            if (c != 0) return c;
            c = x.A.Index.CompareTo(y.A.Index);
            if (c != 0) return c;
            c = string.CompareOrdinal(x.B.PanelId, y.B.PanelId);
            if (c != 0) return c;
            return x.B.Index.CompareTo(y.B.Index);
        }

        // ====================================================================
        // 4. 2D -> 3D lift
        // ====================================================================

        /// <summary>
        /// Lift a 2D in-plane match M2D (child's projected coords -> parent's
        /// projected coords, both in WorldXY) to a 3D relative pose that maps the
        /// CHILD fragment's world rim onto the PARENT fragment's facet plane with
        /// the two facet normals ANTIPARALLEL (opposite sides of the fracture):
        ///     T_rel = Lift_parentFlip * M2D * Proj_child
        /// Proj_child = PlaneToPlane(childFacet, WorldXY) projects the child rim
        /// into its facet plane; M2D aligns it to the parent in-plane; Lift_parentFlip
        /// = PlaneToPlane(WorldXY, parentFacet-with-normal-flipped) lifts it back
        /// into the parent facet plane in 3D, with the child's facet normal now
        /// pointing along -nParent (antiparallel to the parent's outward normal).
        /// Public + static so the composition is unit-testable on a known pair.
        /// </summary>
        public static Transform Lift(Plane childFacet, Plane parentFacet, Transform m2D)
        {
            Transform projChild = Transform.PlaneToPlane(childFacet, Plane.WorldXY);

            // Parent facet with the normal flipped (and y flipped to stay right-
            // handed): normal -> -nParent, so the lifted child normal (which Proj
            // mapped to +Z) ends up antiparallel to the parent outward normal.
            var parentFlip = new Plane(parentFacet.Origin,
                parentFacet.XAxis, -parentFacet.YAxis);
            Transform liftParent = Transform.PlaneToPlane(Plane.WorldXY, parentFlip);

            return Transform.Multiply(liftParent, Transform.Multiply(m2D, projChild));
        }

        /// <summary>
        /// Alternative lift sense: flip the CHILD facet normal instead of the
        /// parent's. The 2D shadow match cannot tell which shard's PCA normal points
        /// "outward", so the true 3D mating may need the opposite flip. The caller
        /// keeps whichever of <see cref="Lift"/> / this gives the lower 3D residual.
        /// </summary>
        public static Transform LiftChildFlip(Plane childFacet, Plane parentFacet, Transform m2D)
        {
            // Flip the child projection's handedness (project with -y) so the lifted
            // normal sense is reversed relative to Lift.
            var childFlip = new Plane(childFacet.Origin, childFacet.XAxis, -childFacet.YAxis);
            Transform projChild = Transform.PlaneToPlane(childFlip, Plane.WorldXY);
            var parentFlip = new Plane(parentFacet.Origin, parentFacet.XAxis, -parentFacet.YAxis);
            Transform liftParent = Transform.PlaneToPlane(Plane.WorldXY, parentFlip);
            return Transform.Multiply(liftParent, Transform.Multiply(m2D, projChild));
        }

        // 3D verification residual of a lifted candidate: symmetric mean nearest-
        // neighbour distance between the CHILD world arc transformed by tRel and the
        // PARENT world arc. A true mating facet -> small; a 2D-shadow false positive
        // -> large (the projection lost the depth that distinguishes them). This is
        // the "3D disposes" check that gates and re-weights the candidate edges.
        private static double Residual3D(Point3d[] childArc, Point3d[] parentArc, Transform tRel)
        {
            if (childArc == null || parentArc == null ||
                childArc.Length == 0 || parentArc.Length == 0)
                return double.PositiveInfinity;

            var moved = new Point3d[childArc.Length];
            for (int i = 0; i < childArc.Length; i++)
            {
                var p = childArc[i]; p.Transform(tRel); moved[i] = p;
            }

            double s1 = 0;
            for (int i = 0; i < moved.Length; i++)
            {
                double best = double.PositiveInfinity;
                for (int j = 0; j < parentArc.Length; j++)
                {
                    double d = moved[i].DistanceToSquared(parentArc[j]);
                    if (d < best) best = d;
                }
                s1 += Math.Sqrt(best);
            }
            double s2 = 0;
            for (int j = 0; j < parentArc.Length; j++)
            {
                double best = double.PositiveInfinity;
                for (int i = 0; i < moved.Length; i++)
                {
                    double d = parentArc[j].DistanceToSquared(moved[i]);
                    if (d < best) best = d;
                }
                s2 += Math.Sqrt(best);
            }
            return 0.5 * (s1 / moved.Length + s2 / parentArc.Length);
        }

        // A MatchResult identical to src but carrying the 3D-verified residual (the
        // weight the agglomerative MST uses).
        private static MatchResult StampResidual(MatchResult src, double residual)
            => new MatchResult(src.A, src.B, src.AontoB, residual, src.Converged, src.Iterations);

        // ====================================================================
        // helpers
        // ====================================================================

        private static SegmenterOptions CloneSeg(SegmenterOptions src, AssemblyOptions opt)
        {
            var s = new SegmenterOptions
            {
                SampleSpacing = src.SampleSpacing,
                BreakAngleDeg = src.BreakAngleDeg,
                BreakWindow = src.BreakWindow,
                SignatureBins = src.SignatureBins,
                MinSegmentLength = src.MinSegmentLength,
            };
            if (opt.EmitPartials)
            {
                s.EmitPartials = true;
                s.PartialFractions = opt.PartialFractions ?? new double[0];
                s.PartialStrideFraction = opt.PartialStrideFraction;
            }
            return s;
        }

        private static double ComputeScale(List<ProjectedRim> rims)
        {
            var diags = new List<double>();
            foreach (var r in rims)
            {
                var bb = r.Panel2D.SourceContour.GetBoundingBox(false);
                if (bb.IsValid) diags.Add(bb.Diagonal.Length);
            }
            if (diags.Count == 0) return 1.0;
            diags.Sort();
            double med = diags[(diags.Count - 1) / 2];
            return med > 1e-9 ? med : 1.0;
        }

        // Median rim-LOOP bbox diagonal (object scale) for the 3D-verification gate.
        private static double MedianLoopScale(List<ProjectedRim> rims)
        {
            var diags = new List<double>();
            foreach (var r in rims) if (r.LoopScale > 1e-9) diags.Add(r.LoopScale);
            if (diags.Count == 0) return 1.0;
            diags.Sort();
            double med = diags[(diags.Count - 1) / 2];
            return med > 1e-9 ? med : 1.0;
        }

        // Even arc-length resample of an OPEN polyline at a target spacing.
        // Deterministic (DivideByCount, count from total length / spacing). Mirrors
        // BoundarySegmenter.ResampleByArcLength; keeps endpoints, no closing point.
        private static Polyline ResampleOpen(Polyline poly, double spacing)
        {
            var crv = poly.ToPolylineCurve();
            double L = crv.GetLength();
            if (L <= 1e-9) return null;
            int n = Math.Max(4, (int)Math.Ceiling(L / Math.Max(spacing, 1e-9)));
            // includeEnds:true on an open curve gives n+1 stations from start to end.
            var ts = crv.DivideByCount(n, true);
            if (ts == null || ts.Length < 3) return null;
            var outPoly = new Polyline();
            foreach (var t in ts) outPoly.Add(crv.PointAt(t));
            return outPoly;
        }
    }
}
