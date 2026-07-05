# Edge Matching: Theory Survey vs. the Frahan Implementation

A structured audit that maps the reference survey *"Computational Geometry and
Robotic Fabrication for the Adaptive Reuse of Irregular Timber"* onto what
Frahan's edge-matching subsystem actually implements. The survey is a good
taxonomy (FPFH to k-d tree to SAC-IA/ICP to shape-context/Frechet to true-shape
nesting/WFC to 6-axis robotic fabrication). This document states, per concept,
what Frahan builds, how it diverges, and what is genuinely missing.

Scope note: two edge-matching stacks exist, and the survey conflates them.

- **Stack A — `Frahan.EdgeMatching.Core`** (namespace `Frahan.EdgeMatching`):
  the 5-stage reassembly solver. Turning/curvature/torsion signatures, a
  descriptor-bin hash index, phase correlation, constrained ICP, CPD Soft-ICP,
  and beam/MST assembly. 2D and 3D paths.
- **Stack B — boundary "rail"** (`Frahan.StonePack.Core`, namespaces
  `Frahan.Core` / `Frahan.Surface`): sliding-window boundary-rail descriptors, a
  4-bucket `EdgeKey` hash, and a multiplicative affinity scorer. 2D (XY) only.

All citations are the canonical tree (`github libishm1/Frahan`,
`D:\frahan-stonepack`); the `Template-General` mirror is stale.

Last updated: 2026-07-05.

---

## Headline finding

The survey is written from a **point-cloud surface-registration** worldview
(PCL/CGAL, FPFH descriptors, SAC-IA + ICP). Frahan's edge-matcher is
**curve/contour-centric**: it describes the shared *rim* of a fragment as a 1D
turning/curvature/torsion signal and matches those signals. For the specific
"which two edges mate" problem this is the more appropriate model, not a
shortfall. FPFH describes a local *surface patch*; a mating edge is a 1D *curve*,
and a curve's intrinsic signature (turning angle, curvature, torsion) is the
natural pose-invariant key. So the largest apparent "gap" (no FPFH) is a
deliberate and defensible modelling choice.

Frahan is also **ahead of the survey** in several places: signed torsion for
mirror/chirality disambiguation, CPD Soft-ICP with geometric annealing and
Lie-algebra retraction, scale-relative descriptor binning, and cycle-consistency
plus best-buddies outlier rejection in the global solver. None of those appear
in the survey.

The genuine, actionable gaps are narrower than the survey implies: a Frechet
verification gate, geometric hashing for partial/occluded rims, point-to-plane
ICP, MILP/FEA-coupled reuse assignment, and wiring edge-match output into the
existing fabrication export chain.

---

## 1. The central thesis: 1D hash -> spatial collision-as-goal

**Survey:** replace the 1D `h(k) = (ak+b) mod p mod m` hash with a spatial
database where a "collision" (two signatures in the same bucket) is the *desired*
outcome — a probable physical match.

**Frahan:** realises exactly this inversion, twice.

- Stack A `SegmentHashIndex` (`src/Frahan.EdgeMatching.Core/SegmentHashIndex.cs`)
  buckets each rim segment by a rotation/translation-invariant key
  (`SegmentHashKey`: chord length, total turning, mean/std of the turning
  signature, sign). Retrieval is `QueryComplement`: it builds the *complement*
  key (a mating rim traverses the shared curve in the opposite sense, so turning
  and mean negate, std is invariant, sign flips) and gathers the bin
  neighbourhood. A "collision" with the complement bucket is the candidate match.
- Stack B `BoundaryRailIndex` (`src/Frahan.StonePack.Core/BoundaryRailMatcher.cs`
  and its index) buckets boundary windows by an `EdgeKey` (length, angle,
  curvature, zone) and returns bucket neighbours as candidates.

Verdict: **Implemented**, and closer to the survey's own framing than the survey
realises — the complement-key trick is the concrete form of "collision is the
goal."

---

## 2. Data acquisition and preprocessing

**Survey:** laser triangulation / photogrammetry to point clouds; statistical
outlier removal; voxel-grid downsampling; normals.

**Frahan:** the `ScanIngest` pipeline covers this: multi-format cloud/mesh
readers, voxel downsampling, ICP cloud registration, normal estimation
(`EstimateCloudNormals`), and out-of-process reconstruction
(`OutOfProcessReconstructor`, now with the density-adaptive AlphaShape auto-alpha
and Auto-mode Poisson-when-normals). Statistical outlier removal exists in the
reconstruction cleanup and mesh sanitiser layers. This is upstream of matching
and largely aligns with the survey.

Verdict: **Implemented** (acquisition is not the edge-matcher's novelty; it is
solid table-stakes).

---

## 3. Feature extraction: FPFH/PFH vs. turning/curvature/torsion signatures

**Survey:** PFH (O(k^2)/point) and FPFH (O(k), 33-D) as pose-invariant local
surface descriptors.

**Frahan:** does **not** compute FPFH or PFH. It computes 1D **curve** signatures:

- `Segment` carries `TurningSignature`, `CurvatureSignature`, and (3D only)
  `TorsionSignature` (`src/Frahan.EdgeMatching.Core/Segment.cs:23-25`).
- 2D: `BoundarySegmenter` resamples the contour by arc length, computes the
  signed turning angle `atan2(cross, dot)` per vertex, splits at curvature
  break-points, and resamples the turning signal into fixed bins.
- 3D: `BoundarySegmenter3D` computes discrete **Frenet-Serret** invariants —
  curvature `kappa = |d(tangent)|/meanL` and signed **torsion** from consecutive
  binormals (`ComputeFrenetInvariants`) — Gaussian-smoothed.
- Stack B `BoundaryRailBuilder` builds a 4-field window descriptor: average
  tangent, inward normal, curvature score, straightness `1 - chord/arclength`.

Why this is the right call, not a gap: the mating feature is a **1D boundary
curve**, and turning/curvature/torsion are its complete intrinsic invariants (a
planar curve is determined up to rigid motion by curvature vs arc length; a space
curve by curvature and torsion). FPFH would describe the surface *beside* the
edge, which is exactly the information that differs between a fragment and its
mate. Torsion additionally gives **chirality**: it flips sign under reflection, so
`SegmentHashKey3D` keeps torsion *variance* (reflection-invariant) while the sign
distinguishes a rim from its mirror — a discriminator FPFH does not provide.

Verdict: **Divergent by design.** FPFH/PFH **Absent**; curve-intrinsic
signatures **Implemented** and better matched to the problem.

Pose-invariance (survey's requirement): Stack A keys are rotation- and
translation-invariant by construction; scale-invariance is opt-in
(`HashOptions.Scale` + `RelativeLengthBinFraction`, binning relative to the median
panel bbox diagonal — the "scale-invariance constraint"). Stack B is
translation- and scale-normalized but **orientation-dependent** (the `EdgeKey`
angle bucket uses the absolute world-XY tangent), so the rail index is
orientation-locked — a real limitation for arbitrarily-rotated inventory.

---

## 4. Spatial organization: geometric hashing / k-d tree / BVH vs. what Frahan uses

**Survey:** geometric hashing (basis-invariant quantization + voting), k-d trees
for the 33-D FPFH nearest-neighbour search, BVH for collision, voxel spatial
hashing.

**Frahan:**

| Survey structure | Frahan | Evidence |
|---|---|---|
| Geometric hashing (basis vote) | **Absent** — uses rotation/translation-invariant *descriptor-bin* hashing + optional query-directed **multi-probe LSH**; no basis frames, no vote accumulator | `SegmentHashIndex.cs` (`KeyOf`, `RankedProbes`) |
| k-d tree | **Absent** — brute-force kNN or a uniform **spatial hash** instead | `SpatialHash3D` (cell = query radius, 27-cell query) accelerates the Soft-ICP E-step |
| BVH | **Absent from matching** (a `TriangleAabbBvh` exists only in the quarry BlockCutOpt path) | — |
| Voxel spatial hash | **Implemented** | `SpatialHash3D` |

The one genuine gap here is **geometric hashing's occlusion/partial-match
robustness**. Descriptor-bin hashing keys the *whole* segment signature; if a rim
is snapped or partially scanned, its global signature shifts bucket. Geometric
hashing votes on *local* basis-relative features, so a partial edge still
accumulates votes. For reclaimed/broken stock (the survey's motivating case) this
is worth having. Absence of a k-d tree is not a real gap: the feature space is
already bucketed, and `SpatialHash3D` gives O(1)-average neighbourhood queries
where they matter (the ICP correspondence step).

---

## 5. Registration: ICP variants / SAC-IA / NDT vs. Kabsch ICP + CPD Soft-ICP

**Survey:** point-to-point ICP, point-to-plane ICP, SAC-IA global init (FPFH +
RANSAC), NDT, ESM-ICP; SVD/quaternion closed-form.

**Frahan:** a coarse-to-fine pipeline built on closed-form Kabsch:

- Coarse init: `InitialTransformBuilder` turns the phase-correlation lag into a
  rigid seed (2D XY frame; 3D discrete Frenet frames), flipping B's frame for the
  complement orientation.
- `ConstrainedIcp2D` — SE(2) ICP, nearest point on B's curve (point-to-curve),
  **closed-form 2D rigid alignment** (Kabsch/Umeyama reduced to `atan2`), with a
  penetration penalty.
- `ConstrainedIcp3D` — SE(3) ICP, 3x3 **Kabsch SVD with a mandatory reflection
  guard** (`det` sign fix), plus penetration and substrate-side rejection.
- `SoftIcpRefiner` — the "Soft" ICP: **CPD soft correspondences**
  (Myronenko-Song 2010) with an outlier mass, a confidence-weighted Kabsch
  M-step, **geometric tau annealing**, penetration folded into the contact
  target, and pose retraction on the **Lie algebra SE(2)/SE(3)** via `Exp`.
- `SoftIcpLbfgs` — an L-BFGS variant (MathNet BFGS over R^{6N}, CPD soft-SSD +
  Huber penetration, numerical gradient).
- Global solver `AssemblySolver` — **beam search** or **Prim minimum-residual
  spanning tree** over the all-pairs match graph, with **cycle-consistency** and
  **best-buddies** outlier penalties.

| Survey | Frahan | Note |
|---|---|---|
| Point-to-point ICP | **Implemented** (Kabsch SVD / atan2, SE2 + SE3) | |
| Point-to-plane ICP | **Absent** | substrate normal is a one-sided *rejection* guard, not a residual |
| SAC-IA (FPFH+RANSAC) | **Absent** | coarse init is phase-correlation + beam/MST, not RANSAC |
| NDT | **Absent** | |
| Soft/robust ICP | **Implemented and ahead** — CPD + annealing + Lie retraction + L-BFGS | survey mentions ESM-ICP only in passing |

Frahan is **ahead** on the robust-registration axis (CPD Soft-ICP with Lie
retraction is a stronger tool than the survey's baseline point-to-plane ICP). The
one worthwhile addition is **point-to-plane** as the fine-stage residual: for the
smooth sawn flanks the survey correctly notes, point-to-plane lets samples slide
along the surface and converges tighter than point-to-point.

---

## 6. Planar curve matching: Hu-moments / shape-context / Frechet vs. Frahan

**Survey:** Hu-moments (`matchShapes`) for rapid culling, Shape Context
(log-polar histogram) for global shape, Frechet distance ("dog-walking") for
final ordered-curve verification before toolpath.

**Frahan:**

| Survey metric | Frahan | Evidence |
|---|---|---|
| Turning-function matching | **Implemented** — `PhaseCorrelator.Correlate` (L1 cyclic cross-correlation of turning signatures, B reversed+negated) | `PhaseCorrelator.cs:13-44` |
| Custom affinity score | **Implemented** — 4-factor product (length, angle, curvature, zone) gated by `MinAffinityScore` | `PackingDescriptors.cs:110-136` |
| DTW | **Partial** — a "DTW-style" monotone non-crossing DP (Marcotte-Suri 1991), a *correspondence* not a distance | `OrderedBoundaryMatcher.cs:66-171` |
| Hausdorff | **Partial** — sampled one-sided Hausdorff on the 3D *block* path, used as tolerance/residual | `BlockPairMatch3DComponent.cs:311` |
| Hu-moments / matchShapes | **Absent** | |
| Shape Context | **Absent** | |
| **Frechet distance** | **Absent** | |

This is the section with the clearest actionable gap. The survey correctly
positions **discrete Frechet** as the *final verification* metric because it
respects the sequential ordering and direction of the mating curve — precisely
the property phase-correlation similarity does not certify. Frahan currently
gates on phase-correlation score then ICP residual. Adding a **discrete Frechet
gate** as the last check before a pair is accepted (and before any cut is
emitted) is cheap (O(nm) DP, same shape as `OrderedBoundaryMatcher`) and directly
raises match precision. This is recommendation R1 below.

---

## 7. Nesting: true-shape / WFC / MILP vs. Frahan's packers

**Survey:** true-shape nesting (NFP), Wave Function Collapse for volumetric
aggregation, MILP stock-constrained assignment with FEA capacity checks.

**Frahan:**

- **True-shape NFP: Implemented.** `NfpRhino` computes the no-fit polygon by
  Minkowski difference (convex hull for convex; ear-clip + per-triangle-pair
  Minkowski hulls + boolean union for concave).
- **NFP-BLF packer: Implemented.** `NfpBottomLeftFillRhino` builds
  `feasible = IFR - union(placed NFPs)`, orders candidates bottom-left, does
  true-shape overlap tests, and runs a light order-metaheuristic.
- **Hole-aware IFP/NFP: Implemented** as `IrregularSheetFill*`
  (`feasible = IFP \ NFP`, packs across multiple non-rectangular sheets with
  holes).
- **Trencadis chipping-fit: Implemented.** `TrencadisFill` — Battiato-2013 cut
  budget, CVD-Lloyd blue-noise seeding, GVF tangent-oriented rotation.
- **WFC: Absent.** No occurrences in source.
- **MILP stock-constrained: Absent.** Named only as an unimplemented
  `MilpSolver (Google.OrTools)` comment in `MatcherRegistry`; no OrTools
  reference.

Verdict: Frahan's **true-shape nesting is strong and shipping** (arguably ahead
of the survey's generic "Almacam" reference). WFC and MILP are genuinely absent.
Of the two, **MILP stock-constrained assignment** (Brutting et al., reclaimed-
element structures) is the higher-value addition because it directly serves the
"inventory dictates layout" reversal the survey emphasises; WFC is a niche
generative method with weaker structural guarantees.

---

## 8. Combinatorial assignment: Hungarian / beam / MST

**Survey:** MILP + WFC for global assembly; the report treats assignment as a
side note.

**Frahan** is actually **stronger than the survey here**:

- `HungarianAssigner` — Kuhn (1955), Jonker-Volgenant dual-potential O(n^3), with
  rectangular padding and a data-scaled big-M for infeasible cells. Used by the
  Voussoir stone matcher and exposed as a GH component.
- `AssemblySolver` beam search + Prim minimum-residual spanning tree, with
  cycle-consistency and best-buddies outlier rejection.

The `MatcherRegistry` advertises seven solver names (Greedy / Hungarian /
Bipartite / MILP / NSGA-II / BruteForce) but **ships only the standalone
`HungarianAssigner`** plus a test wrapper — the registry is substrate, not a
menu of working solvers. Any claim that MILP/NSGA assignment is "in the codebase"
is unsupported. Filling `MatcherRegistry` with the real `MilpSolver` (OrTools)
and a greedy/bipartite baseline would make the substrate honest.

---

## 9. Fabrication tie-in

**Survey:** 6-axis robotic arms, COMPAS FAB + ROS real-time path planning,
URScript, adaptive bandsawing (Ashen Cabin, Bandsawn Bands).

**Frahan:** a real CAM/robot bridge exists but is **not wired to edge-match
output**:

- `StoneCutExportComponent` writes a per-piece `.3dm` with CAM metadata
  (bed/grain, finish, weight, kerf) for EasySTONE/Alphacam/Breton/Lantek.
- `PlanesToRobotTargetsComponent` (visose/Robots, 6-vendor), plus
  `PlanesToKukaPrcCommands` (KUKA|prc), `WireSawToolpathAdapter`,
  `GCodeToPlanes` / `GCodeParser`.
- **No** URScript, **no** COMPAS/`compas_fab`, **no** ROS emitter in `src/`.
- The fabrication chain consumes the **slab-cut / quarry / masonry** pipeline,
  not `AssemblySolver` / Trencadis match output. There is no component that feeds
  a matched-edge assembly into the cut export.

Verdict: **Partial.** The robot/CAM substrate is real (and 6-axis-capable via the
Robots plugin), but the survey's specific stack (COMPAS FAB + ROS + URScript) is
absent, and the edge-match -> fabrication link is missing. Wiring
`AssemblySolver` / Trencadis output (matched rims + scribe/trim curves) into
`StoneCutExportComponent` is recommendation R4.

---

## Where Frahan is ahead of the survey

1. **Signed torsion for chirality.** Mirror disambiguation of 3D rims via torsion
   sign; the survey's FPFH pipeline has no chirality channel.
2. **CPD Soft-ICP with Lie-algebra retraction + annealing + L-BFGS.** A stronger
   robust-registration tool than the survey's point-to-plane ICP baseline.
3. **Scale-relative descriptor binning.** Explicit unit/size invariance
   (mm to quarry) via `HashOptions.Scale`; the survey assumes fixed metric bins.
4. **Cycle-consistency + best-buddies outlier rejection** in the global MST/beam
   solver; the survey stops at per-pair RANSAC scoring.
5. **The tessellation-invariance insight.** `ProjectionPairFinder` documents that
   independently-tessellated rims never hash-match (R0: cross-panel hits = 0), and
   works around it with a per-facet PCA projection to a scale-relative 2D
   resampling. This is a real, hard-won finding the survey does not anticipate.

## Genuine gaps, prioritised

- **R1 — Discrete Frechet verification gate (high value, low cost).** Add a
  discrete Frechet check as the final accept test on a matched rim pair, before
  emitting any cut. Same O(nm) DP shape as `OrderedBoundaryMatcher`. Directly
  raises precision on the ordered mating curve (the survey's stated use for
  Frechet).
- **R2 — Geometric hashing for partial/occluded rims (medium).** Descriptor-bin
  hashing misses snapped/partially-scanned edges. A basis-vote geometric hash (or
  local-window sub-signatures) recovers partial matches for broken reclaimed
  stock.
- **R3 — Point-to-plane ICP fine stage (medium).** Lets samples slide along
  smooth sawn flanks; converges tighter than the current point-to-point residual.
- **R4 — Wire edge-match output into fabrication (medium).** Feed
  `AssemblySolver` / Trencadis matched-rim + trim curves into
  `StoneCutExportComponent`; add a live-edge example that closes match -> cut.
- **R5 — MILP stock-constrained assignment (medium, larger).** Implement the
  advertised `MilpSolver` (OrTools) in `MatcherRegistry` for the
  "inventory dictates structural layout" reuse problem (Brutting et al.); couple
  to the existing RBE stability check for a capacity gate. Higher value than WFC.
- **R6 (optional) — Stack B rotation invariance.** The rail `EdgeKey` angle
  bucket is world-locked; a relative-angle keying (or rotating candidates to a
  canonical frame) would let the rail matcher handle arbitrarily-oriented
  inventory like Stack A already does.

## Corrections to the survey's framing

- The survey treats "no FPFH" as a deficiency; for edge (curve) matching it is
  the correct modelling choice, and Frahan's curve-intrinsic signatures plus
  torsion chirality are arguably superior for the mating problem.
- The survey's MILP/NSGA/WFC assignment claims map to **named-but-unimplemented**
  registry entries in Frahan, not shipping code.
- The survey's two example flows (a live-edge fabrication handoff, an NBO-to-robot
  path) exist as capabilities in adjacent pipelines but are **not** connected to
  the edge-matcher; no single example demonstrates match -> fabrication end to
  end.

## Source map

| Layer | Primary files |
|---|---|
| Curve signatures (2D/3D) | `Frahan.EdgeMatching.Core/{Segment,BoundarySegmenter,BoundarySegmenter3D}.cs` |
| Hash index + retrieval | `Frahan.EdgeMatching.Core/{SegmentHashKey,SegmentHashIndex}.cs` |
| Phase correlation | `Frahan.EdgeMatching.Core/PhaseCorrelator.cs` |
| Constrained ICP (SE2/SE3) | `Frahan.EdgeMatching.Core/{ConstrainedIcp2D,ConstrainedIcp3D}.cs` |
| Soft-ICP (CPD) + L-BFGS | `Frahan.EdgeMatching.Core/{SoftIcpRefiner,SoftIcpLbfgs}.cs` |
| Global assembly | `Frahan.EdgeMatching.Core/AssemblySolver.cs` |
| Ordered / DTW-style matcher | `Frahan.EdgeMatching.Core/OrderedBoundaryMatcher.cs` |
| Optimal assignment | `Frahan.EdgeMatching.Core/HungarianAssigner.cs` |
| Solver substrate | `Frahan.EdgeMatching.Core/Matching/MatcherRegistry.cs` |
| Boundary-rail (Stack B) | `Frahan.StonePack.Core/{BoundaryRailMatcher,PackingDescriptors}.cs` |
| 3D projection bootstrap | `Frahan.EdgeMatching.Core/ProjectionPairFinder.cs` |
| True-shape nesting | `Frahan.StonePack.GH/TwoD/{NfpRhino,NfpBottomLeftFillRhino,IrregularSheetFill*,TrencadisFill}.cs` |
| Fabrication export | `Frahan.StonePack.GH/Fabrication/{StoneCutExportComponent,PlanesToRobotTargetsComponent}.cs` |
