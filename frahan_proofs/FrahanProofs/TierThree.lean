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

Not yet stated as axioms — each needs its precise hypotheses so the axiom is a
genuinely-true classical statement, not an unsound shortcut:

  * `thm:cra` converse — Gale/Farkas cone duality: if `A f = g, f ∈ K` is
    infeasible then a collapse mechanism separates it. Sound only for a CLOSED
    convex cone `K` with a closedness/Slater condition; to be stated with those.
  * `thm:settle` — KKT characterization of the constrained energy rest point
    (`∇U = Σ λⱼ ∇φⱼ`, `λⱼ ≥ 0`, `λⱼφⱼ = 0`), citing standard NLP under a
    constraint qualification. The convex "stationary ⇒ global min" half is
    provable and mirrors `poisson_normal_eq_min`.
  * `thm:stolt` — constant-velocity dispersion remap `ω = c√(kₓ²+k_z²)` with the
    `c·k_z/√(kₓ²+k_z²)` amplitude Jacobian (Fourier machinery).
  * `thm:heat` — Varadhan `lim_{t→0} −4t log u_t = φ²` (heat-kernel / geodesic
    machinery).
-/

end Frahan
