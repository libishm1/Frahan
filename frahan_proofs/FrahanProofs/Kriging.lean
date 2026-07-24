import FrahanProofs.Common

/-!
Frahan StonePack — regression-kriging exactness (interpolation).

Mechanizes the `τ = 0` core of tex Theorem `thm:nugget` ("Exactness vs.
smoothing", §"Regression kriging of a fracture bed") of
`spec/frahan_algorithm_derivations.tex`.

Setup (tex): a fracture bed is fit by regression kriging. The predictor at
a query `q` is `d̂(q) = π(q) + k(q)ᵀ α` with polynomial trend `π`, residuals
`rᵢ = dᵢ − π(pᵢ)`, Gaussian-covariance cross-vector `k(q)ᵢ = σ²exp(−‖q−pᵢ‖²/ρ²)`,
Gram matrix `Kᵢⱼ = C(‖pᵢ−pⱼ‖)`, and weights `α = K⁻¹ r`. With nugget `τ = 0`
and distinct points, `K` is the SPD Gaussian Gram matrix, so it is invertible
and evaluating at a training node `pⱼ` makes the cross-vector `k(pⱼ)` equal to
the `j`-th column of `K`. The tex proof is the identity
`k(pⱼ)ᵀ K⁻¹ = eⱼᵀ` (row `j` of `K K⁻¹ = I`), giving `d̂(pⱼ) = π(pⱼ) + rⱼ = dⱼ`.

Formalized here (PROVED, no sorry), over `K : Matrix (Fin n) (Fin n) ℝ`:
  * `kriging_weights_eq_basis` : for INVERTIBLE `K`, the kriging weight vector
    at a training node is the `j`-th standard basis vector,
    `K⁻¹.mulVec (fun i => K i j) = Pi.single j 1`. This is the matrix identity
    `K⁻¹ (col j) = eⱼ`, because the column `col j = K (Pi.single j 1)` is `K`
    applied to a basis vector and `K⁻¹ (K v) = v`.
  * `kriging_weights_eq_basis_of_isUnit` : same, taking the hypothesis in the
    `IsUnit K.det` form (the SPD Gram matrix has a unit determinant); it builds
    the `Invertible K` instance and reduces to the above.
  * `kriging_interpolates` : the exactness conclusion. With the predictor
    `krigingPredictAtNode K trend d j = trend j + (weights) ⬝ᵥ (residual)`
    evaluated at training node `pⱼ`, we get `d̂(pⱼ) = dⱼ` exactly.

The `τ > 0` strict-smoother half (a genuine contraction: the residual
correction has `ℓ₂`-norm strictly below `‖r‖`) is `nugget_strict_smoother`
below, now PROVED (no sorry). The proof avoids the spectral theorem: with
`s = (K₀ + τI)⁻¹ r` the correction is `K₀ s` and `r = K₀ s + τ s`, so
`‖r‖² − ‖K₀ s‖² = 2τ ⟨K₀ s, s⟩ + τ² ‖s‖² > 0` using `⟨K₀ s, s⟩ ≥ 0`
(`Matrix.PosSemidef`) and `s ≠ 0` (`r ≠ 0`, `K₀ + τI` invertible).

Nothing in this file uses `sorry` or introduces an `axiom`.

Key Mathlib lemmas: `Matrix.inv_mulVec_eq_vec` (`u = A v ⇒ A⁻¹ u = v` for
`[Invertible A]`), `Matrix.mulVec_single_one` (`M (Pi.single j 1) = M.col j`),
`Matrix.col_apply`, `single_dotProduct`, `Matrix.invertibleOfIsUnitDet`.
-/

namespace Frahan

section Kriging

variable {n : ℕ}

/-- tex Theorem `thm:nugget`, the exactness kernel `k(pⱼ)ᵀ K⁻¹ = eⱼᵀ`, stated
as the kriging weight vector at training node `pⱼ`. For an INVERTIBLE Gram
matrix `K`, the cross-covariance at `pⱼ` is exactly the `j`-th column of `K`
(entrywise `k(pⱼ)ᵢ = Kᵢⱼ`, no nugget), so the weights `K⁻¹ k(pⱼ)` collapse to
the standard basis vector `eⱼ = Pi.single j 1`.

Proof: a column is `K` applied to a basis vector,
`(fun i => K i j) = K.mulVec (Pi.single j 1)` (`Matrix.mulVec_single_one` +
`Matrix.col_apply`), and `K⁻¹ (K v) = v` (`Matrix.inv_mulVec_eq_vec`). This is
the region-level content of "`k(pⱼ)ᵀ K⁻¹` is row `j` of `K K⁻¹ = I`". -/
theorem kriging_weights_eq_basis (K : Matrix (Fin n) (Fin n) ℝ) [Invertible K]
    (j : Fin n) : K⁻¹.mulVec (fun i => K i j) = Pi.single j 1 := by
  have hcol : (fun i => K i j) = K.mulVec (Pi.single j 1) := by
    funext i
    simp only [Matrix.mulVec_single_one, Matrix.col_apply]
  exact Matrix.inv_mulVec_eq_vec hcol

/-- tex Theorem `thm:nugget`, exactness kernel with the hypothesis in the
determinant form. With `τ = 0` and distinct points the Gaussian Gram matrix is
SPD, hence its determinant is a unit; this restates `kriging_weights_eq_basis`
under `IsUnit K.det` by materializing the `Invertible K` instance
(`Matrix.invertibleOfIsUnitDet`). -/
theorem kriging_weights_eq_basis_of_isUnit (K : Matrix (Fin n) (Fin n) ℝ)
    (hK : IsUnit K.det) (j : Fin n) :
    K⁻¹.mulVec (fun i => K i j) = Pi.single j 1 := by
  letI := K.invertibleOfIsUnitDet hK
  exact kriging_weights_eq_basis K j

/-- The regression-kriging predictor `d̂` (tex Definition "Regression-kriging
predictor") evaluated AT a training node `pⱼ`: `d̂(pⱼ) = π(pⱼ) + k(pⱼ)ᵀ K⁻¹ r`.
`trend` is the polynomial trend `π`, `d` the data, `r i = d i − trend i` the
residual, and `fun i => K i j = k(pⱼ)` the (nugget-free) cross-covariance,
which at the training node is the `j`-th column of `K`. -/
noncomputable def krigingPredictAtNode (K : Matrix (Fin n) (Fin n) ℝ)
    (trend d : Fin n → ℝ) (j : Fin n) : ℝ :=
  trend j + dotProduct (K⁻¹.mulVec (fun i => K i j)) (fun i => d i - trend i)

/-- tex Theorem `thm:nugget`, exactness conclusion (`τ = 0`, distinct points ⇒
interpolation): the regression-kriging predictor reproduces the data exactly at
every training node, `d̂(pⱼ) = dⱼ`.

Proof: the weights are the basis vector `eⱼ` (`kriging_weights_eq_basis`), so
the residual correction `eⱼ ⬝ᵥ r = rⱼ = dⱼ − π(pⱼ)` (`single_dotProduct`),
and `π(pⱼ) + (dⱼ − π(pⱼ)) = dⱼ`. -/
theorem kriging_interpolates (K : Matrix (Fin n) (Fin n) ℝ) [Invertible K]
    (trend d : Fin n → ℝ) (j : Fin n) :
    krigingPredictAtNode K trend d j = d j := by
  unfold krigingPredictAtNode
  rw [kriging_weights_eq_basis, single_dotProduct]
  change trend j + 1 * (d j - trend j) = d j
  ring

/-- tex Theorem `thm:nugget`, strict-smoother half (`τ > 0`). With a positive
nugget the Gram matrix is `K = K₀ + τ I`, `K₀ ⪰ 0` the pure Gaussian kernel
part, and the residual correction across the training nodes is the vector
`c = K₀ (K₀ + τ I)⁻¹ r`. The predictor no longer interpolates: it is a strict
contraction of the residual, `‖c‖₂ < ‖r‖₂` for every `r ≠ 0`, so it damps the
Gaussian-GP oscillation. (Stated as sum-of-squares to name the `ℓ₂` norm
without moving to `EuclideanSpace`.)

Spectrally `K₀ (K₀ + τ I)⁻¹` is symmetric PSD with eigenvalues `μ / (μ + τ) < 1`
over the spectrum `μ ≥ 0` of `K₀`, giving operator norm `< 1` decreasing in `τ`.
The proof below skips that machinery. Substitute `s = (K₀ + τ I)⁻¹ r` (well
defined: `K₀ + τ I` is `PosDef`, hence a unit). Then `c = K₀ s` and
`r = (K₀ + τ I) s = K₀ s + τ s`, so
`‖r‖² − ‖c‖² = 2τ ⟨K₀ s, s⟩ + τ² ‖s‖²`. Here `⟨K₀ s, s⟩ ≥ 0` by
`Matrix.PosSemidef.dotProduct_mulVec_nonneg`, and `s ≠ 0` (as `r ≠ 0` and the
map is invertible) makes `‖s‖² > 0`, so the right side is `> 0`: strictly `<`. -/
theorem nugget_strict_smoother
    (K₀ : Matrix (Fin n) (Fin n) ℝ) (hK₀ : K₀.PosSemidef)
    (τ : ℝ) (hτ : 0 < τ) (r : Fin n → ℝ) (hr : r ≠ 0) :
    ∑ j, ((K₀ * (K₀ + τ • (1 : Matrix (Fin n) (Fin n) ℝ))⁻¹).mulVec r) j ^ 2
      < ∑ j, r j ^ 2 := by
  classical
  -- The nugget Gram matrix `A = K₀ + τ I` is SPD (PSD + strictly positive shift),
  -- hence invertible.
  set A : Matrix (Fin n) (Fin n) ℝ := K₀ + τ • (1 : Matrix (Fin n) (Fin n) ℝ) with hA_def
  have hApd : A.PosDef := by
    rw [hA_def]; exact Matrix.PosDef.posSemidef_add hK₀ (Matrix.PosDef.one.smul hτ)
  have hdet : IsUnit A.det := (Matrix.isUnit_iff_isUnit_det A).mp hApd.isUnit
  -- Change variables to `s = A⁻¹ r`, so `A s = r` and the correction is `K₀ s`.
  set s : Fin n → ℝ := A⁻¹.mulVec r with hs_def
  have hAs : A.mulVec s = r := by
    rw [hs_def, Matrix.mulVec_mulVec, Matrix.mul_nonsing_inv A hdet, Matrix.one_mulVec]
  have hMr : (K₀ * A⁻¹).mulVec r = K₀.mulVec s := by
    rw [← Matrix.mulVec_mulVec, ← hs_def]
  -- `r = A s = K₀ s + τ s`.
  have hAexp : A.mulVec s = K₀.mulVec s + τ • s := by
    rw [hA_def, Matrix.add_mulVec, Matrix.smul_mulVec, Matrix.one_mulVec]
  -- `r ≠ 0` and `A` invertible force `s ≠ 0`.
  have hs_ne : s ≠ 0 := by
    intro h
    exact hr (by rw [← hAs, h, Matrix.mulVec_zero])
  -- `∑ j, v j ^ 2 = v ⬝ᵥ v`.
  have sq_sum : ∀ v : Fin n → ℝ, ∑ j, v j ^ 2 = v ⬝ᵥ v := by
    intro v; simp only [dotProduct, pow_two]
  rw [sq_sum ((K₀ * A⁻¹).mulVec r), sq_sum r, hMr, ← hAs, hAexp]
  set u : Fin n → ℝ := K₀.mulVec s with hu_def
  -- `⟨K₀ s, s⟩ ≥ 0` (positive semidefiniteness).
  have hb : 0 ≤ u ⬝ᵥ s := by
    rw [hu_def, dotProduct_comm]
    simpa using hK₀.dotProduct_mulVec_nonneg s
  -- `‖s‖² > 0` since `s ≠ 0`.
  have hc : 0 < s ⬝ᵥ s := by
    have h0 : 0 ≤ s ⬝ᵥ s := by
      change 0 ≤ ∑ i, s i * s i
      exact Finset.sum_nonneg fun i _ => mul_self_nonneg (s i)
    exact h0.lt_of_ne fun h => hs_ne (dotProduct_self_eq_zero.mp h.symm)
  -- Expand `‖u + τ s‖² = ‖u‖² + 2τ⟨u,s⟩ + τ²‖s‖²` and compare with `‖u‖²`.
  have e1 : u ⬝ᵥ (τ • s) = τ * (u ⬝ᵥ s) := by rw [dotProduct_smul, smul_eq_mul]
  have e2 : (τ • s) ⬝ᵥ u = τ * (u ⬝ᵥ s) := by
    rw [smul_dotProduct, smul_eq_mul, dotProduct_comm]
  have e3 : (τ • s) ⬝ᵥ (τ • s) = τ * (τ * (s ⬝ᵥ s)) := by
    rw [smul_dotProduct, dotProduct_smul, smul_eq_mul, smul_eq_mul]
  rw [add_dotProduct, dotProduct_add, dotProduct_add, e1, e2, e3]
  nlinarith [mul_nonneg hτ.le hb, mul_pos (mul_pos hτ hτ) hc]

end Kriging

end Frahan
