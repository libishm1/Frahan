# The mathematics of the edge-matching and surface-packing stacks

Code-bound equation layer for the shipping edge-matching/registration solvers
(`Frahan.EdgeMatching.Core`, plus the `Frahan.Core` boundary-rail matcher) and
the surface-packing/flattening pipeline (`Frahan.Surface` in
`Frahan.StonePack.Core/SurfacePacking/`, `Frahan.GH.Surface`). Each equation:
statement, short derivation, code provenance, method-class citation. Derived
from the implementation as read, not copied from paper figures. Renders
natively on GitHub and on the docs site (MathJax). Companion to
[`EQUATIONS.md`](EQUATIONS.md) (the 2D/3D packers); the `ContactNfpHoleNester`
math used by Pack Surfaces is documented there in section 1 and is not
repeated here.

Citations in this file come from the `[Algorithm]` attributes on the GH
components (`EdgeMatchSolveComponent`, `SoftIcp3DComponent`,
`EdgeGapFrechetComponent`, `BlockPairMatch3DComponent`,
`SurfaceChartComponent`, `PackOnSurfaceComponent`,
`BoundaryRailIndexComponent`) and from citations embedded in the solver
source headers. No RANSAC stage exists anywhere in the edge-matching stack
(verified by search; the only "RANSAC"-adjacent hits are in the
discontinuity/point-cloud stack, which is out of scope here).

---

# Part A. Edge matching and registration

Pipeline order (EdgeMatch Solve, 5 stages): segment (A.1/A.2), hash (A.3),
coarse phase (A.4/A.5), constrained ICP (A.6, with A.7 as an opt-in
correspondence), beam/agglomerative assembly. Verification gate: discrete
Frechet (A.8). Assignment layer: Hungarian (A.9). Post-solve pose polish:
Soft-ICP (A.10). Whole-side jigsaw sibling: A.11. Descriptor rail matcher:
A.12. 3D mesh-face pre-segmentation: A.13. Residual semantics: A.14.

## A.1 Signed turning signature (2D rims)

A rim is resampled by arc length into $n=\max(8,\lceil L/h\rceil)$ points
$p_0..p_{n-1}$ ($h$ = `SegmenterOptions.SampleSpacing`). The discrete turning
angle at an interior sample is the signed exterior angle between consecutive
edge vectors $u_i=p_i-p_{i-1}$, $v_i=p_{i+1}-p_i$:

$$
\theta_i = \operatorname{atan2}\big(u_i \times v_i,\ u_i \cdot v_i\big)
\in (-\pi, \pi],
$$

where $\times$ is the scalar 2D cross product. Break points split the rim
where the windowed turning mass exceeds a threshold:

$$
\Big|\sum_{k=-w}^{w} \theta_{i+k}\Big| > \theta_{\text{break}},
$$

then nearby breaks within $w$ samples are coalesced. Each segment carries the
invariants chord $c=\lVert p_{s_1}-p_{s_0}\rVert$, total turning
$\Theta=\sum_j \theta_j$, sign $\sigma=\operatorname{sgn}\Theta$, and a
$B$-bin linearly-resampled turning signature; the curvature signature is
$\kappa_j = |\theta_j|$ per bin. Rotation and translation invariant by
construction (angles and lengths only).

*Derivation.* $\operatorname{atan2}(u\times v, u\cdot v)$ is the angle from
$u$ to $v$ because $u\times v=\lVert u\rVert\lVert v\rVert\sin\alpha$ and
$u\cdot v=\lVert u\rVert\lVert v\rVert\cos\alpha$; the magnitudes cancel in
the ratio, making the signature invariant to sampling-density scale of the
lengths but not to the angle itself.

Code: `BoundarySegmenter.Segment`, `BoundarySegmenter.SignedTurn`,
`BoundarySegmenter.ResampleSignal`. Citation: Frahan-original arc-length
turning signature (per the `[Algorithm]` attribute on
`EdgeMatchSegmentsComponent`); the turning function itself is the classical
polygon turning function.

## A.2 Discrete Frenet invariants (3D rims)

For a spatial rim with unit edge vectors $\hat e_1 = \widehat{p_i - p_{i-1}}$,
$\hat e_2 = \widehat{p_{i+1} - p_i}$ and mean edge length
$\bar\ell = \tfrac12(\ell_1+\ell_2)$:

$$
\kappa_i = \frac{\lVert \hat e_2 - \hat e_1 \rVert}{\bar\ell},
\qquad
\tau_i = \operatorname{sgn}\!\big((b_1 \times b_2)\cdot \hat e_2\big)\,
\frac{\operatorname{atan2}\big(\lVert b_1\times b_2\rVert,\ b_1\cdot b_2\big)}{\bar\ell},
$$

with discrete binormals $b_1=\widehat{\hat e_1\times\hat e_2}$,
$b_2=\widehat{\hat e_2\times\hat e_3}$. The torsion array is smoothed with a
truncated Gaussian, weights $w_k \propto \exp(-k^2/2\sigma^2)$. Curvature is
rotation invariant; torsion flips sign under reflection, which is exactly
what disambiguates mirror-image complementary edges (a segment and its
mirror share $\kappa$ but negate $\tau$).

*Derivation.* $\lVert\hat e_2-\hat e_1\rVert = 2\sin(\alpha/2)\approx\alpha$
for turn angle $\alpha$, so $\kappa_i\approx\alpha/\bar\ell$, the discrete
curvature $d\theta/ds$. The torsion is the signed dihedral between
consecutive osculating planes per unit length, the discrete
$d\psi/ds$ of Frenet-Serret. Deviation from the textbook form: the code uses
the chord-norm $2\sin(\alpha/2)$ directly, not $\alpha$; the two agree to
second order and the difference is absorbed by the hash bin widths.

Code: `BoundarySegmenter3D.ComputeFrenetInvariants`,
`BoundarySegmenter3D.SmoothGaussian`,
`BoundarySegmenter3D.SignedTurnSignature` (the 2D turning of A.1 computed in
the panel's local plane, so 3D segments also carry a turning signature).
Citation: Frahan-original arc-length curvature/torsion signature
(`EdgeMatchSegmentsComponent` attribute); discrete Frenet-Serret, standard.

## A.3 Complement hash key and multi-probe query (Stage 2)

A segment is quantised to the integer key

$$
K(s) = \Big(
\big\lfloor c/\ell \big\rceil,\
\big\lfloor \Theta/t_b \big\rceil,\
\big\lfloor \mu/m_b \big\rceil,\
\big\lfloor \varsigma/s_b \big\rceil,\
\sigma \Big),
$$

where $\mu,\varsigma$ are the mean and standard deviation of the turning
signature, $\lfloor\cdot\rceil$ is round-to-nearest, and the length bin is
scale relative when an assembly scale $S$ is set: $\ell = S\cdot f_\ell$
(the cross-cutting scale-invariance rule). Two mating fracture edges
traverse the same physical curve in opposite senses, so the complement of a
query is

$$
\mathcal{C}(K) = \big(\text{len},\ -\Theta,\ -\mu,\ \varsigma,\ -\sigma\big):
$$

turning and its mean negate, the spread $\varsigma$ is invariant, length is
shared. The 3D key appends a planarity bin (matched exactly: a flat shard
cannot fit a twisted edge) and a torsion-variance bin (invariant: torsion
sign flips under reflection but its spread does not).

Query widening is either a full $\pm n$ box over the four banded dims or
query-directed multi-probe: perturbation vectors $t\in\mathbb{Z}^4$ are
ranked by the squared distance from the continuous complement coordinate to
the probed bin's near boundary,

$$
\operatorname{score}(t) = \sum_{d:\,t_d\neq 0}
\begin{cases}
(t_d - \tfrac12 - \delta_d)^2 & t_d > 0\\[2pt]
(-t_d - \tfrac12 + \delta_d)^2 & t_d < 0
\end{cases},
\qquad \delta_d \in [-\tfrac12,\tfrac12],
$$

and the top-$T$ probes are visited. This is the standard multi-probe LSH
ranking (the code comment calls it "the standard multi-probe score";
**UNVERIFIED:** no paper is cited in code; the construction matches Lv et
al. 2007 multi-probe LSH).

Code: `SegmentHashIndex.KeyOf`, `SegmentHashIndex.KeyOf3D`,
`SegmentHashIndex.ComplementCoords`, `SegmentHashIndex.RankedProbes`.
Citation: Frahan-original planarity-aware spatial bucketing
(`EdgeMatchSolveComponent` attribute).

## A.4 Turning-signature phase correlation (Stage 3)

Complementary rims run the shared curve in opposite senses with opposite
turning sign, so the B signature is reversed and negated,
$\tilde b_i = -\,b_{n-1-i}$, then the best cyclic lag minimises the circular
$L_1$ mismatch:

$$
\operatorname{lag}^\star = \operatorname*{arg\,min}_{0\le \lambda < n}
\sum_{i=0}^{n-1}\big| a_i - \tilde b_{(i+\lambda)\bmod n}\big|,
\qquad
\text{sim} = 1 - \frac{\min_\lambda \sum_i |\cdot|}{2\pi n} \in [0,1],
$$

where $2\pi n$ bounds the worst case because each per-sample turning lies in
$(-\pi,\pi]$. **Deviation (code vs attribute):** the `[Algorithm]` attribute
names this "Phase correlator FFT (classical cross-correlation lag
estimation)", but the shipped code is a brute-force $O(n^2)$ circular
$L_1$-difference minimisation, not an FFT and not a product correlation. The
lag semantics are the same; the label overstates the implementation.

Code: `PhaseCorrelator.Correlate`. Citation: classical cross-correlation lag
estimation (`EdgeMatchSolveComponent` attribute), with the deviation noted.

## A.5 Initial rigid transform from the lag

The coarse pose maps A's start frame onto B's lag-shifted frame with B's
tangent flipped (complement orientation). With B-sample index
$j = (n-1-\lambda) \bmod n$ (undoing the reversal in A.4):

$$
T_0 = \operatorname{PlaneToPlane}\big(
\Pi(a_0,\ t_A,\ \hat z),\ \Pi(b_j,\ -t_B,\ \hat z)\big),
$$

2D: $t$ = local polyline tangent, $\hat z$ = world Z. 3D: the planes are
discrete Frenet frames, tangent $t = \widehat{p_{i+1}-p_{i-1}}$ and principal
normal $n = \widehat{\Delta^2 p - (\Delta^2 p\cdot t)\,t}$
(second difference orthogonalised against the tangent), with the same
$-t_B$ flip.

Code: `InitialTransformBuilder.FromLag2D`,
`InitialTransformBuilder.FromLag3D`, `InitialTransformBuilder.FrenetFrameAt`.
Method class: frame-to-frame rigid seeding, standard.

## A.6 Constrained ICP (Stage 4): correspondence, residual, closed-form step

Each iteration transforms A's samples $a_i \mapsto T a_i$, builds
correspondences, and solves one closed-form rigid update.

Correspondence (default, free nearest-neighbour, point-to-curve):

$$
b_i = \Pi_B(T a_i) = \operatorname*{arg\,min}_{q \in B_{\text{curve}}}
\lVert T a_i - q \rVert,
\qquad
r = \frac{1}{N} \sum_{i=1}^{N} \lVert T a_i - b_i \rVert .
$$

The residual $r$ is a MEAN point-to-polyline-curve distance (foot points on
the B curve, not B's samples). With the opt-in non-crossing correspondence
(A.7) the pairs are sample-to-sample and $r$ becomes a mean point-to-point
distance over the matched subset only.

2D rigid step (Kabsch/Umeyama reduced to one angle). With centred
coordinates $a_i' = a_i-\bar a$, $b_i' = b_i - \bar b$ and cross-covariance
sums $S_{xx},S_{xy},S_{yx},S_{yy}$:

$$
\theta^\star = \operatorname{atan2}\big(S_{xy}-S_{yx},\ S_{xx}+S_{yy}\big),
\qquad
t^\star = \bar b - R_{\theta^\star}\,\bar a .
$$

*Derivation.* Maximising
$\operatorname{tr}(R^\top H)$ with $H=\sum a_i' b_i'^{\top}$ over 2D
rotations gives
$\cos\theta\,(S_{xx}{+}S_{yy}) + \sin\theta\,(S_{xy}{-}S_{yx}) \to \max$,
solved exactly by the atan2 above; the optimal translation aligns the
centroids under the optimal rotation.

3D rigid step (Kabsch via SVD):

$$
H=\sum_i a_i' b_i'^{\top},\quad H = U\Sigma V^{\top},\quad
R = V\,\mathrm{diag}\big(1,1,\det(VU^{\top})\big)\,U^{\top},\quad
t = \bar b - R\bar a .
$$

The $\det(VU^\top)$ sign on the last diagonal entry is the mandatory
reflection guard (without it, mirror alignments are returned in place of
rotations; the code computes the determinant explicitly, never
$\operatorname{sign}(\cdot)$, because sign(0) would skip the flip on
degenerate input).

Constraint (penetration rejection): the trial pose is rejected when the
transformed A centroid falls inside B's closed source contour (2D
`Curve.Contains`; 3D: inside B's projected interior, or any sample on the
wrong side of a substrate Brep normal, $(p - \Pi_{\text{substrate}}p)\cdot
n < -\varepsilon$). On rejection the pose is kept and the residual is
multiplied by `IcpOptions.PenetrationPenalty`, steering the beam away from
overlapping matches. Convergence when the incremental transform is small,

$$
\lVert \Delta t\rVert < \varepsilon_t
\ \wedge\
\Delta\theta = \arccos\!\Big(\tfrac{\operatorname{tr}R_\Delta - 1}{2}\Big)
< \varepsilon_R,
$$

(2D uses $\arccos(M_{00})$), or when $|r_{k-1}-r_k|<\varepsilon_t$.

Code: `ConstrainedIcp2D.Refine`, `ConstrainedIcp2D.SvdRigid2D`,
`ConstrainedIcp3D.Refine`, `ConstrainedIcp3D.Kabsch3D`,
`ConstrainedIcp3D.OnCorrectSubstrateSide`. Citation: Besl and McKay 1992
(ICP); Kabsch 1976 SVD alignment (`EdgeMatchSolveComponent`,
`BlockPairMatch3DComponent` attributes; the 2D code comment says
"Kabsch/Umeyama").

## A.7 Monotone non-crossing correspondence (opt-in DP)

Free nearest-neighbour ICP can produce crossing correspondences on wiggly
rims. The opt-in replacement is a DTW-style dynamic program over the
$n\times m$ squared-distance grid,

$$
C(i,j) = d^2(a_i, b_j) + \min\big\{\,C(i{-}1,j{-}1),\ C(i{-}1,j),\ C(i,j{-}1)\,\big\},
\qquad C(0,0)=d^2(a_0,b_0),
$$

with an optional Sakoe-Chiba-style band $|i-j|\le g$ (falls back to the
unbounded DP if the band cannot reach the far corner). Backtracking emits a
pair only on diagonal moves, so the emitted matching is one-to-one and
monotone: if $(i,j)$ and $(i',j')$ are matched with $i<i'$ then $j\le j'$,
which is exactly the non-crossing guarantee. Closed rims (no canonical start,
opposite traversal senses) are reduced to the open case by enumerating cyclic
offsets of B around the phase lag of A.4 plus both orientations, keeping the
candidate with the lowest mean matched distance (strict `<` gives a
deterministic tie-break).

Code: `OrderedBoundaryMatcher.MatchOpen`, `OrderedBoundaryMatcher.MatchClosed`,
`OrderedBoundaryMatcher.MeanMatchedDistance`. Citation: the non-crossing
objective is conceptually Marcotte and Suri 1991 (SIAM J. Comput. 20(3),
points on a convex polygon); the code header states that a verbatim port does
not apply (two independent non-convex sequences) and uses the monotone DP as
the general primitive. Method class: dynamic time warping, standard.

## A.8 Discrete Frechet distance (verification gate)

The worst gap along the best order-preserving coupling of two sampled rims,
by the Eiter-Mannila coupling dynamic program:

$$
\begin{aligned}
ca(0,0) &= d(a_0, b_0)\\
ca(i,0) &= \max\big(ca(i{-}1,0),\ d(a_i,b_0)\big)\\
ca(0,j) &= \max\big(ca(0,j{-}1),\ d(a_0,b_j)\big)\\
ca(i,j) &= \max\Big(d(a_i,b_j),\ \min\big(ca(i{-}1,j),\ ca(i{-}1,j{-}1),\ ca(i,j{-}1)\big)\Big)
\end{aligned}
$$

$$
d_F(A,B) = ca(n{-}1, m{-}1), \qquad d_F(A,B) \ \ge\ d_H(A,B)\ \text{(Hausdorff)} .
$$

$O(nm)$ time; the implementation rolls two rows for $O(\min(n,m))$ memory
(recursing on swapped arguments so the inner row is the smaller sequence;
the metric is symmetric). It is a MAX-type residual: it bounds the worst gap
along the joint, the physically meaningful cut tolerance, and unlike
closest-point residuals it respects sequence order and direction, so a
reversed or folded alignment that looks close as a point set is rejected.
Both rims must be sampled at comparable density (they are: A.1 resamples by
arc length).

Code: `FrechetDistance.Discrete`. Citation: Eiter and Mannila, "Computing
discrete Frechet distance", TR CD-TR 94/64, TU Wien, 1994
(`EdgeGapFrechetComponent` attribute).

## A.9 Hungarian assignment (template/voussoir matching)

The rectangular assignment problem

$$
\min_{\sigma}\ \sum_{i} c_{i,\sigma(i)}
\quad\Longleftrightarrow\quad
\min \sum_{ij} c_{ij}x_{ij}\ \ \text{s.t.}\ \sum_j x_{ij}=1,\ \sum_i x_{ij}\le 1,\ x\ge 0,
$$

solved by the $O(n^3)$ Hungarian method with Jonker-Volgenant-style dual
potentials $u_i, v_j$. The invariant is dual feasibility
$u_i + v_j \le c_{ij}$ with assigned pairs tight; each row is inserted by a
shortest-augmenting-path search on reduced costs

$$
\bar c_{ij} = c_{ij} - u_i - v_j \ \ge 0,
\qquad
u_{i} \mathrel{+}= \delta,\ \ v_j \mathrel{-}= \delta\ \ (\text{visited}),
\qquad
\delta = \min_{j \notin \text{visited}} \operatorname{minv}_j,
$$

then the alternating path is flipped. By LP duality the resulting perfect
matching on tight edges is optimal. Rectangular inputs are padded to square;
infeasible cells (cost $\ge 10^{18}$) and padding take a data-scaled big-M

$$
M = (\max_{ij}|c_{ij}| + 1)\,(n+1)
$$

which is strictly larger than any complete feasible assignment ($n$ cells,
each $\le \max|c|$) but bounded, so the duals stay well-scaled: a fixed
$10^{18}$ sentinel accumulates into $u,v$ and pushes real $O(1)$ costs below
the float64 relative ulp on dense-infeasible inputs (documented V3 review
fix). Rows assigned only to padding report `Unassigned`.

Code: `HungarianAssigner.Solve`. Citation: H.W. Kuhn, "The Hungarian Method
for the Assignment Problem", Naval Research Logistics Quarterly 2:83-97,
1955; Jonker-Volgenant potentials (`BlockPairMatch3DComponent` attribute and
the source header).

## A.10 Soft-ICP / CPD pose refiner (EM weighted-Kabsch)

Refines placed fragment poses so fracture rims touch while solids do not
interpenetrate, minimising one smooth objective (stated in the class header):

$$
L(\text{poses}) = w_{\text{contact}}\ \underbrace{\sum_i c_i\,\lVert T p_i - \bar q_i\rVert^2}_{\text{soft rim correspondence SSD}}
\ +\ w_{\text{pen}}\ \underbrace{\lambda \max(0,\ \text{depth})^2}_{\text{penetration hinge}} .
$$

E-step (CPD soft correspondence with truncation). For a moving rim sample
$p_i$ against all other fragments' rim samples $q_j$:

$$
w_{ij} = \exp\!\Big(-\frac{\lVert p_i - q_j\rVert^2}{\tau}\Big)\ 
\mathbf{1}\big[\lVert p_i - q_j\rVert^2 \le r^2\big],
\qquad
\bar q_i = \frac{\sum_j w_{ij}\, q_j}{w_0 + \sum_j w_{ij}},
\qquad
c_i = \frac{\sum_j w_{ij}}{w_0 + \sum_j w_{ij}},
$$

with a constant uniform outlier mass $w_0$ in the denominator (samples with
no near neighbour get low confidence and are ignored by the weighted
M-step). Deviations from the CPD paper, both intentional and documented in
code comments: (1) the outlier term is a constant pseudo-weight, not the
CPD closed-form $\frac{w}{1-w}\frac{(2\pi\sigma^2)^{D/2}M}{N\,V}$ term;
(2) the temperature $\tau$ is annealed geometrically,
$\tau \leftarrow \max(\gamma\tau,\ \tau_{\text{floor}})$, instead of the CPD
M-step re-estimation of $\sigma^2$; (3) the correspondence radius anneals
coarse-to-fine alongside it, $r^2 \leftarrow \max(\gamma^2 r^2,\
r_{\text{floor}}^2)$, a robust-ICP cutoff so a piece's far boundary is never
dragged inward. All scales are relative:
$\tau_0=\max(f_\tau h^2,\ \tfrac14 r_0^2)$ with $h$ = median rim-sample
spacing, and the initial catch radius grows with the measured median
separation of the perturbed pieces.

Non-penetration folded into the contact target: every moving sample found
INSIDE a neighbour solid (3D closed-mesh inside test) or contour (2D
containment) has its target REDIRECTED to the nearest neighbour-surface
point with full confidence, $\bar q_i \leftarrow \Pi_{\partial\Omega}(p_i)$,
$c_i \leftarrow 1$. Pulling the penetrating sample exactly to the boundary
realises the smooth hinge $\max(0,\text{depth})\to 0$ inside the SAME
weighted rigid solve, so the contact pull and the penetration push cannot
fight.

M-step (confidence-weighted Kabsch). Weighted centroids
$\bar p = \sum_i c_i p_i / \sum c_i$ (weights zeroed below
`MinConfidence`), weighted cross-covariance
$H = \sum_i c_i\,p_i'\,\bar q_i'^{\top}$, then the A.6 SVD solution with the
same determinant reflection guard; 2D reduces to the atan2 angle. The
increment is damped to a fractional step by scaling the axis-angle and the
translation through the Lie exponential (a proper rigid motion, not a matrix
lerp), and left-composed:

$$
T_f \leftarrow \operatorname{Exp}(s\,\xi)\; T_f, \qquad s \in (0,1] .
$$

Pose retraction closed forms (Sola 2018, "A micro Lie theory"):

$$
\text{SE(2):}\quad
R_\theta,\qquad
t = V(\theta)\begin{bmatrix}v_x\\ v_y\end{bmatrix},\qquad
V(\theta)=\frac{1}{\theta}\begin{bmatrix}\sin\theta & -(1-\cos\theta)\\ 1-\cos\theta & \sin\theta\end{bmatrix}
\xrightarrow{\theta\to 0} I,
$$

$$
\text{SE(3):}\quad
R=\operatorname{Exp}_{\mathfrak{so}(3)}(\omega)= I + \tfrac{\sin\theta}{\theta}[\omega]_\times + \tfrac{1-\cos\theta}{\theta^2}[\omega]_\times^2,
\qquad
t = V(\omega)\,\rho,
$$

$$
V(\omega) = I + \tfrac{1-\cos\theta}{\theta^2}[\omega]_\times
+ \tfrac{\theta-\sin\theta}{\theta^3}[\omega]_\times^2,
\qquad \theta = \lVert\omega\rVert,
$$

with the small-angle limits $\tfrac12$ and $\tfrac16$ so the maps are smooth
through zero. Anchor-locked (fragment 0 fixed), deterministic iteration
order. An L-BFGS path on the stacked $\mathbb{R}^{6N}$ tangent
(central-difference gradients) exists for cross-validation of the EM basin;
the EM weighted-Kabsch alternation is the production strategy.

Report metrics (the before/after numbers): `MeanRimGap` = MEAN
nearest-neighbour point-to-point distance over samples on the mating
interface only (nearest opposing sample within the interface band
$f_r\,h$; if no sample qualifies, the global mean NN distance is reported
instead of a false zero). `MaxPenetration` = MAX distance from an
inside-classified sample to the neighbour surface (a depth, not a mean).
`ContactSamples` = count of interface samples.

Code: `SoftIcpRefiner.Refine`, `SoftIcpRefiner.SoftTargets`,
`SoftIcpRefiner.WeightedRigid`, `SoftIcpRefiner.ApplyPenetrationTargets`,
`SoftIcpRefiner.DampDelta`, `SoftIcpRefiner.Measure`, `LieSe2.Exp`,
`LieSe3.Exp`, `LieSe3.ExpSo3`, `SoftIcpLbfgs.Refine3D`. Citation: Myronenko
and Song 2010 (Coherent Point Drift), Kabsch 1976/1978, Sola 2018
(`SoftIcp3DComponent` attributes and source headers), with the three CPD
deviations noted above.

## A.11 Whole-side jigsaw matcher (best-first sibling assembler)

Corner detection: the boundary is resampled to 200 arc-length samples,
oriented CCW by the shoelace sign, and the minimum-area oriented bounding
rectangle is found by rotating calipers over the Andrew monotone-chain
convex hull (one candidate rectangle per hull edge; support extents
$[\min u,\max u]\times[\min v,\max v]$ in the edge frame; strict-less with
epsilon for determinism). The side corners are the boundary samples nearest
the four rectangle corners, robust to wavy seams (a wave peak never sits at
a rect corner) and rotation (the box is oriented).

Flat-side gate (border edges excluded from matching):

$$
\frac{\max_{p\in\text{side}}\ \operatorname{dist}(p,\ \text{chord})}{\lVert\text{chord}\rVert} < 0.04 .
$$

Canonical frame: each side is resampled to $K=40$ points and rotated so the
chord runs from the origin along $+x$ (start point at origin). Side pair
score, generalising ryan-puzzle-solver's `error_between_polylines`:

$$
\operatorname{cost}(A,B) = \frac{\min\big(e(A,B),\ e(A,\bar B)\big)}{\max(L_A, L_B)},
\qquad
e = \sum_{i=1}^{K}\big(|x^A_i - x^B_i| + |y^A_i - y^B_i|\big),
$$

$\bar B$ = the reversed traversal (complementary seams mate reversed);
rejected outright when either side is flat or when
$|1 - L_A/L_B| > 0.15$ (chord discrepancy). Validated separation: true
seams score about 0.2 to 1.0, spurious pairs above 1.2.

Seating transform (two-point rigid, about $+\hat z$): map the child side's
corners $(s_0,s_1)$ onto the placed parent's corners, reversed for the
complementary case,

$$
\Delta\theta = \operatorname{atan2}(t_v) - \operatorname{atan2}(s_v),\qquad
T = T_{t_0-s_0}\; R_{\hat z}(\Delta\theta;\ s_0),
$$

with $s_v = s_1-s_0$, $t_v = t_1-t_0$. Assembly is a best-first frontier
under the strict total order (cost, parentId, parentSide, childId,
childSide); a placement is discarded when any placed part's centroid lies
inside the candidate outline or vice versa (`Curve.Contains`, tolerance
$\max(10^{-3},\ 0.02\,S)$ with $S$ = median panel bbox diagonal, the
scale-relative rule); `AssemblyState.TotalResidual` accumulates the accepted
seam costs.

Code: `WholeSideExtractor.Extract`, `WholeSideExtractor.FindCornerIndices`,
`WholeSideExtractor.Flatness`, `WholeSideExtractor.Canonical`,
`WholeSideMatcher.Score`, `BestFirstAssembler.Solve`,
`BestFirstAssembler.TwoPoint`, `BestFirstAssembler.Overlaps`. Citation:
Frahan-original, generalising the open-source ryan-puzzle-solver scorer (per
the source header); rotating calipers and monotone-chain hull, standard.

## A.12 Boundary-rail descriptor matcher (Frahan.Core)

Descriptor quantisation into the rail-index key (floor bucketing, angle
wrapped to $[0,360)$):

$$
K = \Big(\big\lfloor L/\ell\big\rfloor,\ \big\lfloor a/\alpha\big\rfloor,\
\big\lfloor C/c\big\rfloor,\ \text{zone}\Big),
$$

query widened by $\pm$`LengthRadius` and $\pm$`AngleRadius` buckets. The
affinity between a query and a candidate descriptor is a product of four
sub-scores, each in $[0,1]$, so any fully incompatible dimension zeroes the
match:

$$
\operatorname{score} =
\underbrace{\Big(1-\tfrac{|L_1-L_2|}{\max(L_1,L_2)}\Big)_+}_{\text{length}}
\cdot
\underbrace{\Big(1-\tfrac{\Delta_{360}(a_1,a_2)}{180}\Big)_+}_{\text{angle}}
\cdot
\underbrace{\Big(1-\tfrac{|C_1-C_2|}{\max(|C_1|,|C_2|)}\Big)_+}_{\text{curvature}}
\cdot
\underbrace{\mathbf{1}[z_1=z_2]}_{\text{zone (opt)}},
$$

with the wrap-aware angular distance
$\Delta_{360}(a,b) = \min(d,\ 360-d)$, $d = |a-b|\bmod 360 \in [0,180]$.
Matches are ranked descending, top-K capped, thresholded by
`MinAffinityScore`.

Code: `EdgeDescriptor.ToEdgeKey`, `EdgeAffinityScorer.Score`,
`EdgeAffinityScorer.AngleDistanceDegrees`, `BoundaryRailMatcher.MatchEdge`,
`BoundaryRailMatcher.MatchFragment`. Citation: Frahan-original arc-length
affinity bucketing, spec 5 sections 5.5-5.6, not a published algorithm (per
the `[Algorithm]` attributes on `BoundaryRailIndexComponent` and
`FragmentEdgeMatchComponent`).

## A.13 Variational Shape Approximation (3D pipeline Stage 1)

The 3D block pipeline pre-segments mesh faces into near-planar proxies by
Lloyd iteration on the $\mathcal{L}^{2,1}$ metric:

$$
E(f, P) = \operatorname{area}(f)\ \lVert n_f - N_P \rVert^2,
\qquad
N_P = \widehat{\sum_{f\in P} \operatorname{area}(f)\, n_f},
$$

priority-queue best-first partition flooding, convergence when the relative
total-energy change drops below 0.005; patches below `MinFaceArea`
(post-segmentation, 15,000 mm^2 per the UCL reference) or whose worst face
centroid exceeds `FitResidualMax` from the patch mean plane are dropped.
Phase-1 implementation: random spread seeding, no hierarchical doubling or
teleport operators yet (documented TODO in the source header).

Code: `VsaSegmenter` (per-face metric at the `area * ||n - N||^2` line,
`ComputeTotalEnergy`, proxy fit per CGAL `L21_metric_plane_proxy.h`).
Citation: Cohen-Steiner, Alliez, Desbrun 2004, "Variational Shape
Approximation", ACM TOG (SIGGRAPH) 23(3):905-914
(`BlockPairMatch3DComponent` attribute and source header).

## A.14 Residual semantics (what each "gap" number is)

| Quantity | Statistic | Distance type | Code |
|---|---|---|---|
| `MatchResult.Residual` (ICP) | mean | point-to-curve (foot point on B polyline curve); point-to-point over the matched subset when non-crossing correspondence is on | `ConstrainedIcp2D.Refine`, `ConstrainedIcp3D.Refine` |
| Discrete Frechet gap | max over the best monotone coupling | point-to-point, order/direction aware | `FrechetDistance.Discrete` |
| Soft-ICP `MeanRimGap` | mean over interface-band samples | nearest-neighbour point-to-point (any other fragment) | `SoftIcpRefiner.Measure` |
| Soft-ICP `MaxPenetration` | max | inside-sample to neighbour surface (a depth) | `SoftIcpRefiner.MeasureMaxPenetration` |
| Whole-side cost | length-normalised $L_1$ sum | index-aligned point-to-point in the canonical chord frame | `WholeSideMatcher.Score` |
| `AssemblyState.TotalResidual` | sum of accepted pair residuals / side costs | as above per stage | `AssemblySolver`, `BestFirstAssembler.Solve` |
| Live-edge slot cost | mean trim | vertical scribe distance board-to-river | `LiveEdgeScribeMatcher.SlotCost` |

Caveat (from the ICP code path): when a trial step is rejected by the
penetration guard the iteration's residual is multiplied by
`PenetrationPenalty` before being stored, and the returned
`MatchResult.Residual` is the previous iteration's value at exit. The
reported ICP number is therefore not always a pure distance mean; it can
carry the penalty factor, by design (it demotes penetrating candidates in
the beam ranking).

---

# Part B. Surface packing and flattening

Pipeline: mesh clean and OBJ round-trip, BFF flattening (B.1), seam-safe UV
table (B.2), global scale recovery (B.3), edge-stretch audit (B.4), 2D
nesting on the flat chart (EQUATIONS.md section 1), barycentric lift back to
3D (B.5), rigid placement-frame and transform composition (B.6).

## B.1 BFF invocation (external conformal flattening)

What the external solver computes (interface statement, not a re-derivation):
BFF produces a flattening $z:M\to\mathbb{R}^2$ of a disk-topology triangle
mesh that is exactly conformal in the discrete sense, where the flattening is
determined entirely by boundary data: the boundary conformal scale factor
$u|_{\partial M}$ and the boundary curvature density $\kappa$ trade off
through a Poincare-Steklov (Dirichlet-to-Neumann) relation, so prescribing
one determines the other; interior behaviour follows by solving the Yamabe /
Cherrier problem on the cone metric. Cone singularities ($N$ = `--nCones`)
concentrate Gaussian curvature at isolated vertices to reduce area
distortion. With `--normalizeUVs` the returned UVs are scaled into the unit
square, which is why Frahan must recover a global scale (B.3).

Frahan's wrapper contract: input OBJ written from the cleaned triangulated
Rhino mesh, `bff-command-line.exe "<in>" "<out>" [--nCones=N]
[--normalizeUVs]`, async stdout/stderr consumption (pipe-deadlock guard),
timeout kill, non-zero exit and missing-output checks. Face count is
verified before and after so face $i$ in the flat mesh corresponds exactly
to face $i$ in the 3D mesh, the invariant every downstream formula relies
on.

Code: `BffCommandLineRunner.RunAsync`, `BffCommandLineRunner.BuildArgs`,
`SurfaceChartComponent.ComputeChart` (steps 1-5),
`MeshObjIO.TryParseObjWithFaceCornerUVs`. Citation: Sawhney and Crane 2017,
"Boundary First Flattening", ACM TOG 36(4):109, doi 10.1145/3072959.3056432
(`SurfaceChartComponent` attribute: "External BFF command-line exe; Frahan
wraps the binary").

## B.2 Face-corner UV table (seam-safe flat mesh)

BFF output UVs live on face corners, not vertices: a single 3D vertex on a
seam cut carries different UVs on each side. The table is the map

$$
\text{uv}:\ (f, k) \mapsto (u,v) \in \mathbb{R}^2,
\qquad k \in \{0,1,2\},
$$

and the flat mesh is built un-welded: each triangle gets three fresh
vertices $(u,v,0)$, so seams are never bridged and face $i$ in the flat mesh
is face $i$ in the 3D mesh. A missing UV throws instead of defaulting to
$(0,0)$ (bugs surface immediately instead of collapsing triangles to the
origin).

Code: `FaceCornerUvTable.SetUv`, `FaceCornerUvTable.ToFlatUnweldedMesh`,
`FaceCornerKey`. Method class: indexed face-corner attributes, standard.

## B.3 Chart scale recovery (global conformal scale factor)

With normalised UVs the chart must be rescaled to real units. The scale is
the ratio of total triangle edge length in 3D to total triangle edge length
in UV, summed over all corresponding triangles $T$:

$$
s = \frac{\sum_{T}\big(\lVert A_T B_T\rVert + \lVert B_T C_T\rVert + \lVert C_T A_T\rVert\big)_{3D}}
{\sum_{T}\big(\lVert A_T B_T\rVert + \lVert B_T C_T\rVert + \lVert C_T A_T\rVert\big)_{UV}},
$$

and the stored flat mesh is $\text{FlatMesh} = s\cdot\text{UV}$ (uniform
scale about the origin), so every downstream consumer (nester sheets,
barycentric lift) works directly in model units. For an exactly isometric
flattening $s$ recovers the true unit conversion; for a conformal
flattening it is the length-weighted average scale, and the per-edge spread
around it is exactly what B.4 measures. Degenerate inputs (no valid
triangles, zero flat length, face-count mismatch) return $s=1$ with a
warning rather than throwing.

Code: `ChartScaleComputer.ComputeGlobalScale`,
`SurfaceChartComponent.ComputeChart` (steps 7-8),
`FrahanSurfaceChart.ChartScale`. Citation: "Conformal chart-scale recovery,
Frahan-original barycentric UV-to-real-world scaling"
(`SurfaceChartComponent` attribute).

## B.4 Edge-stretch distortion metric

Per triangle edge $e=(i,j)$, comparing the 3D length against the
scale-corrected flat length:

$$
\sigma_e = \frac{\lVert v_i^{3D} - v_j^{3D}\rVert}{s\,\lVert v_i^{UV} - v_j^{UV}\rVert},
\qquad
\sigma_{\max} = \max_e \sigma_e,\quad \sigma_{\min} = \min_e \sigma_e,
$$

over all three edges of every corresponding triangle pair (edges shorter
than $10^{-8}$ in either domain are skipped). Warnings fire at
$\sigma_{\max} > 1.15$ (increase clearance to compensate) and
$\sigma_{\min} < 0.85$ (parts under-scaled after mapping). Edge-scale
distortion, not area distortion, is the fabrication-relevant metric: cut
paths follow edges. Sanity check: because $s$ is the totals ratio of B.3,
the flat-length-weighted mean of $\sigma_e$ is exactly 1, so
$\sigma_{\max}\ge 1 \ge \sigma_{\min}$ always; the report is the spread
around the recovered scale.

Code: `ChartDistortionAnalyzer.Analyze`,
`ChartDistortionAnalyzer.UpdateStretch` (called with the UNscaled flat mesh
plus $s$, equivalent to using the stored scaled FlatMesh). Method class:
per-edge metric distortion audit; Frahan-original thresholds.

## B.5 Barycentric 2D-to-3D lift

A packed 2D point $p$ (real units, $Z=0$, same space as FlatMesh) is located
in a flat triangle $(a,b,c)$ by the explicit 2D barycentric solve. With
$v_0=b-a$, $v_1=c-a$, $v_2=p-a$ and Gram entries
$d_{00}=v_0\cdot v_0$, $d_{01}=v_0\cdot v_1$, $d_{11}=v_1\cdot v_1$,
$d_{20}=v_2\cdot v_0$, $d_{21}=v_2\cdot v_1$:

$$
w_B = \frac{d_{11} d_{20} - d_{01} d_{21}}{d_{00} d_{11} - d_{01}^2},
\qquad
w_C = \frac{d_{00} d_{21} - d_{01} d_{20}}{d_{00} d_{11} - d_{01}^2},
\qquad
w_A = 1 - w_B - w_C,
$$

accepted when $w_A, w_B, w_C \ge -10^{-6}$ (inside or on an edge) and the
denominator is non-degenerate. The 3D position applies the SAME weights to
the same-index 3D face:

$$
x^{3D} = w_A A^{3D} + w_B B^{3D} + w_C C^{3D}.
$$

*Derivation.* $p = a + w_B v_0 + w_C v_1$ dotted with $v_0$ and $v_1$ gives
the $2\times 2$ Gram system; Cramer's rule yields the two quotients.
Barycentric coordinates are affine invariant, so evaluating them in UV and
applying them in 3D is exactly the piecewise-linear map the flattening
defines. Fallback for points marginally outside the chart (sampling noise at
the boundary): `Mesh.ClosestMeshPoint` within $5\times$ the sampling
tolerance, reusing its returned barycentric `T[]` on the hit face. Curves
are sampled to polylines and lifted point-wise; any unmappable point fails
the whole curve (null return, never a silently corrupt curve). The
containing-triangle search is a linear scan, $O(F)$ per point (documented
as acceptable below about 2000 faces).

**Deviation (code vs attribute citation):** the `[Algorithm]` attribute on
`PackOnSurfaceComponent` cites "Floater 2003, mean value coordinates" for
this mapping, but the implemented formula is the standard triangle
barycentric solve above, not mean-value coordinates. On a triangle the two
coincide (mean-value coordinates reduce to barycentric coordinates for
$n=3$), so behaviour is unaffected, but the citation over-specifies the
method.

Code: `BarycentricMapper2DTo3D.TryBarycentricCoords2D`,
`BarycentricMapper2DTo3D.BlendSurfacePoint`,
`BarycentricMapper2DTo3D.MapSinglePoint`,
`BarycentricMapper2DTo3D.MapCurveTo3DSurface`. Citation: as noted, attribute
says Floater 2003 (doi 10.1016/S0167-8396(03)00002-5); implementation is
classical triangle barycentric interpolation.

## B.6 Pack Surfaces: placement transform composition

2D placement (the Core nester convention: rotate about the world origin,
then translate; `HoleNestPlacement` carries $(\theta, t_x, t_y)$):

$$
T_{\text{pack}} = T_{(t_x,\ t_y,\ \Delta z)}\ \circ\ R_{\hat z}(\theta),
\qquad
\Delta z = z_{\text{sheet}} - z_{\text{part}},
$$

where $\Delta z$ lifts the part from its own input plane onto the flat chart
plane so the barycentric search of B.5 can locate it in the flat mesh. The
packed 2D curve is the original input curve under $T_{\text{pack}}$ (the
nester's sampled proxy is collision-only; output is always the exact
original curve).

Rigid placement frame on the surface (per placed part): map the packed
part's bbox centre $m$ and two finite-difference probes through B.5,

$$
o = \phi(m),\qquad
x = \widehat{\phi(m + \delta \hat e_x) - o},\qquad
y' = \phi(m + \delta \hat e_y) - o,
$$

$$
z = \widehat{x \times y'},\qquad
y = z \times x
\qquad(\text{Gram-Schmidt right-handed frame}),
$$

with step $\delta = \max(10\,\text{tol},\ 0.1 \min(w,h))$. The rigid lift is
then a frame-to-frame map from the flat frame at the packed centre to the
surface frame:

$$
T_{3D} = \operatorname{PlaneToPlane}\big(\Pi(m,\hat e_x,\hat e_y),\ \Pi(o, x, y)\big),
$$

and the single fabrication transform from the ORIGINAL flat part directly to
its 3D pose composes the two (matrix product, right-to-left application):

$$
T_{\text{full}} = T_{3D}\ \circ\ T_{\text{pack}} .
$$

Two outputs coexist by design: the rigid pose $T_{\text{full}}$ applied to
the original part (no shape distortion, for fabrication), and the deformed
curve $\phi(\text{packed 2D curve})$ that follows the surface exactly
(B.5). Their disagreement is quantified per part as

$$
\text{MaxDev} = \max_{k=1..4}\ \big|\ \operatorname{dist}_{\text{signed}}\big(\phi(\text{bbox corner}_k),\ \Pi(o,x,y)\big)\ \big|,
$$

the worst out-of-plane gap at the four bbox corners: small means the region
is nearly developable at part scale, large means shimming.

Proxy-deviation compensation (why full-resolution outputs never overlap):
smooth curves are sampled uniformly by length for the nester (deliberately
NOT curvature-adaptive: tiny edges degenerate the Minkowski NFPs), the
worst mid-edge sag of each proxy is measured against the true curve,
$d = \max_i \operatorname{dist}(\text{mid}_i,\ \text{curve})$, and the
engine clearance is widened so exact curves cannot collide:

$$
\text{spacing}_{\text{engine}} = \text{spacing} + 2\,d_{\text{part}} + d_{\text{sheet}}
$$

($2\times$ the worst part deviation for part-part pairs, the sheet term
once). Sheets are the charts' outer naked-edge loops (outer = longest naked
polyline), inner naked loops become holes, and every loop is forced CCW via
the shoelace signed area
$A = \tfrac12\sum_i (x_i y_{i+1} - x_{i+1} y_i)$ before entering the nester.
Charts fill greedily in order (chart 0 first, unplaced parts overflow to
chart 1, ...). The nesting itself is the exact-NFP bottom-left-fill of
EQUATIONS.md section 1 (hard non-overlap, multi-start, optional boundary
mode).

Code: `SurfaceHoleNestBridge.PackTransform`,
`SurfaceHoleNestBridge.CurveToLoop`, `SurfaceHoleNestBridge.SignedArea`,
`PackSurfacesComponent.ComputePacking`,
`PackSurfacesComponent.ComputePlacementFrame`,
`PackSurfacesComponent.BuildSnapshot`,
`FrahanSurfaceChart.ExtractOuterBoundary`,
`ContactNfpHoleNester.PackSheets` (Core). Citation: Burke, Hellier, Kendall,
Whitwell 2006 NFP-BLF for the nesting layer (`PackOnSurfaceComponent`
attribute, doi 10.1287/opre.1060.0293); frame composition and deviation
compensation Frahan-original.

---

# Code-vs-paper deviations found (summary)

1. `PhaseCorrelator.Correlate` is labelled "Phase correlator FFT" in the
   `[Algorithm]` attributes but implements a brute-force $O(n^2)$ circular
   $L_1$ mismatch minimisation, no FFT, no product correlation (A.4).
2. `PackOnSurfaceComponent`'s attribute cites Floater 2003 mean-value
   coordinates; `BarycentricMapper2DTo3D` implements the standard triangle
   barycentric solve. Identical on triangles, so no behavioural difference
   (B.5).
3. `SoftIcpRefiner` deviates from CPD (Myronenko-Song 2010) three ways, all
   documented in code comments: constant uniform outlier mass instead of the
   closed-form CPD outlier term; geometric $\tau$ annealing instead of
   M-step $\sigma^2$ re-estimation; hard correspondence-radius truncation
   (robust-ICP cutoff) annealed alongside $\tau$ (A.10).
4. The penetration hinge of the Soft-ICP design doc is realised not as a
   separate gradient term but by redirecting penetrating samples' targets to
   the neighbour surface inside the same weighted-Kabsch solve (A.10).
5. `ConstrainedIcp*` residuals can carry a `PenetrationPenalty` multiplier
   when a trial step is rejected, so `MatchResult.Residual` is not always a
   pure mean distance (A.6, A.14).
6. `BoundarySegmenter3D` computes discrete curvature as the chord norm
   $\lVert\hat e_2-\hat e_1\rVert/\bar\ell = 2\sin(\alpha/2)/\bar\ell$
   rather than $\alpha/\bar\ell$; second-order equivalent, absorbed by hash
   bin widths (A.2).
7. `HungarianAssigner` replaces the fixed $10^{18}$ infeasible sentinel with
   a data-scaled big-M $(\max|c|+1)(n+1)$ to keep dual potentials
   well-conditioned; a documented V3 numeric-hygiene fix, not in Kuhn's
   formulation (A.9).
8. `VsaSegmenter` is an explicitly-declared Phase-1 subset of Cohen-Steiner
   2004 / CGAL: random spread seeding, no hierarchical doubling, no
   teleport/merge/split operators (A.13).
9. **UNVERIFIED:** the multi-probe ranking in `SegmentHashIndex.RankedProbes`
   matches the multi-probe LSH construction (Lv et al. 2007) but no citation
   exists in code; the comment only calls it "the standard multi-probe
   score" (A.3).
10. No RANSAC stage exists in either stack (searched; the only hits are in
    the discontinuity point-cloud worker, out of scope).
