import Mathlib

/-!
Frahan StonePack — Lean formalization, Milestone 1 (Common).

Mechanizes the region-level core of `spec/frahan_algorithm_derivations.tex`
(the .tex is the spec; each result cites its tex label). Working over an
arbitrary real inner-product space: the plane case E = ℝ² is an instance.

Formalized here (PROVED, no sorry):
  * Halfplane, its carrier, and its convexity.
  * clip = set intersection with a halfplane (tex Lemma `lem:sh`,
    region form): clip P H ⊆ P and μ(clip P H) ≤ μ(P) for ANY measure —
    "every clip only removes material (a valid wire cut)".
  * clipChain (the greedy-trim clip sequence, tex Theorem `thm:trim`,
    region form): the output equals P ∩ (⋂ halfplanes), is ⊆ P, has
    μ ≤ μ(P), and the halfplane part is convex; if P is convex the whole
    output is convex.
  * dag_has_source (tex Theorem `thm:kahn`, source-existence half): a
    finite strict precedence order always offers a next installable part.

The algorithmic layer (Sutherland–Hodgman edge walk = this intersection,
reflex-count termination, the full Kahn loop) is staged in `Roadmap.lean`.
-/

open scoped RealInnerProductSpace

namespace Frahan

open MeasureTheory

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]

/-- A closed half-space `{x | ⟪ν, x − p⟫ ≥ 0}` (tex: Definition
"Half-plane clip", §6). `normal` is ν, `base` is the point p on the
boundary line. -/
structure Halfplane (E : Type*) [NormedAddCommGroup E] [InnerProductSpace ℝ E] where
  normal : E
  base : E

/-- The point set of the half-space. -/
def Halfplane.carrier (H : Halfplane E) : Set E :=
  {x | 0 ≤ ⟪H.normal, x - H.base⟫}

@[simp] theorem Halfplane.mem_carrier {H : Halfplane E} {x : E} :
    x ∈ H.carrier ↔ 0 ≤ ⟪H.normal, x - H.base⟫ := Iff.rfl

/-- A half-space is convex. (Support for tex Lemma `lem:sh` and
Theorem `thm:trim`: intersections of half-spaces are convex.) -/
theorem Halfplane.convex (H : Halfplane E) : Convex ℝ H.carrier := by
  intro x hx y hy a b ha hb hab
  simp only [Halfplane.mem_carrier] at hx hy ⊢
  have hb1 : H.base = (a + b) • H.base := by rw [hab, one_smul]
  have hxy : a • x + b • y - H.base = a • (x - H.base) + b • (y - H.base) := by
    calc a • x + b • y - H.base
        = a • x + b • y - (a + b) • H.base := by rw [← hb1]
      _ = a • (x - H.base) + b • (y - H.base) := by
          rw [add_smul, smul_sub, smul_sub]; abel
  rw [hxy, inner_add_right, real_inner_smul_right, real_inner_smul_right]
  have h1 : 0 ≤ a * ⟪H.normal, x - H.base⟫ := mul_nonneg ha hx
  have h2 : 0 ≤ b * ⟪H.normal, y - H.base⟫ := mul_nonneg hb hy
  linarith

/-- One wire cut: clip a region by a half-space. At the region level this
IS the set intersection (tex Lemma `lem:sh`: "Sutherland–Hodgman against a
single half-plane returns exactly the intersection"). -/
def clip (P : Set E) (H : Halfplane E) : Set E := P ∩ H.carrier

/-- tex Lemma `lem:sh`, subset half: a clip only removes material. -/
theorem clip_subset (P : Set E) (H : Halfplane E) : clip P H ⊆ P :=
  Set.inter_subset_left

/-- tex Lemma `lem:sh`, area half — for ANY measure (no measurability
needed: `measure_mono` holds via the outer measure): the clipped region
never gains area/volume. -/
theorem clip_measure_le [MeasurableSpace E] (μ : Measure E)
    (P : Set E) (H : Halfplane E) : μ (clip P H) ≤ μ P :=
  measure_mono (clip_subset P H)

/-- The greedy-trim clip sequence (tex Definition "Greedy convex trim"):
apply the cuts in order. -/
def clipChain (P : Set E) : List (Halfplane E) → Set E
  | [] => P
  | H :: Hs => clipChain (clip P H) Hs

@[simp] theorem clipChain_nil (P : Set E) : clipChain P [] = P := rfl

/-- The chain output is exactly `P ∩ (every halfplane)` — the region
identity behind tex Theorem `thm:trim`'s
`P_out = ⋂ₜ Hₜ ∩ P`. -/
theorem clipChain_eq (P : Set E) (Hs : List (Halfplane E)) :
    clipChain P Hs = P ∩ {x | ∀ H ∈ Hs, x ∈ H.carrier} := by
  induction Hs generalizing P with
  | nil => simp
  | cons H Hs ih =>
      rw [clipChain, ih]
      ext x
      simp only [clip, Set.mem_inter_iff, Set.mem_setOf_eq, List.mem_cons,
        forall_eq_or_imp]
      tauto

/-- tex Theorem `thm:trim`, subset conclusion: the recovered blank is
contained in the input slab. -/
theorem clipChain_subset (P : Set E) (Hs : List (Halfplane E)) :
    clipChain P Hs ⊆ P := by
  rw [clipChain_eq]; exact Set.inter_subset_left

/-- tex Theorem `thm:trim`, area conclusion: the recovered-area ratio
(yield) is at most 1, for any measure. -/
theorem clipChain_measure_le [MeasurableSpace E] (μ : Measure E)
    (P : Set E) (Hs : List (Halfplane E)) :
    μ (clipChain P Hs) ≤ μ P :=
  measure_mono (clipChain_subset P Hs)

/-- The cut side of the chain is convex: an intersection of half-spaces. -/
theorem convex_chain_halfplanes (Hs : List (Halfplane E)) :
    Convex ℝ {x : E | ∀ H ∈ Hs, x ∈ H.carrier} := by
  have hEq : {x : E | ∀ H ∈ Hs, x ∈ H.carrier} = ⋂ H ∈ Hs, H.carrier := by
    ext x; simp
  rw [hEq]
  exact convex_iInter₂ fun H _ => H.convex

/-- tex Theorem `thm:trim`, convexity conclusion in the convex-input
case: clipping a convex slab by any sequence of wire cuts leaves a convex
blank inside the slab. (The non-convex-input case terminates at a convex
polygon via the reflex-count argument — staged in `Roadmap.lean`.) -/
theorem clipChain_convex {P : Set E} (hP : Convex ℝ P)
    (Hs : List (Halfplane E)) : Convex ℝ (clipChain P Hs) := by
  rw [clipChain_eq]
  exact hP.inter (convex_chain_halfplanes Hs)

section Kahn

/-- tex Theorem `thm:kahn`, source-existence half, stated for the strict
precedence ORDER (the transitive closure of the support/precedence edges):
in a finite acyclic precedence relation, every nonempty set of remaining
parts contains one with no unplaced predecessor — Kahn's loop never gets
stuck on a DAG, i.e. a valid install order always offers a next part.
(The full loop-correctness statement, and the converse "stuck ⇒ cycle",
are staged in `Roadmap.lean`.) -/
theorem dag_has_source {α : Type*} [Finite α] (r : α → α → Prop)
    [IsTrans α r] [IsIrrefl α r] (s : Set α) (hs : s.Nonempty) :
    ∃ m ∈ s, ∀ x ∈ s, ¬ r x m := by
  have hwf : WellFounded r := Finite.wellFounded_of_trans_of_irrefl r
  exact hwf.has_min s hs

end Kahn

end Frahan
