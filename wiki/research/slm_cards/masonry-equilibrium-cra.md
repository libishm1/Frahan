---
slug: masonry-equilibrium-cra
title: Masonry Rigid-Block Equilibrium (CRA/RBE) convex QP with managed Dykstra solver
algorithm_class: Rigid-block equilibrium / limit-state stability as convex QP
core_method_class: Kao 2022 CRA-RBE (CAD 146:103216); Heyman 1966 limit state; Coulomb friction pyramid; Dykstra 1983 / Boyle-Dykstra 1986 alternating projections
fabrication_direction: bottom_up
geometry_type: polyhedral block assembly, planar vertex-sampled contact interfaces
big_o_time: closed-form path O(m_eq^2 n + m_eq^3); Dykstra fallback O(maxIter*(m_eq n + m_ineq n))
big_o_space: O(m_eq n) dense (densified Aeq) + O(m_eq^2) Cholesky + O(nnz) COO
parallel_model: serial
verdict: evolve
gotcha_flags: [T1, T3, T5, G2, G4, M2, P5]
source_files:
  - Frahan.StonePack.Core/Masonry/Solvers/ManagedQpSolver.cs
  - Frahan.StonePack.Core/Masonry/Solvers/RbeQpFormulation.cs
  - Frahan.StonePack.Core/Masonry/Equilibrium/EquilibriumMatrixBuilder.cs
  - Frahan.StonePack.Core/Masonry/Equilibrium/FrictionConeBuilder.cs
  - Frahan.StonePack.Core/Masonry/Equilibrium/BlockCenterOfMass.cs
  - Frahan.StonePack.Core/Masonry/Equilibrium/SparseMatrixCoo.cs
  - Frahan.StonePack.Core/Masonry/Solvers/DenseLinAlg.cs
---

# Masonry Rigid-Block Equilibrium (CRA/RBE) convex QP

## What the code actually computes

The pipeline is: assembly -> `EquilibriumMatrixBuilder.Build` (Aeq, b)
-> optional `FrictionConeBuilder.Build` (Afr) -> `RbeQpFormulation.Build`
(maps to a generic `ConvexQpProblem`) -> `ManagedQpSolver.Solve`. The
solver returns the minimum-norm contact-force vector consistent with
static equilibrium and friction; feasibility is the Heyman/CRA stability
verdict. Method-class origin only: Kao et al. 2022 CRA-RBE and Heyman
1966; the equations below are derived from the C# code, not copied.

## Derived equations (from the code)

Per free block, force + moment balance summed over incident interfaces
and contact vertices (`EquilibriumMatrixBuilder.AddForceAndMoment`,
EquilibriumMatrixBuilder.cs:201-219). For contact vertex $k$ with world
normal $\mathbf{n}$ and tangents $\mathbf{t}_1,\mathbf{t}_2$, scalar
unknowns $f_{n,k}, f_{t1,k}, f_{t2,k}$, contact point $\mathbf{r}_k$, and
block centroid $\mathbf{c}$:

$$
\sum_{k} s\,\big(f_{n,k}\mathbf{n}+f_{t1,k}\mathbf{t}_1+f_{t2,k}\mathbf{t}_2\big)\;+\;\mathbf{W}=\mathbf{0}
$$

$$
\sum_{k} s\,(\mathbf{r}_k-\mathbf{c})\times\big(f_{n,k}\mathbf{n}+f_{t1,k}\mathbf{t}_1+f_{t2,k}\mathbf{t}_2\big)\;+\;\mathbf{M}_W=\mathbf{0}
$$

with sign $s=+1$ for block A, $s=-1$ for block B (the interface normal
points A into B; EquilibriumMatrixBuilder.cs:113-121). The moment cross
product is the literal `mx,my,mz` at EquilibriumMatrixBuilder.cs:213-215.
This assembles 6 rows per free block (3 force, 3 moment) into sparse
$A_{eq}$. The load is gravity only:

$$
\mathbf{W}=(0,\,0,\,\rho\,V\,g_z),\qquad g_z=-9.80665,\qquad \mathbf{M}_W=\mathbf{0}
$$

written into `b[rowBase+2] = density*volume*gravityZ`
(EquilibriumMatrixBuilder.cs:132-137); the moment of gravity about the
COM is taken as exactly zero. Volume and centroid come from a signed-
tetrahedron (divergence) decomposition,
$V=\tfrac16\sum_f \mathbf{a}_f\cdot(\mathbf{b}_f\times\mathbf{c}_f)$,
$\mathbf{c}=\tfrac{1}{24V}\sum_f (\mathbf{a}\cdot(\mathbf{b}\times\mathbf{c}))(\mathbf{a}+\mathbf{b}+\mathbf{c})$
(BlockCenterOfMass.cs:60-94), falling back to vertex mean when
$|V|<10^{-12}$.

`RbeQpFormulation.Build` (RbeQpFormulation.cs:54) maps this to

$$
\min_{\tilde f}\ \tfrac12\,\tilde f^{\top}H\tilde f
\quad\text{s.t.}\quad A_{eq}\tilde f = -b,\ \ A_{fr}\tilde f \le 0,\ \ f_n\ge 0
$$

$H$ is diagonal: $H_{ii}=c$ on normal columns, $H_{ii}=c\,\tau$ on
tangent columns ($c$=hessianScale, $\tau$=tangentialScale; defaults
$1$, paper hint $\tau\approx10^3$; RbeQpFormulation.cs:79-100). Linear
term is zero, so the unconstrained minimiser is $\tilde f=0$ and the QP
is the $H$-norm projection of the origin onto the feasible set. Box
bounds: $f_n\in[0,\infty)$ (compression only), tangents free
(RbeQpFormulation.cs:130-155). RHS sign is $-b$; the documented
correction $\,+b\,$ lives in `BuildPhysicsCorrected`
(RbeQpFormulation.cs:193-212).

Friction: the true Coulomb cone $\sqrt{f_{t1}^2+f_{t2}^2}\le\mu f_n$ is
linearised to a $K$-face pyramid (FrictionConeBuilder.cs:105). Each row:

$$
\cos\theta_k\,f_{t1}+\sin\theta_k\,f_{t2}-\mu f_n\le 0,\qquad \theta_k=\tfrac{2\pi k}{K}
$$

$K=4$ uses exact $\{\pm1,0\}$ coefficients (FrictionConeBuilder.cs:201-213),
$\mu$ default $0.84$ (~40 deg). In penalty mode the normal splits into
$f_n^+ - f_n^-$ (coeffs $-\mu,+\mu$; FrictionConeBuilder.cs:234-236).

Solver (ManagedQpSolver.cs): for diagonal $H$, zero linear cost, no
inequalities, the equality-only QP has the closed form

$$
\tilde f = H^{-1}A_{eq}^{\top}\big(A_{eq}H^{-1}A_{eq}^{\top}\big)^{-1}\,b_{eq}
$$

via Cholesky of $K=A_{eq}H^{-1}A_{eq}^{\top}$ (ManagedQpSolver.cs:285-318);
accepted only if it satisfies the bounds. Otherwise (and always when
$A_{fr}$ is present) it falls to Dykstra alternating projection onto the
affine equality set, half-spaces, and box (ManagedQpSolver.cs:154-199),
with the equality projection
$x \leftarrow x - A_{eq}^{\top}(A_{eq}A_{eq}^{\top})^{-1}(A_{eq}x-b_{eq})$
(ManagedQpSolver.cs:358-370).

## Code sketch / reuse seam

`IConvexQpSolver` is the seam: `ManagedQpSolver` is the managed default;
comments name `IIpoptSolver` / a SOCP backend as the drop-in for full
RBE Hessians and the true second-order cone (ManagedQpSolver.cs:32,
RbeQpFormulation.cs:28). Matrix assembly (`EquilibriumMatrixBuilder`,
`FrictionConeBuilder`) is solver-agnostic and pure-managed (no Rhino),
so the formation layer is directly reusable; only the solver back-end
needs swapping.

## Gotchas (G/M/N/P/T)

| Code | Verdict | Reason (file:line) |
|------|---------|--------------------|
| G1 exact/adaptive predicates | flag | No exact/adaptive predicates; all geometry is raw float64 dot/cross (EquilibriumMatrixBuilder.cs:213, BlockCenterOfMass.cs:71). |
| G2 degeneracy handled | flag | Only volume degeneracy is caught (`|V|<1e-12` fallback, BlockCenterOfMass.cs:84); collinear/zero-area contact polygons and coincident vertices are not screened before building moment rows. |
| G3 predicate/construction sep | flag | None; construction (cross products) and the implicit feasibility predicate are entangled in one float pass. |
| G4 robust boolean kernel | flag | No boolean kernel here, but the home-rolled dense Cholesky with fixed `EqualityRegularization=1e-12` (ManagedQpSolver.cs:39, DenseLinAlg.cs:24) is the robustness-critical float path and is not scale-aware. |
| M1 manifold output | na | Solver outputs a force vector, not a mesh. |
| M2 watertight stated | flag | COM/volume correctness silently assumes closed, outward-oriented triangulations; openness only surfaces as the volume fallback, not validated (BlockCenterOfMass.cs:38-95). |
| M3 winding/self-intersection | na | No mesh produced; winding only matters as the COM orientation assumption (covered by M2). |
| M4 bounded decimation | na | No remesh/decimation. |
| M5 hidden data-structure cost | flag | `SparseMatrixCoo.ToDense` (SparseMatrixCoo.cs:57) densifies Aeq/Afr into O(m_eq*n) before solve; hidden dense blow-up on large assemblies. |
| N1-N6 NURBS | na | No NURBS/splines; planar polygonal contacts only. |
| P1 deterministic reductions | pass | Serial fixed-order loops; reductions are deterministic (no parallel sum). |
| P2 thread safety | pass | No shared mutable geometry across threads; everything serial. |
| P3 GPU memory | na | No GPU. |
| P4 load balance | na | No parallelism to balance. |
| P5 serial tail named | flag | Entire solve is a serial Dykstra loop up to 500 iters (ManagedQpSolver.cs:154); this is the named Amdahl tail and the scaling limit. |
| T1 recenter before far-from-origin | flag | Moment rows recenter (r - c, EquilibriumMatrixBuilder.cs:173-175) but force rows and the COM/volume integral use raw world coords (BlockCenterOfMass.cs:66-79); no assembly-level recentering, so far-from-origin inputs degrade conditioning of A_eq A_eq^T. |
| T2 abs vs scale-relative eps | flag | All tolerances are hard absolute: Tolerance 1e-8, IdentityHessianTol 1e-9, EqualityRegularization 1e-12, BoundsSatisfied tol 1e-7 (ManagedQpSolver.cs:37-39,334); none scale with coordinate magnitude or force units. |
| T3 float32 vs float64 | pass-with-flag | All float64 (good), but mixed-magnitude entries (force ~1, moment ~coord*coord) in one matrix erode the effective float64 precision at architectural scale (EquilibriumMatrixBuilder.cs:208-218). |
| T4 units declared+consistent | flag | gravity in m/s^2 (EquilibriumMatrixBuilder.cs:44) and density*volume assumed SI, but assembly coordinate units are never declared/checked; a mm model silently mixes mm geometry with m/s^2 gravity. |
| T5 tolerance-system count | flag | At least four absolute epsilons plus the dual sign convention (Build vs BuildPhysicsCorrected, RbeQpFormulation.cs:174-212); the Frahan standing tolerance-confusion flag applies. |
| T6 int64 overflow (Clipper2) | na | No Clipper2/integer scaling in this path. |
| T7 snap-rounding | na | No snap-rounding stage. |

## Numeric stress findings

- Coordinate magnitude (T1/T3): force-balance entries are O(1) basis
  components; moment entries are O(|r-c|) and the gravity load is
  O(density*volume) ~ O(coord^3) (EquilibriumMatrixBuilder.cs:132,213).
  $A_{eq}A_{eq}^{\top}$ therefore spans a wide magnitude range and its
  Cholesky (DenseLinAlg.cs:24) loses digits before the fixed 1e-12
  regularizer can help. No recentering of the whole assembly.
- Epsilon kind (T2/T5): every threshold is absolute and unitless; none
  derive from problem scale or RhinoDoc tolerance. Convergence test
  `resEq <= 1e-8` (ManagedQpSolver.cs:190) is meaningless if forces are
  in kN vs N.
- Units (T4): gravity is hard SI; geometry units undeclared. A
  millimetre assembly produces weights off by 1e9 vs a metre assembly
  with identical numbers.
- Overflow: no integer scaling, so no int64 overflow; the risk is
  float conditioning, not overflow.
- Friction relaxation: K=4 pyramid is inscribed under the cone
  (FrictionConeBuilder.cs:201), so the stability verdict is optimistic
  on tangential capacity along cone diagonals (under-constrains by up to
  ~sqrt(2)); not conservative for a Heyman no-sliding guarantee.
- Known feasibility bug: documented at RbeQpFormulation.cs:174-191 -
  `Build` yields f_n = -m*g against lowerBounds=0, making real assemblies
  infeasible; only `BuildPhysicsCorrected` is correct end-to-end.
- Solver convergence: comments at ManagedQpSolver.cs:91 admit Dykstra
  has known trouble on the 6-DOF RBE family; the closed-form fast path
  is skipped whenever friction inequalities are present
  (InequalityRowCount>0, ManagedQpSolver.cs:93), so the production
  (with-friction) path always takes the weak iterative route.

## Verdict: evolve

The formation layer (Aeq, Afr, COM) is a faithful, reusable managed
port of the Kao 2022 CRA-RBE structure and is worth keeping. The
solver and numeric hygiene are not production-grade: a polyhedral cone,
a Dykstra solver with documented convergence trouble and a 500-iter
serial tail, four uncoordinated absolute epsilons, no assembly
recentering, undeclared units, and a still-live dual sign convention.
Evolve the solver back-end and numeric conditioning; do not reject the
matrix builders.

## Evolution plan

- PERFORMANCE: keep Aeq/Afr sparse (CSR), add sparse MatVec/MatTVec and
  a sparse Schur formation of K = Aeq H^-1 Aeq^T; eliminate
  `ToDense` (RbeQpFormulation.cs:106, SparseMatrixCoo.cs:57) so memory
  drops from O(m_eq*n) to O(nnz) and the O(m_eq^2 n) build exploits the
  36-nnz-per-contact structure.
- ACCURACY: recenter the assembly to its centroid before
  `EquilibriumMatrixBuilder.Build` and column-equilibrate (Ruiz) Aeq
  before Cholesky (addresses T1/T3/G4 and the 1e-12 regularizer being
  swamped, ManagedQpSolver.cs:39,128). Default the friction pyramid to
  K=8/16 and document K=4 as optimistic (FrictionConeBuilder.cs:201).
- SPEED: replace Dykstra in the inequality case with an active-set QP or
  a real SOCP/IPOPT backend through the existing IConvexQpSolver seam
  (ManagedQpSolver.cs:32), keeping the true second-order Coulomb cone;
  this removes the P5 serial tail and the always-iterative friction path
  (ManagedQpSolver.cs:93).
- CORRECTNESS (prereq): unify the sign convention end-to-end and delete
  the legacy `Build` RHS path (RbeQpFormulation.cs:174-212) to close the
  T5 two-convention confusion.
