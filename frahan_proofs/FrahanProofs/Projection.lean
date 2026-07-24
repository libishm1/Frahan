import FrahanProofs.Common

/-!
Frahan StonePack — equal-area (Lambert/Schmidt) projection.

Mechanizes tex Theorem `thm:lambert` (equal-area Lambert/Schmidt projection),
the analytic heart of the joint-pole density plots used in §13. The lower-
hemisphere radial map `r(θ) = √2 · sin(θ/2)` (colatitude `θ`, azimuth `φ`)
onto the disk is area-preserving: it sends the spherical area element
`sinθ dθ dφ` to `r dr dφ` up to the constant factor. Hence pole DENSITY on
the Schmidt net is undistorted (unlike the equal-angle Wulff net), so
contoured pole densities give unbiased joint-set strengths.

The full change-of-variables statement `∫ over disk = const · ∫ over sphere`
is the corollary; its analytic heart is the Jacobian identity
`r dr = (1/2) sinθ dθ`, which is what is mechanized here (the `dφ` factor is
untouched by the radial map, so the whole Jacobian reduces to this 1-D
identity). PROVED, no sorry:

  * `lambert_hasDerivAt` — the derivative `r'(θ) = √2 · cos(θ/2) · ½`
    (chain rule: `sin` ∘ `(·/2)`, scaled by the constant `√2`).
  * `lambert_r_dr` — the area-element identity in explicit `r dr` form:
    `r(θ) · r'(θ) = ½ sinθ`. This is exactly the tex proof line
    `r dr = √2 sin(θ/2)·√2·½cos(θ/2) dθ = ½ sinθ dθ`, using
    `√2·√2 = 2` (`Real.mul_self_sqrt`) and `sinθ = 2 sin(θ/2) cos(θ/2)`
    (`Real.sin_two_mul` at `θ/2`).
  * `lambert_area_element` — the same identity phrased with Mathlib's
    `deriv`: `r(θ) · deriv r θ = ½ sinθ`.
-/

namespace Frahan

/-- The equal-area (Lambert/Schmidt) radial map `r(θ) = √2 · sin(θ/2)`
onto the disk (tex Theorem `thm:lambert`; `θ` = colatitude). -/
noncomputable def r (θ : ℝ) : ℝ := Real.sqrt 2 * Real.sin (θ / 2)

/-- tex Theorem `thm:lambert`, derivative step: `r'(θ) = √2 · cos(θ/2) · ½`.
Chain rule for `sin` composed with `θ ↦ θ/2`, scaled by the constant `√2`. -/
theorem lambert_hasDerivAt (θ : ℝ) :
    HasDerivAt r (Real.sqrt 2 * Real.cos (θ / 2) * (1 / 2)) θ := by
  -- inner map `θ ↦ θ/2` has derivative `½`
  have hhalf : HasDerivAt (fun t : ℝ => t / 2) (1 / 2) θ :=
    (hasDerivAt_id θ).div_const 2
  -- chain rule: `θ ↦ sin(θ/2)` has derivative `cos(θ/2) · ½`
  have hsin : HasDerivAt (fun t : ℝ => Real.sin (t / 2))
      (Real.cos (θ / 2) * (1 / 2)) θ :=
    hhalf.sin
  -- scale by the constant `√2`
  have hr := hsin.const_mul (Real.sqrt 2)
  -- reassociate the derivative value to the stated form
  have hval : Real.sqrt 2 * (Real.cos (θ / 2) * (1 / 2))
      = Real.sqrt 2 * Real.cos (θ / 2) * (1 / 2) := by ring
  rw [hval] at hr
  exact hr

/-- tex Theorem `thm:lambert`, area-element identity in explicit `r dr` form:
`r(θ) · r'(θ) = ½ sinθ`, with `r'(θ) = √2 cos(θ/2)·½`. This is the exact
tex proof computation
`r dr = √2 sin(θ/2)·√2·½cos(θ/2) dθ = sin(θ/2)cos(θ/2) dθ = ½ sinθ dθ`. -/
theorem lambert_r_dr (θ : ℝ) :
    r θ * (Real.sqrt 2 * Real.cos (θ / 2) * (1 / 2)) = (1 / 2) * Real.sin θ := by
  -- `sinθ = 2 sin(θ/2) cos(θ/2)` (double-angle at `θ/2`, since `2·(θ/2) = θ`)
  have hsinθ : Real.sin θ = 2 * Real.sin (θ / 2) * Real.cos (θ / 2) := by
    have h := Real.sin_two_mul (θ / 2)
    rwa [show (2 : ℝ) * (θ / 2) = θ from by ring] at h
  -- `√2 · √2 = 2`
  have hsqrt : Real.sqrt 2 * Real.sqrt 2 = 2 := Real.mul_self_sqrt (by norm_num)
  simp only [r]
  rw [hsinθ]
  linear_combination (1 / 2 * Real.sin (θ / 2) * Real.cos (θ / 2)) * hsqrt

/-- tex Theorem `thm:lambert`, area-element preservation phrased with
Mathlib's `deriv`: `r(θ) · deriv r θ = ½ sinθ`. Equivalently `r dr = ½ sinθ dθ`,
so the spherical element `sinθ dθ dφ` maps to `r dr dφ = ½ sinθ dθ dφ`, a
constant multiple — the Jacobian is constant, i.e. the map is area-preserving.
The full `∫ disk = const · ∫ sphere` change of variables is the corollary. -/
theorem lambert_area_element (θ : ℝ) :
    r θ * deriv r θ = (1 / 2) * Real.sin θ := by
  rw [(lambert_hasDerivAt θ).deriv]
  exact lambert_r_dr θ

end Frahan
