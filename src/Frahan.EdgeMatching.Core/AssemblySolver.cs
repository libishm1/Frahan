using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Iterative beam search over the match graph. Seeded with anchors
    /// (typically the Trencadís frame or the first plank). Each step
    /// expands every state by trying every unplaced candidate against
    /// every already-placed neighbour, then keeps the top-K states by
    /// cumulative residual. Deterministic by construction: lexical id
    /// order on anchors, panel-id + segment-index sort inside the hash
    /// index, and a stable cost+id tiebreak on the beam.
    ///
    /// 2D/3D dispatch is per pair: a candidate-vs-hit pair runs the 3D
    /// ICP iff either side is Spatial3D. The optional substrate Brep is
    /// only consulted by the 3D path.
    /// </summary>
    public sealed class AssemblySolver
    {
        private readonly AssemblyOptions _opt;
        private readonly SegmentHashIndex _index;
        private readonly ConstrainedIcp2D _icp2d;
        private readonly ConstrainedIcp3D _icp3d;
        private readonly SegmenterOptions _segOpt;
        private readonly SegmenterOptions3D _segOpt3D;
        private readonly Brep? _substrate;
        // Object scale (median per-panel bbox diagonal) for A1 scale-relative
        // gates. Set per Solve() call; defaults to 1.0 (no effect).
        private double _scale = 1.0;

        public AssemblySolver(
            SegmentHashIndex index,
            AssemblyOptions? opt = null,
            SegmenterOptions? segOpt = null,
            SegmenterOptions3D? segOpt3D = null,
            Brep? substrate = null)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _opt = opt ?? new AssemblyOptions();
            // Thread the (default-off) non-crossing toggle from the assembly
            // options into the ICP options. When the toggle is off this
            // IcpOptions is identical to the previous parameterless ICP
            // construction, so the default numeric path is unchanged.
            var icpOpt = new IcpOptions
            {
                NonCrossingCorrespondence = _opt.NonCrossingCorrespondence,
                NonCrossingMaxGap = _opt.NonCrossingMaxGap,
            };
            _icp2d = new ConstrainedIcp2D(icpOpt);
            _icp3d = new ConstrainedIcp3D(icpOpt);
            _segOpt = segOpt ?? new SegmenterOptions();
            _segOpt3D = segOpt3D ?? new SegmenterOptions3D();

            // R1: thread the (default-off) partial-emission toggle from the
            // assembly options onto the segmenter options used to re-segment
            // candidates in TryPlace, so the long-edge-to-short-edge partials
            // are generated for the candidate side too (the index side is
            // populated by the caller's own segmentation pass). Mirrors the
            // NonCrossingCorrespondence threading above. Only written when on,
            // so the default path leaves the caller's SegmenterOptions
            // untouched and candidate generation byte-for-byte unchanged.
            if (_opt.EmitPartials)
            {
                _segOpt.EmitPartials = true;
                _segOpt.PartialFractions = _opt.PartialFractions ?? new double[0];
                _segOpt.PartialStrideFraction = _opt.PartialStrideFraction;
                _segOpt3D.EmitPartials = true;
                _segOpt3D.PartialFractions = _opt.PartialFractions ?? new double[0];
                _segOpt3D.PartialStrideFraction = _opt.PartialStrideFraction;
            }

            _substrate = substrate;
        }

        public AssemblyState Solve(IEnumerable<Panel> anchors, IEnumerable<Panel> pool)
        {
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            // R0: agglomerative dispatch (opt-in). Default FrameAnchored runs the
            // original beam below, byte-for-byte unchanged.
            if (_opt.Mode == AssemblyMode.Agglomerative)
                return SolveAgglomerative(anchors, pool);

            return SolveBeam(anchors, pool);
        }

        /// <summary>
        /// Projection-bootstrap overload (opt-in). Agglomerative assembly over
        /// node panels (anchors + pool, identified by <see cref="Panel.Id"/>) where
        /// the pair graph is built from EXTERNALLY-SUPPLIED candidate edges instead
        /// of the all-pairs ICP. Each candidate is a <see cref="MatchResult"/> whose
        /// <c>A.PanelId</c> = CHILD node id, <c>B.PanelId</c> = PARENT node id, and
        /// <c>AontoB</c> = the relative pose mapping CHILD-local -> PARENT-local (the
        /// <see cref="ProjectionPairFinder"/> lift output, fragment-level). The MST
        /// resolve + absolute-pose composition is the same R0 logic. Requires
        /// <see cref="AssemblyMode.Agglomerative"/>; otherwise falls back to the beam.
        /// Determinism: id-sorted nodes, candidate-edge order as given, strict
        /// (weight, edge-id, child-id) tie-breaks.
        /// </summary>
        public AssemblyState Solve(
            IEnumerable<Panel> anchors, IEnumerable<Panel> pool,
            IReadOnlyList<MatchResult> candidateEdges)
        {
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (candidateEdges == null || _opt.Mode != AssemblyMode.Agglomerative)
                return Solve(anchors, pool);
            return SolveAgglomerative(anchors, pool, candidateEdges);
        }

        private AssemblyState SolveBeam(IEnumerable<Panel> anchors, IEnumerable<Panel> pool)
        {

            var seed = new AssemblyState();
            foreach (var a in anchors.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                a.IsAnchored = true;
                seed.PlacedPanels.Add(a);
                seed.AppliedTransforms[a.Id] = a.AppliedTransform;
            }
            var unplaced = pool.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

            // A1: object scale for scale-relative gates (median per-panel
            // diagonal; robust to where the unplaced pieces are scattered).
            _scale = ComputeScale(seed.PlacedPanels, unplaced);

            var beam = new List<AssemblyState> { seed };
            int step = 0;
            while (unplaced.Count > 0 && step++ < _opt.MaxIterations)
            {
                var nextBeam = new List<(AssemblyState state, double cost)>();

                foreach (var state in beam)
                {
                    foreach (var candidate in unplaced)
                    {
                        var placements = TryPlace(state, candidate);
                        foreach (var placement in placements)
                        {
                            var s2 = state.Clone();
                            s2.PlacedPanels.Add(candidate);
                            s2.AppliedTransforms[candidate.Id] = placement.AontoB;
                            s2.TotalResidual += placement.Residual;
                            s2.History.Add(placement);

                            // R2 (B6): mark the matched placed-panel segment consumed
                            // so no later candidate in this state can reuse the same
                            // edge. Only when EdgeExclusivity is on (else the set is
                            // never touched and the path is unchanged).
                            if (_opt.EdgeExclusivity)
                                s2.ConsumedSegments.Add(SegmentKey(placement.B));

                            // R2 (A3): add a non-overlap term to the beam cost. The
                            // candidate's contour at placement.AontoB (absolute pose
                            // in the frame-anchored beam, anchor = identity) is tested
                            // against the already-placed contours. Penalty is
                            // OverlapPenalty * overlapFraction. When OverlapPenalty is
                            // 0 the term is skipped entirely (no extra geometry work,
                            // byte-for-byte cost).
                            double cost = s2.TotalResidual;
                            if (_opt.OverlapPenalty > 0.0)
                            {
                                double frac = OverlapFraction(
                                    candidate, placement.AontoB, state.PlacedPanels, state.AppliedTransforms);
                                cost += _opt.OverlapPenalty * frac;
                            }
                            nextBeam.Add((s2, cost));
                        }
                    }
                }

                if (nextBeam.Count == 0) break;

                nextBeam.Sort((x, y) =>
                {
                    int c = x.cost.CompareTo(y.cost);
                    if (c != 0) return c;
                    return string.CompareOrdinal(JoinIds(x.state), JoinIds(y.state));
                });
                beam = nextBeam.Take(_opt.BeamWidth).Select(t => t.state).ToList();

                var bestLast = beam[0].PlacedPanels.Last();
                unplaced.RemoveAll(p => p.Id == bestLast.Id);
            }
            return beam[0];
        }

        private List<MatchResult> TryPlace(AssemblyState state, Panel candidate)
        {
            var results = new List<MatchResult>();

            // A1: scale-relative residual gate when a factor is set; else the
            // original absolute ResidualThreshold.
            double residualGate = _opt.ResidualThresholdFactor > 0.0
                ? _opt.ResidualThresholdFactor * _scale
                : _opt.ResidualThreshold;

            // The unplaced list tracks the BEST beam's progress, so other
            // beams may already have placed this candidate. Skip in that
            // case rather than corrupting state with a duplicate.
            if (state.PlacedPanels.Any(p => p.Id == candidate.Id))
                return results;

            var candSegments = candidate.Mode == PanelMode.Spatial3D
                ? BoundarySegmenter3D.Segment(candidate, _segOpt3D)
                : BoundarySegmenter.Segment(candidate, _segOpt);

            foreach (var cs in candSegments)
            {
                var hits = _index.QueryComplement(cs);
                foreach (var hit in hits)
                {
                    var hitPanel = state.PlacedPanels.FirstOrDefault(p => p.Id == hit.PanelId);
                    if (hitPanel == null) continue;

                    // R2 (B6): skip a placed-panel segment already consumed by an
                    // earlier accepted placement in this state. No-op when
                    // EdgeExclusivity is off (the set is always empty then).
                    if (_opt.EdgeExclusivity && state.ConsumedSegments.Contains(SegmentKey(hit)))
                        continue;

                    var (lag, score) = PhaseCorrelator.Correlate(cs.TurningSignature, hit.TurningSignature);
                    if (score < _opt.PhaseScoreThreshold) continue;

                    bool pairIs3D = candidate.Mode == PanelMode.Spatial3D
                                 || hitPanel.Mode == PanelMode.Spatial3D;

                    MatchResult refined;
                    if (pairIs3D)
                    {
                        var init = InitialTransformBuilder.FromLag3D(cs, hit, lag);
                        refined = _icp3d.Refine(cs, hit, hitPanel, _substrate, init);
                    }
                    else
                    {
                        var init = InitialTransformBuilder.FromLag2D(cs, hit, lag);
                        refined = _icp2d.Refine(cs, hit, hitPanel, init);
                    }

                    if (refined.Residual <= residualGate)
                        results.Add(refined);
                }
            }

            results.Sort((x, y) =>
            {
                int c = x.Residual.CompareTo(y.Residual);
                if (c != 0) return c;
                c = string.CompareOrdinal(x.A.PanelId, y.A.PanelId);
                if (c != 0) return c;
                return x.A.Index.CompareTo(y.A.Index);
            });
            return results;
        }

        // ====================================================================
        // R0: AGGLOMERATIVE assembly. Match ALL pairs -> weighted pair graph ->
        // minimum-residual spanning tree from a seed -> compose absolute poses
        // along the tree. Unlike the frame-anchored beam this does NOT require a
        // candidate to mate with an already-placed panel; pieces that mate only
        // to other (initially unplaced) pieces still chain through the tree.
        //
        // Composition direction (from the AontoB semantics): a directed edge's
        // MatchResult is produced by Refine(candSeg, hitSeg, hitPanel, init),
        // which works entirely in panel-LOCAL coordinates (a.LocalPolyline,
        // b.LocalPolyline, panelB.SourceContour/LocalFrame are all untransformed).
        // So AontoB maps the CANDIDATE panel's local space onto the HIT panel's
        // local space: it is the RELATIVE pose M(cand -> hit). If the hit (parent)
        // already has absolute world pose T_parent (parent-local -> world), the
        // candidate (child) absolute pose is T_child = T_parent * M(child->parent).
        // For the frame-anchored path T_anchor = identity, which is exactly why
        // the beam can store AontoB directly as the absolute transform there.
        // ====================================================================
        private AssemblyState SolveAgglomerative(
            IEnumerable<Panel> anchors, IEnumerable<Panel> pool,
            IReadOnlyList<MatchResult> candidateEdges = null)
        {
            // Deterministic, fixed iteration order everywhere: sort by panel id.
            var anchorList = anchors.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var poolList = pool.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

            // All panels participating, de-duplicated by id (an anchor must not
            // also appear in the pool), sorted by id for a stable node order.
            var byId = new Dictionary<string, Panel>(StringComparer.Ordinal);
            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in anchorList) { byId[a.Id] = a; anchorIds.Add(a.Id); }
            foreach (var p in poolList) if (!byId.ContainsKey(p.Id)) byId[p.Id] = p;
            var nodes = byId.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

            var state = new AssemblyState();
            if (nodes.Count == 0) return state;

            _scale = ComputeScale(anchorList, poolList);

            var edges = new List<PairEdge>();
            if (candidateEdges != null)
            {
                // --- Projection-bootstrap path: build the pair graph from the
                // EXTERNALLY-SUPPLIED candidate edges (e.g. ProjectionPairFinder
                // lift output). Each candidate's A.PanelId = child node, B.PanelId
                // = parent node, AontoB = child-local -> parent-local relative pose,
                // Residual = weight. One edge per unordered node pair (keep the
                // lowest-residual candidate when several reference the same pair).
                // Order as given; strict (residual, child-id) tie-break. ---
                var bestByPair = new Dictionary<string, PairEdge>(StringComparer.Ordinal);
                foreach (var m in candidateEdges)
                {
                    if (m == null) continue;
                    string childId = m.A.PanelId;
                    string parentId = m.B.PanelId;
                    if (!byId.ContainsKey(childId) || !byId.ContainsKey(parentId)) continue;
                    if (string.Equals(childId, parentId, StringComparison.Ordinal)) continue;

                    var e = new PairEdge(childId, parentId, childId, parentId, m.AontoB, m.Residual, m);
                    string key = e.LowId + "|" + e.HighId;
                    if (!bestByPair.TryGetValue(key, out var cur)
                        || m.Residual < cur.Weight
                        || (m.Residual == cur.Weight
                            && string.CompareOrdinal(childId, cur.ChildId) < 0))
                        bestByPair[key] = e;
                }
                // Deterministic edge order: by (LowId, HighId).
                foreach (var kv in bestByPair.OrderBy(k => k.Key, StringComparer.Ordinal))
                    edges.Add(kv.Value);
            }
            else
            {
                // --- 1. Pairwise match graph. For every unordered pair (i,j) keep the
                // single best directed MatchResult (lowest residual passing the gates),
                // trying BOTH directions because segmentation + complement hashing are
                // asymmetric. Edge weight = that residual. ---
                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        var pi = nodes[i];
                        var pj = nodes[j];

                        // Direction A: pi is candidate (child), pj is hit (parent).
                        var mA = BestMatchOrdered(pi, pj);
                        // Direction B: pj is candidate (child), pi is hit (parent).
                        var mB = BestMatchOrdered(pj, pi);

                        // Pick the lower-residual direction; strict id tie-break keeps
                        // determinism if residuals are exactly equal. (The spurious-match
                        // filtering happens upstream in BestMatchOrdered via the contact
                        // gate + ranking; the surviving edges are weighted by residual.)
                        PairEdge? best = null;
                        if (mA != null)
                            best = new PairEdge(pi.Id, pj.Id, childId: pi.Id, parentId: pj.Id, mA.AontoB, mA.Residual, mA);
                        if (mB != null)
                        {
                            bool takeB = best == null
                                || mB.Residual < best.Value.Weight
                                || (mB.Residual == best.Value.Weight
                                    && string.CompareOrdinal(pj.Id, best.Value.ChildId) < 0);
                            if (takeB)
                                best = new PairEdge(pi.Id, pj.Id, childId: pj.Id, parentId: pi.Id, mB.AontoB, mB.Residual, mB);
                        }

                        if (best != null) edges.Add(best.Value);
                    }
                }
            }

            // --- 1b. Phase 1 consensus: cycle-consistency outlier penalty. ---
            // Close every triangle of the pair graph (compose the three relative poses
            // around the loop; a consistent loop returns to identity). Edges whose loops
            // persistently fail to close are penalized so the greedy seed/MST avoids the
            // tight-but-wrong matches it would otherwise lock in. Opt-in; null = legacy.
            Dictionary<string, double>? cyclePenalty = null;
            if (_opt.UseCycleConsistency && edges.Count >= 3)
            {
                // Low-local -> High-local relative pose per undirected pair.
                var rel = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var e in edges)
                {
                    Transform lowToHigh;
                    if (string.Equals(e.ChildId, e.LowId, StringComparison.Ordinal))
                        lowToHigh = e.Relative;                       // child=Low,parent=High: maps Low->High
                    else if (!e.Relative.TryGetInverse(out lowToHigh))
                        lowToHigh = Transform.Identity;               // child=High: invert High->Low
                    rel[e.LowId + "|" + e.HighId] = lowToHigh;
                }
                var devs = new Dictionary<string, List<double>>(StringComparer.Ordinal);
                foreach (var e in edges) devs[e.LowId + "|" + e.HighId] = new List<double>();

                // Directed relative pose from->to via the Low|High table.
                Func<string, string, Transform> relDir = (from, to) =>
                {
                    bool fromLow = string.CompareOrdinal(from, to) <= 0;
                    var lh = rel[fromLow ? from + "|" + to : to + "|" + from];
                    if (fromLow) return lh;
                    lh.TryGetInverse(out var inv); return inv;
                };

                var nodeIds = nodes.Select(p => p.Id).ToList();       // already id-sorted
                for (int i = 0; i < nodeIds.Count; i++)
                for (int j = i + 1; j < nodeIds.Count; j++)
                {
                    string a = nodeIds[i], b = nodeIds[j];
                    if (!rel.ContainsKey(a + "|" + b)) continue;
                    for (int k = j + 1; k < nodeIds.Count; k++)
                    {
                        string c = nodeIds[k];
                        if (!rel.ContainsKey(b + "|" + c) || !rel.ContainsKey(a + "|" + c)) continue;
                        // loop a->b->c->a; consistent => identity.
                        var loop = Transform.Multiply(relDir(c, a), Transform.Multiply(relDir(b, c), relDir(a, b)));
                        double dev = LoopDeviation(loop, _scale);
                        devs[a + "|" + b].Add(dev);
                        devs[b + "|" + c].Add(dev);
                        devs[a + "|" + c].Add(dev);
                    }
                }
                cyclePenalty = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var kv in devs)
                {
                    var list = kv.Value;
                    if (list.Count == 0) { cyclePenalty[kv.Key] = 0.0; continue; }
                    list.Sort();
                    double median = list[list.Count / 2];   // robust: a correct edge is bad only in loops with a wrong edge
                    cyclePenalty[kv.Key] = median > _opt.CycleConsistencyTolerance ? _opt.CycleConsistencyPenalty : 0.0;
                }
            }

            // --- 1c. Phase 1c best-buddies: penalize non-mutual-best edges. ---
            // An edge is trustworthy only if it is the lowest-weight edge for BOTH its
            // endpoints. The impostor (a non-adjacent piece that mates ~as well locally)
            // is some other piece's true neighbour, so it is not mutual-best -> penalize
            // it so the MST/seed prefers mutual edges, falling back to non-mutual only
            // when a node has no mutual edge. Folded into the same penalty dict.
            if (_opt.UseBestBuddies && edges.Count > 0)
            {
                var bestKey = new Dictionary<string, string>(StringComparer.Ordinal);
                var bestW = new Dictionary<string, double>(StringComparer.Ordinal);
                // Edges are in deterministic (LowId,HighId) order; strict < keeps the
                // first-seen lowest as the unique best (deterministic tie-break).
                foreach (var e in edges)
                {
                    string k = e.LowId + "|" + e.HighId;
                    if (!bestW.TryGetValue(e.LowId, out var wl) || e.Weight < wl) { bestW[e.LowId] = e.Weight; bestKey[e.LowId] = k; }
                    if (!bestW.TryGetValue(e.HighId, out var wh) || e.Weight < wh) { bestW[e.HighId] = e.Weight; bestKey[e.HighId] = k; }
                }
                cyclePenalty = cyclePenalty ?? new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var e in edges)
                {
                    string k = e.LowId + "|" + e.HighId;
                    bool mutual = bestKey.TryGetValue(e.LowId, out var kl) && kl == k
                               && bestKey.TryGetValue(e.HighId, out var kh) && kh == k;
                    if (!mutual)
                    {
                        cyclePenalty.TryGetValue(k, out var cur);
                        cyclePenalty[k] = cur + _opt.BestBuddyPenalty;
                    }
                }
            }

            // --- 2. Global resolve: minimum-residual spanning tree from a seed. ---
            // Adjacency: for each node, the edges incident to it. Iterate edges in
            // their fixed insertion order (pairs sorted by (i,j) panel id) so the
            // Prim frontier is deterministic before tie-breaks.
            var adjacency = new Dictionary<string, List<PairEdge>>(StringComparer.Ordinal);
            foreach (var n in nodes) adjacency[n.Id] = new List<PairEdge>();
            foreach (var e in edges)
            {
                adjacency[e.LowId].Add(e);
                adjacency[e.HighId].Add(e);
            }

            // Prim's algorithm growing a MINIMUM-residual spanning tree. Seeds:
            //   * with anchors: ALL anchored panels are pre-seeded at their given
            //     transforms (a multi-rim anchor fragment keeps every rim fixed),
            //     and the tree grows from that anchor forest.
            //   * no anchors: the lower-id endpoint of the globally-best
            //     (lowest-weight) edge seeds at identity.
            // Each accepted edge composes the child's absolute pose from the
            // parent's absolute pose and the edge's relative transform. Strict
            // (weight, edge-id, child-id) tie-breaks keep it deterministic.
            var inTree = new HashSet<string>(StringComparer.Ordinal);
            var absolute = new Dictionary<string, Transform>(StringComparer.Ordinal);

            if (anchorList.Count > 0)
            {
                foreach (var a in anchorList) // sorted by id
                {
                    if (inTree.Contains(a.Id)) continue;
                    inTree.Add(a.Id);
                    absolute[a.Id] = a.AppliedTransform;
                    state.PlacedPanels.Add(a);
                    state.AppliedTransforms[a.Id] = a.AppliedTransform;
                }
            }
            else if (edges.Count > 0)
            {
                var bestEdge = edges[0];
                foreach (var e in edges)
                {
                    double we = e.Weight + EdgePenalty(cyclePenalty, e);
                    double wb = bestEdge.Weight + EdgePenalty(cyclePenalty, bestEdge);
                    if (we < wb || (we == wb && CompareEdgeId(e, bestEdge) < 0))
                        bestEdge = e;
                }
                // Lower-id endpoint of the best edge is the seed (deterministic).
                string seedId = string.CompareOrdinal(bestEdge.LowId, bestEdge.HighId) <= 0
                    ? bestEdge.LowId : bestEdge.HighId;
                var seedPanel = byId[seedId];
                inTree.Add(seedId);
                absolute[seedId] = Transform.Identity;
                state.PlacedPanels.Add(seedPanel);
                state.AppliedTransforms[seedId] = Transform.Identity;
            }
            else
            {
                // No matches and no anchors: nothing to place.
                return state;
            }

            // R2 (A3): cache the absolute placed contours so the overlap term does
            // not re-transform on every frontier comparison. Only built when the
            // penalty is on. Maps id -> transformed-and-closed contour.
            var placedContours = _opt.OverlapPenalty > 0.0
                ? new Dictionary<string, Curve?>(StringComparer.Ordinal)
                : null;
            if (placedContours != null)
                foreach (var id in inTree)
                    placedContours[id] = TransformedContour(byId[id], absolute[id]);

            while (true)
            {
                PairEdge? pick = null;
                string? pickChild = null;
                Transform pickChildAbs = Transform.Identity;
                double pickScore = double.PositiveInfinity;

                // Scan every frontier edge (one endpoint in the tree, the other
                // out) and keep the minimum by (effective-weight, edge-id,
                // child-id). Effective weight = edge residual + the R2 overlap
                // penalty for placing the child at its composed pose against the
                // panels already in the tree (penalty term is 0 / skipped when
                // OverlapPenalty is 0, so the selection is unchanged by default).
                foreach (var inId in inTree.OrderBy(s => s, StringComparer.Ordinal))
                {
                    foreach (var e in adjacency[inId])
                    {
                        string other = e.LowId == inId ? e.HighId : e.LowId;
                        if (inTree.Contains(other)) continue;

                        // Compose the OUT node's absolute pose using the edge's
                        // stored child/parent roles. The in-tree endpoint is the
                        // parent here regardless of the edge's intrinsic child role,
                        // so invert the relative transform when the in-tree node is
                        // the edge's child.
                        Transform childAbs;
                        if (e.ParentId == inId)
                        {
                            // edge: child=other, parent=inId. M maps other-local ->
                            // inId-local. other_abs = inId_abs * M.
                            childAbs = Transform.Multiply(absolute[inId], e.Relative);
                        }
                        else
                        {
                            // edge: child=inId, parent=other. M maps inId-local ->
                            // other-local. We need other_abs from inId_abs:
                            // inId_abs = other_abs * M  =>  other_abs = inId_abs * M^-1.
                            Transform inv;
                            if (!e.Relative.TryGetInverse(out inv))
                                continue; // non-invertible relative pose: skip edge
                            childAbs = Transform.Multiply(absolute[inId], inv);
                        }

                        double score = e.Weight + EdgePenalty(cyclePenalty, e);
                        if (placedContours != null)
                        {
                            var childCurve = TransformedContour(byId[other], childAbs);
                            score += _opt.OverlapPenalty
                                   * OverlapFractionAgainst(byId[other], childCurve, placedContours);
                        }

                        bool better;
                        if (pick == null) better = true;
                        else
                        {
                            int wc = score.CompareTo(pickScore);
                            if (wc != 0) better = wc < 0;
                            else
                            {
                                int ec = CompareEdgeId(e, pick.Value);
                                if (ec != 0) better = ec < 0;
                                else better = string.CompareOrdinal(other, pickChild) < 0;
                            }
                        }
                        if (!better) continue;

                        pick = e;
                        pickChild = other;
                        pickChildAbs = childAbs;
                        pickScore = score;
                    }
                }

                if (pick == null) break; // no frontier edge: remaining nodes unreachable

                inTree.Add(pickChild!);
                absolute[pickChild!] = pickChildAbs;
                var childPanel = byId[pickChild!];
                state.PlacedPanels.Add(childPanel);
                state.AppliedTransforms[pickChild!] = pickChildAbs;
                state.TotalResidual += pick.Value.Weight;
                state.History.Add(pick.Value.Match);
                if (placedContours != null)
                    placedContours[pickChild!] = TransformedContour(childPanel, pickChildAbs);
            }

            return state;
        }

        // Best directed match: candidate's segments vs the index, keeping only
        // hits that belong to hitPanel, refined by the same ICP / gates as
        // TryPlace. Returns the lowest-residual passing result, or null. The
        // index must already contain hitPanel's segments (callers build it over
        // all panels). Mirrors TryPlace's inner loop but constrained to one pair
        // and stateless (no PlacedPanels lookup).
        private MatchResult? BestMatchOrdered(Panel candidate, Panel hitPanel)
        {
            double residualGate = _opt.ResidualThresholdFactor > 0.0
                ? _opt.ResidualThresholdFactor * _scale
                : _opt.ResidualThreshold;

            var candSegments = candidate.Mode == PanelMode.Spatial3D
                ? BoundarySegmenter3D.Segment(candidate, _segOpt3D)
                : BoundarySegmenter.Segment(candidate, _segOpt);

            MatchResult? best = null;
            double bestContact = -1.0;
            foreach (var cs in candSegments)
            {
                var hits = _index.QueryComplement(cs);
                foreach (var hit in hits)
                {
                    if (!string.Equals(hit.PanelId, hitPanel.Id, StringComparison.Ordinal))
                        continue;

                    var (lag, score) = PhaseCorrelator.Correlate(cs.TurningSignature, hit.TurningSignature);
                    if (score < _opt.PhaseScoreThreshold) continue;

                    bool pairIs3D = candidate.Mode == PanelMode.Spatial3D
                                 || hitPanel.Mode == PanelMode.Spatial3D;

                    MatchResult refined;
                    if (pairIs3D)
                    {
                        var init = InitialTransformBuilder.FromLag3D(cs, hit, lag);
                        refined = _icp3d.Refine(cs, hit, hitPanel, _substrate, init);
                    }
                    else
                    {
                        var init = InitialTransformBuilder.FromLag2D(cs, hit, lag);
                        refined = _icp2d.Refine(cs, hit, hitPanel, init);
                    }

                    if (refined.Residual > residualGate) continue;

                    // Phase 1b: contact-seam-length discriminator. A true neighbour
                    // shares a long contiguous complementary seam; a spurious match
                    // shares one coincidental fragment with an equally-low residual.
                    // Reject below the contact floor and rank survivors by contact
                    // first (then residual). When off, contact = 1.0 (no gate) and the
                    // ranking falls back to lowest-residual exactly as before.
                    double contact = 1.0;
                    if (_opt.UseContactScore)
                    {
                        contact = ContactFraction(candidate, hitPanel, refined.AontoB);
                        if (contact < _opt.MinContactFraction) continue;
                    }

                    bool take;
                    if (best == null) take = true;
                    else if (_opt.UseContactScore)
                    {
                        int cc = contact.CompareTo(bestContact);
                        if (cc != 0) take = cc > 0;                       // higher contact wins
                        else
                        {
                            int rc = refined.Residual.CompareTo(best.Residual);
                            take = rc != 0 ? rc < 0 : SegmentTieBreak(refined, best) < 0;
                        }
                    }
                    else
                    {
                        take = refined.Residual < best.Residual
                            || (refined.Residual == best.Residual && SegmentTieBreak(refined, best) < 0);
                    }

                    if (take) { best = refined; bestContact = contact; }
                }
            }
            return best;
        }

        // Phase 1b: fraction of the candidate perimeter that lands within a scale-relative
        // band of the hit boundary once the candidate is moved by the match pose. Both
        // contours are panel-local; AontoB maps candidate-local -> hit-local, so the
        // transformed candidate shares the hit's frame. A true mate touches along one
        // whole seam (~1/sides of the perimeter); a spurious fragment touches a sliver.
        private double ContactFraction(Panel candidate, Panel hitPanel, Transform aontoB)
        {
            var cc = (PolylineCurve)candidate.SourceContour.DuplicateCurve();
            cc.Transform(aontoB);
            var hit = hitPanel.SourceContour;
            double eps = Math.Max(1e-6, _opt.ContactToleranceFraction * (_scale > 1e-9 ? _scale : 1.0));
            int n = Math.Max(8, _opt.ContactSamples);
            var ts = cc.DivideByCount(n, true);
            if (ts == null || ts.Length == 0) return 0.0;
            int within = 0;
            foreach (var t in ts)
            {
                var pt = cc.PointAt(t);
                if (hit.ClosestPoint(pt, out double hp) && pt.DistanceTo(hit.PointAt(hp)) <= eps)
                    within++;
            }
            return (double)within / ts.Length;
        }

        private static int SegmentTieBreak(MatchResult x, MatchResult y)
        {
            int c = string.CompareOrdinal(x.A.PanelId, y.A.PanelId);
            if (c != 0) return c;
            c = x.A.Index.CompareTo(y.A.Index);
            if (c != 0) return c;
            c = string.CompareOrdinal(x.B.PanelId, y.B.PanelId);
            if (c != 0) return c;
            return x.B.Index.CompareTo(y.B.Index);
        }

        // Phase 1: cycle-consistency penalty for an edge (0 when off or consistent).
        private static double EdgePenalty(Dictionary<string, double>? cyclePenalty, PairEdge e)
            => cyclePenalty != null && cyclePenalty.TryGetValue(e.LowId + "|" + e.HighId, out var p) ? p : 0.0;

        // Deviation of a loop-closure transform from identity: rotation angle (rad,
        // trace-based, valid for 2D Z-rotations and 3D) + scale-relative translation.
        private static double LoopDeviation(Transform t, double scale)
        {
            double cos = (t.M00 + t.M11 + t.M22 - 1.0) * 0.5;
            if (cos > 1.0) cos = 1.0; else if (cos < -1.0) cos = -1.0;
            double ang = Math.Acos(cos);
            double tmag = Math.Sqrt(t.M03 * t.M03 + t.M13 * t.M13 + t.M23 * t.M23);
            double s = scale > 1e-9 ? scale : 1.0;
            return ang + tmag / s;
        }

        // Ordinal compare on the (LowId, HighId) endpoint pair: a stable,
        // direction-independent identity for an undirected pair edge.
        private static int CompareEdgeId(PairEdge a, PairEdge b)
        {
            int c = string.CompareOrdinal(a.LowId, b.LowId);
            return c != 0 ? c : string.CompareOrdinal(a.HighId, b.HighId);
        }

        // One undirected pair-graph edge carrying the best directed relative pose.
        // LowId/HighId are the id-sorted endpoints (undirected identity). ChildId/
        // ParentId record which endpoint was the ICP candidate vs hit, so the
        // composition along the tree gets the AontoB direction right. Relative is
        // that MatchResult.AontoB (child-local -> parent-local). Match is kept for
        // the history. Weight is the residual.
        private readonly struct PairEdge
        {
            public readonly string LowId;
            public readonly string HighId;
            public readonly string ChildId;
            public readonly string ParentId;
            public readonly Transform Relative;
            public readonly double Weight;
            public readonly MatchResult Match;

            public PairEdge(string a, string b, string childId, string parentId,
                Transform relative, double weight, MatchResult match)
            {
                if (string.CompareOrdinal(a, b) <= 0) { LowId = a; HighId = b; }
                else { LowId = b; HighId = a; }
                ChildId = childId;
                ParentId = parentId;
                Relative = relative;
                Weight = weight;
                Match = match;
            }
        }

        // Median per-panel bounding-box diagonal across anchors + pool. Used as
        // the object scale for A1 scale-relative gates. Median (not the combined
        // bbox) so it is invariant to how far apart the unplaced pieces are
        // scattered. Deterministic: ascending sort, lower-median on even counts.
        private static double ComputeScale(IEnumerable<Panel> anchors, IEnumerable<Panel> pool)
        {
            var diags = new List<double>();
            foreach (var p in anchors)
                AddDiag(p, diags);
            foreach (var p in pool)
                AddDiag(p, diags);
            if (diags.Count == 0) return 1.0;
            diags.Sort();
            double med = diags[(diags.Count - 1) / 2];
            return med > 1e-9 ? med : 1.0;
        }

        private static void AddDiag(Panel p, List<double> diags)
        {
            var c = p?.SourceContour;
            if (c == null) return;
            var bb = c.GetBoundingBox(false);
            if (bb.IsValid) diags.Add(bb.Diagonal.Length);
        }

        private static string JoinIds(AssemblyState s) =>
            string.Join("|", s.PlacedPanels.Select(p => p.Id));

        // ====================================================================
        // R2: overlap helpers (only invoked when OverlapPenalty > 0 /
        // EdgeExclusivity on). All deterministic: pure geometry, no randomness.
        // ====================================================================

        // Stable identity for a placed boundary segment: "PanelId#Index".
        private static string SegmentKey(Segment s) => s.PanelId + "#" + s.Index;

        // The candidate placed at AontoB, tested against every already-placed
        // panel's transformed contour. Returns the summed overlap area divided by
        // the candidate's own area (scale-relative fraction). 2D curve-curve
        // boolean intersection. Closed planar contours only contribute; 3D / open
        // contours fall through to 0 (the 2D overlap term is a no-op in 3D, where
        // depenetration is the mesh-based Contact Settle path instead).
        private double OverlapFraction(
            Panel candidate, Transform candXform,
            IReadOnlyList<Panel> placed, IReadOnlyDictionary<string, Transform> placedXforms)
        {
            var candCurve = TransformedContour(candidate, candXform);
            if (candCurve == null) return 0.0;
            double candArea = ClosedArea(candCurve);
            if (candArea <= 1e-12) return 0.0;

            double overlap = 0.0;
            foreach (var p in placed)
            {
                Transform t = placedXforms.TryGetValue(p.Id, out var xf) ? xf : Transform.Identity;
                var other = TransformedContour(p, t);
                if (other == null) continue;
                overlap += IntersectionArea(candCurve, other);
            }
            return overlap / candArea;
        }

        // Overlap fraction of a pre-transformed child curve against a set of
        // already-placed transformed contours (agglomerative path; the contours
        // are cached so they are not rebuilt per comparison).
        private double OverlapFractionAgainst(
            Panel childPanel, Curve? childCurve, IReadOnlyDictionary<string, Curve?> placedContours)
        {
            if (childCurve == null) return 0.0;
            double childArea = ClosedArea(childCurve);
            if (childArea <= 1e-12) return 0.0;

            double overlap = 0.0;
            foreach (var kv in placedContours)
            {
                if (string.Equals(kv.Key, childPanel.Id, StringComparison.Ordinal)) continue;
                if (kv.Value == null) continue;
                overlap += IntersectionArea(childCurve, kv.Value);
            }
            return overlap / childArea;
        }

        private static Curve? TransformedContour(Panel p, Transform t)
        {
            var c = p?.SourceContour;
            if (c == null || !c.IsClosed) return null;
            var dup = (PolylineCurve)c.DuplicateCurve();
            dup.Transform(t);
            return dup;
        }

        private static double ClosedArea(Curve c)
        {
            if (c == null || !c.IsClosed) return 0.0;
            var amp = AreaMassProperties.Compute(c);
            return amp == null ? 0.0 : Math.Abs(amp.Area);
        }

        // Pairwise interpenetration area via 2D curve boolean intersection, with a
        // bounding-box pre-filter. Mirrors the harness Validator.IntersectionArea2D
        // so the penalty term and the reported overlap use the same measure.
        private static double IntersectionArea(Curve a, Curve b)
        {
            const double tol = 1e-4;
            var ba = a.GetBoundingBox(false);
            var bb = b.GetBoundingBox(false);
            if (!ba.IsValid || !bb.IsValid) return 0.0;
            if (ba.Max.X < bb.Min.X || ba.Min.X > bb.Max.X) return 0.0;
            if (ba.Max.Y < bb.Min.Y || ba.Min.Y > bb.Max.Y) return 0.0;

            Curve[]? regions = null;
            try { regions = Curve.CreateBooleanIntersection(a, b, tol); }
            catch { regions = null; }
            if (regions == null || regions.Length == 0) return 0.0;

            double area = 0.0;
            foreach (var r in regions)
            {
                if (r == null || !r.IsClosed) continue;
                var amp = AreaMassProperties.Compute(r);
                if (amp != null) area += Math.Abs(amp.Area);
            }
            return area;
        }
    }
}
