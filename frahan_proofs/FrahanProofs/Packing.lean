import FrahanProofs.Common

/-!
Frahan StonePack — no-fit-polygon (NFP) separation.

The correctness core of the 2D nester (`ContactNfpHoleNester`, the CNH / NFP-BLF
packer): a part `B` translated to place it at `t` overlaps a fixed part `A`
exactly when `t` lies in the Minkowski-difference set `A - B` (the no-fit
polygon). Hence choosing the translation OUTSIDE the NFP guarantees the placed
parts are disjoint — the "zero overlap" contract verified empirically (code_ws
`outputs/2026-07-24/nester_verification`: 0 overlaps / 34832 pairs). This is the
row where the code was verified before the proof existed; the theorem closes it.

Stated for sets in any additive commutative group (the plane `ℝ²` is an
instance). The pointwise set difference `A - B = {a - b | a ∈ A, b ∈ B}` is the
NFP; the multi-part feasible region is the complement of the union of the
pairwise NFPs, of which the pairwise statement here is the heart.
-/

namespace Frahan

open Pointwise

variable {E : Type*} [AddCommGroup E]

/-- tex (NFP separation), overlap characterization: the part `B` translated by
`t` meets the fixed part `A` iff `t` lies in the no-fit polygon `A - B`. -/
theorem overlap_iff_mem_nfp (A B : Set E) (t : E) :
    (A ∩ (fun y => t + y) '' B).Nonempty ↔ t ∈ A - B := by
  constructor
  · rintro ⟨x, hxA, y, hy, rfl⟩
    -- x = t + y ∈ A ; witness (t + y) - y = t
    refine Set.mem_sub.2 ⟨t + y, hxA, y, hy, ?_⟩
    abel
  · rintro ⟨p, hp, q, hq, rfl⟩
    -- t = p - q ; the point p lies in A and in the translate of B (via q)
    refine ⟨p, hp, q, hq, ?_⟩
    show (p - q) + q = p
    abel

/-- tex (NFP separation), the packing guarantee: if the placement `t` avoids the
no-fit polygon `A - B`, the translated part is disjoint from `A` — no overlap.
This is the invariant the CNH nester's IFP/NFP construction enforces. -/
theorem nfp_separation (A B : Set E) (t : E) (ht : t ∉ A - B) :
    Disjoint A ((fun y => t + y) '' B) := by
  rw [Set.disjoint_iff_inter_eq_empty, ← Set.not_nonempty_iff_eq_empty]
  exact fun h => ht ((overlap_iff_mem_nfp A B t).1 h)

/-- Two placed parts `t₁ + P₁` and `t₂ + P₂` are disjoint when their relative
offset `t₂ - t₁` avoids the NFP `P₁ - P₂` — the pairwise nester invariant that,
applied over all pairs, gives the "zero overlap" contract. -/
theorem placed_disjoint_of_rel_not_mem_nfp (P₁ P₂ : Set E) (t₁ t₂ : E)
    (h : t₂ - t₁ ∉ P₁ - P₂) :
    Disjoint ((fun y => t₁ + y) '' P₁) ((fun y => t₂ + y) '' P₂) := by
  rw [Set.disjoint_iff_inter_eq_empty, ← Set.not_nonempty_iff_eq_empty]
  rintro ⟨x, ⟨a, ha, rfl⟩, b, hb, hxb⟩
  -- hxb : t₂ + b = t₁ + a ; then a - b = t₂ - t₁ ∈ P₁ - P₂, contradicting h
  refine h (Set.mem_sub.2 ⟨a, ha, b, hb, ?_⟩)
  have ht2 : t₂ = t₁ + a - b := by rw [eq_sub_iff_add_eq]; exact hxb
  rw [ht2]; abel

end Frahan
