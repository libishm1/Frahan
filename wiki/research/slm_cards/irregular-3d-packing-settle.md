---
algorithm: Irregular 3D container packing + drop-settle (DLBF + Bullet rigid body)
slug: irregular-3d-packing-settle
card_type: algorithm_spine          # gap-fill SLM spine (completes workflow algorithm coverage)
core_method_class: Deepest-Bottom-Left-Fill heuristic + rigid-body drop-settle (Bullet, Coulomb friction)
source_file: Core/GreedyHeightmapPacker.cs + IrregularMeshContainer.cs + MeshPileHeightmap.cs; Bullet via BulletSettleService                  # REAL Core/EdgeMatching source (audit anchor; verify before extracting)
workflows: [w09-3d-irregular-packing, w12-fabricate-staggered-decompose]                   # workflow spines that rest on this algorithm
big_o_time: TBD (derive from source)
big_o_space: TBD (derive from source)
parallel_model: TBD
gotchas_to_assess: M (mesh manifold for collision), P (settle determinism P1), T1 (recenter container)             # run the section-4 G/M/N/P/T instrument against the source (R12)
evolution_status: EVOLVED + deployed (physics settle 35-38% density, 0 interpenetration); NOT committed
provenance:
  source_tree: Frahan.StonePack.Core / Frahan.EdgeMatching.Core
  carried_from: EVOLUTION_PROGRESS.md + SLM_RECOVERY_REVIEW.md + project memory
  reviewed: 2026-06-04
  status: spine                     # minimal viable; full file:line extraction is the fill step
verdict: reuse (evolved; commit pending HITL)
---

## Study question
Does the heightmap/DLBF seed + Bullet settle reach dense, stable, non-interpenetrating packs on real ETH1100 geometry, deterministically?

## Why this card exists
The top-10 V3 review did not cover this algorithm, but workflow spine(s) w09-3d-irregular-packing, w12-fabricate-staggered-decompose depend on it,
so the algorithm-level coverage was incomplete. This spine closes that gap.

## Derived math + reuse seam (TODO)
TODO: read Core/GreedyHeightmapPacker.cs + IrregularMeshContainer.cs + MeshPileHeightmap.cs; Bullet via BulletSettleService in full; derive the core equation(s) from definitions + the code (R7, never
literal-extracted); cite only the method-class origin (Deepest-Bottom-Left-Fill heuristic + rigid-body drop-settle (Bullet, Coulomb friction)). List the reuse seam.

## Section-4 gotchas (TODO, R12)
TODO: score M (mesh manifold for collision), P (settle determinism P1), T1 (recenter container) (and the rest of G/M/N/P/T) against the source; record flags; verify any
primitive empirically (R7).

## Baseline + bottleneck (the gate, R1+R2)
TODO: run the shipping component on the REAL dataset (ETH1100 / quarry-scan); honest metric (R6).

## Cross-references
- Workflow spines: ../slm_spines/_INDEX.md
- Top-10 cards: ../../algo_review_v3/slm_cards/_INDEX.md
- Evolution status: ../../algo_review_v3/EVOLUTION_PROGRESS.md
