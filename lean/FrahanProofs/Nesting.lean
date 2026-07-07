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

/-- Lexicographic (y, x) order on candidates, as the nester compares them. -/
def lexLe (a b : Nat × Nat) : Prop := a.1 < b.1 ∨ (a.1 = b.1 ∧ a.2 ≤ b.2)

theorem lexLe_refl (a : Nat × Nat) : lexLe a a := Or.inr ⟨rfl, Nat.le_refl _⟩

/-- **The shipping BLF selection rule is optimal.** `ContactNfpHoleNester`
    sorts candidates by `(y, x)` (`OrderedVertices`) and accepts the FIRST one
    that passes the verification gate (`find?`). This proves that strategy
    correct: on a lex-sorted list, the first gate-survivor is the lexicographic
    minimum of ALL survivors — scanning in order loses nothing. -/
theorem first_verified_is_lexmin (p : Nat × Nat → Bool) :
    ∀ (xs : List (Nat × Nat)), List.Pairwise lexLe xs →
      ∀ m, xs.find? p = some m → ∀ q ∈ xs, p q → lexLe m q := by
  intro xs
  induction xs with
  | nil => intro _ m hm; simp [List.find?] at hm
  | cons x xs ih =>
    intro hpw m hm q hq hpq
    have hpw' := List.pairwise_cons.mp hpw
    cases hpx : p x with
    | true =>
      simp only [List.find?_cons, hpx] at hm
      cases hm
      rcases List.mem_cons.mp hq with rfl | hq'
      · exact lexLe_refl q
      · exact hpw'.1 q hq'
    | false =>
      simp only [List.find?_cons, hpx] at hm
      rcases List.mem_cons.mp hq with rfl | hq'
      · rw [hpx] at hpq; cases hpq
      · exact ih hpw'.2 m hm q hq' hpq

end FrahanProofs
