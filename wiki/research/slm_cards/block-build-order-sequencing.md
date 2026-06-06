---
algorithm: Block build-order sequencing (graph coloring + topological course order)
slug: block-build-order-sequencing
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: adjacency graph coloring (Welsh-Powell class) + topological/gravity ordering
source_file: Core/Masonry/Interfaces/BlockGraphColorer.cs + Core/Masonry/Geometry/BlockBuildOrderer.cs                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w04-masonry-wall-assembly, w11-voussoir-stereotomy, w12-fabricate-staggered-decompose]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: M5 (graph data-structure cost), P (independent-set parallelism)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: shipped; feeds RBE stability stream + pick/place frames
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (validate the order is build-stable at every partial step)
---

## Study question
Is every partial assembly in the emitted build order itself stable (no mid-build collapse)?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w04-masonry-wall-assembly, w11-voussoir-stereotomy, w12-fabricate-staggered-decompose depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/Masonry/Interfaces/BlockGraphColorer.cs + Core/Masonry/Geometry/BlockBuildOrderer.cs in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (adjacency graph coloring (Welsh-Powell class) + topological/gravity ordering). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score M5 (graph data-structure cost), P (independent-set parallelism) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
