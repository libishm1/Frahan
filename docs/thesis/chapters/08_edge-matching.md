# 08. Edge-Matching & Fragment Reassembly

This chapter covers the repository's fragment-reassembly subsystem: the
`EdgeMatch` ribbon tab (10 components) and the Rhino-free engine it wraps,
`Frahan.EdgeMatching.Core`, together with the closed-form rigid-registration
kernels in `Frahan.StonePack.Core/Registration`. The task is fracture
reassembly: given the broken pieces of a stone object (a Trencadís mosaic, a
shattered ceramic, a quarry block split along a joint) recover the rigid
motion that brings every mating boundary back into contact without
interpenetration.

The engine is a five-stage pipeline declared verbatim on the solver component
(`EdgeMatchSolveComponent.cs:23-27`):

1. **Boundary segmenter** — split each contour at curvature breaks, build a
   signed-turning signature per segment.
2. **Segment hash index** — rotation/translation-invariant bucketing for
   complement lookup.
3. **Phase correlator** — coarse lag between two turning signatures.
4. **Constrained ICP** — closed-form rigid alignment, refined to convergence
   (Besl and McKay 1992; MathNet SVD Kabsch).
5. **Assembly solver** — beam-search (frame-anchored) or agglomerative
   minimum-residual spanning tree (R0).

Three opt-in research increments ride on top of the five stages: A1 (scale-
relative gates), R1 (partial sub-segment matching), R2 (global non-overlap
resolve), the Soft-ICP refiner (Coherent Point Drift soft correspondence plus a
non-penetration hinge), and the projection bootstrap that unblocks the
geometric 3D path. Every increment defaults OFF, so the shipped default path is
byte-for-byte the original beam (`AssemblyOptions.cs:28`, `:44`, `:93`, `:135`,
`:197`, `:229`).

The mathematics here is rigid-motion estimation on $SE(2)$ and $SE(3)$: the
turning-angle edge descriptor, the orthogonal-Procrustes / Kabsch rigid fit and
its mandatory reflection guard, a monotone non-crossing correspondence DP, and a
soft-assignment EM alternation. Where the repository evolved the published math
we show the derivation. Every originality claim is anchored to a `file:line`, an
`[Algorithm]` attribute, or a measured benchmark.

---

## 1. The edge descriptor: signed-turning signature

### 1.1 Definition

A fracture rim is a planar (or per-facet planar) open curve. The descriptor is
the **signed turning angle** sampled at even arc length. For three consecutive
resampled points $p_{i-1},p_i,p_{i+1}$ in the panel-local XY plane, with edge
vectors $u=p_i-p_{i-1}$ and $v=p_{i+1}-p_i$, the turning at $p_i$ is

$$
\tau_i=\operatorname{atan2}\!\big(u_x v_y - u_y v_x,\; u_x v_x + u_y v_y\big)\in(-\pi,\pi].
$$

The cross product $u_xv_y-u_yv_x$ carries the sign (left turn positive, right
turn negative); the dot product disambiguates the quadrant. This is exactly
`BoundarySegmenter.SignedTurn` (`BoundarySegmenter.cs:151-158`). The signature
of a segment is the turning sequence resampled to a fixed bin count
`SignatureBins` by piecewise-linear interpolation
(`BoundarySegmenter.cs:74`, `:171-189`), so two rims of different vertex counts
are comparable.

### 1.2 Why signed turning is the canonical 2D signal

Turning angle is **invariant under rigid motion**: rotating or translating the
curve leaves every $\tau_i$ unchanged, because $\tau_i$ is built only from
relative edge directions. It is the discrete analogue of the curve's signed
curvature integrated over arc length,

$$
\tau_i \approx \int_{s_{i-1}}^{s_{i+1}} \kappa(s)\,ds ,
$$

so the per-segment total $\sum_i\tau_i$ is the segment's net turning
(`TotalTurning`, `BoundarySegmenter.cs:70-72`), a rotation-invariant scalar used
directly as a hash bin. The sign of the total fixes the segment's convex/concave
character (`Sign`, `:72`), which is the key fact for matching: two mating
fracture rims traverse the *same physical curve in opposite senses*, so their
turning signatures are **reverse-and-negate** images of each other.

### 1.3 Break-point detection

Segment boundaries are placed where the windowed turning sum exceeds a threshold
$\theta=\texttt{BreakAngleDeg}$ (`BoundarySegmenter.cs:37-45`):

$$
\Big|\sum_{k=-w}^{w}\tau_{i+k}\Big| > \theta \;\Rightarrow\; \text{break at } i,
$$

with window half-width $w=\texttt{BreakWindow}$. Nearby breaks are coalesced
(`:160-169`) so a single sharp corner yields one break, not a cluster. The 3D
sibling `BoundarySegmenter3D` adds a torsion signature (the out-of-plane
signal), null for planar panels, used only for 3D mirror disambiguation
(`Segment.cs:25`, `:9-13`).

**Originality.** The signed-turning descriptor and the break-point segmenter are
**clean-room**, built from the standard turning-function representation of plane
curves (Arkin et al. 1991). The `[Algorithm("Boundary segmenter",
"Frahan-original arc-length curvature/torsion signature")]` attribute
(`EdgeMatchSolveComponent.cs:23`) claims the *implementation*, not the turning
function itself. Tier B/C (faithful reimplementation of a textbook
representation).

---

## 2. The segment hash index

To avoid an all-pairs comparison the segments are bucketed by a
rotation/translation-invariant key (`SegmentHashKey.cs`). The 2D key is the
five-tuple

$$
k(s)=\Big(\big\lfloor \tfrac{L}{b_L}\big\rceil,\ \big\lfloor\tfrac{T}{b_T}\big\rceil,\ \big\lfloor\tfrac{\mu}{b_\mu}\big\rceil,\ \big\lfloor\tfrac{\sigma}{b_\sigma}\big\rceil,\ \operatorname{sign}(s)\Big),
$$

with $L$ the chord length, $T$ the total turning, and $\mu,\sigma$ the mean and
standard deviation of the turning signature, each quantised by its bin size
(`SegmentHashIndex.cs:KeyOf`). Every term is rigid-invariant, so two segments
fall in the same bucket only if they are plausibly the same shape up to pose. A
**complement query** looks up the bucket of the reverse-and-negate image of a
segment, returning candidate mating edges in $O(1)$ expected time
(`SegmentHashIndex.cs:97`, `QueryComplement`). The 3D key extends the tuple with
planarity and torsion-variance bins (`SegmentHashKey3D`), and 2D and 3D segments
never cross-match by construction (`SegmentHashIndex.cs:12-15`).

**Originality.** **clean-room / Frahan-original** invariant hashing
(`[Algorithm("Segment hash index", "Frahan-original planarity-aware spatial
bucketing")]`, `EdgeMatchSolveComponent.cs:24`). Tier C: a standard
locality-style index, but the planarity-aware 2D/3D split is the local
increment. A known limitation is recorded in section 8: independently tessellated
3D rims hash to disjoint buckets (cross-panel hits = 0), which is precisely why
the projection bootstrap exists.

---

## 3. Phase correlation: the coarse lag

Before any ICP the matcher finds the best cyclic alignment of two equal-length
turning signatures $a,b$. Because mating rims are reverse-and-negate images, the
second signature is first flipped, $b^{\flat}_i=-b_{n-1-i}$
(`PhaseCorrelator.cs:23-24`), then the lag minimising the $L_1$ disagreement is
chosen:

$$
\ell^\star=\arg\min_{\ell\in[0,n)}\ \sum_{i=0}^{n-1}\big|\,a_i - b^{\flat}_{(i+\ell)\bmod n}\,\big|.
$$

The score is normalised to a similarity in $[0,1]$ using the fact that
per-sample turning is bounded by $\pi$, so the worst-case disagreement is
$2\pi n$ (`PhaseCorrelator.cs:39-43`):

$$
\mathrm{sim}=1-\frac{\min_\ell \sum_i |a_i-b^{\flat}_{(i+\ell)}|}{2\pi n}\in[0,1].
$$

A pair is admitted only if $\mathrm{sim}\ge\texttt{PhaseScoreThreshold}$
(default $0.5$, `AssemblyOptions.cs:65`). This is the cheap gate that keeps the
expensive ICP off non-complementary pairs.

**Originality.** **clean-room** classical cross-correlation lag estimation
(`[Algorithm("Phase correlator FFT", "Classical cross-correlation lag
estimation")]`, `EdgeMatchSolveComponent.cs:25`). The attribute name says "FFT";
the shipped code is a direct $O(n^2)$ circular $L_1$ correlation, not an FFT,
which is correct and deterministic at these signature lengths (a documentation
nit, flagged in section 9). Tier C.

---

## 4. Constrained ICP and the rigid-fit derivation

Stage 4 is the geometric heart: iterative closest point with a closed-form
rigid update per iteration, plus a penetration guard. Two implementations exist,
`ConstrainedIcp2D` and `ConstrainedIcp3D`, sharing the same outer loop
(Besl and McKay 1992).

### 4.1 The ICP outer loop

Given source segment $A$ (sampled to points $\{a_i\}$) and target $B$ (a
polyline curve), each iteration (`ConstrainedIcp3D.cs:60-142`):

1. Push $A$ through the current pose $g_k\in SE(d)$: $a_i^{(k)}=g_k\,a_i$.
2. **Correspondence**: for each $a_i^{(k)}$ find the closest point $b_i$ on $B$
   (`bCurve.ClosestPoint`, `:109-110`), or, when the order-preserving mode is
   on, a monotone non-crossing pairing (section 5).
3. **Alignment**: solve the closed-form rigid fit
   $\delta=\arg\min_{g}\sum_i\|g\,a_i^{(k)}-b_i\|^2$ and update
   $g_{k+1}=\delta\,g_k$ (`:116-118`).
4. **Guard**: reject the step if it would push $A$'s centroid inside $B$'s
   panel interior, or onto the wrong side of a substrate; penalise the residual
   instead of accepting (`:119-128`).

The residual is the mean correspondence distance; convergence is declared when
the per-step translation and rotation both fall below tolerance, or the residual
stalls (`:131-140`). The monotone convergence of this alternation to a local
minimum is the standard ICP result (Besl and McKay 1992): the correspondence
step can only lower (or hold) the residual, and the optimal-fit step minimises it
exactly, so the residual is non-increasing.

### 4.2 The 3D rigid fit (Kabsch / orthogonal Procrustes via SVD)

The inner solve is the orthogonal-Procrustes problem. We want $R\in SO(3)$ and
$t\in\mathbb{R}^3$ minimising

$$
E(R,t)=\sum_{i=1}^{n}\|R\,a_i+t-b_i\|^2 .
$$

**Derivation.** Centre both sets: let
$\bar a=\tfrac1n\sum_i a_i$, $\bar b=\tfrac1n\sum_i b_i$, and
$a_i'=a_i-\bar a$, $b_i'=b_i-\bar b$. For fixed $R$ the optimal translation is
found by $\partial E/\partial t=0$, giving $t=\bar b-R\bar a$, which cancels the
centroids and leaves

$$
E(R)=\sum_i \|R\,a_i'-b_i'\|^2
   =\sum_i\big(\|a_i'\|^2+\|b_i'\|^2\big)-2\operatorname{tr}\!\big(R\,H\big),\quad
H=\sum_i a_i'\,{b_i'}^{\!\top}.
$$

Minimising $E(R)$ is therefore maximising $\operatorname{tr}(RH)$ over rotations.
Take the SVD $H=U\Sigma V^\top$. Then
$\operatorname{tr}(RH)=\operatorname{tr}(R U\Sigma V^\top)=\operatorname{tr}(\Sigma\,V^\top R U)$,
and since $V^\top R U$ is orthogonal its diagonal entries are $\le 1$, so the
trace is maximised when $V^\top R U=I$, i.e.

$$
\boxed{\,R=V\,U^\top\,}.
$$

This is the cross-covariance $H$ assembled at `ConstrainedIcp3D.cs:158-166`, the
SVD at `:168`, and $R=VDU^\top$ at `:179`.

**The reflection guard.** $VU^\top$ can have determinant $-1$ (a reflection,
the unconstrained orthogonal-Procrustes minimiser) when the points are coplanar
or noisy. To force $R\in SO(3)$ we insert a sign-fixing diagonal
$D=\operatorname{diag}(1,1,d)$ with

$$
d=\operatorname{sign}\!\big(\det(VU^\top)\big),\qquad R=V D U^\top,
$$

which flips the smallest-singular-value axis only, the provably minimal change
that restores a proper rotation (Umeyama 1991). The repository's implementation
makes a hard correctness point at `ConstrainedIcp3D.cs:172-177`: the sign is
computed with `Matrix.Determinant()`, **not** `Math.Sign`, because
`Math.Sign` returns $0$ on a degenerate determinant and would silently skip the
flip, returning a mirror-image alignment in place of a rotation. The code comment
flags this as spec item D8; the same guard is duplicated in the Soft-ICP M-step
(`SoftIcpRefiner.cs:439-442`). The translation is then recovered as
$t=\bar b-R\bar a$ (`:185-187`).

### 4.3 The 2D rigid fit reduces to a single atan2

In the plane the rotation has one degree of freedom, so the SVD collapses. With
the centred cross-covariance entries
$S_{xx}=\sum a_x'b_x'$, $S_{xy}=\sum a_x'b_y'$, $S_{yx}=\sum a_y'b_x'$,
$S_{yy}=\sum a_y'b_y'$, maximising $\operatorname{tr}(R(\theta)H)$ over the
planar rotation $R(\theta)$ gives, after $\partial/\partial\theta=0$,

$$
\theta^\star=\operatorname{atan2}\!\big(S_{xy}-S_{yx},\ S_{xx}+S_{yy}\big),
$$

exactly `ConstrainedIcp2D.SvdRigid2D` (`ConstrainedIcp2D.cs:163-193`). This is
the planar Kabsch in closed form with no matrix factorisation, the same answer
the SVD would give but cheaper and branch-free.

### 4.4 The penetration guard

The matcher rewards edge contact but must not let a piece's body pass through its
neighbour. After each trial pose the 3D guard projects $A$'s centroid onto $B$'s
local frame and tests containment in $B$'s flattened contour
(`ConstrainedIcp3D.cs:191-213`); if inside, the step is rejected and the residual
is multiplied by `PenetrationPenalty` so the beam de-ranks it
(`:127`). An optional substrate test rejects any sample on the wrong side of a
backing surface normal (`:215-237`). The 2D guard is the same idea on the closed
contour (`ConstrainedIcp2D.cs:114-117`).

**Originality.** The ICP loop and SVD/atan2 rigid fit are **clean-room**
implementations of published math (`[Algorithm("Constrained ICP", "Besl and
McKay 1992 iterative closest point; MathNet.Numerics SVD")]`,
`EdgeMatchSolveComponent.cs:26`; the SVD itself is the **vendored**
`MathNet.Numerics` package). The *constrained* adjective is the local increment:
the centroid-penetration and substrate-side guards have no equivalent in vanilla
ICP and turn a registration routine into a non-overlapping assembler. Tier B with
a B-level engineering delta.

---

## 5. Order-preserving correspondence: a monotone non-crossing DP

Free nearest-neighbour ICP can produce tangled correspondences on wiggly rims:
$a_i$ matches $b_j$ while $a_{i+1}$ matches $b_{j-3}$, so the matched chords
cross and the fit is biased. The opt-in non-crossing mode
(`AssemblyOptions.cs:44`) replaces nearest-neighbour with a monotone dynamic
program (`OrderedBoundaryMatcher.cs`).

The correspondence must be **monotone**: if $(i,j)$ and $(i',j')$ are both
matched with $i<i'$ then $j\le j'$. This is enforced by a Dynamic-Time-Warping
style DP over the $n\times m$ squared-distance grid
$C_{ij}=\|a_i-b_j\|^2$ with three monotone moves into cell $(i,j)$:

$$
\mathrm{cost}_{ij}=C_{ij}+\min\big(\mathrm{cost}_{i-1,j-1},\ \mathrm{cost}_{i-1,j},\ \mathrm{cost}_{i,j-1}\big),
$$

diagonal (advance both, emit a pair), up (advance $A$, gap in $B$), left
(advance $B$, gap in $A$), with the diagonal preferred on ties so the path hugs
the main correspondence and stays deterministic
(`OrderedBoundaryMatcher.cs:100-131`). Backtracking from $(n-1,m-1)$ emits a pair
only on diagonal moves, which makes the emitted correspondence both non-crossing
and one-to-one (`:145-167`). An optional index band `maxGap` bounds how far the
two running indices may diverge, with a graceful fall back to the unbounded DP if
the band is too tight (`:133-138`).

Closed rims have no canonical start and may run in either sense, so
`MatchClosed` brackets a set of cyclic offsets (seeded by the phase-correlation
lag) and tries both the forward and reversed orientation, keeping the candidate
with the lowest mean matched distance (`:188-241`).

**Originality.** **clean-room** with an explicit, honest provenance note. The
file header (`OrderedBoundaryMatcher.cs:24-37`) cites the minimum-weight
non-crossing matching idea of Marcotte and Suri (1991) and the user's own
reference implementation, then states plainly that a verbatim port does *not*
apply: Marcotte-Suri matches points among themselves on a single convex polygon
in $O(N\log N)$, whereas this problem matches points *between* two arbitrary
non-convex rims, for which the monotone DP is the correct general primitive. The
DTW lineage is the engineering substrate. Tier C, with the non-crossing-on-two-
sequences framing as the local increment.

---

## 6. The initial transform

ICP needs a basin-of-attraction seed. `InitialTransformBuilder.FromLag2D`
(`InitialTransformBuilder.cs:16-40`) anchors a plane at $A$'s start point with
its first edge as the x-axis, and a plane at $B$'s lag-shifted sample with the
**negated** local tangent as its x-axis, then returns the plane-to-plane map.
The negation enforces the complement orientation: matching edges traverse the
fracture in opposite senses, so $B$'s frame is flipped. The 3D variant
`FromLag3D` (`:42-51`) builds discrete Frenet frames (tangent plus principal
normal from the second difference, `:53-65`) and again flips $B$'s tangent. This
seed places $A$'s leading edge against $B$'s complement edge in the right sense
before the first ICP step; the iterations then tighten the whole rim.

**Originality.** **clean-room** geometric construction (no `[Algorithm]`
attribute; it is plumbing for stages 3-4). Tier D scaffolding.

---

## 7. Soft-ICP refinement: CPD soft correspondence + non-penetration hinge

Hard ICP commits to a single nearest neighbour per sample, which is brittle when
rims are perturbed or partially overlapping. The Soft-ICP refiner
(`SoftIcpRefiner.cs`, surfaced as the `Soft ICP 3D` component, GUID
`D5F1000E`) replaces hard correspondence with the soft-assignment
Expectation-Maximisation of Coherent Point Drift (Myronenko and Song 2010) and
adds a smooth non-penetration term.

### 7.1 The objective

The refiner minimises one objective over the placed fragment poses
(`SoftIcpRefiner.cs:14-21`):

$$
\mathcal{L}(\{g_f\})=w_{\text{contact}}\underbrace{\sum_i c_i\,\|g_f p_i-\bar q_i\|^2}_{\text{soft rim SSD}}\;+\;w_{\text{pen}}\underbrace{\sum \lambda\,\max(0,\text{depth})^2}_{\text{penetration hinge}}.
$$

### 7.2 E-step: soft correspondence

For a rim sample $p_i$ of fragment $f$ and the rim samples $q_j$ of all other
fragments, the responsibilities are a temperature-controlled softmax with a
uniform outlier mass $\pi_0$ that auto-downweights non-overlapping rim tails
(`SoftIcpRefiner.cs:299-374`):

$$
w_{ij}=\frac{\exp\!\big(-\|g_f p_i-q_j\|^2/\tau\big)}{\pi_0+\sum_{j'}\exp\!\big(-\|g_f p_i-q_{j'}\|^2/\tau\big)},\qquad
\bar q_i=\sum_j w_{ij}\,q_j,\quad c_i=\sum_j w_{ij}.
$$

$\bar q_i$ is the soft target and $c_i\in[0,1)$ its confidence. The temperature
$\tau$ is **scale-relative**: $\tau_0=(\text{median rim spacing})^2$, annealed
geometrically each outer iteration toward a spacing-relative floor
(`:189-214`, `:283-288`), with a coarse-to-fine correspondence radius that starts
wide enough to *catch* a perturbed rim and sharpens to the true contact
(`:198-207`). This is the scale-invariance constraint of section 10 applied
directly to the EM temperature.

### 7.3 M-step: confidence-weighted Kabsch

Given $\{(p_i,\bar q_i,c_i)\}$ the pose increment is the confidence-weighted
rigid fit, the same orthogonal-Procrustes solve of section 4.2 but with the
covariance weighted by $c_i$ (`SoftIcpRefiner.cs:381-453`):

$$
H=\sum_i c_i\,(p_i-\bar p_c)(\bar q_i-\bar q_c)^\top,\qquad R=V D U^\top,
$$

with $\bar p_c,\bar q_c$ the confidence-weighted centroids and the same
$\det$-based reflection guard (`:439-442`). In 2D it collapses to the weighted
atan2 (`:402-422`). The increment is damped by a step factor and retracted
through the Lie exponential so a fractional step is still a proper $SE(3)$
element, not a matrix lerp (`DampDelta`, `:788-828`, using `LieSe3.ExpSo3`).

### 7.4 The non-penetration coupling

Rather than a separate ejecting translation that could fight the contact pull,
any moving sample found *inside* a neighbour solid has its soft target
**redirected** to the neighbour surface point with full confidence
(`ApplyPenetrationTargets`, `:468-527`). The single weighted-Kabsch then finds
the rigid motion that brings rims to contact and lifts penetrating samples to the
boundary at once, so the contact and non-penetration terms cannot oppose. This
realises the smooth hinge $\max(0,\text{depth})^2$ by pulling penetrating samples
exactly to $\text{depth}=0$. The 3D inside-test uses `Mesh.IsPointInside`; the 2D
test uses closed-contour containment, the same primitives as the masonry contact
settle (`:482-525`). Fragment 0 is anchor-locked and the whole loop is
deterministic (fixed iteration order, no randomness).

**Originality.** **clean-room / evolved-fork.** The soft correspondence is a
faithful implementation of Coherent Point Drift (`[Algorithm("Soft-ICP / CPD
weighted-Kabsch alternation", "Myronenko and Song 2010 Coherent Point Drift;
Frahan EM closed-form M-step")]`, `SoftIcp3DComponent.cs:43-45`), with the
weighted-Kabsch M-step citing Kabsch (1976/1978) and the vendored MathNet SVD
(`:49-51`). The evolved-fork delta is the unified contact-plus-non-penetration
target redirection: folding the penetration hinge into the *same* weighted-Kabsch
target is the local contribution, not present in CPD, and is what makes the
refiner a reassembler rather than a registrar. A measured delta is recorded in
the roadmap: the refiner is the +97% clearance / open-rim-contact stage of the
settle pipeline. Tier B with a B-level delta.

![Kintsugi fracture reassembly, Breaking Bad parity, pair score 0.71 STRONG](../examples/14_kintsugi/14_kintsugi_result.png)

---

## 8. The projection bootstrap: unblocking the geometric 3D path

The geometric 3D path has a structural blocker, proved by run R0 and recorded on
the finder (`ProjectionPairFinder.cs:16-22`): independently tessellated shard
rims triangulate the shared cut differently, so the hash index finds **zero**
cross-panel complements (hash hits: self 172, cross-panel 0). The agglomerative
solver then has an empty pair graph and assembles nothing.

The key geometric fact that breaks the deadlock: for an open-shell shard the
naked rim along a cut facet **lies in that facet plane**, so two mating shards
share a plane, and per-facet projection reduces 3D rim matching to the *working*
2D matcher. The pipeline (`ProjectionPairFinder.cs:23-48`):

1. **Split and fit.** Split each naked rim loop into maximal planar facet arcs;
   fit each facet plane by PCA, taking the covariance
   $\Sigma=\tfrac1n\sum (x_i-\bar x)(x_i-\bar x)^\top$, its symmetric
   eigendecomposition, the smallest-eigenvalue eigenvector as the normal, and
   $\sqrt{\lambda_{\min}}$ as the RMS out-of-plane residual
   (`FitFacetPlane`, `:481-543`). Low-planarity arcs are flagged and excluded.
2. **Project and resample.** Map each arc into its facet plane (`PlaneToPlane`
   $\to$ WorldXY) and resample at a scale-relative spacing, tessellation-invariant
   (`:362-383`). Build a 2D `Panel`.
3. **Match in 2D.** Run the working stages 1-4 across fragments to get
   complementary pairs and a 2D in-plane relative transform $M_{2D}$.
4. **Lift to $SE(3)$.** The 2D match lifts to a 3D candidate pose
   (`Lift`, `:607-619`):

$$
T_{\text{rel}}=\mathrm{Lift}_{B^{\flat}}\cdot M_{2D}\cdot \mathrm{Proj}_A,
$$

with $\mathrm{Proj}_A=\mathrm{PlaneToPlane}(\text{facet}_A,\text{WorldXY})$ and
$\mathrm{Lift}_{B^{\flat}}=\mathrm{PlaneToPlane}(\text{WorldXY},\text{facet}_B^{\flat})$,
where $\text{facet}_B^{\flat}$ is the parent facet with its normal flipped so the
lifted child facet normal is **antiparallel** to the parent's outward normal
(opposite sides of the fracture).

### 8.1 Projection proposes, 3D disposes

A 2D shadow match has a normal-sign ambiguity, so both lift senses are tried
(flip the parent or flip the child, `Lift` vs `LiftChildFlip`, `:621-636`) and
each is *verified in 3D*: the symmetric mean nearest-neighbour distance between
the lifted child world arc and the parent world arc (`Residual3D`, `:643-678`).
A candidate whose 3D residual exceeds `ProjectionVerifyFactor * objectScale`
(default 12%) is a projection-ambiguous false positive and is dropped
(`:268-270`). This is the "projection proposes, 3D disposes" gate: a genuine
mating facet has a small 3D residual; a shadow match that lost depth has a large
one. Measured on the 6-shard fixture (roadmap, 2026-05-25): 67 cross-panel 2D
matches where R0 had 0, 25 lifted-and-3D-verified pairs, 6/6 fragments placed via
the agglomerative MST. The honest caveat is also recorded: the assembly is only
*partially* tight (2 of 5 MST interfaces fully in contact), so the bootstrap
proposes correctly but Soft-ICP does not yet tighten every edge.

**Originality.** **original-research (A-candidate).** The per-facet projection
bootstrap, the antiparallel lift composition, and the 3D-disposes verification
gate have no equivalent in the local corpus and no `[Algorithm]` attribute citing
an upstream source; the design basis is the in-repo
`wiki/algorithms/edge_matching/projection_bootstrap_3d.md`. A prior-art sweep is
pending, so per AGENTS.md §9 this is marked A-candidate, not asserted novel. The
PCA plane fit it builds on is clean-room textbook. Tier A.

---

## 9. The rigid-registration kernels

Two closed-form rigid-fit kernels live beside the matcher, and the chapter must
keep them distinct.

**Kabsch / SVD (the ICP kernel).** `ConstrainedIcp3D.Kabsch3D`
(`ConstrainedIcp3D.cs:147-189`) and the Soft-ICP M-step use the
orthogonal-Procrustes SVD solve of section 4.2 on the vendored `MathNet.Numerics`
package. This is the per-iteration registration inside ICP (Besl and McKay 1992;
Kabsch 1976). **clean-room** algorithm over a **vendored** SVD.

**Horn quaternion (the marker / georeference kernel).**
`RigidTransformRecovery` (`Frahan.StonePack.Core/Masonry/Geometry`) solves the
*same* absolute-orientation problem by Horn's unit-quaternion method: build the
$4\times4$ symmetric profile matrix $N$ from the nine cross-covariance entries
(Horn eq. 25), take its largest-eigenvalue eigenvector as the optimal quaternion
via cyclic Jacobi, and read off $R$ and $t=\bar b-R\bar a$
(`RigidTransformRecovery.cs:6-30`). It is pure-managed with **no** SVD
dependency and is exposed through the `RegistrationApi` facade
(`RegistrationApi.cs:9-29`, `:57-108`) and the georeference component
(`[Algorithm("Absolute orientation + UTM/EPSG transform", "Horn, B.K.P. (1987).
Closed-form solution of absolute orientation using unit quaternions...")]`,
`GeoreferenceComponent.cs:36`). **clean-room** implementation of Horn (1987).

The two kernels solve the identical least-squares problem
$\min_{R,t}\sum\|Ra_i+t-b_i\|^2$ by two routes (SVD vs quaternion eigenproblem)
and agree numerically. The repository's own architecture audit flags this as a
**keep-core duplication**: three Horn/Kabsch absolute-orientation copies
(`RigidTransformRecovery`, the Georeference private Horn, and the
`Kabsch3D`/`SoftIcpRefiner` SVD) to be unified on one MathNet-SVD kernel. That is
a refactor item, not a correctness bug; both routes are correct.

---

## 10. Scale invariance

Real fragments span seven orders of magnitude: fracture aperture ~1.5-2 mm,
Kintsugi/Trencadís shards 5-15 cm, dimension-stone blocks ~3 m, quarry blocks
~21 m, study areas km-scale. The same mating logic must hold at every scale, so
**every threshold is expressed as a fraction of object size**, never an absolute
millimetre. Concretely:

- **A1 acceptance gates.** The original residual gate was an absolute model-unit
  distance, which rejects everything on a mm fracture and is meters of slack on a
  100-unit block. The A1 increment makes it
  $\texttt{ResidualThresholdFactor}\times \text{objectScale}$
  (`AssemblyOptions.cs:67-75`), objectScale being the combined bounding-box
  diagonal. Default 0 keeps the absolute gate, so existing behaviour is unchanged.
- **Soft-ICP.** $\tau_0$, the correspondence radius, the contact band, the
  convergence translation, and the penetration tolerance are all multiples of the
  median rim spacing or the object diagonal (`SoftIcpRefiner.cs:189-214`,
  `:277-278`).
- **Projection bootstrap.** Sample spacing, planarity flag, and the 3D-disposes
  gate are fractions of the rim-loop diagonal
  (`AssemblyOptions.cs:231-265`).

The pattern follows the existing `MeshContactDetector.adaptiveToleranceFactor`
(5% of median edge). Benchmark errors are reported as fractions of object size so
metrics compare across the ~100-unit synthetic fixtures and the 5-15 cm real
objects.

---

## 11. The five-stage solver and the Trencadís case

The assembly solver runs the two modes declared on the options component
(`EdgeMatchOptionsComponent.cs:31-32`): the **frame-anchored** beam (the
original; every candidate is refined against panels already placed, growing from
an anchor, which suits the 2D Trencadís case where shards mate to a frame placed
first) and the **agglomerative** model (R0; match all pairs, build a weighted
pair graph, compose a minimum-residual spanning tree, which suits free 3D
reassembly where no single frame touches all shards). The two non-overlap
increments R1 (partial sub-segment emission so a long edge can reach the short
edge it mates with, `BoundarySegmenter.cs:115-139`) and R2 (an overlap penalty,
edge exclusivity, and a Jacobi depenetration polish, `AssemblyOptions.cs:111-177`)
drove the measured 2D overlap from 12-25% to 0% with 8/8 placed and 100% union
coverage (roadmap, 2026-05-25).

The Trencadís component is the geometric path's flagship: a centroidal-Voronoi
cell partition plus optimal one-to-one shard assignment, placing 28 irregular
shards into 28 cells with a grout gap, each piece used exactly once. The 3D
`Block Pair Match` component (GUID `D5F10008`) chains a variational shape
approximation segmenter, the 3D phase correlator, the constrained 3D ICP, and a
Hungarian assignment (`BlockPairMatch3DComponent.cs:43-52`); its VSA stage is a
**stub** (`Cohen-Steiner, Alliez, Desbrun 2004 SIGGRAPH; Frahan stub
implementation`, `:43-45`), and the component's own `[RelatedComponent]` points
users at the practically-tested Hungarian Stone-Cell Match for real stone-to-cell
work today (`:41`).

![Trencadís mosaic, 28 shards into 28 Voronoi cells, each used once](../examples/12_trencadis/12_trencadis_result.png)

---

## Status & what's left

- **Geometric 3D reassembly is gated by tessellation.** Independently tessellated
  rims produce zero cross-panel hash hits; the geometric 3D path only works
  through the projection bootstrap, which proposes correctly but leaves some MST
  interfaces loose. The tessellation-invariant path for production 3D is the
  learned Kintsugi Port (chapter on fragment learning), not this geometric engine.
  Severity high.
- **VSA segmenter is a stub.** `Block Pair Match 3D` stage 1 is a stub
  (`BlockPairMatch3DComponent.cs:43-45`); the component is honest about it and
  redirects to Stone-Cell Match. Severity medium.
- **Three duplicate absolute-orientation kernels.** Horn-quaternion vs SVD-Kabsch
  (×2) solve the same problem; the architecture audit schedules unification on one
  MathNet-SVD kernel. Refactor, not a bug. Severity low.
- **Documentation nits.** The "Phase correlator FFT" `[Algorithm]` name describes
  an FFT but the code is a direct $O(n^2)$ circular correlation; harmless but
  should be reworded. Severity low.
- **Projection-bootstrap prior-art sweep pending.** The bootstrap, antiparallel
  lift, and 3D-disposes gate are marked A-candidate; AGENTS.md §9 forbids
  asserting novelty without the sweep. Severity medium.

---

### Originality summary

| Family | Class | Tier | Evidence |
|---|---|---|---|
| Boundary segmenter / turning descriptor | clean-room | B/C | `EdgeMatchSolveComponent.cs:23`; `BoundarySegmenter.cs:151-189` |
| Segment hash index | clean-room (Frahan-original) | C | `EdgeMatchSolveComponent.cs:24`; `SegmentHashKey.cs` |
| Phase correlator | clean-room | C | `EdgeMatchSolveComponent.cs:25`; `PhaseCorrelator.cs:13-44` |
| Constrained ICP (2D/3D) + SVD Kabsch | clean-room (algo) over vendored MathNet | B | `EdgeMatchSolveComponent.cs:26`; `ConstrainedIcp3D.cs:147-189` |
| Order-preserving correspondence DP | clean-room | C | `OrderedBoundaryMatcher.cs:24-37`, `:100-167` |
| Soft-ICP refiner (CPD + hinge) | clean-room / evolved-fork | B | `SoftIcp3DComponent.cs:43-51`; `SoftIcpRefiner.cs:299-527` |
| Projection bootstrap | original-research (A-candidate) | A | `ProjectionPairFinder.cs:16-48`, `:481-678` |
| Horn registration kernel | clean-room | B | `RigidTransformRecovery.cs:6-30`; `GeoreferenceComponent.cs:36` |

---

### References

- Arkin, E.M., Chew, L.P., Huttenlocher, D.P., Kedem, K., Mitchell, J.S.B.
  (1991). An efficiently computable metric for comparing polygonal shapes. IEEE
  Transactions on Pattern Analysis and Machine Intelligence 13(3):209-216. DOI
  10.1109/34.75509.
- Bennell, J.A., Oliveira, J.F. (2009). A tutorial in irregular shape packing
  problems. Journal of the Operational Research Society 60(S1):S93-S105. DOI
  10.1057/jors.2008.169.
- Besl, P.J., McKay, N.D. (1992). A method for registration of 3-D shapes. IEEE
  Transactions on Pattern Analysis and Machine Intelligence 14(2):239-256. DOI
  10.1109/34.121791.
- Cohen-Steiner, D., Alliez, P., Desbrun, M. (2004). Variational shape
  approximation. ACM Transactions on Graphics (SIGGRAPH) 23(3):905-914. DOI
  10.1145/1015706.1015817.
- Horn, B.K.P. (1987). Closed-form solution of absolute orientation using unit
  quaternions. Journal of the Optical Society of America A 4(4):629-642. DOI
  10.1364/JOSAA.4.000629.
- Kabsch, W. (1976). A solution for the best rotation to relate two sets of
  vectors. Acta Crystallographica A 32(5):922-923. DOI
  10.1107/S0567739476001873.
- Kuhn, H.W. (1955). The Hungarian method for the assignment problem. Naval
  Research Logistics Quarterly 2(1-2):83-97. DOI 10.1002/nav.3800020109.
- Marcotte, O., Suri, S. (1991). Fast matching algorithms for points on a
  polygon. SIAM Journal on Computing 20(3):405-422. DOI 10.1137/0220025.
- Myronenko, A., Song, X. (2010). Point set registration: Coherent Point Drift.
  IEEE Transactions on Pattern Analysis and Machine Intelligence 32(12):2262-2275.
  DOI 10.1109/TPAMI.2010.46.
- Tomczak, A., Haakonsen, S.M., Luczkowski, M. (2023). Matching algorithms to
  assist in designing with reclaimed building elements. Environmental Research:
  Infrastructure and Sustainability 3(3):035005. DOI 10.1088/2634-4505/acf341.
- Umeyama, S. (1991). Least-squares estimation of transformation parameters
  between two point patterns. IEEE Transactions on Pattern Analysis and Machine
  Intelligence 13(4):376-380. DOI 10.1109/34.88573.
- Wang, Z., Chen, N., Furukawa, Y. (2025). PuzzleFusion++: Auto-agglomerative 3D
  fracture assembly by denoise and verify. International Conference on Learning
  Representations (ICLR). arXiv:2406.00259.
