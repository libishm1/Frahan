# Frahan StonePack — wiki index

Last updated: 2026-05-22

This is the navigation index for the curated `wiki/` (AGENTS.md
§Context loading). Read this first in any new session. Cap: 150
lines. Do not paste raw content here — link out.

> **Start here today:** Libish's immediate next steps for this week are
> in `outputs/2026-05-15/NEXT_STEPS.md`. Canvas validation of the 14
> new components is the next gate; cloud-debug workflow + lighthouse
> pilot pick + Q1 roadmap scoping follow.

## Top-level layout

```
wiki/
├── index.md                  this file
├── health.md                 contradictions + open questions (P0/P1/P2)
├── log.md                    session log; one dated entry per meaningful run
├── audit_trail.jsonl         machine-readable audit (append-only)
├── agent_prompts/            durable agent prompts
├── algorithms/               validated approaches per topic
│   ├── cgal_shim/
│   ├── hitl_cards/           HitL card pattern (pre-canvas fixtures)
│   ├── ingest/               vector + GPR multi-format readers (new 2026-05-22)
│   ├── packing_2D/
│   ├── polygonal_masonry/    Kim 2024 install-order DAG (HitL pending)
│   └── surface_mosaicing/
├── index/                    cross-cutting audits / inventories
├── papers/                   master paper + reproduction reports
├── research/                 research consolidations (long-form)
├── specs/                    numbered Frahan-area specs (00–22)
└── testing/                  test plans + validation matrices
```

## Specs (numbered, 00–22)

23 specs total. Read by index:

- 00 project overview
- 01 software principles
- 02 architecture
- 03 module map
- 04 Grasshopper component spec
- 05 2D Trencadís packing
- 06 surface Trencadís
- 07 3D Ashlar packing
- 08 GeoPack (input pipeline)            ← spec status: v0 manual landed 2026-05-14
- 09 GeoCut (per-block planning)         ← spec status: SlabYieldOpt + BilletCutter landed 2026-05-14
- 10 QuarryCutOpt (bench-scale)          ← spec status: full pipeline landed 2026-05-14
- 11 mesh + native backend
- 12 learning-guided packing
- 13 testing + validation
- 14 minimal pre-factory test plan
- 15 agent implementation plan
- 16 licensing + source porting policy
- 17 roadmap + timeline
- 18 open questions
- 19 source relocation plan
- 20 CGAL audit
- 21 CGAL build setup
- 22 COACD build setup

## Algorithms (validated approaches)

- `algorithms/cgal_shim/` — CGAL native-shim approach + validation log.
- `algorithms/hitl_cards/` — HitL card pattern for pre-canvas
  GH-component validation (.3dm + .md + .gh per card, two-script
  authoring split). 16 phases (~38 cards) as of 2026-05-22.
- `algorithms/ingest/` — vector (Shapefile / GeoJSON) + GPR (CSV /
  SEG-Y / MALA RD3 / pulseEKKO DT1) multi-format ingest readers.
- `algorithms/packing_2D/` — 2D irregular-sheet packing pipeline.
- `algorithms/polygonal_masonry/` — Kim 2024 installation-order DAG
  (DETC2024-142563); 2D + 3D pipelines implemented, headless-tested
  756/0/91, deployed 2026-05-20; canvas HitL pending.
- `algorithms/surface_mosaicing/` — surface-Trencadís pipeline,
  primitives, descriptors.

## Cross-cutting indexes (`wiki/index/`)

- `frahan_archive_inventory.md` — snapshot of frozen prior versions.
- `frahan_class_method_component_audit.md` — class / method / GH-
  component cross-reference.
- `frahan_code_snippet_audit.md` — audit of code snippets vs source.
- `frahan_document_inventory.md` — what document lives where.
- `frahan_naming_drift_report.md` — naming-consistency report.
- `frahan_reference_register.md` — external references / citations.

## Papers + research

- `papers/frahan_master_paper.md` — master paper draft.
- `papers/frahan_zip_bundle_audit.md` — audit of the research zip set.
- `papers/equations_and_diagrams/` — 26 per-paper synthesis pages
  (BlockCutOpt 2020, Zhang 2024, Tong 2024, Jalalian, Shao 2022, etc.).
- `research/master_knowledge_base_v0_2.md` — consolidated KB.
- `research/geopack_geocut_landscape.md` — Layer 8/9 landscape.
- `research/boundary_aware_packing_consolidated.md` — packing notes.
- `research/market_features_gap_analysis.md` — 2026-05-15 gap
  analysis vs `D:\deep-research-report (1).md`; 15 gaps in 3 tiers,
  3-quarter roadmap.
- `research/porting_tradeoffs_2026-05-22.md` — Rhino+GH vs standalone
  trade-off analysis; recommend staying on the canvas 6-12 months.
- `research/algorithms_papers_audit_2026-05-22.md` — ~50 algorithms
  across 12 domains mapped to peer-reviewed citations.
- `research/gpr_datasets_granite.md` — Krietsch 2020 Grimsel + USGS
  Mirror Lake as the only open granite GPR datasets; TN gap.
- `research/scan_and_gpr_datasets.md` — consolidated catalog of all
  scan / mesh / GPR datasets (12 sources); ingest routing; license
  flags for Independence Rock (NC) and Granite Dells TLS (unknown).
- `research/fabrication_bridge_gap_map.md` — per-layer avoid-double-work
  map (scan → cleanup → reconstruct → design → match → cut-plan →
  toolpath → robot → CAM → post-processor) vs Open3D, CloudCompare,
  Metashape, Geogram, CGAL, Voussoir plugin, structuralCircle, BRG,
  KUKAprc, visose/Robots, SprutCAM, RhinoCAM, etc. Explicit demarcation
  per layer. 2026-05-31.
- `testing/hitl_validation_log_2026_05_15.md` — human-in-the-loop
  punchlist for the 14 new components plus forward gates for each
  gap in the analysis.
- `testing/hitl_validation_log_full_masonry_quarry.md` — exhaustive
  canvas-validation log covering every Frahan / Masonry / Fracture /
  Slab / Mesh / Quarry component, plus 7 end-to-end workflows.

## Recent activity (newest first)

See `log.md` for the dated trail. Top-level signals:

- 2026-06-04 (nightshift) Workflow + plugin study promoted:
  `specs/03_frahan_workflows_spec.md` (W1-W15 registry),
  `research/slm_spines/` (15 workflow spines), `research/slm_cards/` (now
  20: top-10 + 10 gap-spines, full coverage), `research/roses_synthesis/
  ROSES_plugin_wide_review.md` (plugin-wide interdisciplinary), and
  `fabrication_workflows/quarry_to_monument.md` (evolved 5.34%->~80%).
  GeometryNumerics validated in-Rhino via MCP (5/5). (index over 150-cap; split pending.)
- 2026-05-22 Multi-format ingest layer (Shapefile + GeoJSON + SEG-Y + RD3
  + DT1 + dispatchers) + 4 new .rhp commands incl. `_FrahanWhichAlgorithm`
  + Lab subcat + 18 algorithm tags + 25 RelatedComponent cross-refs +
  15 HitL cards across 5 phases. Spec 02 promoted to v0.2. 769/91/0.
- 2026-05-15 (later) Monument packing (3 GH) + BCO inspector / ingestion
  / heterogeneous track (11 GH) + 3D DLBF in Core; 694/0/56.
- 2026-05-15 stale GH-metadata tests refreshed; 680/0/56.
- 2026-05-14 Layer 7 QuarryCutOpt + GeoCut + GeoPack v0 landed
  (commit `7e709ae`).
- 2026-05-08 Zhang 2024 cut-code parity + algebraic store (`c36fcf3`).
- 2026-05-08 BlockCutOpt module README (`2e165b6`).

## How to use this index

- AGENTS.md §Workflows §Session start: read this file, last 5
  `log.md` entries, and `health.md` before answering anything in a
  new session.
- AGENTS.md §Workflows §Query wiki: read this file first to locate
  the right sub-page, then read only that sub-page.
- AGENTS.md §Context budget: cap at 150 lines (this file), 300 lines
  per content page.

## Open questions / things to chase

Tracked in `health.md` (P0 / P1 / P2). Do not duplicate them here.
