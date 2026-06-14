# 05. Masonry Equilibrium & Cyclopean Reassembly (CRA)

The Masonry tab (ribbon `Frahan > Masonry`, 35 components) covers the
stone-on-stone end of the repository: turning a set of blocks into a wall, a
wall into a verified static structure, and an inventory of found stones into a
fitted, carved assembly. The subsystem has three layers. The lowest is a
managed reimplementation of Coupled Rigid-Block Analysis (CRA) and its
Rigid-Block Equilibrium (RBE) precursor, ported from the published
mathematics of Kao et al. (2022) and structured after the MIT BlockResearchGroup
`compas_cra` reference. Above it sit the convex solvers (a managed QP, an
OSQP-style ADMM, and a kinematic certificate search). On top of both sit the
generation and assignment layer: a polygonal-wall generator, an exact-joint
assembler, the imposition metric Lambda, and the Cyclopean carve-back.

Provenance in one line: the equilibrium and friction algebra is **clean-room**
from a cited paper; the CRA verdict is built from an **A-candidate** convex
certificate that is not in the upstream; the generator and Lambda metric are
**A-candidate original-research**; the rubble settle is clean-room from robotics
sources; and the assignment/carve-back are facade compositions of in-repo
primitives. Every claim below is anchored to `file:line`, an `[Algorithm]`
attribute, or a test.

The static-analysis truth criterion for this chapter is the battery: at the
last audit the suite reported 1034 PASS / 0 FAIL, including the CRA H-model
counterexample regression and a `compas_cra` cross-fixture parity set
(`tests/Frahan.StonePack.Tests/Program.cs:334`, `:347`-`:356`).

---

## 5.1 Rigid-block equilibrium (RBE)

### The equilibrium matrix

A masonry assembly is a set of rigid blocks meeting at planar interfaces. Each
interface carries a contact polygon whose vertices each host a contact force
resolved on a local frame: one normal $\mathbf{n}$ (block A into block B) and
two tangents $\mathbf{t}_1,\mathbf{t}_2$. For block $i$ with centre of mass
$\mathbf{c}_i$, static equilibrium under self-weight $\mathbf{W}_i$ is force and
moment balance summed over every incident interface and every contact vertex
$k$ at world position $\mathbf{r}_k$:

$$
\sum_k \left( f^n_k\,\mathbf{n} + f^{t_1}_k\,\mathbf{t}_1 + f^{t_2}_k\,\mathbf{t}_2 \right) + \mathbf{W}_i = \mathbf{0}
$$

$$
\sum_k (\mathbf{r}_k - \mathbf{c}_i) \times \left( f^n_k\,\mathbf{n} + f^{t_1}_k\,\mathbf{t}_1 + f^{t_2}_k\,\mathbf{t}_2 \right) + \mathbf{M}^{W}_i = \mathbf{0}
$$

Gravity acts at the centre of mass, so $\mathbf{M}^{W}_i = \mathbf{0}$. Stacking
the six rows (three force, three moment) for every free block, and one column
per contact-vertex force component, gives the linear system the code builds
verbatim (`EquilibriumMatrixBuilder.cs:13-30`):

$$
A_{eq}\,\tilde{f} + \mathbf{b} = \mathbf{0}
$$

The per-column contribution is a force triple and the cross-product moment
triple $(\mathbf{r}_k - \mathbf{c}_i) \times \mathbf{e}$ for each basis vector
$\mathbf{e}\in\{\mathbf{n},\mathbf{t}_1,\mathbf{t}_2\}$, written explicitly at
`EquilibriumMatrixBuilder.cs:201-219`. Sign convention follows
`compas_cra/equilibrium/cra_helper.py`: block A sees $+1$, block B sees $-1$
(`:113-121`). Fixed blocks contribute no rows (`:158-162`). The gravity load is
$b[F_z]=\rho V g_z$ with $g_z=-9.80665\,\mathrm{m/s^2}$ (`:127-138`,
`:44`). A **penalty** variant splits the normal into a $f_n^{+}/f_n^{-}$ pair
(shift 4 instead of 3) so a tensile residual can be measured rather than
forbidden (`:79-92`).

### The friction cone and its linearisation

Coulomb friction at each contact is the second-order cone

$$
\sqrt{(f^{t_1}_k)^2 + (f^{t_2}_k)^2}\;\le\;\mu\,f^n_k .
$$

RBE replaces it with a polyhedral pyramid of $K$ faces. Face $k$ at angle
$\theta_k = 2\pi k/K$ contributes the linear row

$$
\cos\theta_k\,f^{t_1} + \sin\theta_k\,f^{t_2} - \mu\,f^n \;\le\; 0,
$$

assembled into $A_{fr}\tilde f \le \mathbf 0$ (`FrictionConeBuilder.cs:24-32`,
`:236-251`). The default $K=4$ is special-cased to exact
$\{+1,0,-1,0\}$ coefficients to match `compas_cra._make_afr` bit-for-bit
(`:215-227`); $\mu=0.84$ (a 40-degree friction angle) is the upstream default
(`:83-86`).

**Original derivation: the inscribed-pyramid correction.** A circumscribed
square pyramid over-estimates the cone. Its faces touch the cone along the
axes but bulge out at $45^\circ$, where the admissible tangential magnitude is

$$
\|f_t\|_{\max} = \frac{\mu f^n}{\cos(\pi/K)} ,
$$

which at $K=4$ equals $\mu f^n / \cos 45^\circ = \sqrt 2\,\mu f^n$: the
linearisation grants up to $\sqrt 2 \approx 1.41$ times the true friction
capacity. For a stability claim that is the wrong direction (it certifies
unstable walls as stable). The fix is to shrink the coefficient so the pyramid
is **inscribed** in the cone,

$$
\mu_{\text{eff}} = \mu\cos\!\left(\tfrac{\pi}{K}\right),
$$

making every admissible $(f^{t_1},f^{t_2})$ satisfy the exact quadratic cone, a
conservative under-approximation (`FrictionConeBuilder.cs:105-130`). The CRA
checker passes `inscribed: true` by default (`CraStabilityChecker.cs:90`); the
flag was a flagged correctness gap (the V3-review blocker) and is the
recommended setting for any published verdict.

### The QP and the verdict

RBE is then a convex quadratic program (`RbeQpFormulation.cs:11-19`):

$$
\min_{\tilde f}\; \tfrac12\,\tilde f^{\top} H\,\tilde f
\quad\text{s.t.}\quad A_{eq}\tilde f = -\mathbf b,\;\; A_{fr}\tilde f \le \mathbf 0,\;\; f^n \ge 0 .
$$

$H$ is diagonal with separate normal and tangential weights; the default
identity recovers the minimum-norm contact-force solution, and a tangential
weight of $\sim10^3$ (Kao 2022 section 5) biases toward normal-dominated
distributions (`:78-111`). The $f^n\ge0$ box enforces compression-only
contact (`:140-166`). Feasibility of this QP is the stability verdict: a
feasible force state means a statically admissible solution exists.

**Sign fix.** The original `Build` set the equality right-hand side to $-\mathbf b$,
which combined with the builder's $A_{eq}[F_z]=-1$ and $b[F_z]=-mg$ yields
$f^n=-mg<0$, infeasible against $f^n\ge0$. `BuildPhysicsCorrected` flips the sign
so $f^n\ge0$ means compression (`RbeQpFormulation.cs:180-224`). The shipped GH
component calls `BuildPhysicsCorrected`, not the legacy `Build`, with the bug
explained inline (`MasonryStabilityRbeComponent.cs:297-305`); the prior audit
note that the component still wires the sign-buggy `Build` is stale for the
current source.

**Originality.** RBE is **clean-room (tier B)**. The implementation is a
pure-managed transcription of the equations in Kao et al. (2022), with the MIT
`compas_cra` reference as the structural model (cited at
`MasonryStabilityRbeComponent.cs:69-71`; GUID
`F6BAC3D4-4E5F-4071-BC3D-5E6F7A8B9CAD`). No upstream `.cs` is in the tree; the
parity tests compare numbers, not lines (`CraCompasParityTests`). The
inscribed-pyramid correction is the small clean-room delta over a naive
transcription.

---

## 5.2 Coupled Rigid-Block Analysis (CRA)

RBE is force-only, and force-only equilibrium admits physically unrealisable
states. Kao's **H-model** is the canonical counterexample: a beam bridging two
columns, touching them only on vertical faces with nothing underneath. RBE
finds a self-equilibrated horizontal squeeze whose friction carries the beam,
even though nothing can produce that squeeze. CRA couples statics with virtual
rigid-body **kinematics** so that contacts only carry force where a consistent
motion lets them engage (`CraStabilityChecker.cs:17-50`). The coupling
conditions (Kao 2022 Eqs. 8-11) are

$$
\delta d = A_{eq}^{\top}\,\delta q \qquad\text{(duality: block motions induce contact displacements)}
$$

$$
f_t = -\alpha\,\delta d_t,\;\; \alpha \ge 0 \qquad\text{(friction opposes virtual sliding)}
$$

$$
f_n\,(\delta d_n - \varepsilon) = 0,\;\; s\,\delta d_n \le \varepsilon \qquad\text{(normal force only where the joint engages)}
$$

$$
\min\;\|f_n\|^2 + \|\alpha\|^2 \qquad\text{(Gauss least-constraint; infeasible} \Leftrightarrow \text{unstable).}
$$

The exact problem is a nonconvex NLP (bilinear complementarity); `compas_cra`
solves it with IPOPT. The repository does **not** ship IPOPT. Instead it
implements an **alternating convex certificate search** that is *sound in the
certifying direction* (`:31-49`):

1. Solve the penalty RBE QP for forces $f$.
2. Solve a convex **kinematic certificate** QP: given the engaged set
   $E=\{k: f^n_k > \text{tol}\}$ and the friction directions $\hat f_t$, find a
   virtual motion $\delta q$ and slacks $\beta\ge0$ minimising

   $$
   \sum_{k\in E}\!\left(\frac{\delta d_{n,k}-\varepsilon}{\varepsilon}\right)^2 + \sum_{k}\!\left(\frac{\delta d_{t,k}+\beta_k\hat f_{t,k}}{\varepsilon}\right)^2
   $$

   subject to $\delta d = A_{eq}^{\top}\delta q$, non-penetration
   $s\,\delta d_n\le\varepsilon$, and a virtual-motion bound $|\delta d|\le\eta$
   (`:236-240`). A near-zero residual means a consistent virtual motion exists,
   so the state is **CRA-certified**.
3. Otherwise **restrict**: contacts whose engagement the kinematics rejected
   have their normal columns forced to zero (complementarity, Eq. 10),
   badly misaligned friction is zeroed (Eq. 9), and the force QP is re-solved.
   Tension or infeasibility means the RBE acceptance was self-stress, so the
   assembly is **CRA-unstable**.
4. Iterate, bounded (`:127`, `maxOuterIterations=12`).

A found $(f,\delta q,\alpha\ge0)$ triple is a feasible point of the CRA
constraints, so "certified" is sound; "not certified" is conservative because
the alternating search is a heuristic for a nonconvex problem (`:45-49`). Two
engineering refinements matter for correctness. The engagement threshold is set
to 1% of peak normal force so min-norm RBE's tiny spurious forces on
load-free joints are not treated as engaged (`:131-138`). Engagement weighting
is force-weighted, $w_i=\sqrt{f^n_i/f^n_{\max}}$, mirroring the NLP's energy
trade where unloading a lightly-loaded joint is cheap (`:255-263`). The
certificate has an exact fast path: solve the unconstrained least squares by
dense Cholesky (`SolveDenseSpd`, `:412-444`), and only fall back to the
constrained ADMM (warm-started at the LS optimum) when the LS point violates
non-penetration (`:342-384`).

**Original derivation: the residual decode.** The certificate solves
$H = 2J^{\top}J$, $c = 2J^{\top}r_0$, i.e. the normal equations of the
weighted least-squares system $J x + r_0$ with $x=[\delta q,\beta]$
(`:289-306`). The reported residual is the worst force-weighted engaged-vertex
mismatch in units of $\varepsilon$,

$$
\text{residual} = \max_{k\in E}\; w_k\,\frac{|\delta d_{n,k} - s\varepsilon|}{\varepsilon},
$$

with the certified threshold at $0.5\varepsilon$ (`:181-186`, `:387-396`). The
restriction peels only the worst offenders each round (within 75% of the worst
weighted residual) so de-loading mirrors the exact NLP's energy trade
(`:189-201`).

**Originality.** The CRA verdict is an **A-candidate** (original formulation,
prior-art sweep pending). The equations are Kao's, but the
alternating-convex soundness certificate is not in `compas_cra`, which uses a
nonconvex IPOPT solve. The contribution is the *managed soundness certificate*,
not the algorithm family. It is fronted by the Masonry Stability Check
component (GUID `D5F10015-2B43-4E8A-A1C7-9D0F4B6E2A91`,
`MasonryStabilityCheckComponent.cs:34`). The H-model is a first-class
regression test: `Cra_HModel_RbeAcceptsButCraRejects` asserts RBE accepts and
CRA rejects (`CraStabilityCheckerTests.cs:83-105`), and a `compas_cra`
cross-fixture parity suite pins agreement on shared cubes, stacks, and an arch
(`Program.cs:347-356`).

---

## 5.3 The convex solvers (ADMM)

The certificate's constrained fallback and the penalty RBE QP run on a
pure-managed OSQP-style ADMM solver (`AdmmQpSolver.cs:6-51`). The standard form
is

$$
\min\;\tfrac12 x^{\top}Px + q^{\top}x \quad\text{s.t.}\quad l \le A x \le u,
$$

with equality, inequality, and box-bound blocks stacked into one CSR matrix
$A$. The iteration (Stellato et al. 2020, simplified) is

$$
\tilde x = (P + \sigma I + \rho A^{\top}A)^{-1}\,(\sigma x - q + A^{\top}(\rho z - y))
$$

$$
x^{+} = \alpha\tilde x + (1-\alpha)x,\qquad z^{+} = \Pi_{[l,u]}(\alpha A\tilde x + (1-\alpha)z + y/\rho)
$$

$$
y^{+} = y + \rho\,(\alpha A\tilde x + (1-\alpha)z - z^{+}),
$$

with the factorisation cached and refactored only on $\rho$ changes
(`:182-256`). Convergence is the standard primal/dual infinity-norm test
(`:224`). Two adaptations tame the masonry systems, which mix newton-scale
forces, metre-scale moment arms, and the $10^3$ penalty weight: full Ruiz
equilibration of rows and columns over three passes
(`:108-145`), and per-row $\rho$ that stiffens equality rows by $10^3$
(`:475-478`). The constraint blocks are >99% sparse, stored CSR for $O(\text{nnz})$
matvecs (`:36-39`, `:318-426`).

A measured limit is recorded honestly: cold-start convergence on penalty-RBE
systems degrades past about 50 contact interfaces (54-interface wall 5.4 s,
147-interface 86 s), which is why `MasonryStabilityChecker` runs an LS-first
KKT certificate that decodes wall verdicts without ADMM and only falls back to
the warm-started solver when the certificate declines
(`AdmmQpSolver.cs:41-50`).

**Originality.** The ADMM is **clean-room (tier C)**: a faithful, simplified
OSQP from Stellato et al. (2020), with masonry-specific equilibration and
per-row $\rho$ as engineering deltas. It is solver infrastructure, not a
research contribution.

---

## 5.4 The polygonal wall generator (v2)

The generator builds an architectural polygonal-masonry pattern in the
$(u,v)$ parameter rectangle and reports a quality score
(`PolygonalWallGenerator.cs:7-34`; component GUID
`D5F10014-7A11-4C0E-9B22-3F6A1E2C4D80`, `PolygonalWallGeneratorComponent.cs:37`).
Four pieces of math compose it.

**Power diagram.** Cells are an additively-weighted Voronoi diagram of
jittered-grid seeds, computed exactly by half-plane clipping:

$$
\text{cell}(i) = R \cap \bigcap_{j\ne i}\Big\{x : 2(s_j - s_i)\cdot x \le |s_j|^2 - |s_i|^2 + w_i - w_j\Big\},
$$

where per-seed weights $w_i$ give genuine size grading (`:13-17`). The method
is $O(n^2 v)$, exact, and bounded for the few-hundred stones a wall needs.

**Lloyd relaxation** moves seeds to cell centroids over $k$ iterations to even
size and roundness (`:18-19`, Lloyd 1982). **Coursing morph** interpolates each
vertex toward its nearest course line,

$$
v' = (1-c)\,v + c\cdot\text{nearestCourse}(v),
$$

a continuum from $c=0$ irregular (Inca) to $c=1$ coursed rubble (`:20-21`).
**Sliver cull** removes cells whose inradius proxy $\rho = 2A/P$ falls below
$\text{frac}\cdot\sqrt{WH/n}$ and recomputes the diagram (`:22-24`).

**Original derivation: the interlock score $J$.** The generator reports an
interlock quality $J\in[0,1]$ formalising the Inca reading of Clifford and
McGee's (2018) shape-grammar analysis (`:25-30`, `InterlockScore`,
`:310-384`). Head joints are interior, more-vertical-than-horizontal cell
edges (`:341-345`); two head joints in consecutive courses are *aligned* (a
running joint, the masonry failure mode) when their $u$-midpoints coincide
within tolerance (`:360-375`); a cross vertex is a diagram node where four or
more cells meet, a "+" junction that weakens interlock (`:377-379`). The score
penalises both:

$$
J = \mathrm{clamp}\!\left(1 - \frac{L_{\text{aligned}}}{L_{\text{head}}} - \tfrac12\,\frac{N_{+}}{N_{\text{cells}}},\; 0,\; 1\right),
$$

with each interior edge counted twice (once per neighbour) and halved
consistently (`:350-353`, `:374`, `:381-383`). Higher $J$ means better
staggering.

**Originality.** **A-candidate original-research**. Kim (2024) does masonry
sequencing, not generation; the Coursing-morph continuum and the $J$ metric
have no local-corpus equivalent (prior-art sweep pending, Legakis et al. 2001
closest). The GH hover credits Kim 2024 as the sequencing substrate, Clifford
and McGee 2018 for the interlock reading, and Lloyd 1982 for relaxation
(`PolygonalWallGeneratorComponent.cs:31-33`).

![Generated polygonal wall, three-band layout](../../../examples/27_polygonal_masonry/27_01_three_band_wall.png)

![Wall-generator stability and interlock readout](../../../examples/27_polygonal_masonry/27_06_wall_generator_stability.png)

---

## 5.5 The exact-joint assembler

The generator already knows cell adjacency (shared power-diagram edges), so
re-detecting contacts from triangle meshes is wasteful and lossy: a mesh
contact detector splinters 40 stones into ~125 sub-interfaces and ~612 contact
vertices, inflating and ill-conditioning the QP. `PolygonalWallAssembler` emits
one exact planar-quad interface per adjacent stone pair directly from the
shared $(u,v)$ edge (`PolygonalWallAssembler.cs:8-30`):

$$
\text{contact quad} = [\,F_1, F_2, B_2, B_1\,],\qquad F_k = \text{map}(e_k),\quad B_k = F_k + \mathbf{nrm}(e_k)\cdot d .
$$

Because stones are extruded per-vertex along the surface normal, both stones
build their side walls from the same two rails, so the quad is exactly the
shared face on any curvature with zero tolerance dependence. This is the
correct input to the equilibrium builder of 5.1, which is why CRA certifies
generated walls (`Cra_GeneratedWall_Certified`, `Program.cs:335`).

**Originality.** **clean-room (tier B)** geometry: the quad construction is
elementary, and the contribution is the lossless coupling of generator
adjacency to the equilibrium QP rather than any new algorithm.

---

## 5.6 The imposition metric Lambda and Cyclopean carve-back

The assignment layer makes the top-down/bottom-up balance executable. Given a
stone **inventory** (found/scanned stones, the negotiation side) and the
**target cells** of a generated wall (the imposition side), assign stones to
cells minimising the material to carve away, and report the trade as numbers
(`StoneCellAssignment.cs:8-37`):

$$
\lambda_i = \frac{\mathrm{vol}(\text{stone}_i \setminus \text{cell}_i)}{\mathrm{vol}(\text{stone}_i)},\qquad
\Lambda = \frac{\sum_i \lambda_i\,\mathrm{vol}(\text{stone}_i)}{\sum_i \mathrm{vol}(\text{stone}_i)},\qquad
g_i = \frac{\mathrm{vol}(\text{cell}_i \setminus \text{stone}_i)}{\mathrm{vol}(\text{cell}_i)} .
$$

$\Lambda\approx1$ is full imposition (stock cut entirely to cells, sawn ashlar);
$\Lambda\approx0$ is true negotiation (stones used as found). The measured
middle is Clifford and McGee's Cyclopean Cannibalism wall at $\Lambda\approx0.27$
(73% of scanned stock used), the datum the ETH1100 benchmark test reports
against (`StoneCellAssignmentEthBenchmarkTests.cs:14`, `:95`). The reported
result on ETH1100 is $\Lambda=0.194$ (better than the 0.27 datum).

The pipeline is: per-mesh volume/centroid/PCA frame; a cheap volume+extent
prefilter; a voxel symmetric-difference cost over the top-K candidates per cell,
best of the four proper-rotation flips (`:283-335`); a **Hungarian** one-to-one
assignment reused from `Frahan.EdgeMatching.Core` (`:141-145`); and per-pair
$\lambda$/gap at a finer voxel resolution (`:147-171`). The voxel metrics carry
a few-percent discretisation error.

**Cyclopean carve-back.** The exact fabrication step replaces the voxel
estimate (`StoneCarveBack.cs:9-29`). Clifford and McGee's (2018) anti-nesting
places a stone overlapping its cell, then carves back everything outside the
cell, "displacing the concept of waste to the amount of material carved from
each part." Operationally, per placement:

$$
\text{carved} = \text{stone(placed)} \cap \text{cell},\qquad
\lambda_i = 1 - \frac{\mathrm{vol}(\cap)}{\mathrm{vol}(\text{stone})},\qquad
g_i = 1 - \frac{\mathrm{vol}(\cap)}{\mathrm{vol}(\text{cell})} .
$$

Booleans run through `CgalMeshBoolean`: the native CGAL kernel inside Rhino, a
managed BSP fallback headless, both volume-validated in the battery
(`:23-28`).

**Originality.** Lambda is **A-candidate original-research**: the
Lambda/gap formalisation is the contribution (Clifford and McGee measured 0.27
but never formalised it; the assignment itself is published in Bruetting 2019 /
Bukauskas 2019). The carve-back is **facade-over-primitives** over
`CgalMeshBoolean`. The assignment is a **facade** composing the in-repo
Hungarian solver and voxel kernel. Fronted by Stone Cell Match (GUID
`D5F10016-6C2D-4F1B-B3E8-7A95D0C41F62`, `StoneCellMatchComponent.cs:34`).

![Stone-to-cell match with Lambda readout](../../../examples/27_polygonal_masonry/27_07_stone_match_lambda.png)

---

## 5.7 Drop-settle for rubble; ashlar and best-fit packers

**Rubble settle.** `RubbleWallSettle` places each found stone upright in a
single-wythe wall: PCA-orient for flat bedding (largest extent to X, mid to Y,
smallest to Z so the broad face beds down), then settle into the dimples of the
course below per $(x,y)$ cell against a running height map, non-penetrating by
construction (`RubbleWallSettle.cs:9-32`). Stability is the Heyman (1966)
limit-state test: the centre of mass projected to the bed must lie inside the
convex hull of the contact footprint with margin
(`RubbleWallSettleComponent.cs:35-36`). It is deterministic (contacts from mesh
vertices, no RNG). **Clean-room (tier B)** from Furrer et al. (2017) and Johns
et al. (2020); GUID `6514A1BB-FE82-4919-9419-141A07D2358A`.

![Rubble masonry wall, ETH1100 dry-stone](../../../examples/16_rubble_masonry/16_rubble_wall.png)

**Ashlar and best-fit.** `AshlarPackComponent` is a 3D running-bond grid
stacking, AABB-first, translation-only, credited to the Gramazio/Kohler/
Eichenhofer NCCR robotic-stone running-bond pipeline
(`AshlarPackComponent.cs:31-32`; GUID `F1A2B3C4-D5E6-4789-9ABC-DEF012345678`).
`BestFitInventoryPacker` scores every remaining slab against each slot on
width/depth/height/aspect fit and picks the best, falling back to first-fit
(`BestFitInventoryPacker.cs:8-32`). **clean-room (tier C)**: the Core class
carries the correct Furrer (2017) / Johns (2020) lineage. *Licensing/citation
flag:* the GH facade `BestFitPackComponent.cs:30` carries an
`[Algorithm("Best-fit rubble inventory placement","Gramazio Kohler Eichenhofer
2017 ...")]` whose attribution to a 2017 NCCR paper is the previously flagged
likely-fabricated citation (E5); the real lineage is Furrer/Johns/`gramaziokohler-ashlar`,
and the attribute should be corrected.

![Ashlar coursed wall](../../../examples/17_ashlar_masonry/17_ashlar_wall.png)

---

## 5.8 Status and what is left

- **CRA convergence at scale.** The ADMM degrades past ~50 interfaces; the
  LS-first certificate mitigates wall-scale checks but per-element verification
  remains the pattern for large mixed assemblies (`AdmmQpSolver.cs:41-50`).
  *High.*
- **CRA is a conservative heuristic.** "Not certified" can be a false negative
  on the nonconvex problem; it is sound only in the certifying direction
  (`CraStabilityChecker.cs:45-49`). *Medium.*
- **Fabricated GH citation (E5).** `BestFitPackComponent.cs:30` attributes a
  2017 NCCR paper that does not match the Core's true Furrer/Johns lineage.
  *Medium.*
- **Stale audit note (E4).** The prior digest claims the RBE component wires the
  sign-buggy `Build`; the current source uses `BuildPhysicsCorrected`
  (`MasonryStabilityRbeComponent.cs:305`). The legacy `Build` survives only for
  sign-pinning unit tests and should be marked obsolete to avoid future
  mis-wiring. *Low.*
- **Prior-art sweeps pending.** The $J$ interlock metric, the Coursing-morph
  continuum, and the Lambda formalisation are marked A-candidate; AGENTS.md
  section 9 forbids asserting "novel" without a completed sweep. *Medium.*
- **Sequencer redundancy.** The 3D Kim sequencer
  (`PolygonalMasonrySequence3DComponent`, GUID
  `C5F18B4D-8A6F-4E72-AC83-1FBD32D8C7B2`) overlaps `BlockBuildOrderer`; a merge
  is a documented candidate. *Low.*
- **Example 02 has no render.** `02_masonry_assembly` ships `.gh` + `.3dm` only;
  no PNG is embeddable for the assembly-sequencing figure. *Low.*

---

### References

- Kao, G.T.-C., Iannuzzo, A., Thomaszewski, B., Coros, S., Van Mele, T., Block, P. (2022). Coupled Rigid-Block Analysis: Stability-Aware Design of Complex Discrete-Element Assemblies. Computer-Aided Design 146:103216. DOI 10.1016/j.cad.2022.103216
- Stellato, B., Banjac, G., Goulart, P., Bemporad, A., Boyd, S. (2020). OSQP: an operator splitting solver for quadratic programs. Mathematical Programming Computation 12:637-672. DOI 10.1007/s12532-020-00179-2
- Heyman, J. (1966). The stone skeleton. International Journal of Solids and Structures 2(2):249-279. DOI 10.1016/0020-7683(66)90018-7
- Kim, S. et al. (2024). Finding the Installation Sequence of Polygonal Masonry through Design and Depth Search of a DAG. ASME IDETC/CIE, paper DETC2024-142563.
- Clifford, B., McGee, W. (2018). Cyclopean Cannibalism: A Method for Recycling Rubble. ACADIA 2017 / 2018 (Disciplines and Disruption).
- Lloyd, S.P. (1982). Least squares quantization in PCM. IEEE Transactions on Information Theory 28(2):129-137. DOI 10.1109/TIT.1982.1056489
- Furrer, F., Wermelinger, M., Yoshida, H., Gramazio, F., Kohler, M., Siegwart, R., Hutter, M. (2017). Autonomous robotic stone stacking with online next best object target pose planning. IEEE ICRA. DOI 10.1109/ICRA.2017.7989273
- Johns, R.L., Wermelinger, M., Mascaro, R., Jud, D., Gramazio, F., Kohler, M., Chli, M., Hutter, M. (2020). Autonomous dry stone. Construction Robotics 4:127-140. DOI 10.1007/s41693-020-00037-6
- Legakis, J., Dorsey, J., Gortler, S. (2001). Feature-based cellular texturing for architectural models. SIGGRAPH 2001:309-316. DOI 10.1145/383259.383293
- Bruetting, J., Desruelle, J., Senatore, G., Fivet, C. (2019). Design of truss structures through reuse. Structures 18:128-137. DOI 10.1016/j.istruc.2018.11.006
- Bukauskas, A. et al. (2019). Inventory-constrained structural design: new objectives and optimization techniques. (reuse-of-irregular-elements lineage cited for the assignment).
