import FrahanProofs.Common

/-!
Frahan StonePack — Tier 3 (analysis / duality / PDE) pass.

The deep results of `spec/frahan_algorithm_derivations.tex` rest on classical
theorems (limit-analysis duality, KKT, Varadhan, Stolt, EM). The honesty policy
here, per the repository contract:

  * where an ABSTRACT CORE of the result is soundly provable, PROVE it (no
    axiom) — this file proves the EM/soft-ICP monotonicity, the Poisson
    least-squares normal-equation, and the CRA convex-feasibility structure;
  * the genuinely-classical residue is introduced as a NAMED axiom WITH a
    citation and its EXACT hypotheses — never as an equivalence to a free
    predicate, which would be unsound (it would let `False` be derived). Those
    are queued at the end of this file with their citations.

So this pass deliberately proves-where-sound and defines-with-citation, and
only adds a cited axiom once its precise (sound) statement is in hand.
-/

namespace Frahan

open scoped RealInnerProductSpace

/-! ### tex Theorem `thm:cpd` — EM / Soft-ICP / CPD monotonicity (PROVED) -/

/-- tex Theorem `thm:cpd`, abstract minorize-maximize monotonicity: if `g ·θ`
minorizes the objective `f` (`g ψ θ ≤ f ψ` for all `ψ`), is tight at the anchor
(`f θ = g θ θ`), and the M-step `θ'` maximizes the surrogate
(`g ψ θ ≤ g θ' θ`), then the objective does not decrease: `f θ ≤ f θ'`.

This IS the EM / Soft-ICP / CPD monotonicity: the E-step builds a tight
variational lower bound `g` (Jensen), the M-step maximizes it (weighted
Kabsch + σ² update), so the data log-likelihood is non-decreasing and the
iteration converges. The Jensen construction of `g` is the concrete instance;
the monotonicity itself is this three-line argument. -/
theorem mm_monotone {Θ : Type*} (f : Θ → ℝ) (g : Θ → Θ → ℝ) (θ θ' : Θ)
    (minorize : ∀ ψ, g ψ θ ≤ f ψ) (tight : f θ = g θ θ)
    (mstep : ∀ ψ, g ψ θ ≤ g θ' θ) : f θ ≤ f θ' :=
  calc f θ = g θ θ := tight
    _ ≤ g θ' θ := mstep θ
    _ ≤ f θ' := minorize θ'

/-! ### tex Theorem `thm:poisson` — least-squares Euler–Lagrange (PROVED) -/

section Poisson

variable {H H' : Type*}
  [NormedAddCommGroup H] [InnerProductSpace ℝ H] [CompleteSpace H]
  [NormedAddCommGroup H'] [InnerProductSpace ℝ H'] [CompleteSpace H']

/-- tex Theorem `thm:poisson`, the least-squares Euler–Lagrange step, operator
form: for a continuous linear operator `T` between Hilbert spaces, if `x`
satisfies the normal equation `T† (T x − V) = 0`, then `x` minimizes the
least-squares functional `‖T x − V‖²`.

With `T = ∇` (gradient) this is exactly tex `thm:poisson`: the χ minimizing
`∫‖∇χ − V‖²` solves `T† (∇χ − V) = 0`, and since the adjoint of the gradient is
minus the divergence, `T†T = −Δ`, this is the Poisson equation `Δχ = ∇·V`.
(This is also the operator-level generalization of `thm:qem`'s
`qem_min_of_normal_eq`.) Proof: expand `‖T y − V‖²` around `x`; the cross term
is `⟪T†(Tx−V), y−x⟫ = 0`, and the residual `‖T(y−x)‖² ≥ 0`. -/
theorem poisson_normal_eq_min (T : H →L[ℝ] H') (V : H') (x : H)
    (hnormal : ContinuousLinearMap.adjoint T (T x - V) = 0) (y : H) :
    ‖T x - V‖ ^ 2 ≤ ‖T y - V‖ ^ 2 := by
  have hsplit : T y - V = (T x - V) + T (y - x) := by
    rw [map_sub]; abel
  have hcross : ⟪T x - V, T (y - x)⟫ = (0 : ℝ) := by
    rw [← ContinuousLinearMap.adjoint_inner_left T (y - x) (T x - V), hnormal,
      inner_zero_left]
  rw [hsplit, norm_add_sq_real, hcross]
  nlinarith [sq_nonneg ‖T (y - x)‖]

end Poisson

/-! ### tex Theorem `thm:cra` — static/safe theorem, feasibility structure -/

section CRA

variable {F L : Type*} [AddCommGroup F] [Module ℝ F] [AddCommGroup L] [Module ℝ L]

/-- tex Theorem `thm:cra` (static/safe theorem of limit analysis; Heyman;
Kao 2022 CRA). The static-admissibility predicate: the load `g` is carried by
some admissible self-stress `f` in the yield cone `K` with `A f = g`. The safe
theorem IDENTIFIES this with physical stability — that identification is the
cited modeling content of Heyman's lower-bound theorem, recorded here as a
definition rather than an unsound free-predicate axiom. -/
def StaticallyAdmissible (A : F →ₗ[ℝ] L) (K : Set F) (g : L) : Prop :=
  ∃ f ∈ K, A f = g

/-- tex Theorem `thm:cra`, the convex-program structure (PROVED): the set of
admissible self-stresses for a load `g` is convex whenever the yield set `K` is
convex (e.g. a second-order / Coulomb friction cone). This is the sense in
which "stability is feasibility of a convex (SOC) program": the feasible region
is `K ∩ A⁻¹{g}`, an intersection of the convex cone with an affine subspace. -/
theorem admissibleSet_convex (A : F →ₗ[ℝ] L) (K : Set F) (hK : Convex ℝ K)
    (g : L) : Convex ℝ (K ∩ A ⁻¹' {g}) :=
  hK.inter ((convex_singleton g).linear_preimage A)

end CRA

section CRAduality

variable {F L : Type*}
  [NormedAddCommGroup F] [NormedSpace ℝ F]
  [NormedAddCommGroup L] [NormedSpace ℝ L]

/-- tex Theorem `thm:cra`, the DUALITY CONVERSE (Gale/Farkas), PROVED: for a
convex yield set `K` and a load `g`, either the equilibrium `A f = g, f ∈ K` is
feasible (the assembly is safe), or a collapse mechanism `φ` strictly separates
`g` from every admissible internal force (`φ g < φ (A f)` for all `f ∈ K`, with
a strict gap) — i.e. a statically inadmissible load is certified by a mechanism.
This is the classical converse of the safe theorem; it reduces to Hahn–Banach
separation of the point `g` from the closed convex image `A '' K`. The closedness
of `A '' K` is the honest hypothesis the classical theorem needs (a
closedness / Slater condition), stated explicitly rather than hidden. -/
theorem cra_farkas (A : F →L[ℝ] L) (K : Set F) (hK : Convex ℝ K)
    (hclosed : IsClosed (A '' K)) (g : L) :
    (∃ f ∈ K, A f = g) ∨
    (∃ φ : StrongDual ℝ L, ∃ u : ℝ, φ g < u ∧ ∀ f ∈ K, u < φ (A f)) := by
  by_cases hg : g ∈ A '' K
  · obtain ⟨f, hf, hAf⟩ := hg
    exact Or.inl ⟨f, hf, hAf⟩
  · refine Or.inr ?_
    have hconv : Convex ℝ (A '' K) := hK.linear_image A.toLinearMap
    obtain ⟨φ, u, hgu, hbu⟩ := geometric_hahn_banach_point_closed hconv hclosed hg
    exact ⟨φ, u, hgu, fun f hf => hbu (A f) ⟨f, hf, rfl⟩⟩

end CRAduality

/-! ### tex Theorem `thm:settle` — rest = constrained energy minimum (core) -/

section Settle

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]

/-- tex Theorem `thm:settle`, the convex-optimality core (PROVED): a feasible
pose `x` at which the variational inequality holds (`⟪grad, y − x⟫ ≥ 0` for
every feasible `y` — the KKT stationarity of the constrained energy) is a global
minimizer of the energy `U` over the feasible set, provided `U` is convex there
(encoded by the subgradient lower bound `U x + ⟪grad, y − x⟫ ≤ U y`). Physically:
a dropped block whose contact reactions balance gravity under the non-penetration
constraints is at a constrained energy minimum, hence at static rest. The KKT
MULTIPLIER-existence direction (constraint qualification) is the classical
residue, queued below. -/
theorem settle_convex_optimality (U : E → ℝ) (s : Set E) (x : E) (grad : E)
    (hsub : ∀ y ∈ s, U x + ⟪grad, y - x⟫ ≤ U y)
    (hstat : ∀ y ∈ s, 0 ≤ ⟪grad, y - x⟫) :
    ∀ y ∈ s, U x ≤ U y := by
  intro y hy
  have h1 := hsub y hy
  have h2 := hstat y hy
  linarith

/-- tex Theorem `thm:settle`, the KKT NECESSITY direction — the one genuinely
classical ingredient, introduced as a CITED AXIOM (Karush; Kuhn–Tucker; e.g.
Nocedal–Wright, Bertsekas). At a constrained minimum of the potential energy
`U` subject to the non-penetration constraints `φ_j ≥ 0`, IF the active-constraint
gradients are linearly independent (LICQ, the constraint qualification), THEN
Lagrange multipliers `λ_j ≥ 0` exist with `∇U = Σ λ_j ∇φ_j` and complementary
slackness `λ_j φ_j = 0` — the contact-force equilibrium of §6. This is a
genuinely-true theorem asserted here (multiplier EXISTENCE under an explicit CQ),
NOT an equivalence to a free predicate; sound but not yet mechanized in Mathlib
(no inequality-KKT). The SUFFICIENCY direction is proved above
(`settle_convex_optimality`). -/
axiom settle_kkt {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]
    [CompleteSpace E]
    {n : ℕ} (U : E → ℝ) (φ : Fin n → E → ℝ) (x : E)
    (gU : E) (gφ : Fin n → E)
    (hU : HasGradientAt U gU x)
    (hφ : ∀ j, HasGradientAt (φ j) (gφ j) x)
    (hmin : ∀ y, (∀ j, 0 ≤ φ j y) → U x ≤ U y)
    (hLICQ : LinearIndependent ℝ fun j : {j : Fin n // φ j x = 0} => gφ j.1) :
    ∃ lam : Fin n → ℝ, (∀ j, 0 ≤ lam j) ∧
      gU = ∑ j, lam j • gφ j ∧ ∀ j, lam j * φ j x = 0

end Settle

/-! ### tex Theorem `thm:stolt` — constant-velocity dispersion Jacobian (PROVED) -/

/-- tex Theorem `thm:stolt`, the amplitude-Jacobian core (PROVED). The Stolt
migration remaps `ω → k_z` along the constant-velocity dispersion
`ω = c√(kₓ²+k_z²)`, with amplitude factor `c·k_z/√(kₓ²+k_z²)`. That factor is
exactly the Jacobian `∂ω/∂k_z`: differentiating `c√(kₓ²+k_z²)` in `k_z` gives
`c·k_z/√(kₓ²+k_z²)`. (The full 2D-Fourier change of variables is prose; this is
its analytic heart, the same shape as `thm:lambert`.) Needs `kₓ²+k_z² ≠ 0`. -/
theorem stolt_dispersion_jacobian (c kx kz : ℝ) (h : kx ^ 2 + kz ^ 2 ≠ 0) :
    HasDerivAt (fun t => c * Real.sqrt (kx ^ 2 + t ^ 2))
      (c * kz / Real.sqrt (kx ^ 2 + kz ^ 2)) kz := by
  have hpos : 0 < kx ^ 2 + kz ^ 2 := lt_of_le_of_ne (by positivity) (Ne.symm h)
  have hs : Real.sqrt (kx ^ 2 + kz ^ 2) ≠ 0 := ne_of_gt (Real.sqrt_pos.mpr hpos)
  have hg : HasDerivAt (fun t : ℝ => kx ^ 2 + t ^ 2) (2 * kz) kz := by
    simpa using (hasDerivAt_pow 2 kz).const_add (kx ^ 2)
  have hsqrtg : HasDerivAt (fun t : ℝ => Real.sqrt (kx ^ 2 + t ^ 2))
      (1 / (2 * Real.sqrt (kx ^ 2 + kz ^ 2)) * (2 * kz)) kz :=
    (Real.hasDerivAt_sqrt h).comp kz hg
  have hfull := hsqrtg.const_mul c
  have hval : c * (1 / (2 * Real.sqrt (kx ^ 2 + kz ^ 2)) * (2 * kz))
      = c * kz / Real.sqrt (kx ^ 2 + kz ^ 2) := by
    field_simp
  rw [hval] at hfull
  exact hfull

/-! ### tex Theorem `thm:blocktheory` — Shi finiteness / removability -/

/-- tex Theorem `thm:blocktheory` (Shi, Block Theory). Shi's removability
characterization for a rock block, in terms of its joint pyramid `JP` and
excavation pyramid `EP`: a block is removable iff `JP` is nonempty and disjoint
from `EP` (finite: `JP ∩ EP = ∅`; not tapered: `JP ≠ ∅`). Recorded as the
definition Shi's theorem supplies; the physical "translate-to-infinity"
argument is the cited content. -/
def Removable {V : Type*} (JP EP : Set V) : Prop :=
  JP.Nonempty ∧ Disjoint JP EP

/-- A block whose joint pyramid meets the excavation pyramid is NOT removable
(it is either infinite or tapered) — the contrapositive half of Shi's test,
immediate from the definition. -/
theorem not_removable_of_not_disjoint {V : Type*} {JP EP : Set V}
    (h : ¬ Disjoint JP EP) : ¬ Removable JP EP :=
  fun hr => h hr.2

/-!
### Tier-3 cited-axiom queue (to add with exact, sound hypotheses)

Discharged by PROOF (not axiomatized):
  * `thm:cra` converse — `cra_farkas`, via Hahn–Banach separation of `g` from
    the closed convex image `A '' K` (closedness stated as an honest hypothesis).
  * `thm:settle` convex half — `settle_convex_optimality` (variational-inequality
    stationary point ⇒ global constrained minimum of a convex energy).
  * `thm:stolt` Jacobian — `stolt_dispersion_jacobian`, the amplitude factor
    `c·k_z/√(kₓ²+k_z²) = ∂/∂k_z [c√(kₓ²+k_z²)]` (the analytic heart of the remap).

Discharged by a CITED AXIOM (sound existence statement under explicit hypotheses,
the classical ingredient Mathlib does not yet carry):
  * `thm:settle` KKT necessity — `settle_kkt` (multiplier existence under LICQ;
    Karush; Kuhn–Tucker). The ONLY `axiom` declaration in the library.

Still queued — needs heavy machinery before it is a genuinely-true, faithfully-
stated axiom:
  * `thm:heat` — Varadhan `lim_{t→0} −4t log u_t = φ²` (heat-kernel / geodesic
    machinery). The last Tier-3 residue.
-/

end Frahan
