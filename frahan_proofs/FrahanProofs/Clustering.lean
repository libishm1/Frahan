import FrahanProofs.Common

/-!
Frahan StonePack — clustering / alternating minimization.

tex Theorem `thm:kplanes` (descent inequality): one Lloyd round of the
k-planes bed-separation — reassign each point to its best plane, then
refit each plane to its points — never increases the total cost. Stated
abstractly for ANY cost `C : assignments → parameters → ℝ`: the concrete
k-planes instance (squared plane distances) satisfies the two argmin
hypotheses by construction of the algorithm's two steps.

The finite-termination half is `antitone_finite_range_eventually_constant`
below: the cost sequence is antitone (by the descent inequality) and
attains finitely many values (finitely many assignments), so it is
eventually constant — Lloyd iteration stabilizes in finitely many rounds.
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

/-- tex Theorem `thm:kplanes`, termination half: an antitone real
sequence with finitely many attainable values (finitely many
assignments) is eventually constant. Once the sequence attains the
minimum of its range, antitonicity pins it there. -/
theorem antitone_finite_range_eventually_constant
    (f : ℕ → ℝ) (hf : ∀ n, f (n + 1) ≤ f n)
    (hfin : (Set.range f).Finite) :
    ∃ N, ∀ n, N ≤ n → f n = f N := by
  have hanti : Antitone f := antitone_nat_of_succ_le hf
  obtain ⟨x, hx, hmin⟩ :=
    Set.exists_min_image (Set.range f) id hfin ⟨f 0, Set.mem_range_self 0⟩
  obtain ⟨N, rfl⟩ := hx
  exact ⟨N, fun n hn => le_antisymm (hanti hn) (hmin _ (Set.mem_range_self n))⟩

end Frahan
