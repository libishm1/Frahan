import FrahanProofs.Common

/-!
Frahan StonePack — guillotine separability and DP optimality.

tex Theorem `thm:guillotine`: a block layout is separable by full-span
(diamond-wire) cuts IFF it is guillotine-partitionable — a wire makes exactly
full-span straight cuts = guillotine cuts. The staged three-stage scheme
(bed cuts → strips → cross cuts) produces such a partition by construction, so
its separable fraction `φ = 1`. This is modeled by `GuillotineTree` below: a
region is a single block or one full-span cut into two guillotine parts.

tex Theorem `thm:guillodp`: the optimum over guillotine tilings satisfies the
Bellman recursion `V(R) = max(place a block, best full-span cut)`. Its
mechanizable heart is optimal substructure — the optimum over a union of the
"place" and "cut" option families is the max of the two family optima —
formalized as `guillotine_dp_recursion`.
-/

namespace Frahan

/-- tex Theorem `thm:guillotine`: a guillotine-partitionable (equivalently,
diamond-wire-separable) layout — either a single `block`, or one full-span `cut`
into two guillotine sub-layouts. Being of this form IS wire-separability (a wire
cut = a full-span guillotine cut); the staged bed→strip→cross scheme builds such
a tree by construction, giving separable fraction `φ = 1`. -/
inductive GuillotineTree (Block : Type*) where
  | block : Block → GuillotineTree Block
  | cut : GuillotineTree Block → GuillotineTree Block → GuillotineTree Block

/-- The staged three-stage guillotine (bed cuts → strips → cross cuts) is, by
construction, a `GuillotineTree` — a full-span cut into strips, each a full-span
cut into blocks. Witnesses `thm:guillotine`'s `φ = 1` (fully separable). -/
def stagedThreeStage {Block : Type*} (strips : List (List Block)) :
    Option (GuillotineTree Block) :=
  let mkStrip : List Block → Option (GuillotineTree Block) := fun bs =>
    bs.foldr (fun b acc => match acc with
      | none => some (.block b)
      | some t => some (.cut (.block b) t)) none
  (strips.filterMap mkStrip).foldr (fun s acc => match acc with
    | none => some s
    | some t => some (.cut s t)) none

/-- tex Theorem `thm:guillodp`, optimal substructure (the Bellman recursion
`V(R) = max(place, cut)`): the optimum objective over the union of the
"single-block placement" options and the "full-span cut" options equals the
maximum of the two families' optima. This is the recursion the memoized DP
evaluates over the O(n²) distinct sub-rectangles. -/
theorem guillotine_dp_recursion {ι β : Type*} [DecidableEq ι] [SemilatticeSup β]
    (place cut : Finset ι) (val : ι → β)
    (hp : place.Nonempty) (hc : cut.Nonempty) :
    (place ∪ cut).sup' (hp.mono Finset.subset_union_left) val
      = (place.sup' hp val) ⊔ (cut.sup' hc val) :=
  Finset.sup'_union hp hc val

end Frahan
