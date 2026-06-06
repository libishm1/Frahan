---
algorithm: Ashlar coursing layout engine (course-based masonry packing)
slug: ashlar-coursing-layout
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: course-based greedy shelf/level packing (ashlar bond)
source_file: Core/Masonry/Packing/AshlarLayoutEngine.cs (+ AshlarPackOptions, CourseMode)                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w04-masonry-wall-assembly]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: T2/T5 (course tolerance), M (planar polygon extraction)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: reliable on fracture slabs (one of the two trustworthy masonry packers per memory)
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (validate vs the other packers; the masonry-workflow-status known-good)
---

## Study question
Does ashlar coursing produce a stable, gap-controlled wall on real fracture-pattern slabs?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w04-masonry-wall-assembly depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/Masonry/Packing/AshlarLayoutEngine.cs (+ AshlarPackOptions, CourseMode) in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (course-based greedy shelf/level packing (ashlar bond)). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score T2/T5 (course tolerance), M (planar polygon extraction) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
