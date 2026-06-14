# Frahan StonePack v0.1.0-alpha — experimental research prototype (2026-06-15)

Status: **Experimental / research prototype**. License: **GPLv3**. Scope: **independent
open-source implementation, not an official university or company product.**

This is an **early alpha release of an independent open-source research implementation for
stone / geometry-processing workflows in Grasshopper**. The tool is **experimental and under
active development**, and this release is intended for **public testing, feedback, and citation
of the initial implementation**.

It is a Rhino 8 / Grasshopper plugin for stone-fabrication readiness: the bridge layer between
design intent and machine-ready fabrication for dimension stone, monuments, and dry-stone
masonry. Expect rough edges; APIs and component GUIDs may still move.

## What is in it

End-to-end pipeline, each stage a Grasshopper component family on the `Frahan` ribbon tab:

- **GPR + scan ingest** — radargram migration + fracture extraction; point-cloud load,
  downsample, normals, ICP, reconstruction (Geogram Poisson + CGAL fallback).
- **Point-cloud discontinuity / joint sets** — clean-room CSR worker: PCA normals + FACETS
  facets + Watson joint sets + stereonet + Palmstrom block size (Discontinuity Sets D5F10048).
- **Discrete fracture networks** — deterministic infinite-plane DFN and stochastic finite-disc
  **Baecher DFN** (Fisher poles, lognormal persistence) for Monte-Carlo block-yield.
- **Block packing + cutting** — BlockCutOpt (+ evolved Omni solver), wire-saw staged guillotine,
  recovery cascade; 2D nesting incl. **hole-aware HoleNest / ContactNfpHoleNester**.
- **Masonry** — polygonal / rubble wall generation, exact-joint assembly, **CRA equilibrium**
  certification, Lambda + J interlock metrics.
- **Surface + restoration** — BFF flatten + pack + lift (Trencadis mosaic), edge-matching, and
  an optional learned 6-DoF reassembly module (Kintsugi; see License).
- **Fabrication export** — cut plans, robot/KUKA adapters.

## Quality

- Test battery: **1034 PASS / 0 FAIL / 147 SKIP** (2026-06-14, clean clone, headless; skips are
  Rhino-runtime + optional-dataset gates).
- Benchmarks + figures with methodology: `docs/results/RESULTS.md`, `docs/benchmarks/`,
  `wiki/research/`. Every kept algorithm has a measured benchmark + math derivation + citation.

## Install

1. Rhino 8 (Windows). `git lfs pull`, then run `install/deploy.ps1` (or `deploy.sh`) with Rhino closed.
   The bundle ships the `.gha` + native libs (`install/plugin/`). Or build from source per `docs/INSTALL.md`.
2. Open Rhino + Grasshopper; the `Frahan` tab appears. Open an `examples/` definition for a full workflow.

## License + citation (read this)

- **GPL-3.0**, released for **educational and research use** (`LICENSE`, `NOTICE.md`).
- Bundles **PuzzleFusion++** (Kintsugi module + `kintsugi.bin`), which its authors license
  **non-commercial research-only**. Do not use this software commercially. A commercial-capable
  GPL-3.0 subset is obtainable by excluding the Kintsugi module. Full attribution:
  `THIRD_PARTY_NOTICES.md`, `data/ATTRIBUTION.md`.
- Cite via `CITATION.cff`. Zenodo DOI minted for this release.

## Known limitations (alpha)

- Many heavy nodes ship a default-FALSE `Run` toggle; press per-stage Run in order (avoids long solves).
- Some example data blobs are gitignored and hosted on Google Drive (see `data/DATA_ACCESS.md`); a few
  bundled datasets are research-use-only.
- Rhino-runtime tests (147) skip headless; they require a live Rhino install.
- Component set is broad and still consolidating; see `docs/SUPERSESSION_MAP.md` for evolved-vs-legacy.

## Author
Libish Murugesan (ORCID 0009-0004-3238-4202). Independent research.
