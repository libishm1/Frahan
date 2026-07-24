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

/-! ### Mode-merge separation (discontinuity joint-set clustering)

The joint-set clusterer (`SetClusterer.Cluster`) keeps a converged mode only if
it is not within the merge angle of an already-kept mode. This models that greedy
merge and proves its guarantee: the kept modes are pairwise "far" (never merge to
the same set). `close a b` is "within the merge angle" (symmetric). -/

variable {α : Type*} (close : α → α → Prop) [DecidableRel close]

/-- The greedy merge: fold the candidate modes, keeping `x` only when no
already-kept mode is `close` to it (clusterer lines "merge converged modes"). -/
def mergeKeep : List α → List α → List α
  | acc, [] => acc
  | acc, x :: xs =>
      if (∃ y ∈ acc, close x y) then mergeKeep acc xs
      else mergeKeep (acc ++ [x]) xs

/-- Fold invariant: merging into an already-separated accumulator keeps it
separated (pairwise not-`close`). Needs `close` symmetric. -/
theorem mergeKeep_pairwise (hsym : ∀ a b, close a b → close b a) :
    ∀ (acc l : List α), acc.Pairwise (fun a b => ¬ close a b) →
      (mergeKeep close acc l).Pairwise (fun a b => ¬ close a b) := by
  intro acc l
  induction l generalizing acc with
  | nil => intro h; exact h
  | cons x xs ih =>
      intro h
      by_cases hx : ∃ y ∈ acc, close x y
      · rw [mergeKeep, if_pos hx]; exact ih acc h
      · rw [mergeKeep, if_neg hx]
        refine ih (acc ++ [x]) ?_
        rw [List.pairwise_append]
        refine ⟨h, by simp, ?_⟩
        intro a ha b hb
        rw [List.mem_singleton] at hb
        subst hb
        exact fun hca => hx ⟨a, ha, hsym a b hca⟩

/-- Mode-merge separation: the kept modes are pairwise not-`close` — no two
survive within the merge angle, so the discovered joint sets are distinct. -/
theorem mergeKeep_separated (hsym : ∀ a b, close a b → close b a) (l : List α) :
    (mergeKeep close [] l).Pairwise (fun a b => ¬ close a b) :=
  mergeKeep_pairwise close hsym [] l List.Pairwise.nil

end Frahan
