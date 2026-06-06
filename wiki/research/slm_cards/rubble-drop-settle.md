---
algorithm: Random-rubble wall drop-settle (2.5D physics + COM/load-path stability)
slug: rubble-drop-settle
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: drop-settle relaxation + center-of-mass / load-path stability gate
source_file: Core/Masonry/RubbleWallSettle.cs (+ GH RubbleWallSettleComponent)                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w04-masonry-wall-assembly]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: P (settle determinism), T (stability tolerance), G2 (contact degeneracy)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: SIGNED OFF (2026-05-25): flat-bed + width 7x + COM/load-path stability
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: reuse (signed off; boulder dynamic settle + full equilibrium next)
---

## Study question
Does rubble settle place stones in stable contact with a valid load path on a flat bed?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w04-masonry-wall-assembly depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/Masonry/RubbleWallSettle.cs (+ GH RubbleWallSettleComponent) in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (drop-settle relaxation + center-of-mass / load-path stability gate). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score P (settle determinism), T (stability tolerance), G2 (contact degeneracy) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
