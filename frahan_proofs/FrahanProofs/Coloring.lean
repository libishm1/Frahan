import FrahanProofs.Common

/-!
Frahan StonePack — conflict-graph coloring.

tex (WelshPowell): greedy coloring of a conflict graph with maximum
degree at most `D` uses at most `D + 1` colors. This is the correctness
core behind the conflict-scheduling components (cut-station assignment,
batch grouping): sorting vertices by decreasing degree — Welsh–Powell
proper — only improves the constant; the `D + 1` guarantee holds for
ANY insertion order, which is what is proved here.

Mathlib (as of v4.32.1) has `SimpleGraph.Coloring` but no `Δ + 1`
greedy bound, so this is built from scratch on a bare symmetric
irreflexive conflict relation. Proof: induction on the vertex set; the
newly inserted vertex conflicts with at most `D` colored vertices, so
at most `D` of the `D + 1` colors are blocked (pigeonhole).
-/

namespace Frahan

/-- tex (WelshPowell), existence form: a finite symmetric irreflexive
conflict relation whose degree within `s` is at most `D` admits a
proper coloring of `s` with `D + 1` colors. -/
theorem greedy_coloring_exists {V : Type*}
    (adj : V → V → Prop) [DecidableRel adj]
    (hsymm : Symmetric adj) (hirr : ∀ v, ¬ adj v v) (D : ℕ)
    (s : Finset V) (hdeg : ∀ v ∈ s, (s.filter (adj v)).card ≤ D) :
    ∃ c : V → Fin (D + 1), ∀ v ∈ s, ∀ w ∈ s, adj v w → c v ≠ c w := by
  classical
  revert hdeg
  induction s using Finset.induction_on with
  | empty => exact fun _ => ⟨fun _ => 0, by simp⟩
  | insert a s ha ih =>
      intro hdeg
      -- color the smaller set
      obtain ⟨c, hc⟩ := ih fun v hv =>
        le_trans
          (Finset.card_le_card
            (Finset.filter_subset_filter _ (Finset.subset_insert a s)))
          (hdeg v (Finset.mem_insert_of_mem hv))
      -- the colors blocked at `a`: at most D of the D+1
      set used : Finset (Fin (D + 1)) := (s.filter (adj a)).image c with hused
      have hcard : used.card ≤ D :=
        le_trans Finset.card_image_le
          (le_trans
            (Finset.card_le_card
              (Finset.filter_subset_filter _ (Finset.subset_insert a s)))
            (hdeg a (Finset.mem_insert_self a s)))
      -- pigeonhole: some color is free
      have hfree : ∃ k : Fin (D + 1), k ∉ used := by
        by_contra h
        push_neg at h
        have : used = Finset.univ := Finset.eq_univ_iff_forall.mpr h
        rw [this, Finset.card_univ, Fintype.card_fin] at hcard
        omega
      obtain ⟨k, hk⟩ := hfree
      refine ⟨Function.update c a k, fun v hv w hw hvw => ?_⟩
      rcases Finset.mem_insert.mp hv with rfl | hv'
      · rcases Finset.mem_insert.mp hw with rfl | hw'
        · exact absurd hvw (hirr _)
        · -- v is the new vertex, w ∈ s: free color ≠ every blocked color
          have hwv : w ≠ v := fun h => ha (h ▸ hw')
          rw [Function.update_self, Function.update_of_ne hwv]
          exact fun h => hk (h ▸ Finset.mem_image_of_mem c
            (Finset.mem_filter.mpr ⟨hw', hvw⟩))
      · rcases Finset.mem_insert.mp hw with rfl | hw'
        · -- w is the new vertex, v ∈ s: symmetric case
          have hvne : v ≠ w := fun h => ha (h ▸ hv')
          rw [Function.update_self, Function.update_of_ne hvne]
          exact fun h => hk (h ▸ Finset.mem_image_of_mem c
            (Finset.mem_filter.mpr ⟨hv', hsymm hvw⟩))
        · -- both old: the inductive coloring is untouched
          have hva : v ≠ a := fun h => ha (h ▸ hv')
          have hwa : w ≠ a := fun h => ha (h ▸ hw')
          rw [Function.update_of_ne hva, Function.update_of_ne hwa]
          exact hc v hv' w hw' hvw

end Frahan
