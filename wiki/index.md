# Frahan StonePack — research wiki

Last updated: 2026-07-05

This wiki holds the research and engineering record behind the plugin: the
specs the components were built to, the algorithm studies with their
benchmarks, and the design decisions. It is a working research notebook, not
polished documentation — for the user-facing docs start at the
[component reference](../docs/components/COMPONENTS.md) and
[results & benchmarks](../docs/results/RESULTS.md).

To discuss the research or the plugin, [book a meeting](https://calendly.com/libish-1234/30min) (Calendly).

## Layout

```
wiki/
├── index.md      this file
├── log.md        dated engineering log (append-only, newest first)
├── research/     long-form studies with benchmarks and figures
└── specs/        numbered design specs the plugin was built to
```

## Research studies (`wiki/research/`)

- [`edge_matching_theory_vs_implementation.md`](research/edge_matching_theory_vs_implementation.md)
  — the edge-matching stack (descriptor buckets, soft-ICP, Fréchet gap gate)
  audited against the computational-geometry literature; gaps R1–R6 with a
  build plan (R1/R2 shipped in 0.1.0-alpha).
- [`tolerances_dimensions_slm_roses.md`](research/tolerances_dimensions_slm_roses.md)
  — per-application units, dimension ranges, and the tolerance budget
  (`eps_geo = max(floor, 1e-3·L)`, kept under a third of kerf).
- [`stereotomy_voussoir_from_rubble.md`](research/stereotomy_voussoir_from_rubble.md)
  — cutting voussoirs from irregular rubble stock.
- [`kintsugi_port_parity.md`](research/kintsugi_port_parity.md) — the learned
  reassembler (PuzzleFusion++ Port): the denoiser attention-mask parity fix that
  made the transformer bit-exact, the in-distribution reassembly result, and the
  honest scope (deterministic solver stays primary; learned Port is
  research-only, in-distribution).
- [`research/packing/`](research/packing/SYNTHESIS_2D.md) — the 2D/3D packing
  study series: synthesis reports (2D, 3D, beyond-BLF), the pack-study
  reports with utilization/validity tables, the masonry-vs-quarry decision
  note, and all benchmark figures. The evolved exact-NFP hole-aware nester
  that ships as *Sheet Nest* came out of this series.
- [`research/slm_cards/`](research/slm_cards/_INDEX.md) — systematic
  literature-mapping cards: one page per workflow spine (mining plans,
  coursing layouts, surface flattening, …) tying components to sources.

## Specs (`wiki/specs/`)

Numbered 00–22 (design-time; the shipped plugin deviates where the log says
so): project overview (00), software principles (01), architecture (02),
module map + workflow registry (03), Grasshopper component spec (04),
2D/surface/3D packing (05–07), GeoPack/GeoCut/QuarryCutOpt (08–10), mesh +
native backend (11), learning-guided packing (12), testing + validation
(13–14), implementation plan (15), licensing + porting policy (16), roadmap
(17), open questions (18), source relocation (19), CGAL audit + build (20–21),
CoACD build (22). Plus undated planning notes: design philosophy,
architectural decisions, component decomposition, cathedral-scale fitting,
scan-to-mill architecture, HITL cards plan.

## Engineering log (`wiki/log.md`)

Dated, append-only record of what was built, measured, and decided — the
provenance trail behind the results tables. Newest entries first.

## How results are validated

Every performance or correctness claim in these pages traces to a runnable
check: the headless test battery (`tests/Frahan.StonePack.Tests`), the exact
0-overlap layout validator, the RBE/CRA equilibrium gates, or a benchmark
protocol run documented in [`docs/results/RESULTS.md`](../docs/results/RESULTS.md).
See [`CONTRIBUTING.md`](../CONTRIBUTING.md) for the full verification stack.
