---
algorithm: Edge-match beam-search assembly (Trencadis / live-edge 5-stage)
slug: edge-match-beam-assembly
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: geometric-hash coarse phase + beam-search assembly over edge correspondences
source_file: EdgeMatching.Core/AssemblySolver.cs (segment -> hash -> coarse -> ICP -> beam)                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w08-edge-matching, w06-surface-mosaic-trencadis]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: T2/T5 (scale-invariant edge tolerance), M5 (hash bucket cost)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: shipped opt-in; BeamWidth default 8 (architectural_decisions section 9.4)
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (scale-invariant beam across mm fractures -> m blocks)
---

## Study question
Does beam assembly match adjacent edges scale-invariantly with bounded joint Hausdorff?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w08-edge-matching, w06-surface-mosaic-trencadis depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read EdgeMatching.Core/AssemblySolver.cs (segment -> hash -> coarse -> ICP -> beam) in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (geometric-hash coarse phase + beam-search assembly over edge correspondences). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score T2/T5 (scale-invariant edge tolerance), M5 (hash bucket cost) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
