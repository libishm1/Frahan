import FrahanProofs.Common

/-!
Frahan StonePack — learned reassembly pose algebra + quadric error metric.

Mechanizes two named results of `spec/frahan_algorithm_derivations.tex`:

  * tex Theorem `thm:kintsugi` (World-pose composition, §"Learned
    reassembly"): the Kintsugi / PotNet world pose
    `T_world(f) = T_unnorm(0) · T_net(f) · T_norm(f)` is the composition
    of the three-stage pipeline, is the unique transform realizing it
    (faithful action), and — under a global pre-transform `g` of the raw
    input — moves by conjugation `g · T_world · g⁻¹` (the assembly is
    rigid; `T_net` is literally unchanged). Modeled in an arbitrary group
    `G` with a faithful action on points, i.e. Sim(3)/SE(3) acting on ℝ³.

  * tex Theorem `thm:qem` (QEM = sum of squared plane distances, §"Mesh
    simplification"): the per-plane quadric value `ṽᵀKₚṽ = (⟪n,v⟫+d)²`
    equals the squared point-plane distance; the total QEM is a convex
    quadratic; and the normal-equations stationary point (∇ = 0, i.e.
    `Q̄v̄ = −b`) is a global minimizer. Modeled over an arbitrary real
    inner-product space `E` (the mesh case `E = ℝ³` is an instance).

Nothing here uses `sorry` or introduces an axiom.
-/

namespace Frahan

open scoped RealInnerProductSpace

/-! ### tex Theorem `thm:kintsugi` — world-pose composition -/

section Kintsugi

variable {G P F : Type*} [Group G] [MulAction G P] [FaithfulSMul G P]

/-- The Kintsugi / PotNet world pose of fragment `f`
(tex Theorem `thm:kintsugi`):
`T_world(f) = T_unnorm(0) · T_net(f) · T_norm(f)`. `Tunnorm0` is the
inverse of the anchor (fragment 0) normalization; `Tnet f` is the
network placement of the normalized fragment; `Tnorm f` normalizes
fragment `f`. Elements of `G` model Sim(3)/SE(3) transforms. -/
def worldPose (Tunnorm0 : G) (Tnet Tnorm : F → G) (f : F) : G :=
  Tunnorm0 * Tnet f * Tnorm f

omit [FaithfulSMul G P] in
/-- tex Theorem `thm:kintsugi`, "composing the pipeline": acting the
world pose on a point `p` is exactly normalize → place → undo-anchor,
`T_world(f) • p = T_unnorm(0) • (T_net(f) • (T_norm(f) • p))`. This is
associativity of the group action (`mul_smul` twice). -/
theorem worldPose_smul (Tunnorm0 : G) (Tnet Tnorm : F → G) (f : F) (p : P) :
    worldPose Tunnorm0 Tnet Tnorm f • p
      = Tunnorm0 • (Tnet f • (Tnorm f • p)) := by
  simp only [worldPose, mul_smul]

/-- tex Theorem `thm:kintsugi`, uniqueness: `worldPose` is the *unique*
transform realizing the normalize → place → undo-anchor pipeline. Any
`S` that acts on every point the same way equals `worldPose`, because the
action is faithful (`FaithfulSMul.eq_of_smul_eq_smul`). -/
theorem worldPose_unique (Tunnorm0 : G) (Tnet Tnorm : F → G) (f : F) (S : G)
    (hS : ∀ p : P, S • p = Tunnorm0 • (Tnet f • (Tnorm f • p))) :
    S = worldPose Tunnorm0 Tnet Tnorm f := by
  apply eq_of_smul_eq_smul (α := P)
  intro p
  rw [hS, worldPose_smul]

/-- tex Theorem `thm:kintsugi`, global-pose (in)variance — the *honest*
statement. Pre-transforming the raw input by `g` is absorbed by
normalization (`Tnorm` becomes `Tnorm · g⁻¹`, centroid/extent are
equivariant) and by the anchor unnormalization (`Tunnorm0` becomes
`g · Tunnorm0`). `Tnet` is literally the same function — the network sees
identical normalized geometry. The world pose is then *conjugated* by `g`:
`worldPose_g f = g · worldPose f · g⁻¹`. It is NOT unchanged (the whole
assembly moves rigidly with `g`); the invariant is `Tnet`. -/
theorem worldPose_equivariant (g Tunnorm0 : G) (Tnet Tnorm : F → G) (f : F) :
    worldPose (g * Tunnorm0) Tnet (fun k => Tnorm k * g⁻¹) f
      = g * worldPose Tunnorm0 Tnet Tnorm f * g⁻¹ := by
  simp only [worldPose, mul_assoc]

/-- tex Theorem `thm:kintsugi`, "final world relation": the *relative*
pose between two fragments also transforms by conjugation under a global
pre-transform `g`. This is the precise sense in which the reconstructed
assembly is rigid: every pairwise world relation is carried by the single
global `g`, so the shape is preserved. -/
theorem worldPose_relative_equivariant (g Tunnorm0 : G) (Tnet Tnorm : F → G)
    (f f' : F) :
    worldPose (g * Tunnorm0) Tnet (fun k => Tnorm k * g⁻¹) f
        * (worldPose (g * Tunnorm0) Tnet (fun k => Tnorm k * g⁻¹) f')⁻¹
      = g * (worldPose Tunnorm0 Tnet Tnorm f
            * (worldPose Tunnorm0 Tnet Tnorm f')⁻¹) * g⁻¹ := by
  rw [worldPose_equivariant, worldPose_equivariant]
  group

end Kintsugi

/-! ### tex Theorem `thm:qem` — QEM = sum of squared plane distances -/

section QEM

variable {E : Type*} [NormedAddCommGroup E] [InnerProductSpace ℝ E]
variable {ι : Type*}

/-- Per-plane quadric error (tex `thm:qem`, Garland–Heckbert). For the
homogeneous quadric `Kₚ = [n;d][n;d]ᵀ` of the plane `⟪n,x⟫ + d = 0` and
`ṽ = (v,1)`, the value `ṽᵀKₚṽ = (⟪n,v⟫ + d)²`; this is that value. -/
def planeErr (n : E) (d : ℝ) (v : E) : ℝ := (⟪n, v⟫ + d) ^ 2

/-- tex Theorem `thm:qem`, expansion `ṽᵀKₚṽ = (nᵀv + d)²` written against
a point `p` on the plane: `planeErr n d v = ⟪n, v − p⟫²`. Pure algebraic
identity (holds for any `n`); `⟪n, v − p⟫` is the signed distance along
the normal, and equals the Euclidean point-plane distance exactly when
`‖n‖ = 1` (see `planeErr_eq_sq_dist`). -/
theorem planeErr_eq_sq_signed (n : E) (d : ℝ) (p v : E)
    (hp : ⟪n, p⟫ + d = 0) :
    planeErr n d v = ⟪n, v - p⟫ ^ 2 := by
  simp only [planeErr]
  rw [inner_sub_right]
  have hnp : ⟪n, p⟫ = -d := by linarith
  rw [hnp]; ring

/-- tex Theorem `thm:qem`, `= dist(v,p)²` in full: with a UNIT normal
`‖n‖ = 1` and `p` on the plane, `planeErr n d v = ‖⟪n, v − p⟫ • n‖²`,
the squared Euclidean distance from `v` to the plane (the perpendicular
offset is `⟪n, v − p⟫ • n`). Uses `‖n‖ = 1` to drop the normal's length. -/
theorem planeErr_eq_sq_dist (n : E) (d : ℝ) (p v : E)
    (hn : ‖n‖ = 1) (hp : ⟪n, p⟫ + d = 0) :
    planeErr n d v = ‖⟪n, v - p⟫ • n‖ ^ 2 := by
  have hc : ⟪n, v - p⟫ = ⟪n, v⟫ + d := by
    rw [inner_sub_right]; linarith
  simp only [planeErr]
  rw [norm_smul, hn, mul_one, Real.norm_eq_abs, sq_abs, hc]

/-- Single-plane QEM is convex: `v ↦ (⟪n,v⟫ + d)²` is the square of an
affine functional, hence convex (Jensen for `t ↦ t²`). Supporting
`qem_convexOn`. -/
theorem planeErr_convexOn (m : E) (c : ℝ) :
    ConvexOn ℝ Set.univ (fun v => planeErr m c v) := by
  refine ⟨convex_univ, fun x _ y _ a b ha hb hab => ?_⟩
  have hb' : b = 1 - a := by linarith
  subst hb'
  simp only [planeErr, smul_eq_mul]
  have hin : ⟪m, a • x + (1 - a) • y⟫ = a * ⟪m, x⟫ + (1 - a) * ⟪m, y⟫ := by
    rw [inner_add_right, real_inner_smul_right, real_inner_smul_right]
  rw [hin]
  nlinarith [mul_nonneg (mul_nonneg ha hb) (sq_nonneg (⟪m, x⟫ - ⟪m, y⟫)),
    ha, hb, sq_nonneg (⟪m, x⟫ - ⟪m, y⟫)]

/-- tex Theorem `thm:qem`, convexity: the total QEM
`v ↦ ∑ᵢ (⟪nᵢ,v⟫ + dᵢ)²` is a convex quadratic (a finite sum of squared
affine functionals). Proved by folding `ConvexOn.add` over the finite
plane set. -/
theorem qem_convexOn (s : Finset ι) (n : ι → E) (d : ι → ℝ) :
    ConvexOn ℝ Set.univ (fun v => ∑ i ∈ s, planeErr (n i) (d i) v) := by
  classical
  induction s using Finset.induction_on with
  | empty => simpa using convexOn_const (0 : ℝ) convex_univ
  | insert a t ha ih =>
      have hsplit : (fun v => ∑ i ∈ insert a t, planeErr (n i) (d i) v)
          = (fun v => planeErr (n a) (d a) v)
            + fun v => ∑ i ∈ t, planeErr (n i) (d i) v := by
        funext v
        simp only [Pi.add_apply, Finset.sum_insert ha]
      rw [hsplit]
      exact (planeErr_convexOn (n a) (d a)).add ih

/-- tex Theorem `thm:qem`, "the optimum solves `∇(vᵀQv) = 0`, i.e.
`Q̄v̄ = −b`": the normal equations imply a GLOBAL minimum. If `v0`
satisfies the vector normal equation `∑ᵢ (⟪nᵢ,v0⟫ + dᵢ) • nᵢ = 0`
(the gradient of the total QEM, `Q̄v0 + b = 0`), then `v0` minimizes the
total QEM. Proved elementarily by expanding
`Q(v0 + u) = Q(v0) + 2⟪∑(⟪nᵢ,v0⟫+dᵢ)•nᵢ, u⟫ + ∑⟪nᵢ,u⟫²`: the cross term
vanishes by hypothesis and the residual sum of squares is `≥ 0`. -/
theorem qem_min_of_normal_eq (s : Finset ι) (n : ι → E) (d : ι → ℝ) (v0 : E)
    (hnormal : ∑ i ∈ s, (⟪n i, v0⟫ + d i) • n i = 0) (w : E) :
    (∑ i ∈ s, planeErr (n i) (d i) v0) ≤ ∑ i ∈ s, planeErr (n i) (d i) w := by
  have expand : ∀ i ∈ s, planeErr (n i) (d i) w
      = planeErr (n i) (d i) v0
        + 2 * ((⟪n i, v0⟫ + d i) * ⟪n i, w - v0⟫)
        + ⟪n i, w - v0⟫ ^ 2 := by
    intro i _
    simp only [planeErr]
    have h1 : ⟪n i, w⟫ = ⟪n i, v0⟫ + ⟪n i, w - v0⟫ := by
      rw [inner_sub_right]; ring
    rw [h1]; ring
  have hsum : ∑ i ∈ s, planeErr (n i) (d i) w
      = (∑ i ∈ s, planeErr (n i) (d i) v0)
        + (∑ i ∈ s, 2 * ((⟪n i, v0⟫ + d i) * ⟪n i, w - v0⟫))
        + ∑ i ∈ s, ⟪n i, w - v0⟫ ^ 2 := by
    rw [Finset.sum_congr rfl expand, Finset.sum_add_distrib, Finset.sum_add_distrib]
  have hcross : ∑ i ∈ s, 2 * ((⟪n i, v0⟫ + d i) * ⟪n i, w - v0⟫) = 0 := by
    have h2 : ∑ i ∈ s, 2 * ((⟪n i, v0⟫ + d i) * ⟪n i, w - v0⟫)
        = 2 * ⟪∑ i ∈ s, (⟪n i, v0⟫ + d i) • n i, w - v0⟫ := by
      rw [sum_inner, Finset.mul_sum]
      refine Finset.sum_congr rfl fun i _ => ?_
      rw [real_inner_smul_left]
    rw [h2, hnormal, inner_zero_left, mul_zero]
  rw [hsum, hcross, add_zero]
  have hnn : 0 ≤ ∑ i ∈ s, ⟪n i, w - v0⟫ ^ 2 :=
    Finset.sum_nonneg fun i _ => sq_nonneg _
  linarith

end QEM

end Frahan
