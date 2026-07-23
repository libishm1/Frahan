import FrahanProofs.Common

/-!
Frahan StonePack — clustering / alternating minimization.

tex Theorem `thm:kplanes` (descent inequality): one Lloyd round of the
k-planes bed-separation — reassign each point to its best plane, then
refit each plane to its points — never increases the total cost. Stated
abstractly for ANY cost `C : assignments → parameters → ℝ`: the concrete
k-planes instance (squared plane distances) satisfies the two argmin
hypotheses by construction of the algorithm's two steps.

The finite-termination half of `thm:kplanes` (an antitone cost sequence
over finitely many assignments stabilizes) is staged in `Roadmap.lean`.
-/

namespace Frahan

/-- tex Theorem `thm:kplanes`, descent inequality (abstract alternating
minimization): if the reassignment `a'` is optimal for the old
parameters `θ` and the refit `θ'` is optimal for the new assignment
`a'`, the round never increases the cost. -/
theorem alternating_min_descent {A Θ : Type*} (C : A → Θ → ℝ)
    (a a' : A) (θ θ' : Θ)
    (hassign : ∀ b : A, C a' θ ≤ C b θ)
    (hupdate : ∀ ψ : Θ, C a' θ' ≤ C a' ψ) :
    C a' θ' ≤ C a θ :=
  le_trans (hupdate θ) (hassign a)

end Frahan
