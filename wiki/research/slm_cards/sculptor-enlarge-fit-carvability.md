---
algorithm: Sculptor ops: non-uniform enlarge + fit-in-block + carvability gate
slug: sculptor-enlarge-fit-carvability
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: non-uniform scaling (digital pointing machine) + ICP/Hungarian fit + thin-feature/grain gate
source_file: GH Sculpt subcategory (Enlarge Sculpture, Fit In Block per 2026-05-29 architecture); Core TBD                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w14-sculptor-enlarge-fit-carve]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: M (undercut/wall-thickness), T4 (units), N (if NURBS target)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: STUBS (2026-05-29); lowest tech risk, highest-demand TN wedge
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (the TN monument differentiator; needs baseline)
---

## Study question
Does enlarge + fit-in-raw-block + carvability gate yield carvable geometry with a clean pre-CAM handoff?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w14-sculptor-enlarge-fit-carve depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read GH Sculpt subcategory (Enlarge Sculpture, Fit In Block per 2026-05-29 architecture); Core TBD in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (non-uniform scaling (digital pointing machine) + ICP/Hungarian fit + thin-feature/grain gate). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score M (undercut/wall-thickness), T4 (units), N (if NURBS target) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
