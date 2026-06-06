---
algorithm: BlockCutOpt multi-objective Pareto + Fisher-robust DFN sampling
slug: blockcutopt-pareto-fisher-robust
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: Pareto-front multi-objective (recovery/revenue/kerf/BCSdbBV) + Fisher-distributed DFN robustness (p10/p50/p90)
source_file: Core/Masonry/Quarry/BlockCutOpt/BlockCutOptParetoSolver.cs + ParetoFront.cs + FisherRobustSampler.cs + BlockValueModel.cs                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w01-quarry-decomposition]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: P (robustness ensemble parallel), T (value-model units)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: present (Pareto Front Inspector + Fisher-Robust BCO); Jalalian BCSdbBV = 4th axis
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: evolve (value-vs-recovery frontier is the decision surface for monument vs slab)
---

## Study question
Does the Pareto front + Fisher robustness give a stable value/recovery decision across DFN realisations?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w01-quarry-decomposition depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/Masonry/Quarry/BlockCutOpt/BlockCutOptParetoSolver.cs + ParetoFront.cs + FisherRobustSampler.cs + BlockValueModel.cs in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (Pareto-front multi-objective (recovery/revenue/kerf/BCSdbBV) + Fisher-distributed DFN robustness (p10/p50/p90)). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score P (robustness ensemble parallel), T (value-model units) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
