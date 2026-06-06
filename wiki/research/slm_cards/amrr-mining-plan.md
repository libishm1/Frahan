---
algorithm: AMRR plane-cut extraction sequence (mining plan)
slug: amrr-mining-plan
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: Adjusted Mineable Reserve Ratio plane-cut sequencing (Shao 2022, verify)
source_file: Core/Masonry/Quarry/BlockCutOpt/* (AMRR plan path); cites Shao 2022 (verify Crossref)                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w01-quarry-decomposition]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: P5 (sequence serial tail), T1 (quarry-scale coords)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: present in the quarry workflow (SLM_RECOVERY_REVIEW section 4 step 6)
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (sequence the extraction of the packed blocks; tie to revenue order)
---

## Study question
Does the extraction sequence respect saw access + free-face availability per cut?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w01-quarry-decomposition depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/Masonry/Quarry/BlockCutOpt/* (AMRR plan path); cites Shao 2022 (verify Crossref) in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (Adjusted Mineable Reserve Ratio plane-cut sequencing (Shao 2022, verify)). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score P5 (sequence serial tail), T1 (quarry-scale coords) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
