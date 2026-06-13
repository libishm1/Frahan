# 03. Quarry Block-Cutting Optimization

## 3.0 Scope and lineage

This chapter covers the **Quarry** ribbon tab subset built on the `BlockCutOpt`
engine: the pose-sweep solver, its recovery and yield objective, the Jalalian
cutting-surface-area-per-value cost axis, the multi-scale recovery cascade, and
the downstream flat-versus-oblique guillotine and gangsaw cost frontier studied
in examples 08, 24, and 25. The core lives at
`src/Frahan.StonePack.Core/Masonry/Quarry/BlockCutOpt`. The managed solver is a
clean-room reimplementation of the BlockCutOpt brute-force algorithm
(Elkarmoty et al. 2020), whose original implementation is private C++
(`BlockCutOpt/README.md:46-50`). The repository's tracked delta is documented as
the "14 improvements" table (`README.md:185-198`), of which 12 of 14 are shipped
(`README.md:200`). Per the originality framework, the base solver is
**clean-room** (published math, no upstream source in the tree); the I1 full-3D
pose, the I6/I11 Pareto/BCSdbBV axes, and `RecoveryCascade` are the
**evolved-fork** increments claimed in the submitted BoEGE paper
(Murugean 2026, Zenodo DOI 10.5281/zenodo.20608279).

The geological front end (the GPR fracture chain that feeds the tested area) is
covered in the GPR chapter; here we take the fracture mesh as given input.

---

## 3.1 The block-cut objective: recovery over a pose space

### 3.1.1 Problem statement

A quarry bench is an axis-aligned tested region
$\mathcal{A}\subset\mathbb{R}^3$. The rock contains a discontinuity set
represented as a triangle soup $\mathcal{F}=\{T_k\}$ (the fan-triangulated
fracture mesh). A **cutting grid** lays out candidate commercial blocks of fixed
size $(L_x,L_y,L_z)$ on a regular lattice. A block is *marketable* only if it is
crossed by no fracture. The decision variables are the rigid pose of the lattice:
the yaw $\psi$ about the vertical axis, the optional tilts $\theta,\phi$ about the
horizontal axes, and the in-plane offsets $(d_x,d_y)$. The objective is to
maximise the count of non-intersected blocks.

Let $G(\psi,\theta,\phi,d_x,d_y)$ be the set of candidate oriented blocks
(OBBs) whose footprint lies inside $\mathcal{A}$. Write the indicator

$$
\chi(b,\mathcal{F})=
\begin{cases}
1 & \text{if } \forall T_k\in\mathcal{F}:\ b\cap T_k=\varnothing,\\
0 & \text{otherwise.}
\end{cases}
$$

The non-intersected count is

$$
N_{ni}(\psi,\theta,\phi,d_x,d_y)=\sum_{b\in G(\psi,\theta,\phi,d_x,d_y)}\chi(b,\mathcal{F}),
$$

and the solver returns the argmax over the discretised pose grid:

$$
(\psi^\*,\theta^\*,\phi^\*,d_x^\*,d_y^\*)=\arg\max_{\psi,\theta,\phi,d_x,d_y} N_{ni}.
$$

This is the brute-force search of Elkarmoty et al. (2020), restricted in their
paper to $\psi$-only with $\theta=\phi=0$. The implementation enumerates the
full five-dimensional pose grid (`BlockCutOptSolver.cs:108-113`).

### 3.1.2 The cutting-grid pose (I1: full 3D rotation)

The original algorithm rotates the lattice only by yaw $\psi$ about the vertical.
The repository adds tilt about both horizontal axes (improvement I1,
`README.md:185`). The grid basis is built from the rotation

$$
R=R_z(\psi)\,R_x(\theta)\,R_y(\phi),
$$

and the three block axes are $U=R\,e_x$, $V=R\,e_y$, $W=R\,e_z$.
Carrying the product through (the derivation that is inlined and pre-multiplied
in `CuttingGrid.cs:91-110`) gives, for the in-plane $U$ axis,

$$
U=\begin{pmatrix}
\cos\psi\cos\phi-\sin\psi\sin\phi\sin\theta\\
\sin\psi\cos\phi+\cos\psi\sin\phi\sin\theta\\
-\sin\phi\cos\theta
\end{pmatrix},\quad
V=\begin{pmatrix}-\sin\psi\cos\theta\\ \cos\psi\cos\theta\\ \sin\theta\end{pmatrix},\quad
W=\begin{pmatrix}\cos\psi\sin\phi+\sin\psi\cos\phi\sin\theta\\ \sin\psi\sin\phi-\cos\psi\cos\phi\sin\theta\\ \cos\phi\cos\theta\end{pmatrix}.
$$

Setting $\theta=\phi=0$ collapses $U\to(\cos\psi,\sin\psi,0)$,
$V\to(-\sin\psi,\cos\psi,0)$, $W\to(0,0,1)$, recovering the BlockCutOpt-2020
yaw-only grid exactly. This is verified in source: the `OrientedBlock`
$\psi$-only constructor hard-sets $U_z=V_z=W_x=W_y=0$, $W_z=1$
(`OrientedBlock.cs:40-50`). The lattice centre of cell $(i,j)$ is

$$
c_{ij}=c+ i\,p_x\,U + j\,p_y\,V + (d_x,d_y,0),\qquad
p_x=L_x+k,\ p_y=L_y+k,
$$

with $c$ the tested-area centroid and $k$ the saw **kerf**: the cell pitch is the
block dimension *plus* the kerf, so the gap between adjacent blocks is exactly the
material lost to the saw (`CuttingGrid.cs:77-78`, `127-129`). A candidate is kept
only if its four horizontal footprint corners lie inside $\mathcal{A}$
(`CuttingGrid.cs:135-145`).

> **Originality.** `CuttingGrid.GenerateTilted` and `BlockCutOptSolver`:
> **clean-room** base (Elkarmoty et al. 2020), with the I1 full-3D pose an
> **evolved-fork** increment. Evidence: `[Algorithm]` at
> `BlockCutOptComponents.cs:97-100` ("BlockCutOpt brute-force search",
> Doi 10.1016/j.resourpol.2020.101761; "Full 3D rotation grid", Frahan I1).
> Tier B for the base, A-candidate-adjacent for the tilt axes.

### 3.1.3 The intersection predicate (I2/I4)

The inner test $\chi$ is a triangle-versus-OBB overlap. The implementation uses
the 13-axis Separating Axis Theorem of Akenine-Moller (2001): three OBB face
normals $U,V,W$, the triangle normal, and the nine cross products
$U,V,W \times e_0,e_1,e_2$ of OBB axis against triangle edge
(`ObbTriangleIntersection.cs:10-16`). A block is clean iff a separating axis
exists for *every* nearby triangle. To avoid testing every triangle against every
block, fracture triangles are stored in an axis-aligned BVH and only the leaves
overlapping a block's bound are tested (improvement I2,
`TriangleAabbBvh`, used at `BlockCutOptSolver.cs:243-256`). An edge-triangle
variant (Moller-Trumbore) is improvement I4 (`README.md:188`).

> **Originality.** SAT predicate and BVH: **clean-room** faithful
> implementation of published geometry (Akenine-Moller 2001). Evidence:
> `[Algorithm("Triangle-AABB BVH pruning", "Akenine-Moller 2001 ...")]`
> `BlockCutOptComponents.cs:99`. Tier C (textbook geometry, engineering value).

### 3.1.4 Deterministic parallel argmax

The pose grid is large; the per-pose grid build and SAT counting are the cost.
The solver enumerates all poses in the exact serial loop order, computes each
pose's count in `Parallel.For` (every pose reads the immutable BVH and builds its
own grid, so there is no shared mutable state), then takes a serial strict-greater
argmax over the original enumeration order (`BlockCutOptSolver.cs:106-135`).
Strict-greater ($>$ not $\ge$) means ties resolve to the earliest pose, so the
chosen pose is **bit-identical** to the serial reference
(`SolveInternalSerial`, `BlockCutOptSolver.cs:151-211`), validated headless in
`BlockCutOptParallelTests`. This is an engineering detail with a real
correctness consequence: parallelism does not perturb the result.

### 3.1.5 Recovery and yield

The reported yield is the kerf-aware **recovery** of Elkarmoty's thesis Eq. 7-1
(`BlockCutOptResult.cs:9-14`):

$$
R = \frac{N_{ni}\,V_B}{V_{\text{tested}}-V_{\text{kerf}}}\times 100\%,
$$

where $V_B=L_xL_yL_z$ is the block volume and $V_{\text{kerf}}$ is the volume
lost to the saw inside the grid. The implementation approximates the kerf volume
as a thin film of thickness $k/2$ over the footprint
($V_{\text{kerf}}\approx A_{xy}\,k/2$, `BlockCutOptSolver.cs:261-268`), and floors
the denominator at $10^{-12}$ to stay finite. Putting the denominator at the
*intact-minus-kerf* volume rather than the gross volume means recovery measures
how much of the *cuttable* rock is captured, not how much of the bench. The
README's documented Stratum-a run returns $N_{ni}=83$, $R=44.21\%$, $\psi=84^\circ$
on a $40\times30\times6$ m bench (`README.md`).

---

## 3.2 The Pareto front and the Jalalian I11 cost axis

A single recovery scalar hides the economics: more blocks can mean more saw cuts
and a worse cost per unit value. The repository adds a four-objective Pareto
solver (improvement I6, `BlockCutOptParetoSolver.cs`). Each pose is scored on:

$$
\underbrace{N_{ni}}_{\text{recovery}\ \uparrow},\quad
\underbrace{\Pi=\textstyle\sum_b \mathrm{RMV}_b}_{\text{revenue}\ \uparrow},\quad
\underbrace{\tau=\textstyle\sum_b t_b}_{\text{kerf time}\ \downarrow},\quad
\underbrace{Z_{\mathrm{BCSdbBV}}}_{\text{cut cost}\ \downarrow}.
$$

A pose $p$ dominates $q$ iff it is no worse on all four axes and strictly better
on at least one; recovery and revenue are maximised, kerf-time and BCSdbBV
minimised (`ParetoPoint.Dominates`, `ParetoPoint.cs:54-71`).

### 3.2.1 BCSdbBV (I11): cutting surface area per block value

The fourth axis is the Jalalian et al. (2023) **BCSdbBV** objective: the total
cut surface area divided by the total recovered block value (improvement I11,
`README.md:195`, citing *Sci. Reports* 13, Doi 10.1038/s41598-023-49633-w):

$$
Z_{\mathrm{BCSdbBV}}=\frac{\sum_{b}\, S(b)}{\sum_{b}\, \mathrm{BV}_b},
$$

where $S(b)$ is the OBB surface area

$$
S(b)=2\,(L_xL_y+L_yL_z+L_xL_z)
$$

(`BlockValueModel.SurfaceArea`, `BlockValueModel.cs:54-58`) and $\mathrm{BV}_b$ is
the per-block value (default $=$ block volume, a richer model multiplies by a
class A/B/C price factor, `BlockValueModel.cs:22-27`). The accumulation runs only
over non-intersected blocks; when no value is recovered the ratio is set to
$+\infty$ so the pose cannot dominate
(`BlockCutOptParetoSolver.cs:82-95`). Minimising $Z_{\mathrm{BCSdbBV}}$ favours
layouts that spend the least saw surface per dollar of stone, which is the
sustainability objective Jalalian formalised to cut quarry waste.

> **Originality.** Pareto solver and BCSdbBV axis: **evolved-fork** over the
> single-scalar Elkarmoty objective. Evidence: `[Algorithm]` pair at
> `BlockCutOptComponents.cs:306-307` ("BlockCutOpt omni-solve ... Pareto",
> Elkarmoty 2020; "BCSdbBV cost objective", Jalalian 2023). The Omni solver
> composes sub-division, coarse-to-fine search, and the per-zone Pareto front
> (`BlockCutOptOmniSolver.cs:115-178`). Tier B (faithful axis from Jalalian,
> integrated as a new objective).

### 3.2.2 The cost / volume / balanced frontier (examples 25 and 08)

The Pareto front is exercised by a single scalarising knob in the example
studies: the packer maximises $\mathrm{net} + W\cdot V$, where $W$ ($/m^3$) is a
volume credit swept from cost ($W=0$) through balanced to volume
($W\to\infty$). Example 25 (synthetic 6 x 3 x 3 m marble bench, three oblique
fractures, 0.5 m keep-out) reports the measured frontier
(`25_cost_metrics.json`):

| Objective | $W$ ($/m^3$) | Blocks | Volume | Recovery (intact) | NET |
|---|---|---|---|---|---|
| Max cost | 0 | 15 | 30.5 m3 | 71.8% | **$25,650** |
| Balanced | 800 | 20 | 35.5 m3 | 83.5% | $24,650 |
| Max volume | $10^6$ | 25 | 38.0 m3 | 89.4% | $16,700 |

![Balanced gangsaw packing on a fractured marble bench](../../../examples/25_marble_gangsaw_cost/25c_balanced.png)

The economics are non-trivial: per the catalogue, price per $m^3$ falls and cut
cost per $m^3$ rises as blocks shrink, so the smallest 1x1x1 block has negative
net ($-200) (`25_cost_metrics.json` catalogue). The trade-off the example
quantifies: cost-to-balanced recovers +5.0 m3 for only -$1,000 net (-3.9%,
marginal $200/m3), but cost-to-volume costs -$8,950 (-34.9%) because reaching the
last cells forces premium gangsaw runs to be broken into loss-making offcuts. The
recommended operating point is balanced.

---

## 3.3 The bed-bounded hexahedra fix and flat-vs-oblique guillotine (example 08)

### 3.3.1 The bug

Example 08 packs real Botticino marble: a 600 MHz GPR grid yields 280 fracture
picks clustered into three dipping beds (0.72 m / 6.1 deg, 2.10 m / 0.9 deg,
3.70 m / 6.1 deg, sub-cm plane-fit RMS) (`08_marble_cost_volume_metrics.json`).

![GPR-extracted three-bed grid, Botticino marble](../../../examples/08_gpr_marble/08b_bench_beds.png)

The original layout placed axis-aligned blocks at the *mean* bed depth. Because
the beds dip ~6 deg, a flat box centred at the mean depth **crosses** the dipping
bed plane at its ends, so the "intact" block actually straddles a parting. The
fix is to bound each block between the *true* bed planes rather than at a constant
$z$: the recovered solids are **bed-bounded hexahedra** whose top and bottom faces
follow the per-$(x,y)$ bed planes. The metrics note records this directly:
"Block economics use each layer's mean spacing; the oblique geometry follows the
true per-(x,y) bed planes, so displayed blocks never cross a bed"
(`08_marble_cost_volume_metrics.json`, limitations). Concretely, a block with
footprint $[x_0,x_1]\times[y_0,y_1]$ between an upper bed plane
$z_u(x,y)=a_u+b_u^x x+b_u^y y$ and a lower bed plane $z_\ell(x,y)$ has its eight
corners on those two planes, so its local height varies across the footprint and
no corner pierces a parting.

### 3.3.2 Flat versus oblique guillotine

Two cut plans are compared. The **flat** plan places horizontal full-span cuts at
the dip-safe envelope: the top cut at the *deepest* point of the upper bed, the
bottom at the *shallowest* point of the lower bed, minus the keep-out. The wedges
between the flat cut and the dipping bed are waste. This is fabricable on any
gangsaw today. The **oblique** plan tilts each bed-parallel pass to follow the
dipping bed, recovering the wedge.

![Flat (orthogonal) guillotine baseline, the dip wedges are waste](../../../examples/08_gpr_marble/08f_flat_guillotine.png)

The dip-safe flat layer thicknesses collapse the nominal bed spacing
$[0.72,1.38,1.59,0.30]$ m down to $[0.328,1.051,1.037,0.161]$ m
(`flat_guillotine.dip_safe_layers_m`), losing the wedge volume. The measured
frontier (oblique vs flat, at max-cost):

| Plan | Volume | NET |
|---|---|---|
| Oblique (bed-following) | 32.16 m3 | $28,741 |
| Flat (orthogonal) | 20.26 m3 | $17,454 |

![Oblique max-cost block layout](../../../examples/08_gpr_marble/08c_maxcost.png)

The **georeferencing prize** is the gap: oblique recovers +11.90 m3 (+59%) and
+$11,287 net per ~50 m3 bench, entirely because the beds dip ~6 deg. That delta is
the business case for the scanning + georeferenced-marking last mile required to
execute the sloped passes (`georeferencing_prize_at_max_cost`).

> **Originality.** The bed-bounded hexahedra fix and the flat/oblique frontier
> are study-level constructions in the example generator, not a Core algorithm
> class; they are a **facade-over-primitives** result (bed-plane fitting plus the
> packing objective composed in the example). The recovery, cost, and frontier
> numbers are measured, not gated by a Core unit test, so they are **REPORTED**
> per the honesty convention. Tier B (engineering study on real data).

---

## 3.4 The guillotine cut sequence (example 24)

A gangsaw or bridge saw can only make straight edge-to-edge passes. Guillotine
packing guarantees every placed block is reachable by such cuts, so the plan is
directly fabricable. Example 24 renders the literal saw passes that turn a
3.0 x 1.5 x 1.5 m quarry block into 20 dimension blocks of 0.5 m at 10 mm kerf
(grid $5\times2\times2$), in the real sequential order
(`24_guillotine_metrics.json`):

1. RIP perpendicular to X $\to$ 4 rip planes $\to$ slabs.
2. CROSS-Y per slab $\to$ 5 planes $\to$ columns.
3. CROSS-Z per column $\to$ 10 planes $\to$ the 20 finished blocks.

That is **19 guillotine cuts** for 20 blocks: each plane spans only its current
sub-region, which is the defining property of a guillotine cut.

![All guillotine cut planes, accumulated, 3-stage rip and cross](../../../examples/24_guillotine_cut_sequence/24_stage3_crossZ_allcuts.png)

The same hierarchy resolves the balanced packing of example 25 into a saw plan:
two perp-Y cuts (3 slices), two perp-Z cuts (9 beams of 1 x 1 x 6 m), then 25
perp-X rip cuts within each beam, 29 passes total
(`25_cost_metrics.json`, `guillotine_cuts_balanced`). The oblique counterpart for
example 08 is 3 tilted bed-parallel passes plus 24 vertical rips, of which only
the 3 tilted passes need georeferenced marking
(`oblique_guillotine_cut_plan_balanced`).

> **Originality.** Guillotine staging: **facade-over-primitives** / Tier C. It is
> standard staged guillotine cutting (Gilmore and Gomory 1965 lineage); the
> contribution is the rendered, in-order, fabricable saw plan on real geometry,
> not a new algorithm.

---

## 3.5 The recovery cascade (multi-scale reject-recover)

`RecoveryCascade` is the repository's named evolved-fork increment over single-
scale BlockCutOpt. At each scale (coarse $\to$ fine) the solver runs on the tested
region; non-intersected blocks are *recovered*, and every block a fracture crosses
is fed back into the **same** engine at the next finer scale, cutting *around* the
fracture, until the remnant falls below the smallest marketable size
(`RecoveryCascade.cs:9-37`). The value recovered from a region $R$ at scale $s$
obeys the recursion (`RecoveryCascade.cs:26-29`):

$$
W(R,s)=\!\!\sum_{b\in\mathrm{kept}(R,s)}\!\!\mathrm{RMV}_s(b)
\;+\!\!\sum_{b\in\mathrm{cracked}(R,s)}\!\!
\begin{cases}
W\!\left(\mathrm{AABB}(b),\,s{+}1\right) & s{+}1<S,\ \mathrm{Vol}(b)\ge V_{\min},\\
\mathrm{residual}(b) & \text{otherwise,}
\end{cases}
$$

where kept/cracked partition the winning grid by the same SAT predicate
`!bvh.AnyTriangleIntersects` used in the single-scale solver
(`RecoveryCascade.cs:91-119`). The recursion depth is capped at the number of
scales (`RecoveryCascade.cs:74`), so it terminates. Crucially, with a single
`ScaleSpec` the cascade recovers exactly the blocks `BlockCutOptSolver.Solve`
finds at the same winning pose, so it is a faithful **superset** that reduces to
BlockCutOpt 2020 (`RecoveryCascade.cs:21-24`). This reject-coarse / recover-fine
structure is grounded in conditional two-scale cutting (Yarahmadi et al. 2018),
usable-leftover thresholds (Cherri et al. 2009), and staged guillotine cut-up
(Gilmore and Gomory 1965), with the unified 3D recursive form introduced in the
companion paper (`RecoveryCascade.cs:31-36`).

> **Originality.** `RecoveryCascade`: **evolved-fork** / **A-candidate** for the
> unified 3D recursive formulation. **Licensing/honesty flag (E9):** the header
> previously self-described "novel"; the audit ruling is to soften this with the
> BoEGE cite, which the current header does (Murugean 2026,
> DOI 10.5281/zenodo.20608279). The class is Core-validated (six headless tests
> per memory) but has **no GH consumer**; the shipped `FractureBlockPack`
> component ships a self-contained recovery engine that calls none of
> `RecoveryCascade` / `BlockCutOptSolver` / `Dlbf3dMixedSizePacker`
> (`FractureBlockPackComponent.cs:9-25`), a silent-disagreement risk whose
> resolution is facade-not-fork.

---

## 3.6 In-block secondary cutting (AMRR, I9/I12)

Improvement I9 couples the quarry-scale solver to in-block secondary cutting via
the Shao et al. (2022) AMRR (Average Material Removal Rate) plane-sequence
strategy (`AmrrPlanner.cs:7-31`). The planner iteratively cuts a blank convex
polyhedron by planes tangent to a target shape (a bounding sphere in v1): find the
vertex $P_v$ farthest outside the target, cut by the plane through the tangent
point with normal from centre to $P_v$, record the step, and repeat until the
outside volume falls below a convergence fraction (`AmrrPlanner.cs:132-178`). The
objective maximised is the average removal rate

$$
\mathrm{AMRR}=\frac{\sum_i V_{r,i}}{\sum_i \tau_i},
$$

removed volume over cutting time (`AmrrPlanResult.Amrr`, `AmrrPlanner.cs:85`),
where the per-cut path length feeding $\tau_i$ comes from the Minetto et al.
(2017) shared-edge mesh section (improvement I12, `SharedEdgeSlicer`, used at
`AmrrPlanner.cs:171-177`). The GH front end is `BlockCutOpt AMRR Plan`
(GUID `F2D0BC03-...`, `[Algorithm("AMRR in-block plane-sequence cutting",
"Shao, Liu, Gao 2022 ...")]`, `BlockCutOptComponents.cs:215`).

> **Originality.** `AmrrPlanner`: **clean-room** faithful implementation of
> Shao et al. (2022); `SharedEdgeSlicer` likewise of Minetto et al. (2017).
> Tier B.

---

## 3.7 Saw-bed scheduling and extraction order (Quarry tab)

Two scheduling helpers round out the tab. The **Saw Bed Schedule** component
assigns recovered blocks to gangsaw beds by greedy LPT (Longest Processing Time)
list scheduling (Graham 1969, `[Algorithm("Greedy LPT list scheduling",
"Graham 1969 ...", Doi 10.1137/0117039)]`,
`QuarryCutOptComponents.cs:322`). The **Extraction Order Optimizer** sorts blocks
by a weighted-sum greedy rule with a min-yield skip threshold; its `[Algorithm]`
attribute honestly records "Frahan-original ... no published scheduling algorithm
matched" (`QuarryCutOptComponents.cs:223`).

> **Originality.** Saw Bed Schedule: **clean-room** Graham 1969, Tier C.
> Extraction Order: **original-research / A-candidate** (self-declared, prior-art
> sweep pending). Evidence as cited.

---

## 3.8 Heterogeneous facade

`HeteroExt` (`Heterogeneous quarry extraction pipeline`,
`BlockCutOptHeterogeneousComponents.cs:169`) is the correct facade pattern: it
composes `BlockCutOptSolver` plus the Chehrazad et al. (2025) Deepest-Left-Bottom-
Fill 3D mixed-size packer (improvement I7, `[Algorithm("Deepest-Left-Bottom-Fill
(3D)", "Chehrazad, Roose, Wauters 2025 ...", Doi 10.1080/00207543.2025.2478434)]`,
`BlockCutOptHeterogeneousComponents.cs:42`). Per the HITL ruling its note credits
"interpretation and reimplementation of Elkarmoty 2020 and Chehrazad 2025 ... the
composition and the heterogeneity model are the contribution"
(`BlockCutOptHeterogeneousComponents.cs:169`).

> **Originality.** `HeteroExt`: **facade-over-primitives**. Tier D for the
> wrapper; the wrapped DLBF is clean-room Chehrazad 2025 (Tier B).

---

## 3.9 Status and what's left

- **RBE-style correctness is out of scope here** but the same audit gates the
  quarry tab's honesty conventions. No `BlockCutOpt` sign-bug is present; the
  parallel argmax is bit-identical to serial (validated).
- **`RecoveryCascade` has no GH consumer** (blocker for shipping the cascade on
  canvas). `FractureBlockPack` duplicates a recovery engine instead of calling
  the validated Core cascade; resolution is facade-not-fork.
- **Coarse-to-fine in the Omni solver is a stub bridge.** `UseCoarseToFine`
  currently runs the uniform-grid Pareto sweep at the fine step on both branches
  (`BlockCutOptOmniSolver.cs:150-169`), the worst-case wall clock; the true
  coarse-to-fine Pareto sweep is a follow-up.
- **Kerf volume is a film approximation** ($A_{xy}k/2$,
  `BlockCutOptSolver.cs:261-268`), refined alongside sub-division but not exact.
- **Two improvements unshipped:** I13 (multi-model joint generator, Tian 2025) is
  proposed only; I14 (composite multi-convex block, Zhang et al. 2024) is partial
  (`README.md:197-200`).
- **Bed-bounded hexahedra and the flat/oblique frontier are example-level**, not
  a Core class; numbers are REPORTED (measured), not gated by a unit test.
- **Data licence:** example 08 marble GPR data is CC-BY-NC-ND (research/testing
  only, not commercial product demos) (`08_marble_cost_volume_metrics.json`).

---

### References (this chapter)

Akenine-Moller, T. (2001). Fast 3D Triangle-Box Overlap Testing. *Journal of
Graphics Tools* 6(1):29-33.

Cherri, A.C., Arenales, M.N., Yanasse, H.H. (2009). The one-dimensional cutting
stock problem with usable leftover. *European Journal of Operational Research*
196(3):897-908. DOI 10.1016/j.ejor.2008.04.039.

Chehrazad, R., Roose, D., Wauters, T. (2025). A fast and scalable
deepest-left-bottom-fill algorithm. *International Journal of Production Research*
63:6606-6629. DOI 10.1080/00207543.2025.2478434.

Elkarmoty, M., Bondua, S., Bruno, R. (2020). A 3D brute-force algorithm for the
optimum cutting pattern of dimension stone quarries. *Resources Policy*
68:101761. DOI 10.1016/j.resourpol.2020.101761.

Gilmore, P.C., Gomory, R.E. (1965). Multistage cutting stock problems of two and
more dimensions. *Operations Research* 13(1):94-120. DOI 10.1287/opre.13.1.94.

Goodman, R.E., Shi, G.-h. (1985). *Block Theory and Its Application to Rock
Engineering.* Prentice-Hall.

Graham, R.L. (1969). Bounds on multiprocessing timing anomalies. *SIAM Journal on
Applied Mathematics* 17(2):416-429. DOI 10.1137/0117039.

Jalalian, M., Bagherpour, R., Khoshouei, M. (2023). Environmentally sustainable
mining in quarries to reduce waste production and loss of resources using the
developed optimization algorithm. *Scientific Reports* 13.
DOI 10.1038/s41598-023-49633-w.

Minetto, R., Volpato, N., Stolfi, J., Gregori, R.M.M.H., da Silva, M.V.G. (2017).
An optimal algorithm for 3D triangle mesh slicing. *Computer-Aided Design*
92:1-10. DOI 10.1016/j.cad.2017.07.001.

Murugean, L. (2026). GPR-to-block-yield optimization for fractured dimension-stone
quarries (submitted, *Bulletin of Engineering Geology and the Environment*;
reproducibility deposit). DOI 10.5281/zenodo.20608279.

Shao, H., Liu, Q., Gao, Z. (2022). Material Removal Optimization Strategy of 3D
Block Cutting Based on Geometric Computation Method. *Processes* 10(4):695.
DOI 10.3390/pr10040695.

Yarahmadi, R., Bagherpour, R., Sousa, L.M.O. (2018). Discontinuity modelling and
rock block geometry identification to optimize production in dimension stone
quarries. *Engineering Geology* 232:22-33. DOI 10.1016/j.enggeo.2017.11.006.

Zhang, N., Zheng, H., Yang, M., Wang, N. (2024). An open-source MATLAB toolbox for
3D block cutting and 3D mesh cutting in geotechnical engineering. *Advances in
Engineering Software* 197:103762. DOI 10.1016/j.advengsoft.2024.103762.
