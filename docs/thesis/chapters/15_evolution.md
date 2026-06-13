# 15. Evolution: From Baselines to the Current System

This chapter is cross-cutting. The preceding chapters describe each subsystem
as it stands; this one narrates how it got there. The repository is not a fresh
build. It is a sequence of measured deltas over named baselines, each baseline a
shipping implementation rather than a paper abstraction, each delta benchmarked
on the same instances before and after. The discipline is stated in the
research-workflow rules: the baseline is the current Frahan implementation, the
data is real (ETH1100 dry stone, Botticino GPR, Stanford scans), the metric is
mesh-accurate rather than bounding-box, and the truth criterion is live visual
validation, not a self-reported number (`outputs/2026-06-03/RESEARCH_MATH_WORKFLOW_V2.md`).

The originality scheme of chapter 90 governs the verdicts here: a thing is
called `clean-room`, `evolved-fork`, `facade-over-primitives`, `direct-port`,
`vendored-library`, `original-research`, or `wrapper-of-native`, with
file-and-line or commit evidence and a licensing flag where one applies. The
OpenNest-lineage nester used in the 2D benchmarks is named **the reference
physics nester**; no competitor source sits in this tree. Every number below
traces to a committed benchmark output or an `[Algorithm]` attribute.

The evolution has a shape. Six threads each moved a baseline forward: the 2D
nester walked from an overlap-tolerant placement rule to a hole-aware exact
solver; the 3D packer moved from a heightmap proxy to mesh-accurate collision
with physics settling; the quarry block-cutter gained a full-3D pose and three
new Pareto axes; the masonry stack grew from a force-only check to a coupled
kinematic certificate and an executable imposition metric; the GPR layer added
crack-aware recovery and wire-saw-separable yield; and the surface packers and
the BFF runtime were rebuilt onto the hardened nesting engine and a single static
binary. The recurring lever across all of them is the shared numeric-hygiene
finding from the ROSES synthesis: recenter before computing, use a
scale-relative epsilon, and route booleans through a robust kernel
(`ROSES_top10_fabrication_synthesis.md`, section 6).

---

## 15.1 Two-dimensional nesting: V506 to NFP-BLF to HoleNest to CNH

This is the longest and best-instrumented thread, and it sets the pattern for
the chapter.

### 15.1.1 The baseline and its defect

The originally shipped nester is `IrregularSheetFillV506` (FreeNest, GUID
`D5E7A2B1-...`). It is an NFP-assisted bottom-left placement that, by design,
permits a bounded overlap controlled by a **Trim Tolerance** and then
boolean-trims the contact. At the default Trim Tolerance of 0.1 it produces
visible overlaps; this is documented behaviour, not a fault (KB-6/KB-7;
`examples/10_pack2d/README.md`). The defect for fabrication is precisely this:
"packed" can mean "overlapping by up to the trim tolerance," which is not a
cut-ready layout.

### 15.1.2 First delta: the complete feasible region (V506 to FreeNestX)

`IrregularSheetFillNfpBlf` (FreeNestX, GUID `2d351646-...`) re-derives the
placement so that the feasible set is the **complete** inner-fit-minus-no-fit
region, making zero overlap a hard constraint of construction rather than a
post-hoc trim. For a part $B$ at fixed rotation on sheet $S$ with holes $\{H_l\}$
and placed parts $\{A_k\}$,

$$
\mathrm{feasible}(B)=\mathrm{IFP}(B,S)\ \setminus\ \Bigl(\bigcup_k \mathrm{NFP}(A_k,B)\Bigr)\ \setminus\ \Bigl(\bigcup_l \mathrm{NFP}(H_l,B)\Bigr),
$$

and the placement is the lexicographically lowest-then-leftmost vertex of that
region (Burke et al. 2006; Bennell and Oliveira 2009). The measured improvement
is recorded in source: a **mean wasted-area cut of 53.9% against V506 at zero
overlap**, validated against a Python reference
(`IrregularSheetFillNfpBlf.cs:21-22`;
`outputs/2026-06-03/pack2d_nfp_evolution/CHECKPOINT_4.md`). FreeNestX is the only
zero-overlap packer in the study to cross 80% utilisation with holes (82.0% to
89.6% on the hole fixtures, `examples/10_pack2d/README.md`).

> **Originality.** FreeNestX is an **evolved-fork** of V506: same NFP/BLF
> lineage, but a re-derived complete feasible region replaces the
> overlap-then-trim contract, with the 53.9% waste-cut measured delta
> (`IrregularSheetFillNfpBlf.cs:18-27`; commit `46ae0d2`).

### 15.1.3 Second delta: holes, a native kernel, and a depth certificate (CNH)

`ContactNfpHoleNester` (HoleNest, GUID `D5F10019-...`) evolves FreeNestX with
capabilities the fork lacks, and moves the engine out of the Grasshopper project
into the Rhino-free `Frahan.StonePack.Core`. The before/after of the engine
properties is the clearest single table in the thread.

| Property | V506 (FreeNest) | FreeNestX | CNH / HoleNest |
|---|---|---|---|
| Overlap guarantee | bounded, trimmed | zero by construction | zero, depth-certified always-on |
| Sheet holes | no | yes (no-fit only) | yes + part-in-part-hole nesting |
| Rotations | fixed list | fixed list | base + contact-adaptive edge alignment |
| Rect instances | general path | general path | exact integer shelf fast-path |
| Multi-sheet | no | no | greedy overflow |
| Rhino-free | no | no | **yes (CI-benchmarkable)** |
| Kernel | managed Clipper2 | managed Clipper2 | + native batched `nfp_kernel.dll` |

The three research increments are the contact-adaptive rotation set (augment the
uniform base $\{0,\tfrac{\pi}{2},\pi,\tfrac{3\pi}{2}\}$ with edge-alignment angles
$\theta=\alpha_h-\alpha_p$ and its flip $\theta+\pi$), the part-in-part-hole IFP
nesting (a filler's legal translations into a host hole $G$ are exactly
$\mathrm{IFP}(B,G)=\bigcap_{v\in\operatorname{hull}(B)}(G-v)$), and the
distance-based penetration certificate with a single micro-retreat along the
measured penetration vector. These are derived in full in chapter 1; the point
here is the arc, not the algebra. The native kernel wraps `nfp_kernel.dll`, which
vendors official Clipper2 (BSL-1.0) unmodified, giving a measured **8x** on the
batched-NFP path with only the marshalling owned in-repo (`NativeNfpKernel.cs:10-22`;
commit `ec8a060`).

### 15.1.4 Third delta: the rect fast-path and the keep-best fix

CNH v2 added a rectangle shelf fast-path: when every loop is an axis-aligned
rectangle, the NFP and IFP degenerate and the solve reduces to interval
arithmetic with no Clipper calls, using the exact integer rotation map
$(x,y)\mapsto(-y,x)$ so there is no trigonometric round-off
(`ContactNfpHoleNester.cs:635-738`). A completeness fallback discards the
fast-path result and runs the general engine if the sparser candidate set strands
any part (about 1 in 4000 in fuzzing). Multi-start keep-best runs $K$
deterministic part orders and keeps the densest **valid** layout. This last lever
carried a real bug worth recording because it shows the discipline at work: the
first multi-start landing computed `candValid` but never wrote `cand.Valid`, so
the keep-best comparison always saw `false` and degraded to "keep the last order
tried," which regressed the 60-irregular instance from 47 to 46 placed. The
verify pass root-caused it (not the NFP cache, which is structurally fresh per
pass, but the selection write), and the fix tracks a `bestValid` local. Post-fix,
$\mathrm{placed}(K{=}4)\ge\mathrm{placed}(K{=}1)$ on all twelve tight instances
and the densest 90-irregular result of any engine in the study (0.735) is
HoleNest multi-start (`outputs/2026-06-13/twod_decision/THREE_WAY_HEAD_TO_HEAD.md`;
commit `f4657e4`).

### 15.1.5 The honest head-to-head

The 2026-06-13 three-way study put vanilla OpenNest (the reference physics
nester, built from committed HEAD), its evolved hole/ThetaWise fork, and HoleNest
through the same instances, with an **independent shapely checker** scoring every
output so no engine's self-report is trusted.

| Lane | Reference physics nester | HoleNest | Reading |
|---|---|---|---|
| Irregular density (60/90 parts) | 0.751 / 0.710 | 0.730 / 0.711 | within ~3%, a wash |
| Rectangles (validity + speed) | 1 overlap on 20-rect; 0.5-1 s | 0 overlap; 5-54 ms | HoleNest 15-100x, always valid |
| Outline strip density (Sparrow) | wins by 6-10% | loses | honest boundary, unchanged |
| Determinism | stochastic relaxation | deterministic | reproducible cut layouts |

The honesty boundary is held in source: on pure outline strip packing the
reference physics nester still wins density by 6 to 10 percent, and no universal
"2x better" claim is made there. HoleNest's win is the hole-aware lane (where the
reference is invalid because it ignores holes), the rectangle lane (speed and
validity), and the determinism property. On the true-hole lane CNH is
**54x faster and valid** where Sparrow is invalid, and the rect fast-path is
about 22,000x faster (`HOLE_PACKER_MATH_AND_BENCHMARK.md`, sections 2-3).

### 15.1.6 The consolidation ruling

The thread ends in an architecture decision rather than another algorithm. A
2026-06-13 code-and-math-and-performance comparison found that as exposed in the
shipped components, HoleNest is a strict superset of FreeNestX: FreeNestX's
component calls only the 7-argument legacy constructor that forces the
concave-overlap verify off (KB-4), so its advanced math (multi-start, compaction,
GLS reinsertion) is dormant dead code unreachable from the canvas. The ruling is
fold-then-hide: port FreeNestX's eight unique UI features into HoleNest first, so
the fold is honest, then mark FreeNestX `[Obsolete]` and `GH_Exposure.hidden`
with its GUID preserved. The older V1/V2/V3/V506 standalones are already hidden
(`outputs/2026-06-13/twod_decision/FREENESTX_VS_HOLENEST_DECISION.md`,
`TWOD_PACKER_ARCHITECTURE_DECISION.md`). This is the 2D-V-solver phase-out the
collaborator-readiness plan mandated (`EVOLUTION_PLAN_COLLAB_READY.md`, HITL
ruling row 2).

![2D nest result: parts nested around a sheet hole, zero overlap](../../../examples/10_pack2d/10_pack2d_result.png)

---

## 15.2 Three-dimensional packing: heightmap proxy to mesh-accurate plus settle

The shipped 3D baseline is `Pack3DIrregularContainerComponent` (GUID
`B3E8A42F-...`) over `GreedyMeshHeightmapPacker`: a greedy deepest-bottom-left
placement on a per-cell vertical-interval ("heightmap column") collision of
mesh-derived footprints, scoring height growth against a back-left compactness
bias (Park and Han 2024; `Pack3DIrregularContainerComponent.cs:18`). Its defect,
stated in the plan, is that it packs footprint **proxies**, not true mesh
geometry: parts stack on columns rather than nesting into concavities, there is no
settling, and the reported fill is bounding-box based, so "packed" overstates true
density and says nothing about whether the pile is stable
(`outputs/2026-06-03/pack3d_evolution/PLAN.md`, section 1).

The evolution replaced the proxy with two stages. The constructive seed voxelises
each real ETH1100 stone and places it by deepest-bottom-left over true-3D FFT
feasibility (~33 to 37% compactness). Then a Bullet rigid-body settle drops the
seeded poses under a gravity ramp with Coulomb friction and convex-decomposition
(CoACD) collision, closing the voxel-quantisation gaps into real contact. The
measured arc on real stones, with the honest mesh-volume compactness metric, is:

| Stage | N | placed | compactness | interpenetration | COM-stable |
|---|---|---|---|---|---|
| heightmap baseline (B3E8A42F) | 30 | 30 | 33.8% | ~0 | 57% |
| constructive seed | 30 | 30 | 32.6% | ~0 | 67% |
| **+ physics settle** | 30 | 30 | **35.7%** | ~0 | all at rest |
| heightmap baseline | 60 | 32 | 33.x% | ~0 | - |
| constructive seed | 60 | 32 | 36.9% | ~0 | 78% |
| **+ physics settle** | 60 | 32 | **38.7%** | ~0 | all at rest |

The honest finding is recorded against the original "beat it 2x" directive: the
physics settle improves on the constructive seed (32.6 to 35.7%, 36.9 to 38.7%)
and on the shipping baseline (33.8%), but by ~1.05 to 1.15x, **not** 2x, because
greedy irregular-stone packing plateaus around 33 to 39% and a literal 66% sits
above the physical ceiling without active void-insertion and a snug container
(`outputs/2026-06-03/pack3d_evolution/CHECKPOINT_4.md`). The win the settle does
deliver is qualitative as much as quantitative: a physically valid pile, every
stone at rest, zero interpenetration, which is the "complete packing, not bounding
boxes" deliverable the proxy packer could not produce.

The backend decision was deliberate and is itself evolution evidence. Bullet (via
`BulletSharp.x64` 0.12.0, zlib, double precision) was chosen over Kangaroo 2
because Kangaroo's position-based rigid collision has no friction and its author
states it "isn't suitable for stacking"; Kangaroo is kept only for the 2D
Trencadis soft-settle (`BULLET_PHYSICS_BACKEND.md`). The dev pybullet harness and
the shipped `BulletSettleService` wrap the same engine, so the dev-proven numbers
transfer. The result is `Settle 3D (Physics)` (GUID `134785ac-...`), labelled as
the evolved volume packer while the heightmap components remain the validated
baseline, exactly the "keep one highly evolved 3D packer, supersession-document
the rest" ruling (`PackSettle3DComponent.cs:29-46`; `EVOLUTION_PLAN_COLLAB_READY.md`).

> **Originality.** The mesh-accurate seed plus drop-settle is an **evolved-fork**
> of the heightmap baseline with a measured (modest, honest) compactness delta and
> a new stability guarantee. The Bullet settle credits Zhuang et al. (2024) for
> dynamics-based packing of irregular 3D objects, Bullet (Coumans) via BulletSharp,
> CoACD (Wei et al. 2022) for convex pieces, and Heyman (1966) for the
> COM-over-support gate (`PackSettle3DComponent.cs:29-35`). The settle service and
> CoACD path are **wrapper-of-native** / **vendored-library** at the engine
> boundary; the seed-and-compose orchestration is ours.

![Bullet-settled pile of real ETH1100 stones](../../../examples/18_pack_settle_bullet/18_settle_bullet.png)

---

## 15.3 Quarry block-cutting: BlockCutOpt 2020 to v2

The quarry thread takes a published serial algorithm and adds dimensions to it.
The baseline is Elkarmoty, Bondua and Bruno's (2020) BlockCutOpt: a brute-force
pose sweep that maximises the number of intact blocks a cutting lattice extracts
from a fractured deposit, with the lattice rotated only by yaw $\psi$ about the
vertical. The repository's faithful transcription is `BlockCutOptSolver` (a
clean-room pose-grid argmax, bit-identical to a serial reference,
`BlockCutOptSolver.cs:108-135`).

The v2 increments are catalogued by improvement number in chapter 3 and
summarised here as a before/after:

| Axis | BlockCutOpt 2020 | Frahan v2 | Evidence |
|---|---|---|---|
| Lattice pose | yaw $\psi$ only | full 3D $R=R_z(\psi)R_x(\theta)R_y(\phi)$ (I1) | `CuttingGrid.cs:84-110` |
| Intersection test | full mesh scan | OBB-triangle 13-axis SAT + BVH prune (I2/I4) | `ObbTriangleIntersection.cs:10-16` |
| Objective | block count | four-axis Pareto: count, kerf, yield, value (I6/I11) | `BlockCutOptParetoSolver.cs:82-95` |
| Cost model | none | BCSdbBV cutting-surface/value (Jalalian 2023) | `BlockValueModel.cs:54-58` |
| Cut sequence | none | AMRR plane-sequence planner (I9/I12) | `AmrrPlanner.cs:7-31` |

The pose generalisation is exact and back-compatible: setting $\theta=\phi=0$
collapses the rotated basis $U,V,W$ to $(\cos\psi,\sin\psi,0)$,
$(-\sin\psi,\cos\psi,0)$, $(0,0,1)$, recovering the BlockCutOpt-2020 $\psi$-only
behaviour bit-for-bit through a back-compat constructor. The new objective adds
Jalalian et al.'s (2023) BCSdbBV cutting-surface-area-over-value cost as a fourth
Pareto axis, so the solver trades block count against waste and value rather than
maximising count alone.

> **Originality.** The pose-sweep core is **clean-room** from Elkarmoty et al.
> (2020); the full-3D pose (I1), the four-axis Pareto/Omni solvers (I6/I11), and
> the multi-scale `RecoveryCascade` are **evolved-fork** increments, each with an
> `[Algorithm]` attribute naming the Frahan improvement over the named baseline
> (`BlockCutOptComponents.cs:98`, `:306-307`). The `RecoveryCascade` header was a
> flagged "novel" overclaim (E9) and has been softened to cite the BoEGE paper
> lineage; its lack of a GH consumer is an open roadmap item.

---

## 15.4 Masonry: RBE to CRA-coupled, plus Lambda and the generator

The masonry stack is the deepest evolution, executed as a planned sequence P0
through P7 (`outputs/2026-06-10/masonry_evolution/EVOLUTION_PLAN_MASONRY.md`).

### 15.4.1 Force-only to coupled-kinematic

The baseline verifier is Rigid-Block Equilibrium: a convex QP over contact forces
balancing self-weight, with Coulomb friction linearised to a polyhedral pyramid.
Force-only equilibrium has a known failure: Kao's H-model (a beam bridging two
columns, touching only on vertical faces with nothing underneath) is accepted by
RBE because a self-equilibrated horizontal friction squeeze appears to carry it,
even though nothing can produce that squeeze. The evolution (P2) couples statics
with virtual rigid-body kinematics so that a contact carries normal force only
where a consistent virtual motion lets it engage (Kao et al. 2022, Eqs. 8-14).
The repository does not ship IPOPT; it implements an **alternating convex
certificate** that is sound in the certifying direction, alternating a penalty RBE
force solve with a convex kinematic-certificate QP and a complementarity-driven
restriction step (`CraStabilityChecker.cs:31-49`). The H-model is now a
first-class regression: RBE accepts, CRA rejects
(`CraStabilityCheckerTests.cs:83-105`), and a `compas_cra` cross-fixture parity
suite pins agreement on shared cubes, stacks and an arch (commit `64ae069`,
parity 5/5).

A correctness fix in the same thread is the friction linearisation direction. A
circumscribed $K=4$ pyramid over-estimates the cone by up to
$1/\cos(\pi/4)=\sqrt2\approx1.41$, certifying unstable walls as stable. The fix
inscribes the pyramid by shrinking the coefficient to
$\mu_{\text{eff}}=\mu\cos(\pi/K)$, a conservative under-approximation that the CRA
checker now passes by default (`FrictionConeBuilder.cs:105-130`;
`CraStabilityChecker.cs:90`). For a stability verdict this is the
highest-consequence correction in the whole repository, because an optimistic
"stable" is the most dangerous failure mode (ROSES synthesis section 3).

### 15.4.2 Performance: dense Dykstra to sparse ADMM to exact-joint coupling

The solver path was rebuilt for scale (P1.1). The dense managed QP was replaced
with a CSR-sparse OSQP-style ADMM (Stellato et al. 2020) with full Ruiz
equilibration and per-row $\rho$ to tame the mixed newton/metre/penalty scales
(`AdmmQpSolver.cs:108-145`). Separately, P1.2 stopped re-detecting contacts from
triangle meshes: a mesh contact detector splinters 40 stones into ~125
sub-interfaces and ~612 contact vertices, ill-conditioning the QP. The exact-joint
assembler emits one planar-quad interface per adjacent stone pair directly from
the shared generator edge, a **27x total speedup** and the reason CRA certifies
generated walls (commit `a843027`; `Cra_GeneratedWall_Certified`).

### 15.4.3 The new metrics: Lambda, the J interlock score, and the building

P3 evolved the wall generator (power diagram, Lloyd, coursing morph, sliver cull,
and an original interlock score $J$ penalising aligned running joints and "+"
junctions). P4 made the top-down/bottom-up balance executable as the imposition
metric

$$
\Lambda=\frac{\sum_i \lambda_i\,\mathrm{vol}(\text{stone}_i)}{\sum_i \mathrm{vol}(\text{stone}_i)},\qquad \lambda_i=\frac{\mathrm{vol}(\text{stone}_i\setminus\text{cell}_i)}{\mathrm{vol}(\text{stone}_i)},
$$

with $\Lambda\approx1$ full imposition (sawn ashlar) and $\Lambda\approx0$ pure
negotiation (stones as found). The Cyclopean datum is Clifford and McGee's (2018)
$\Lambda\approx0.27$; the repository reports $\Lambda=0.194$ on ETH1100, better
than the datum (`StoneCellAssignmentEthBenchmarkTests.cs:14`). P5 upgraded the
rubble drop-settle objective (Furrer support/COM plus Johns under-void candidate
ranking), P6 added the xBIM IFC terminal, and P7 composed it all into a
multi-container IFC castle (one `.ifc`, 8 containers, 123 parts, each
CRA-certified; commit `6e684b5`). Lambda, $J$, and the CRA instability field are
the three numbers every masonry example reports from P4 onward.

> **Originality.** RBE and the ADMM solver are **clean-room** from cited papers;
> the CRA soundness certificate, the $J$ interlock metric, and the Lambda
> formalisation are **original-research** (A-candidate, prior-art sweep pending per
> AGENTS.md section 9); the exact-joint assembler is **clean-room** geometry; the
> carve-back is **facade-over-primitives** over the CGAL boolean kernel. One
> citation flag remains open: `BestFitPackComponent.cs:30` attributes a likely
> fabricated 2017 NCCR paper whose real lineage is Furrer (2017) / Johns (2020)
> (E5).

![Generated polygonal wall, three-band layout](../../../examples/27_polygonal_masonry/27_01_three_band_wall.png)

![Stone-to-cell match with Lambda readout](../../../examples/27_polygonal_masonry/27_07_stone_match_lambda.png)

---

## 15.5 GPR additions: RecoveryCascade and the staged guillotine packer

The GPR thread is additive rather than a replacement: it gave the quarry packers
fracture awareness. `RecoveryCascade` is a multi-scale reject-recover packer that
partitions a candidate block into kept and cracked regions by whether any DFN
triangle intersects it, recurses at a finer scale on the cracked remainder, and
reduces exactly to the `BlockCutOpt` baseline at a single scale
(`RecoveryCascade.cs:26-29`, `:91-119`). `FractureBlockPack` is the
uncertainty-safe yield engine of example 09: a wire-saw **staged guillotine**
packer whose blocks are separable by full-width saw passes, trading raw density
for fabricability. The measured trade is recorded: mode-5 wire-saw-separable yield
49.3% at 100% separability versus a voxel-DLBF 53.3% at 0% separability, costed by
Jalalian's I11 saw objective (memory `project_gpr_fracture_capability`). Both are
folded into the same BoEGE master paper rather than a separate publication, and
the paper passed a five-reviewer re-review at accept.

> **Originality.** `RecoveryCascade` is **evolved-fork** (multi-scale recursion
> over BlockCutOpt, reduces to baseline at $S=1$); `FractureBlockPack` is
> **facade-over-primitives**, a self-contained recovery engine. A flagged risk is
> that `FractureBlockPack` does **not** call `RecoveryCascade` or
> `BlockCutOptSolver`, so a silent-disagreement between the two recovery paths is
> possible and is an open roadmap item.

---

## 15.6 Surface packers and BFF: onto the hardened engine and a single static exe

Two infrastructure evolutions land last. First, both surface packers (`Pack
Surfaces`, `Pack On Surface`) were moved off `IrregularSheetFillV506` onto the
deterministic HoleNest engine (`ContactNfpHoleNester.PackSheets`: exact NFP-BLF,
multi-start, zero overlap) and adopted HoleNest's self-trigger background pattern
(previous result stays visible, live progress, stable bbox loop-guard hash) in
place of `GH_TaskCapableComponent`. The async lineage itself evolved: Sheet Nest
went async, was reverted to synchronous to kill an endless re-solve loop
(commit `0a56dfb`), then restored as a self-trigger flag with a stable bbox hash
that progresses without starving or looping (`7d46ec8`). Back-compatibility was
held exactly: GUIDs unchanged, outputs unchanged, the four V506-only controls made
inert and documented (commit `05c7e82`).

Second, the Boundary First Flattening runtime (Sawhney and Crane 2017, used by the
Surface Chart) was recompiled from the GeometryCollective source as a **fully
static single executable** via MSYS2 mingw64 with static SuiteSparse and OpenBLAS
archives. External dependencies drop to KERNEL32 and msvcrt only (objdump
verified); the deploy shrinks from **~67 MB across an exe plus 17 DLLs to ~38 MB
in one self-contained exe** (`install/tools`: 18 files to 2). This also fixed a
latent break: the deployed `bff-command-line.exe` had shipped with none of its 17
DLLs, so BFF was non-functional from the `.gha` folder (exit 0, zero UVs). The
new exe needs no siblings, produces byte-identical UV flattening to the reference
build, and was validated end-to-end (dome to chart to 18/18 placed, valid, curves
exactly on surface; commit `d1b5c5b`).

> **Originality.** The surface packers are **facade-over-primitives** recomposed
> onto the HoleNest engine; the integration delta is real (deterministic,
> verified, zero-overlap nesting replacing an overlap-tolerant one). BFF remains a
> **vendored-library** (Sawhney and Crane 2017); the static-link rebuild is a
> packaging change, not an algorithm change, and still owes a `THIRD_PARTY_NOTICES`
> row per the licensing register.

![Surface chart segments, BFF-flattened and packed](../../../examples/13_surface_mapping/13_surface_segments.png)

---

## 15.7 Status and what is left

The evolution is real but incomplete, and the honest gaps are tracked.

- **2D fold not yet executed.** The fold-FreeNestX-into-HoleNest port (eight UI
  features, then hide) is decided but not landed; until it is, the plugin ships
  two NFP nesters and FreeNestX's shipped path still runs no concave-overlap
  verify (KB-4). *Medium.*
- **3D "2x" narrative.** The compactness gain over the heightmap baseline is
  ~1.05 to 1.15x, not the original 2x target; the ROSES roadmap keeps a
  mesh-accurate-port item open to fix the narrative and push past ~35% with active
  void-insertion. *Medium.*
- **CRA convergence at scale.** The ADMM degrades past ~50 interfaces; the
  LS-first certificate mitigates wall-scale checks but per-element verification
  remains the pattern for large mixed assemblies, and conditioning to 300
  interfaces is an open benchmark item. *High.*
- **GPR recovery-path divergence.** `FractureBlockPack` and `RecoveryCascade` are
  separate engines with no shared call path; a silent-disagreement test is owed.
  *High.*
- **Open citation and licensing flags.** The fabricated Best-Fit citation (E5),
  the "Phase correlator FFT" wording over a direct-correlation implementation, and
  the missing `THIRD_PARTY_NOTICES.md` for the BFF numeric stack must close before
  external review (AGENTS.md section 9; licensing register rows 10, 13). *Medium.*
- **Prior-art sweeps pending.** The $J$ interlock metric, the Lambda
  formalisation, the CRA certificate, and the projection bootstrap are all
  A-candidate; none may be asserted "novel" until the sweep completes. *Medium.*

The one-line reading: across six threads the repository moved from
overlap-tolerant, proxy-based, force-only, single-axis baselines to
zero-overlap-certified, mesh-accurate, kinematically-coupled, multi-axis
successors, every step measured against the shipping predecessor on real data,
with the modest-but-honest deltas reported as modest and the genuine wins
(validity, determinism, stability, fabricability) reported where they are real.

---

## References (this chapter)

- Burke, E.K., Hellier, R., Kendall, G., Whitwell, G. (2006). A new bottom-left-fill heuristic algorithm for the two-dimensional irregular packing problem. *Operations Research* 54(3):587-601. DOI 10.1287/opre.1060.0293.
- Bennell, J.A., Oliveira, J.F. (2009). A tutorial in irregular shape packing problems. *Journal of the Operational Research Society* 60(S1):S93-S105. DOI 10.1057/jors.2008.169.
- Park, J., Han, S. (2024). Tree-packing for irregular 3D containers (tree-search 3D bin packing / orthogonal-block packing).
- Chehrazad, R., Roose, D., Wauters, T. (2025). A fast and scalable deepest-left-bottom-fill algorithm for the 3D bin packing problem. *International Journal of Production Research* 63:6606-6629. DOI 10.1080/00207543.2025.2478434.
- Zhuang, Q., Chen, Z., He, K., Cao, J., Wang, W. (2024). Dynamics simulation-based packing of irregular 3D objects. *Computers and Graphics* 123:103996. DOI 10.1016/j.cag.2024.103996.
- Wei, X., Liu, M., Ling, Z., Su, H. (2022). Approximate convex decomposition for 3D meshes with collision-aware concavity and tree search (CoACD). *ACM Transactions on Graphics* 41(4):42. DOI 10.1145/3528223.3530103.
- Elkarmoty, M., Bondua, S., Bruno, R. (2020). Mechanized in-situ determination of joint-related and yield-related rock-mass parameters during dimension stone block extraction. *Resources Policy* 68:101761. DOI 10.1016/j.resourpol.2020.101761.
- Jalalian, M.H., Bagherpour, R., Khoshouei, M. (2023). Environmentally sustainable mining in quarries to reduce waste production and loss of resources using the developed optimization algorithm (BCSdbBV). *Scientific Reports* 13:22183. DOI 10.1038/s41598-023-49633-w.
- Kao, G.T.-C., Iannuzzo, A., Thomaszewski, B., Coros, S., Van Mele, T., Block, P. (2022). Coupled Rigid-Block Analysis: stability-aware design of complex discrete-element assemblies. *Computer-Aided Design* 146:103216. DOI 10.1016/j.cad.2022.103216.
- Stellato, B., Banjac, G., Goulart, P., Bemporad, A., Boyd, S. (2020). OSQP: an operator splitting solver for quadratic programs. *Mathematical Programming Computation* 12:637-672. DOI 10.1007/s12532-020-00179-2.
- Heyman, J. (1966). The stone skeleton. *International Journal of Solids and Structures* 2(2):249-279. DOI 10.1016/0020-7683(66)90018-7.
- Furrer, F., Wermelinger, M., Yoshida, H., Gramazio, F., Kohler, M., Siegwart, R., Hutter, M. (2017). Autonomous robotic stone stacking with online next best object target pose planning. IEEE ICRA. DOI 10.1109/ICRA.2017.7989273.
- Johns, R.L., Wermelinger, M., Mascaro, R., Jud, D., Gramazio, F., Kohler, M., Chli, M., Hutter, M. (2020). Autonomous dry stone. *Construction Robotics* 4:127-140. DOI 10.1007/s41693-020-00037-6.
- Clifford, B., McGee, W. (2018). Cyclopean Cannibalism: a method for recycling rubble. ACADIA 2018 (Disciplines and Disruption).
- Sawhney, R., Crane, K. (2017). Boundary First Flattening. *ACM Transactions on Graphics* 37(1):5. DOI 10.1145/3132705.
