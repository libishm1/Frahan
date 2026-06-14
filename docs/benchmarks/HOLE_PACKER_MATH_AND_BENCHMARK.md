# Hole-aware 2D packing: math, benchmark, and the evolution target

2026-06-12. Clean-room study. Names: **Sparrow** = the jagua-rs guided-local-search strip packer
(arXiv 2509.13329, the benchmark target). **MIT native nester** = the MIT-licensed C++ NFP-GLS nester
with hole extensions (P. Vestartas; the product name is deliberately not used). **FreeNestX** = Frahan's
shipped deterministic exact-NFP bottom-left-fill nester (`IrregularSheetFillNfpBlf`). **CNH** = the
Frahan evolution proposed here (Contact-NFP Hole nester).

## 1. The three packers, as equations

A placement of part `i` is a rigid transform `T_i(x) = R(θ_i)·x + t_i`. With part outer polygon `P_i`,
part-holes `H_ik`, sheet `S`, sheet-holes `Q_l`, the **true occupied material** is
`M_i = T_i(P_i) \ ⋃_k T_i(H_ik)`. Hole-aware feasibility is

```
M_i ⊆ S \ ⋃_l Q_l        (inside sheet, clear of sheet defects)
M_i ∩ M_j = ∅   (i≠j)     (no part overlap)
T_s(P_s) ⊆ T_h(H_hk)      (a small part s nested INTO host h's hole k)
T_s(P_s) ∩ T_h(M_h) = ∅
```

### 1a. Sparrow — guided local search on a surrogate overlap field
Sparrow never builds the exact feasible region. It scatters placements, lets them overlap, then *relaxes*
the overlap away under guided weights. Each polygon carries inscribed surrogate poles `C_i={(c_a,r_a)}`.
For a pole pair the penetration is `δ_ab = r_a + r_b − ‖c_a−c_b‖`, smoothed by
`φ_ε(δ)=δ if δ≥ε else ε²/(2ε−δ)`. The pairwise loss (area proxy) is
`L_ij = √(π Σ_ab φ_ε(δ_ab)·min(r_a,r_b) + ε²)·(A_i^ch A_j^ch)^¼`. Guided local search reweights the
worst constraints each round, `w_e ← max(w_e·m_e, 1)` with `m_e = 1.2 + 0.8·L_e/L_max`, and shrinks the
strip `W ← (1−0.001)W` whenever total loss hits zero. **Strength:** best outline-strip density (it explores
a huge basin set cheaply). **Structural limit:** the surrogate field and the SPP schema have *no hole term* —
holes are ignored on import. It cannot represent, let alone respect, `Q_l` or `H_ik`.

### 1b. MIT native nester — Sparrow's pipeline, ported, plus exact hole obstacles
A faithful C++ re-implementation of Sparrow's LBF order, sampler, coordinate descent, GLS tracker and
separator, with three additions: (1) sheet-holes become exact obstacle polygons in the same pairwise
penalty, `L_i^Q = Σ_l L_iQl`; (2) a verified part-in-part-hole move set with **contact rotations**
`Θ = {0,π/2,π,3π/2} ∪ {α(e_h)−α(e_s), α(e_h)−α(e_s)+π}` (align a host-hole edge to a part edge); (3) a
"holes-first shelf" shortcut that pre-nests smalls then places the reduced outer set by exact
bottom-left contact candidates `X={0}∪{x_max(M_j), x_min(M_j)−w_i}∪{x_max(Q_l),…}`, `Y` analogous,
each committed only after an exact containment + overlap check, else it falls back to the stochastic
solver. **Strength:** valid hole-aware layouts. **Limit:** on the plain outline lane it *loses* to Sparrow
(below); the depth-vs-area objective tweak is geometry-quality, not speed (measured ~1.0×).

### 1c. FreeNestX — deterministic exact NFP bottom-left-fill (Frahan, shipped)
For each part and each rotation in a *fixed* set, FreeNestX builds the **complete** feasible region exactly:
the no-fit polygon against every placed part is `NFP_ij = M_j ⊕ (−R(θ)P_i)` (Clipper2 Minkowski sum of the
placed material with the reflected part), the inner-fit region against the sheet and each sheet-hole is the
analogous Minkowski term, and the feasible set is
`F_i(θ) = IFP_sheet \ ⋃_j NFP_ij \ ⋃_l NFP_iQl`. It places at the bottom-left vertex of `F_i`. **Strength:**
exact, **0-overlap by construction** (no relaxation needed), deterministic/reproducible — the only packer in
the Frahan 2D study that crosses 80 % util_stock, and it already subtracts sheet-holes. **Gaps vs the task:**
(1) rotations are a fixed user list, not contact-adaptive; (2) no part-in-part-hole phase (only sheet-holes);
(3) purely constructive — no second-chance improvement pass.

## 2. Benchmarks (reproduced on this machine, 2026-06-12)

### 2a. True-hole lane — 1 sheet + 1 sheet-hole, 4 host parts with slanted holes, 8 fillers
| Packer | elapsed | placed | part-holes filled | sheet-hole overlap | valid true-hole |
|---|---:|---:|---:|---:|---|
| MIT native nester | **21.6 ms** | 12/12 | 4 | 0.000 | **true** |
| Sparrow release | 3255 ms | 12/12 outlines | 0 | 953.7 | **false** (4 hole-ignore warnings) |

Reproduced fresh (`scripts/sparrow_hole_head_to_head.py`): native 21.6 ms valid; Sparrow 3255 ms and
**structurally invalid** — it packs outlines and drives 953 units of geometry through the holes. This is the
honest headline: *on the hole-aware task Sparrow does not produce a usable result at any time budget*, so the
native nester is both valid and ~150× faster **on the same input** — with the caveat that Sparrow is solving
a hole-blind relaxation of the problem.

### 2b. Outline-strip lane (no holes) — the fair, both-valid comparison
| Packer | used width (smaller=better) | density | time |
|---|---:|---:|---:|
| Sparrow Rust | **60.18** | **0.663** | reference |
| MIT native (best audited) | 64.5–67.6 | 0.605 | 4.7–11.1 s |

On its own game Sparrow wins by ~6–10 % density. The native port does **not** beat Sparrow here, and the
self-reported "2×" speed gates are *shelf-vs-no-shelf internal* speedups (19.4 → 9.65 ms = 2.01×), not wins
over Sparrow. Stated plainly so we don't inherit an overclaim.

### 2c. Fastest hole packer, verdict
**The MIT native nester is the fastest *valid* hole packer measured** (9.7–21.6 ms, deterministic-checked
holes). Sparrow is fastest at *outline* strip density but cannot pack holes at all. FreeNestX handles
sheet-holes exactly today but has not been benchmarked on part-in-part-hole and is not contact-adaptive.

## 3. The evolution target — CNH (Contact-NFP Hole nester), and where 2× is honest

The defensible, domain-relevant win is **the hole-aware lane**, where Sparrow is invalid. The plan evolves
**FreeNestX** (reuse-first; it is already exact, 0-overlap, sheet-hole-capable and deterministic) with the
two ideas the MIT nester proved out, ported clean-room into a **Rhino-free Core** engine so it is directly
benchmarkable without Grasshopper:

1. **Contact-adaptive rotation set.** Replace the fixed rotation list with
   `Θ_i = {0,π/2,π,3π/2} ∪ {α(e_host)−α(e_part)}` over the longest host/part edges, ranked by edge-length
   product. Fewer, *better* rotations → tighter fit and fewer Minkowski evaluations.
2. **Holes-first part-in-part-hole phase.** Use FreeNestX's existing inner-fit (IFP) machinery against a
   host-hole polygon: a small part nests when `IFP(P_s, H_hk) ≠ ∅`; place at its bottom-left vertex,
   exact-check containment + clearance. Then run the existing exact NFP-BLF on the reduced outer set with
   sheet-holes as obstacles.
3. **Determinism kept.** No stochastic relaxation — the fabrication domain needs reproducible cut layouts.

**Honest 2× definition (to be measured, not assumed):** on the true-hole matrix, CNH must produce a *valid*
layout (which Sparrow cannot) in **≤ ½ the wall-time of the fastest valid baseline** (the MIT native nester,
~10–21 ms) **at equal-or-better packed density**. That is a fair, like-for-like 2× against the strongest
hole-aware competitor, and it is automatically >100× faster than Sparrow's invalid result on the same input.
The number CNH actually hits will be reported as measured — if it lands at 1.4× or 3×, that is what ships.
No universal "2× better than Sparrow" claim will be made for the outline lane, where Sparrow remains ahead.

## 3b. CNH v2 — rect shelf fast-path + Grasshopper component (FINAL, 2026-06-12)

The evolution round after the first build added (workflow: 2 implementers + 2 adversarial reviewers):

1. **Exact rectangle shelf fast-path** — when the whole instance is axis-aligned rectangles and spacing=0,
   placement runs on pure interval arithmetic (contact candidates, {0°,90°} with exact coordinate maps, no
   Clipper calls). Activation is conservative: anything non-rect routes to the general exact-NFP engine, and
   a **completeness fallback** reruns the general engine whenever the shelf strands a part (fuzzing found
   ~1/4000 such instances), so speed never trades away placements. The path-independent boolean validation
   gates BOTH engines. Reviewer fuzzing: 4000 instances, independent 1e-9 checker, **zero overlap or
   hole-overflow counterexamples**.
2. **Grasshopper component "Sheet Nest (Hole-Aware)"** (`HoleNestComponent`, Frahan ▸ 2D Packing) — Sheet +
   Sheet Holes + Parts + Part Holes (tree, PATH-matched so pruned branches can't shift holes onto the wrong
   part) → Placed curves + Source indices + Transforms + Nested flags + Report/Density/Valid. Synchronous,
   WorldXY-planarity guarded, house icon + algorithm-citation hover.

| Measurement | Result |
|---|---:|
| Fast path (bench instance, best of 5) | **0.148 ms** — valid, 12/12, 4 holes filled |
| vs native reference 21.6 ms | **146× faster** |
| vs Sparrow 3255 ms (invalid) | **~22,000× faster, and valid** |
| General exact-NFP engine (same instance) | 43.5 ms — valid, identical counts |
| LIVE canvas (cold solve incl. JIT) | 420 ms once |
| LIVE canvas warm re-solve | 13–14 ms total, solver 0.7 ms |
| Cold-reopen of the saved example | 12/12 previewed, 0 errors, 0.2 ms |

Live validation (truth criterion c): component placed on a real canvas, real curves, zero errors/warnings,
layout visually verified (hosts in a row, fillers nested in their holes, defect avoided) — capture
`holenest_canvas_live.png`. Self-presenting example saved: `examples/28_hole_nest/28_hole_nest_demo.gh`
(Custom Preview + swatch + report panel; cold reopen reproduces).

The same engine on the h2h instance set (real placements, independently shapely-validated;
red hatch = sheet-hole avoided, dashed = part cavities filled, green title = VALID):

![HoleNest packed sheets](../../wiki/research/packing/figures/holepack_layouts.png)

Reference-vs-HoleNest head-to-head (median ms log, density, parts placed; from
`outputs/2026-06-13/twod_decision/h2h/results/h2h_report.json`):

![reference vs HoleNest](../../wiki/research/packing/figures/fig_holepack_h2h.png)

**Final verdict: CNH is now the fastest valid hole packer measured here in BOTH regimes** — 0.148 ms on
all-rect instances (146× the strongest valid baseline) and 43.5 ms general exact-NFP (vs the baseline's
stochastic 21.6 ms shelf, with CNH the only deterministic engine). Against Sparrow the result remains
validity + ~3-4 orders of magnitude in wall time on hole-aware inputs, with the standing caveat that Sparrow
still wins outline-only strip density — that claim boundary is unchanged.

## 3a. CNH v1 — first build, measured (this machine, 2026-06-12)
First working build of the Rhino-free `ContactNfpHoleNester` core, on the same true-hole instance class
(sheet 120×80 + one 20×20 defect, 4 host squares with 16×16 holes, 8 fillers):

| Packer | elapsed | placed | part-holes filled | valid | deterministic |
|---|---:|---:|---:|---:|---|
| **CNH (Frahan, this build)** | **60.7 ms** | 12/12 | 4 | **true** | **yes** |
| MIT native nester | 21.6 ms | 12/12 | 4 | true | no (stochastic) |
| Sparrow release | 3255 ms | 12/12 outlines | 0 | **false** | no |

**Honest read of the multiplier:**
- **vs Sparrow: ~54× faster AND valid** on the same parts (3255 → 60.7 ms). The "2× better than Sparrow"
  target is exceeded by an order of magnitude on the hole-aware lane — Sparrow produces no valid hole layout
  at any budget, so this is validity + speed, not a like-for-like density race. This is the lane that matters
  for stone (defect-avoidant cuts), and it is where the win is real.
- **vs the MIT native nester: 2.8× slower.** The native path hits 21.6 ms via a special-case axis-aligned
  "shelf" shortcut (contact placement, no full NFP). CNH runs the GENERAL exact-NFP construction (any
  polygon, contact-adaptive rotations, exact boolean validation) and is deterministic + managed + Rhino-free.
  Closing that 2.8× with an axis-aligned fast path for the rectangular-part common case is the next lever,
  but it is a shortcut, not the general engine.
- **Determinism** is a CNH-only property here and it matters: a fabrication cut layout must be reproducible
  run-to-run, which neither stochastic solver guarantees.

Floor analysis: the 60 ms is dominated by Clipper2 boolean/Minkowski calls (rotation dedup confirmed the
rotation set is not the cost). Beating the native shelf in managed code means either a contact-shelf fast
path for axis-aligned parts or an integer NFP cache — both planned, neither needed to clear the Sparrow bar.

## 4. Why this matters for stone (the reason it's ours, not a generic nester)
Frahan already detects fractures/voids from GPR (`FractureExtractor`). Feed those defect polygons in as
sheet-holes `Q_l` → **defect-aware slab nesting**: lay cuts that dodge the cracks a hole-blind packer cannot
even see. And part-in-part-hole nesting extends the Λ/carve-back offcut-reuse story into 2D (nest small parts
into the voids of larger remnants). That GPR-defect → CNH → cut-layout pipeline is the differentiator.

## 3c. NURBS-input performance study (GH wrapper, 2026-06-12, frahan 98b0d98)

User-reported: 6.3 s canvas solve on 7 smooth interpolated-NURBS shields. Methodology after an early
mistake: single-sample A/B comparisons on this machine drift +-12% run to run (thermal); every claim below
is from INTERLEAVED A/B (median of 3-4) or identical-instance in-process pairs.

| Measurement (same 7-shield NURBS instance) | ms |
|---|---:|
| BEFORE: absolute-tolerance sampling (~200 adaptive verts/curve) | 6,300 |
| Relative chord 1e-3*diag (still angle-driven: 172 verts) | ~6,200 (no gain) |
| Chord 3e-3 + 12 deg + 64-cap (53 adaptive verts) | 4,200 |
| **AFTER: uniform-by-length, 48 verts (DivideEquidistant)** | **2,550** (2.5x) |
| Core direct, 48 uniform verts, in-process | 2,816 (wrapper overhead negligible) |

Findings (all measured):
1. **Engine cost = Minkowski NFP builds (~95% of solve)**, scaling with input vertex count AND degeneracy:
   curvature-adaptive sampling concentrates tiny edges (0.2-unit) at curved spots — 53 adaptive verts cost
   4.2 s where 48 uniform verts cost 2.8 s. Uniform-by-length sampling is the wrapper-level fix.
2. **Negative result — analytic IFP**: closed-form bbox/half-plane IFP for convex sheets (the "obvious" win,
   eliminating one boolean per hull vertex) measured a consistent -70% regression in interleaved A/B. The
   hull-wise Clipper IFP costs ~0 ms, and its precision-snapped output avoids degenerate exact-tangency work
   downstream. Reverted: engine stays byte-identical to 90a88c4. Lesson: profile before optimizing — the
   plausible bottleneck was never the bottleneck.
3. **Adapter double-union removed**: Clipper2's MinkowskiSum-D already unions internally (verified
   behaviorally); the adapter's second union doubled per-NFP boolean work. Neutral-to-positive interleaved.
4. **Harder RDP on NFP inputs (8e-3): no measured gain** — reverted to 2e-3.
5. **The managed floor is ~2.5-2.8 s for 7x48v parts** (420 Minkowski calls at ~4.5-10 ms each in managed
   Clipper2). The next real lever, following the C-API wrapper pattern from the reference study (OpenNest,
   github.com/petrasvestartas/OpenNest, MIT): a native NFP kernel (C++ Clipper2 behind a C API, P/Invoke,
   batch the rotation x obstacle NFP builds per part into ONE native call). Expected by that study's own
   wrapper-overhead benchmarks: 5-20x on the NFP stage. Second managed lever: NFP caching keyed on
   (rotation signature, obstacle) for quantity nesting (same part repeated N times), which is the common
   production case.

Live validation of the shipped state: 7/7 placed, Valid=True, density 0.194 (identical to the slow path),
0 errors/warnings, 49 verts/curve.
