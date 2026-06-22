namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Assembly strategy. <see cref="FrameAnchored"/> is the original beam:
    /// every candidate is refined only against panels ALREADY placed in the
    /// state, growing outward from the anchor(s). It suits the 2D Trencadís
    /// case where parts mate to a sheet/frame that is placed first.
    /// <see cref="Agglomerative"/> matches ALL pairs first, builds a weighted
    /// pair graph, and composes a minimum-residual spanning tree from a seed.
    /// It suits free 3D fragment reassembly where pieces mate PAIRWISE (no
    /// single frame touches all shards), so the frame-anchored beam places
    /// nothing in round 1. See R0 / RUN_NOTES 2026-05-25 (c).
    /// </summary>
    public enum AssemblyMode
    {
        FrameAnchored = 0,
        Agglomerative = 1,
    }

    public class AssemblyOptions
    {
        /// <summary>
        /// Which assembly model to run. Default <see cref="AssemblyMode.FrameAnchored"/>
        /// keeps the original beam byte-for-byte (2D Trencadís + all existing
        /// tests unchanged). Set to <see cref="AssemblyMode.Agglomerative"/> for
        /// free pairwise 3D reassembly.
        /// </summary>
        public AssemblyMode Mode { get; set; } = AssemblyMode.FrameAnchored;

        public int BeamWidth { get; set; } = 8;
        public int MaxIterations { get; set; } = 1000;
        public double ResidualThreshold { get; set; } = 1.0;
        public double TripleJunctionBonus { get; set; } = 0.5;
        public double GrainParallelBonus { get; set; } = 0.3;
        public int RandomSeed { get; set; } = 20260510;

        /// <summary>
        /// When true the per-pair ICP uses an ORDER-PRESERVING (monotone,
        /// non-crossing) rim correspondence instead of free nearest-neighbour.
        /// Default false: existing behaviour and all current tests unchanged.
        /// Threaded into <see cref="IcpOptions.NonCrossingCorrespondence"/> by
        /// <see cref="AssemblySolver"/>.
        /// </summary>
        public bool NonCrossingCorrespondence { get; set; } = false;

        /// <summary>
        /// Index-band bound for the monotone DP when
        /// <see cref="NonCrossingCorrespondence"/> is on. Zero = unbounded.
        /// </summary>
        public int NonCrossingMaxGap { get; set; } = 0;

        // --- Phase 1: cycle-consistency consensus on the Agglomerative pair graph (opt-in) ---
        // Greedy MST commits to the lowest-residual edge per pair and never re-checks it
        // against the global graph, so one tight-but-wrong match locks in and propagates.
        // When enabled, every triangle of the pair graph is closed (compose the three
        // relative poses around the loop; a consistent loop returns to identity). Edges
        // whose triangles persistently fail to close (median loop deviation above the
        // tolerance) get CycleConsistencyPenalty added to their MST/seed weight, so the
        // assembly avoids them. Default off = byte-identical legacy MST.
        public bool UseCycleConsistency { get; set; } = false;

        /// <summary>Scale-relative loop-closure tolerance: |rotation (rad)| + |translation|/scale.</summary>
        public double CycleConsistencyTolerance { get; set; } = 0.12;

        /// <summary>Weight added to an edge sitting in inconsistent loops (soft rejection; preserves connectivity).</summary>
        public double CycleConsistencyPenalty { get; set; } = 1.0e6;

        // --- Phase 1b: contact-seam-length match discriminator (opt-in) ---
        // The ICP residual cannot tell a true neighbour (shares a long contiguous
        // complementary seam) from a spurious match (one coincidental short fragment) --
        // spurious matches can have residuals AS LOW AS or lower than true ones. After
        // the ICP pose, measure what fraction of the candidate perimeter lands within
        // ContactToleranceFraction*scale of the hit boundary; reject matches below
        // MinContactFraction and rank survivors by contact (then residual). This removes
        // the spurious fragment matches at the source, before the pair graph is built.
        // Default off = legacy residual-only behaviour (all current tests unchanged).
        public bool UseContactScore { get; set; } = false;

        /// <summary>Contact band as a fraction of object scale (median panel bbox diagonal).</summary>
        public double ContactToleranceFraction { get; set; } = 0.02;

        /// <summary>Minimum fraction of candidate perimeter in contact to accept a match (0 = no gate).
        /// 0.18 default tuned on the jigsaw harness (true seams ~0.25-0.36, spurious fragments ~0.14-0.17);
        /// raise toward 0.24 to cut more false edges, but above ~0.30 it starts dropping true seams.</summary>
        public double MinContactFraction { get; set; } = 0.18;

        /// <summary>Number of arc-length samples along the candidate perimeter for the contact test.</summary>
        public int ContactSamples { get; set; } = 64;

        // --- A1: scale-relative acceptance gates (opt-in) ---------------------
        // The original gates are ABSOLUTE: the phase-correlation similarity gate
        // is a fixed 0.5 and ResidualThreshold is a fixed model-unit distance.
        // That breaks across scales (a 1.0-unit residual is 1% of a 100u block
        // but meters of slack on a cm fragment / rejects everything on a mm
        // fracture). These options make the gates scale-relative WITHOUT changing
        // defaults: PhaseScoreThreshold defaults to the original 0.5, and
        // ResidualThresholdFactor defaults to 0 = use the absolute ResidualThreshold.

        /// <summary>
        /// Minimum PhaseCorrelator similarity to accept a candidate pair.
        /// Default 0.5 (the original hardcoded gate).
        /// </summary>
        public double PhaseScoreThreshold { get; set; } = 0.5;

        /// <summary>
        /// When &gt; 0, the effective residual gate becomes
        /// <c>ResidualThresholdFactor * objectScale</c> (objectScale = the
        /// combined bounding-box diagonal of the panels being assembled), making
        /// acceptance scale-relative. Zero (default) keeps the absolute
        /// <see cref="ResidualThreshold"/> so existing behaviour is unchanged.
        /// Suggested value ~0.01 (1% of the assembly diagonal).
        /// </summary>
        public double ResidualThresholdFactor { get; set; } = 0.0;

        // --- R1: partial sub-segment matching (opt-in) ------------------------
        // A long boundary edge and the short sub-edge it physically mates with
        // hash to different length/turning bins and never meet in
        // SegmentHashIndex. When EmitPartials is on, the segmenters ALSO emit
        // shorter overlapping sub-windows so the long edge's partials reach the
        // short complementary edge. AssemblySolver threads these onto the
        // SegmenterOptions it uses for candidate segmentation (mirror of how
        // NonCrossingCorrespondence is threaded onto IcpOptions). Default OFF:
        // when off, segmentation and candidate generation are byte-for-byte
        // unchanged.

        /// <summary>
        /// When true, the boundary segmenters emit partial sub-windows of each
        /// base segment (see <see cref="SegmenterOptions.EmitPartials"/>).
        /// Default false: candidate generation is identical to before.
        /// </summary>
        public bool EmitPartials { get; set; } = false;

        /// <summary>
        /// Partial window lengths as fractions of each base segment's span
        /// (see <see cref="SegmenterOptions.PartialFractions"/>). Only consulted
        /// when <see cref="EmitPartials"/> is true. Default {0.5, 0.25}, a
        /// reasonable two-scale ladder; ignored entirely when EmitPartials off.
        /// </summary>
        public double[] PartialFractions { get; set; } = new[] { 0.5, 0.25 };

        /// <summary>
        /// Stride between consecutive partial windows as a fraction of the
        /// window length (see <see cref="SegmenterOptions.PartialStrideFraction"/>).
        /// Default 1.0 (non-overlapping tiling). Only consulted when
        /// <see cref="EmitPartials"/> is true.
        /// </summary>
        public double PartialStrideFraction { get; set; } = 1.0;

        // --- R2: global non-overlap resolve (opt-in) --------------------------
        // This is REASSEMBLY: every piece belongs, so the goal is to place all
        // pieces touching but NOT interpenetrating, never to drop a piece. The
        // matcher maximises per-pair edge-match quality with no global non-overlap
        // term, so two outlines can snap to the same / a nearby edge and overlap
        // (measured: ~12% area frame-anchored, ~25% agglomerative on the 2D
        // fixture). The three knobs below add a non-overlap term WITHOUT changing
        // any default: OverlapPenalty defaults to 0 (no penalty), EdgeExclusivity
        // defaults false (a placed segment can be reused), ResolveOverlap defaults
        // false (no post-solve polish). With all three off the solver path is
        // byte-for-byte the prior behaviour. See wiki/research/
        // edge_matching_recall_and_overlap.md Part 2 + Tier A3/B6/C8.

        /// <summary>
        /// R2 (A3, primary). When &gt; 0, a candidate placement's score is
        /// penalised by <c>OverlapPenalty * (overlapAreaFraction)</c>, where the
        /// fraction is the area the candidate's transformed contour overlaps the
        /// already-placed contours divided by the candidate's own area (so the
        /// term is scale-relative and in [0, ~k]). This lowers overlapping
        /// placements in the beam sort and in the agglomerative spanning-tree
        /// edge selection. Default 0 = no penalty (existing behaviour). A working
        /// value is ~1.0 (an overlap equal to the whole piece costs as much as a
        /// 1.0 residual); the harness uses 2.0.
        /// </summary>
        public double OverlapPenalty { get; set; } = 0.0;

        /// <summary>
        /// R2 (B6). When true, a placed panel's matched boundary segment is marked
        /// "consumed": a later candidate in the SAME assembly state cannot match
        /// that identical segment, preventing two pieces from snapping to the same
        /// placed edge. Deterministic (segment identity is (PanelId, Index)).
        /// Default false = a segment can be reused (existing behaviour).
        /// </summary>
        public bool EdgeExclusivity { get; set; } = false;

        /// <summary>
        /// R2 (post-solve 2D rigid depenetration polish, the 2D Contact Settle).
        /// When true, after the solve a deterministic Jacobi relaxation translates
        /// overlapping placed contours apart along their centroid-separation
        /// direction until pairwise overlap is &lt;= <see cref="ResolveOverlapTolerance"/>,
        /// anchor-locked (anchored panels do not move). Mirrors
        /// SettleContactComponent in 2D. Translation only: matched orientation is
        /// preserved. Default false = no polish (existing behaviour). Applied by
        /// the caller via <see cref="OverlapResolver2D"/> on the returned state;
        /// the solver itself never calls it (keeps Solve pure / RhinoCommon-light).
        /// </summary>
        public bool ResolveOverlap { get; set; } = false;

        /// <summary>
        /// Target maximum pairwise overlap area (as a FRACTION of the smaller of
        /// the two contours' areas) for the <see cref="ResolveOverlap"/> polish.
        /// 0 = settle to just touching. Default 0.001 (0.1%). Only consulted when
        /// <see cref="ResolveOverlap"/> is true.
        /// </summary>
        public double ResolveOverlapTolerance { get; set; } = 0.001;

        /// <summary>
        /// Maximum relaxation iterations for the <see cref="ResolveOverlap"/>
        /// polish. Stops early once max overlap is within tolerance. Default 50.
        /// </summary>
        public int ResolveOverlapIterations { get; set; } = 50;

        /// <summary>
        /// Per-iteration step factor in (0, 1] for the <see cref="ResolveOverlap"/>
        /// polish. Lower = stabler but slower. Default 0.5.
        /// </summary>
        public double ResolveOverlapRelaxation { get; set; } = 0.5;

        // --- Pillar A: Soft-ICP (gradient-descent / EM) rim-contact refine -----
        // OPT-IN, default OFF = byte-for-byte unchanged. The solver never calls
        // the refiner; the CALLER (harness / GH component) runs SoftIcpRefiner on
        // the placed AssemblyState when SoftIcpRefine is true. The refiner adjusts
        // placed fragment poses to pull OPEN-mesh fracture rims into CONTACT while
        // a smooth penetration hinge keeps solids from interpenetrating. One
        // objective: w_contact * SoftRimCorrespondenceSSD + w_pen * PenetrationHinge,
        // optimised by EM weighted-Kabsch on the SE(2)/SE(3) Lie algebra, anchor-
        // locked, deterministic. See wiki/research/differentiable_edge_matching.md
        // §3 (Pillar A) and SoftIcpRefiner / SoftIcpOptions.

        /// <summary>
        /// Pillar A. When true the caller runs <see cref="SoftIcpRefiner"/> after
        /// the solve to bring open-mesh rims into contact with a non-penetration
        /// hinge. Default false = no Soft-ICP refine (solver path identical to
        /// before). The refiner itself is invoked by the caller, not by
        /// <see cref="AssemblySolver"/>, so Solve stays pure.
        /// </summary>
        public bool SoftIcpRefine { get; set; } = false;

        /// <summary>
        /// Tuning for the Soft-ICP refiner (scale-relative tau / anneal / weights /
        /// iterations). Only consulted when <see cref="SoftIcpRefine"/> is true.
        /// Never null; defaults are tuned for the ~100u normalised fixtures.
        /// </summary>
        public SoftIcpOptions SoftIcp { get; set; } = new SoftIcpOptions();

        // --- 2.5D per-facet PROJECTION BOOTSTRAP (opt-in) ---------------------
        // R0 proved the geometric 3D path has an EMPTY pair graph: independently-
        // tessellated shard rims never hash-match (hash hits self=172,
        // cross-panel=0), so the agglomerative solver has nothing to assemble.
        // When ProjectionBootstrap is on, the CALLER runs ProjectionPairFinder to
        // project each naked rim into its facet plane (planar by the open-shell
        // fact), match the projected rims with the WORKING 2D path, lift each 2D
        // match to a 3D relative pose, and feed those candidate edges to
        // SolveAgglomerative (which then ignores the empty 3D-hash edges and builds
        // the pair graph from the injected candidates instead). Default OFF: the
        // FrameAnchored beam, the 2D path, and all headless tests are byte-for-byte
        // unchanged. Used ONLY on the agglomerative 3D path. See
        // wiki/algorithms/edge_matching/projection_bootstrap_3d.md and
        // ProjectionPairFinder.

        /// <summary>
        /// When true, the caller bootstraps 3D candidate pairs via
        /// <see cref="ProjectionPairFinder"/> (per-facet 2D projection + lift) and
        /// passes them to the agglomerative solver. Default false = no projection
        /// bootstrap (solver path identical to before). The finder itself is invoked
        /// by the caller, not by <see cref="AssemblySolver"/>; the solver only
        /// consumes injected candidate edges when given them.
        /// </summary>
        public bool ProjectionBootstrap { get; set; } = false;

        /// <summary>
        /// Resample spacing for the projected 2D rim, as a FRACTION of the rim loop's
        /// bbox diagonal (tessellation-invariant, scale-relative). Smaller = denser
        /// resample (higher fidelity, more cost). Default 0.02 (2% of the loop
        /// diagonal). Only consulted by <see cref="ProjectionPairFinder"/>.
        /// </summary>
        public double ProjectionSampleSpacingFactor { get; set; } = 0.02;

        /// <summary>
        /// Planarity flag threshold for a projected rim, as a FRACTION of the rim
        /// loop's bbox diagonal: a loop whose PCA out-of-plane RMS residual exceeds
        /// <c>ProjectionPlanarityFactor * loopScale</c> is FLAGGED low-planarity
        /// (worn / non-planar facet) and excluded from projection matching (the
        /// projection error would dominate; fall back to the learned Port for those).
        /// Default 0.05 (5% of the loop diagonal). Only consulted by
        /// <see cref="ProjectionPairFinder"/>.
        /// </summary>
        public double ProjectionPlanarityFactor { get; set; } = 0.05;

        /// <summary>
        /// 3D verification gate for a lifted candidate pair, as a FRACTION of the
        /// projected-rim scale (median projected-panel bbox diagonal). After lifting
        /// a 2D-shadow match to a 3D relative pose, the finder measures the ACTUAL
        /// 3D rim-to-rim residual; a candidate whose 3D residual exceeds
        /// <c>ProjectionVerifyFactor * scale</c> is a projection-ambiguous false
        /// positive (matched in shadow, not in 3D) and is DROPPED. This is the
        /// "projection PROPOSES, 3D DISPOSES" check at edge-selection time. Default
        /// 0.12 (12% of the object scale = median rim-loop diagonal): wide enough to
        /// admit every genuine mating facet on the synthetic open-shell fixture
        /// (true 3D residuals cluster below this; false-positive shadow matches sit
        /// well above), so the agglomerative MST can span all fragments and then
        /// pick the lowest-residual edges. Only consulted by
        /// <see cref="ProjectionPairFinder"/>.
        /// </summary>
        public double ProjectionVerifyFactor { get; set; } = 0.12;
    }
}
