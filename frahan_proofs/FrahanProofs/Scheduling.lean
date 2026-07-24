import FrahanProofs.Common

/-!
Frahan StonePack — scheduling / install-order results.

tex Theorem `thm:kahn` (full): a finite acyclic strict precedence
relation admits a linear install order in which every part strictly
follows all of its predecessors. Together with `Frahan.dag_has_source`
(Common.lean: the loop never sticks) this is the correctness content of
Kahn's algorithm for the masonry build-order components.

Route: the reflexive closure of the strict precedence is a partial
order; Mathlib's order-extension principle (`extend_partialOrder`,
Szpilrajn) extends it to a linear order containing every precedence
edge, and irreflexivity of the input keeps those edges strict.
-/

namespace Frahan

/-- tex Theorem `thm:kahn` (full order-existence statement): any strict
precedence relation (transitive, irreflexive) embeds in a linear order:
there is a total install order `s` with every precedence edge `r a b`
strict in `s`. `Finite` is not even needed for existence; Kahn's loop
(`dag_has_source`) uses finiteness only to terminate. -/
theorem kahn_linear_extension {α : Type*} (r : α → α → Prop)
    [IsTrans α r] [Std.Irrefl r] :
    ∃ s : α → α → Prop, IsLinearOrder α s ∧ ∀ a b, r a b → s a b ∧ a ≠ b := by
  -- the reflexive closure of the strict precedence
  let r' : α → α → Prop := fun a b => r a b ∨ a = b
  haveI : Std.Refl r' := ⟨fun a => Or.inr rfl⟩
  haveI : IsTrans α r' := ⟨by
    rintro a b c (hab | rfl) (hbc | rfl)
    · exact Or.inl (Trans.trans hab hbc)
    · exact Or.inl hab
    · exact Or.inl hbc
    · exact Or.inr rfl⟩
  haveI : Std.Antisymm r' := ⟨by
    rintro a b (hab | rfl) (hba | h)
    · exact absurd (Trans.trans hab hba) (irrefl a)
    · exact h.symm
    · rfl
    · rfl⟩
  haveI : IsPreorder α r' := {}
  haveI : IsPartialOrder α r' := {}
  obtain ⟨s, hs, hsub⟩ := extend_partialOrder r'
  refine ⟨s, hs, fun a b hab => ⟨hsub _ _ (Or.inl hab), ?_⟩⟩
  rintro rfl
  exact irrefl a hab

end Frahan
