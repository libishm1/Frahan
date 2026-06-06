# Frahan StonePack — Full Workflows + Architecture Study

Date: 2026-06-04. Author: claude_cloud. Branch `docs/frahan-autonomous-nightshift`.
Status: workspace draft (outputs/). HITL before any wiki promotion.

EXTENDS, does not replace: `Template-General/outputs/2026-05-29/architecture/FRAHAN_ARCHITECTURE_AND_TRANSITION.md`
(component map, 8 emergent workflows, factory/sculptor gaps, Rhino transition plan) and
`wiki/specs/architectural_decisions_2026-05-31.md` (5-stage pipeline, 31 primitives + 19
monoliths, 4 interdisciplinary compositions). This study adds the layer those two lack: a
formal **workflow registry** that binds every workflow to its **algorithm spine** (the 10
V3 SLM cards), its fabrication direction, scale, discipline, and maturity. That binding is
the scaffold the SLM spines (T2) and the plugin-wide ROSES review (T3) hang off.

Honest current numbers (re-run `inventory.py` 2026-06-04): **206 component constructors
across 21 subcategories** (was 177/18 on 2026-05-29; Fabricate + Fabrication + Sculpt
subcategories added since). GUID stability is sacred (AGENTS.md §8).

---

## 1. The two organizing axes

The plugin is read along two orthogonal axes. Every workflow has a coordinate in both.

**Axis A — the 5-stage matching pipeline** (architectural_decisions §7; Tomczak 2023 Fig 2).
Every workflow, regardless of discipline, decomposes to:

```
1 Ingest  ->  2 Incidence  ->  3 Weight  ->  4 Match  ->  5 Refine + Export
(geometry    (which parts    (cost of     (assign      (settle / register /
 in)          can pair)       a pairing)   parts)        cut / emit artifact)
```

**Axis B — fabrication direction** (feedback_top_down_bottom_up_design; Quarra sensibility):

- **top-down** = form-first; find/cut stone to a designed target (voussoirs, sculpture
  enlargement, surface mosaic onto a designed surface, blockcut to a value target).
- **bottom-up** = material-first; form emerges from the stock (rubble, Trencadis, fragment
  reassembly, irregular nesting).
- **bridges** = workflows that translate one direction into the other (the Fabricate
  staggered-decomposition flagship; scan ingest as the shared enabling spine).

Scale spans ~7 orders of magnitude (ceramic shard 30 mm -> cyclopean wall 6.6 m ->
quarry/UTM coords 1e5-1e6 mm). Absolute mm tolerances are NOT comparable across that span
(architectural_decisions §9.5); the numeric-hygiene roadmap (recenter + scale-relative eps)
is what lets a single pipeline serve all of it.

---

## 2. Workflow registry (W1..W15)

Each row: stage coverage on Axis A, direction on Axis B, the algorithm spine (which of the
10 SLM cards it rests on), discipline reach, and maturity (from prior HITL notes; all are
"validate next phase" per the 2026-05-29 roadmap). Algorithm slugs are the V3 SLM cards at
`outputs/2026-06-04/algo_review_v3/slm_cards/`.

| # | Workflow | Stages | Direction | Algorithm spine (SLM cards) | Scale | Maturity |
|---|---|---|---|---|---|---|
| W1 | **Quarry decomposition -> block extraction** (commercial spine) | 1,2,3,4,5 | top_down | blockcutopt-quarry; (feeds) nfp-construction | m -> quarry/UTM | most developed |
| W2 | **Scan -> mesh -> bench** (shared enabling spine) | 1,5 | bridges | scan-reconstruction; constrained-icp-3d | mm -> m | developed |
| W3 | **Fracture -> slab** | 1,5 | top_down | coacd-mesh-boolean (slab cut) | m | developed |
| W4 | **Masonry wall assembly** | 1,2,3,4,5 | bottom_up | masonry-equilibrium-cra; hungarian-assignment | m (wall) | uneven (only Ashlar + Random Rubble reliable on fracture slabs) |
| W5 | **2D irregular nesting** | 1,4,5 | bottom_up | nfp-construction | mm -> m (sheet) | EVOLVED (NFP-BLF, >=2x, Rhino-validated 46ae0d2) |
| W6 | **Surface mosaic / Trencadis** | 1,3,4,5 | bottom_up | nfp-construction; bff-surface-flatten | cm (shards) | coherent niche |
| W7 | **Kintsugi fragment reassembly** | 1,4,5 | bottom_up | soft-icp-cpd; constrained-icp-3d | mm -> cm | research branch (pose-composition fixed) |
| W8 | **Edge matching (Trencadis / live-edge)** | 1,2,4,5 | bottom_up | soft-icp-cpd; constrained-icp-3d | cm -> m | coherent niche |
| W9 | **3D irregular container packing** | 1,4,5 | bottom_up | coacd-mesh-boolean (collision proxy) | m (container) | EVOLVED (physics settle, 35-38% density, Bullet backend) |
| W10 | **Surface packing (onto designed surface)** | 1,3,4,5 | top_down | bff-surface-flatten | m | coherent niche |
| W11 | **Voussoir / stereotomy** | 1,2,3,4,5 | top_down | hungarian-assignment; masonry-equilibrium-cra | m (vault) | shipped end-to-end (3 comps) |
| W12 | **Fabricate: staggered masonry decomposition** (flagship niche) | 1..5 | bridges | vsa-segmentation; coacd-mesh-boolean; masonry-equilibrium-cra; blockcutopt-quarry | m (sculpted form) | compose-only (no fork yet) |
| W13 | **Fabrication / robot bridge (G-code -> robot targets)** (terminal factory branch) | 5 | top_down | (none; transport + toolpath) | m | shipped (Stage F, 5 comps) |
| W14 | **Sculptor: enlarge / fit-in-block / carvable** (new terminal branch) | 1,4,5 | top_down | constrained-icp-3d (fit); hungarian-assignment (block-to-sculpture) | cm -> m | stubs (Enlarge, Fit-In-Block per 2026-05-29 §4d) |
| W15 | **GPR / fracture ingestion** (enabling, feeds W1/W3) | 1 | bridges | (none; signal + picks) | m -> survey | developed |
| W16 | **Overburden strip to rock face** (soil -> rock; feeds W1) | 1,5 | top_down | cut-fill SLM A2 TinPeel / A3 TinMerge / A5 OverburdenVolume (staged) / A9 GPR-bedrock | m -> quarry/UTM | NEW (OverburdenVolume Core staged + tested) |

The **commercial through-line** remains W1 -> W2/W3 -> W4 (the 2026-05-29 spine). W5..W10 are
adjacent capabilities. W12/W13/W14 are the terminal factory/sculptor branches that attach at
the end of the spine and are the product differentiators. W15 is an upstream feeder.

---

## 3. The pipeline view: every workflow on the 5 stages

This is the matrix that proves the architectural-decisions claim "one substrate, many
disciplines." Reading down a stage column shows which components are reused across workflows;
reading across a workflow row shows its end-to-end composition.

| Stage | Shared primitives (reused across workflows) | Workflow-specific |
|---|---|---|
| **1 Ingest** | Load Cloud / E57 / LAS, Scan Reconstruct, Sanitize/Repair, Bench From Mesh, GPR loader/picks, Voussoir Ingest, Scan->Block Inventory, EdgeMatch Segments | per-discipline source readers |
| **2 Incidence** | Constraint Dictionary, Incidence Matrix, OBB / Fracture / Grain filters | discipline constraint sets (§4) |
| **3 Weight** | Cost Matrix + cost-term primitives (yield, grain, carving, Hausdorff, GWP) | discipline cost terms |
| **4 Match** | MatcherRegistry: Greedy / Hungarian / Bipartite / MILP / NSGA-II; plus monolith solvers (EdgeMatch Solve, Pack2D*, AshlarPack, BestFit, Kintsugi, Pack3D*, RubbleWallSettle, BlockCutOpt Solve) | solver choice per workflow |
| **5 Refine + Export** | Soft ICP 3D, Constrained ICP 3D, Apply Assignment, Build Order Sequencer, RBE Stability, Settle (Rubble/Contact/Physics), Stone-Aware Cut Export, Fab Prep Report, G-code -> Robot Targets | export target per workflow |

The reuse seam is real: W1, W4, W11, W12 all consume Stage 4 `MatcherRegistry`; W2, W7, W8,
W14 all consume Stage 5 ICP; W3, W9, W12 all consume the CoACD/CGAL boolean backend. This is
why a numeric-hygiene fix in one shared Core utility (the recenter pass, ROSES roadmap #1)
propagates correctness across 7-9 workflows at once.

---

## 4. Discipline reach (Axis B x the 4 canonical compositions)

architectural_decisions §9 locks four interdisciplinary compositions on the SAME substrate.
Mapped onto the workflow registry:

| Discipline | Primary workflows | Constraints (numeric / categorical) | Cost terms | Object scale |
|---|---|---|---|---|
| **Stone fabrication** (primary) | W1, W2, W3, W11, W12, W13, W14 | Volume>=, MaxDim>= / Lithology==, Color== | yield + grain + carving | m -> quarry |
| **Timber reuse** (structuralCircle) | W4-shaped match, W5 | Length>=, Area>=, Inertia>= / Species==, Moisture== | GWP (k_new 28.9, k_reuse 2.25 kgCO2/m3) | m |
| **Cyclopean concrete-rubble** | W4, W12 | Mass<= / shape-class | recipe-rule satisfaction | 0.1 -> 6.6 m wall |
| **Ceramic mosaic / Trencadis** | W5, W6, W8 | Area>= / ColorPalette== | Hausdorff joint residual | 30 mm shard |

All four differ only in WHICH solver, WHICH cost terms, WHICH constraints — the matching
engine is unchanged. This is the interdisciplinary claim the ROSES review (T3) appraises.

---

## 5. Architecture health (carried + current)

From 2026-05-29 §3, still open (validate-and-fix targets, paired with each sprint):

- Subcategory sprawl now **21** (was 18): `Fabricate`/`Fabrication` split, `Sculpt` added,
  plus the old `2D` vs `2D Packing`, `Surface` vs `Surface Packing` inconsistencies. Fold to
  ~12 coherent ribbons; GUID-safe via `GH_UpgradeUtil.SwapComponents`.
- Duplicate display name "Frahan Packing Report" (two GUIDs) — rename one.
- 9 2D packers / triple mesh-repair backends / 5 Quarry Decompose variants: AUDIT + validate,
  do NOT blind-merge (reuse_dont_duplicate_components memory — Trencadis physics + edge-match
  variants are real differences).
- Add a build-time GUID-uniqueness reflection test (a collision was fixed 2026-05-29).
- Two wiki roots (`D:/code_ws/wiki` vs `Template-General/wiki`); reconcile.

Cross-cutting correctness (V3 ROSES roadmap, top-10 review): the suite needs ONE shared
numeric-hygiene layer (recenter-to-centroid + scale-relative epsilon + one tolerance budget)
across 9/10 algorithms, plus the masonry K=4->K=8/16 friction correctness fix. These are
workflow-spanning, not per-component (see §3 reuse seam).

---

## 6. Maturity + validation state per workflow (honest)

- EVOLVED + Rhino-validated: W5 (NFP-BLF, 46ae0d2). 
- EVOLVED + deployed, not committed: W9 (physics settle).
- Shipped end-to-end, validation pending: W11, W13.
- Developed, validate next: W1, W2, W3, W15.
- Uneven / known-bug: W4 (only Ashlar + Random Rubble reliable on fracture slabs).
- Research / niche: W6, W7, W8, W10.
- Compose-only / stub: W12, W14.

No workflow has a logged full-pipeline HITL pass in a `validation_log.md` yet; the MCP HITL
loop (`validation_pack/POST_RESTART_HANDOFF.md`) is the mechanism to close that.

---

## 7. How this study feeds T2 and T3

- **T2 (SLM spines):** one spine per workflow W1..W15. Each spine front-matter carries the
  workflow's Axis-A stage coverage, Axis-B direction, algorithm spine (the slugs in §2),
  discipline reach (§4), and a gotchas pre-fill inherited from the algorithm cards it rests
  on. The derivation body is stubbed with honest TODOs (minimal viable, not fabricated).
- **T3 (ROSES review):** clusters W1..W15 by Axis-B direction x scale x discipline (§2 + §4),
  appraises each cluster against the §4 gotchas instrument carried from the algorithm cards,
  and produces the plugin-wide compatibility matrix + verdicts.

## 8. Last updated
2026-06-04 — T1 authored; extends 2026-05-29 architecture + 2026-05-31 decisions.
