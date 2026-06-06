---
algorithm: G-code parse + arc discretization + robot-target tagging (transport)
slug: gcode-toolpath-transport
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: ISO 6983 parse + arc chord-step discretization + frame tagging (no geometry optimisation)
source_file: GH Fabrication/{GCodeParser,GCodeToPlanes,WireSawToolpathAdapter,PlanesToKukaPrc,PlanesToRobotTargets}                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w13-fabrication-robot-bridge]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: T4 (units mm/m), frames (base/tool/TCP), arc chord-error bound             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: shipped (Stage F, 5 comps); transport not algorithm
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: reuse (transport; unit/frame/chord checks only)
---

## Study question
Does G-code -> Plane[] -> KUKAprc/Robots preserve units, frames, and arc chord-error bounds?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w13-fabrication-robot-bridge depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read GH Fabrication/{GCodeParser,GCodeToPlanes,WireSawToolpathAdapter,PlanesToKukaPrc,PlanesToRobotTargets} in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (ISO 6983 parse + arc chord-step discretization + frame tagging (no geometry optimisation)). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score T4 (units mm/m), frames (base/tool/TCP), arc chord-error bound (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
