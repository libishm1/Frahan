# SLM algorithm cards index (unified: top-10 review + workflow gap-spines)

This directory holds the V3 Tier-1 SLM cards. Two tiers of completeness:
- **Top-10 cards** (full file:line-grounded studies, the 2026-06-04 review).
- **Gap-spines** (minimal-viable skeletons added 2026-06-04 night so EVERY algorithm the 15
  workflow spines depend on has a card; bodies are honest TODOs to fill).

With both, every algorithm in every workflow master spine maps to a card here: coverage PASS.

## Top-10 (full SLM cards) — all verdict EVOLVE

| slug | direction | gotcha flags |
|---|---|---|
| masonry-equilibrium-cra | bottom_up | T1,T3,T5,G2,G4,M2,P5 |
| constrained-icp-3d | bottom_up | G2,M3,M5,P5,T1,T2,T3,T4,T5,T7 |
| soft-icp-cpd | both | G2,G4,M2,M5,T1,T3,T5,T7 |
| vsa-segmentation | bottom_up | M1,M3,T2,T3,T7 |
| hungarian-assignment | top_down | T2,T3,M5 |
| blockcutopt-quarry | top_down | T1,T2,P5,G2,M2,P4 |
| nfp-construction | bottom_up | G1,G2,G4,T2,T6,T5,M5 |
| scan-reconstruction | bottom_up | T1,T2,T3,T5,G2,M1,M2,M3,P1,P2 |
| bff-surface-flatten | top_down | M1,M3,T1,T2,T5,T7,G2,M5 |
| coacd-mesh-boolean | top_down | G1,G2,G4,M1,M2,M3,T1,T2,T3,T6,T7 |

Per-card big-O, derived equations, numeric stress, and verdicts are in each `<slug>.md`.
Synthesis: `../roses_synthesis/ROSES_top10_fabrication_synthesis.md`. Evolution status:
`../../../outputs/2026-06-04/algo_review_v3/EVOLUTION_PROGRESS.md` (3 evolved + GeometryNumerics).

## Workflow gap-spines (minimal-viable; complete the coverage)

| gap slug | method class | workflows | verdict | status |
|---|---|---|---|---|
| irregular-3d-packing-settle | DLBF heuristic + Bullet drop-settle | w09, w12 | reuse | EVOLVED + deployed (35-38% density) |
| mixed-size-dlbf-revenue-pack | multi-size DLBF + revenue weighting | w01, w12 | evolve | CORE of evolved quarry workflow (~80% vol) |
| ashlar-coursing-layout | course-based greedy shelf/level pack | w04 | evolve | reliable on fracture slabs |
| rubble-drop-settle | drop-settle + COM/load-path stability | w04 | reuse | SIGNED OFF 2026-05-25 |
| block-build-order-sequencing | graph coloring + topological order | w04, w11, w12 | evolve | shipped |
| amrr-mining-plan | AMRR plane-cut sequencing (Shao 2022) | w01 | evolve | present |
| blockcutopt-pareto-fisher-robust | Pareto multi-objective + Fisher robust | w01 | evolve | present |
| edge-match-beam-assembly | geometric-hash coarse + beam assembly | w08, w06 | evolve | shipped opt-in |
| sculptor-enlarge-fit-carvability | non-uniform enlarge + fit + carvability | w14 | evolve | STUBS (TN wedge) |
| gcode-toolpath-transport | ISO 6983 parse + arc discretization | w13 | reuse | shipped (transport) |

Total: 10 top-10 cards + 10 gap-spines = 20 algorithms. Generators live in
`../../../outputs/2026-06-04/nightshift/{slm_spines,slm_gap_cards}/`. Workflow spines:
`../slm_spines/_INDEX.md`.

## Coverage check
Every workflow's `algorithm_spine` maps to a card above (top-10 or gap); W13/W15 are
transport/signal. all workflow algorithm dependencies mapped: PASS.
