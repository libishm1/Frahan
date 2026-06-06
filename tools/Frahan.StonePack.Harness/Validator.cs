#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Frahan.Core;
using Frahan.Core.Packing;
using Frahan.EdgeMatching;
using Frahan.GH.TwoD;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Packing;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Frahan.StonePack.Harness
{
    /// <summary>
    /// RhinoCommon-heavy validation logic. Kept in its own type so it is only
    /// jitted after Rhino.Inside has booted (Program.Run is the JIT boundary;
    /// nothing here is touched until then).
    ///
    /// 2D path mirrors TrencadisEdgeMatchComponent / EdgeMatchSolveComponent:
    ///   Frame2D polyline -> anchored Frame Panel; Fragments2D polylines ->
    ///   Shard Panels; SegmentHashIndex via BoundarySegmenter; AssemblySolver.
    ///
    /// 3D path mirrors KintsugiAssemblyComponent:
    ///   Shards3D meshes -> naked-edge rim loops -> 3D Panels; first fragment's
    ///   rims anchored as Frames; AssemblySolver; per-fragment transform taken
    ///   from any placed rim panel of that fragment.
    /// </summary>
    internal static class Validator
    {
        // Segmenter / solver knobs. Match the GH component defaults so the
        // numbers the harness prints are the numbers a canvas user would see.
        private const double SampleSpacing = 1.0;
        private const double BreakAngleDeg = 18.0;       // 2D component default
        private const double MinSegmentLength = 8.0;     // 2D component default
        private const double ResidualThreshold2D = 1.0;
        private const int BeamWidth2D = 16;              // Trencadis default
        private const int MaxIterations = 1000;
        // R2 (--resolve): beam/edge overlap penalty weight. 2.0 dominates the
        // ~0.01 per-pair residuals on this fixture so overlapping placements rank
        // below non-overlapping ones in the beam sort and MST edge selection.
        private const double OverlapPenalty2D = 2.0;

        // R1 partial sub-segment emission knobs (only used when --partial is on).
        // Two-scale ladder (half + quarter of each base segment span), tiled
        // non-overlapping. Matches AssemblyOptions defaults so index side and
        // candidate side agree.
        private static readonly double[] PartialFractions = { 0.5, 0.25 };
        private const double PartialStride = 1.0;

        // 3D / Kintsugi defaults.
        private const double BreakAngleDeg3D = 8.0;
        private const double MinSegmentLength3D = 1.0;
        private const double JointWidth3D = 1.0;
        private const int BeamWidth3D = 32;
        private const int MaxIterations3D = 2000;
        private const double MinLoopLength3D = 10.0;

        // ====================================================================
        // 2D
        // ====================================================================
        public static int Run2D(HarnessOptions opts, Action<string> emit)
        {
            if (!File.Exists(opts.FixturePath))
            {
                emit("ERROR: fixture not found: " + opts.FixturePath);
                return 1;
            }

            using var doc = File3dm.Read(opts.FixturePath);
            if (doc == null)
            {
                emit("ERROR: File3dm.Read returned null.");
                return 1;
            }

            var frameCurves = ReadCurvesOnLayer(doc, "Frame2D");
            var fragCurves = ReadCurvesOnLayer(doc, "Fragments2D");
            var truthCurves = ReadCurvesOnLayer(doc, "Assembled2D");

            emit($"layers   : Frame2D={frameCurves.Count}  Fragments2D={fragCurves.Count}  Assembled2D={truthCurves.Count}");
            if (fragCurves.Count < 2)
            {
                emit("ERROR: need >= 2 Fragments2D curves (anchor + at least one to place).");
                return 1;
            }

            // --- REASSEMBLY model: anchor the FIRST fragment as the seed and
            // match the rest to it (and to each other) by their shared CUT
            // edges. Anchoring the full-rectangle Frame2D fails because its long
            // boundary edges do not hash-match the fragments' short sub-edges;
            // a fragment seed shares real cut-edge segments with its neighbours.
            // (This mirrors how the 3D Kintsugi path anchors fragment 0.)
            // Frame2D, if present, is used only as the coverage denominator. ---
            PolylineCurve? framePcOpt = null;
            if (frameCurves.Count > 0 && TryToPolylineCurve(frameCurves[0], out var fpc0) && fpc0.IsClosed)
                framePcOpt = fpc0;

            var shardPanels = new List<Panel>();
            for (int i = 0; i < fragCurves.Count; i++)
            {
                if (TryToPolylineCurve(fragCurves[i], out var pc) && pc.IsClosed)
                    shardPanels.Add(new Panel($"s{i:D4}", pc, PanelKind.Shard));
                else
                    emit($"  warn: Fragments2D[{i}] not a closed polyline; skipped.");
            }
            if (shardPanels.Count < 2)
            {
                emit("ERROR: need >= 2 usable (closed) shard panels.");
                return 1;
            }
            var anchorPanel = shardPanels[0];
            // Mark the seed anchored so the R2 depenetration polish locks it (the
            // beam Solve also sets this, but the agglomerative path does not; the
            // flag is harmless to the default measurement, which does not move
            // anything). The pool panels are placed relative to this fixed seed.
            anchorPanel.IsAnchored = true;
            var poolPanels = shardPanels.Skip(1).ToList();

            var segOpt = new SegmenterOptions
            {
                SampleSpacing = SampleSpacing,
                BreakAngleDeg = BreakAngleDeg,
                MinSegmentLength = MinSegmentLength,
                // R1: --partial emits sub-windows so a long edge hash-matches
                // the short sub-edge it physically mates with. Applied to the
                // INDEX-build segmentation here AND to candidate re-segmentation
                // via AssemblyOptions below (both sides must emit partials).
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
            };
            var segOpt3D = new SegmenterOptions3D
            {
                SampleSpacing = SampleSpacing,
                BreakAngleDeg = BreakAngleDeg,
                MinSegmentLength = MinSegmentLength,
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
            };
            var index = new SegmentHashIndex();
            foreach (var p in shardPanels) AddSegmentsFor(p, segOpt, segOpt3D, index);

            var asmOpt = new AssemblyOptions
            {
                // R0: --agglomerative switches to the pairwise-graph + spanning-tree
                // model. Default (off) keeps the frame-anchored beam unchanged.
                Mode = opts.Agglomerative ? AssemblyMode.Agglomerative : AssemblyMode.FrameAnchored,
                BeamWidth = BeamWidth2D,
                MaxIterations = MaxIterations,
                ResidualThreshold = ResidualThreshold2D,
                NonCrossingCorrespondence = opts.NonCrossing,
                // A1: scale-relative gates when --autoscale F is passed.
                ResidualThresholdFactor = opts.AutoScale,
                PhaseScoreThreshold = opts.AutoScale > 0 ? 0.35 : 0.5,
                // R1: candidate-side partial emission (matches the index side).
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
                // R2: --resolve turns on the global non-overlap term. OverlapPenalty
                // 2.0 = an overlap equal to the whole piece costs as much as a 2.0
                // residual, which dominates the ~0.01 per-pair residuals here so
                // overlapping placements sink in the beam / edge selection.
                // EdgeExclusivity stops two pieces snapping to the same placed edge.
                OverlapPenalty = opts.Resolve ? OverlapPenalty2D : 0.0,
                EdgeExclusivity = opts.Resolve,
            };
            var solver = new AssemblySolver(index, asmOpt, segOpt, segOpt3D);
            var state = solver.Solve(new[] { anchorPanel }, poolPanels);

            // R2 post-solve 2D rigid depenetration polish (the 2D Contact Settle).
            // Anchor-locked Jacobi relaxation that translates overlapping placed
            // contours apart until pairwise overlap is within tolerance. Opt-in.
            if (opts.Resolve)
            {
                var (resFrac, resIters) = OverlapResolver2D.Resolve(state, asmOpt);
                emit($"resolve  : depenetration polish ran {resIters} iters, " +
                     $"final max pairwise overlap {resFrac * 100.0:F2}% of smaller piece");
            }

            // Pillar A: Soft-ICP (EM weighted-Kabsch) rim-contact + non-penetration
            // refine. Opt-in via --softicp. Runs on the placed AssemblyState: pulls
            // the placed-contour boundaries (the 2D "rims") into contact while a
            // smooth penetration hinge keeps the closed contours from overlapping.
            // Anchor-locked (the seed shard stays put). Reports rim-gap + overlap
            // before vs after.
            if (opts.SoftIcp)
                RunSoftIcp2D(state, emit);

            // Placed contours INCLUDING the anchor (at its identity pose), since
            // the anchor occupies space and must count for overlap / coverage.
            var placed = new List<PolylineCurve>();
            foreach (var panel in state.PlacedPanels)
            {
                Transform t = state.AppliedTransforms.TryGetValue(panel.Id, out var xf)
                    ? xf : Transform.Identity;
                var c = (PolylineCurve)panel.SourceContour.DuplicateCurve();
                c.Transform(t);
                placed.Add(c);
            }

            emit(new string('-', 64));
            emit($"solve    : placed {placed.Count} of {shardPanels.Count} shards");
            emit($"residual : total {state.TotalResidual:F4}  ({state.History.Count} placement events)");

            // --- Overlap: pairwise interpenetration area via boolean intersection ---
            double tol = 1e-4;
            int overlapPairs = 0;
            double maxOverlap = 0.0;
            double totalOverlap = 0.0;
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    double a = IntersectionArea2D(placed[i], placed[j], tol);
                    if (a > tol)
                    {
                        overlapPairs++;
                        totalOverlap += a;
                        if (a > maxOverlap) maxOverlap = a;
                    }
                }
            }

            // --- Coverage: total placed area vs frame area (Frame2D if present,
            //     else the bounding-box area of the placed layout) ---
            double frameArea;
            if (framePcOpt != null)
            {
                frameArea = ClosedCurveArea(framePcOpt);
            }
            else
            {
                var ub = BoundingBox.Unset;
                foreach (var c in placed) ub.Union(c.GetBoundingBox(false));
                frameArea = ub.IsValid ? (ub.Max.X - ub.Min.X) * (ub.Max.Y - ub.Min.Y) : 0.0;
            }
            double placedArea = placed.Sum(ClosedCurveArea);
            double coverage = frameArea > tol ? placedArea / frameArea : double.NaN;
            // True packing number: UNION area of the placed contours (overlaps
            // counted once). sum-of-areas masks overlap; union does not. union ==
            // sum only when there is no interpenetration.
            double unionArea = UnionArea2D(placed, tol);
            double unionCoverage = frameArea > tol ? unionArea / frameArea : double.NaN;

            emit(new string('-', 64));
            emit("OVERLAP (interpenetration):");
            emit($"  overlapping pairs    : {overlapPairs} of {Pairs(placed.Count)}");
            emit($"  total overlap area   : {totalOverlap:F4} (model units^2)");
            emit($"  max pairwise overlap : {maxOverlap:F4}");
            emit($"  overlap / placed area: {(placedArea > tol ? (totalOverlap / placedArea * 100.0) : 0.0):F2} %");
            emit("COVERAGE / packing:");
            emit($"  frame area           : {frameArea:F4}");
            emit($"  total placed area    : {placedArea:F4} (sum of areas; masks overlap)");
            emit($"  union placed area    : {(unionArea > 0 ? unionArea.ToString("F4", CultureInfo.InvariantCulture) : "n/a")} (overlaps counted once)");
            emit($"  coverage (sum/frame) : {(double.IsNaN(coverage) ? "n/a" : (coverage * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %")}");
            emit($"  coverage (union/frame): {(double.IsNaN(unionCoverage) || unionArea <= 0 ? "n/a" : (unionCoverage * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %")} (true packing number)");

            // --- Reassembly error vs ground truth (recoverable: equal counts) ---
            if (truthCurves.Count == placed.Count && placed.Count > 0)
            {
                double err = CentroidMatchError2D(placed, truthCurves);
                emit("REASSEMBLY (vs Assembled2D ground truth):");
                emit($"  mean centroid residual: {err:F4} (model units, greedy nearest match)");
            }
            else
            {
                emit("REASSEMBLY: ground-truth count != placed count; skipping centroid match.");
            }

            return 0;
        }

        // ====================================================================
        // 3D
        // ====================================================================
        public static int Run3D(HarnessOptions opts, Action<string> emit)
        {
            if (!File.Exists(opts.FixturePath))
            {
                emit("ERROR: fixture not found: " + opts.FixturePath);
                return 1;
            }

            using var doc = File3dm.Read(opts.FixturePath);
            if (doc == null)
            {
                emit("ERROR: File3dm.Read returned null.");
                return 1;
            }

            var shardMeshes = ReadMeshesOnLayer(doc, "Shards3D");
            var truthMeshes = ReadMeshesOnLayer(doc, "Assembled3D");
            emit($"layers   : Shards3D={shardMeshes.Count}  Assembled3D={truthMeshes.Count}");
            if (shardMeshes.Count < 2)
            {
                emit("ERROR: need at least 2 Shards3D meshes to assemble.");
                return 1;
            }

            // Pillar A: Soft-ICP refiner demonstration from a PERTURBED-GROUND-TRUTH
            // start. The GEOMETRIC solver places 0 fragments on this fixture (the
            // shards are closed convex hulls with independent tessellation -> no
            // hash pairs), so there is nothing to refine from the solver output.
            // To ISOLATE and prove the refiner we load Assembled3D (the GT poses),
            // apply a known small random SE(3) perturbation per shard, run the
            // refiner, and show it converges back to rim-contact with penetration
            // ~0. Opt-in via --softicp. This does not depend on 3D candidate
            // generation. Runs first, then the normal geometric path proceeds.
            if (opts.SoftIcp)
                RunSoftIcp3DPerturbedGt(truthMeshes, emit);

            // --- Extract naked-edge rim loops per fragment (mirror Kintsugi) ---
            var rimsPerFragment = new List<List<PolylineCurve>>();
            int rimCount = 0;
            for (int f = 0; f < shardMeshes.Count; f++)
            {
                var loops = ExtractNakedRimLoops(shardMeshes[f], MinLoopLength3D);
                rimsPerFragment.Add(loops);
                rimCount += loops.Count;
            }
            emit($"rims     : {rimCount} naked-edge loops across {shardMeshes.Count} fragments");
            if (rimCount == 0)
            {
                emit("NOTE: no naked-edge rims found. The fixture shards are CLOSED convex hulls");
                emit("      (no open fracture boundaries), so the geometric rim-matching path");
                emit("      has nothing to lock onto. This is expected for closed-cell shatter");
                emit("      fixtures: the Kintsugi GEOMETRIC path needs open rims; the learned");
                emit("      Port path works from point clouds instead. Reporting input-state");
                emit("      overlap only.");
                ReportInputStateOverlap3D(shardMeshes, JointWidth3D, emit);
                return 0;
            }

            // --- 2.5D PROJECTION BOOTSTRAP path (opt-in, --project3d). Project each
            // naked rim into its facet plane, match the projected rims with the 2D
            // path, lift each match to a 3D relative pose, feed those candidate
            // edges to the agglomerative solver, then refine the placed fragments
            // with Soft-ICP and report. Returns when done; the normal geometric path
            // below is the default (off). ---
            if (opts.Project3D)
                return RunProjectionBootstrap3D(opts, shardMeshes, rimsPerFragment, emit);

            var segOpt = new SegmenterOptions
            {
                SampleSpacing = SampleSpacing,
                BreakAngleDeg = BreakAngleDeg3D,
                MinSegmentLength = MinSegmentLength3D,
                // R1: --partial sub-window emission (index side).
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
            };
            var segOpt3D = new SegmenterOptions3D
            {
                SampleSpacing = SampleSpacing,
                BreakAngleDeg = BreakAngleDeg3D,
                MinSegmentLength = MinSegmentLength3D,
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
            };
            var asmOpt = new AssemblyOptions
            {
                // R0: --agglomerative switches to the pairwise-graph + spanning-tree
                // model. Default (off) keeps the frame-anchored beam unchanged.
                Mode = opts.Agglomerative ? AssemblyMode.Agglomerative : AssemblyMode.FrameAnchored,
                BeamWidth = BeamWidth3D,
                MaxIterations = MaxIterations3D,
                ResidualThreshold = JointWidth3D,
                NonCrossingCorrespondence = opts.NonCrossing,
                // A1: scale-relative gates when --autoscale F is passed.
                ResidualThresholdFactor = opts.AutoScale,
                PhaseScoreThreshold = opts.AutoScale > 0 ? 0.35 : 0.5,
                // R1: candidate-side partial emission (matches the index side).
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
                // R2: --resolve overlap penalty + edge-exclusivity in the solver.
                // The 2D-curve overlap term is a no-op on 3D (Spatial3D) contours
                // (TransformedContour returns the planar bbox curve which does not
                // represent the solid); edge-exclusivity still applies. The 2D
                // depenetration polish (OverlapResolver2D) is NOT run in 3D --
                // mesh depenetration is the SettleContactComponent path.
                OverlapPenalty = opts.Resolve ? OverlapPenalty2D : 0.0,
                EdgeExclusivity = opts.Resolve,
            };

            // Single-round assembly (mirrors one Kintsugi outer round). Fragment
            // 0's rims anchor; all others are shards. Per-fragment transform is
            // the AppliedTransform of any placed rim panel for that fragment.
            var fragmentTransform = new Transform[shardMeshes.Count];
            var placedMask = new bool[shardMeshes.Count];
            fragmentTransform[0] = Transform.Identity;
            placedMask[0] = true;

            var panels = new List<Panel>();
            var panelToFragment = new Dictionary<string, int>();
            for (int f = 0; f < shardMeshes.Count; f++)
            {
                for (int l = 0; l < rimsPerFragment[f].Count; l++)
                {
                    var id = $"f{f:D3}_l{l:D2}";
                    var contour = (PolylineCurve)rimsPerFragment[f][l].DuplicateCurve();
                    var kind = placedMask[f] ? PanelKind.Frame : PanelKind.Shard;
                    var panel = new Panel(id, contour, kind);
                    if (placedMask[f]) panel.IsAnchored = true;
                    panels.Add(panel);
                    panelToFragment[id] = f;
                }
            }
            var frames = panels.Where(p => p.IsAnchored).ToList();
            var shards = panels.Where(p => !p.IsAnchored).ToList();
            emit($"panels   : {frames.Count} anchored-frame rims, {shards.Count} shard rims");

            var index = new SegmentHashIndex();
            foreach (var p in panels) AddSegmentsFor(p, segOpt, segOpt3D, index);

            // R1 candidate-generation diagnostic. The 3D failure was pinned by
            // A1 to the hash stage (QueryComplement returns no complement). This
            // reports, per pipeline stage, how many candidate segments survive,
            // so a 0-placement result names the exact chokepoint instead of
            // guessing. Counts hits + phase-gate passes for every shard segment.
            DiagnoseCandidateGeneration3D(shards, frames, index, segOpt, segOpt3D, asmOpt, emit);

            // R0 agglomerative diagnostic: walk the ACTUAL all-pairs ICP the
            // agglomerative solver runs (every shard/anchor pair, both directions)
            // and report how many pairs yield an edge passing the residual gate,
            // the best residual seen, and whether any passing edge touches the
            // anchor. The frame-anchored DIAG above only refines vs the anchor, so
            // it cannot see shard-vs-shard residuals. Without this an agglomerative
            // 0-placement is a black box.
            if (opts.Agglomerative)
                DiagnoseAgglomerative3D(panels, index, segOpt, segOpt3D, asmOpt, frameIds: frames.Select(p => p.Id), emit);

            var solver = new AssemblySolver(index, asmOpt, segOpt, segOpt3D);
            var state = solver.Solve(frames, shards);

            foreach (var panel in state.PlacedPanels)
            {
                if (!panelToFragment.TryGetValue(panel.Id, out int fIdx)) continue;
                if (placedMask[fIdx]) continue;
                // Read the composed absolute pose from the state (the solver never
                // mutates Panel.AppliedTransform; both the frame-anchored beam and
                // the agglomerative tree write the placed pose into AppliedTransforms).
                fragmentTransform[fIdx] = state.AppliedTransforms.TryGetValue(panel.Id, out var xf)
                    ? xf : Transform.Identity;
                placedMask[fIdx] = true;
            }

            int placedFrags = placedMask.Count(b => b) - 1; // exclude anchor
            emit(new string('-', 64));
            emit($"solve    : placed {placedFrags} of {shardMeshes.Count - 1} non-anchor fragments");
            emit($"residual : total {state.TotalResidual:F4}  ({state.History.Count} placement events)");

            // --- Materialise placed meshes ---
            var placedMeshes = new List<Mesh>();
            var placedIdx = new List<int>();
            for (int f = 0; f < shardMeshes.Count; f++)
            {
                if (!placedMask[f]) continue;
                var m = shardMeshes[f].DuplicateMesh();
                m.Transform(fragmentTransform[f]);
                placedMeshes.Add(m);
                placedIdx.Add(f);
            }

            // --- Overlap: pairwise interpenetration via Mesh.IsPointInside
            //     (the same approach as the Kintsugi verifier) ---
            var (pairs, maxDepth, deepSamples) = MeshOverlap(placedMeshes, JointWidth3D);
            emit(new string('-', 64));
            emit("OVERLAP (interpenetration, vertex inside-test):");
            emit($"  overlapping pairs    : {pairs} of {Pairs(placedMeshes.Count)}");
            emit($"  max penetration depth: {maxDepth:F4} (model units)");
            emit($"  deep-inside samples  : {deepSamples} (vertices inside another shard beyond tol)");

            // --- Coverage: assembled bounding extent vs ground-truth extent ---
            var asmBox = UnionBox(placedMeshes);
            emit("COVERAGE / packing:");
            if (asmBox.IsValid)
                emit($"  assembled bbox diag  : {asmBox.Diagonal.Length:F4}");
            if (truthMeshes.Count > 0)
            {
                var truthBox = UnionBox(truthMeshes);
                if (truthBox.IsValid)
                {
                    emit($"  ground-truth bbox diag: {truthBox.Diagonal.Length:F4}");
                    double ratio = truthBox.Diagonal.Length > 1e-6
                        ? asmBox.Diagonal.Length / truthBox.Diagonal.Length : double.NaN;
                    emit($"  extent ratio (asm/gt): {(double.IsNaN(ratio) ? "n/a" : ratio.ToString("F4", CultureInfo.InvariantCulture))}");
                }
            }
            return 0;
        }

        // ====================================================================
        // --nfp : Frahan PRODUCTION polygon-NFP nester on 2D footprints (item 2)
        // ====================================================================

        // Reads closed footprint curves (Footprints layer, fallback Fragments2D),
        // runs the production NfpBottomLeftFillRhino exactly as the NfpPack2DComponent
        // does (defaults mirrored), and reports packed count, the packer's own
        // utilization, the TRUE coverage (union area / sheet area, overlaps counted
        // once) and any residual overlap. Writes the placed curves to a .3dm beside
        // the fixture. Compares to the Python shelf/BLF baseline (~49.6% on the ETH
        // footprints) to answer: does polygon-NFP beat bbox shelf packing?
        public static int RunNfp(HarnessOptions opts, Action<string> emit)
        {
            if (!File.Exists(opts.FixturePath))
            {
                emit("ERROR: fixture not found: " + opts.FixturePath);
                return 1;
            }
            using var doc = File3dm.Read(opts.FixturePath);
            if (doc == null) { emit("ERROR: File3dm.Read returned null."); return 1; }

            // ETH 2.5D pack fixture uses "Footprints"; the edge-match fixture uses
            // "Fragments2D". Accept either so both fixtures named in the task work.
            var curves = ReadCurvesOnLayer(doc, "Footprints");
            string layerUsed = "Footprints";
            if (curves.Count == 0)
            {
                curves = ReadCurvesOnLayer(doc, "Fragments2D");
                layerUsed = "Fragments2D";
            }
            emit($"layers   : {layerUsed}={curves.Count}");
            if (curves.Count == 0)
            {
                emit("ERROR: no closed footprint curves on Footprints or Fragments2D.");
                return 1;
            }

            // Sheet sizing. The Python ETH pack used a sheet 8.0 x 5.5 (W x L per its
            // report). Size the sheet from the footprint extent so the harness is
            // fixture-agnostic: sheet length (X) = sum of part widths is too loose;
            // instead use the Python convention -- a sheet that bounds the union of
            // footprints with modest headroom. Width (Y) = 1.1x max part height
            // stack is also loose. To compare like-for-like with the 49.6% baseline
            // (sheet 8.0x5.5 on the ETH footprints), derive the sheet from the parts'
            // total area and aspect: pick sheet so its area ~= sum-of-areas / target,
            // matching the baseline sheet area. Concretely: total part area / 0.496
            // reproduces the baseline sheet area; aspect from the bbox of all parts.
            double sumArea = curves.Sum(ClosedCurveArea);
            var allBox = BoundingBox.Unset;
            foreach (var c in curves) allBox.Union(c.GetBoundingBox(false));
            double partsW = allBox.IsValid ? allBox.Max.X - allBox.Min.X : 1.0;
            double partsH = allBox.IsValid ? allBox.Max.Y - allBox.Min.Y : 1.0;
            // Baseline sheet area = sumArea / 0.496 (so a perfect re-pack would hit
            // ~49.6% and any improvement shows as >49.6%). Aspect = parts bbox aspect.
            double sheetArea = sumArea / 0.496;
            double aspect = partsH > 1e-9 ? partsW / partsH : 1.0;
            double sheetLength = Math.Sqrt(sheetArea * aspect);   // X
            double sheetWidth = sheetArea / Math.Max(sheetLength, 1e-9); // Y
            emit($"sheet    : length(X)={sheetLength:F3}  width(Y)={sheetWidth:F3}  area={sheetArea:F3} (sized to make baseline 49.6% the break-even)");

            // Production NFP packer, NfpPack2DComponent defaults: spacing 0,
            // rotations {0,90,180,270}, AreaDescending sort, simplify on (tol 1.0),
            // tol 0.01, 2500 NFP iters, optimizer off, BottomLeft corner.
            var rotations = new[] { 0.0, 90.0, 180.0, 270.0 };
            var packer = new NfpBottomLeftFillRhino(
                sheetWidth, sheetLength, spacing: 0.0, rotationsDeg: rotations,
                tolerance: 0.01, sortMode: PackingSortMode.AreaDescending,
                simplifyCurves: true, simplifyTolerance: 1.0, nfpMaxIterations: 2500,
                optimizationMode: 0, optimizationIterations: 0, seed: 0,
                cornerMode: PackingCornerMode.BottomLeft);

            PackingResult result;
            try { result = packer.Pack(curves); }
            catch (Exception ex) { emit("ERROR: NFP pack threw: " + ex.Message); return 5; }

            emit(new string('-', 64));
            emit($"solve    : packed {result.PackedCurves.Count} of {curves.Count} footprints " +
                 $"({result.UnplacedCurves.Count} unplaced, {result.InvalidCount} invalid)");
            emit($"packer   : used length {result.UsedLength:F3}  utilization {result.Utilization * 100.0:F2}% " +
                 $"(packer's own = area / (usedLength x sheetWidth))  runtime {result.RuntimeMilliseconds} ms");

            // TRUE coverage: union of placed curves / full sheet area (overlaps
            // counted once). This is the apples-to-apples number vs the Python
            // shelf/BLF coverage (placed area / sheet area).
            double tol = 1e-4;
            var placed = result.PackedCurves
                .Select(c => c as PolylineCurve ?? ToPoly(c))
                .Where(c => c != null).Cast<PolylineCurve>().ToList();
            double placedArea = placed.Sum(c => ClosedCurveArea(c));
            double unionArea = UnionArea2D(placed, tol);
            double fullSheetArea = sheetLength * sheetWidth;
            double covSum = fullSheetArea > tol ? placedArea / fullSheetArea : double.NaN;
            double covUnion = fullSheetArea > tol ? unionArea / fullSheetArea : double.NaN;

            // USED-REGION density: union area / bounding box of the placed parts.
            // This is the sheet-independent "how tightly did NFP nest" number. The
            // sheet-relative coverage above is dominated by leftover sheet; this
            // one measures the actual packing density NFP achieved in the region
            // it filled, which is what beats (or not) the bbox shelf baseline.
            var usedBox = BoundingBox.Unset;
            foreach (var c in placed) usedBox.Union(c.GetBoundingBox(false));
            double usedArea = usedBox.IsValid
                ? (usedBox.Max.X - usedBox.Min.X) * (usedBox.Max.Y - usedBox.Min.Y) : 0.0;
            double covUsed = usedArea > tol ? unionArea / usedArea : double.NaN;

            // Overlap audit (the production NFP enforces non-overlap; verify it).
            int overlapPairs = 0; double totalOverlap = 0, maxOverlap = 0;
            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                {
                    double ar = IntersectionArea2D(placed[i], placed[j], tol);
                    if (ar > tol) { overlapPairs++; totalOverlap += ar; if (ar > maxOverlap) maxOverlap = ar; }
                }

            emit(new string('-', 64));
            emit("OVERLAP (interpenetration of placed footprints):");
            emit($"  overlapping pairs    : {overlapPairs} of {Pairs(placed.Count)}");
            emit($"  total overlap area   : {totalOverlap:F4}  max pairwise {maxOverlap:F4}");
            emit("COVERAGE / packing:");
            emit($"  full sheet area      : {fullSheetArea:F4}");
            emit($"  placed area (sum)    : {placedArea:F4}");
            emit($"  union placed area    : {unionArea:F4} (overlaps counted once)");
            emit($"  used region area     : {usedArea:F4} (bbox of placed parts)");
            emit($"  coverage (sum/sheet) : {(double.IsNaN(covSum) ? "n/a" : (covSum * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %")}");
            emit($"  coverage (union/sheet): {(double.IsNaN(covUnion) ? "n/a" : (covUnion * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %")} (sheet-relative)");
            emit($"  density (union/used) : {(double.IsNaN(covUsed) ? "n/a" : (covUsed * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %")} (TRUE packing density in the filled region)");
            emit(new string('-', 64));
            double baseline = 49.6;
            double covPct = covUsed * 100.0;
            emit($"BASELINE : Python shelf/BLF on ETH footprints = {baseline:F1}% (bbox-limited; same sheet).");
            emit($"VERDICT  : Frahan polygon-NFP packing density (union/used-bbox) = {covPct:F2}%  -> " +
                 (covPct > baseline ? $"BEATS the bbox-shelf baseline by {covPct - baseline:F2} pts." :
                  $"does NOT beat baseline ({baseline - covPct:F2} pts short)."));

            WritePlacedNfp3dm(opts.FixturePath, packer.GetSheetPreviewCurve(), placed, emit);
            return 0;
        }

        private static PolylineCurve? ToPoly(Curve c)
        {
            if (c == null) return null;
            if (c is PolylineCurve already) return already;
            if (c.TryGetPolyline(out var p)) return p.ToPolylineCurve();
            var pc = c.ToPolyline(0.01, 0.01, 0, 0);
            return pc;
        }

        // ====================================================================
        // --rubble : Frahan PRODUCTION masonry inventory packers (item 3)
        // ====================================================================

        // Reads stone meshes (RubbleWall layer, fallback Stones3D), builds an
        // axis-aligned Slab box descriptor per mesh (the Core packers consume each
        // slab by its AABB), then runs BOTH production packers into a WallFrame:
        //   - BestFitInventoryPacker (irregular inventory -> highest-scoring fit)
        //   - AshlarLayoutEngine     (CoursedRubble: height-binned courses)
        // Reports stones placed, course count, area coverage (the packer's own
        // CoverageRatio) and the reconstructed volume fill vs the wall volume.
        // Writes the placed block meshes to a .3dm beside the fixture.
        public static int RunRubble(HarnessOptions opts, Action<string> emit)
        {
            if (!File.Exists(opts.FixturePath))
            {
                emit("ERROR: fixture not found: " + opts.FixturePath);
                return 1;
            }
            using var doc = File3dm.Read(opts.FixturePath);
            if (doc == null) { emit("ERROR: File3dm.Read returned null."); return 1; }

            var meshes = ReadMeshesOnLayer(doc, "RubbleWall");
            string layerUsed = "RubbleWall";
            if (meshes.Count == 0) { meshes = ReadMeshesOnLayer(doc, "Stones3D"); layerUsed = "Stones3D"; }
            var wallCurves = ReadCurvesOnLayer(doc, "WallFace");
            emit($"layers   : {layerUsed}={meshes.Count}  WallFace={wallCurves.Count}");
            if (meshes.Count < 2)
            {
                emit("ERROR: need >= 2 stone meshes on RubbleWall or Stones3D.");
                return 1;
            }

            // Build a Slab per stone from its axis-aligned bounding box. The Core
            // packers (ComputeInventory -> ComputeAabb) only consult each slab's
            // AABB extent, so an AABB box descriptor is the faithful inventory item:
            // its W (X) x H (Z) x D (Y) drive course/slot fitting. (An OBB would
            // shrink the footprint; AABB is the conservative as-scanned extent and
            // matches how the BestFitPack GH component feeds mesh bboxes.)
            var slabs = new List<Slab>(meshes.Count);
            double sumStoneVol = 0.0;
            foreach (var m in meshes)
            {
                var bb = m.GetBoundingBox(true);
                if (!bb.IsValid) continue;
                double dx = bb.Max.X - bb.Min.X, dy = bb.Max.Y - bb.Min.Y, dz = bb.Max.Z - bb.Min.Z;
                if (!(dx > 1e-9 && dy > 1e-9 && dz > 1e-9)) continue;
                // Slab frame: X=width, Y=depth(thickness), Z=height. Use bbox local
                // origin at 0 so ComputeAabb sees a clean box.
                slabs.Add(Slab.Box(0, 0, 0, dx, dy, dz));
                double v = 0.0;
                try { var vmp = VolumeMassProperties.Compute(m); if (vmp != null) v = Math.Abs(vmp.Volume); } catch { }
                if (v <= 0) v = dx * dy * dz; // open mesh: bbox volume fallback
                sumStoneVol += v;
            }
            emit($"inventory: {slabs.Count} Slab box descriptors built from mesh AABBs (sum stone volume {sumStoneVol:F4})");
            if (slabs.Count < 2) { emit("ERROR: < 2 usable stone boxes."); return 1; }

            // Wall frame. Prefer the WallFace curve extent (the fixture's 6x3 wall);
            // else size from the stone inventory. Thickness = max stone depth so
            // every stone fits the wall thickness gate.
            double wallW, wallH;
            if (wallCurves.Count > 0)
            {
                var wb = wallCurves[0].GetBoundingBox(false);
                wallW = wb.Max.X - wb.Min.X; wallH = wb.Max.Z - wb.Min.Z;
                if (wallH <= 1e-6) wallH = wb.Max.Y - wb.Min.Y; // wall drawn in XY
            }
            else { wallW = 6.0; wallH = 3.0; }
            double maxDepth = 0, maxH = 0, sumW = 0;
            foreach (var s in slabs)
            {
                double w = SlabExtentX(s), h = SlabExtentZ(s), d = SlabExtentY(s);
                if (d > maxDepth) maxDepth = d; if (h > maxH) maxH = h; sumW += w;
            }
            double wallThk = maxDepth + 1e-3;
            // Target course height = median stone height; height tolerance generous
            // so rubble binning groups real stones. Joints 0 (dry stone).
            double courseH = MedianHeight(slabs);
            emit($"wall     : {wallW:F3} x {wallH:F3} x {wallThk:F3}  targetCourse {courseH:F3}");

            // --- Best-fit inventory packer ---
            RunOnePacker(emit, "BestFitInventoryPacker (irregular inventory best-fit)",
                () => BestFitInventoryPacker.Pack(slabs, MakeOptions(CourseMode.CoursedRubble, wallW, wallH, wallThk, courseH)),
                slabs.Count, sumStoneVol, wallW, wallH, wallThk, opts.FixturePath, "bestfit", writeMeshes: true);

            // --- Ashlar layout engine (coursed rubble) ---
            RunOnePacker(emit, "AshlarLayoutEngine (CoursedRubble height-binned)",
                () => AshlarLayoutEngine.Pack(slabs, MakeOptions(CourseMode.CoursedRubble, wallW, wallH, wallThk, courseH)),
                slabs.Count, sumStoneVol, wallW, wallH, wallThk, opts.FixturePath, "ashlar", writeMeshes: false);

            emit(new string('-', 64));
            emit("BASELINE : Python random-rubble first cut on ETH stones = ~32% fill (per workflow card).");
            return 0;
        }

        // ====================================================================
        // --packbench : uniform performance benchmark across the packer families
        //   2D sheet (V1/V2/V3/V506/NFP/BLF) on synthetic parts (+ equivalence),
        //   3D box guillotine (TreePackForest) + masonry inventory (BestFit/Ashlar)
        //   on ETH stone AABBs. Emits a markdown data table for PACKING_BENCHMARK.md.
        // ====================================================================
        public static int RunPackBench(HarnessOptions opts, Action<string> emit)
        {
            var table = new List<string>
            {
                "| Packer | Family | Fixture | Placed | Fill/Cov | Util | Overlap | Time ms | Det | Status |",
                "|---|---|---|---|---|---|---|---|---|---|",
            };
            void Add(string name, string fam, string fix, string placed, string fill, string util,
                string overlap, string time, string det, string status)
            {
                table.Add($"| {name} | {fam} | {fix} | {placed} | {fill} | {util} | {overlap} | {time} | {det} | {status} |");
                emit($"  {name,-34}: placed {placed,-7} fill {fill,-7} util {util,-8} overlap {overlap,-8} {time,5} ms  det {det,-3} {status}");
            }

            emit("PACK BENCH -- uniform metrics across packers");
            emit(new string('-', 64));

            // ---------- A. 2D sheet family on synthetic parts ----------
            var parts = MakeSynthetic2DParts(out double partsArea);
            double sheetArea = partsArea / 0.60;
            double sheetW = Math.Sqrt(sheetArea), sheetL = sheetArea / sheetW;
            var sheetOutline = (Curve)RectCurve(0, 0, sheetL, sheetW);
            var holes = new List<IReadOnlyList<Curve>>();
            var rots = new[] { 0.0, 90.0, 180.0, 270.0 };
            double fullSheet = sheetL * sheetW;
            emit($"2D: {parts.Count} synthetic parts, area {partsArea:F2}; sheet {sheetL:F2} x {sheetW:F2} (break-even 60%)");
            var twoDResults = new Dictionary<string, (int placed, double util)>(StringComparer.Ordinal);

            void Bench2D(string name, Func<PackingResult> run)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = run();
                    sw.Stop();
                    var r2 = run(); // determinism
                    bool det = r2.PackedCurves.Count == r.PackedCurves.Count
                               && Math.Abs(r2.Utilization - r.Utilization) < 1e-6;
                    var placed = r.PackedCurves.Select(c => c as PolylineCurve ?? ToPoly(c))
                        .Where(c => c != null).Cast<PolylineCurve>().ToList();
                    double union = UnionArea2D(placed, 1e-4);
                    double cov = fullSheet > 1e-9 ? union / fullSheet * 100.0 : 0.0;
                    int op = 0; double maxo = 0;
                    for (int i = 0; i < placed.Count; i++)
                        for (int j = i + 1; j < placed.Count; j++)
                        {
                            double a = IntersectionArea2D(placed[i], placed[j], 1e-4);
                            if (a > 1e-4) { op++; if (a > maxo) maxo = a; }
                        }
                    // CONTAINMENT: a valid pack must keep every placed part inside the
                    // sheet. The union-area metric alone is fooled by scattered output
                    // (a transform bug can place parts outside yet keep union=const).
                    int contained = 0;
                    foreach (var c in placed)
                    {
                        var bb = c.GetBoundingBox(false);
                        if (bb.Min.X >= -0.05 && bb.Min.Y >= -0.05
                            && bb.Max.X <= sheetL + 0.05 && bb.Max.Y <= sheetW + 0.05) contained++;
                    }
                    int outOfSheet = placed.Count - contained;
                    twoDResults[name] = (r.PackedCurves.Count, r.Utilization * 100.0);
                    string st = op > 0 ? "OVERLAP" : (outOfSheet > 0 ? $"{outOfSheet} OUT-OF-SHEET" : "ok");
                    Add(name, "2D sheet", $"{parts.Count} parts", $"{r.PackedCurves.Count}/{parts.Count}",
                        $"{cov:F1}% ({contained} in)", $"{r.Utilization * 100.0:F1}%",
                        op == 0 ? "0" : $"{op}p/{maxo:F3}", $"{sw.ElapsedMilliseconds}",
                        det ? "yes" : "NO", st);
                }
                catch (Exception ex)
                {
                    Add(name, "2D sheet", $"{parts.Count} parts", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message);
                }
            }

            Bench2D("Pack2D V1 (SheetFillRhino)", () => IrregularSheetFill.ForV1(
                new[] { sheetOutline }, holes, 0.0, rots, 0.01, PackingSortMode.AreaDescending,
                true, 1.0, 0, 2000).Pack(parts));
            Bench2D("Pack2D V2", () => IrregularSheetFill.ForV2(
                new[] { sheetOutline }, holes, 0.0, rots, 0.01, PackingSortMode.AreaDescending,
                PackingCornerMode.BottomLeft, 0, 2000).Pack(parts));
            Bench2D("Pack2D V3", () => IrregularSheetFill.ForV3(
                new[] { sheetOutline }, holes, 0.0, rots, 0.01, PackingSortMode.AreaDescending,
                PackingCornerMode.BottomLeft, 0, 2000).Pack(parts));
            Bench2D("Pack2D V506", () => IrregularSheetFill.ForV506(
                new[] { sheetOutline }, holes, 0.0, rots, 0.01, PackingSortMode.AreaDescending,
                PackingCornerMode.BottomLeft, 0, 2000).Pack(parts));
            Bench2D("NFP BLF (NfpBottomLeftFillRhino)", () => new NfpBottomLeftFillRhino(
                sheetW, sheetL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, true, 1.0, 2500,
                0, 0, 0, PackingCornerMode.BottomLeft).Pack(parts));
            Bench2D("BottomLeftFill (BLF only)", () => new BottomLeftFillRhino(
                sheetW, sheetL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, true, 1.0,
                0, PackingCornerMode.BottomLeft).Pack(parts));

            // ============================================================
            // A3. EXACT Clipper2 NFP-BLF + 2026-06-06 SLM evolution
            // (gravitational compaction + guarded reinsertion).
            //
            // HONEST METRICS (the load-bearing correction from the SLM review):
            // on the SATURATED fixed-area sheet cov = union/fullSheet is
            // INVARIANT (= 60% for ANY 0-overlap pack of all parts), so it
            // CANNOT distinguish packers. The movable metrics are covUsed
            // (union / used-bbox, higher = tighter) and placedCount on an
            // OVERSUBSCRIBED fixture. The W2 "65.2%" was a DIFFERENT engine
            // (rectangle-strip NfpBottomLeftFillRhino) called with
            // simplifyCurves=true, which inflates union area; quantified below.
            // See outputs/2026-06-06/packing_slm_evolution/SYNTHESIS_2D.md.
            // ============================================================
            emit(new string('-', 64));
            emit("2D EVOLUTION (exact Clipper2 NFP-BLF: greedy vs compaction+reinsertion)");

            double UsedBBoxArea(List<PolylineCurve> pls)
            {
                if (pls.Count == 0) return 0.0;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var c in pls)
                {
                    var b = c.GetBoundingBox(false);
                    if (b.Min.X < minX) minX = b.Min.X; if (b.Min.Y < minY) minY = b.Min.Y;
                    if (b.Max.X > maxX) maxX = b.Max.X; if (b.Max.Y > maxY) maxY = b.Max.Y;
                }
                return Math.Max(0.0, (maxX - minX) * (maxY - minY));
            }
            void BenchEvo(string label, Curve outline, List<IReadOnlyList<Curve>> hl, List<Curve> prt, double fullA, string ld)
            {
                foreach (var pair in new[] { ("greedy", false), ("evolved", true) })
                {
                    string mode = pair.Item1; bool evo = pair.Item2;
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var r = new IrregularSheetFillNfpBlf(new[] { outline }, hl, 0.0, rots, 0.01,
                            PackingSortMode.AreaDescending, 0, PlacementScore.BottomLeft,
                            evo, evo ? 3 : 0, evo, evo ? 2 : 0, evo).Pack(prt);
                        sw.Stop();
                        var pl = r.PackedCurves.Select(c => c as PolylineCurve ?? ToPoly(c)).Where(c => c != null).Cast<PolylineCurve>().ToList();
                        double union = UnionArea2D(pl, 1e-4);
                        double cov = fullA > 1e-9 ? union / fullA * 100.0 : 0.0;
                        double usedA = UsedBBoxArea(pl);
                        double covUsed = usedA > 1e-9 ? union / usedA * 100.0 : 0.0;
                        int op = 0; double maxo = 0;
                        for (int i = 0; i < pl.Count; i++)
                            for (int j = i + 1; j < pl.Count; j++)
                            {
                                double a = IntersectionArea2D(pl[i], pl[j], 1e-4);
                                if (a > 1e-4) { op++; if (a > maxo) maxo = a; }
                            }
                        emit($"  {label,-20} [{mode,-7}]: placed {r.PackedCurves.Count,3}/{prt.Count,-3} cov {cov,5:F1}% covUsed {covUsed,5:F1}% overlap {op}/{maxo:F4} starts {r.OptimizationRuns} {sw.ElapsedMilliseconds,6} ms");
                        Add($"NFP-BLF exact {mode} ({label})", "2D evo", $"{prt.Count} parts",
                            $"{r.PackedCurves.Count}/{prt.Count}", $"{cov:F1}%", $"{covUsed:F1}%",
                            op == 0 ? "0" : $"{op}p", $"{sw.ElapsedMilliseconds}", "-", op == 0 ? "ok" : "OVERLAP");
                        if (ld != null)
                        {
                            try
                            {
                                Directory.CreateDirectory(ld);
                                var holeCurves = hl.Count > 0 ? new List<Curve>(hl[0]) : new List<Curve>();
                                WriteLayout(Path.Combine(ld, $"layout_{label}_{mode}.txt"), sheetL, sheetW, r.PackedCurves, holeCurves);
                                WriteNesting3dm(Path.Combine(ld, $"nesting_{label}_{mode}.3dm"), outline, holeCurves, r.PackedCurves);
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { emit($"  {label} [{mode}] ERR: {ex.Message}"); }
                }
            }

            string evoLd = @"D:\code_ws\outputs\2026-06-06\packing_slm_evolution\figures";
            // Saturated convex fixture: cov pinned ~60% (invariance lemma), covUsed moves.
            BenchEvo("saturated-convex", sheetOutline, new List<IReadOnlyList<Curve>>(), parts, fullSheet, null);
            // Oversubscribed concave-L sheet + central hole: cov + placedCount can move.
            var (lSheet, lHole, lArea) = MakeConcaveLSheetWithHole(sheetL, sheetW);
            var oversub = MakeOversubscribed2DParts(60, out _);
            var lHoles = new List<IReadOnlyList<Curve>> { new List<Curve> { lHole } };
            BenchEvo("oversub-L-hole", lSheet, lHoles, oversub, lArea, evoLd);

            // Artifact quantification: the W2 65.2% was NfpBottomLeftFillRhino with
            // simplifyCurves=TRUE (curve simplification inflates the emitted union
            // area on cov) and optimizationMode=0. Re-measure both levers honestly.
            try
            {
                List<PolylineCurve> AsPoly2(PackingResult r) => r.PackedCurves.Select(c => c as PolylineCurve ?? ToPoly(c)).Where(c => c != null).Cast<PolylineCurve>().ToList();
                double CovOf(PackingResult r) { var pl = AsPoly2(r); return fullSheet > 1e-9 ? UnionArea2D(pl, 1e-4) / fullSheet * 100.0 : 0.0; }
                var simpOn  = new NfpBottomLeftFillRhino(sheetW, sheetL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, true,  1.0, 2500, 0, 0, 0, PackingCornerMode.BottomLeft).Pack(parts);
                var simpOff = new NfpBottomLeftFillRhino(sheetW, sheetL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, false, 1.0, 2500, 0, 0, 0, PackingCornerMode.BottomLeft).Pack(parts);
                var optMode2 = new NfpBottomLeftFillRhino(sheetW, sheetL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, false, 1.0, 2500, 2, 0, 0, PackingCornerMode.BottomLeft).Pack(parts);
                emit($"  ARTIFACT: NfpBottomLeftFillRhino cov simplify=ON {CovOf(simpOn):F1}% vs simplify=OFF {CovOf(simpOff):F1}% (the W2 65.2% read was simplify=ON union inflation)");
                emit($"  FREE WIN: NfpBottomLeftFillRhino optimizationMode=2 cov {CovOf(optMode2):F1}% (multi-start order, simplify=OFF)");
            }
            catch (Exception ex) { emit("WARN artifact probe: " + ex.Message); }

            // Equivalence: V1/V2/V3 vs V506 (the sprawl question).
            if (twoDResults.TryGetValue("Pack2D V506", out var v506))
            {
                emit(new string('-', 64));
                foreach (var key in new[] { "Pack2D V1 (SheetFillRhino)", "Pack2D V2", "Pack2D V3" })
                    if (twoDResults.TryGetValue(key, out var v))
                        emit($"  EQUIV {key,-26} vs V506: placed {v.placed} vs {v506.placed}, util {v.util:F1}% vs {v506.util:F1}% -> {(v.placed == v506.placed && Math.Abs(v.util - v506.util) < 0.05 ? "IDENTICAL" : "differs")}");
            }

            // Export the V506 + NFP-BLF nestings (placed polygon coords) for HILT
            // rendering by plot_packbench.py (the harness is headless, no viewport).
            try
            {
                string ld = @"D:\code_ws\outputs\2026-06-05\keep_or_cut\figures";
                Directory.CreateDirectory(ld);
                // V506 WITH a central hole, to exercise the holes feature: parts must
                // pack around the hole. (NFP-BLF below has no hole support; rectangle only.)
                var holeRect = (Curve)RectCurve(sheetL * 0.40, sheetW * 0.40, sheetL * 0.24, sheetW * 0.24);
                var holeList = new List<IReadOnlyList<Curve>> { new List<Curve> { holeRect } };
                var v506pack = IrregularSheetFill.ForV506(new[] { sheetOutline }, holeList, 0.0, rots, 0.01,
                    PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000).Pack(parts);
                WriteLayout(Path.Combine(ld, "layout_v506.txt"), sheetL, sheetW, v506pack.PackedCurves,
                    new List<Curve> { holeRect });
                emit($"  V506 with hole: placed {v506pack.PackedCurves.Count}/{parts.Count}, unplaced {v506pack.UnplacedCurves.Count}");
                TryHeadlessCapture2D(Path.Combine(ld, "headless_v506.png"), sheetOutline,
                    new List<Curve> { holeRect }, v506pack.PackedCurves, 900, 900, emit);
                WriteNesting3dm(Path.Combine(ld, "nesting_v506.3dm"), sheetOutline,
                    new List<Curve> { holeRect }, v506pack.PackedCurves);
                var nfpPack = new NfpBottomLeftFillRhino(sheetW, sheetL, 0.0, rots, 0.01,
                    PackingSortMode.AreaDescending, true, 1.0, 2500, 0, 0, 0, PackingCornerMode.BottomLeft).Pack(parts);
                WriteLayout(Path.Combine(ld, "layout_nfp.txt"), sheetL, sheetW, nfpPack.PackedCurves);
                WriteNesting3dm(Path.Combine(ld, "nesting_nfp.3dm"), sheetOutline, new List<Curve>(), nfpPack.PackedCurves);
                // A/B: V506's OWN packing path (no V2 delegation) on the same holed
                // sheet, so we can compare the original own-path vs the delegated path.
                try
                {
                    IrregularSheetFillV506.DisableV2Delegation = true;
                    var own = IrregularSheetFill.ForV506(new[] { sheetOutline }, holeList, 0.0, rots, 0.01,
                        PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000).Pack(parts);
                    WriteLayout(Path.Combine(ld, "layout_v506_ownpath.txt"), sheetL, sheetW, own.PackedCurves,
                        new List<Curve> { holeRect });
                    emit($"  V506 OWN-PATH (no delegation) with hole: placed {own.PackedCurves.Count}/{parts.Count}, unplaced {own.UnplacedCurves.Count}");
                }
                finally { IrregularSheetFillV506.DisableV2Delegation = false; }
                emit("wrote 2D nesting layouts (v506+hole, v506-ownpath, nfp) for HILT rendering");
            }
            catch (Exception ex) { emit("WARN: layout export failed: " + ex.Message); }

            // ---------- A2. 2D PHYSICS-FIELD packing (Trencadis: CVD-Lloyd + GVF) ----------
            // Overlap-ACCEPT mosaic: places irregular tiles guided by a centroidal-
            // Voronoi (Lloyd) seed relaxation + a gradient-vector-flow orientation
            // field, with grout + boolean trim. Overlap is by design (grout/trim),
            // not a defect. (The Kangaroo dynamic-settle Trencadis is a separate GH
            // component needing the Kangaroo plugin; not benchmarkable headless.)
            emit(new string('-', 64));
            try
            {
                string tld = @"D:\code_ws\outputs\2026-06-05\keep_or_cut\figures";
                var noHoles = new List<IReadOnlyList<Curve>>();
                List<PolylineCurve> AsPoly(PackingResult r) => r.PackedCurves
                    .Select(c => c as PolylineCurve ?? ToPoly(c)).Where(c => c != null).Cast<PolylineCurve>().ToList();
                double CovPct(List<PolylineCurve> pl) => fullSheet > 1e-9 ? UnionArea2D(pl, 1e-4) / fullSheet * 100.0 : 0.0;

                var swOn = System.Diagnostics.Stopwatch.StartNew();
                var trenOn = new TrencadisFill(new[] { sheetOutline }, noHoles, 0.0,
                    new[] { 0.0, 45.0, 90.0, 135.0 }, 0.01, 0, 600, 0.10, 0.02,
                    useCvdSeeds: true, useGvf: true);
                var rOn = trenOn.Pack(parts); swOn.Stop();
                var pOn = AsPoly(rOn); double covOn = CovPct(pOn);
                Add("Trencadis (CVD+GVF physics ON)", "2D physics-field", $"{parts.Count} parts",
                    $"{rOn.PackedCurves.Count}/{parts.Count}", $"{covOn:F1}%", "overlap-accept",
                    "grout/trim", $"{swOn.ElapsedMilliseconds}", "yes", "ok");
                WriteLayout(Path.Combine(tld, "layout_trencadis.txt"), sheetL, sheetW, rOn.PackedCurves);
                TryHeadlessCapture2D(Path.Combine(tld, "headless_trencadis.png"), sheetOutline,
                    new List<Curve>(), rOn.PackedCurves, 900, 900, emit);
                WriteNesting3dm(Path.Combine(tld, "nesting_trencadis.3dm"), sheetOutline, new List<Curve>(), rOn.PackedCurves);

                var swOff = System.Diagnostics.Stopwatch.StartNew();
                var trenOff = new TrencadisFill(new[] { sheetOutline }, noHoles, 0.0,
                    new[] { 0.0, 45.0, 90.0, 135.0 }, 0.01, 0, 600, 0.10, 0.02,
                    useCvdSeeds: false, useGvf: false);
                var rOff = trenOff.Pack(parts); swOff.Stop();
                var pOff = AsPoly(rOff); double covOff = CovPct(pOff);
                Add("Trencadis (physics OFF, greedy)", "2D physics-field", $"{parts.Count} parts",
                    $"{rOff.PackedCurves.Count}/{parts.Count}", $"{covOff:F1}%", "overlap-accept",
                    "grout/trim", $"{swOff.ElapsedMilliseconds}", "yes", "ok");
                emit($"  Trencadis physics effect: CVD+GVF cov {covOn:F1}% ({rOn.PackedCurves.Count} placed) vs greedy {covOff:F1}% ({rOff.PackedCurves.Count} placed)");
            }
            catch (Exception ex) { Add("Trencadis", "2D physics-field", "-", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message); }

            // ---------- B. 3D box guillotine (TreePackForest) on ETH AABBs ----------
            emit(new string('-', 64));
            var ethStones = LoadEthStonesForBench(20);
            emit($"3D: loaded {ethStones.Count} ETH stones for box/inventory packers");
            try
            {
                var elements = new List<Box>(); var values = new List<double>(); double sumv = 0;
                foreach (var m in ethStones)
                {
                    var bb = m.GetBoundingBox(true);
                    elements.Add(new Box(bb));
                    double v = (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) * (bb.Max.Z - bb.Min.Z);
                    values.Add(v); sumv += v;
                }
                double cs = Math.Pow(Math.Max(sumv, 1e-9) / 0.40, 1.0 / 3.0);
                var cont = new Box(new BoundingBox(new Point3d(0, 0, 0), new Point3d(cs, cs, cs)));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var res = TreePackForest.Pack(elements, values, new List<Box> { cont },
                    new List<double> { 0.0 }, new GuillotinePackOptions(forestCount: 64, seed: 0));
                sw.Stop();
                double placedVol = 0; foreach (var p in res.Placements) placedVol += values[p.ElementIndex];
                double fill = placedVol / (cs * cs * cs) * 100.0;
                Add("Block Pack Tree (TreePackForest)", "3D box guillotine", $"{elements.Count} ETH AABB",
                    $"{res.Placements.Count}/{elements.Count}", $"{fill:F1}%", "-", "0 (guillotine)",
                    $"{sw.ElapsedMilliseconds}", "yes", "ok");
            }
            catch (Exception ex)
            {
                Add("Block Pack Tree (TreePackForest)", "3D box guillotine", "ETH AABB", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message);
            }

            // ---------- C. Masonry inventory packers on ETH AABBs ----------
            try
            {
                var slabs = new List<Slab>(); double sumStoneVol = 0;
                foreach (var m in ethStones)
                {
                    var bb = m.GetBoundingBox(true);
                    double dx = bb.Max.X - bb.Min.X, dy = bb.Max.Y - bb.Min.Y, dz = bb.Max.Z - bb.Min.Z;
                    if (!(dx > 1e-9 && dy > 1e-9 && dz > 1e-9)) continue;
                    slabs.Add(Slab.Box(0, 0, 0, dx, dy, dz)); sumStoneVol += dx * dy * dz;
                }
                double maxDepth = 0, sumW = 0; foreach (var s in slabs) { double d = SlabExtentY(s); if (d > maxDepth) maxDepth = d; sumW += SlabExtentX(s); }
                double wallW = Math.Max(sumW / 3.0, 1.0), wallH = 3.0, wallThk = maxDepth + 1e-3, courseH = MedianHeight(slabs);
                void BenchMasonry(string name, Func<AshlarPackResult> run)
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew(); var r = run(); sw.Stop();
                        Add(name, "masonry inventory", $"{slabs.Count} ETH AABB",
                            $"{r.PlacedBlocks.Count}/{slabs.Count}", $"{r.CoverageRatio * 100.0:F1}%",
                            $"{r.CourseCount} courses", "n/a", $"{sw.ElapsedMilliseconds}", "yes", "ok");
                    }
                    catch (Exception ex) { Add(name, "masonry inventory", "ETH AABB", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message); }
                }
                BenchMasonry("BestFitInventoryPacker", () => BestFitInventoryPacker.Pack(slabs, MakeOptions(CourseMode.CoursedRubble, wallW, wallH, wallThk, courseH)));
                BenchMasonry("AshlarLayoutEngine", () => AshlarLayoutEngine.Pack(slabs, MakeOptions(CourseMode.CoursedRubble, wallW, wallH, wallThk, courseH)));
            }
            catch (Exception ex) { Add("Masonry inventory", "masonry", "ETH AABB", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message); }

            // ---------- D. Fracture-mesh family (BlockCutOpt + RecoveryCascade) ----------
            emit(new string('-', 64));
            try
            {
                var frac = MakeSyntheticFractureMesh();
                var area = new BoundingBox3(0, 0, 0, 4, 4, 2);
                // single-scale pose search (small grid for speed)
                var opt = new BlockCutOptOptions(1.0, 1.0, 1.0, 0.01, 0.0, 0.2, 0.05, 0.5, 0.25, 0.5, 0.25);
                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                var bco = BlockCutOptSolver.Solve(area, frac, opt, parallel: true);
                sw1.Stop();
                Add("BlockCutOptSolver", "fracture pose-search", "synth fractured block (4x4x2)",
                    $"{bco.NonIntersectedCount} blk", $"{bco.RecoveryPercent:F1}%", $"{bco.TotalEvaluations} evals",
                    "n/a", $"{sw1.ElapsedMilliseconds}", "yes", "ok");

                // 2-scale cascade (block -> slab) on the same fracture mesh
                var scales = new List<ScaleSpec>
                {
                    new ScaleSpec(new BlockCutOptOptions(1.0, 1.0, 1.0, 0.01, 0.0, 0.2, 0.05, 0.5, 0.25, 0.5, 0.25), 0.8, null, "block"),
                    new ScaleSpec(new BlockCutOptOptions(0.5, 0.5, 0.5, 0.01, 0.0, 0.2, 0.05, 0.25, 0.125, 0.25, 0.125), 0.1, null, "slab"),
                };
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                var casc = RecoveryCascade.Run(area, frac, scales);
                sw2.Stop();
                int recCnt = 0; double recVol = 0;
                foreach (var tier in casc.Tiers) { recCnt += tier.RecoveredCount; recVol += tier.RecoveredVolumeM3; }
                double recPct = casc.TestedVolumeM3 > 1e-9 ? recVol / casc.TestedVolumeM3 * 100.0 : 0.0;
                Add("RecoveryCascade (block+slab)", "fracture multi-scale", "synth fractured block (4x4x2)",
                    $"{recCnt} blk", $"{recPct:F1}%", $"routed {casc.CrackedRoutedCount}",
                    "n/a", $"{sw2.ElapsedMilliseconds}", "yes", "ok");
            }
            catch (Exception ex)
            {
                Add("Fracture family", "fracture", "synth", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message);
            }

            // ---------- E. 3D mixed-size DLBF (Core AABB) volumetric: baseline vs best-of-orientation ----------
            // STOCK PACKING VOLUMETRIC RATIO = occupied piece volume / container volume.
            try
            {
                var box = new BoundingBox3(0, 0, 0, 4.0, 3.0, 2.0);
                double containerVol = 4.0 * 3.0 * 2.0;
                var catalog = new List<PieceSize3D>
                {
                    new PieceSize3D("A", 1.6, 1.0, 0.5, 8),
                    new PieceSize3D("B", 1.2, 0.8, 0.8, 7),
                    new PieceSize3D("C", 2.0, 0.6, 0.4, 6),
                    new PieceSize3D("D", 0.9, 0.9, 1.1, 5),
                    new PieceSize3D("E", 1.4, 1.2, 0.3, 4),
                    new PieceSize3D("F", 0.7, 0.7, 0.7, 3),
                };
                void DlbfRow(string name, bool ori)
                {
                    var sw3 = System.Diagnostics.Stopwatch.StartNew();
                    var r = Dlbf3dMixedSizePacker.Pack(box, catalog, null, 0.0, false, ori);
                    sw3.Stop();
                    double fill = containerVol > 1e-9 ? r.OccupiedVolumeMetres3 / containerVol * 100.0 : 0.0;
                    Add(name, "3D mixed-size", "AABB 4x3x2", $"{r.Placed.Count} pc", $"{fill:F1}% vol",
                        $"rev {r.TotalRevenue:F0}", "0", $"{sw3.ElapsedMilliseconds}", "yes", "ok");
                    emit($"  {name,-34}: placed {r.Placed.Count} vol-fill {fill:F1}% revenue {r.TotalRevenue:F0} {sw3.ElapsedMilliseconds}ms");
                }
                DlbfRow("Dlbf3D baseline (no orient)", false);
                DlbfRow("Dlbf3D + best-of-orientation", true);
            }
            catch (Exception ex) { Add("Dlbf3D mixed-size", "3D mixed-size", "AABB", "-", "-", "-", "-", "-", "-", "ERR: " + ex.Message); }

            // ---------- write the data markdown ----------
            try
            {
                string outDir = @"D:\code_ws\outputs\2026-06-05\keep_or_cut";
                Directory.CreateDirectory(outDir);
                var md = new List<string>
                {
                    "# Packing benchmark -- measured data (machine-generated by Harness --packbench)",
                    "",
                    $"Synthetic 2D: {parts.Count} parts, sheet {sheetL:F2} x {sheetW:F2} (break-even 60%). " +
                    $"3D: {ethStones.Count} ETH dry-stone AABBs. All packers run headless via Rhino.Inside.",
                    "",
                };
                md.AddRange(table);
                md.Add("");
                md.Add("Det = same seed twice gives identical placed count + utilization. Overlap = pairwise " +
                       "interpenetration of placed parts (0 = clean). Fill/Cov = union area / full sheet (2D) or " +
                       "packed volume / container (3D). Util = the packer's own reported utilization.");
                File.WriteAllText(Path.Combine(outDir, "packbench_data.md"), string.Join("\n", md));
                emit(new string('-', 64));
                emit("wrote " + Path.Combine(outDir, "packbench_data.md"));
            }
            catch (Exception ex) { emit("WARN: could not write packbench_data.md: " + ex.Message); }

            return 0;
        }

        // Deterministic synthetic 2D parts (rectangles + L-shapes) for the 2D bench.
        private static List<Curve> MakeSynthetic2DParts(out double totalArea)
        {
            var list = new List<Curve>(); totalArea = 0.0;
            uint s = 12345u;
            double Rnd() { s = s * 1664525u + 1013904223u; return ((s >> 8) & 0xFFFFFF) / 16777216.0; }
            for (int i = 0; i < 24; i++)
            {
                double w = 0.3 + Rnd() * 1.2, h = 0.3 + Rnd() * 1.2;
                if (i % 4 == 3)
                {
                    list.Add(LShapeCurve(w, h)); totalArea += w * h - (w * 0.5) * (h * 0.5);
                }
                else { list.Add(RectCurve(0, 0, w, h)); totalArea += w * h; }
            }
            return list;
        }

        // ============================================================
        // COMPREHENSIVE 2D PACKER STUDY (--pack2dstudy, 2026-06-06).
        // Every 2D engine + V506 boundary modes on matched fixtures, with
        // timing, containment (inside the boundary AND clear of holes/notch),
        // overlap (count + max area), cov + covUsed. Dumps a CSV for the
        // plotter + a markdown table + per-solver .3dm/layout for HILT render.
        // Boundary mode = parts ALIGN to boundaries/holes, stay CONTAINED, and
        // do NOT overlap; the study measures exactly those invariants + speed.
        // ============================================================
        public static int RunPack2DStudy(HarnessOptions opts, Action<string> emit)
        {
            var inv = CultureInfo.InvariantCulture;
            string outDir = @"D:\code_ws\outputs\2026-06-06\packing_slm_evolution";
            string figDir = Path.Combine(outDir, "figures");
            Directory.CreateDirectory(figDir);
            var csv = new List<string>
            { "solver,family,fixture,params,placed,total,utilStockPct,covPct,covUsedPct,overlapPairs,maxOverlap,contained,outOfSheet,timeMs,status" };

            var rots = new[] { 0.0, 90.0, 180.0, 270.0 };
            var noHoles = new List<IReadOnlyList<Curve>>();

            // Fixtures.
            var fSat = MakeSynthetic2DParts(out double satArea);
            double satSheetArea = satArea / 0.60;
            double satW = Math.Sqrt(satSheetArea), satL = satSheetArea / satW;
            var satSheet = (Curve)RectCurve(0, 0, satL, satW);
            double satFull = satL * satW;
            var fOver = MakeOversubscribed2DParts(60, out _);
            var (lSheet, lHole, lArea) = MakeConcaveLSheetWithHole(satL, satW);
            double nx = satL * 0.45, ny = satW * 0.55;
            var lNotch = (Curve)RectCurve(nx, ny, satL - nx, satW - ny);
            var lHoles = new List<IReadOnlyList<Curve>> { new List<Curve> { lHole } };
            var lForbidden = new List<Curve> { lHole, lNotch };
            var fBound = MakeOversubscribed2DParts(40, out _);
            var satAreas = fSat.ConvertAll(PartArea).ToArray();
            var overAreas = fOver.ConvertAll(PartArea).ToArray();
            var boundAreas = fBound.ConvertAll(PartArea).ToArray();

            emit("COMPREHENSIVE 2D PACKER STUDY (--pack2dstudy)");
            emit(new string('-', 64));
            emit($"  saturated: {fSat.Count} parts, rect {satL:F2}x{satW:F2}; oversub: {fOver.Count} parts;");
            emit($"  boundary: {fBound.Count} parts on concave-L + hole (forbidden = hole + notch)");
            emit(new string('-', 64));

            void Run(string solver, string family, string fixture, string prm, Curve outline,
                     double fullA, List<Curve> forbidden, int total, double[] partAreas, Func<PackingResult> make, bool art)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = make();
                    sw.Stop();
                    var pl = r.PackedCurves.Select(c => c as PolylineCurve ?? ToPoly(c))
                        .Where(c => c != null).Cast<PolylineCurve>().ToList();
                    double union = UnionArea2D(pl, 1e-4);
                    double cov = fullA > 1e-9 ? union / fullA * 100.0 : 0.0;
                    double usedA = UsedBBoxArea2D(pl);
                    double covUsed = usedA > 1e-9 ? union / usedA * 100.0 : 0.0;
                    // STOCK UTILIZATION = TRUE placed-part area / (sheet - holes). fullA
                    // is the stock area here (rect = full; L = L minus hole). Numerator
                    // uses the ORIGINAL input part areas (via SourceIndices), NOT the
                    // x1000-inflated emitted geometry, so it is the honest fab yield.
                    // 80% util_stock is the bar for good 2D irregular packing.
                    double placedTrue = 0.0;
                    if (partAreas != null && r.SourceIndices != null && r.SourceIndices.Count == r.PackedCurves.Count)
                        foreach (var si in r.SourceIndices) { if (si >= 0 && si < partAreas.Length) placedTrue += partAreas[si]; }
                    else
                        foreach (var c in pl) { var amp = AreaMassProperties.Compute(c); if (amp != null) placedTrue += Math.Abs(amp.Area); }
                    double utilStock = fullA > 1e-9 ? placedTrue / fullA * 100.0 : 0.0;
                    int op = 0; double maxo = 0;
                    for (int i = 0; i < pl.Count; i++)
                        for (int j = i + 1; j < pl.Count; j++)
                        {
                            double a = IntersectionArea2D(pl[i], pl[j], 1e-4);
                            if (a > 1e-4) { op++; if (a > maxo) maxo = a; }
                        }
                    var ob = outline.GetBoundingBox(false);
                    int contained = 0;
                    foreach (var c in pl)
                    {
                        var bb = c.GetBoundingBox(false);
                        bool inB = bb.Min.X >= ob.Min.X - 0.05 && bb.Min.Y >= ob.Min.Y - 0.05
                                && bb.Max.X <= ob.Max.X + 0.05 && bb.Max.Y <= ob.Max.Y + 0.05;
                        bool clear = true;
                        if (forbidden != null)
                            foreach (var fr in forbidden)
                            {
                                var fp = fr as PolylineCurve ?? ToPoly(fr);
                                if (fp != null && IntersectionArea2D(c, fp, 1e-4) > 1e-4) { clear = false; break; }
                            }
                        if (inB && clear) contained++;
                    }
                    int oos = pl.Count - contained;
                    string status = op > 0 ? "OVERLAP" : (oos > 0 ? oos + "-OUT" : "ok");
                    string bar80 = utilStock >= 80.0 ? " >=80" : "";
                    emit($"  {solver,-22} {fixture,-10}: placed {r.PackedCurves.Count,3}/{total,-3} utilStock {utilStock,5:F1}%{bar80,-5} covUsed {covUsed,5:F1}% ovl {op}/{maxo:F3} in {contained,3}/{pl.Count,-3} {sw.ElapsedMilliseconds,6}ms {status}");
                    csv.Add(string.Join(",", solver, family, fixture, "\"" + prm + "\"",
                        r.PackedCurves.Count.ToString(inv), total.ToString(inv),
                        utilStock.ToString("F2", inv), cov.ToString("F2", inv), covUsed.ToString("F2", inv),
                        op.ToString(inv), maxo.ToString("F4", inv),
                        contained.ToString(inv), oos.ToString(inv),
                        sw.ElapsedMilliseconds.ToString(inv), status));
                    if (art)
                    {
                        var holeCurves = forbidden != null ? new List<Curve>(forbidden) : new List<Curve>();
                        string tag = (solver + "_" + fixture).Replace(' ', '_').Replace("(", "").Replace(")", "");
                        WriteLayout(Path.Combine(figDir, $"study_{tag}.txt"), satL, satW, r.PackedCurves, holeCurves);
                        WriteNesting3dm(Path.Combine(figDir, $"study_{tag}.3dm"), outline, holeCurves, r.PackedCurves);
                    }
                }
                catch (Exception ex)
                {
                    emit($"  {solver} {fixture} ERR: {ex.Message}");
                    csv.Add(string.Join(",", solver, family, fixture, "\"" + prm + "\"", "-", total.ToString(inv),
                        "-", "-", "-", "-", "-", "-", "-", "-", "ERR:" + ex.Message.Replace(',', ';')));
                }
            }

            // General packers on the saturated + oversubscribed rectangle fixtures.
            foreach (var fx in new[] { ("saturated", fSat, satFull, satAreas), ("oversub", fOver, satFull, overAreas) })
            {
                string fn = fx.Item1; var prt = fx.Item2; double full = fx.Item3; var ar = fx.Item4; int tot = prt.Count;
                Run("V1 SheetFillRhino", "2D sheet", fn, "simplify=on,maxCand=2000", satSheet, full, null, tot, ar,
                    () => IrregularSheetFill.ForV1(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, true, 1.0, 0, 2000).Pack(prt), false);
                Run("V2", "2D sheet", fn, "maxCand=2000", satSheet, full, null, tot, ar,
                    () => IrregularSheetFill.ForV2(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000).Pack(prt), false);
                Run("V3", "2D sheet", fn, "maxCand=2000", satSheet, full, null, tot, ar,
                    () => IrregularSheetFill.ForV3(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000).Pack(prt), false);
                Run("V506 plain", "2D sheet", fn, "delegates V2", satSheet, full, null, tot, ar,
                    () => IrregularSheetFill.ForV506(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000).Pack(prt), false);
                Run("V506 quality", "2D sheet", fn, "qualityNfp=on (evolved)", satSheet, full, null, tot, ar,
                    () => IrregularSheetFill.ForV506(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 0, 0.5, -1, 0, true).Pack(prt), true);
                Run("NFP-BLF greedy", "2D NFP", fn, "greedy", satSheet, full, null, tot, ar,
                    () => new IrregularSheetFillNfpBlf(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0).Pack(prt), false);
                Run("NFP-BLF evolved", "2D NFP", fn, "multistart+compact+reinsert+verify", satSheet, full, null, tot, ar,
                    () => new IrregularSheetFillNfpBlf(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0, PlacementScore.BottomLeft, true, 3, true, 2, true).Pack(prt), true);
                Run("NFP-BLF GLS", "2D NFP", fn, "evolved + overlap-min-insert (GLS)", satSheet, full, null, tot, ar,
                    () => new IrregularSheetFillNfpBlf(new[] { satSheet }, noHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0, PlacementScore.BottomLeft, true, 3, true, 2, true, true).Pack(prt), false);
                Run("NfpRect mode0", "2D strip", fn, "simplify=off,opt=0", satSheet, full, null, tot, ar,
                    () => new NfpBottomLeftFillRhino(satW, satL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, false, 1.0, 2500, 0, 0, 0, PackingCornerMode.BottomLeft).Pack(prt), false);
                Run("NfpRect mode2", "2D strip", fn, "simplify=off,opt=2 multistart", satSheet, full, null, tot, ar,
                    () => new NfpBottomLeftFillRhino(satW, satL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, false, 1.0, 2500, 2, 0, 0, PackingCornerMode.BottomLeft).Pack(prt), false);
                Run("BottomLeftFill", "2D BLF", fn, "simplify=on", satSheet, full, null, tot, ar,
                    () => new BottomLeftFillRhino(satW, satL, 0.0, rots, 0.01, PackingSortMode.AreaDescending, true, 1.0, 0, PackingCornerMode.BottomLeft).Pack(prt), false);
            }

            // V506 BOUNDARY MODES on the concave-L + hole fixture (the user's emphasis).
            // boundaryMode > 0 runs V506's OWN path (parts align to boundary/holes,
            // contained, no overlap). Forbidden = hole + the L notch.
            emit(new string('-', 64));
            emit("  V506 BOUNDARY MODES (concave-L + hole; parts align + stay contained + no overlap)");
            int bt = fBound.Count;
            Run("V506 bmode0 plain", "2D boundary", "boundary", "boundaryMode=0", lSheet, lArea, lForbidden, bt, boundAreas,
                () => IrregularSheetFill.ForV506(new[] { lSheet }, lHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 0).Pack(fBound), true);
            Run("V506 bmode1 bias", "2D boundary", "boundary", "boundaryMode=1 affinity=0.5", lSheet, lArea, lForbidden, bt, boundAreas,
                () => IrregularSheetFill.ForV506(new[] { lSheet }, lHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 1, 0.5).Pack(fBound), true);
            Run("V506 bmode2 ring", "2D boundary", "boundary", "boundaryMode=2 affinity=0.5", lSheet, lArea, lForbidden, bt, boundAreas,
                () => IrregularSheetFill.ForV506(new[] { lSheet }, lHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 2, 0.5).Pack(fBound), true);
            Run("V506 bmode3 divide", "2D boundary", "boundary", "boundaryMode=3 curve-division", lSheet, lArea, lForbidden, bt, boundAreas,
                () => IrregularSheetFill.ForV506(new[] { lSheet }, lHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 3, 0.5).Pack(fBound), true);
            Run("V506 quality", "2D boundary", "boundary", "qualityNfp=on (evolved, holes)", lSheet, lArea, lForbidden, bt, boundAreas,
                () => IrregularSheetFill.ForV506(new[] { lSheet }, lHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 0, 0.5, -1, 0, true).Pack(fBound), true);
            Run("NFP-BLF evolved", "2D boundary", "boundary", "multistart+verify (holes)", lSheet, lArea, lForbidden, bt, boundAreas,
                () => new IrregularSheetFillNfpBlf(new[] { lSheet }, lHoles, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0, PlacementScore.BottomLeft, true, 3, true, 2, true).Pack(fBound), true);

            // HARD oversubscribed multi-hole fixture (arithmetic ceiling ~0.95) -- the
            // real 80% stress test, where the existing too-easy boundary fixture (ceiling
            // ~0.87) cannot expose a lever. Stock = outer - sum(hole areas).
            emit(new string('-', 64));
            emit("  HARD oversub multi-hole fixture (ceiling ~0.95; the 80% stress test)");
            var (hOut, hHoles, hStock) = MakeHardOversubHoled(satL, satW);
            var hHolesList = new List<IReadOnlyList<Curve>> { hHoles };
            var fHard = MakeOversubscribed2DParts(90, out _);
            var hardAreas = fHard.ConvertAll(PartArea).ToArray();
            int ht2 = fHard.Count;
            Run("V2", "2D hard", "hard", "maxCand=2000", hOut, hStock, hHoles, ht2, hardAreas,
                () => IrregularSheetFill.ForV2(new[] { hOut }, hHolesList, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000).Pack(fHard), false);
            Run("NFP-BLF greedy", "2D hard", "hard", "greedy", hOut, hStock, hHoles, ht2, hardAreas,
                () => new IrregularSheetFillNfpBlf(new[] { hOut }, hHolesList, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0).Pack(fHard), false);
            Run("NFP-BLF evolved", "2D hard", "hard", "multistart+compact+reinsert+verify (holes)", hOut, hStock, hHoles, ht2, hardAreas,
                () => new IrregularSheetFillNfpBlf(new[] { hOut }, hHolesList, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0, PlacementScore.BottomLeft, true, 3, true, 2, true).Pack(fHard), true);
            Run("NFP-BLF GLS", "2D hard", "hard", "evolved + overlap-min-insert (GLS)", hOut, hStock, hHoles, ht2, hardAreas,
                () => new IrregularSheetFillNfpBlf(new[] { hOut }, hHolesList, 0.0, rots, 0.01, PackingSortMode.AreaDescending, 0, PlacementScore.BottomLeft, true, 3, true, 2, true, true).Pack(fHard), true);
            Run("V506 quality", "2D hard", "hard", "qualityNfp=on", hOut, hStock, hHoles, ht2, hardAreas,
                () => IrregularSheetFill.ForV506(new[] { hOut }, hHolesList, 0.0, rots, 0.01, PackingSortMode.AreaDescending, PackingCornerMode.BottomLeft, 0, 2000, 0, 0.5, -1, 0, true).Pack(fHard), false);

            string csvPath = Path.Combine(outDir, "pack2d_study_metrics.csv");
            File.WriteAllLines(csvPath, csv);
            emit(new string('-', 64));
            emit($"  wrote {csv.Count - 1} metric rows -> {csvPath}");
            emit($"  wrote per-solver .3dm + layout -> {figDir}");
            return 0;
        }

        // True planar area of a closed part curve (stock-utilization numerator).
        // Uses original input geometry, not the inflated emitted curve.
        private static double PartArea(Curve c)
        {
            if (c == null) return 0.0;
            var amp = AreaMassProperties.Compute(c);
            return amp != null ? Math.Abs(amp.Area) : 0.0;
        }

        private static double UsedBBoxArea2D(List<PolylineCurve> pls)
        {
            if (pls == null || pls.Count == 0) return 0.0;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in pls)
            {
                var b = c.GetBoundingBox(false);
                if (b.Min.X < minX) minX = b.Min.X; if (b.Min.Y < minY) minY = b.Min.Y;
                if (b.Max.X > maxX) maxX = b.Max.X; if (b.Max.Y > maxY) maxY = b.Max.Y;
            }
            return Math.Max(0.0, (maxX - minX) * (maxY - minY));
        }

        // Oversubscribed 2D part set (2026-06-06 evolution bench): many small
        // parts so the sheet saturates and a chunk stays unplaced under greedy.
        // This is the regime where covUsed AND placedCount can actually move, so
        // compaction + reinsertion gains are measurable (unlike the saturated
        // fixed-area fixture where cov is invariant). Deterministic LCG.
        private static List<Curve> MakeOversubscribed2DParts(int n, out double totalArea)
        {
            var list = new List<Curve>(); totalArea = 0.0;
            uint s = 777u;
            double Rnd() { s = s * 1664525u + 1013904223u; return ((s >> 8) & 0xFFFFFF) / 16777216.0; }
            for (int i = 0; i < n; i++)
            {
                double w = 0.25 + Rnd() * 0.9, h = 0.25 + Rnd() * 0.9;
                if (i % 5 == 4) { list.Add(LShapeCurve(w, h)); totalArea += w * h - (w * 0.5) * (h * 0.5); }
                else { list.Add(RectCurve(0, 0, w, h)); totalArea += w * h; }
            }
            return list;
        }

        // Concave L-shaped sheet (full w x h minus a top-right rectangular notch)
        // plus a central rectangular hole, for the evolution bench. The concave
        // bay + the hole exercise the exact IFP containment and the NFP-hole
        // obstacle paths that a convex rectangle cannot. Returns the outline, the
        // hole, and the true free area (L area minus hole area).
        // HARD oversubscribed fixture: a rectangular stock with THREE interior
        // holes (thinner bays between them), to stress-test the 80% bar where the
        // mildly-oversubscribed L+hole fixture (ceiling ~0.87) cannot. Returns the
        // outer outline, the hole curves, and the stock area (outer - sum holes).
        private static (Curve outline, List<Curve> holes, double stock) MakeHardOversubHoled(double w, double h)
        {
            var outline = (Curve)RectCurve(0, 0, w, h);
            var holes = new List<Curve>();
            double[,] hs = { { 0.16, 0.18, 0.15, 0.16 }, { 0.56, 0.28, 0.17, 0.22 }, { 0.30, 0.62, 0.22, 0.13 } };
            double holeArea = 0.0;
            for (int i = 0; i < 3; i++)
            {
                double hw = w * hs[i, 2], hh = h * hs[i, 3];
                holes.Add((Curve)RectCurve(w * hs[i, 0], h * hs[i, 1], hw, hh));
                holeArea += hw * hh;
            }
            return (outline, holes, Math.Max(1e-9, w * h - holeArea));
        }

        private static (Curve outline, Curve hole, double area) MakeConcaveLSheetWithHole(double w, double h)
        {
            double nx = w * 0.45, ny = h * 0.55; // notch carved from (nx,ny) to (w,h)
            var pts = new List<Point3d>
            {
                new Point3d(0, 0, 0), new Point3d(w, 0, 0), new Point3d(w, ny, 0),
                new Point3d(nx, ny, 0), new Point3d(nx, h, 0), new Point3d(0, h, 0), new Point3d(0, 0, 0)
            };
            var outline = new PolylineCurve(pts);
            double hw = w * 0.16, hh = h * 0.16;
            var hole = (Curve)RectCurve(w * 0.12, h * 0.12, hw, hh);
            double lArea = w * ny + nx * (h - ny);
            return (outline, hole, Math.Max(1e-9, lArea - hw * hh));
        }

        // Write a placed-nesting layout as plain text: a SHEET line (rectangle
        // w x h) then one PART line per placed curve (flattened x y x y ...).
        // Rendered by plot_packbench.py (the harness is headless = no viewport).
        private static void WriteLayout(string path, double sheetL, double sheetW, List<Curve> curves,
            List<Curve> holeCurves = null)
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new List<string> { string.Format(inv, "SHEET 0 0 {0:R} {1:R}", sheetL, sheetW) };
            if (holeCurves != null)
            {
                foreach (var hc in holeCurves)
                {
                    var hp = hc as PolylineCurve ?? ToPoly(hc);
                    if (hp == null) continue;
                    var ht = new List<string> { "HOLE" };
                    for (int i = 0; i < hp.PointCount; i++)
                    { var p = hp.Point(i); ht.Add(p.X.ToString("R", inv)); ht.Add(p.Y.ToString("R", inv)); }
                    lines.Add(string.Join(" ", ht));
                }
            }
            foreach (var c in curves)
            {
                var pc = c as PolylineCurve ?? ToPoly(c);
                if (pc == null) continue;
                var toks = new List<string> { "PART" };
                for (int i = 0; i < pc.PointCount; i++)
                {
                    var p = pc.Point(i);
                    toks.Add(p.X.ToString("R", inv)); toks.Add(p.Y.ToString("R", inv));
                }
                lines.Add(string.Join(" ", toks));
            }
            File.WriteAllLines(path, lines);
        }

        // Write a 2D nesting to a .3dm: sheet outline (black curve), parts as filled
        // planar breps (viridis ramp), holes as red curves. A real .3dm artifact that
        // can be opened + shaded-captured in a live Rhino viewport (the headless
        // harness cannot ViewCapture, so capture is a separate live-Rhino step).
        private static void WriteNesting3dm(string path, Curve sheet, List<Curve> holes, List<Curve> parts)
        {
            try
            {
                var f = new File3dm();
                f.Settings.ModelUnitSystem = Rhino.UnitSystem.Meters;
                double tol = 0.001;
                int i = 0, n = parts.Count;
                foreach (var p in parts)
                {
                    var attr = new Rhino.DocObjects.ObjectAttributes
                    { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = RampColor(i, n) };
                    i++;
                    var breps = Brep.CreatePlanarBreps(p, tol);
                    if (breps != null && breps.Length > 0) foreach (var b in breps) f.Objects.AddBrep(b, attr);
                    else f.Objects.AddCurve(p, attr);
                }
                var sa = new Rhino.DocObjects.ObjectAttributes
                { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = System.Drawing.Color.Black };
                f.Objects.AddCurve(sheet, sa);
                foreach (var h in holes)
                {
                    var ha = new Rhino.DocObjects.ObjectAttributes
                    { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = System.Drawing.Color.Red };
                    f.Objects.AddCurve(h, ha);
                }
                f.Write(path, 6);
            }
            catch { }
        }

        // Headless ViewCapture: bake the 2D nesting (sheet outline + filled planar
        // breps for parts + red hole outlines) into the in-process Rhino.Inside doc
        // and capture a top-view PNG. Proves the pipeline renders from a headless
        // RhinoCore (no live Rhino session). No-op with a log line if Inside exposes
        // no capturable view.
        private static void TryHeadlessCapture2D(string path, Curve sheet, List<Curve> holes,
            List<Curve> parts, int width, int height, Action<string> emit)
        {
            try
            {
                var doc = Rhino.RhinoDoc.ActiveDoc;
                if (doc == null)
                {
                    try { doc = Rhino.RhinoDoc.Create(null); } catch { }
                }
                if (doc == null) { emit("  headless capture: no doc (Rhino.Inside)"); return; }
                var ids = new List<Guid>();
                foreach (var o in doc.Objects) ids.Add(o.Id);
                foreach (var id in ids) doc.Objects.Delete(id, true);

                double tol = doc.ModelAbsoluteTolerance > 0 ? doc.ModelAbsoluteTolerance : 0.001;
                int n = parts.Count, i = 0;
                foreach (var p in parts)
                {
                    var col = RampColor(i, n); i++;
                    var attr = new Rhino.DocObjects.ObjectAttributes
                    {
                        ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject,
                        ObjectColor = col,
                    };
                    var breps = Brep.CreatePlanarBreps(p, tol);
                    if (breps != null && breps.Length > 0) foreach (var b in breps) doc.Objects.AddBrep(b, attr);
                    else doc.Objects.AddCurve(p, attr);
                }
                var sheetAttr = new Rhino.DocObjects.ObjectAttributes
                { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = System.Drawing.Color.Black };
                doc.Objects.AddCurve(sheet, sheetAttr);
                foreach (var h in holes)
                {
                    var ha = new Rhino.DocObjects.ObjectAttributes
                    { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = System.Drawing.Color.Red };
                    doc.Objects.AddCurve(h, ha);
                }

                var view = doc.Views.ActiveView;
                if (view == null)
                {
                    var vs = doc.Views.GetViewList(true, false);
                    if (vs != null && vs.Length > 0) view = vs[0];
                }
                if (view == null) { emit("  headless capture: Rhino.Inside exposes no view"); return; }
                view.ActiveViewport.SetProjection(Rhino.Display.DefinedViewportProjection.Top, "Top", false);
                var dm = Rhino.Display.DisplayModeDescription.FindByName("Shaded");
                if (dm != null) view.ActiveViewport.DisplayMode = dm;
                var bb = doc.Objects.BoundingBoxVisible;
                if (bb.IsValid) view.ActiveViewport.ZoomBoundingBox(bb);
                doc.Views.Redraw();
                var vc = new Rhino.Display.ViewCapture
                { Width = width, Height = height, DrawGrid = false, DrawAxes = false, ScaleScreenItems = false };
                var bmp = vc.CaptureToBitmap(view);
                if (bmp != null) { bmp.Save(path); emit("  headless capture saved: " + Path.GetFileName(path)); }
                else emit("  headless capture: CaptureToBitmap returned null");
            }
            catch (Exception ex) { emit("  headless capture failed: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static System.Drawing.Color RampColor(int i, int n)
        {
            double t = n <= 1 ? 0.0 : (double)i / (n - 1);
            int r = (int)(255 * Math.Min(1.0, Math.Max(0.0, 1.6 * t)));
            int g = (int)(255 * Math.Min(1.0, Math.Max(0.0, 1.0 - Math.Abs(t - 0.5) * 1.4)));
            int b = (int)(255 * Math.Min(1.0, Math.Max(0.0, 1.6 * (1.0 - t))));
            return System.Drawing.Color.FromArgb(r, g, b);
        }

        private static PolylineCurve RectCurve(double x, double y, double w, double h)
        {
            var pts = new[]
            {
                new Point3d(x, y, 0), new Point3d(x + w, y, 0), new Point3d(x + w, y + h, 0),
                new Point3d(x, y + h, 0), new Point3d(x, y, 0),
            };
            return new PolylineCurve(pts);
        }

        private static PolylineCurve LShapeCurve(double w, double h)
        {
            var pts = new[]
            {
                new Point3d(0, 0, 0), new Point3d(w, 0, 0), new Point3d(w, h * 0.5, 0),
                new Point3d(w * 0.5, h * 0.5, 0), new Point3d(w * 0.5, h, 0), new Point3d(0, h, 0),
                new Point3d(0, 0, 0),
            };
            return new PolylineCurve(pts);
        }

        // Load the first n ETH stones (PCA-aligned) for the 3D/masonry bench.
        private static List<Mesh> LoadEthStonesForBench(int n)
        {
            var list = new List<Mesh>();
            try
            {
                if (!Directory.Exists(EthStonesDir)) return list;
                foreach (var f in Directory.GetFiles(EthStonesDir, "*.obj")
                    .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).Take(n))
                {
                    var m = ReadObjMesh(f);
                    if (m == null || !m.IsValid) continue;
                    list.Add(PcaAlignMesh(m) ?? m);
                }
            }
            catch { }
            return list;
        }

        // Synthetic fractured block: 4 x 4 x 2 m with three intersecting fracture
        // planes (two vertical + one sub-horizontal), as quads -> triangles. Used
        // to bench BlockCutOptSolver + RecoveryCascade headlessly.
        private static PlyMesh MakeSyntheticFractureMesh()
        {
            var v = new List<double>(); var t = new List<int>();
            void Quad(double[] a, double[] b, double[] c, double[] d)
            {
                int i0 = v.Count / 3;
                v.AddRange(a); v.AddRange(b); v.AddRange(c); v.AddRange(d);
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }
            Quad(new[] { 1.3, 0.0, 0.0 }, new[] { 1.3, 4.0, 0.0 }, new[] { 1.3, 4.0, 2.0 }, new[] { 1.3, 0.0, 2.0 }); // x=1.3
            Quad(new[] { 0.0, 2.7, 0.0 }, new[] { 4.0, 2.7, 0.0 }, new[] { 4.0, 2.7, 2.0 }, new[] { 0.0, 2.7, 2.0 }); // y=2.7
            Quad(new[] { 0.0, 0.0, 1.0 }, new[] { 4.0, 1.0, 1.0 }, new[] { 4.0, 1.0, 1.2 }, new[] { 0.0, 0.0, 1.2 }); // sub-horiz
            return new PlyMesh(v, t, new byte[0]);
        }

        // ====================================================================
        // --pack3d : Frahan PRODUCTION 3D irregular-container mesh-heightmap packer
        //            on the ETH1100 dry-stone meshes (added 2026-05-25)
        // ====================================================================

        // Default ETH1100 closed-stone directory. The positional arg overrides it.
        private const string EthStonesDir =
            @"D:\code_ws\Template-General\raw\2026-05-25\eth_drystone\closed\1100 Closed Stone Meshes";
        private const int EthStoneCount = 30;          // first N stones, sorted
        private const string Pack3dOutPath =
            @"D:\code_ws\outputs\2026-05-25\eth1100_pack\eth1100_3d_pack_production.3dm";

        // Routes the first ~30 ETH watertight stone meshes through a faithful C# port
        // of the GOLDEN V2 skyline pack (outputs/2026-05-25/eth1100_pack/eth1100_pack3d.py
        // skyline_pack). PCA-aligns each stone (eth1100_pack3d.py pca_orient), converts it
        // (RhinoCommon Mesh) -> MeshPackItem (bbox-local Vec3 verts + MeshTriangle faces)
        // the same way CreateMeshPackItem does, builds the 6 orientation bottom/top maps
        // with the Core OrientedMeshHeightmap, then runs V2's FFD-by-volume skyline drop
        // that MINIMISES THE RESULTING TOP-Z (the crux the Core GreedyMeshHeightmapPacker
        // gets wrong by minimising added height-mass / lowest bottom-Z instead). Reports
        // placed/total, compactness, volume fill, used pile height, the AABB-in-container
        // invariant; writes the placed stones in the container to a .3dm for visual HITL.
        public static int RunPack3D(HarnessOptions opts, Action<string> emit)
        {
            // Stone source directory: positional arg if given, else the ETH default.
            string dir = string.IsNullOrEmpty(opts.FixturePath) ? EthStonesDir : opts.FixturePath;
            if (!Directory.Exists(dir))
            {
                emit("ERROR: ETH stone directory not found: " + dir);
                return 1;
            }

            var objFiles = Directory.GetFiles(dir, "*.obj")
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                .Take(EthStoneCount)
                .ToList();
            emit($"source   : {dir}");
            emit($"stones   : loading first {objFiles.Count} of *.obj (sorted)");
            if (objFiles.Count < 2)
            {
                emit("ERROR: need >= 2 .obj stone meshes.");
                return 1;
            }

            // Load each .obj into a RhinoCommon Mesh (the ETH closed meshes are
            // simple v / f-triangle files; parse them directly so we do not depend on
            // an unverified FileObj import path inside the headless Rhino core).
            var stones = new List<Mesh>(objFiles.Count);
            double sumStoneVol = 0.0;
            foreach (var f in objFiles)
            {
                var m = ReadObjMesh(f);
                if (m == null || !m.IsValid || m.Vertices.Count == 0 || m.Faces.Count == 0)
                {
                    emit($"  warn: {Path.GetFileName(f)} did not parse to a valid mesh; skipped.");
                    continue;
                }
                // PCA-ALIGN each stone BEFORE any item / volume / diagonal work, matching
                // the validated Python demo eth1100_pack3d.py pca_orient(): vertex-covariance
                // eigenframe, largest extent -> X, mid -> Y, smallest -> Z, right-handed,
                // re-centred at its own bbox via the bbox-local conversion downstream. So
                // the Core packer's 6 box orientations rotate about PRINCIPAL axes and the
                // stone beds on its broad/flat face. PCA is baked into this source mesh; the
                // placement transform (BuildPack3DTransform) is unchanged.
                var aligned = PcaAlignMesh(m) ?? m;
                stones.Add(aligned);
                double v = 0.0;
                try { var vmp = VolumeMassProperties.Compute(aligned); if (vmp != null) v = Math.Abs(vmp.Volume); } catch { }
                if (v <= 0) { var bb = aligned.GetBoundingBox(true); v = (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) * (bb.Max.Z - bb.Min.Z); }
                sumStoneVol += v;
            }
            emit("alignment: each stone PCA-aligned (vertex-covariance eigenframe, largest->X mid->Y smallest->Z, right-handed) -- matches eth1100_pack3d.py pca_orient");
            if (stones.Count < 2) { emit("ERROR: < 2 usable stone meshes after parse."); return 1; }

            // Grid resolution. The golden V2 (eth1100_pack3d.py) ran a FINE floor grid:
            // cell = min(cw,cd)/grid with grid 50 -> cell 0.070 on the 3.5 box, so each
            // stone footprint spans ~10-14 cells and the skyline contact interlocks
            // tightly (its 32% compactness depends on this fine grid). medianDiag/8 was
            // tried first but gives a 16x16 floor (cell 0.209) on these stones -- ~4 cells
            // per footprint, too blocky: stones cannot seat into each other so the pile
            // rises and compactness drops to ~28%. We therefore size the cell from the
            // container like V2 (grid 50) and take the FINER of that and medianDiag/8, so
            // the floor resolution always matches (or beats) the golden's. medianDiag is
            // kept as a reported diagnostic.
            var diags = stones.Select(s => s.GetBoundingBox(true).Diagonal.Length)
                .Where(d => d > 1e-9).OrderBy(d => d).ToList();
            double medianDiag = diags.Count > 0 ? diags[(diags.Count - 1) / 2] : 1.0;
            const int V2Grid = 50;                                  // golden V2's floor grid (cell 0.070)
            double v2Cell = Math.Min(3.5, 3.5) / V2Grid;            // V2 cell = min(cw,cd)/grid
            double cellSize = Math.Max(0.02, Math.Min(v2Cell, medianDiag / 8.0));

            // Per-stone volume (true mesh volume; AABB fallback) for FFD-by-volume DESC
            // ordering and the compactness numerator, matching the golden V2's `vols`.
            var stoneVol = new double[stones.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                double v = 0.0;
                try { var vmp = VolumeMassProperties.Compute(stones[i]); if (vmp != null) v = Math.Abs(vmp.Volume); } catch { }
                if (v <= 0) { var bb = stones[i].GetBoundingBox(true); v = (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) * (bb.Max.Z - bb.Min.Z); }
                stoneVol[i] = v;
            }

            // CONTAINER = the golden V2's box: 3.5 x 3.5 x 3.0 (eth1100_pack3d.py main()),
            // so the harness result is directly comparable to the validated Python pack.
            // grid = floor(cw / cell) cells per side, exactly V2's nx = int(cw/cell).
            // stride = max(1, int(0.05*grid)), V2's anchor scan stride (~5% of the grid).
            double cw = 3.5, cd = 3.5, ch = 3.0;
            var containerMesh = MakeBoxMesh(new Point3d(0, 0, 0), new Point3d(cw, cd, ch));
            int nx = Math.Max(1, (int)(cw / cellSize));
            int ny = Math.Max(1, (int)(cd / cellSize));
            int gridSide = Math.Min(nx, ny);
            int stride = Math.Max(1, (int)(0.05 * gridSide));
            double containerVol = cw * cd * ch;
            emit($"container: V2 box {cw:F2} x {cd:F2} x {ch:F2} (volume {containerVol:F3})  cellSize {cellSize:F4} (V2 cell {v2Cell:F4} grid {V2Grid}; medianDiag/8 {medianDiag / 8.0:F4}; finer wins)");
            emit($"grid     : {nx} x {ny} floor cells (cell {cellSize:F4})  stride {stride} (~5% of {gridSide})  -- matches eth1100_pack3d.py skyline_pack");
            emit($"inventory: {stones.Count} stone meshes  (sum stone volume {sumStoneVol:F4})");

            // Convert each stone to a MeshPackItem (bbox-local verts + tri faces), the
            // same conversion CreateMeshPackItem performs. Track each item's source-min
            // so we can compose the placement transform like the GH component's BuildTransform.
            var items = new List<MeshPackItem>(stones.Count);
            var sourceMinById = new Dictionary<string, Point3d>(StringComparer.Ordinal);
            var meshById = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            var volById = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < stones.Count; i++)
            {
                var item = MeshToPackItem(i.ToString(), stones[i], out Point3d srcMin);
                if (item == null) { emit($"  warn: stone {i} could not be triangulated; skipped."); continue; }
                items.Add(item);
                sourceMinById[item.Id] = srcMin;
                meshById[item.Id] = stones[i];
                volById[item.Id] = stoneVol[i];
            }
            if (items.Count < 2) { emit("ERROR: < 2 usable MeshPackItems."); return 1; }

            // Container min is (0,0,0): the box mesh's bbox min is the origin, so the
            // GH component's containerMin term is 0 here.
            var containerMin = new Point3d(0, 0, 0);
            double eps = cellSize + 1e-6;

            // ----------------------------------------------------------------
            // V2-SKYLINE PORT (replaces the Core GreedyMeshHeightmapPacker call)
            // ----------------------------------------------------------------
            // The Core packer minimises ADDED height-mass (ScorePlacement) and rests on
            // the LOWEST bottom-Z (TryGetLowestZ), which builds tall blocking piles
            // (15/30 placed, ~25%). The golden V2 (eth1100_pack3d.py skyline_pack) instead
            // MINIMISES THE RESULTING TOP-Z first. This block ports skyline_pack exactly:
            //   * FFD by stone volume DESC,
            //   * for each stone scan 6 orientations x grid anchors (stride),
            //   * drop onto OUR OWN floor skyline: z = max(skyline[ax+mx,ay+my] - bottom),
            //   * reject if z + (top - bottom) > ch,
            //   * pick the lexicographically-smallest (resultingTop, z, ax, ay),
            //   * raise the skyline at the placed cells to z + topAt.
            // It reuses OrientedMeshHeightmap.Build for the per-(downAxis,yaw) bottom/top
            // maps (the AABB-verified orientation machinery) with clearance 0 so
            // PaddingCells == 0 and map cell (mx,my) maps to floor cell (ax+mx, ay+my),
            // matching V2's prof key (lx,ly) -> (ax+lx, ay+ly).

            // Precompute the 6 orientation heightmaps per item (Core's GetOrientations
            // set: downAxis in {0,1,2} x yaw {0,90}, yaw90 skipped when the rotated
            // footprint is square -- byte-identical orientation enumeration).
            var mapsById = new Dictionary<string, List<OrientedMeshHeightmap>>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                var maps = new List<OrientedMeshHeightmap>(6);
                foreach (var downAxis in new[] { 0, 1, 2 })
                {
                    var m0 = OrientedMeshHeightmap.Build(it, downAxis, 0, cellSize, 0.0);
                    maps.Add(m0);
                    if (Math.Abs(m0.GeometrySize.Width - m0.GeometrySize.Depth) > 1e-9)
                        maps.Add(OrientedMeshHeightmap.Build(it, downAxis, 90, cellSize, 0.0));
                }
                mapsById[it.Id] = maps;
            }

            // Own floor skyline over the V2 container floor cells (init 0).
            var skyline = new double[nx, ny];
            // FFD: largest stone volume first.
            var order = items.OrderByDescending(it => volById[it.Id]).ToList();

            var placements = new List<MeshPackPlacement>(items.Count);
            var failures = new List<string>();
            int seq = 0;
            foreach (var it in order)
            {
                OrientedMeshHeightmap? bestMap = null;
                int bestAx = 0, bestAy = 0;
                double bestZ = 0.0;
                // Lexicographic key (resultingTop, z, ax, ay), smaller is better.
                double bestTop = double.PositiveInfinity, bestKeyZ = double.PositiveInfinity;
                int bestKeyAx = int.MaxValue, bestKeyAy = int.MaxValue;

                foreach (var map in mapsById[it.Id])
                {
                    // V2 ptop / pbot: global max-top / min-bottom of the oriented stone.
                    double ptop = map.MaxTop, pbot = map.MinBottom;
                    int maxAx = nx - map.WidthCells;   // V2: range(0, max(1, nx - pxmax))
                    int maxAy = ny - map.DepthCells;
                    if (maxAx < 0 || maxAy < 0) continue; // footprint exceeds the floor
                    for (int ax = 0; ax <= maxAx; ax += stride)
                    {
                        for (int ay = 0; ay <= maxAy; ay += stride)
                        {
                            // rest_z: z = max over occupied cells of (skyline[ax+mx,ay+my] - bottom).
                            double z = 0.0;
                            for (int mx = 0; mx < map.WidthCells; mx++)
                            {
                                for (int my = 0; my < map.DepthCells; my++)
                                {
                                    if (!map.IsOccupied(mx, my)) continue;
                                    double need = skyline[ax + mx, ay + my] - map.BottomAt(mx, my);
                                    if (need > z) z = need;
                                }
                            }
                            if (z + ptop - pbot > ch) continue;   // would poke through the lid
                            double top = z + ptop;
                            // Lexicographic compare (top, z, ax, ay).
                            bool better = top < bestTop - 1e-12
                                || (Math.Abs(top - bestTop) <= 1e-12 && (z < bestKeyZ - 1e-12
                                || (Math.Abs(z - bestKeyZ) <= 1e-12 && (ax < bestKeyAx
                                || (ax == bestKeyAx && ay < bestKeyAy)))));
                            if (bestMap == null || better)
                            {
                                bestMap = map; bestAx = ax; bestAy = ay; bestZ = z;
                                bestTop = top; bestKeyZ = z; bestKeyAx = ax; bestKeyAy = ay;
                            }
                        }
                    }
                }

                if (bestMap == null) { failures.Add(it.Id); continue; }

                // Raise the skyline at the placed cells to z + topAt (V2 sky update).
                for (int mx = 0; mx < bestMap.WidthCells; mx++)
                {
                    for (int my = 0; my < bestMap.DepthCells; my++)
                    {
                        if (!bestMap.IsOccupied(mx, my)) continue;
                        double t = bestZ + bestMap.TopAt(mx, my);
                        if (t > skyline[bestAx + mx, bestAy + my]) skyline[bestAx + mx, bestAy + my] = t;
                    }
                }

                // Placement origin: match the Core packer's convention exactly
                // (GreedyMeshHeightmapPacker.FindBestPlacement): geometry origin at
                // ((ax + PaddingCells)*cell, (ay + PaddingCells)*cell, z). PaddingCells == 0.
                var origin = new Vec3(
                    (bestAx + bestMap.PaddingCells) * cellSize,
                    (bestAy + bestMap.PaddingCells) * cellSize,
                    bestZ);
                placements.Add(new MeshPackPlacement(
                    it, origin, bestMap.OrientationBoundsMin, bestMap.GeometrySize,
                    bestMap.YawDegrees, bestMap.DownAxis, bestTop, seq++));
            }

            // Materialise + measure: place each source (PCA-aligned) mesh with the verified
            // BuildPack3DTransform, then measure placed count, true placed-stone volume,
            // used height, compactness, volume fill, and the AABB-in-container invariant.
            var placedMeshes = new List<Mesh>(placements.Count);
            double placedStoneVol = 0.0, usedHeight = 0.0;
            int outsideCount = 0; double worstOverhang = 0.0;
            int downNone = 0, downX = 0, downY = 0;
            foreach (var p in placements)
            {
                if (p.DownAxis == 1) downX++; else if (p.DownAxis == 2) downY++; else downNone++;
                if (!sourceMinById.TryGetValue(p.Item.Id, out var srcMin)) continue;
                if (!meshById.TryGetValue(p.Item.Id, out var srcMesh)) continue;
                var dup = srcMesh.DuplicateMesh();
                dup.Transform(BuildPack3DTransform(p, srcMin, containerMin));
                placedMeshes.Add(dup);
                var bb = dup.GetBoundingBox(true);
                if (bb.IsValid && bb.Max.Z > usedHeight) usedHeight = bb.Max.Z;
                if (bb.IsValid)
                {
                    double over = Math.Max(0.0, Math.Max(
                        Math.Max(-bb.Min.X, bb.Max.X - cw),
                        Math.Max(Math.Max(-bb.Min.Y, bb.Max.Y - cd),
                                 Math.Max(-bb.Min.Z, bb.Max.Z - ch))));
                    if (over > eps) { outsideCount++; if (over > worstOverhang) worstOverhang = over; }
                }
                if (volById.TryGetValue(p.Item.Id, out var pv)) placedStoneVol += pv;
            }

            // Compactness = packed stone volume / (floor area x USED height) -- the golden
            // V2 metric (rewards interlocking). Volume fill = packed / full container.
            double usedVol = cw * cd * Math.Max(usedHeight, cellSize);
            double compactness = usedVol > 1e-9 ? placedStoneVol / usedVol * 100.0 : 0.0;
            double volFill = containerVol > 1e-9 ? placedStoneVol / containerVol * 100.0 : 0.0;

            emit(new string('-', 64));
            emit("Frahan 3D irregular-container packer -- V2 SKYLINE PORT (eth1100_pack3d.py skyline_pack):");
            emit("  algorithm    : FFD by volume DESC; per stone scan 6 orientations x grid anchors (stride);");
            emit("                 drop onto own floor skyline; pick MIN resulting TOP-Z (lexicographic top,z,ax,ay).");
            emit("  orientations : 3 PCA-axis-down (None/X/Y) x yaw {0,90}, yaw90 skipped on square footprint.");
            emit("  maps         : reuse OrientedMeshHeightmap.Build(item, downAxis, yaw, cell, 0) bottom/top profiles.");
            emit(new string('-', 64));
            emit($"  placed count       : {placements.Count} of {items.Count}");
            emit($"  compactness        : {compactness:F1}%  (packed stone volume {placedStoneVol:F4} / (floor {cw * cd:F3} x used height {usedHeight:F3}))");
            emit($"  volume fill        : {volFill:F2}%  (packed stone volume / full container volume {containerVol:F3})");
            emit($"  used height        : {usedHeight:F3}  (container height {ch:F2})");
            emit($"  down-axis None/X/Y : {downNone}/{downX}/{downY}");
            emit(new string('-', 64));

            // CORRECTNESS INVARIANT: every placed stone's world AABB must lie inside the
            // container box [0,cw] x [0,cd] x [0,ch]. A rotation round-trip sign error
            // would push a stone outside. eps absorbs grid quantisation (one cell).
            if (outsideCount == 0)
                emit($"  AABB-in-box  : PASS (all {placements.Count} placed stones inside the V2 container, eps {eps:F4})");
            else
                emit($"  AABB-in-box  : FAIL ({outsideCount} stones outside, worst overhang {worstOverhang:F4}) -- transform compose is wrong");
            if (failures.Count > 0)
                emit($"  unplaced     : {failures.Count} stone(s) had no fitting (orientation, anchor) under the lid.");

            WritePlacedPack3d(containerMesh, placedMeshes, emit);

            emit(new string('-', 64));
            emit("GOLDEN   : Python V2 (eth1100_pack3d.py) on 30 ETH stones in 3.5x3.5x3.0 = 30/30 placed, used H 2.22, compactness 32.0%.");
            return 0;
        }

        // Minimal Wavefront .obj reader -> RhinoCommon Mesh. Reads `v x y z` vertex
        // lines and `f ...` triangle/polygon faces (1-based, supports a/b/c, a//c,
        // a/b/c index forms; takes the vertex index, ignores normal / uv indices).
        // Fans polygons into triangles. Sufficient for the ETH closed stone meshes
        // (pure v + vn + triangle f). Returns null on read failure.
        private static Mesh? ReadObjMesh(string path)
        {
            Mesh mesh;
            try
            {
                mesh = new Mesh();
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length < 2) continue;
                    if (line[0] == 'v' && line[1] == ' ')
                    {
                        var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length >= 4
                            && double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                            && double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                            && double.TryParse(t[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                        {
                            mesh.Vertices.Add(x, y, z);
                        }
                    }
                    else if (line[0] == 'f' && line[1] == ' ')
                    {
                        var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        var idx = new List<int>(t.Length - 1);
                        for (int i = 1; i < t.Length; i++)
                        {
                            // token may be "v", "v/vt", "v//vn", or "v/vt/vn".
                            string tok = t[i];
                            int slash = tok.IndexOf('/');
                            string vstr = slash < 0 ? tok : tok.Substring(0, slash);
                            if (int.TryParse(vstr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vi))
                            {
                                // 1-based; negative = relative to current vertex count.
                                if (vi < 0) vi = mesh.Vertices.Count + vi; else vi -= 1;
                                idx.Add(vi);
                            }
                        }
                        // Fan-triangulate.
                        for (int i = 1; i + 1 < idx.Count; i++)
                            mesh.Faces.AddFace(idx[0], idx[i], idx[i + 1]);
                    }
                }
            }
            catch { return null; }

            if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0) return null;
            mesh.Vertices.CullUnused();
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        // RhinoCommon Mesh -> Core MeshPackItem, the byte-for-byte conversion
        // Pack3DIrregularContainerComponent.CreateMeshPackItem performs: triangulate,
        // weld + cull, translate verts to bbox-local (Vec3), emit MeshTriangle faces.
        // Outputs the source bbox-min so the caller can compose the placement transform.
        private static MeshPackItem? MeshToPackItem(string id, Mesh mesh, out Point3d sourceMin)
        {
            sourceMin = Point3d.Origin;
            var sourceBounds = mesh.GetBoundingBox(true);
            if (!sourceBounds.IsValid) return null;

            var core = mesh.DuplicateMesh();
            core.Faces.ConvertQuadsToTriangles();
            core.Vertices.CombineIdentical(true, true);
            core.Vertices.CullUnused();
            core.Compact();
            if (core.Vertices.Count == 0 || core.Faces.Count == 0) return null;

            sourceMin = sourceBounds.Min;
            var verts = new List<Vec3>(core.Vertices.Count);
            foreach (var v in core.Vertices)
                verts.Add(new Vec3(v.X - sourceMin.X, v.Y - sourceMin.Y, v.Z - sourceMin.Z));

            var tris = new List<MeshTriangle>(core.Faces.Count);
            foreach (var face in core.Faces)
            {
                if (face.IsTriangle) tris.Add(new MeshTriangle(face.A, face.B, face.C));
                else if (face.IsQuad)
                {
                    tris.Add(new MeshTriangle(face.A, face.B, face.C));
                    tris.Add(new MeshTriangle(face.A, face.C, face.D));
                }
            }
            if (tris.Count == 0) return null;
            return new MeshPackItem(id, verts, tris);
        }

        // Compose the world placement transform from a MeshPackPlacement, mirroring
        // Pack3DIrregularContainerComponent.BuildTransform exactly: move to origin,
        // yaw, normalize to the oriented bounds min, then translate to the container-
        // relative geometry origin.
        private static Transform BuildPack3DTransform(MeshPackPlacement placement, Point3d sourceMin, Point3d containerMin)
        {
            var sourceToOrigin = Transform.Translation(-sourceMin.X, -sourceMin.Y, -sourceMin.Z);
            var downRot = DownRotation(placement.DownAxis);
            var yawRot = Transform.Rotation(Rhino.RhinoMath.ToRadians(placement.YawDegrees), Vector3d.ZAxis, Point3d.Origin);
            var rotation = yawRot * downRot; // yaw AFTER down, matching Core's Rdown-then-yaw order
            var normalize = Transform.Translation(
                -placement.OrientationBoundsMin.X,
                -placement.OrientationBoundsMin.Y,
                -placement.OrientationBoundsMin.Z);
            var target = Transform.Translation(
                containerMin.X + placement.GeometryOrigin.X,
                containerMin.Y + placement.GeometryOrigin.Y,
                containerMin.Z + placement.GeometryOrigin.Z);
            return target * normalize * rotation * sourceToOrigin;
        }

        // Down rotation tilts a local axis to -Z, applied BEFORE the yaw rotation.
        // 0 = None (identity), 1 = X down (about +Y +90deg), 2 = Y down (about +X -90deg).
        private static Transform DownRotation(int downAxis)
        {
            switch (downAxis)
            {
                case 1:
                    return Transform.Rotation(Math.PI / 2.0, Vector3d.YAxis, Point3d.Origin);
                case 2:
                    return Transform.Rotation(-Math.PI / 2.0, Vector3d.XAxis, Point3d.Origin);
                default:
                    return Transform.Identity;
            }
        }

        // PCA-align a mesh exactly as the validated Python demo eth1100_pack3d.py
        // pca_orient() does:
        //   V = vertices - mean(vertices)
        //   w, vec = eigh(cov(V.T))           # ascending eigenvalues, eigenvectors as columns
        //   axes = vec[:, argsort(w)[::-1]]   # columns reordered: largest, mid, smallest
        //   if det(axes) < 0: axes[:,2] = -axes[:,2]   # right-handed (negate smallest axis)
        //   out = V @ axes                    # new coord j = (v-centroid) . axes[:,j]
        // i.e. axisX = eigenvector of the LARGEST eigenvalue (broadest extent) -> X,
        //      axisY = mid -> Y, axisZ = smallest -> Z. The new vertex is the projection
        //      of (v - centroid) onto (axisX, axisY, axisZ). Normals recomputed. Returns
        //      null only if the mesh has no vertices.
        private static Mesh? PcaAlignMesh(Mesh mesh)
        {
            if (mesh == null) return null;
            int n = mesh.Vertices.Count;
            if (n == 0) return null;

            // Centroid of vertices.
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = mesh.Vertices[i];
                cx += v.X; cy += v.Y; cz += v.Z;
            }
            cx /= n; cy /= n; cz /= n;

            // 3x3 symmetric covariance of (v - centroid). Population covariance (divide by
            // n) matches numpy.cov default only up to a positive scale (numpy uses n-1);
            // eigenVECTORS are invariant to that scale, so the frame is identical.
            double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = mesh.Vertices[i];
                double dx = v.X - cx, dy = v.Y - cy, dz = v.Z - cz;
                sxx += dx * dx; syy += dy * dy; szz += dz * dz;
                sxy += dx * dy; sxz += dx * dz; syz += dy * dz;
            }
            double inv = 1.0 / n;
            var cov = new double[3, 3]
            {
                { sxx * inv, sxy * inv, sxz * inv },
                { sxy * inv, syy * inv, syz * inv },
                { sxz * inv, syz * inv, szz * inv },
            };

            // Symmetric eigendecomposition. eigVals[k] paired with column k of eigVecs.
            JacobiEigen3x3(cov, out double[] eigVals, out double[,] eigVecs);

            // Sort eigenvalues DESCENDING; build the orthonormal frame.
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => eigVals[b].CompareTo(eigVals[a]));
            var axisX = new Vector3d(eigVecs[0, order[0]], eigVecs[1, order[0]], eigVecs[2, order[0]]);
            var axisY = new Vector3d(eigVecs[0, order[1]], eigVecs[1, order[1]], eigVecs[2, order[1]]);
            var axisZ = new Vector3d(eigVecs[0, order[2]], eigVecs[1, order[2]], eigVecs[2, order[2]]);
            axisX.Unitize(); axisY.Unitize(); axisZ.Unitize();
            // Right-handed: if Cross(axisX, axisY) . axisZ < 0, negate the smallest axis,
            // exactly as the Python det(axes) < 0 guard (det < 0 -> negate column 2).
            if (Vector3d.CrossProduct(axisX, axisY) * axisZ < 0)
                axisZ = -axisZ;

            // Project each (v - centroid) onto the principal frame to build the aligned mesh.
            var outMesh = new Mesh();
            for (int i = 0; i < n; i++)
            {
                var v = mesh.Vertices[i];
                double dx = v.X - cx, dy = v.Y - cy, dz = v.Z - cz;
                double nx = dx * axisX.X + dy * axisX.Y + dz * axisX.Z;
                double ny = dx * axisY.X + dy * axisY.Y + dz * axisY.Z;
                double nz = dx * axisZ.X + dy * axisZ.Y + dz * axisZ.Z;
                outMesh.Vertices.Add(nx, ny, nz);
            }
            foreach (var f in mesh.Faces)
            {
                if (f.IsQuad) outMesh.Faces.AddFace(f.A, f.B, f.C, f.D);
                else outMesh.Faces.AddFace(f.A, f.B, f.C);
            }
            outMesh.Normals.ComputeNormals();
            outMesh.Compact();
            return outMesh.IsValid && outMesh.Vertices.Count > 0 && outMesh.Faces.Count > 0 ? outMesh : null;
        }

        // Classic cyclic Jacobi eigenvalue algorithm for a 3x3 SYMMETRIC matrix.
        // Returns eigenvalues in vals[0..2] and the corresponding eigenvectors as the
        // COLUMNS of vecs (vecs[row, k] is component `row` of eigenvector `k`). The input
        // matrix is treated read-only (a working copy is rotated). Converges in a handful
        // of sweeps for 3x3; capped at 50 sweeps as a safety net.
        private static void JacobiEigen3x3(double[,] m, out double[] vals, out double[,] vecs)
        {
            // Working copy of the symmetric matrix.
            double a00 = m[0, 0], a01 = m[0, 1], a02 = m[0, 2];
            double a11 = m[1, 1], a12 = m[1, 2], a22 = m[2, 2];
            // Eigenvector accumulator (starts as identity).
            double v00 = 1, v01 = 0, v02 = 0;
            double v10 = 0, v11 = 1, v12 = 0;
            double v20 = 0, v21 = 0, v22 = 1;

            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(a01) + Math.Abs(a02) + Math.Abs(a12);
                if (off < 1e-18) break;

                // Rotate to zero each off-diagonal in turn: (0,1), (0,2), (1,2).
                for (int pq = 0; pq < 3; pq++)
                {
                    int p, q; double apq, app, aqq;
                    if (pq == 0) { p = 0; q = 1; apq = a01; app = a00; aqq = a11; }
                    else if (pq == 1) { p = 0; q = 2; apq = a02; app = a00; aqq = a22; }
                    else { p = 1; q = 2; apq = a12; app = a11; aqq = a22; }
                    if (Math.Abs(apq) < 1e-300) continue;

                    // Jacobi rotation angle.
                    double phi = 0.5 * Math.Atan2(2.0 * apq, aqq - app);
                    double c = Math.Cos(phi), s = Math.Sin(phi);

                    // Apply the rotation J^T A J for the active (p,q) plane. Recompute the
                    // full 3x3 from the current entries (3x3 is tiny; clarity over speed).
                    double[,] A =
                    {
                        { a00, a01, a02 },
                        { a01, a11, a12 },
                        { a02, a12, a22 },
                    };
                    double[,] B = (double[,])A.Clone();
                    // Rows/cols p,q updated.
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = A[k, p], akq = A[k, q];
                        B[k, p] = c * akp - s * akq;
                        B[k, q] = s * akp + c * akq;
                    }
                    double[,] C = (double[,])B.Clone();
                    for (int k = 0; k < 3; k++)
                    {
                        double bpk = B[p, k], bqk = B[q, k];
                        C[p, k] = c * bpk - s * bqk;
                        C[q, k] = s * bpk + c * bqk;
                    }
                    a00 = C[0, 0]; a01 = C[0, 1]; a02 = C[0, 2];
                    a11 = C[1, 1]; a12 = C[1, 2]; a22 = C[2, 2];

                    // Accumulate eigenvectors: V <- V * J.
                    double[,] V =
                    {
                        { v00, v01, v02 },
                        { v10, v11, v12 },
                        { v20, v21, v22 },
                    };
                    double[,] VN = (double[,])V.Clone();
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = V[k, p], vkq = V[k, q];
                        VN[k, p] = c * vkp - s * vkq;
                        VN[k, q] = s * vkp + c * vkq;
                    }
                    v00 = VN[0, 0]; v01 = VN[0, 1]; v02 = VN[0, 2];
                    v10 = VN[1, 0]; v11 = VN[1, 1]; v12 = VN[1, 2];
                    v20 = VN[2, 0]; v21 = VN[2, 1]; v22 = VN[2, 2];
                }
            }

            vals = new[] { a00, a11, a22 };
            vecs = new double[,]
            {
                { v00, v01, v02 },
                { v10, v11, v12 },
                { v20, v21, v22 },
            };
        }

        private static void WritePlacedPack3d(Mesh containerMesh, List<Mesh> placed, Action<string> emit)
        {
            try
            {
                var outDoc = new File3dm();
                int containerLayer = AddLayer(outDoc, "Container", System.Drawing.Color.Gray);
                int stoneLayer = AddLayer(outDoc, "PackedStones", System.Drawing.Color.SandyBrown);
                if (containerMesh != null) outDoc.Objects.AddMesh(containerMesh, AttrFor(containerLayer));
                foreach (var m in placed) if (m != null) outDoc.Objects.AddMesh(m, AttrFor(stoneLayer));
                string outPath = Pack3dOutPath;
                Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
                outDoc.Write(outPath, 6);
                emit("  placed   : wrote " + outPath);
            }
            catch (Exception ex) { emit("  WARN: could not write pack3d placed .3dm: " + ex.Message); }
        }

        private static AshlarPackOptions MakeOptions(
            CourseMode mode, double wallW, double wallH, double wallThk, double courseH)
        {
            // bed/head joints 0 (dry stone), stagger 0.5 (running bond), density 1,
            // height tolerance = courseH (rubble: wide bins). All > 0 where required.
            return new AshlarPackOptions(
                mode, wallW, wallH, wallThk, courseH,
                bedJoint: 0.0, headJoint: 0.0, staggerOffset: 0.5,
                density: 1.0, heightTolerance: courseH);
        }

        private static void RunOnePacker(
            Action<string> emit, string label, Func<AshlarPackResult> run,
            int inventoryCount, double sumStoneVol,
            double wallW, double wallH, double wallThk,
            string fixturePath, string tag, bool writeMeshes)
        {
            emit(new string('-', 64));
            emit(label + ":");
            AshlarPackResult res;
            try { res = run(); }
            catch (Exception ex) { emit("  ERROR: packer threw: " + ex.GetType().Name + ": " + ex.Message); return; }

            int placed = res.PlacedBlocks.Count;
            emit($"  placed blocks        : {placed} of {inventoryCount}  ({res.Leftovers.Count} leftover)");
            emit($"  courses              : {res.CourseCount}");
            emit($"  area coverage        : {res.CoverageRatio * 100.0:F2}% (packer CoverageRatio = sum block face area / wall face area)");

            // Volume fill: placed-block volume / wall volume (the 3D analogue of the
            // ~32% Python fill). MasonryBlock volume via signed tetrahedra.
            double placedVol = 0.0;
            foreach (var b in res.PlacedBlocks) placedVol += Math.Abs(BlockVolume(b));
            double wallVol = wallW * wallH * wallThk;
            emit($"  volume fill          : {(wallVol > 1e-9 ? (placedVol / wallVol * 100.0) : 0.0):F2}% " +
                 $"(placed block volume {placedVol:F4} / wall volume {wallVol:F4})");
            if (res.Notes.Count > 0)
                emit($"  packer notes         : {res.Notes.Count} (e.g. \"{res.Notes[0]}\")");

            if (writeMeshes)
                WritePlacedRubble3dm(fixturePath, res, wallW, wallH, wallThk, emit);
        }

        private static double SlabExtentX(Slab s) => SlabExtent(s, 0);
        private static double SlabExtentY(Slab s) => SlabExtent(s, 1);
        private static double SlabExtentZ(Slab s) => SlabExtent(s, 2);
        private static double SlabExtent(Slab s, int axis)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            var v = s.VertexCoordsXyz;
            for (int i = 0; i < s.VertexCount; i++)
            {
                double x = v[3 * i + axis];
                if (x < lo) lo = x; if (x > hi) hi = x;
            }
            return hi - lo;
        }

        private static double MedianHeight(List<Slab> slabs)
        {
            var hs = slabs.Select(SlabExtentZ).Where(h => h > 1e-9).OrderBy(h => h).ToList();
            if (hs.Count == 0) return 1.0;
            return hs[(hs.Count - 1) / 2];
        }

        // MasonryBlock volume via origin-tetrahedra over its triangles.
        private static double BlockVolume(Frahan.Masonry.DataModel.MasonryBlock b)
        {
            var v = b.VertexCoordsXyz;
            var t = b.TriangleIndices;
            double total = 0.0;
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                int i0 = t[i], i1 = t[i + 1], i2 = t[i + 2];
                double ax = v[3 * i0], ay = v[3 * i0 + 1], az = v[3 * i0 + 2];
                double bx = v[3 * i1], by = v[3 * i1 + 1], bz = v[3 * i1 + 2];
                double cx = v[3 * i2], cy = v[3 * i2 + 1], cz = v[3 * i2 + 2];
                double crossx = by * cz - bz * cy;
                double crossy = bz * cx - bx * cz;
                double crossz = bx * cy - by * cx;
                total += ax * crossx + ay * crossy + az * crossz;
            }
            return total / 6.0;
        }

        // ====================================================================
        // Placed-output .3dm writers (--nfp / --rubble)
        // ====================================================================

        private static void WritePlacedNfp3dm(
            string fixturePath, Curve sheet, List<PolylineCurve> placed, Action<string> emit)
        {
            try
            {
                var outDoc = new File3dm();
                int sheetLayer = AddLayer(outDoc, "Sheet", System.Drawing.Color.Gray);
                int placedLayer = AddLayer(outDoc, "PackedFootprints", System.Drawing.Color.SeaGreen);
                if (sheet != null)
                    outDoc.Objects.AddCurve(sheet, AttrFor(sheetLayer));
                foreach (var c in placed) outDoc.Objects.AddCurve(c, AttrFor(placedLayer));
                string outPath = Path.Combine(Path.GetDirectoryName(fixturePath) ?? ".",
                    Path.GetFileNameWithoutExtension(fixturePath) + "_nfp_packed.3dm");
                outDoc.Write(outPath, 6);
                emit("placed   : wrote " + outPath);
            }
            catch (Exception ex) { emit("WARN: could not write NFP placed .3dm: " + ex.Message); }
        }

        private static void WritePlacedRubble3dm(
            string fixturePath, AshlarPackResult res,
            double wallW, double wallH, double wallThk, Action<string> emit)
        {
            try
            {
                var outDoc = new File3dm();
                int wallLayer = AddLayer(outDoc, "Wall", System.Drawing.Color.Gray);
                int blockLayer = AddLayer(outDoc, "PlacedStones", System.Drawing.Color.SandyBrown);
                // Wall outline (XZ face at y=0).
                var wall = new Polyline
                {
                    new Point3d(0, 0, 0), new Point3d(wallW, 0, 0),
                    new Point3d(wallW, 0, wallH), new Point3d(0, 0, wallH), new Point3d(0, 0, 0),
                }.ToPolylineCurve();
                outDoc.Objects.AddCurve(wall, AttrFor(wallLayer));
                foreach (var b in res.PlacedBlocks)
                {
                    var mesh = BlockToMesh(b);
                    if (mesh != null) outDoc.Objects.AddMesh(mesh, AttrFor(blockLayer));
                }
                string outPath = Path.Combine(Path.GetDirectoryName(fixturePath) ?? ".",
                    Path.GetFileNameWithoutExtension(fixturePath) + "_rubble_packed.3dm");
                outDoc.Write(outPath, 6);
                emit("  placed   : wrote " + outPath);
            }
            catch (Exception ex) { emit("  WARN: could not write rubble placed .3dm: " + ex.Message); }
        }

        private static Mesh? BlockToMesh(Frahan.Masonry.DataModel.MasonryBlock b)
        {
            var m = new Mesh();
            var v = b.VertexCoordsXyz;
            for (int i = 0; i < b.VertexCount; i++)
                m.Vertices.Add(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
            var t = b.TriangleIndices;
            for (int i = 0; i + 2 < t.Count; i += 3)
                m.Faces.AddFace(t[i], t[i + 1], t[i + 2]);
            m.Normals.ComputeNormals();
            m.Compact();
            return m;
        }

        private static int AddLayer(File3dm doc, string name, System.Drawing.Color color)
        {
            var layer = new Rhino.DocObjects.Layer { Name = name, Color = color };
            doc.AllLayers.Add(layer);
            // File3dmLayerTable exposes no indexer; find the layer just added by
            // name and return its assigned Index.
            foreach (var l in doc.AllLayers)
                if (l != null && string.Equals(l.Name, name, StringComparison.Ordinal))
                    return l.Index;
            return 0;
        }

        private static Rhino.DocObjects.ObjectAttributes AttrFor(int layerIndex)
        {
            return new Rhino.DocObjects.ObjectAttributes { LayerIndex = layerIndex };
        }

        // ====================================================================
        // 2.5D per-facet PROJECTION BOOTSTRAP (--project3d)
        // ====================================================================

        // Projects each naked rim into its facet plane, matches the projected rims
        // with the WORKING 2D path (ProjectionPairFinder), lifts each match to a 3D
        // relative pose, feeds those candidate edges to the agglomerative solver
        // (which builds the pair graph the empty 3D hash could not), then refines
        // the placed fragments with Soft-ICP (rim contact + non-penetration) and
        // reports the targets: (a) cross-panel 2D matches > 0; (b) fragments placed
        // > 0; (c) rim-gap small + penetration ~0 after refine; plus per-rim
        // planarity residuals.
        private static int RunProjectionBootstrap3D(
            HarnessOptions opts, List<Mesh> shardMeshes,
            List<List<PolylineCurve>> rimsPerFragmentList, Action<string> emit)
        {
            emit(new string('-', 64));
            emit("PROJECTION BOOTSTRAP (2.5D per-facet -> 2D match -> 3D lift):");

            // Fragment id <-> index. Rim loops keyed by fragment id (id-sorted in
            // the finder). Fragment 0 is the anchor (seed at identity), mirroring
            // the Kintsugi / Run3D convention.
            int nFrag = shardMeshes.Count;
            var rimsByFrag = new Dictionary<string, List<PolylineCurve>>(StringComparer.Ordinal);
            var fragIdOf = new string[nFrag];
            for (int f = 0; f < nFrag; f++)
            {
                string id = $"frag{f:D3}";
                fragIdOf[f] = id;
                rimsByFrag[id] = rimsPerFragmentList[f];
            }

            var asmOpt = new AssemblyOptions
            {
                Mode = AssemblyMode.Agglomerative,           // --project3d implies agglomerative
                ProjectionBootstrap = true,
                ResidualThreshold = JointWidth3D,
                NonCrossingCorrespondence = opts.NonCrossing,
                ResidualThresholdFactor = opts.AutoScale,
                PhaseScoreThreshold = opts.AutoScale > 0 ? 0.35 : 0.5,
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
            };
            // Projected-rim 2D segmenter knobs: scale-relative resample inside the
            // finder, so a fixed small SampleSpacing/MinSegmentLength here just
            // controls the segment break detection on the (already resampled) 2D rim.
            var projSegOpt = new SegmenterOptions
            {
                SampleSpacing = SampleSpacing,
                BreakAngleDeg = BreakAngleDeg3D,
                MinSegmentLength = MinSegmentLength3D,
                EmitPartials = opts.Partial,
                PartialFractions = opts.Partial ? PartialFractions : new double[0],
                PartialStrideFraction = PartialStride,
            };

            var find = ProjectionPairFinder.FindPairs(rimsByFrag, asmOpt, projSegOpt);

            // Per-rim planarity residual report (confirms the rims are near-planar).
            emit($"  projected rims       : {find.Rims.Count}");
            int lowPlanar = 0;
            double maxResAbs = 0, maxResRel = 0;
            foreach (var r in find.Rims)
            {
                double rel = r.LoopScale > 1e-9 ? r.PlanarityResidual / r.LoopScale : 0.0;
                if (r.PlanarityResidual > maxResAbs) maxResAbs = r.PlanarityResidual;
                if (rel > maxResRel) maxResRel = rel;
                if (r.LowPlanarity) lowPlanar++;
                emit($"    {r.PanelId,-12} planarity rms {r.PlanarityResidual:F4} " +
                     $"({rel * 100.0:F2}% of loop diag {r.LoopScale:F2})" +
                     (r.LowPlanarity ? "  [LOW-PLANARITY: excluded]" : ""));
            }
            emit($"  planarity (worst)    : abs {maxResAbs:F4}  rel {maxResRel * 100.0:F2}%  low-planarity loops: {lowPlanar}");

            // TARGET (a): cross-panel 2D matches found.
            emit($"  cross-panel 2D matches: {find.CrossPanelMatches}  (R0 had 0)");
            emit($"  lifted+3D-verified pairs: {find.Pairs.Count} (false positives dropped by the 3D gate)");
            emit("  best 3D-verify residual per fragment pair (lower = truer mating):");
            foreach (var kv in find.BestFragPairResidual.OrderBy(k => k.Value))
                emit($"    {kv.Key,-18} : {kv.Value:F4}");

            if (find.Pairs.Count == 0)
            {
                emit("  no lifted candidate pairs; cannot bootstrap. Stage = 2D MATCH (no");
                emit("  cross-fragment projected-rim match passed the gates).");
                return 0;
            }

            // --- Build one representative Panel per fragment (id = fragment id).
            // FREE reassembly: NO forced anchor. The agglomerative solver seeds the
            // globally-best edge's lower-id endpoint at identity and grows the MST,
            // so the whole connected component is placed in a common (arbitrary)
            // frame -- correct for projection bootstrap where fragment 0 may be a
            // poorly-connected node. (Forcing fragment 0 anchored places nothing
            // when fragment 0's best lifted edge is above the verify gate.) Map each
            // lifted CandidatePair to a MatchResult candidate edge (A.PanelId =
            // child fragment, B.PanelId = parent fragment, AontoB = relative pose). ---
            var fragPanels = new List<Panel>();
            var fragPanelById = new Dictionary<string, Panel>(StringComparer.Ordinal);
            for (int f = 0; f < nFrag; f++)
            {
                // Use the fragment's first rim loop as the representative contour
                // (identity/scale only; the solver composes poses, not geometry).
                var loops = rimsPerFragmentList[f];
                if (loops == null || loops.Count == 0) continue;
                var contour = (PolylineCurve)loops[0].DuplicateCurve();
                Panel panel;
                try { panel = new Panel(fragIdOf[f], contour, PanelKind.Shard, planarityTolerance: double.PositiveInfinity); }
                catch { continue; }
                fragPanels.Add(panel);
                fragPanelById[fragIdOf[f]] = panel;
            }
            var anchorFrag = new List<Panel>();          // no forced anchor
            var poolFrag = fragPanels;

            var candidateEdges = new List<MatchResult>();
            foreach (var pr in find.Pairs)
            {
                if (!fragPanelById.ContainsKey(pr.ChildFragmentId) ||
                    !fragPanelById.ContainsKey(pr.ParentFragmentId)) continue;
                candidateEdges.Add(StubMatch(pr.ChildFragmentId, pr.ParentFragmentId, pr.Relative, pr.Residual));
            }

            // The solver needs a SegmentHashIndex even though the candidate-edge
            // path does not consult it; pass an empty one.
            var solver = new AssemblySolver(new SegmentHashIndex(), asmOpt, projSegOpt);
            var state = solver.Solve(anchorFrag, poolFrag, candidateEdges);

            // Per-fragment absolute pose from the solved state.
            var fragmentTransform = new Transform[nFrag];
            var placedMask = new bool[nFrag];
            for (int f = 0; f < nFrag; f++) fragmentTransform[f] = Transform.Identity;
            foreach (var panel in state.PlacedPanels)
            {
                int idx = Array.IndexOf(fragIdOf, panel.Id);
                if (idx < 0) continue;
                fragmentTransform[idx] = state.AppliedTransforms.TryGetValue(panel.Id, out var xf)
                    ? xf : Transform.Identity;
                placedMask[idx] = true;
            }

            int placedTotal = placedMask.Count(b => b);
            // The MST seed is placed at identity for "free"; the remaining placed
            // fragments are the ones actually positioned by a lifted edge.
            int placedByEdge = Math.Max(0, placedTotal - 1);
            emit(new string('-', 64));
            // TARGET (b): fragments placed.
            emit($"  solve (agglomerative): placed {placedTotal} of {nFrag} fragments " +
                 $"(seed + {placedByEdge} positioned by lifted edges, {state.History.Count} MST edges)");
            emit($"  total residual       : {state.TotalResidual:F4}");

            // --- Materialise placed meshes at the composed poses. ---
            var placedMeshes = new List<Mesh>();
            var placedFragIdx = new List<int>();
            for (int f = 0; f < nFrag; f++)
            {
                if (!placedMask[f]) continue;
                var m = shardMeshes[f].DuplicateMesh();
                m.Transform(fragmentTransform[f]);
                placedMeshes.Add(m);
                placedFragIdx.Add(f);
            }

            // --- TARGET (c): Soft-ICP refine of the placed fragments (rim contact +
            // non-penetration), report rim-gap + penetration before/after. Drop /
            // flag pairs whose post-refine penetration stays high. The refiner rims
            // are the MATCHED facet arcs (the actual mating interfaces) at the solved
            // pose, NOT the full naked loops -- the bulk of a loop is the outer block
            // surface that mates with nothing and would otherwise drown the contact
            // term and let the penetration term eject the pieces. ---
            RunProjectionSoftIcpRefine(find, state, fragIdOf, placedMeshes, placedFragIdx,
                fragmentTransform, emit);

            return 0;
        }

        // Refine the placed fragments with Soft-ICP: rim samples = each placed
        // fragment's MATCHED facet-arc points (the mating interfaces) at the solved
        // pose; solid = FillHoles-closed placed mesh for the non-penetration inside-
        // test; the MST seed (first placed) is anchored. Reports rim-gap + max
        // penetration before vs after (target c).
        private static void RunProjectionSoftIcpRefine(
            ProjectionPairFinder.Result find, AssemblyState state, string[] fragIdOf,
            List<Mesh> placedMeshes, List<int> placedFragIdx, Transform[] fragmentTransform,
            Action<string> emit)
        {
            emit(new string('-', 64));
            emit("SOFT-ICP refine of placed fragments (rim contact + non-penetration):");
            if (placedMeshes.Count < 2)
            {
                emit("  fewer than 2 placed fragments; nothing to refine.");
                return;
            }

            // Collect the matched facet-arc world points per fragment id (the arcs
            // that participated in a verified lifted pair). Use the ProjectedRim
            // WorldArc (in the SCATTERED frame) transformed by the fragment's solved
            // pose -> the arc at the assembled pose.
            var arcByPanel = new Dictionary<string, ProjectionPairFinder.ProjectedRim>(StringComparer.Ordinal);
            foreach (var r in find.Rims) arcByPanel[r.PanelId] = r;
            var matchedArcPanels = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void AddArc(string fragId, string panelId)
            {
                if (!matchedArcPanels.TryGetValue(fragId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    matchedArcPanels[fragId] = set;
                }
                set.Add(panelId);
            }
            foreach (var pr in find.Pairs)
            {
                AddArc(pr.ChildFragmentId, pr.ChildRimPanelId);
                AddArc(pr.ParentFragmentId, pr.ParentRimPanelId);
            }

            // Build per-fragment matched-arc rim points (at the solved pose) + a
            // closed solid for the penetration test.
            var rimPtsByFrag = new Dictionary<string, Point3d[]>(StringComparer.Ordinal);
            var solidByFrag = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            for (int k = 0; k < placedMeshes.Count; k++)
            {
                int f = placedFragIdx[k];
                string fragId = fragIdOf[f];
                var rimPts = new List<Point3d>();
                if (matchedArcPanels.TryGetValue(fragId, out var panels))
                {
                    foreach (var pid in panels)
                    {
                        if (!arcByPanel.TryGetValue(pid, out var rim)) continue;
                        foreach (var p0 in rim.WorldArc)
                        {
                            var p = p0; p.Transform(fragmentTransform[f]); rimPts.Add(p);
                        }
                    }
                }
                rimPtsByFrag[fragId] = rimPts.ToArray();
                solidByFrag[fragId] = ClosedCopy(placedMeshes[k]);
            }

            var beforeAll = MeasureGlobalRimContact(rimPtsByFrag, solidByFrag);

            // World arcs at the solved (assembled) pose, keyed by arc panel id.
            var arcWorld = new Dictionary<string, Point3d[]>(StringComparer.Ordinal);
            foreach (var r in find.Rims)
            {
                int fi = Array.IndexOf(fragIdOf, r.FragmentId);
                if (fi < 0) continue;
                var pts = new Point3d[r.WorldArc.Length];
                for (int i = 0; i < r.WorldArc.Length; i++)
                {
                    var p = r.WorldArc[i]; p.Transform(fragmentTransform[fi]); pts[i] = p;
                }
                arcWorld[r.PanelId] = pts;
            }

            // PER-MST-EDGE quality + pairwise Soft-ICP refine (the refiner's
            // VALIDATED regime: one moving child vs one anchored parent, using only
            // the two SPECIFIC mating facet arcs; a simultaneous all-N refine is
            // ill-posed on a fully-surrounded fragment whose arcs sit near several
            // neighbours at once and diverges). For each tree edge report the stored
            // 3D-verified residual (the lift quality), the assembled mating-arc gap,
            // and the post-refine gap; flag interfaces that stay loose. This realises
            // "projection PROPOSES, 3D DISPOSES": a loose post-refine interface marks
            // a projection-ambiguous / MST-drifted edge to drop or re-resolve.
            var treeEdges = MstTreeEdges(state, fragIdOf);
            emit($"  matched-arc rim pts  : {rimPtsByFrag.Values.Sum(a => a.Length)} across {rimPtsByFrag.Count} fragments");
            emit($"  GLOBAL rim gap (assembled, matched arcs): {beforeAll.gap:F4} (object scale)");
            emit($"  GLOBAL max penetration (assembled)      : {beforeAll.pen:F4}");
            emit($"  per-MST-edge interface (stored 3D residual = lift quality; assembled gap; pairwise Soft-ICP):");
            int refinedEdges = 0, tightInterfaces = 0;
            double scaleObj = beforeAll.scale;
            double tightGate = 0.06 * scaleObj; // 6% of object scale = "in contact"
            foreach (var te in treeEdges)
            {
                var pr = find.Pairs.FirstOrDefault(p =>
                    (p.ChildFragmentId == te.child && p.ParentFragmentId == te.parent) ||
                    (p.ChildFragmentId == te.parent && p.ParentFragmentId == te.child));
                if (pr == null) continue;
                if (!arcWorld.TryGetValue(pr.ChildRimPanelId, out var childArc)) continue;
                if (!arcWorld.TryGetValue(pr.ParentRimPanelId, out var parentArc)) continue;
                refinedEdges++;

                double assembledGap = DirectArcGap(childArc, parentArc);
                var pairFrags = new List<SoftIcpRefiner.Fragment>
                {
                    new SoftIcpRefiner.Fragment(pr.ParentRimPanelId, parentArc,
                        solid: solidByFrag.TryGetValue(pr.ParentFragmentId, out var ps) ? ps : null,
                        contour2D: null, anchored: true),
                    new SoftIcpRefiner.Fragment(pr.ChildRimPanelId, childArc,
                        solid: solidByFrag.TryGetValue(pr.ChildFragmentId, out var cs) ? cs : null,
                        contour2D: null, anchored: false),
                };
                var post = SoftIcpRefiner.Refine3D(pairFrags, new SoftIcpOptions());
                bool tight = assembledGap <= tightGate;
                if (tight) tightInterfaces++;
                emit($"    {pr.ChildFragmentId}<->{pr.ParentFragmentId}: lift {pr.Residual:F2}  assembled {assembledGap:F2}  " +
                     $"post-refine {post.MeanRimGap:F2}/pen {post.MaxPenetration:F2}  {(tight ? "[contact]" : "[loose -> drop/re-resolve]")}");
            }
            emit($"  tight interfaces (assembled gap <= {tightGate:F2}): {tightInterfaces} of {refinedEdges} MST edges");
        }

        // Mean symmetric nearest-neighbour distance between two arcs (direct gap).
        private static double DirectArcGap(Point3d[] a, Point3d[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return double.NaN;
            double s = 0;
            foreach (var p in a)
            {
                double best = double.PositiveInfinity;
                foreach (var q in b) { double d = p.DistanceToSquared(q); if (d < best) best = d; }
                s += Math.Sqrt(best);
            }
            return s / a.Length;
        }

        // The MST tree edges actually used to place fragments: each placement event
        // in state.History is the candidate MatchResult whose A/B PanelId are the
        // (child, parent) FRAGMENT ids of that edge.
        private static List<(string child, string parent)> MstTreeEdges(
            AssemblyState state, string[] fragIdOf)
        {
            var edges = new List<(string, string)>();
            foreach (var m in state.History)
            {
                if (m?.A == null || m.B == null) continue;
                edges.Add((m.A.PanelId, m.B.PanelId));
            }
            return edges;
        }

        // Global rim contact / penetration of the assembled fragments at their solved
        // poses (matched-arc rims). gap = mean nearest-neighbour distance from each
        // fragment's matched arc to ANY other fragment's matched arc; pen = deepest
        // matched-arc point inside another fragment's closed solid.
        private static (double gap, double pen, double scale) MeasureGlobalRimContact(
            Dictionary<string, Point3d[]> rimPtsByFrag, Dictionary<string, Mesh> solidByFrag)
        {
            var ids = rimPtsByFrag.Keys.ToList();
            var box = BoundingBox.Unset;
            foreach (var arr in rimPtsByFrag.Values) foreach (var p in arr) box.Union(p);
            double scale = box.IsValid && box.Diagonal.Length > 1e-9 ? box.Diagonal.Length : 1.0;
            double gapSum = 0; int gapCount = 0;
            for (int a = 0; a < ids.Count; a++)
            {
                var pa = rimPtsByFrag[ids[a]];
                foreach (var p in pa)
                {
                    double best = double.PositiveInfinity;
                    for (int b = 0; b < ids.Count; b++)
                    {
                        if (b == a) continue;
                        foreach (var q in rimPtsByFrag[ids[b]])
                        {
                            double d = p.DistanceToSquared(q);
                            if (d < best) best = d;
                        }
                    }
                    if (!double.IsPositiveInfinity(best)) { gapSum += Math.Sqrt(best); gapCount++; }
                }
            }
            double gap = gapCount > 0 ? gapSum / gapCount : 0.0;

            double pen = 0.0;
            for (int a = 0; a < ids.Count; a++)
            {
                var pa = rimPtsByFrag[ids[a]];
                for (int b = 0; b < ids.Count; b++)
                {
                    if (b == a) continue;
                    var solid = solidByFrag[ids[b]];
                    if (solid == null) continue;
                    foreach (var p in pa)
                    {
                        bool inside;
                        try { inside = solid.IsPointInside(p, JointWidth3D * 0.5, false); }
                        catch { inside = false; }
                        if (!inside) continue;
                        var cp = solid.ClosestMeshPoint(p, 0.0);
                        double d = cp == null ? 0.0 : p.DistanceTo(cp.Point);
                        if (d > pen) pen = d;
                    }
                }
            }
            return (gap, pen, scale);
        }

        // Minimal MatchResult carrying only the fields the agglomerative candidate-
        // edge path reads (A.PanelId, B.PanelId, AontoB, Residual). The Segments are
        // stub identities (one-point polyline, empty signatures) tagged with the
        // child / parent FRAGMENT ids.
        private static MatchResult StubMatch(string childId, string parentId, Transform rel, double residual)
        {
            var a = StubSegment(childId);
            var b = StubSegment(parentId);
            return new MatchResult(a, b, rel, residual, converged: true, iterations: 0);
        }

        private static Segment StubSegment(string panelId)
        {
            var poly = new Polyline { new Point3d(0, 0, 0), new Point3d(1, 0, 0) };
            return new Segment(panelId, 0, poly, 1.0, 0.0, 1, new double[1], new double[1], null);
        }

        // ====================================================================
        // Helpers shared with the GH components
        // ====================================================================

        private static void AddSegmentsFor(
            Panel panel, SegmenterOptions segOpt, SegmenterOptions3D segOpt3D, SegmentHashIndex index)
        {
            var segs = panel.Mode == PanelMode.Spatial3D
                ? BoundarySegmenter3D.Segment(panel, segOpt3D)
                : BoundarySegmenter.Segment(panel, segOpt);
            foreach (var s in segs) index.Add(s);
        }

        // Walks the candidate-generation pipeline for every shard segment and
        // reports how many survive each AND-gate: segments emitted -> hash
        // complements found (QueryComplement) -> phase-correlation gate passed.
        // A zero at any stage names the chokepoint. Mirrors AssemblySolver.TryPlace
        // up to (not including) the ICP/residual gate, which A1 already showed is
        // not the limiter here.
        private static void DiagnoseCandidateGeneration3D(
            List<Panel> shards, List<Panel> frames, SegmentHashIndex index,
            SegmenterOptions segOpt, SegmenterOptions3D segOpt3D,
            AssemblyOptions asmOpt, Action<string> emit)
        {
            int candSegs = 0, totalHits = 0, phasePass = 0, mode3D = 0, mode2D = 0;
            double bestPhase = 0.0;

            // Stage past phase: run the SAME ICP refine TryPlace runs, against
            // the anchored frame panel the hit segment belongs to, and bin the
            // residuals against the active gate. This isolates whether the gate
            // (acceptance) or the candidate flow (hash/phase) is the limiter.
            double residualGate = asmOpt.ResidualThresholdFactor > 0.0
                ? asmOpt.ResidualThresholdFactor * EstimateScale(shards, frames)
                : asmOpt.ResidualThreshold;
            var icpOpt = new IcpOptions
            {
                NonCrossingCorrespondence = asmOpt.NonCrossingCorrespondence,
                NonCrossingMaxGap = asmOpt.NonCrossingMaxGap,
            };
            var icp3d = new ConstrainedIcp3D(icpOpt);
            var icp2d = new ConstrainedIcp2D(icpOpt);
            var frameById = frames.ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);
            var frameIds = new HashSet<string>(frames.Select(p => p.Id), StringComparer.Ordinal);
            double bestResidual = double.PositiveInfinity;
            int icpRuns = 0, residualPass = 0;
            int hitsOnFrame = 0, hitsOnShard = 0;

            foreach (var p in shards)
            {
                var segs = p.Mode == PanelMode.Spatial3D
                    ? BoundarySegmenter3D.Segment(p, segOpt3D)
                    : BoundarySegmenter.Segment(p, segOpt);
                if (p.Mode == PanelMode.Spatial3D) mode3D++; else mode2D++;
                foreach (var cs in segs)
                {
                    candSegs++;
                    var hits = index.QueryComplement(cs);
                    totalHits += hits.Count;
                    foreach (var hit in hits)
                    {
                        if (frameIds.Contains(hit.PanelId)) hitsOnFrame++; else hitsOnShard++;
                        var (lag, score) = PhaseCorrelator.Correlate(cs.TurningSignature, hit.TurningSignature);
                        if (score > bestPhase) bestPhase = score;
                        if (score < asmOpt.PhaseScoreThreshold) continue;
                        phasePass++;

                        // Only refine against an anchored frame panel (TryPlace
                        // only places against already-placed panels; in round 1
                        // those are exactly the frame rims of fragment 0).
                        if (!frameById.TryGetValue(hit.PanelId, out var hitPanel)) continue;
                        icpRuns++;
                        bool pairIs3D = p.Mode == PanelMode.Spatial3D || hitPanel.Mode == PanelMode.Spatial3D;
                        MatchResult refined = pairIs3D
                            ? icp3d.Refine(cs, hit, hitPanel, null, InitialTransformBuilder.FromLag3D(cs, hit, lag))
                            : icp2d.Refine(cs, hit, hitPanel, InitialTransformBuilder.FromLag2D(cs, hit, lag));
                        if (refined.Residual < bestResidual) bestResidual = refined.Residual;
                        if (refined.Residual <= residualGate) residualPass++;
                    }
                }
            }
            emit("DIAG candidate generation (R1 chokepoint probe):");
            emit($"  index segments       : 2D={index.Count2D}  3D={index.Count3D}");
            emit($"  shard panel modes    : Spatial3D={mode3D}  Planar2D={mode2D}");
            emit($"  candidate segments   : {candSegs}");
            emit($"  hash complements     : {totalHits} (QueryComplement total over all candidate segments)");
            emit($"    -> on anchored frame: {hitsOnFrame}   on unplaced shards: {hitsOnShard}");
            emit($"  phase-gate passes    : {phasePass} (>= {asmOpt.PhaseScoreThreshold:F2})  bestPhaseScore={bestPhase:F3}");
            emit($"  ICP refines vs frame : {icpRuns}");
            emit($"  residual-gate passes : {residualPass} (<= {residualGate:F4})  bestResidual={(double.IsPositiveInfinity(bestResidual) ? double.NaN : bestResidual):F4}");
        }

        // Walks the agglomerative all-pairs ICP exactly as AssemblySolver's
        // SolveAgglomerative does: for each unordered pair, both directions, run
        // segment->hash->phase->ICP and keep the best passing residual. Reports
        // how many pairs produced a passing edge, the global best residual, and
        // how many passing edges touch an anchor (a passing edge that touches the
        // seed/anchor is required for anything to chain from it).
        private static void DiagnoseAgglomerative3D(
            List<Panel> panels, SegmentHashIndex index,
            SegmenterOptions segOpt, SegmenterOptions3D segOpt3D,
            AssemblyOptions asmOpt, IEnumerable<string> frameIds, Action<string> emit)
        {
            var nodes = panels.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var anchorSet = new HashSet<string>(frameIds, StringComparer.Ordinal);

            double residualGate = asmOpt.ResidualThresholdFactor > 0.0
                ? asmOpt.ResidualThresholdFactor * EstimateScale(panels, new List<Panel>())
                : asmOpt.ResidualThreshold;
            var icpOpt = new IcpOptions
            {
                NonCrossingCorrespondence = asmOpt.NonCrossingCorrespondence,
                NonCrossingMaxGap = asmOpt.NonCrossingMaxGap,
            };
            var icp3d = new ConstrainedIcp3D(icpOpt);
            var icp2d = new ConstrainedIcp2D(icpOpt);

            int pairsTried = 0, pairsWithHit = 0, pairsPassing = 0, anchorTouchingPassing = 0;
            double globalBest = double.PositiveInfinity;

            // Hash-recall breakdown: of every complement returned for every panel
            // segment, how many land on the SAME panel vs a DIFFERENT panel. If
            // cross-panel hits are 0, the pair graph has no edges to build on and
            // the failure is hash recall, not the assembly model.
            int selfHits = 0, crossHits = 0;
            foreach (var p in nodes)
            {
                var segs = p.Mode == PanelMode.Spatial3D
                    ? BoundarySegmenter3D.Segment(p, segOpt3D)
                    : BoundarySegmenter.Segment(p, segOpt);
                foreach (var cs in segs)
                    foreach (var hit in index.QueryComplement(cs))
                    {
                        if (string.Equals(hit.PanelId, p.Id, StringComparison.Ordinal)) selfHits++;
                        else crossHits++;
                    }
            }

            MatchResult BestDir(Panel cand, Panel hitPanel)
            {
                var segs = cand.Mode == PanelMode.Spatial3D
                    ? BoundarySegmenter3D.Segment(cand, segOpt3D)
                    : BoundarySegmenter.Segment(cand, segOpt);
                MatchResult best = null;
                foreach (var cs in segs)
                {
                    foreach (var hit in index.QueryComplement(cs))
                    {
                        if (!string.Equals(hit.PanelId, hitPanel.Id, StringComparison.Ordinal)) continue;
                        var (lag, score) = PhaseCorrelator.Correlate(cs.TurningSignature, hit.TurningSignature);
                        if (score < asmOpt.PhaseScoreThreshold) continue;
                        bool is3D = cand.Mode == PanelMode.Spatial3D || hitPanel.Mode == PanelMode.Spatial3D;
                        var refined = is3D
                            ? icp3d.Refine(cs, hit, hitPanel, null, InitialTransformBuilder.FromLag3D(cs, hit, lag))
                            : icp2d.Refine(cs, hit, hitPanel, InitialTransformBuilder.FromLag2D(cs, hit, lag));
                        if (best == null || refined.Residual < best.Residual) best = refined;
                    }
                }
                return best;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    pairsTried++;
                    var mA = BestDir(nodes[i], nodes[j]);
                    var mB = BestDir(nodes[j], nodes[i]);
                    double bestPair = double.PositiveInfinity;
                    if (mA != null) bestPair = Math.Min(bestPair, mA.Residual);
                    if (mB != null) bestPair = Math.Min(bestPair, mB.Residual);
                    if (mA != null || mB != null) pairsWithHit++;
                    if (!double.IsPositiveInfinity(bestPair) && bestPair < globalBest) globalBest = bestPair;
                    if (bestPair <= residualGate)
                    {
                        pairsPassing++;
                        if (anchorSet.Contains(nodes[i].Id) || anchorSet.Contains(nodes[j].Id))
                            anchorTouchingPassing++;
                    }
                }
            }

            emit("DIAG agglomerative pairwise graph (R0):");
            emit($"  nodes                : {nodes.Count} ({anchorSet.Count} anchored)");
            emit($"  hash hits self/cross : self={selfHits}  cross-panel={crossHits} (cross-panel=0 => hash recall blocks ALL edges)");
            emit($"  pairs tried          : {pairsTried}");
            emit($"  pairs with any hit   : {pairsWithHit}");
            emit($"  pairs passing gate   : {pairsPassing} (<= {residualGate:F4})  bestResidual={(double.IsPositiveInfinity(globalBest) ? double.NaN : globalBest):F4}");
            emit($"  passing touching anchor: {anchorTouchingPassing} (>=1 needed to chain from the anchor seed)");
        }

        private static double EstimateScale(List<Panel> a, List<Panel> b)
        {
            var diags = new List<double>();
            foreach (var p in a) { var bb = p.SourceContour.GetBoundingBox(false); if (bb.IsValid) diags.Add(bb.Diagonal.Length); }
            foreach (var p in b) { var bb = p.SourceContour.GetBoundingBox(false); if (bb.IsValid) diags.Add(bb.Diagonal.Length); }
            if (diags.Count == 0) return 1.0;
            diags.Sort();
            double med = diags[(diags.Count - 1) / 2];
            return med > 1e-9 ? med : 1.0;
        }

        private static bool TryToPolylineCurve(Curve? c, out PolylineCurve pc)
        {
            pc = null!;
            if (c == null) return false;
            if (c is PolylineCurve already) { pc = already; return true; }
            if (c.TryGetPolyline(out Polyline poly)) { pc = poly.ToPolylineCurve(); return true; }
            return false;
        }

        private static List<PolylineCurve> ExtractNakedRimLoops(Mesh mesh, double minLoopLength)
        {
            var result = new List<PolylineCurve>();
            if (mesh == null) return result;
            Polyline[]? naked = null;
            try { naked = mesh.GetNakedEdges(); } catch { naked = null; }
            if (naked == null) return result;
            foreach (var loop in naked)
            {
                if (loop == null || loop.Count < 4) continue;
                if (!loop.IsClosed) continue;
                if (loop.Length < minLoopLength) continue;
                result.Add(loop.ToPolylineCurve());
            }
            return result;
        }

        // ====================================================================
        // .3dm reading
        // ====================================================================

        private static int LayerIndexByName(File3dm doc, string name)
        {
            // File3dmLayerTable implements IList<Layer> but exposes no public
            // indexer directly; enumerate instead.
            foreach (var layer in doc.AllLayers)
            {
                if (layer != null && string.Equals(layer.Name, name, StringComparison.Ordinal))
                    return layer.Index;
            }
            return -1;
        }

        private static List<Curve> ReadCurvesOnLayer(File3dm doc, string layerName)
        {
            var result = new List<Curve>();
            int li = LayerIndexByName(doc, layerName);
            if (li < 0) return result;
            foreach (var obj in doc.Objects)
            {
                if (obj?.Attributes == null || obj.Geometry == null) continue;
                if (obj.Attributes.LayerIndex != li) continue;
                if (obj.Geometry is Curve crv) result.Add(crv);
                else if (obj.Geometry is PolylineCurve plc) result.Add(plc);
            }
            return result;
        }

        private static List<Mesh> ReadMeshesOnLayer(File3dm doc, string layerName)
        {
            var result = new List<Mesh>();
            int li = LayerIndexByName(doc, layerName);
            if (li < 0) return result;
            foreach (var obj in doc.Objects)
            {
                if (obj?.Attributes == null || obj.Geometry == null) continue;
                if (obj.Attributes.LayerIndex != li) continue;
                if (obj.Geometry is Mesh m) result.Add(m);
            }
            return result;
        }

        // ====================================================================
        // 2D measurement
        // ====================================================================

        private static double ClosedCurveArea(Curve c)
        {
            if (c == null || !c.IsClosed) return 0.0;
            var amp = AreaMassProperties.Compute(c);
            return amp == null ? 0.0 : Math.Abs(amp.Area);
        }

        private static double IntersectionArea2D(Curve a, Curve b, double tol)
        {
            // Bbox prefilter.
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

        // UNION area of a set of closed planar curves (overlaps counted once),
        // the true packing number. Falls back to the sum of areas if the boolean
        // union fails (returns 0 only when there are no valid closed curves).
        private static double UnionArea2D(List<PolylineCurve> curves, double tol)
        {
            var closed = new List<Curve>();
            foreach (var c in curves) if (c != null && c.IsClosed) closed.Add(c);
            if (closed.Count == 0) return 0.0;
            if (closed.Count == 1) return ClosedCurveArea(closed[0]);

            Curve[]? union = null;
            try { union = Curve.CreateBooleanUnion(closed, tol); }
            catch { union = null; }
            if (union == null || union.Length == 0)
            {
                // Boolean union failed: report sum-of-areas as a conservative
                // fallback (caller can still compare against pairwise overlap).
                double sum = 0.0;
                foreach (var c in closed) sum += ClosedCurveArea(c);
                return sum;
            }
            // The union may return outer loops plus inner-hole loops; signed area
            // summation (not abs) nets holes out correctly.
            double area = 0.0;
            foreach (var u in union)
            {
                if (u == null || !u.IsClosed) continue;
                var amp = AreaMassProperties.Compute(u);
                if (amp != null) area += amp.Area;
            }
            return Math.Abs(area);
        }

        private static double CentroidMatchError2D(List<PolylineCurve> placed, List<Curve> truth)
        {
            // Greedy nearest-centroid pairing. Diagnostic only: the solver does
            // not know the ground-truth correspondence, so this measures how
            // close the placed layout sits to the true layout up to labelling.
            var pc = placed.Select(CurveCentroid).ToList();
            var tc = truth.Select(CurveCentroid).ToList();
            var used = new bool[tc.Count];
            double sum = 0.0;
            int n = 0;
            for (int i = 0; i < pc.Count; i++)
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int j = 0; j < tc.Count; j++)
                {
                    if (used[j]) continue;
                    double d = pc[i].DistanceTo(tc[j]);
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best >= 0) { used[best] = true; sum += bestD; n++; }
            }
            return n > 0 ? sum / n : double.NaN;
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

        // ====================================================================
        // 3D measurement
        // ====================================================================

        private static (int pairs, double maxDepth, int deepSamples) MeshOverlap(
            List<Mesh> meshes, double tol)
        {
            int pairs = 0;
            double maxDepth = 0.0;
            int deepSamples = 0;
            // FillHoles-closed copies so IsPointInside has a watertight solid to
            // test against (mirrors the SettleContact / Kintsugi inside-test
            // approach). Naked-rim shards may be open; fill first.
            var closed = meshes.Select(ClosedCopy).ToList();
            for (int i = 0; i < meshes.Count; i++)
            {
                for (int j = 0; j < meshes.Count; j++)
                {
                    if (i == j) continue;
                    var probe = meshes[i];
                    var solid = closed[j];
                    if (probe == null || solid == null) continue;

                    var bbA = probe.GetBoundingBox(false);
                    var bbB = solid.GetBoundingBox(false);
                    if (!bbA.IsValid || !bbB.IsValid) continue;
                    if (bbA.Max.X < bbB.Min.X || bbA.Min.X > bbB.Max.X) continue;
                    if (bbA.Max.Y < bbB.Min.Y || bbA.Min.Y > bbB.Max.Y) continue;
                    if (bbA.Max.Z < bbB.Min.Z || bbA.Min.Z > bbB.Max.Z) continue;

                    bool thisPair = false;
                    int vcount = probe.Vertices.Count;
                    int step = Math.Max(1, vcount / 128);
                    for (int v = 0; v < vcount; v += step)
                    {
                        Point3d p = probe.Vertices[v];
                        bool inside;
                        try { inside = solid.IsPointInside(p, tol * 0.5, false); }
                        catch { inside = false; }
                        if (!inside) continue;
                        var cmp = solid.ClosestMeshPoint(p, 0.0);
                        double depth = cmp == null ? tol : p.DistanceTo(cmp.Point);
                        if (depth > tol)
                        {
                            deepSamples++;
                            thisPair = true;
                            if (depth > maxDepth) maxDepth = depth;
                        }
                    }
                    // Count each unordered pair once.
                    if (thisPair && i < j) pairs++;
                }
            }
            return (pairs, maxDepth, deepSamples);
        }

        private static void ReportInputStateOverlap3D(
            List<Mesh> meshes, double tol, Action<string> emit)
        {
            var (pairs, maxDepth, deepSamples) = MeshOverlap(meshes, tol);
            emit(new string('-', 64));
            emit("OVERLAP (input scattered state, no assembly performed):");
            emit($"  overlapping pairs    : {pairs} of {Pairs(meshes.Count)}");
            emit($"  max penetration depth: {maxDepth:F4}");
            emit($"  deep-inside samples  : {deepSamples}");
            var box = UnionBox(meshes);
            if (box.IsValid)
                emit($"  scattered bbox diag  : {box.Diagonal.Length:F4}");
        }

        private static Mesh ClosedCopy(Mesh m)
        {
            var c = m.DuplicateMesh();
            try { if (!c.IsClosed) c.FillHoles(); } catch { /* best effort */ }
            return c;
        }

        private static BoundingBox UnionBox(List<Mesh> meshes)
        {
            var box = BoundingBox.Unset;
            foreach (var m in meshes)
            {
                if (m == null) continue;
                var b = m.GetBoundingBox(false);
                if (b.IsValid) box.Union(b);
            }
            return box;
        }

        private static int Pairs(int n) => n < 2 ? 0 : n * (n - 1) / 2;

        // ====================================================================
        // Pillar A — Soft-ICP refine (rim contact + non-penetration)
        // ====================================================================

        // 2D Soft-ICP demonstration from a PERTURBED start (mirrors the 3D box-pair
        // demo). The solver's placed 2D mosaic is ALREADY tiled to contact (overlap
        // 0%), so refining it in place has no gap to close, and its irregular pieces
        // share only short edges that under-constrain the rotation. To cleanly
        // demonstrate the refiner pulling rims into contact while overlap stays ~0,
        // the demo uses a self-contained ABUTTING SQUARE pair (sized from the placed
        // assembly extent): square A anchored, square B abuts on a shared edge and
        // is displaced by a known SE(2) perturbation INTO A (start has both a rim
        // gap and real overlap). The refiner pulls B's shared-edge rim into contact
        // with A while the non-penetration term lifts B's penetrating samples back
        // to A's boundary. Deterministic.
        private static void RunSoftIcp2D(AssemblyState state, Action<string> emit)
        {
            emit(new string('-', 64));
            emit("SOFT-ICP (Pillar A, 2D perturbed-start rim-contact + non-penetration):");
            int n = state.PlacedPanels.Count;
            if (n < 1)
            {
                emit("  no placed panels; nothing to refine.");
                return;
            }
            emit("  note: the placed mosaic is already tiled to contact (overlap 0%); the");
            emit("        refiner is demonstrated on an abutting square pair sized from the");
            emit("        placed extent (clean shared-edge rim, reliable inside-test).");

            // Scale from the placed assembly extent (scale-invariant loss).
            var ub = BoundingBox.Unset;
            foreach (var panel in state.PlacedPanels)
            {
                Transform t = state.AppliedTransforms.TryGetValue(panel.Id, out var xf) ? xf : Transform.Identity;
                var c = (PolylineCurve)panel.SourceContour.DuplicateCurve();
                c.Transform(t);
                ub.Union(c.GetBoundingBox(false));
            }
            double scale = ub.IsValid ? ub.Diagonal.Length : 100.0;
            double s = scale / 6.0;

            // Square A = [0,s]x[0,s] anchored. Square B = [s,2s]x[0,s] abuts on x=s.
            var sqA = MakeSquareCurve(0, 0, s, s);
            var sqB = MakeSquareCurve(s, 0, 2 * s, s);

            double tMag = 0.05 * s, ang = Rhino.RhinoMath.ToRadians(4.0);
            var rng = new Random(20260525);
            // Perturb B about its centroid, biased -X so it penetrates A.
            double dx = -(0.5 + rng.NextDouble()) * tMag;
            double dy = (rng.NextDouble() * 2 - 1) * tMag * 0.3;
            Point3d cB = new Point3d(1.5 * s, 0.5 * s, 0);
            var perturb = Transform.Multiply(
                Transform.Translation(dx, dy, 0),
                Transform.Rotation((rng.NextDouble() * 2 - 1) * ang, Vector3d.ZAxis, cB));
            var sqBp = (PolylineCurve)sqB.DuplicateCurve();
            sqBp.Transform(perturb);

            // Rim = the shared edge (x=s) of each square, sampled densely.
            var rimA = EdgeSamples(new Point3d(s, 0, 0), new Point3d(s, s, 0), 48);
            var rimB0 = EdgeSamples(new Point3d(s, 0, 0), new Point3d(s, s, 0), 48);
            var rimB = new Point3d[rimB0.Length];
            for (int i = 0; i < rimB0.Length; i++) { var p = rimB0[i]; p.Transform(perturb); rimB[i] = p; }

            var frags = new List<SoftIcpRefiner.Fragment>
            {
                new SoftIcpRefiner.Fragment("sqA", rimA, solid: null, contour2D: sqA, anchored: true),
                new SoftIcpRefiner.Fragment("sqB", rimB, solid: null, contour2D: sqBp, anchored: false),
            };

            var opt = new SoftIcpOptions();
            var before = SoftIcpRefiner.Measure(frags, opt, threeD: false);
            var after = SoftIcpRefiner.Refine2D(frags, opt);

            emit($"  pair                 : abutting squares A (anchor) + B, shared edge x={s:F2}");
            emit($"  perturbation         : ~{tMag:F3} u translation (biased into A), ~4 deg rotation on B");
            emit($"  EM iterations        : {after.Iterations}");
            emit($"  mean rim GAP  before : {before.MeanRimGap:F4}  after : {after.MeanRimGap:F4} (model units; should DROP toward 0)");
            emit($"  contact samples      : {before.ContactSamples} -> {after.ContactSamples} (rim samples on the mating interface)");
            emit($"  overlap proxy before : {before.MaxPenetration:F4}  after : {after.MaxPenetration:F4} (max contour interpenetration; should stay ~0)");
        }

        private static PolylineCurve MakeSquareCurve(double x0, double y0, double x1, double y1)
        {
            var poly = new Polyline
            {
                new Point3d(x0, y0, 0), new Point3d(x1, y0, 0),
                new Point3d(x1, y1, 0), new Point3d(x0, y1, 0), new Point3d(x0, y0, 0),
            };
            return poly.ToPolylineCurve();
        }

        private static Point3d[] EdgeSamples(Point3d a, Point3d b, int count)
        {
            var pts = new Point3d[count + 1];
            for (int i = 0; i <= count; i++)
            {
                double u = (double)i / count;
                pts[i] = a + (b - a) * u;
            }
            return pts;
        }

        // 3D Soft-ICP refiner demonstration from a PERTURBED start. The directive
        // is to perturb the ground-truth poses by a known small SE(3) and show the
        // refiner converges back to rim-contact with penetration ~0, ISOLATING the
        // refiner from 3D candidate generation. The Assembled3D shards in this
        // fixture are CLOSED CONVEX HULLS whose only naked edge is their own outer
        // silhouette, NOT a shared fracture rim, so a per-shard perturb-and-refine
        // has no valid mating-rim target. To give the refiner a genuine 3D mating
        // interface AND a reliably watertight solid for the non-penetration inside-
        // test, the demo builds a self-contained pair of ABUTTING BOXES (sized from
        // the GT bbox) that share a face: box A is the anchor, box B abuts it and is
        // displaced by a known SE(3) perturbation INTO A (so the start has both a
        // rim gap and real interpenetration). The refiner must pull B's shared-face
        // rim into contact with A while the non-penetration term lifts B's
        // penetrating samples back to A's surface. Boxes close exactly, so
        // Mesh.IsPointInside is reliable (unlike FillHoles-capped open halves).
        // Deterministic.
        private static void RunSoftIcp3DPerturbedGt(List<Mesh> truthMeshes, Action<string> emit)
        {
            emit(new string('-', 64));
            emit("SOFT-ICP (Pillar A, 3D perturbed-start rim-contact + non-penetration):");
            int n = truthMeshes.Count;
            if (n < 1)
            {
                emit("  Assembled3D empty; cannot run the perturbed-start demo.");
                return;
            }
            emit("  note: Assembled3D shards are closed convex hulls with no shared fracture");
            emit("        rim; the refiner is demonstrated on an abutting watertight-box pair");
            emit("        (reliable inside-test) sized from the GT bounding box.");

            // Box dimensions from the GT extent (scale-relative; the loss is
            // scale-invariant so the absolute size is immaterial).
            var gtBox = UnionBox(truthMeshes);
            double scale = gtBox.IsValid ? gtBox.Diagonal.Length : 100.0;
            double s = scale / 6.0; // edge length ~ a sixth of the assembly diagonal

            // Box A: [0,s]^3 anchored. Box B: [s,2s]x[0,s]x[0,s] abuts A on x=s.
            var boxA = MakeBoxMesh(new Point3d(0, 0, 0), new Point3d(s, s, s));
            var boxB = MakeBoxMesh(new Point3d(s, 0, 0), new Point3d(2 * s, s, s));

            double tMag = 0.05 * s;   // shove B into A by 5% of an edge
            double rMag = Rhino.RhinoMath.ToRadians(4.0);
            var rng = new Random(20260525);
            // Known SE(3) perturbation of B about its centroid, biased -X so it
            // penetrates A (guarantees real interpenetration at the start).
            double rx = -(0.5 + rng.NextDouble()) * tMag;       // into A
            double ry = (rng.NextDouble() * 2 - 1) * tMag * 0.3;
            double rz = (rng.NextDouble() * 2 - 1) * tMag * 0.3;
            double wx = (rng.NextDouble() * 2 - 1) * rMag;
            double wy = (rng.NextDouble() * 2 - 1) * rMag;
            double wz = (rng.NextDouble() * 2 - 1) * rMag;
            var perturb = LieSe3.Exp(rx, ry, rz, wx, wy, wz);
            var cB = boxB.GetBoundingBox(true).Center;
            var aboutB = Transform.Multiply(Transform.Translation((Vector3d)cB),
                Transform.Multiply(perturb, Transform.Translation(-(Vector3d)cB)));
            boxB.Transform(aboutB);

            // Rim = the shared-face boundary (the x=s face) of each box. Sample the
            // face-loop corners + edge midpoints densely.
            var rimA = FaceRimSamples(new Point3d(s, 0, 0), new Point3d(s, s, s), 12);
            var rimB0 = FaceRimSamples(new Point3d(s, 0, 0), new Point3d(s, s, s), 12);
            // Move B's rim with the perturbation so it starts displaced + penetrating.
            var rimB = new Point3d[rimB0.Length];
            for (int i = 0; i < rimB0.Length; i++) { var p = rimB0[i]; p.Transform(aboutB); rimB[i] = p; }

            var frags = new List<SoftIcpRefiner.Fragment>
            {
                new SoftIcpRefiner.Fragment("boxA", rimA, solid: boxA, contour2D: null, anchored: true),
                new SoftIcpRefiner.Fragment("boxB", rimB, solid: boxB, contour2D: null, anchored: false),
            };

            var opt = new SoftIcpOptions();
            var before = SoftIcpRefiner.Measure(frags, opt, threeD: true);
            var after = SoftIcpRefiner.Refine3D(frags, opt);

            emit($"  perturbation         : ~{tMag:F3} u translation (biased into A), ~4 deg rotation on box B");
            emit($"  EM iterations        : {after.Iterations}");
            emit($"  mean rim GAP  before : {before.MeanRimGap:F4}  after : {after.MeanRimGap:F4} (model units; should DROP toward 0)");
            emit($"  contact samples      : {before.ContactSamples} -> {after.ContactSamples} (rim samples on the mating interface)");
            emit($"  max penetration before: {before.MaxPenetration:F4}  after : {after.MaxPenetration:F4} (depth; should stay ~0)");
        }

        // Watertight axis-aligned box mesh from two opposite corners. Closed
        // (12 triangles), so Mesh.IsPointInside is reliable.
        private static Mesh MakeBoxMesh(Point3d lo, Point3d hi)
        {
            var box = new BoundingBox(lo, hi);
            var m = Mesh.CreateFromBox(box, 1, 1, 1);
            if (m != null) { m.Normals.ComputeNormals(); m.Compact(); }
            return m;
        }

        // Samples of the rectangular shared FACE rim (corners + edge midpoints +
        // a grid) on the x = lo.X plane between lo and hi (a YZ rectangle). These
        // are the mating-interface points of the abutting box.
        private static Point3d[] FaceRimSamples(Point3d lo, Point3d hi, int per)
        {
            double x = lo.X;
            var pts = new List<Point3d>();
            for (int iy = 0; iy <= per; iy++)
            {
                double y = lo.Y + (hi.Y - lo.Y) * iy / per;
                for (int iz = 0; iz <= per; iz++)
                {
                    double z = lo.Z + (hi.Z - lo.Z) * iz / per;
                    pts.Add(new Point3d(x, y, z));
                }
            }
            return pts.ToArray();
        }

        // Even-arc-length samples of a closed curve (the 2D rim).
        private static Point3d[] SampleClosed(Curve c, int count)
        {
            if (c == null) return new Point3d[0];
            var ts = c.DivideByCount(Math.Max(3, count), true);
            if (ts == null) return new Point3d[0];
            var pts = new Point3d[ts.Length];
            for (int i = 0; i < ts.Length; i++) pts[i] = c.PointAt(ts[i]);
            return pts;
        }
    }
}
