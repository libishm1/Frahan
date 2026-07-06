# The mathematics of the masonry stability stack

Code-bound equation layer for the shipping masonry equilibrium and stability
checkers. Each equation: statement, short derivation, code provenance, method-
class citation. Derived from the implementation in
`src/Frahan.StonePack.Core/Masonry/`, not from paper
figures. Renders natively on GitHub and on the docs site (MathJax).

Shipping implementations: `EquilibriumMatrixBuilder` + `FrictionConeBuilder`
(assembly of the rigid-block system), `RbeQpFormulation` (QP statement),
`MasonryStabilityChecker` (penalty RBE verdict, the shared "does it stand?"
gate), `CraStabilityChecker` (coupled kinematic certificate),
`AdmmQpSolver` / `OsqpQpSolver` / `ManagedQpSolver` behind
`MasonrySolverRegistry` (solver ladder), `BulletSettleService` (physics
settle), and the COM-over-support gates in `RubbleWallSettle` and
`NboPlanner`. Primary method-class citation throughout: Kao, Iannuzzo,
Thomaszewski, Coros, Van Mele, Block (2022), "Coupled Rigid-Block Analysis:
Stability-Aware Design of Complex Discrete-Element Assemblies",
Computer-Aided Design 146:103216 (reference implementation
BlockResearchGroup/compas_cra, MIT), per the `[Algorithm]` attributes on
`MasonryStabilityRbeComponent` (Doi 10.1016/j.cad.2022.103216).

---

## 1. Rigid-block equilibrium (RBE)

### 1.1 Unknowns: contact forces at interface vertices

Each contact interface $j$ (a planar contact polygon between blocks $A_j$ and
$B_j$) carries one force per polygon vertex $k$, expressed in the interface
frame $(\hat n_j, \hat t_{1j}, \hat t_{2j})$:

$$
f_k = f_{n,k}\,\hat n_j + f_{t1,k}\,\hat t_{1j} + f_{t2,k}\,\hat t_{2j}.
$$

In penalty mode the normal splits into two non-negative halves,

$$
f_{n,k} = f^{+}_{n,k} - f^{-}_{n,k},
\qquad f^{+}_{n,k} \ge 0,\quad f^{-}_{n,k} \ge 0,
$$

so the column layout is 3 per vertex (no-penalty, `shift=3`) or 4 per vertex
(penalty, `shift=4`). $f^{-}_{n}$ is the tensile half; the Signorini
complementarity $f^{+}_{n}f^{-}_{n}=0$ is enforced by the objective penalty,
not by the bounds. Code: `EquilibriumMatrixBuilder.Build` (column layout,
lines 72-91), `ForceComponent` enum in `EquilibriumSystem.cs`. Citation:
Kao et al. 2022 (compas_cra `equilibrium_setup` / `make_aeq`).

### 1.2 Equilibrium constraints, one 6-row block per free block

For every free (non-fixed) block $i$ with volume-weighted centre of mass
$c_i$, force and moment balance over its incident interface vertices:

$$
\sum_{(j,k)\ \text{incident on } i} s_{ij}\, f_k \;+\; W_i \;=\; 0,
\qquad
\sum_{(j,k)\ \text{incident on } i} s_{ij}\,(r_k - c_i)\times f_k \;=\; 0,
$$

where $r_k$ is the contact-vertex world position, $s_{ij}=+1$ when block $i$
is the $A$ side of interface $j$ and $s_{ij}=-1$ when it is the $B$ side (the
interface normal points from $A$ into $B$, matching compas_cra's `reverse=`
convention), and the external load is self-weight applied at the COM:

$$
W_i = \big(0,\ 0,\ \rho_i\,\lvert V_i\rvert\, g_z\big),
\qquad g_z = -9.80665 \ \text{by default},
$$

so gravity contributes no moment about $c_i$. The whole system is assembled
sparse (COO) as

$$
A_{eq}\, f + b = 0,
$$

6 rows per free block (3 force, 3 moment, xyz order), with
$b_{[\text{row } F_z]} = \rho_i \lvert V_i\rvert g_z$ and all other entries of
$b$ zero. Fixed blocks (boundary conditions, the ground course) contribute no
rows. A moment column entry is the cross product $(r_k - c_i)\times\hat e$
of the arm with the basis vector. Code:
`EquilibriumMatrixBuilder.Build` / `ContributeBlock` / `AddForceAndMoment`
(`Masonry/Equilibrium/EquilibriumMatrixBuilder.cs`), sign convention documented
in the file header. Citation: Kao et al. 2022, CAD 146:103216 (compas_cra
`aeq_block`).

### 1.3 Centre of mass and volume (divergence theorem)

Per closed outward-oriented triangulation with triangles $(a_f, b_f, c_f)$:

$$
V = \frac{1}{6}\sum_f a_f\cdot(b_f\times c_f),
\qquad
c = \frac{1}{24\,V}\sum_f \Big[a_f\cdot(b_f\times c_f)\Big]\,(a_f + b_f + c_f).
$$

*Derivation.* Each triangle spans a signed tetrahedron with the origin;
$a\cdot(b\times c)$ is six times its signed volume and its centroid is
$(a+b+c)/4$; summing signed-volume-weighted centroids and normalising gives
the exact solid centroid. Falls back to the vertex mean when
$\lvert V\rvert < 10^{-12}$; the stability checker rejects free blocks with
degenerate volume outright (a weightless block would trivially "balance",
a silent false-stable). Code: `BlockCenterOfMass.VolumeWeighted` /
`SignedVolume`; degenerate-block guard in
`MasonryStabilityChecker.CheckDetailed` (lines 185-197). Citation: standard
divergence-theorem mass properties; matches compas_cra `block.center()`.

### 1.4 Friction cone linearisation (polyhedral pyramid)

The Coulomb condition at each contact vertex,

$$
\sqrt{f_{t1}^2 + f_{t2}^2} \;\le\; \mu\, f_n,
$$

is linearised by $K$ equally spaced face directions in the tangent plane.
Face $k$ at $\theta_k = 2\pi k/K$ gives one row of $A_{fr}$:

$$
\cos\theta_k\, f_{t1} + \sin\theta_k\, f_{t2} - \mu_{\mathrm{eff}}\, f_n \;\le\; 0,
\qquad k = 0,\dots,K-1,
$$

stacked as $A_{fr} f \le 0$ ($K$ rows per contact vertex, same columns as
$A_{eq}$). In penalty mode the normal term expands to
$-\mu_{\mathrm{eff}} f^{+}_n + \mu_{\mathrm{eff}} f^{-}_n$. The effective
coefficient is

$$
\mu_{\mathrm{eff}} =
\begin{cases}
\mu\,\cos(\pi/K) & \text{inscribed pyramid (checker default, conservative)}\\
\mu & \text{circumscribed pyramid (compas\_cra behaviour)}
\end{cases}
$$

with defaults $\mu = 0.84$ (a $40^\circ$ friction angle, dry stone, the
compas_cra default) and $K = 8$.

**Deviation from the reference implementation:** compas_cra hard-codes a
$K{=}4$ circumscribed pyramid, which over-estimates friction capacity by up
to $\sqrt 2$ on the pyramid diagonal. The shipping checker defaults to $K=8$
inscribed ($\mu_{\mathrm{eff}} = \mu\cos(\pi/8) \approx 0.924\,\mu$), so every
admissible $(f_{t1}, f_{t2})$ also satisfies the true quadratic cone. Code:
`FrictionConeBuilder.Build` (`Masonry/Equilibrium/FrictionConeBuilder.cs`,
`inscribed` parameter, K=4 exact-coefficient special case at lines 216-234);
default flip in `MasonryStabilityChecker` (`DefaultFaceCount = 8`,
`inscribed: true`). Citation: Kao et al. 2022 section 4 (compas_cra
`friction_setup` / `_make_afr`).

### 1.5 The penalty RBE QP actually solved

`MasonryStabilityChecker.CheckDetailed` formulates and solves, per assembly:

$$
\min_{f}\ \frac{1}{2}\sum_{v}\Big(
(f^{+}_{n,v})^2 \;+\; \gamma\,(f^{-}_{n,v})^2 \;+\;
\tau\big(f_{t1,v}^2 + f_{t2,v}^2\big)\Big)
$$

$$
\text{s.t.}\quad A_{eq} f = b_{eq},
\qquad A_{fr} f \le 0,
\qquad f^{+}_{n,v} \ge 0,\ f^{-}_{n,v} \ge 0,
\qquad f_{t} \ \text{free},
$$

with the diagonal Hessian weights as shipped: `hessianScale` $=1$ on
$f^{+}_n$, tension penalty $\gamma = 10^{3}$ (`TensionPenaltyGamma`) on
$f^{-}_n$, and `tangentialScale` $\tau = 1$ by default (Kao 2022 section 5
hints $\tau\sim 10^{3}$ to prefer normal-dominated distributions; the code
exposes it but ships 1.0). The linear term is zero. The penalty formulation is
always feasible, so the verdict is read from the optimum, not from
infeasibility: large $\lVert f^{-}_n\rVert$ means the assembly demands tension
there, and it localises WHERE (Kao 2022 Eq. 14 semantics).

**Deviations from Kao 2022 Eq. 14 as documented in the code:**

1. The displacement coupling of Eq. 14 is omitted at this stage; the coupled
   kinematics is the separate CRA pass (section 3 below).
2. $\gamma = 10^{3}$, not a huge weight: the verdict reads the tension
   magnitude (fixed by statics), not the objective value, and $10^{6}$ stalls
   the ADMM (Hessian conditioning note, `MasonryStabilityChecker` lines
   199-215).
3. Sign correction: `RbeQpFormulation.Build` inherited a sign inconsistency
   (builder writes $b_z = -m g_{\mathrm{abs}}$, Build sets
   $b_{eq} = -b$, and with the $B$-side $-1$ convention the constraint forced
   $f_n \le 0$, infeasible against the $f_n \ge 0$ bounds).
   `RbeQpFormulation.BuildPhysicsCorrected` flips $b_{eq}$ once more so
   $f_n \ge 0$ means compression under the existing conventions; it is the
   entry point the shipping checker uses. The uncorrected `Build` remains only
   for tests that pin the old signs (remark block, `RbeQpFormulation.cs`
   lines 203-220).

Code: `RbeQpFormulation.Build` / `BuildPhysicsCorrected`
(`Masonry/Solvers/RbeQpFormulation.cs`); dense/sparse gate at
$n^2 + m_{eq} n + m_{fr} n > 1.2\times 10^{7}$ cells switches the problem to
COO blocks with a diagonal-only Hessian (the Guell portico OOM fix,
2026-07-02). Citation: Kao et al. 2022 sections 4-5; compas_cra (MIT).

### 1.6 Verdict and the margin metrics reported to users

Stability is decided against the solver noise floor, which scales with the
largest force in the system:

$$
\text{RBE-stable} \iff
\max_v f^{-}_{n,v} \;\le\; 10^{-3}\,\max\Big(\max_v f^{+}_{n,v},\ 10^{-9}\Big).
$$

The comment block in `MasonryStabilityChecker` (lines 317-341) records why
$\max$-compression is the correct scale: ADMM residual noise is
$\varepsilon_{rel}\cdot\max\text{force}$; median-compression and
gravity-load scalings are both disproven by pinned counterexamples (the
compas_cra wedge and a 14-block near-limit arch would flip to false-unstable).

Per-interface friction utilisation (the "how close to sliding" margin shown
on the canvas):

$$
U_j = \max_{v \in j}\ \frac{\sqrt{f_{t1,v}^2 + f_{t2,v}^2}}{\mu_{\mathrm{eff}}\, f^{+}_{n,v}}
\qquad\text{(computed only where } f^{+}_{n,v} \ge \max(10^{-12},\ 10^{-4}\textstyle\max_v f^{+}_{n,v})\text{)},
$$

with $U_j = 1$ meaning the cone is saturated; vertices carrying ~zero normal
force are floored out (their ratio is dust). The "weakest interface" is the
one demanding the most tension, falling back to the highest $U_j$ when no
tension exists. `StabilityResult` carries `MaxCompression`,
`MaxFrictionUtilization`, `WeakestInterfaceIndex`, and the per-interface
list; the GH component `Masonry Stability Check` outputs the Stable boolean,
the report string, and the per-interface utilisation list. Code:
`MasonryStabilityChecker.CheckDetailed` (decode passes 1-2, lines 267-356);
`MasonryStabilityCheckComponent.RegisterOutputParams`. Citation: verdict
semantics per the static (lower-bound) theorem of limit analysis
(Heyman 1966; Kao 2021/2022 RBE reading in the component description).

---

## 2. LS-first KKT certificate and cone polish (KB-10 short-circuit)

### 2.1 Closed-form equality-KKT point

The penalty Hessian is diagonal, so the equality-constrained relaxation
$\min \tfrac12 f^\top H f + c^\top f$ s.t. $A_{eq} f = b_{eq}$ has the
closed-form KKT solution

$$
f = H^{-1}\big(A_{eq}^\top y - c\big),
\qquad
\big(A_{eq} H^{-1} A_{eq}^\top\big)\, y = b_{eq} + A_{eq} H^{-1} c,
$$

with the dual system only $m = 6\cdot\text{freeBlocks}$ square (dense
Cholesky). The raw point is projected onto the complementarity split per
normal pair, $v = f^{+}_n - f^{-}_n$:

$$
f^{+}_n \leftarrow \max(v, 0),
\qquad
f^{-}_n \leftarrow \max(-v, 0),
$$

which preserves $A_{eq} f$ and $A_{fr} f$ exactly because the $f^{+}_n$ and
$f^{-}_n$ columns are exact negatives of each other in both matrices. If the
projected point is equality-feasible (residual $\le 10^{-6}\cdot$ scale),
inside the cone ($\le 10^{-7}\cdot$ scale), inside the bounds, and
tension-free ($\max f^{-}_n \le 10^{-3}\max f^{+}_n$), it is an admissible
compressive force state and the static lower-bound theorem certifies STABLE
without running the ADMM at all. The short-circuit never declares unstable.
Measured: 54/95/147-interface walls drop from 5.4/24/86 s to 0.07/0.4/1.1 s.
Code: `MasonryStabilityChecker.TryLsFirstKktPoint` /
`SolveLsCertificateRound` (comment block lines 493-524). Citation: static
theorem of limit analysis (Heyman 1966); Kao et al. 2022 for the QP being
short-circuited.

### 2.2 POCS cone polish (alternating projection)

When only the friction cone blocks the KKT point, the code alternates
projections between the equality manifold
$E = \{f : A_{eq} f = b_{eq}\}$ (projected in the $H$-metric, one cached
Cholesky of the same dual matrix) and the per-vertex second-order cone
inscribed in the polyhedral cone, coefficient
$a = \mu_{\mathrm{eff}}\cos(\pi/K)$. The closed-form SOC projection per
vertex, with $v = f^{+}_n - f^{-}_n$ and $z_t = \lVert(f_{t1}, f_{t2})\rVert$:

$$
\Pi_{\mathcal C}(v, f_t) =
\begin{cases}
(v,\ f_t) & z_t \le a\,v \quad\text{(inside)}\\[2pt]
(0,\ 0) & a\,z_t \le -v \quad\text{(polar cone, to the origin)}\\[2pt]
\left(t,\ \dfrac{a\,t}{z_t}\, f_t\right),\quad t = \dfrac{a\,z_t + v}{a^2 + 1} & \text{otherwise,}
\end{cases}
$$

with $f^{-}_n$ zeroed, so the returned point is exactly cone-, bound- and
tension-feasible by construction; only the equality residual needs the
$10^{-6}$ gate. Membership in the inscribed SOC implies membership in the
$K$-face polyhedral cone, so certification stays conservative. A stagnation
cutoff (residual not improving 5% per 250 iterations, cap 20000) hands
control to the warm-started ADMM: an empty intersection $E\cap\mathcal C$
(unstable assembly) produces a positive-gap cycle. Code:
`MasonryStabilityChecker.PolishConeByAlternatingProjection`. Citation:
alternating projections (POCS), standard; inscribed-cone conservatism per
the FrictionConeBuilder note.

---

## 3. CRA: the coupled rigid-block certificate

### 3.1 What "coupled" means (Kao 2022 Eqs. 8-11 as quoted by the code)

RBE is force-only and admits physically unrealisable self-stressed states
(normal-force pairs "squeezed out of nowhere" that let friction carry
anything; Kao's H-model counterexample). CRA couples statics with virtual
rigid-body kinematics. The constraint set implemented against, per the
`CraStabilityChecker` header:

$$
\delta d = A_{eq}^\top\,\delta q
\qquad\text{(8: duality, block motions induce contact displacements)}
$$

$$
f_t = -\alpha\,\delta d_t,\qquad \alpha \ge 0
\qquad\text{(9: friction opposes the virtual sliding)}
$$

$$
f_n\,(\delta d_n - \varepsilon) = 0,
\qquad s\,\delta d_n \le \varepsilon
\qquad\text{(10: normal force only where the joint engages; non-penetration)}
$$

$$
\min\ \lVert f_n\rVert^2 + \lVert\alpha\rVert^2
\qquad\text{(11: Gauss least constraint; infeasible} \iff \text{unstable)}
$$

with $\varepsilon$ the rigid-body "give" and $s = +1$ the closing-positive
sign under the builder's conventions. The exact problem is a nonconvex NLP
(bilinear complementarity; compas_cra solves it with IPOPT).

**Deviation:** the shipping implementation is NOT the IPOPT NLP. It is an
alternating convex certificate search, sound in the certifying direction: a
found $(f, \delta q, \alpha \ge 0)$ pair IS a feasible point of the CRA
constraints, so "certified" is sound; "not certified" is conservative. The
`IpoptManagedStub` reports unavailable and the registry falls back, so no
IPOPT path ships today. Code: `CraStabilityChecker` header (lines 9-50);
`MasonrySolverRegistry.UseIpoptIfAvailable`. Citation: Kao et al. 2022,
CAD 146:103216, Eqs. 8-14.

### 3.2 The certificate QP (step 2 of the alternation)

Given RBE forces, the engaged set
$E = \{i : f_{n,i} > \max(10^{-9},\ 0.01\max_i f_{n,i})\}$ (min-norm RBE
sprinkles dust on unloaded joints; the 1% threshold ignores them the way the
exact NLP drives them to zero) and the friction set $F$ (vertices with
$\lvert f_t\rvert$ above the same tolerance), solve over
$x = [\delta q,\ \beta]$:

$$
\min_{\delta q,\ \beta\ge 0}\
\sum_{i\in E} w_i^2\left(\frac{\delta d_{n,i} - s\,\varepsilon}{\varepsilon}\right)^2
+ \sum_{i\in F}\left\lVert \frac{\delta d_{t,i} + \beta_i\,\hat f_{t,i}}{\varepsilon}\right\rVert^2
$$

$$
\text{s.t.}\quad
s\,\delta d_{n,i} \le \varepsilon \ \ (\text{all contact vertices}),
\qquad
\lvert \delta d \rvert_{\text{component}} \le \eta,
\qquad
\delta d = A_{geo}^\top\,\delta q,
$$

where $A_{geo}$ is the penalty-free ($\text{shift}=3$) equilibrium matrix
(the geometric duality columns), $\hat f_{t,i}$ is the unit friction
direction from the force solve, and the engagement weights mirror the NLP's
$\lVert f_n\rVert^2$ energy trade (unloading a lightly loaded joint is cheap):

$$
w_i = \sqrt{\frac{f_{n,i}}{\max_i f_{n,i}}},
\qquad
\varepsilon = 10^{-4}\cdot\overline{\ell}_{\text{contact edge}},
\qquad
\eta = 100\,\varepsilon.
$$

The scales come from the mean contact-polygon edge length (Kao: fractions of
block size). The QP is solved as unconstrained least squares first (dense
Cholesky on $H = 2J^\top J + 10^{-9} I$); the constrained ADMM runs only when
the LS optimum violates non-penetration or the $\eta$ bound, warm-started at
the LS point (KB-11). Code: `CraStabilityChecker.SolveCertificate`
(objective block comment lines 235-240). Citation: Kao et al. 2022 Eqs. 8-10.

### 3.3 PASS gate and complementarity restriction

$$
\text{CRA-certified} \iff
\max_{i\in E}\ w_i\,\frac{\lvert \delta d_{n,i} - s\,\varepsilon\rvert}{\varepsilon}
\;\le\; 0.5,
$$

the worst weighted engaged residual in units of $\varepsilon$
(`CraResult.CertificateResidual`, the CRA margin reported to users). If the
gate fails, step 3 peels only the worst offenders this round:

$$
\text{drop engaged } i \ \text{with}\quad
w_i\,\frac{\lvert\delta d_{n,i} - s\varepsilon\rvert}{\varepsilon}
\;>\; \max(0.5,\ 0.75\cdot\text{residual})
\ \Rightarrow\ f_{n,i} := 0 \ \text{(Eq. 10)},
$$

$$
\text{zero tangents where}\quad
\frac{\lVert\delta d_{t,i} + \beta_i\hat f_{t,i}\rVert}{\varepsilon} > 2
\ \text{(friction cannot oppose any consistent sliding, Eq. 9)},
$$

then re-solves the force QP with those columns pinned to zero. Tension or
infeasibility after restriction means the RBE acceptance was self-stress:
CRA-UNSTABLE. No certificate within `maxOuterIterations = 12` reports
not-certified (conservative). RBE-unstable short-circuits immediately (CRA
only adds constraints). Code: `CraStabilityChecker.Check` steps 1-4.
Citation: Kao et al. 2022 Eqs. 8-14.

### 3.4 Out-of-process CRA worker

`VaultShellCraComponent` / `VaultRubbleCraComponent` run the solve in
`frahan_cra_worker.exe` via `CraWorkerClient` (binary blob I/O, timeout +
kill). The worker executes `MasonryStabilityChecker.Check`, i.e. the penalty
RBE gate of section 1.5; the label "CRA" on the worker path denotes the
pipeline, not the coupled certificate of section 3.2. **Verified 2026-07-06:**
no shipping path routes the coupled certificate out of process. The worker
(`Frahan.Cra.Worker/Program.cs` lines 45/61/68) invokes only
`MasonryStabilityChecker.Check`; `CraStabilityChecker.Check` has a single GH
call site, the in-process `MasonryStabilityCheckComponent` with `CRA = true`.

---

## 4. Solver layer

### 4.1 Which solvers ship

`MasonrySolverRegistry.UseOsqpIfAvailable()` installs, first available wins:
(1) `OsqpQpSolver`, native OSQP via `frahan_osqp.dll`;
(2) `AdmmQpSolver`, pure managed OSQP-style ADMM (always available).
`ManagedQpSolver` (Dykstra 1983 / Boyle & Dykstra 1986 alternating
projections) is no longer in the ladder: it diverges on mixed-scale masonry
RBE rows (residuals ~$10^{60}$, the V3-review "Dykstra convergence tail").
Code: `MasonrySolverRegistry.cs` lines 89-115; `ManagedQpSolver.cs` header.

### 4.2 The ADMM problem form and iteration (managed OSQP)

All constraint blocks stack into a single form

$$
\min_x\ \tfrac12 x^\top P x + q^\top x
\qquad\text{s.t.}\quad l \le A x \le u,
$$

$$
A = \begin{bmatrix} A_{eq} \\ A_{fr} \\ I \end{bmatrix},
\qquad
l = \begin{bmatrix} b_{eq} \\ -\infty \\ lb \end{bmatrix},
\qquad
u = \begin{bmatrix} b_{eq} \\ 0 \\ ub \end{bmatrix},
$$

(equality rows $l=u$; friction rows upper-bounded by 0; box rows are the
identity block). Iteration $k$, exactly as coded:

$$
\tilde x = \big(P + \sigma I + A^\top \mathrm{diag}(\rho)\, A\big)^{-1}
\big(\sigma x - q + A^\top(\rho z - y)\big)
$$

$$
x^{+} = \alpha\tilde x + (1-\alpha)x,
\qquad
z^{+} = \Pi_{[l,u]}\big(\alpha A\tilde x + (1-\alpha)z + y/\rho\big),
$$

$$
y^{+} = y + \rho\big(\alpha A\tilde x + (1-\alpha)z - z^{+}\big),
$$

with $\sigma = 10^{-6}$, over-relaxation $\alpha = 1.6$, base $\rho = 0.1$,
and per-row $\rho_r = 10^{3}\rho$ on equality rows (they must hold exactly).
Convergence check every 10 iterations:

$$
\lVert Ax - z\rVert_\infty \le \varepsilon_{abs} + \varepsilon_{rel}\max(\lVert Ax\rVert_\infty, \lVert z\rVert_\infty),
\qquad
\lVert Px + q + A^\top y\rVert_\infty \le \varepsilon_{abs} + \varepsilon_{rel}\max(\lVert Px\rVert_\infty, \lVert A^\top y\rVert_\infty, \lVert q\rVert_\infty).
$$

Constructor defaults $\varepsilon_{abs}=10^{-6}$, $\varepsilon_{rel}=10^{-5}$;
the stability checker calls it at $10^{-4}/10^{-4}$ because the verdict only
reads tension against $10^{-3}\cdot$max-compression. Adaptive $\rho$: every
100 iterations, if the primal/dual residual ratio exceeds 10 then
$\rho \leftarrow 5\rho$, if below 0.1 then $\rho \leftarrow \rho/5$, clamped
to $[10^{-6}, 10^{6}]$, at most 10 refactorisations. Pre-scaling is 3 passes
of full Ruiz equilibration (rows and columns by inverse square-root infinity
norms), with $P \leftarrow DPD$, $q \leftarrow Dq$ and the solution unscaled
as $x = Dx'$. Sparse-built problems skip the dense Cholesky: the x-update
solves $(P + \sigma I + A^\top\mathrm{diag}(\rho)A)\tilde x = \text{rhs}$
matrix-free with Jacobi-preconditioned CG (cap 250 steps, relative tolerance
$10^{-10}$, warm-started from the previous $\tilde x$). Code:
`AdmmQpSolver.Solve` (`Masonry/Solvers/AdmmQpSolver.cs`, iteration at lines
210-347, CG at 349-405). Citation: Stellato, Banjac, Goulart, Bemporad, Boyd
(2020), "OSQP: an operator splitting solver for quadratic programs",
Mathematical Programming Computation 12:637-672.

### 4.3 Primal infeasibility certificate (why "cannot stand" is a verdict)

Over the residual-check window, with $\delta y = y^{+} - y$:

$$
\lVert A^\top \delta y\rVert_\infty \le \epsilon_{\mathrm{pinf}}\lVert\delta y\rVert_\infty
\quad\wedge\quad
u^\top(\delta y)_{+} + l^\top(\delta y)_{-} < -\epsilon_{\mathrm{pinf}}\lVert\delta y\rVert_\infty
\;\Rightarrow\; \text{primal infeasible},
$$

$\epsilon_{\mathrm{pinf}} = 10^{-4}$, rows with infinite bounds required to
have negligible $\delta y$. This turns the Guell-portico "grind to a
SolverError plateau" failure mode into an explicit verdict: no admissible
force state exists for this assembly/friction/bounds; the geometry cannot
stand as constructed. Code: `AdmmQpSolver.Solve` (certificate block, lines
268-313). Citation: Stellato et al. 2020, section 3.4.

### 4.4 Native OSQP marshal

`OsqpQpSolver` passes the same $(P, q, A_{eq}, b_{eq}, A_{ineq}, b_{ineq},
lb, ub)$ blocks to `frahan_osqp_solve` with $P$ as its upper triangle
(row-major flat), $\varepsilon_{abs}=10^{-6}$, $\varepsilon_{rel}=10^{-5}$,
6000 iterations, polish on. Status mapping: 1 SOLVED $\to$ Optimal;
2 SOLVED_INACCURATE $\to$ Inaccurate (deliberately NOT Optimal, so the
stability checkers never certify from a non-converged solve); -3 $\to$
Infeasible; -4 $\to$ Unbounded; -7 / other $\to$ SolverError. Dense marshal
only; sparse-built problems route to the managed ADMM. Code:
`OsqpQpSolver.Solve` (`Masonry/Solvers/OsqpQpSolver.cs`). Citation:
Stellato et al. 2020.

---

## 5. Physics settle termination (`BulletSettleService`)

The rigid-body dynamics equations are documented in the packing equation
layer (EQUATIONS.md section 2.5); only the equilibrium/termination behaviour
is stated here, as found. The service integrates a fixed budget, not an
energy criterion:

$$
g_z \in \{-0.5,\ -2,\ -5,\ g_{\text{full}}\}\ \text{(ramp, 250 sub-steps each)},
\qquad
\text{then } N_{\text{settle}} = 1500 \ \text{steps at}\ \Delta t = \tfrac{1}{600},
$$

followed by `TampRounds` densification bursts (gravity $3.5\,g_{\text{full}}$
for 200 steps, then $g_{\text{full}}$ for 400 steps). Solver: Bullet
sequential impulse, 80 iterations per step, friction 0.85, restitution 0,
linear/angular damping 0.4, convex-hull collision margin 0.0015 (the default
0.04 rounds contact faces and rolls stones off stable faces), dynamic mass
floored at $10^{-4}$, fixed stones static (mass 0, not lifted). Bodies rotate
about the caller-supplied volume COM (the hull vertex centroid is offset on
irregular stones and applies a false gravity torque). Containment check on
exit: a stone counts as settled iff its final COM height satisfies
$z > -0.3$. **Deviation note:** the packing equation layer describes settling
as "integration until kinetic energy $\to 0$ (bodies sleep)"; the shipping
service itself runs the full fixed step budget and never tests kinetic
energy. Bullet's internal sleeping thresholds may idle resting bodies within
the budget, but no explicit termination criterion exists in this code. Code:
`BulletSettleService.Settle` (`Masonry/Physics/BulletSettleService.cs`, ramp
and budget at lines 160-174). Citation: Bullet (sequential impulse); Coulomb
friction.

---

## 6. COM-over-support gates (Core stability pre-filters)

### 6.1 Support clearance (`RubbleWallSettle`)

The COM projected along gravity must lie inside the convex hull of the
contact footprint, with margin. With hull edges $(a, b)$ CCW and inward unit
normals $\hat n_{in} = \frac{(-e_y,\ e_x)}{\lVert e\rVert}$:

$$
\mathrm{clr}(c) = \min_{(a,b)\in\partial\,\mathrm{hull}}\ \hat n_{in}\cdot(c_{xy} - a),
\qquad
\text{stable} \iff \mathrm{clr}(c) \ge m,
$$

signed: positive inside (distance to the nearest edge), non-positive on or
outside (would topple), $-1$ for a degenerate support. The hull is Andrew's
monotone chain over the grid-cell contact points. Candidate seats that pass
the stability preference are ranked by the settle-v2 objective:

$$
J = w_1\left(1 - \frac{A_{\mathrm{hull}}}{A_{\mathrm{foot}}}\right)
+ w_3\,\frac{\lVert c_{xy} - g_{\mathrm{hull}}\rVert}{\bar W}
+ w_u\,\frac{V_{\mathrm{under}}}{A_{\mathrm{foot}}\, h}
+ w_s\,\frac{z_{\mathrm{seat}}}{\bar W},
$$

maximise the support polygon and minimise the COM offset from its centroid
$g_{\mathrm{hull}}$ (Furrer terms $w_1, w_3$), minimise the volume trapped
under the stone (Johns term), plus the legacy deep-seat preference; all terms
scale-normalised by footprint area, stone height $h$, and mean stone width
$\bar W$. Defaults $w_1 = w_3 = w_u = 1$, $w_s = 0.5$. Code:
`RubbleWallSettle.SupportClearance` / `SupportMetrics` / seat loop
(`Masonry/RubbleWallSettle.cs` lines 296-351, 520-568). Citations, per the
`[Algorithm]` attributes on `RubbleWallSettleComponent`: Heyman 1966
limit-state masonry (centre of thrust within the support); Furrer et al. 2017
(ICRA) support-polygon/COM-offset placement; Johns et al. 2020
(Construction Robotics) under-stone void.

### 6.2 NBO analytic gate (`NboPlanner.Gate`)

The cheap accept/reject before any physics, three conditions:

$$
\tilde m = \pm\,\frac{d_{\mathrm{edge}}}{\sqrt{A_{\mathrm{contact}}}},
\qquad
\text{stable} \iff
c_{xy}\in\mathrm{hull}(\mathrm{support})
\ \wedge\
\tilde m \ge m_{\min}
\ \wedge\
\frac{d_{\mathrm{span}}}{h_{\mathrm{span}}} \ge 0.5,
$$

where $d_{\mathrm{edge}}$ is the XY distance from the projected COM to the
nearest resting-face boundary edge (sign $+$ inside, $-$ outside), normalised
by the square root of the contact area so the margin is scale-free, and
$d_{\mathrm{span}}/h_{\mathrm{span}}$ is the placed depth-into-wall span over
the height span. Defaults $m_{\min} = 0$, $d/h \ge 0.5$. The resting face is
the convex-hull stable face (COM projects inside the face). Code:
`NboPlanner.Gate` / `ComOverSupportWorld` / `PlacedSpan`
(`Masonry/Nbo/NboPlanner.cs`). Citations, per the file header: stable-face
analysis Goldberg & Mirtich 1999; the $d/h \ge 0.5$ depth-into-wall rule is
the ETH dry-stone heuristic (Johns et al. 2020, "length into the wall").

---

## 7. Citation index (as attributed in the code)

- Kao, Iannuzzo, Thomaszewski, Coros, Van Mele, Block 2022, CAD 146:103216,
  DOI 10.1016/j.cad.2022.103216 (RBE QP, penalty Eq. 14, CRA Eqs. 8-14);
  reference implementation BlockResearchGroup/compas_cra (MIT).
  `[Algorithm]` on `MasonryStabilityRbeComponent`.
- Kao et al. 2021 (J Mech Des), named in `MasonryStabilityCheckComponent`
  description alongside the 2022 paper.
- Heyman 1966, limit-state masonry: COM-over-support (`[Algorithm]` on
  `RubbleWallSettleComponent`) and the stability-gate precedent on
  `BuildOrderStabilityStreamComponent` (with Kim et al. 2024 install order).
- Stellato et al. 2020, OSQP, Math. Prog. Comp. 12:637-672 (`AdmmQpSolver`
  header; infeasibility certificate section 3.4).
- Dykstra 1983; Boyle & Dykstra 1986 (`ManagedQpSolver`, legacy, out of the
  ladder).
- Furrer et al. 2017 (ICRA) and Johns et al. 2020 (Construction Robotics)
  (`RubbleWallSettle` v2 objective; NBO d/h rule).
- Goldberg & Mirtich 1999 (`NboPlanner` stable-face rest).
- Whiting 2009, "Procedurally-Assembled Stable Masonry": cited in this
  codebase only as a contact-detection precedent on
  `RobustAutoInterfacesComponent`, not as the equilibrium formulation.
