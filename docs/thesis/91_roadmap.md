# What Is Left: Roadmap

Sole author: Independent Research. Open data, open source.

This is the consolidated, deduplicated, and prioritised list of open work,
merged from the per-chapter audits and the licensing register. Items are
graded blocker / high / medium / low and tagged by subsystem. Each carries a
single-line action. The grade reflects what blocks a public or commercial
release, not difficulty.

Honesty note. Several "what's left" items are honesty constraints, not
defects: a stated boundary on a claim (for example, the outline-only density
boundary against the reference physics nester). These are listed so the claim
boundary is preserved, not because the code is wrong.

---

## Blocker — must resolve before a public or commercial release

| Subsystem | Item | Action |
|---|---|---|
| Licensing (E1) | Kintsugi / PuzzleFusion++ is NON-COMMERCIAL, not plain GPL; covers ported C# and the ~255 MB `kintsugi.bin` weights. | Keep Kintsugi as a separately distributed, optional, research-only package; verify root LICENSE, port README, and any repo-root statement say research-only non-commercial, not plain GPL. |
| Licensing | Root LICENSE is a placeholder header, not full GPL text, while the dist links Kintsugi.Port. | Replace root LICENSE with canonical `gpl-3.0.txt`; isolate Kintsugi.Port behind a separate build so the rest can be relicensed if desired. |
| Licensing (spec 16) | No `THIRD_PARTY_NOTICES.md` and no `frahan_reference_register.md` at audit time; BFF, SuiteSparse, OpenBLAS, GFortran, and any copied source lack attribution rows. | Create `THIRD_PARTY_NOTICES.md` (one row per dependency) and `docs/index/frahan_reference_register.md`, with per-file SPDX headers on copied source, before external review. |

---

## High — correctness or canvas-reachability defects

| Subsystem | Item | Action |
|---|---|---|
| Quarry | `RecoveryCascade` has no GH consumer; `FractureBlockPack` ships a duplicate self-contained recovery engine, so the validated Core cascade is unreachable on canvas (silent-disagreement risk). | Refactor `FractureBlockPack` to call the validated Core `RecoveryCascade` (facade-not-fork); retire the duplicate engine. |
| Masonry / CRA | `AdmmQpSolver` cold-start degrades steeply past ~50 contact interfaces (54-iface 5.4 s, 147-iface 86 s), so wall-scale equilibrium does not converge in interactive time. | Keep the LS-first KKT certificate in `MasonryStabilityChecker` and add warm-start / per-element verification for large mixed assemblies; document per-element as the wall-scale pattern. |
| Edge-Matching | Independently tessellated shard rims yield zero cross-panel hash hits (self 172, cross 0, `ProjectionPairFinder.cs:16-22`); the geometric 3D path assembles only via the projection bootstrap, which leaves some MST interfaces loose (2 of 5 fully in contact). | Treat the learned Kintsugi Port as the production 3D path; for the geometric engine, replace independent tessellation with a shared-rim resampling so cross-panel hashes hit. |

---

## Medium — partial implementations, attribution, and scale-invariance

| Subsystem | Item | Action |
|---|---|---|
| 2D Nesting | Standalone greedy Trencadis box (`F2D00002`) is a skeleton returning empty; a ghost on the primary ribbon violates `AGENTS.md` §6. | Either implement the greedy pack or move the box off the primary ribbon; route users to Catalog (`F2D00007`) / Pipeline (`F2D00009`). |
| 2D Nesting | Deployed `.gha` can lag current source on live 2D solves (KB-7); an old build may overlap parts where current source does not. | Rebuild and redeploy the `.gha` before trusting any live 2D result; gate release on a live zero-overlap check. |
| Quarry | `BlockCutOptOmniSolver` coarse-to-fine is a stub: both `UseCoarseToFine` branches run the fine-step uniform sweep (worst-case wall clock). | Implement the true coarse-to-fine Pareto sweep; until then, document the flag as a no-op. |
| Quarry | I13 (Tian 2025 multi-model joint generator) is proposed only; I14 (Zhang et al. 2024 composite multi-convex block) is partial. 12 of 14 improvements shipped. | Ship I13 and complete I14, or mark both explicitly as future work in the README. |
| Quarry | Bed-bounded hexahedra and the flat/oblique cost/volume/balanced frontier live in example generators, REPORTED not gated. | Promote the bed-bounded hexahedra fix and the frontier metric into a Core class with a unit-test gate. |
| Surface Packing | `PackOnSurfaceComponent.cs:41` mis-credits Floater 2003 MVC; the lift is plain barycentric (Cramer's rule), confirmed by the SLM card. | Soften the attribution to related-work or replace with a classical barycentric citation; the shipped math is correct, only the attribution is wrong. |
| Surface Packing | A single global chart scale `s` mis-sizes parts on conformal charts where the local conformal factor e^u departs from the perimeter-weighted average. | Add per-face / per-cone-patch scale plus adaptive re-cut driven by `ChartFlatnessReport`. |
| Surface Packing | `BarycentricMapper2DTo3D` inverse map is an O(P·F) linear scan, author-stated ceiling ~2000 faces. | Add an RTree over flat-face bounding boxes to make the lift O(P log F). |
| Surface Packing | `ChartDistortionAnalyzer` edge-stretch metric cannot detect BFF foldovers (scalar lengths ignore orientation sign). | Add a per-face signed-area sign test (flag M3). |
| Surface Packing | Tolerance system unreconciled and units undeclared: four absolute epsilons plus a 0.01 sampling tolerance, none scaled to chart size; at m-scale the 1e-6 containment eps drops valid boundary points and returns a null curve. | Route all surface-packing tolerances through the scale-relative budget; record the model unit in `FrahanSurfaceChart`. |
| Licensing (E2) | xBIM is CDDL-1.0, distribution-incompatible with GPL-3.0. | Move to the licence-clean GeometryGymIFC path (HITL ruling), or use an out-of-process IFC writer. |
| Licensing (E5) | `BestFitInventoryPacker.cs:26-29` and the Masonry facade `BestFitPackComponent.cs:30` cite a non-existent "Gramazio/Kohler/Eichenhofer 2017 CAD paper"; the Core lineage is correctly Furrer 2017 / Johns 2020. | Correct both `[Algorithm]` attributes to Furrer/Johns before external review. |
| Masonry / CRA | The alternating-convex CRA certificate is sound only in the certifying direction; "not certified" can be a false negative on the non-convex CRA NLP (`CraStabilityChecker.cs:45-49`). | Document the verdict as conservative: stable claims are sound, unstable claims are conservative; do not assert sharpness. |
| Masonry / CRA | The J interlock metric, the Coursing-morph continuum, and the Lambda imposition formalisation are A-candidate originality claims; `AGENTS.md` §9 forbids "novel" without a completed sweep (Legakis et al. 2001 is the closest known prior). | Run the targeted prior-art sweep before any external novelty claim. |
| Edge-Matching | `BlockPairMatch3DComponent.cs:43-45` declares the VSA face partitioner a Frahan stub; real stone-to-cell work routes to the Hungarian Stone-Cell Match via `[RelatedComponent]` (:41). | Either implement the VSA partitioner (Cohen-Steiner 2004) or keep the honest stub-plus-redirect and mark it future work in the README. |
| Edge-Matching | The per-facet projection bootstrap, antiparallel SE(3) lift composition, and 3D-disposes verification gate (`ProjectionPairFinder.cs`) are original-research A-candidate; the prior-art sweep has not been run. | Run the targeted prior-art sweep per `AGENTS.md` §9 before asserting novelty. |

---

## Low — perf limitations, bounded residuals, and honesty boundaries

| Subsystem | Item | Action |
|---|---|---|
| 2D Nesting | Example 28 (hole nest) ships no rendered figure and no README; the CNH renders borrow examples 10 and 12. | Add a HoleNest-specific capture and README so the hole-aware lane is shown directly. |
| 2D Nesting | Rect shelf fast-path only activates at `spacing == 0`; `spacing > 0` defers to the general engine. | Add exact rect-dilation bookkeeping for `spacing > 0`; perf limitation, correct fallback already exists. |
| 2D Nesting | Residual penetration band (~2e-5 caller units) can be accepted after the compound gate; deeper ones cannot on any path. | Document the bounded residual in the claim; it is far inside the fabrication budget but is not a zero guarantee. |
| 2D Nesting | Outline-only strip density still trails the reference physics nester by 6-10%; CNH's win is the hole-aware lane only. | Preserve this boundary in any external claim (honesty constraint, not a defect). |
| Quarry | Kerf volume is a film approximation `A_xy·k/2`, not exact inter-cell kerf; recovery denominator is approximate. | Refine to exact inter-cell kerf alongside the sub-division work (documented Phase-1). |
| Quarry | `RecoveryCascade` header originality wording (E9): formerly self-labelled "novel", now softened to the Murugean 2026 BoEGE cite. | Complete the prior-art sweep per `AGENTS.md` §9 to confirm the A-candidate status. |
| Quarry | Example 08 marble GPR data is CC-BY-NC-ND (research/testing only). | Do not use the flagship marble study in commercial product demos; swap to a CC-BY dataset for any commercial demo. |
| Quarry | Extraction Order Optimizer is self-declared Frahan-original without a prior-art sweep. | Run the prior-art sweep (A-candidate) per the originality framework. |
| Surface Packing | Far-from-origin (UTM-scale) charts lose ~4 mantissa decimals because OBJ is written at raw world coords G10 with no recenter (flag T1). | Recenter to the bbox centroid before OBJ write and undo after the inverse map. |
| Masonry / CRA | Stale audit note E4: a prior digest claimed the shipped RBE verdict still wires the sign-buggy `RbeQpFormulation.Build`; current source uses `BuildPhysicsCorrected` (`MasonryStabilityRbeComponent.cs:305`). Legacy `Build` survives only for sign-pinning unit tests. | Mark the legacy `Build` `[Obsolete]` to prevent future mis-wiring; the note is stale, not an active bug. |
| Masonry / CRA | `PolygonalMasonrySequence3DComponent` (`C5F18B4D`) overlaps `BlockBuildOrderer` (3D contact-support DAG); two sequencers for one job. | Merge to a single 3D sequencer (documented architecture candidate). |
| Masonry / CRA | `examples/02_masonry_assembly` ships `.gh` + `.3dm` only, no PNG; the assembly colour/order sequencing figure cannot be embedded. | Add a rendered PNG capture so the assembly-sequencing figure is embeddable. |
| Edge-Matching | Three duplicate absolute-orientation kernels (Horn quaternion in `RigidTransformRecovery`, the Georeference private Horn, and SVD-Kabsch in `ConstrainedIcp3D`/`SoftIcpRefiner`) solve the same rigid fit by two routes. | Unify on one MathNet-SVD kernel; both routes are correct, so this is a refactor not a bug. |
| Edge-Matching | The `[Algorithm]` at `EdgeMatchSolveComponent.cs:25` names "Phase correlator FFT" while `PhaseCorrelator.cs:29-34` is direct O(n²) circular L1 correlation, not an FFT. | Reword the attribute to avoid implying a frequency-domain implementation; the code is correct and deterministic. |

---

## Priority order (top to bottom)

1. **Resolve the Kintsugi non-commercial split and the root LICENSE (E1, blocker).** This is the single item that gates any public or commercial release; everything ships under the wrong terms until it is correct.
2. **Add `THIRD_PARTY_NOTICES.md` and the reference register (spec 16, blocker).** Required before external review; cheap to author, mandatory to ship.
3. **Make `RecoveryCascade` the on-canvas engine; retire the `FractureBlockPack` duplicate (high).** Removes a silent-disagreement risk and makes the validated cascade reachable.
4. **Hold the line on the two 3D-path boundaries (high).** CRA equilibrium does not converge interactively past ~50 contact interfaces (warm-start / per-element verification needed); the geometric 3D reassembler only assembles via the projection bootstrap because independent tessellation kills cross-panel hashes (the learned Kintsugi Port is the production 3D path). Both must be stated, not papered over.
5. **Remove the ghost greedy Trencadis box and rebuild/redeploy the `.gha` (medium).** Two §6 canvas-honesty fixes: no empty-output node on the primary ribbon, no stale build overlapping live parts.
6. **Correct the fabricated and mislabelled citations (E5 / Floater / FFT, medium).** Fix the BestFit "Gramazio 2017" attribute to Furrer/Johns, soften the Floater-2003 MVC credit on the barycentric lift, and reword the "Phase correlator FFT" attribute to direct correlation; required before any external review under §9.
