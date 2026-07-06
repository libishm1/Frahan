/-!
# Packing theorems (Lean-verified)

Combinatorial results behind the packing / recovery stack, provable in core
Lean 4. Each theorem cites the derivation it formalizes.
-/
namespace FrahanProofs

/-- RecoveryCascade recovered value = best over re-cut scales (max over the
    per-scale recoveries). Derivation: RecoveryCascade multi-scale monotone. -/
def cascade : List Nat → Nat
  | []      => 0
  | x :: xs => Nat.max x (cascade xs)

/-- **RecoveryCascade monotonicity.** Re-cutting a cracked block at finer
    scales never recovers less than any single scale already in the cascade. -/
theorem cascade_ge_mem : ∀ (xs : List Nat) (v : Nat), v ∈ xs → v ≤ cascade xs := by
  intro xs
  induction xs with
  | nil => intro v h; cases h
  | cons x xs ih =>
    intro v h
    simp only [cascade]
    rcases List.mem_cons.mp h with h1 | h2
    · rw [h1]; exact Nat.le_max_left x (cascade xs)
    · exact Nat.le_trans (ih v h2) (Nat.le_max_right x (cascade xs))

/-- **Orientation count.** The 3D packer enumerates every (up-face, in-plane)
    pair: 6 up-faces x 4 quarter-turns = 24 orientations (the cube's rotation
    group order). Derivation: 24-orientation cube group. -/
theorem orientation_count :
    ((List.range 6).flatMap (fun f => (List.range 4).map (fun r => (f, r)))).length = 24 := by
  decide

/-- **Guillotine cut loses no item.** A full-span cut (predicate `p` = "on the
    low side") partitions the items so every item is on exactly one side; the
    recursion covers the whole set. Derivation: guillotine separability. -/
theorem guillotine_no_loss {α : Type} (p : α → Bool) (xs : List α) (x : α) :
    x ∈ xs ↔ (x ∈ xs.filter p ∨ x ∈ xs.filter (fun a => !p a)) := by
  simp only [List.mem_filter]
  cases hp : p x <;> simp [hp]

end FrahanProofs
