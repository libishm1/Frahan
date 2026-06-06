---
slug: soft-icp-cpd
title: Soft-ICP CPD weighted-Kabsch fragment pose refiner (+ opt-in L-BFGS path)
tier: 1
algorithm_class: Coherent Point Drift soft correspondence + weighted Kabsch EM
core_method_class: Myronenko-Song 2010 CPD; Kabsch 1976/1978; Sola 2018 SE(3) Exp; MathNet BfgsMinimizer
fabrication_direction: both
geometry_type: open-mesh fragment rim point sets (3D world coords) + closed solids for inside-test + 2D contours
big_o_time: "EM O(I*N^2*P^2); L-BFGS O(B*dim*(N*P)^2), dim=6*moving"
big_o_space: "O(N*P) point buffers + O(N) transforms; 3x3 SVD O(1)"
parallel_model: serial
units: mm (declared SoftIcp3DComponent.cs:104); all lengths/temperatures scale-relative to median rim spacing or bbox diagonal
gotcha_flags: [G2, G4, M2, M5, T1, T3, T5, T7]
verdict: evolve
source_files:
  - Frahan.EdgeMatching.Core/SoftIcpRefiner.cs
  - Frahan.EdgeMatching.Core/SoftIcpLbfgs.cs
  - Frahan.EdgeMatching.Core/SoftIcpOptions.cs
  - Frahan.EdgeMatching.Core/LieGroups.cs
  - Frahan.StonePack.GH/EdgeMatch3D/SoftIcp3DComponent.cs
---

## Derived equations (reconstructed FROM the code)

All derived from the actual loops; method-class origin cited, not the formulas copied.

**E-step soft correspondence (CPD, Myronenko-Song 2010).** For a moving rim sample
$p_i$ of fragment $f$ and every rim sample $q_j$ on the OTHER fragments
(SoftIcpRefiner.cs:316-330), with robust cutoff $\lVert p_i-q_j\rVert^2 \le r^2$
(line 323) and temperature $\tau$:

$$w_{ij} = \exp\!\Big(-\tfrac{\lVert p_i - q_j\rVert^2}{\tau}\Big),\qquad
W_i = w_{\text{out}} + \sum_{j} w_{ij}$$

where $w_{\text{out}} = $ `OutlierWeight` is the uniform outlier pseudo-mass placed
in the denominator ONLY (lines 314, 334-339). Soft target and confidence:

$$\bar q_i = \frac{\sum_j w_{ij}\, q_j}{W_i},\qquad
c_i = \frac{W_i - w_{\text{out}}}{W_i}\in(0,1)$$

(lines 336-339). $c_i$ is the matched-mass fraction; samples on non-overlapping rim
tails get $c_i \to 0$ and drop out of the M-step.

**Temperature schedule (scale-relative, annealed).** From lines 213, 283-288 and
SoftIcpOptions.cs:19-33:

$$\tau_0 = \max\!\big(\text{Tau0Factor}\cdot s^2,\; \tfrac14 r_0^2\big),\quad
\tau_{k+1} = \max(\text{TauAnneal}\cdot\tau_k,\; \text{TauFloorFactor}\cdot s^2)$$

with $s$ = median rim-sample spacing (lines 696-711). Correspondence radius anneals
$r^2_{k+1} = \max(r^2_k\cdot\text{TauAnneal}^2,\; r_{\text{floor}}^2)$ (lines 287-288).
Initial $r_0$ widens to catch a perturbed rim: $r_0 = r_{\text{floor}} + $ initial
separation if separated (lines 202-206).

**M-step confidence-weighted Kabsch (Kabsch 1976/1978, SVD).** Over samples with
$c_i \ge $ `MinConfidence` (lines 354-426). Weighted centroids
$\bar p = \tfrac{1}{W}\sum c_i p_i$, $\bar q = \tfrac{1}{W}\sum c_i \bar q_i$,
cross-covariance

$$H = \sum_i c_i\,(p_i-\bar p)(\bar q_i-\bar q)^{\!\top},\quad
H = U\Sigma V^{\!\top},\quad
R = V\,\mathrm{diag}(1,1,\det(VU^{\!\top}))\,U^{\!\top}$$

The reflection guard uses `(V*U^T).Determinant()` (line 413), NOT `Math.Sign` of a
singular value -- correct. Translation $t = \bar q - R\,\bar p$ (lines 422-424).
2D case reduces $R$ to $\theta = \operatorname{atan2}(S_{xy}-S_{yx},\,S_{xx}+S_{yy})$
(line 387).

**Damped Lie retraction (Sola 2018).** The closed-form increment $\Delta$ is taken
fractionally (DampDelta, lines 761-801): angle $\to\theta\cdot\text{step}$ via
$\mathrm{Exp}_{so(3)}$ (Rodrigues), translation $\to t\cdot\text{step}$, then
left-composed $T_f \leftarrow \Delta\,T_f$ (line 267).

**Non-penetration coupling (folded into target, NOT a separate push).** For a moving
sample $p_i$ found inside neighbour solid $g$ (`Mesh.IsPointInside`, line 469), the
target is redirected to the surface with full confidence (lines 472-478):
$\bar q_i = \mathrm{ClosestPoint}_g(p_i)$, $c_i = 1$. This realises the hinge
$\lambda\max(0,\text{depth})^2$ by pulling depth $\to 0$ exactly without overshoot.

**L-BFGS objective (alternative path, SoftIcpLbfgs.cs).** Same CPD residual written
as an explicit SSD plus an EXPLICIT Huber penetration term (lines 134-190):

$$L(\xi) = \sum_{f}\sum_i \big\lVert p_i(\xi) - \bar q_i(\xi)\big\rVert^2
+ 10\!\!\sum_{\text{inside}}\!\! \rho_H\big(\text{depth}\big),\quad
\rho_H(d)=\begin{cases} d^2 & d<\kappa\\ 2\kappa d-\kappa^2 & d\ge\kappa\end{cases}$$

with knee $\kappa=$ `HuberPenetration`$\cdot s$ (line 132). Parameterised by
$\xi\in\mathbb R^{6M}$ ($M$ moving fragments), retracted by `ExpSe3` (lines 236-265),
minimised by MathNet `BfgsMinimizer` with a central-difference gradient (lines 207-214).

## Code sketch / reuse seam

- Reuse seam: `SoftIcpRefiner.Refine3D(IList<Fragment>, SoftIcpOptions)` and
  `Refine2D`. `Fragment` carries `RimPoints` (world), optional closed `Solid` (3D
  inside-test) or `Contour2D` (2D), `Anchored`, and out `Delta`
  (SoftIcpRefiner.cs:69-116). `Measure(...)` gives the before/after `Report`
  (MeanRimGap, MaxPenetration, ContactSamples, Iterations).
- GH surface: `SoftIcp3DComponent` (GUID D5F1000E) samples naked edges
  (SoftIcp3DComponent.cs:269-291), pins `AnchorIndex`, runs `Refine3D`, emits
  per-fragment Delta + refined mesh + report.
- L-BFGS path `SoftIcpLbfgs.Refine3D` is wired by API but the component does NOT
  select it; `SoftIcpStrategy` (SoftIcpOptions.cs:201-222) defaults to EmAlternation.

## Gotchas (G/M/N/P/T)

| Check | Verdict | Reason (file:line) |
|---|---|---|
| G1 exact/adaptive predicates | flag | No exact predicates; inside/closest is float `Mesh.IsPointInside`/`ClosestPoint` (SoftIcpRefiner.cs:469,472). |
| G2 degeneracy handled | flag | Kabsch guards empty-weight -> identity (line 361) and SVD reflection (413); NO guard for collinear/coplanar rim samples giving rank-deficient H (lines 397-407) -> ambiguous R. |
| G3 predicate/construction separation | pass | E-step (weights), M-step (construction), penetration redirect are separate stages (lines 299/354/441). |
| G4 robust boolean kernel | flag | Penetration uses home-rolled `Mesh.IsPointInside(pl, tol*0.5, false)` with try/catch swallow (lines 469,640), ray-parity on FillHoles-closed open meshes; not a robust kernel. |
| M1 manifold output | na | Refiner outputs rigid Transforms, not new mesh topology. |
| M2 watertight stated | flag | Inside-test REQUIRES closed solid; component only sets Solid if `m.IsClosed` (SoftIcp3DComponent.cs:196); open meshes silently lose the penetration term (closure asserted by `IsClosed`, Euler not verified). |
| M3 winding + self-intersect | na | No mesh construction; winding irrelevant to point transforms. |
| M4 bounded decimation error | na | No remesh/decimation; rim sampling is arc-length (SoftIcp3DComponent.cs:283). |
| M5 hidden data-structure cost | flag | All-pairs O(N^2*P^2) E-step (lines 316-330) and O(N^2*P^2) MeanRimGap (548-571): linear scan, no spatial index. |
| N1-N6 nurbs | na | No NURBS; 2D contour is a PolylineCurve, sampled not evaluated as a spline. |
| P1 deterministic reductions | pass | Fixed fragment/sample order, serial sums (lines 234,311); EM byte-deterministic by design (SoftIcpRefiner.cs:54-56). |
| P2 thread safety | na | Single-threaded; no shared mutable geometry across threads. |
| P3 GPU memory | na | CPU only. |
| P4 load balance | na | Serial. |
| P5 Amdahl serial tail | na | Wholly serial; no parallel section to name. |
| T1 recenter far-from-origin | flag | Rim points kept in WORLD coords (SoftIcpRefiner.cs:75-79); NO recenter before DistanceToSquared/exp. Metre block at UTM/site coords loses float64 precision in the softmax. |
| T2 absolute vs scale-relative eps | pass | tau, corrR, bands, convergence all scale-relative to spacing or bbox diagonal (SoftIcpOptions.cs:8-11,19-132). |
| T3 float32 vs float64 | flag | Internals are double (good), BUT RhinoCommon `Mesh.IsPointInside` mesh math is single-aware; combined with no-recenter (T1) the absolute `tol*0.5` (line 469) degrades at architectural scale. |
| T4 units declared+consistent | pass | mm declared (SoftIcp3DComponent.cs:104); all internal lengths relative so unit-agnostic. |
| T5 tolerance-system count | flag | Three concurrent systems: scale-relative spacing factors; `RhinoMath.SqrtEpsilon` (2D contour, lines 491,666); absolute `tol*0.5` mesh inside-test (line 469) -- not reconciled to one declared budget. |
| T6 int64 overflow (Clipper2) | na | No integer scaling / Clipper2 in this path. |
| T7 snap-rounding/near-degenerate | flag | corrR cutoff hard-rejects `d2 > corrR2` (line 323); a sample at the annealed radius flips in/out between iterations (no hysteresis), jittering confidence. |

## Numeric stress findings

- Coordinate magnitude (T1/T3): rim points are world-pose points
  (SoftIcpRefiner.cs:75-79). At quarry/site coordinates (10^5-10^6 mm) the squared
  distance feeding `exp(-d2/tau)` (line 324) and the centroid sums lose mantissa
  precision; tau is tiny-relative (spacing^2 ~ 1) so catastrophic cancellation in
  $p_i-\bar q_i$ near contact. Recenter is the fix.
- Epsilon kinds: scale-relative (spacing, bbox) dominate (good); but `tol*0.5`
  absolute mesh inside-test (lines 469,640) and `RhinoMath.SqrtEpsilon` 2D Contains
  (line 491) are absolute and unreconciled -> T5.
- Overflow: none; no int scaling, no Clipper2. `exp(-d2/tau)` underflows cleanly to 0
  for far pairs (already cut at corrR); `wsum>1e-15` guard prevents divide-by-zero
  (line 334).
- Determinism: EM path deterministic (fixed order, no RNG). L-BFGS path single-shot
  BfgsMinimizer (SoftIcpLbfgs.cs:76) with NO RNG either, despite the `LbfgsRestarts`
  Gaussian-restart spec (SoftIcpOptions.cs:177-183) being UNIMPLEMENTED.
- Standing-flag check: NFP cache key na (no NFP); Clipper2 int64 na; transform
  composition drift LOW (left-compose via Transform.Multiply each iter, line 267,
  damped through Lie Exp not a matrix lerp -- correct); tolerance confusion across 3
  systems CONFIRMED (T5).

## Verdict: EVOLVE

The EM core is sound: correct CPD soft-correspondence, correct weighted Kabsch with a
proper determinant reflection guard, scale-relative tolerances, byte-deterministic,
penetration folded into the contact target so the two terms cannot fight. It is
production-grade on normalised ~100u fixtures, so not reject. But three issues block
reuse at architectural/quarry scale and block the gradient path matching its spec, so
it is evolve: (1) no recenter (T1) at far-from-origin coordinates; (2) O(N^2*P^2)
all-pairs cost (M5); (3) the L-BFGS path's translation parameterisation and Huber
smoothness diverge from the EM path / spec.

## Evolution plan

PERFORMANCE
- Recenter to the assembly bbox centroid before EM/LBFGS, translate Delta back. bbox
  already computed (SoftIcpRefiner.cs:746-755); rim points are world (75-79). Closes
  T1 and the T3 inside-test scale issue.
- Implement the documented `LbfgsRestarts` warm-restart + tau-anneal loop
  (SoftIcpOptions.cs:177-183) which Refine3D omits (SoftIcpLbfgs.cs:74-89) to escape
  the Myronenko-Song local-minimum trap.

ACCURACY
- Unify the LBFGS `ExpSe3` translation (raw M03/M13/M23, SoftIcpLbfgs.cs:263) onto
  `LieSe3.Exp`'s left-Jacobian translation (LieGroups.cs:144-146) so EM and LBFGS
  optimise the same coordinate (the docstring cross-validation claim is otherwise
  invalid).
- Replace the binary `IsPointInside` gate in the LBFGS Huber term
  (SoftIcpLbfgs.cs:179-187) with a signed-distance hinge so the objective is C^1 at
  the surface as `HuberPenetration` intends; defeats the discontinuity that breaks
  BFGS curvature conditions.
- Route the penetration inside/depth test through the geogram/CGAL robust kernel per
  the project HITL note instead of home-rolled `Mesh.IsPointInside` (G4, lines
  469,640); add a rank check on H for collinear rim samples (G2, line 397).

SPEED
- Spatial hash / KD-tree bucketed at corrR for the E-step (SoftIcpRefiner.cs:316-330)
  and MeanRimGap (548-571): O(N^2*P^2) -> O(N*P*k). The corrR cutoff (323) already
  defines the bucket size.
- Add the corrR prune to the LBFGS `EvalObjective` (SoftIcpLbfgs.cs:144-157, currently
  all-pairs unconditional) and derive the analytic CPD+Kabsch gradient to remove the
  2*dim finite-difference factor in NumericalGradient (207-214).