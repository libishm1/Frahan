---
slug: constrained-icp-3d
algorithm: Constrained ICP rigid registration of fracture rims (Kabsch/SVD per-iteration alignment + penetration/substrate guard)
core_method_class: Besl-McKay 1992 (ICP); Kabsch 1976 / Horn 1987 (per-iteration absolute orientation via SVD); DTW-style monotone DP (Sakoe-Chiba 1978 band) for non-crossing correspondence; cf. Marcotte-Suri 1991 (non-crossing matching, cited as non-portable)
fabrication_direction: bottom_up
geometry_type: point-sampled polyline rims + closed contour panels + optional substrate Brep
big_o_time: per-iter free-NN O(S*E); per-iter non-crossing O(B_off * S^2); x MaxIterations
big_o_space: free-NN O(S); non-crossing O(S^2) DP grid per trial
parallel_model: serial
gotcha_flags: [G2, M3, M5, P5, T1, T2, T3, T4, T5, T7]
source_files:
  - ConstrainedIcp3D.cs:30 (Refine loop)
  - ConstrainedIcp3D.cs:147 (Kabsch3D)
  - ConstrainedIcp3D.cs:191 (NotPenetrating)
  - ConstrainedIcp2D.cs:164 (SvdRigid2D)
  - OrderedBoundaryMatcher.cs:66 (MatchOpen DP)
  - IcpOptions.cs:3
  - Panel.cs:28
verdict: evolve
---

# Constrained ICP 3D (fracture-rim registration)

Two-tier SLM card. Math DERIVED from the Frahan code (read, not recalled). Method-class citations only.

## What the code actually does

`ConstrainedIcp3D.Refine` (ConstrainedIcp3D.cs:30) samples rim A into `S = SamplesPerSegment` (default 64, IcpOptions.cs:9) points via `Curve.DivideByCount` (ConstrainedIcp3D.cs:242). Each iteration (default cap `MaxIterations = 50`, IcpOptions.cs:5):
1. Push A's samples through the current transform (lines 62-68).
2. Build correspondence. Default: free nearest-point on B's polyline curve via `bCurve.ClosestPoint` (line 109) -- a linear scan over B's segments, no spatial index. Opt-in: `OrderedBoundaryMatcher.MatchClosed` (line 81), a monotone non-crossing DTW-style DP.
3. Solve the rigid step `delta` in closed form: 3D Kabsch SVD (`Kabsch3D`, line 147); the 2D variant reduces the SVD to a single `atan2` (`SvdRigid2D`, ConstrainedIcp2D.cs:184).
4. Compose `trial = delta * current` (line 118) and REJECT it if A's transformed centroid lands inside B's interior (`NotPenetrating`, line 191) or, with a substrate, if a sample is on the wrong side of the substrate normal (`OnCorrectSubstrateSide`, line 215). On reject the residual is multiplied by `PenetrationPenalty = 100` (line 127) and `current` is not advanced.
5. Converge when the step's translation `< TranslationTol` AND rotation `< RotationTolDeg`, or when the residual change `< TranslationTol` (lines 131-140).

## Derived equations (from the code)

Let A-samples after the current transform be $\{p_i\}_{i=1}^{S}$ and their correspondences on B be $\{q_i\}$.

Mean residual actually computed (lines 107-113):
$$ r = \frac{1}{S}\sum_{i=1}^{S} \lVert p_i - q_i \rVert_2 . $$
(Mean of Euclidean distances, NOT mean-squared.)

Per-iteration alignment is the orthogonal Procrustes / Kabsch solution. Centroids (lines 150-156):
$$ \bar p = \frac1S\sum_i p_i,\qquad \bar q = \frac1S\sum_i q_i . $$
Cross-covariance accumulated in RAW coordinates (lines 159-166):
$$ H = \sum_{i=1}^{S} (p_i-\bar p)(q_i-\bar q)^{\top}\in\mathbb{R}^{3\times3}. $$
SVD $H = U\Sigma V^{\top}$ (line 168). Reflection guard with the determinant (lines 175-177), exactly as coded:
$$ d = \det(V U^{\top}),\quad D=\mathrm{diag}(1,1,\operatorname{sgn}^{+}(d)),\ \operatorname{sgn}^{+}(d)=\begin{cases}+1 & d\ge 0\\ -1 & d<0\end{cases} $$
$$ R = V\,D\,U^{\top},\qquad t = \bar q - R\,\bar p . $$
Note: the guard uses `>= 0` (line 177), so a degenerate $d=0$ maps to $+1$ -- deliberate, per the in-code D8 note that `Math.Sign` would return 0 and skip the flip.

2D closed form (ConstrainedIcp2D.cs:175-191), derived from the same Procrustes problem reduced to a single rotation angle:
$$ \theta = \operatorname{atan2}\!\big((S_{xy}-S_{yx}),\,(S_{xx}+S_{yy})\big), $$
with $S_{xx}=\sum a_x b_x$, etc. over centered coords; $R(\theta)=\begin{bmatrix}\cos\theta&-\sin\theta\\\sin\theta&\cos\theta\end{bmatrix}$, $t=\bar q - R\bar p$.

Step magnitude used for convergence (ConstrainedIcp3D.cs:248-254):
$$ \Delta t = \sqrt{M_{03}^2+M_{13}^2+M_{23}^2},\qquad \Delta\phi=\arccos\!\Big(\mathrm{clamp}\tfrac{\mathrm{tr}(R)-1}{2}\Big). $$

Monotone correspondence (OrderedBoundaryMatcher.cs:88-131). DP over the $n\times m$ grid with squared-distance cell cost $c(i,j)=\lVert a_i-b_j\rVert^2$:
$$ C(i,j)=c(i,j)+\min\{C(i-1,j-1),\,C(i-1,j),\,C(i,j-1)\}, $$
diagonal preferred on ties; only diagonal moves emit a matched pair (lines 154-166); optional Sakoe-Chiba band $|i-j|\le \text{maxGap}$ (line 105). `MatchClosed` wraps this with cyclic offset + reversal trials and keeps the minimum mean-matched-distance candidate (lines 217-240).

## Code sketch / reuse seam

The clean reuse seam is `OrderedBoundaryMatcher` (RhinoCommon-light, only `Point3d` as a value container, no Rhino-runtime calls -- header lines 38-40) and `Kabsch3D` (pure MathNet SVD on a 3x3, line 147). Both are unit-testable headless. `Refine` is the orchestrator that owns the Rhino geometry calls (`Curve.ClosestPoint`, `Curve.Contains`, `Brep.ClosestPoint`).

## Gotchas (G/M/N/P/T)

| Code | Verdict | Reason (file:line) |
|---|---|---|
| G1 exact/adaptive predicates | flag | No exact/adaptive predicates; pure float64. Containment via `Curve.Contains` with absolute `SqrtEpsilon` (ConstrainedIcp3D.cs:208-211). |
| G2 degeneracy handled | flag | Empty-sample guard exists (line 52) and the `d>=0` reflection guard handles det=0 (line 177); but collinear/zero-area sample clusters make $H$ rank-deficient with no explicit rank check -- the SVD still returns something, unguarded. |
| G3 predicate/construction separation | pass | Correspondence (test) and Kabsch (construction) are distinct phases; no mixing. |
| G4 robust boolean kernel | na | No booleans; containment only. |
| M1 manifold output | na | Output is a `Transform`, not a mesh. |
| M2 watertight/Euler | na | No mesh. |
| M3 consistent winding + self-intersection | flag | Free-NN correspondence (line 109) can produce crossing/tangled pairings on wiggly rims; the non-crossing path that fixes this is opt-in and off by default (IcpOptions.cs:21). |
| M4 bounded decimation error | na | No remesh/decimation. |
| M5 hidden O(n) data-structure cost | flag | `Curve.ClosestPoint` is a linear segment scan (no spatial index), making correspondence O(S*E) and re-scanning B every iteration (line 109). |
| N1-N6 NURBS | na | Polylines only; `ToPolylineCurve` (line 42). No NURBS knots/weights/trim. |
| P1 deterministic reductions | pass | Serial accumulation, fixed order; DP ties break toward diagonal; MatchClosed ties break to lowest offset/forward (OrderedBoundaryMatcher.cs:227-228). |
| P2 thread safety | na | Serial; no shared mutable geometry across threads. |
| P3 GPU | na | CPU only. |
| P4 load balance | na | Serial. |
| P5 Amdahl serial tail | flag | Entire solver is serial; the up-to 6-10 MatchClosed trials per iteration are independent and unparallelised (OrderedBoundaryMatcher.cs:217-238). |
| T1 recenter before far-from-origin compute | flag | $H$ is accumulated in raw panel-local coords (lines 159-166); no recenter to a shared origin before the SVD. Centroid subtraction helps but the centroid is itself computed in raw coords. |
| T2 absolute vs scale-relative epsilon | flag | `RhinoMath.SqrtEpsilon` (~1.49e-8) used as an absolute containment/side tolerance (lines 211, 219) irrespective of model scale. |
| T3 float32 vs float64 | flag | All float64 (`Point3d`, `Matrix<double>`), good -- but combined with T1 the cross-covariance loses precision far from origin at architectural scale. |
| T4 units declared+consistent | flag | No unit declaration; `TranslationTol=1e-4` and `RotationTolDeg=1e-3` are unit-naive constants (IcpOptions.cs:6-7). |
| T5 tolerance-system count reconciled | flag | Three tolerance kinds coexist: `TranslationTol` (also reused as a residual-DELTA tolerance, ConstrainedIcp3D.cs:136), `RotationTolDeg`, and `SqrtEpsilon` containment -- not reconciled to one system. |
| T6 integer overflow (int64) | na | No integer scaling / Clipper2 here; DP indices are small ints bounded by S. |
| T7 snap/near-degenerate defined | flag | Containment near-boundary handled only by the fixed `SqrtEpsilon`; no scale-relative snap definition for the near-degenerate centroid-on-rim case. |

## Numeric stress findings
- Coordinate magnitude: Kabsch cross-covariance `H` (ConstrainedIcp3D.cs:159-166) is formed in raw panel-local coordinates. At quarry/building scale far from origin, catastrophic cancellation in `(p_i - mean)` degrades `H` precision (T1/T3). Mitigated only if upstream already centers panels in `LocalFrame` (Panel.cs:44-46), which is not guaranteed.
- Epsilon kind: a single absolute `RhinoMath.SqrtEpsilon` governs containment and substrate-side classification (lines 211, 219). For mm-scale fractures this is far too tight; for metre blocks too loose -- a direct violation of the project scale-invariance constraint.
- Units: none declared; tolerances are bare constants (IcpOptions.cs:6-7). `TranslationTol` is overloaded as both a step-translation tolerance and a residual-delta tolerance (line 136), which have different units (length vs mean-distance), so convergence can fire early or never depending on input scale.
- Overflow: none. All compute is float64; DP integer indices bounded by S (default 64). T6 not exercised.
- Reflection: correctly guarded by the determinant (not `Math.Sign`), with `>=0 -> +1` so the rare det=0 case does not silently skip the flip (lines 175-177). Good.

## Evolution plan

PERFORMANCE
- Replace the O(S*E) per-iteration `Curve.ClosestPoint` linear scan (ConstrainedIcp3D.cs:109) with a one-time RTree/kd-tree over B's samples queried per A-sample -> O(S log E). The B samples are already materialised when the non-crossing path is on (bPts, line 47), so the index is cheap to add.
- Hoist and reuse the O(S^2) `cost`/`back` DP buffers out of `MatchOpen` (OrderedBoundaryMatcher.cs:89-91); they are reallocated for each of the up-to 2*(2*bracket+1) MatchClosed trials per iteration (64x64 grid, 6-10x per iter).

ACCURACY
- Recenter samples to a shared origin (panel `LocalFrame`, Panel.cs:44) before forming `H` (lines 159-166) to keep float64 precision far from world origin (fixes T1/T3).
- Make the containment / substrate-side epsilon scale-relative (panel bbox diagonal or doc ModelAbsoluteTolerance) instead of the fixed `SqrtEpsilon` (lines 211, 219) -- fixes T2/T7 and satisfies the scale-invariance constraint.
- Give residual-stagnation its own relative tolerance instead of reusing `TranslationTol` (line 136) -- fixes the T5 unit overload.

SPEED
- Parallelise the independent MatchClosed offset/orientation trials (OrderedBoundaryMatcher.cs:217-238) with a deterministic argmin reduction; each trial owns its DP buffers so thread-safety is trivial and the existing strict-`<` lowest-offset/forward tie-break is preserved (fixes the P5 serial tail without breaking P1 determinism).

## Verdict: EVOLVE
The core math is correct and faithfully Kabsch/Horn with the mandatory reflection guard, and the non-crossing matcher is a genuinely useful robustness primitive. It is not reject-grade. But the default free-NN correspondence is unindexed (M5) and crossing-prone (M3), the tolerance system is unreconciled and scale-absolute (T1/T2/T4/T5/T7), and the whole solver is a serial tail (P5). These are addressable without redesign, so the verdict is evolve, not reuse or reject.