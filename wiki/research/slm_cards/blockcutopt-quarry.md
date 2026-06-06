---
slug: blockcutopt-quarry
algorithm: BlockCutOpt v2 quarry block-yield optimiser (brute-force cutting-grid pose search + BVH-pruned OBB/triangle SAT)
tier: 1
fabrication_direction: top_down
geometry_type: oriented bounding boxes vs triangle mesh (DFN fracture planes)
core_method_class: Elkarmoty-Bondua-Bruno 2020 (Resources Policy 68:101761); SAT = Akenine-Moller 2001
big_o_time: O(P * T*D2 * (B + Q))   # pose grid x per-pose grid build x per-block BVH-SAT
big_o_space: O(Tri) BVH + O(B) per-pose candidates, transient
parallel_model: serial
gotcha_flags: [T1, T2, P5, G2, M2, P4]
verdict: evolve
source_files:
  - BlockCutOptSolver.cs:107
  - CuttingGrid.cs:84
  - ObbTriangleIntersection.cs:27
  - TriangleAabbBvh.cs:175
  - BlockCutOptTolerances.cs:72
  - BlockCutOptSolverTests.cs:35
---

## What the code actually does (grounded)

`BlockCutOptSolver.SolveInternal` (BlockCutOptSolver.cs:85-145) runs a five-deep
nested loop over the rigid pose of a regular block grid: yaw `psi`
(line 107), tilt `theta` (108), tilt `phi` (109), and translations `dx` (111),
`dy` (113). For each pose it (1) generates the candidate grid via
`CuttingGrid.GenerateTilted` (line 116), (2) counts blocks that no fracture
triangle intersects via `CountNonIntersected` -> `bvh.AnyTriangleIntersects`
(line 121, 190), and (3) keeps the argmax count with its pose (122-130). The
fracture geometry is a `PlyMesh`; a `TriangleAabbBvh` is built once
(line 76) and reused across all poses and (via `SolveSubdivided`) all sub-zones
(line 49-54). Method class = Elkarmoty et al. 2020 BlockCutOpt; the SAT inner
kernel is Akenine-Moller 2001 (ObbTriangleIntersection.cs:16).

## Derived equations (from the code, not from memory)

Cutting-grid pose. `CuttingGrid.GenerateTilted` (CuttingGrid.cs:84-110) builds
the block axes from the rotation composition stated in the comment and matched
by the component algebra:

$$R(\psi,\theta,\phi) = R_z(\psi)\,R_x(\theta)\,R_y(\phi),\qquad
U = R e_1,\; V = R e_2,\; W = R e_3.$$

Reading lines 94-110, the columns are exactly:

$$U=\begin{pmatrix}\cos\psi\cos\phi-\sin\psi\sin\phi\sin\theta\\
\sin\psi\cos\phi+\cos\psi\sin\phi\sin\theta\\ -\sin\phi\cos\theta\end{pmatrix},\;
V=\begin{pmatrix}-\sin\psi\cos\theta\\ \cos\psi\cos\theta\\ \sin\theta\end{pmatrix},\;
W=\begin{pmatrix}\cos\psi\sin\phi+\sin\psi\cos\phi\sin\theta\\
\sin\psi\sin\phi-\cos\psi\cos\phi\sin\theta\\ \cos\phi\cos\theta\end{pmatrix}.$$

Block center for integer cell $(i,j)$, kerf-inflated pitch
$p_x=L_x+k$, $p_y=L_y+k$ (lines 77-78, 125-129), area centroid $c$:

$$\mathbf{b}_{ij} = c + (i\,p_x)\,U + (j\,p_y)\,V + (d_x,d_y,0)^\top.$$

Footprint clip (CuttingGrid.cs:136-144): a block is kept iff its four
horizontal corners $\mathbf{b}_{ij}\pm \tfrac{L_x}{2}U \pm \tfrac{L_y}{2}V$
lie inside the tested AABB in X and Y only.

Objective. The solver maximises the fracture-free count
(BlockCutOptSolver.cs:121-130):

$$(\psi^\star,\theta^\star,\phi^\star,d_x^\star,d_y^\star)
= \arg\max_{\text{pose}} N_{ni},\quad
N_{ni}=\big|\{\,b : \neg\,\exists\,\triangle\in M,\; \triangle\cap \mathrm{OBB}(b)\neq\varnothing\}\big|.$$

Recovery (BlockCutOptSolver.cs:135-139), Elkarmoty thesis Eq. 7-1 form, with
$V_B=L_xL_yL_z$ and the code's kerf-film approximation
$V_{kerf}=A_{xy}\cdot k/2$ (ApproximateKerfVolume, line 201):

$$R = \frac{N_{ni}\,V_B}{\max(V_{tested}-V_{kerf},\;10^{-12})}\times 100.$$

OBB/triangle overlap (ObbTriangleIntersection.cs:27-92): the 13-axis SAT.
Translate triangle to OBB frame $q_i=p_i-c$; the box is disjoint from the
triangle iff some axis $a$ in {U,V,W, n, U×e0..W×e2} separates them, where for
each axis the box half-projection is

$$r(a)=h_U|a\!\cdot\!U|+h_V|a\!\cdot\!V|+h_W|a\!\cdot\!W| \quad(\text{ObbHalfProjection, line 116})$$

and overlap requires $\min_i (q_i\!\cdot\!a) \le r(a)$ and
$\max_i (q_i\!\cdot\!a) \ge -r(a)$ (OverlapOnAxis, line 106). Degenerate cross
axes with $\|a\|^2<\mathrm{Eps}=10^{-12}$ are skipped (line 136).

BVH world-AABB of a (possibly tilted) OBB (TriangleAabbBvh.cs:179-181):

$$e_X=h_U|U_x|+h_V|V_x|+h_W|W_x|,\ \text{etc.},\quad
\mathrm{AABB}=[c-e,\,c+e].$$

Surface-area term feeding the I11 BCSdbBV Pareto axis (BlockValueModel.cs:54-57):
$S=2(L_xL_y+L_yL_z+L_xL_z)$.

## Complexity (from loop structure)

- Pose count $= P\cdot T_\theta\cdot T_\phi\cdot N_{dx}\cdot N_{dy}$, the five
  loops at BlockCutOptSolver.cs:107-113. With theta/phi disabled (default,
  IsPsiOnly) this collapses to $P\cdot N_{dx}\cdot N_{dy}$.
- Per pose: `GenerateTilted` is $O(B)$ over the $(2r_x+1)(2r_y+1)$ index box
  (CuttingGrid.cs:119-121), $B=O(\text{area}/p^2)$; each block does one BVH
  query $Q=O(\log \mathrm{Tri}+k\cdot 13)$ (TriangleAabbBvh.cs:189-224).
- Total time $O(P\,T_\theta T_\phi N_{dx}N_{dy}\,(B+Q))$. BVH build once,
  $O(\mathrm{Tri}\log\mathrm{Tri})$ (TriangleAabbBvh.cs:92-94, comment line 22),
  but the centroid sort allocates a fresh `int[]` per node (SortByCentroid,
  line 159) so build constant factor is heavy.
- Space: BVH `Node[]` + `int[] permutation` = $O(\mathrm{Tri})$; per-pose
  `List<OrientedBlock>` = $O(B)$, discarded each iteration.

## §4 Gotcha instrument (grounded, file:line)

| Code | Verdict | Reason |
|------|---------|--------|
| G1 exact/adaptive predicates | flag | All SAT projections are raw float64 dot/cross, no adaptive or exact predicate (ObbTriangleIntersection.cs:101-106). Fine at metre scale, not robust far from origin. |
| G2 degeneracy handled | flag | Degenerate triangle normal and parallel cross-axes are skipped via `nLen2>Eps` / `len2<Eps` (ObbTriangleIntersection.cs:60,136), but a fully degenerate (zero-area / collinear) triangle silently passes the normal+cross tests and can register a false hit on the 3 face-axis tests only. No explicit collinear/coincident-vertex guard. |
| G3 predicate/construction separation | pass | Grid construction (CuttingGrid) and the boolean overlap predicate (ObbTriangleIntersection) are cleanly separated; no constructed point feeds back into a predicate. |
| G4 robust boolean kernel | flag | This is a home-rolled float SAT (ObbTriangleIntersection.cs), not a robust kernel; acceptable because it is a boolean overlap classifier, not a boolean-volume cut. The actual cutting (ConvexPolyhedron.ClipByHalfSpace) is out of this card's scope. |
| M1 manifold output | na | Output is a count + pose + a filtered `OrientedBlock` list (BlockCutOptSolver.cs:173-179); no mesh is produced here. |
| M2 watertight/Euler | flag | The grid is a set of independent OBBs; adjacent kerf-inflated blocks neither share faces nor tile watertight, and the footprint clip (CuttingGrid.cs:135) keeps blocks by X/Y only, so tilted blocks are not volume-consistent with the tested area. Stated as a flag because downstream VTU export treats them as hexahedra. |
| M3 winding/self-intersection | na | No surface emitted by the solver. |
| M4 decimation/remesh error | na | No remeshing. |
| M5 data-structure hidden cost | flag | `TriangleAabbBvh.SortByCentroid` allocates and `Array.Sort`s a fresh slice at every internal node (TriangleAabbBvh.cs:159-167): O(Tri log^2 Tri) build with per-node GC churn, a hidden cost vs an in-place partition. |
| N1-N6 NURBS | na | No NURBS; geometry is OBBs and triangles only. |
| P1 deterministic reductions | pass | Serial argmax with strict `count > bestCount` (BlockCutOptSolver.cs:122) is deterministic; tests assert identical results across runs (BlockCutOptSolverTests.cs:228-232). |
| P2 thread safety shared geometry | na | Serial; BVH is immutable after build but never touched concurrently today. |
| P3 GPU memory | na | CPU only. |
| P4 load balance | flag | `SolveSubdivided` iterates zones in a serial `foreach` (BlockCutOptSolver.cs:51-55); per-zone block counts are highly irregular (dense vs sparse fracture zones) so there is a real imbalance opportunity left on the table. |
| P5 Amdahl serial tail | flag | The entire pose search is serial (BlockCutOptSolver.cs:107-133); this is the dominant cost and the whole-program serial tail. |
| T1 recenter far-from-origin | flag | SAT recenters the triangle about the OBB center (ObbTriangleIntersection.cs:34-36), which helps, but the grid and BVH AABBs are computed in raw world coordinates; the test fixture deliberately uses 1e6-magnitude coords (BlockCutOptSolverTests.cs:35) with no global recenter. |
| T2 absolute vs scale-relative epsilon | flag | `Eps=1e-12` is absolute (ObbTriangleIntersection.cs:21, BlockCutOptTolerances.cs:72) and the recovery floor `1e-12` is absolute (BlockCutOptSolver.cs:138). At 1e6 coords a normalised cross product can fall below 1e-12 spuriously; epsilon should scale with block pitch / area diagonal. |
| T3 float32 vs float64 | pass | Everything is `double` (BlockCutOptSolver, CuttingGrid, ObbTriangleIntersection); no float32 at architectural scale. |
| T4 units declared+consistent | pass | Units are documented as metres throughout (BlockCutOptTolerances.cs:11-23) with explicit mm/cm/ns conversion helpers. |
| T5 tolerance-system count reconciled | flag | At least three epsilons coexist: SAT `Eps=1e-12` (ObbTriangleIntersection.cs:21), recovery floor `1e-12` (BlockCutOptSolver.cs:138), vertex-dedupe `1e-9` (BlockCutOptTolerances.cs:78), plus loop guards `1e-9` (BlockCutOptSolver.cs:107-113). They are not derived from one tolerance system and are not reconciled to the Rhino model tolerance. |
| T6 int64 overflow (Clipper2) | na | No Clipper2 / integer-scaling path in this solver; all geometry is float64. |
| T7 snap-rounding / near-degenerate | flag | No snap-rounding; "touching counts as intersection" (ObbTriangleIntersection.cs:24) means a block grazing a fracture is rejected, but there is no tolerance band, so a sub-epsilon graze is classified by raw float sign only. |

Standing-flag checks: NFP cache-key -- na (no NFP here). Clipper2 int64 -- na.
Transform composition drift -- the R_z R_x R_y product is recomputed from
scalars per pose (CuttingGrid.cs:87-110), no accumulating composition, so no
drift (pass). Three-tolerance confusion -- FLAGGED (T5 above).

## Numeric stress findings

- Coordinate magnitude: solver is exercised at 1e6 m in the empty-fracture
  fixture (BlockCutOptSolverTests.cs:35) and at bench scale 0..65 m in the
  real cases. The far-field 1e6 case is the worst case for the absolute
  1e-12 SAT epsilon (T2): a unit-length cross product computed from
  differences of 1e6 magnitudes loses ~6 decimal digits, leaving only ~9-10
  significant digits, still above 1e-12 here but with no scale-relative
  margin if coordinates grow another 3 decades (UTM eastings ~5e5-1e7).
- Epsilon kind: absolute, three distinct values (1e-12, 1e-9, loop 1e-9);
  none scale with model size (T2/T5).
- Units: metres, declared and consistent (T4 pass); mm-based papers (Shao
  sawblade) converted on entry (BlockCutOptTolerances.cs:114, MmToMetres).
- Overflow: none. The only integer arithmetic is grid index range
  `idxRadiusX/Y` and a `checked((2r+1)(2r+1))` capacity (CuttingGrid.cs:119),
  which would throw rather than wrap; no int64 coordinate scaling exists.
- Determinism: confirmed by tests (BlockCutOptSolverTests.cs:228-232,290-293);
  any parallelisation must preserve it via an ordered reduction.

## Verdict: EVOLVE

The math is correctly derived from the BlockCutOpt 2020 method class and the
SAT/BVH kernels are sound and test-covered. It is not reject (it works and is
deterministic) and not pure reuse (a single-thread exhaustive pose grid with
absolute epsilons will not scale to granite-LVB extents or UTM coordinates).
The evolution levers below are each tied to a measured flag.

## Evolution plan

- PERFORMANCE (P5, BlockCutOptSolver.cs:107): the 5-deep pose loop is
  embarrassingly parallel and read-only against the immutable BVH. Wrap the
  outer `psi` loop in `Parallel.For` with per-thread `(bestCount, pose)`
  locals merged by a deterministic tie-break (count desc, then psi/dx/dy asc)
  to keep the determinism the tests assert (BlockCutOptSolverTests.cs:228).
- SPEED (algorithmic, BlockCutOptSolver.cs:107 flat 3deg sweep over [0,pi]):
  the coarse-to-fine sibling already exists (BlockCutOptCoarseToFine, exercised
  at BlockCutOptSolverTests.cs:383). Make it the default search so psi runs
  12deg -> 3deg -> 0.5deg around top-K, cutting pose evaluations by ~1-2 orders
  on unimodal yield landscapes without changing the argmax.
- ACCURACY (T1/T2, ObbTriangleIntersection.cs:21 + BlockCutOptSolver.cs:138):
  derive epsilons from block pitch / tested-area diagonal instead of absolute
  1e-12, and recenter triangles+OBBs about the tested-area centroid before SAT;
  the 1e6-coord fixture (BlockCutOptSolverTests.cs:35) is the regression hook.
- ACCURACY (G2/M2, CuttingGrid.cs:135): the footprint clip checks only 4
  horizontal corners at center Z, so tilted blocks (theta/phi != 0) can poke
  vertically outside the tested AABB yet still be counted. Switch to full
  8-corner containment when `!IsAxisAlignedZ` to stop over-counting tilted yield.
- PERFORMANCE (P4, BlockCutOptSolver.cs:51-55): parallelise `SolveSubdivided`
  across zones with `Parallel.ForEach` (zones independent, BVH read-only),
  fixing the irregular per-zone block-count load imbalance; also replace the
  per-node allocating `SortByCentroid` (TriangleAabbBvh.cs:159, M5) with an
  in-place nth-element partition to cut BVH build GC churn.