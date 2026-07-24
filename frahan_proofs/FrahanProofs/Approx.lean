import FrahanProofs.Common

/-!
Frahan StonePack — approximation-ratio cores (bin packing, convex partition).

These are the ARITHMETIC cores of two approximation bounds, with the geometric /
algorithmic facts they rest on stated as explicit named hypotheses (the same
honest pattern as `thm:lpt` in `Machines.lean`: prove the counting, cite the
structural facts). The tight research-scale constants (`FFD 11/9·OPT + 6/9`,
Dósa; the exact Hertel–Mehlhorn geometry) are `proof_wanted`.
-/

namespace Frahan

/-- tex (FFD / First-Fit bin packing), the elementary 2-approximation core.
Capacity normalized to 1; `total` = sum of item sizes, `opt` = optimal bin count
(so `total ≤ opt`, each optimal bin holds ≤ 1). The First-Fit invariant — at
most one open bin is ≤ half full (two half-empty bins would have merged) — gives
`(k−1)/2 < total` for the `k` bins First-Fit opens. Hence `k < 2·opt + 1`. -/
theorem firstFit_lt_two_opt (k : ℕ) (total opt : ℝ)
    (hopt : total ≤ opt) (hff : ((k : ℝ) - 1) / 2 < total) :
    (k : ℝ) < 2 * opt + 1 := by
  linarith

/-
tex (FFD), the tight bound (Johnson; Dósa tight additive): First-Fit-Decreasing
uses `FFD ≤ 11/9·OPT + 6/9` bins. This is PROSE, not a `proof_wanted`, on purpose:
a faithful statement needs the full First-Fit-Decreasing bin-packing MODEL (items,
bins, the FFD assignment as a function, OPT as a minimum over valid packings —
none of which Mathlib carries), and the proof is Dósa's (2007) weighting-function
case analysis, one of the longest in combinatorial optimization (~40 pp). It is a
research-scale formalization, not a proof that can be written now. The provable
FFD content — the elementary `< 2·OPT` bound — is `firstFit_lt_two_opt` above.
(An earlier `proof_wanted ffd_tight` with a `hbound : True` placeholder was
removed: with that placeholder the asserted statement was FALSE, so it was not a
legitimate `proof_wanted`.)
-/

/-- tex Theorem `thm:hm` (Hertel–Mehlhorn), the 4-approximation counting core.
`r` = number of reflex vertices, `P` = pieces the algorithm emits, `opt` = pieces
in a minimum convex partition. The geometric facts (cited): HM keeps ≤ 2 essential
diagonals per reflex vertex, so `P ≤ 2r + 1`; and each optimal convex piece
resolves ≤ 2 reflex vertices, so `r + 1 ≤ 2·opt`. These give the 4-approximation
`P ≤ 4·opt`. -/
theorem hertelMehlhorn_four_opt (P r opt : ℕ)
    (hHM : P ≤ 2 * r + 1) (hOPT : r + 1 ≤ 2 * opt) : P ≤ 4 * opt := by
  omega

end Frahan
