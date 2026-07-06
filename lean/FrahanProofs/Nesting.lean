/-!
# Nesting theorems (Lean-verified)

Combinatorial facts behind the 2D nesting / trimming stack.
-/
namespace FrahanProofs

/-- **Sutherland–Hodgman clip subset** (Slab Trim I). Clipping a polygon's
    vertex list to those satisfying an inside-predicate yields a subset of the
    original vertices, so a greedy convex trim can only shrink the blank. -/
theorem clip_subset {α : Type} (p : α → Bool) (xs : List α) :
    ∀ x ∈ xs.filter p, x ∈ xs := fun _ hx => (List.mem_filter.mp hx).1

/-- **1D no-fit interval** (the Minkowski-sum NFP building block). Interval
    `[a₁,a₂]` shifted by `p` overlaps `[b₁,b₂]` iff `p ∈ [b₁-a₂, b₂-a₁]`. This
    is `NFP(A,B) = A ⊕ (−B)` on ℤ, decided by `omega`. The 2D no-fit polygon is
    the coordinate-wise conjunction of this per axis for axis-aligned parts. -/
theorem nfp_interval (a1 a2 b1 b2 p : Int) (ha : a1 ≤ a2) (hb : b1 ≤ b2) :
    (max (a1 + p) b1 ≤ min (a2 + p) b2) ↔ (b1 - a2 ≤ p ∧ p ≤ b2 - a1) := by
  omega

/-- **Bottom-left minimum is attained.** Over a nonempty candidate list, the
    lexicographic `(y,x)` minimum is a member and is `≤` every candidate, so the
    BLF rule's argmin exists (a linear objective over the feasible vertices
    attains its min at a vertex). Derivation: bottom-left placement rule. -/
theorem blf_argmin_mem :
    ∀ (cs : List (Nat × Nat)), cs ≠ [] →
      ∃ m ∈ cs, ∀ q ∈ cs, m.1 < q.1 ∨ (m.1 = q.1 ∧ m.2 ≤ q.2)
  | [], h => absurd rfl h
  | [d], _ => ⟨d, by simp, by intro q hq; simp only [List.mem_singleton] at hq; subst hq; omega⟩
  | d :: e :: es, _ => by
      obtain ⟨m, hm, hmin⟩ := blf_argmin_mem (e :: es) (by simp)
      by_cases h : d.1 < m.1 ∨ (d.1 = m.1 ∧ d.2 ≤ m.2)
      · refine ⟨d, by simp, ?_⟩
        intro q hq
        rcases List.mem_cons.mp hq with rfl | hq'
        · omega
        · have := hmin q hq'; omega
      · refine ⟨m, List.mem_cons.mpr (Or.inr hm), ?_⟩
        intro q hq
        rcases List.mem_cons.mp hq with rfl | hq'
        · omega
        · exact hmin q hq'

end FrahanProofs
