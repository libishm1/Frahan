import FrahanProofs.Common

/-!
Frahan StonePack — surface-packing transfer.

tex Theorem `thm:surfpack`: parts packed WITHOUT OVERLAP in the UV chart `Ω`
(the 2D nesting of §2) map, under the chart map `Φ = φ ∘ chart⁻¹`, to
surface-embedded parts that are MUTUALLY NON-OVERLAPPING on the surface — because
`Φ` is a bijection on the chart, disjoint UV placements have disjoint images.

The non-overlap transfer is the mechanizable core and is proved below
(`surfpack_disjoint`): injectivity of the chart map on `Ω` sends disjoint
subsets of `Ω` to disjoint images. The AREA statement (physical area = UV area ×
the local chart-scale `e^{2u}`, the BFF conformal factor; pre-scaling each UV
cell by `e^{-u}` matches on-surface tile sizes) is the change-of-variables
Jacobian of the conformal map and stays prose here — it needs the measure-
theoretic conformal factor, not the combinatorial non-overlap guarantee.
-/

namespace Frahan

/-- tex Theorem `thm:surfpack`, the non-overlap transfer: if the chart map `Φ`
is injective on the chart `Ω` (atlas validity — `Φ` is a bijection on the
chart), then two parts placed disjointly inside `Ω` map to disjoint
surface-embedded parts. Disjoint UV placements ⇒ non-overlapping on the surface. -/
theorem surfpack_disjoint {α β : Type*} (Φ : α → β) (Ω : Set α)
    (hinj : Set.InjOn Φ Ω) {A B : Set α} (hA : A ⊆ Ω) (hB : B ⊆ Ω)
    (hAB : Disjoint A B) : Disjoint (Φ '' A) (Φ '' B) := by
  rw [Set.disjoint_left]
  rintro _ ⟨a, ha, rfl⟩ ⟨b, hb, hab⟩
  have hab' : a = b := hinj (hA ha) (hB hb) hab.symm
  subst hab'
  exact Set.disjoint_left.mp hAB ha hb

/-- tex Theorem `thm:surfpack`, the many-parts form: a whole family of pairwise-
disjoint UV placements inside `Ω` maps to a pairwise-disjoint family of
surface parts — the full "the packed layout stays valid on the surface" claim. -/
theorem surfpack_pairwiseDisjoint {α β ι : Type*} (Φ : α → β) (Ω : Set α)
    (hinj : Set.InjOn Φ Ω) (part : ι → Set α) (hsub : ∀ i, part i ⊆ Ω)
    (hpd : Pairwise (Function.onFun Disjoint part)) :
    Pairwise (Function.onFun Disjoint fun i => Φ '' part i) :=
  fun i j hij => surfpack_disjoint Φ Ω hinj (hsub i) (hsub j) (hpd hij)

end Frahan
