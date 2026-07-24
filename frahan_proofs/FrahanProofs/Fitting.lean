import FrahanProofs.Common

/-!
Frahan StonePack — least-squares dip-plane existence/uniqueness.

Mechanizes tex Lemma `lem:plane` (§"Regression kriging of a fracture bed")
of `spec/frahan_algorithm_derivations.tex`. The tex writes the fit through
the normal-equations Gram matrix
`M = Σᵢ p̃ᵢ p̃ᵢᵀ ⪰ 0`, with `p̃ᵢ = (xᵢ, yᵢ, 1)` the homogeneous picks, and
argues: `uᵀMu = Σᵢ (p̃ᵢᵀu)² = 0` iff every `pᵢ` lies on the line
`u₁x + u₂y + u₃ = 0`; non-collinearity forbids this for `u ≠ 0`, so
`M ≻ 0` and `θ = M⁻¹b` is unique.

This file gives the coordinate-free quadratic-form version, which is
cleaner and avoids materializing the 3×3 matrix. Over a real
inner-product space `E` with a finite indexed family `v : ι → E` (the
`p̃ᵢ`), set `Q u = Σᵢ ⟪vᵢ, u⟫²`. Then:

  * `gram_quad_nonneg`      : `0 ≤ Q u`                   (M ⪰ 0).
  * `gram_quad_eq_zero_iff` : `Q u = 0 ↔ ∀ i, ⟪vᵢ,u⟫ = 0` (uᵀMu = 0 iff
                              every pick lies on the line `u`).
  * `gram_posDef_iff_span`  : `(∀ u, Q u = 0 → u = 0) ↔ span ℝ {vᵢ} = ⊤`
                              — the coordinate-free form of `M ≻ 0 ⟺ the
                              `p̃ᵢ` are not all collinear (they span)`.

The `E = ℝ³` instance recovers the tex statement: the homogeneous picks
span `ℝ³` exactly when the planimetric `pᵢ` are not collinear, and that
is exactly when `M ≻ 0` and the dip plane `θ` is unique.

Nothing here uses `sorry` or introduces an axiom. Fit UNIQUENESS is PROVED
(`ls_fit_unique`): positive-definite Gram form ⇒ any two least-squares
minimizers coincide (parallelogram identity + `gramQuad`). The concrete matrix
`M = Σ p̃p̃ᵀ` and its closed form `θ = M⁻¹b` are the basis-dependent restatement,
left as prose.
-/

open scoped RealInnerProductSpace

namespace Frahan

section Fitting

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]
variable {ι : Type*} [Fintype ι]

/-- The Gram quadratic form of a finite family `v : ι → E` (tex `lem:plane`,
`uᵀMu` for `M = Σᵢ vᵢ vᵢᵀ`): `Q u = Σᵢ ⟪vᵢ, u⟫²`, the sum of squared
projections of `u` onto the family. In the tex, `vᵢ = p̃ᵢ = (xᵢ, yᵢ, 1)`
is the homogeneous pick and `⟪p̃ᵢ, u⟫ = u₁xᵢ + u₂yᵢ + u₃`. -/
noncomputable def gramQuad (v : ι → E) (u : E) : ℝ := ∑ i, ⟪v i, u⟫ ^ 2

/-- tex `lem:plane`, `M ⪰ 0`: the Gram form is a sum of squares, hence
nonnegative for every `u`. -/
theorem gram_quad_nonneg (v : ι → E) (u : E) : 0 ≤ gramQuad v u :=
  Finset.sum_nonneg fun _ _ => sq_nonneg _

/-- tex `lem:plane`, the isotropy characterization: `uᵀMu = Σᵢ (p̃ᵢᵀu)² = 0`
iff every pick is annihilated by `u`, i.e. lies on the line
`u₁x + u₂y + u₃ = 0`. A finite sum of squares of reals vanishes iff each
term does. -/
theorem gram_quad_eq_zero_iff (v : ι → E) (u : E) :
    gramQuad v u = 0 ↔ ∀ i, ⟪v i, u⟫ = 0 := by
  rw [gramQuad, Finset.sum_eq_zero_iff_of_nonneg fun _ _ => sq_nonneg _]
  simp only [Finset.mem_univ, true_implies, sq_eq_zero_iff]

/-- tex `lem:plane`, the core equivalence `M ≻ 0 ⟺ non-collinear`, stated
coordinate-free: the Gram form is positive definite (`Q u = 0 ⇒ u = 0`)
iff the family `v` spans the whole space. In the tex `E = ℝ³` model the
homogeneous picks `p̃ᵢ` span `ℝ³` exactly when the planimetric `pᵢ` are
not all collinear, so this says `M ≻ 0 ⟺ the picks are not all
collinear`, and then the dip plane `θ = M⁻¹b` is the unique minimizer
(see `ls_fit_unique`).

Direction `⇐` (span ⇒ positive definite): if `⟪vᵢ, u⟫ = 0` for all `i`
then every `vᵢ` is orthogonal to `u`, so `span ℝ {vᵢ} ≤ (ℝ ∙ u)ᗮ`; since
the family spans `⊤`, `u ∈ (ℝ ∙ u)ᗮ`, i.e. `⟪u, u⟫ = 0`, so `u = 0`.

Direction `⇒` (positive definite ⇒ span): contrapositive. If the family
does not span, its span `K` (finite-dimensional, as `ι` is finite) has a
nontrivial orthogonal complement `Kᗮ ≠ ⊥`; a nonzero `u ∈ Kᗮ` is
orthogonal to every `vᵢ`, so `Q u = 0` while `u ≠ 0`, contradicting
positive definiteness. -/
theorem gram_posDef_iff_span (v : ι → E) :
    (∀ u, gramQuad v u = 0 → u = 0) ↔ (Submodule.span ℝ (Set.range v) = ⊤) := by
  constructor
  · -- positive definite ⇒ span = ⊤ (contrapositive via orthogonal complement)
    intro H
    by_contra hne
    haveI : FiniteDimensional ℝ (Submodule.span ℝ (Set.range v)) :=
      FiniteDimensional.span_of_finite ℝ (Set.finite_range v)
    have hbot : (Submodule.span ℝ (Set.range v))ᗮ ≠ ⊥ := by
      rw [Ne, Submodule.orthogonal_eq_bot_iff]
      exact hne
    obtain ⟨u, hu_mem, hu_ne⟩ := Submodule.exists_mem_ne_zero_of_ne_bot hbot
    refine hu_ne (H u ?_)
    rw [gram_quad_eq_zero_iff]
    intro i
    exact Submodule.inner_right_of_mem_orthogonal
      (Submodule.subset_span (Set.mem_range_self i)) hu_mem
  · -- span = ⊤ ⇒ positive definite
    intro hspan u hu
    have hzero : ∀ i, ⟪v i, u⟫ = 0 := (gram_quad_eq_zero_iff v u).mp hu
    have hsub : Set.range v ⊆ ↑((ℝ ∙ u)ᗮ) := by
      rintro w ⟨i, rfl⟩
      rw [SetLike.mem_coe, Submodule.mem_orthogonal_singleton_iff_inner_left]
      exact hzero i
    have hle : Submodule.span ℝ (Set.range v) ≤ (ℝ ∙ u)ᗮ := Submodule.span_le.mpr hsub
    rw [hspan] at hle
    have huu : u ∈ (ℝ ∙ u)ᗮ := hle Submodule.mem_top
    rw [Submodule.mem_orthogonal_singleton_iff_inner_left] at huu
    exact inner_self_eq_zero.mp huu

/-- tex `lem:plane`, uniqueness conclusion (`θ = M⁻¹b` unique). When the
Gram form is positive definite (equivalently, the picks are not all
collinear — `gram_posDef_iff_span`), the least-squares dip plane `θ`
minimizing `Σᵢ (⟪p̃ᵢ, θ⟫ − dᵢ)²` is unique: the positive-definite Gram
form makes the objective strictly convex, so any two global minimizers
coincide. Stated but not yet proved — the strict-convexity ⇒ unique-argmin
step and the closed form `M⁻¹b` (existence via the finite-dimensional
normal-equations solve) are pending. -/
theorem ls_fit_unique (p : ι → E) (d : ι → ℝ)
    (hpd : ∀ u, gramQuad p u = 0 → u = 0) (θ θ' : E)
    (hθ : ∀ w, ∑ i, (⟪p i, θ⟫ - d i) ^ 2 ≤ ∑ i, (⟪p i, w⟫ - d i) ^ 2)
    (hθ' : ∀ w, ∑ i, (⟪p i, θ'⟫ - d i) ^ 2 ≤ ∑ i, (⟪p i, w⟫ - d i) ^ 2) :
    θ = θ' := by
  -- It suffices to show the difference `θ − θ'` is annihilated by the Gram
  -- form, since `hpd` then forces it to be `0`.
  have hzero : θ - θ' = 0 := by
    apply hpd
    -- `gramQuad p (θ−θ') ≥ 0` always; show `≤ 0`, hence `= 0`.
    refine le_antisymm ?_ (gram_quad_nonneg p _)
    rw [gramQuad]
    -- Midpoint of the two minimizers.
    set m : E := (2⁻¹ : ℝ) • (θ + θ') with hm
    -- Both are global minima, so they attain the same objective value.
    have hFeq : (∑ i, (⟪p i, θ⟫ - d i) ^ 2) = ∑ i, (⟪p i, θ'⟫ - d i) ^ 2 :=
      le_antisymm (hθ θ') (hθ' θ)
    -- Parallelogram identity for the least-squares objective:
    -- `F(m) = ½·F(θ) + ½·F(θ') − ¼·∑ᵢ ⟪pᵢ, θ−θ'⟫²`.
    have key : (∑ i, (⟪p i, m⟫ - d i) ^ 2)
        = (∑ i, (⟪p i, θ⟫ - d i) ^ 2) / 2
          + (∑ i, (⟪p i, θ'⟫ - d i) ^ 2) / 2
          - (1 / 4) * ∑ i, ⟪p i, θ - θ'⟫ ^ 2 := by
      rw [Finset.sum_div, Finset.sum_div, Finset.mul_sum,
        ← Finset.sum_add_distrib, ← Finset.sum_sub_distrib]
      refine Finset.sum_congr rfl fun i _ => ?_
      have h1 : ⟪p i, m⟫ = 2⁻¹ * (⟪p i, θ⟫ + ⟪p i, θ'⟫) := by
        rw [hm, real_inner_smul_right, inner_add_right]
      have h2 : ⟪p i, θ - θ'⟫ = ⟪p i, θ⟫ - ⟪p i, θ'⟫ := by
        rw [inner_sub_right]
      rw [h1, h2]; ring
    -- `θ` is a minimizer, so `F(θ) ≤ F(m)`. With the identity and `F(θ)=F(θ')`
    -- this pins `∑ᵢ ⟪pᵢ, θ−θ'⟫² ≤ 0`.
    have hmin : (∑ i, (⟪p i, θ⟫ - d i) ^ 2) ≤ ∑ i, (⟪p i, m⟫ - d i) ^ 2 := hθ m
    linarith [hFeq, key, hmin]
  exact sub_eq_zero.mp hzero

end Fitting

end Frahan
