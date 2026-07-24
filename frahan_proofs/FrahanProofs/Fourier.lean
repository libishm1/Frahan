import FrahanProofs.Common

/-!
Frahan StonePack — Lean formalization: the discrete Fourier SHIFT theorem.

Mechanizes the core of tex Theorem `thm:phasecorr` (phase-correlation
registration in `spec/frahan_algorithm_derivations.tex`). The continuous
statement: if `f₂(x) = f₁(x − t)` then the shift theorem gives
`f̂₂(k) = f̂₁(k)·e^{−i k·t}`, hence the cross-power spectrum
`f̂₁·conj(f̂₂) = |f̂₁|²·e^{i k·t}`; dividing by its magnitude leaves the pure
unit phase `e^{i k·t}`, whose inverse transform is the shifted delta
`δ(x − t)`. That phase-correlation peak is how the aligner recovers the
integer offset `t` between two overlapping stone / point-cloud tiles.

The mechanizable core is the DISCRETE version on `ZMod N`, using Mathlib's
`ZMod.dft` (the counting-measure DFT with respect to the standard additive
character `ZMod.stdAddChar`, `j ↦ exp (2·π·I·j / N)`; see
`Mathlib/Analysis/Fourier/ZMod.lean`).

Formalized here (PROVED, no sorry):
  * `stdAddChar_norm`  — the character value is unit-modulus (`‖e^{iθ}‖ = 1`).
  * `conj_stdAddChar`  — `conj (stdAddChar a) = stdAddChar (-a)` (a root of
    unity is inverted by conjugation).
  * `dft_shift`        — tex `thm:phasecorr`, shift step: the DFT of the
    shifted signal `j ↦ Φ (j − s)` equals the phase `stdAddChar (-(s·k))`
    (the discrete `e^{−i k·t}`) times `dft Φ k`. Proof: reindex the defining
    sum by `Equiv.subRight s` and factor the character with
    `AddChar.map_add_eq_mul`.
  * `dft_shift_mul`    — the `ℂ`-valued form with `*` in place of `•`.
  * `crossPower_eq`    — the cross-power `dft Φ k * conj (dft Ψ k)` with
    `Ψ j = Φ (j − s)` equals `(‖dft Φ k‖:ℂ)² · conj(phase)`, i.e. `|f̂₁|²·e^{i k·t}`.
  * `crossPower_eq'`   — same, with the phase written as `stdAddChar (s·k)`.
  * `crossPower_norm`  — its magnitude is exactly `‖dft Φ k‖²`.
  * `normalized_crossPower_eq_phase` — dividing the cross-power by its own
    magnitude (when `dft Φ k ≠ 0`) leaves precisely the unit phase
    `conj(phase) = e^{i k·t}`.

Staged (`proof_wanted`; the inverse-DFT-of-a-character step needs the
additive-character orthogonality sum, which `ZMod.dft`'s inversion proof
keeps private):
  * `dft_inv_phase_eq_delta` — the transform of the pure phase `k ↦ e^{i k·t}`
    is a scaled shifted delta, peaked at `x = s`.
-/

open scoped ComplexConjugate

namespace Frahan

open ZMod AddChar

variable {N : ℕ} [NeZero N] {E : Type*} [AddCommGroup E] [Module ℂ E]

/-- The standard additive character is unit-modulus: `‖exp (2·π·I·j/N)‖ = 1`.
It factors through the unit circle, whose complex coercion has norm one. -/
lemma stdAddChar_norm (a : ZMod N) : ‖stdAddChar a‖ = 1 := by
  rw [stdAddChar_apply]
  exact Circle.norm_coe _

/-- Conjugation inverts the character: `conj (stdAddChar a) = stdAddChar (-a)`.
The value is a root of unity, so its conjugate equals its inverse, which is
the value at `-a` (`AddChar.map_neg_eq_inv`). -/
lemma conj_stdAddChar (a : ZMod N) :
    conj (stdAddChar a) = stdAddChar (-a) := by
  rw [stdAddChar_apply, ← Circle.coe_inv_eq_conj, ← map_neg_eq_inv, ← stdAddChar_apply]

/-- Discrete Fourier SHIFT theorem (tex Theorem `thm:phasecorr`, shift step).
The DFT of the shifted signal `j ↦ Φ (j − s)` is the unit phase
`stdAddChar (-(s·k))` (the discrete `e^{−i k·t}`) times `dft Φ k`.

Proof: expand `ZMod.dft` by its definition (`dft_apply`), reindex the sum by
`j ↦ j − s` (`Equiv.subRight s`), and split off the shift factor with the
additive-character law `stdAddChar (x + y) = stdAddChar x · stdAddChar y`. -/
theorem dft_shift (Φ : ZMod N → E) (s k : ZMod N) :
    ZMod.dft (fun j => Φ (j - s)) k = stdAddChar (-(s * k)) • ZMod.dft Φ k := by
  simp only [dft_apply, Finset.smul_sum]
  refine Fintype.sum_equiv (Equiv.subRight s) _ _ (fun j => ?_)
  simp only [Equiv.subRight_apply]
  rw [smul_smul, ← map_add_eq_mul]
  have h : -(j * k) = -(s * k) + -((j - s) * k) := by ring
  rw [← h]

/-- `ℂ`-valued form of `dft_shift`, with multiplication in place of scalar
action: `dft (fun j => Φ (j − s)) k = stdAddChar (-(s·k)) * dft Φ k`. -/
theorem dft_shift_mul (Φ : ZMod N → ℂ) (s k : ZMod N) :
    ZMod.dft (fun j => Φ (j - s)) k = stdAddChar (-(s * k)) * ZMod.dft Φ k := by
  rw [dft_shift, smul_eq_mul]

/-- Cross-power spectrum (tex Theorem `thm:phasecorr`): with `Ψ j = Φ (j − s)`,
`dft Φ k * conj (dft Ψ k) = (‖dft Φ k‖:ℂ)² * conj(phase)`. The `conj(phase)`
factor is the discrete `e^{+i k·t}`, so the cross-power is `|f̂₁|²·e^{i k·t}`. -/
theorem crossPower_eq (Φ : ZMod N → ℂ) (s k : ZMod N) :
    ZMod.dft Φ k * conj (ZMod.dft (fun j => Φ (j - s)) k)
      = (‖ZMod.dft Φ k‖ : ℂ) ^ 2 * conj (stdAddChar (-(s * k))) := by
  rw [dft_shift_mul, map_mul]
  have h : ZMod.dft Φ k * (conj (stdAddChar (-(s * k))) * conj (ZMod.dft Φ k))
      = (ZMod.dft Φ k * conj (ZMod.dft Φ k)) * conj (stdAddChar (-(s * k))) := by ring
  rw [h, Complex.mul_conj, Complex.normSq_eq_norm_sq, Complex.ofReal_pow]

/-- Cross-power with the phase written as the discrete `e^{+i k·t}`
directly: `dft Φ k * conj (dft Ψ k) = (‖dft Φ k‖:ℂ)² * stdAddChar (s·k)`. -/
theorem crossPower_eq' (Φ : ZMod N → ℂ) (s k : ZMod N) :
    ZMod.dft Φ k * conj (ZMod.dft (fun j => Φ (j - s)) k)
      = (‖ZMod.dft Φ k‖ : ℂ) ^ 2 * stdAddChar (s * k) := by
  rw [crossPower_eq, conj_stdAddChar, neg_neg]

/-- The cross-power magnitude collapses to `‖dft Φ k‖²`: the phase factor is
unit-modulus, so normalizing by this magnitude will leave a pure phase. -/
theorem crossPower_norm (Φ : ZMod N → ℂ) (s k : ZMod N) :
    ‖ZMod.dft Φ k * conj (ZMod.dft (fun j => Φ (j - s)) k)‖ = ‖ZMod.dft Φ k‖ ^ 2 := by
  rw [crossPower_eq]
  simp only [norm_mul, norm_pow, Complex.norm_real, norm_norm, RCLike.norm_conj,
    stdAddChar_norm, mul_one]

/-- tex Theorem `thm:phasecorr`, normalized cross-power step: dividing the
cross-power by its own magnitude leaves exactly the unit phase
`conj(stdAddChar (-(s·k))) = e^{+i k·t}`. Requires the reference spectrum to
be nonzero at `k` (otherwise the numerator vanishes and the quotient is `0`).
This is the quantity whose inverse transform peaks at the offset `s`. -/
theorem normalized_crossPower_eq_phase (Φ : ZMod N → ℂ) (s k : ZMod N)
    (hk : ZMod.dft Φ k ≠ 0) :
    (ZMod.dft Φ k * conj (ZMod.dft (fun j => Φ (j - s)) k))
        / (‖ZMod.dft Φ k * conj (ZMod.dft (fun j => Φ (j - s)) k)‖ : ℂ)
      = conj (stdAddChar (-(s * k))) := by
  have h0 : (‖ZMod.dft Φ k‖ : ℂ) ≠ 0 := by
    simp only [ne_eq, Complex.ofReal_eq_zero, norm_eq_zero]
    exact hk
  have hc : (‖ZMod.dft Φ k‖ : ℂ) ^ 2 ≠ 0 := pow_ne_zero 2 h0
  rw [crossPower_norm, crossPower_eq, Complex.ofReal_pow]
  exact mul_div_cancel_left₀ _ hc

/-- tex Theorem `thm:phasecorr`, final delta step: the transform of the pure
phase `k ↦ stdAddChar (s·k)` (the discrete `e^{+i k·t}`) is a scaled shifted
delta, peaked at `x = s` — `F⁻¹[e^{i k·t}] = δ(x − t)`.

Left as `proof_wanted`: the value follows from the additive-character
orthogonality sum `∑_k stdAddChar (k·t) = if t = 0 then N else 0`, which
`ZMod.dft`'s Fourier-inversion proof (`ZMod.dft_dft`) keeps as a private
`have`; re-deriving it here from `ZMod.isPrimitive_stdAddChar` and
`AddChar.sum_eq_zero_of_ne_one` is a self-contained follow-up. -/
proof_wanted dft_inv_phase_eq_delta (s x : ZMod N) :
    ZMod.dft (fun k => stdAddChar (s * k)) x = if x = s then (N : ℂ) else 0

end Frahan
