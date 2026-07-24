import FrahanProofs.Common

/-!
Frahan StonePack — PCA / Rayleigh spectral characterization.

Mechanizes the Rayleigh core of tex Proposition `prop:pca` ("PCA-OBB and
normal estimation", §"Spectral and PCA geometry") of
`spec/frahan_algorithm_derivations.tex`. The tex covariance operator is
`C = (1/n) Σᵢ (pᵢ − p̄)(pᵢ − p̄)ᵀ ⪰ 0`, symmetric; its eigenpairs
`(λ₁ ≥ λ₂ ≥ λ₃, u₁, u₂, u₃)` are the principal axes, and the **surface
normal** is `u₃`, the least-eigenvalue direction. The tex proof reduces the
normal-estimation content to one fact: "the least-variance direction
minimizes `uᵀC u` over `‖u‖ = 1` (Rayleigh)".

Here `T : E →L[ℝ] E` is the (self-adjoint / symmetric) covariance operator on
a real inner-product space `E`, and the directional variance along `u` is the
real pairing `⟪T u, u⟫` (= `T.reApplyInnerSelf u`, `reApplyInnerSelf_real`; on
the unit sphere it is exactly Mathlib's `T.rayleighQuotient u`). We prove the
Rayleigh **lower-bound** characterization directly from a PSD/operator lower
bound `T ⪰ λ` — self-contained, WITHOUT invoking the full spectral theorem:

  * `reApplyInnerSelf_real`      : over ℝ, `T.reApplyInnerSelf u = ⟪T u, u⟫`.
  * `rayleigh_ge_of_lowerBound`  : if `T ⪰ λ` (`∀ v, λ‖v‖² ≤ ⟪T v,v⟫`) then a
                                   unit `u` has `λ ≤ ⟪T u, u⟫` — the least
                                   eigenvalue lower-bounds the variance in ANY
                                   direction.
  * `eigenvector_rayleigh_eq`    : a unit λ-eigenvector ATTAINS the bound,
                                   `⟪T u, u⟫ = λ`.
  * `eigenvector_isMinOn_rayleigh` (the PCA content): under `T ⪰ λ`, a unit
                                   λ-eigenvector `u` MINIMIZES the variance
                                   `⟪T v, v⟫` over the unit sphere. Packaged as
                                   `isMinOn_variance_of_eigenvector` in
                                   Mathlib's `IsMinOn` vocabulary. With
                                   `λ = λ₃` this is exactly "the least-variance
                                   / surface-normal direction is a minimizer".

`λ` is kept abstract with the faithful hypothesis `T ⪰ λ` (the operator
inequality that `C ⪰ 0` symmetric supplies). Pinning `λ` to the **actual
smallest eigenvalue** — existence of the eigenvector attaining the sphere
infimum — is the spectral-theorem content and is now PROVED (`least_eigenvalue_lowerBound` below) via Mathlib's
`hasEigenvalue_iInf_of_finiteDimensional`.

The PCA-OBB "box in the eigenbasis" (prop:pca (i)) and the Hoppe-MST normal
orientation (prop:pca (iii)) are prose in the tex and are NOT formalized here.

Nothing in this file uses `sorry` or introduces an `axiom`.
-/

open scoped RealInnerProductSpace

namespace Frahan

section Spectral

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]

/-- Bridge to Mathlib's Rayleigh machinery. Over ℝ the "real part" is the
identity, so Mathlib's `T.reApplyInnerSelf u = re ⟪T u, u⟫` is literally the
real pairing `⟪T u, u⟫`. Hence on the unit sphere `T.rayleighQuotient u`
(`= T.reApplyInnerSelf u / ‖u‖²`) coincides with the directional variance
`⟪T u, u⟫` used throughout this file. -/
@[simp] theorem reApplyInnerSelf_real (T : E →L[ℝ] E) (u : E) :
    T.reApplyInnerSelf u = ⟪T u, u⟫ := by
  simp [ContinuousLinearMap.reApplyInnerSelf_apply]

/-- tex `prop:pca`, Rayleigh lower bound. If the covariance operator dominates
`λ` as an operator inequality — `T ⪰ λ`, i.e. `λ‖v‖² ≤ ⟪T v, v⟫` for every `v`
— then along any UNIT direction `u` the variance is at least `λ`:
`λ ≤ ⟪T u, u⟫`. Instantiating `λ = λ₃` (the least eigenvalue) says the least
eigenvalue lower-bounds the variance captured in every direction. Pure
specialization of the hypothesis at `u`, using `‖u‖ = 1`. -/
theorem rayleigh_ge_of_lowerBound (T : E →L[ℝ] E) {lam : ℝ}
    (hlb : ∀ v : E, lam * ‖v‖ ^ 2 ≤ ⟪T v, v⟫) {u : E} (hu : ‖u‖ = 1) :
    lam ≤ ⟪T u, u⟫ := by
  have h := hlb u
  rwa [hu, one_pow, mul_one] at h

/-- tex `prop:pca`, the eigenvector attains the Rayleigh bound. A unit
`λ`-eigenvector `u` (`T u = λ • u`, `‖u‖ = 1`) realizes the variance exactly:
`⟪T u, u⟫ = λ`. Computation `⟪λ • u, u⟫ = λ ⟪u, u⟫ = λ ‖u‖² = λ` via
`real_inner_smul_left` and `real_inner_self_eq_norm_sq`. -/
theorem eigenvector_rayleigh_eq (T : E →L[ℝ] E) {lam : ℝ} {u : E}
    (hev : T u = lam • u) (hu : ‖u‖ = 1) : ⟪T u, u⟫ = lam := by
  rw [hev, real_inner_smul_left, real_inner_self_eq_norm_sq, hu, one_pow, mul_one]

/-- tex `prop:pca`, the PCA content ("the least-variance direction minimizes
`uᵀC u` over `‖u‖ = 1`"). Under the operator lower bound `T ⪰ λ`, a unit
`λ`-eigenvector `u` MINIMIZES the directional variance over the unit sphere:
for every unit `v`, `⟪T u, u⟫ ≤ ⟪T v, v⟫`. The eigenvector attains `λ`
(`eigenvector_rayleigh_eq`) and `λ` lower-bounds every direction
(`rayleigh_ge_of_lowerBound`). With `λ = λ₃`, `u = u₃`, this is exactly "the
least-eigenvalue direction is the minimum-variance direction = the estimated
surface normal". -/
theorem eigenvector_isMinOn_rayleigh (T : E →L[ℝ] E) {lam : ℝ} {u : E}
    (hlb : ∀ v : E, lam * ‖v‖ ^ 2 ≤ ⟪T v, v⟫)
    (hev : T u = lam • u) (hu : ‖u‖ = 1) :
    ∀ v : E, ‖v‖ = 1 → ⟪T u, u⟫ ≤ ⟪T v, v⟫ := by
  intro v hv
  rw [eigenvector_rayleigh_eq T hev hu]
  exact rayleigh_ge_of_lowerBound T hlb hv

/-- `eigenvector_isMinOn_rayleigh` packaged in Mathlib's minimization
vocabulary: a unit `λ`-eigenvector is an `IsMinOn` point of the variance
`fun v ↦ ⟪T v, v⟫` on the unit sphere `Metric.sphere 0 1`. This is the
argmin form of the Rayleigh characterization. -/
theorem isMinOn_variance_of_eigenvector (T : E →L[ℝ] E) {lam : ℝ} {u : E}
    (hlb : ∀ v : E, lam * ‖v‖ ^ 2 ≤ ⟪T v, v⟫)
    (hev : T u = lam • u) (hu : ‖u‖ = 1) :
    IsMinOn (fun v => ⟪T v, v⟫) (Metric.sphere (0 : E) 1) u := by
  rw [isMinOn_iff]
  intro v hv
  rw [mem_sphere_zero_iff_norm] at hv
  exact eigenvector_isMinOn_rayleigh T hlb hev hu v hv

/-- Eigenvalue identification (PROVED via the spectral eigenvalue theorem). For a symmetric operator `T` on a finite-dimensional
nontrivial real inner-product space, the abstract `λ` of this section can be
pinned to the ACTUAL smallest eigenvalue: there is a least eigenvalue `λ` with
a unit eigenvector `u` (attaining the sphere infimum of the Rayleigh quotient,
`IsSelfAdjoint.hasEigenvector_of_isMinOn` /
`LinearMap.IsSymmetric.hasEigenvalue_iInf_of_finiteDimensional`) such that the
operator lower bound `T ⪰ λ` holds. Feeding this `λ`, `u` into
`eigenvector_isMinOn_rayleigh` turns the abstract statement into the concrete
PCA claim: `u = u₃` (least-eigenvalue axis) is the minimum-variance /
surface-normal direction. Left wanted: this bundles the spectral theorem
(orthonormal eigenbasis) plus the `iInf`-attainment argument; kept out of the
self-contained lower-bound development above per the abstract-`λ` design. -/
theorem least_eigenvalue_lowerBound [FiniteDimensional ℝ E] [Nontrivial E]
    (T : E →L[ℝ] E) (hT : (T : E →ₗ[ℝ] E).IsSymmetric) :
    ∃ (lam : ℝ) (u : E), ‖u‖ = 1 ∧ T u = lam • u ∧
      ∀ v : E, lam * ‖v‖ ^ 2 ≤ ⟪T v, v⟫ := by
  -- The Rayleigh quotient is bounded below (by `-‖T‖`), so its infimum exists.
  have hbdd : BddBelow (Set.range fun x : { x : E // x ≠ 0 } => T.rayleighQuotient x) := by
    refine ⟨-‖T‖, ?_⟩
    rintro y ⟨x, rfl⟩
    exact (abs_le.mp (T.rayleighQuotient_le_norm (x : E))).1
  -- In finite dimension the infimum of the Rayleigh quotient of a symmetric
  -- operator IS an eigenvalue (Mathlib's Rayleigh + spectral theorem):
  -- `LinearMap.IsSymmetric.hasEigenvalue_iInf_of_finiteDimensional`.
  have hEig : Module.End.HasEigenvalue (T : E →ₗ[ℝ] E)
      (⨅ x : { x : E // x ≠ 0 }, T.rayleighQuotient x) :=
    hT.hasEigenvalue_iInf_of_finiteDimensional
  obtain ⟨w, hw⟩ := hEig.exists_hasEigenvector
  have hw_ne : w ≠ 0 := hw.2
  have hTw : T w = (⨅ x : { x : E // x ≠ 0 }, T.rayleighQuotient x) • w := hw.apply_eq_smul
  refine ⟨⨅ x : { x : E // x ≠ 0 }, T.rayleighQuotient x, (‖w‖⁻¹ : ℝ) • w,
      norm_smul_inv_norm hw_ne, ?_, ?_⟩
  · -- The normalized vector `‖w‖⁻¹ • w` is still an eigenvector for the same `lam`.
    rw [map_smul, hTw, smul_comm]
  · -- `lam = ⨅ rayleigh` lower-bounds the Rayleigh quotient in every direction,
    -- which rearranges to `lam ‖v‖² ≤ ⟪T v, v⟫`.
    intro v
    rcases eq_or_ne v 0 with rfl | hv
    · simp
    · have hvpos : (0 : ℝ) < ‖v‖ := norm_pos_iff.mpr hv
      have hb : (0 : ℝ) < ‖v‖ ^ 2 := pow_pos hvpos 2
      have hle : (⨅ x : { x : E // x ≠ 0 }, T.rayleighQuotient x) ≤ T.rayleighQuotient v :=
        ciInf_le hbdd (⟨v, hv⟩ : { x : E // x ≠ 0 })
      have hrq : T.rayleighQuotient v = ⟪T v, v⟫ / ‖v‖ ^ 2 := by
        rw [show T.rayleighQuotient v = T.reApplyInnerSelf v / ‖v‖ ^ 2 from rfl,
          reApplyInnerSelf_real]
      rw [hrq, le_div_iff₀ hb] at hle
      exact hle

end Spectral

end Frahan
