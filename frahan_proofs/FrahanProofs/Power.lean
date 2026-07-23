import FrahanProofs.Common

/-!
Frahan StonePack — power diagram cells.

tex Proposition `prop:power`: the power cell of a weighted site is an
intersection of half-spaces, hence a convex polytope region — the
quadratic terms `‖x‖²` cancel in the power inequality
`‖x − p‖² − w ≤ ‖x − q‖² − w_q`, leaving a linear constraint.
This underlies the power-diagram / weighted-Voronoi partitions used by
the packing and tessellation components.
-/

namespace Frahan

open scoped RealInnerProductSpace

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]

/-- Half-spaces of the form `{x | ⟪x, ν⟫ ≤ c}` are convex (elementary
proof in the style of `Halfplane.convex`). -/
theorem convex_inner_le (ν : E) (c : ℝ) :
    Convex ℝ {x : E | ⟪x, ν⟫ ≤ c} := by
  intro x hx y hy a b ha hb hab
  simp only [Set.mem_setOf_eq] at hx hy ⊢
  rw [inner_add_left, real_inner_smul_left, real_inner_smul_left]
  have h1 : a * ⟪x, ν⟫ ≤ a * c := mul_le_mul_of_nonneg_left hx ha
  have h2 : b * ⟪y, ν⟫ ≤ b * c := mul_le_mul_of_nonneg_left hy hb
  have hc : a * c + b * c = c := by rw [← add_mul, hab, one_mul]
  linarith

/-- The single power constraint against one site is a half-space: the
quadratic terms cancel. -/
theorem powerConstraint_eq_halfspace (p : E) (w : ℝ) (q : E) (wq : ℝ) :
    {x : E | ‖x - p‖ ^ 2 - w ≤ ‖x - q‖ ^ 2 - wq} =
    {x : E | ⟪x, (2 : ℝ) • (q - p)⟫ ≤ ‖q‖ ^ 2 - wq - ‖p‖ ^ 2 + w} := by
  ext x
  simp only [Set.mem_setOf_eq, norm_sub_sq_real, real_inner_smul_right,
    inner_sub_right]
  constructor <;> intro h <;> linarith

/-- tex Proposition `prop:power`: the power cell of site `p` with weight
`w` among weighted sites `Q` is convex — an intersection of half-spaces.
(Discharges the `proof_wanted` from the roadmap.) -/
theorem powerCell_convex (p : E) (w : ℝ) (Q : List (E × ℝ)) :
    Convex ℝ {x : E | ∀ qw ∈ Q, ‖x - p‖ ^ 2 - w ≤ ‖x - qw.1‖ ^ 2 - qw.2} := by
  have hset : {x : E | ∀ qw ∈ Q, ‖x - p‖ ^ 2 - w ≤ ‖x - qw.1‖ ^ 2 - qw.2} =
      ⋂ qw ∈ Q, {x : E | ‖x - p‖ ^ 2 - w ≤ ‖x - qw.1‖ ^ 2 - qw.2} := by
    ext x; simp
  rw [hset]
  refine convex_iInter₂ fun qw _ => ?_
  rw [powerConstraint_eq_halfspace]
  exact convex_inner_le _ _

end Frahan
