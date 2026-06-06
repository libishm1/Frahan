---
slug: vsa-segmentation
algorithm: Variational Shape Approximation (VSA) planar-proxy mesh segmentation
core_method_class: Cohen-Steiner / Alliez / Desbrun 2004 SIGGRAPH (L^{2,1} metric + Lloyd clustering)
fabrication_direction: bottom_up
geometry_type: triangle mesh (RhinoCommon Mesh) -> planar face patches + mean planes
big_o_time: O(I * N * k)  (N faces, k proxies, I Lloyd iterations)
big_o_space: O(N + k)
parallel_model: serial
verdict: evolve
source_files:
  - Frahan.EdgeMatching.Core/VsaSegmenter.cs:96   (Segment driver)
  - Frahan.EdgeMatching.Core/VsaSegmenter.cs:222  (FaceProxyError, L^{2,1})
  - Frahan.EdgeMatching.Core/VsaSegmenter.cs:233  (Partition / E-step)
  - Frahan.EdgeMatching.Core/VsaSegmenter.cs:286  (FitProxies / M-step)
  - Frahan.EdgeMatching.Core/VsaSegmenter.cs:333  (InitialSeeds)
  - Frahan.EdgeMatching.Core/VsaSegmenter.cs:374  (BuildFaceAdjacency)
gotcha_flags: [M1, M3, T2, T3, T7]
---

# VSA Segmentation -- Tier-1 SLM Card

Frahan bottom-up preprocessing: decompose a scanned-stone mesh into a small
set of quasi-planar face patches (feeds B3D/A3D/C3D/D3D + Cyclopean Recipe
Coursing). Method class is Cohen-Steiner / Alliez / Desbrun 2004; the file
states it is recovered from CGAL `Variational_shape_approximation.h`
(VsaSegmenter.cs:8-39). Status declared PHASE-1 REAL (line 25).

## Derived equations (from the code, not from memory)

Let face $f$ have unit normal $\mathbf{n}_f$ (line 116-118, unitized),
area $A_f$ (line 408, $A_f = \tfrac12 \lVert (\mathbf{b}-\mathbf{a})\times(\mathbf{c}-\mathbf{a})\rVert$),
and centroid $\mathbf{c}_f$ (line 419, arithmetic mean of the 3 vertices).
Proxy $p$ has a normal $\mathbf{N}_p$ and origin $\mathbf{o}_p$.

**Per-face metric** (FaceProxyError, lines 222-226):
$$
E(f,p) \;=\; A_f \,\lVert \mathbf{n}_f - \mathbf{N}_p \rVert^2 .
$$
This is the area-weighted $L^{2,1}$ normal metric. (The code stores
$\mathbf{N}_p$ as a non-normalized accumulator candidate but renormalizes
in the M-step, so at evaluation time both vectors are unit; then
$\lVert \mathbf{n}_f - \mathbf{N}_p\rVert^2 = 2 - 2(\mathbf{n}_f\!\cdot\!\mathbf{N}_p)$,
which the code does NOT exploit -- see SPEED lever.)

**E-step / partition** (lines 238-248):
$$
\text{assign}(f) \;=\; \arg\min_{p} \; E(f,p).
$$
Pure per-face independent argmin. NOT the paper's adjacency-aware
best-first flood; the docstring (lines 228-232) admits this. A
connectivity-repair pass follows (lines 253-279): any face whose
neighbours share none of its proxy is reassigned to the dominant
neighbour proxy, iterated <=5 times (line 255).

**M-step / proxy refit** (lines 286-317). Minimiser of $\sum_{f\in p} E(f,p)$
over unit $\mathbf{N}_p$ is the normalized area-weighted normal sum:
$$
\mathbf{N}_p \;=\; \frac{\sum_{f\in p} A_f \,\mathbf{n}_f}{\bigl\lVert \sum_{f\in p} A_f \,\mathbf{n}_f \bigr\rVert},
\qquad
\mathbf{o}_p \;=\; \frac{\sum_{f\in p} A_f \,\mathbf{c}_f}{\sum_{f\in p} A_f}.
$$
Guarded by $\lVert\sum\rVert^2 > 10^{-18}$ (line 307); empty proxies keep
prior values (line 304).

**Total energy + convergence** (lines 319-327, 161-165):
$$
\mathcal{E} = \sum_f E(f,\text{assign}(f)),
\qquad \text{stop when } \frac{\lvert \mathcal{E}_{t-1}-\mathcal{E}_t\rvert}{\mathcal{E}_{t-1}} < \tau,\;\; \tau = 0.005 .
$$

**Patch mean plane** (lines 185-201):
$\mathbf{o}_p^{\text{patch}} = (\sum A_f \mathbf{c}_f)/(\sum A_f)$, plane
$= \text{Plane}(\mathbf{o}_p^{\text{patch}}, \mathbf{N}_p)$.

**Post-filters** (lines 204-206):
drop patch if $\text{Area}_p = \sum_{f\in p} A_f < 15000$ (mm^2) OR
$\max_{f\in p} \lvert \text{dist}(\mathbf{c}_f, \text{plane}_p)\rvert > 5.0$ (mm)
(ComputeMaxResidual, lines 425-436).

**Seeding** (InitialSeeds, lines 333-372): seed 0 = largest-area face
(line 340); seed $i$ = farthest-point in normal space,
$\arg\max_f \min_{j<i} \lVert \mathbf{n}_f - \mathbf{n}_{seed_j}\rVert^2$
(lines 346-369). NOT hierarchical-doubling (TODO line 35).

## Code sketch / reuse seam

Entry: `List<Patch> Segment(Mesh mesh)` (line 96). Output `Patch` carries
`Id`, `MeanPlane`, `List<int> FaceIndices`, `Area`, and `Normal => MeanPlane.ZAxis`
(lines 81-88). Quads are triangulated first (line 103). Adjacency via
`mesh.TopologyEdges.GetConnectedFaces` (lines 381-395). Deterministic:
`Seed=1` exists (line 78) but is UNUSED -- seeding is fully deterministic
farthest-point, so the field is dead. Reuse seam is clean: pure CPU,
RhinoCommon-only, no external native dep.

## Gotchas (G/M/N/P/T)

| Check | Result | Reason (file:line) |
|---|---|---|
| G1 exact/adaptive predicates | na | No orientation/incircle predicates; only normals + areas. |
| G2 degeneracy | flag | Zero-area faces get $A_f=0$ -> $E=0$, silently glued to proxy 0 in argmin (line 241); no skip. |
| G3 predicate/construction sep | na | No predicate layer. |
| G4 robust boolean kernel | na | No booleans. |
| M1 manifold output | flag | Patches are face-index sets; per-face argmin + <=5 repair passes (line 255) do not guarantee each patch is connected/manifold -- repair may not converge. |
| M2 watertight/Euler | na | Output is patch labels, not a remeshed surface; no Euler claim made. |
| M3 winding + self-intersection | flag | No winding/orientation check; a flipped face normal (line 116) silently lands in the wrong proxy and skews the area-weighted sum (line 298). |
| M4 bounded decimation error | pass | FitResidualMax (line 67) bounds worst-face perp distance; patches over 5mm residual dropped (line 206). |
| M5 data-structure cost | pass | Adjacency built once O(N) (lines 374-399); per-face arrays O(N). No hidden blowup. |
| N1-N6 NURBS | na | Mesh-only; no NURBS/spline. |
| P1-P5 parallel | na | Fully serial (single-threaded loops throughout). |
| T1 recenter far-from-origin | flag | Centroid/area computed on raw world coords (lines 405-422); no recenter -> precision loss at quarry/architectural coordinates. |
| T2 abs vs scale-relative eps | flag | MinFaceArea=15000 mm^2 (line 48) and FitResidualMax=5.0 mm (line 67) are ABSOLUTE; break scale-invariance (mm fractures vs m blocks). |
| T3 float32 vs float64 | flag | float64 throughout (Vector3d/Point3d doubles) -- good -- BUT no recenter (T1) erodes the normal precision the metric depends on at large coords. |
| T4 units declared+consistent | pass | Docstrings state mm for area/residual (lines 47-48, 66-67); consistent. |
| T5 tolerance-system count | pass | One tolerance system (input units); no Rhino doc tol / Clipper int scale mixed in here. |
| T6 int64 overflow (Clipper2) | na | No integer scaling / Clipper in this file. |
| T7 snap/near-degenerate | flag | Near-zero proxy normal sum guarded at 1e-18 (line 307) but degenerate (zero-area, near-coincident-normal) faces are not snapped/merged; thin slivers from scan noise pollute the argmin. |

## Numeric stress findings
- Coordinate magnitude: NO recentering (lines 401-423 operate on world
  coords). At quarry/architectural coordinates (1e4-1e6 mm from origin)
  the subtraction $\mathbf{b}-\mathbf{a}$ in the cross product loses
  relative precision; normals degrade before the metric ever runs. T1/T3.
- Epsilon kind: absolute. Energy guard $10^{-18}$ (line 307) is an
  absolute squared-length floor, fine for unit normals. Filter epsilons
  15000 mm^2 / 5.0 mm are absolute and scale-bound (T2).
- Units: mm declared and internally consistent (no conversion path here).
- Overflow: none. All double; areas/energies sum monotonically; no int
  scaling. The $10^{18}$ inverse of the 1e-18 guard is well within double
  range.
- Determinism: deterministic (largest-area + farthest-point seeding,
  serial loops). The `Seed` field (line 78) is dead -- no RNG is used,
  so output is reproducible but the field misleads.

## Evolution plan
- PERFORMANCE (partition correctness): replace per-face independent argmin
  E-step (lines 238-248) + heuristic 5-pass connectivity repair (lines
  253-279) with the paper's adjacency-aware best-first priority-queue flood
  (seed queue at proxy origins, pop min $E$, claim unassigned neighbours).
  Directly addresses the M1/M3 connectivity flags.
- ACCURACY (seeding + local minima): add hierarchical-doubling seeding
  (paper section 5 / CGAL init_hierarchical, file TODO line 35) and
  teleport/merge/split operators (Skrodzki 2020) so the 0.5% convergence
  test (line 165) does not lock onto a poor local minimum from the
  order-sensitive farthest-point seed (lines 346-369).
- ACCURACY (scale invariance): make MinFaceArea (line 48) and
  FitResidualMax (line 67) relative to mesh bounding-box diagonal / total
  area; closes the T2 absolute-epsilon flag and satisfies the mm-to-m
  scale-invariance constraint.
- SPEED (hot loop): the N*k FaceProxyError calls per iteration (lines
  238-248) are the bottleneck; both vectors are unit so substitute
  $\lVert\mathbf{n}_f-\mathbf{N}_p\rVert^2 = 2 - 2(\mathbf{n}_f\!\cdot\!\mathbf{N}_p)$
  (one dot product) and skip faces whose neighbour proxies were unchanged.
- SPEED/precision (T1/T3): recenter the mesh to its centroid once before
  the loop, compute areas/centroids/normals in local float64 coords, then
  translate output `MeanPlane` origins back -- removes far-from-origin
  precision loss in lines 401-423 at no asymptotic cost.