---
algorithm: Heterogeneous mixed-size revenue-weighted block pack (DLBF)
slug: mixed-size-dlbf-revenue-pack
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: multi-size Deepest-Bottom-Left-Fill with value/revenue weighting
source_file: Core/Masonry/Quarry/BlockCutOpt/Dlbf3dMixedSizePacker.cs                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w01-quarry-decomposition, w12-fabricate-staggered-decompose]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: P4 (load balance on irregular sizes), T1/T2 (recenter+scale-rel at quarry scale)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: CORE of the evolved quarry workflow: fills ~80% bench volume (vs 5-33% prime-only)
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (the composed objective is the quarry-to-monument value lever)
---

## Study question
Does revenue-weighted mixed-size packing (monuments + dimension + slab + tile) lift usable recovery far above prime-block-only on a real fractured bench?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w01-quarry-decomposition, w12-fabricate-staggered-decompose depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/Masonry/Quarry/BlockCutOpt/Dlbf3dMixedSizePacker.cs in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (multi-size Deepest-Bottom-Left-Fill with value/revenue weighting). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score P4 (load balance on irregular sizes), T1/T2 (recenter+scale-rel at quarry scale) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
