# Frahan StonePack

A Rhino / Grasshopper plugin for stone-fabrication readiness: the bridge layer between design intent and
machine-ready fabrication for dimension stone, monuments, and dry-stone masonry. It covers the pipeline
GPR / scan -> fracture mapping -> 3D reconstruction -> block packing + cutting -> masonry assembly ->
fabrication export, plus a research-grade algorithm library (2D/3D packing, no-fit-polygon nesting,
block-cut optimization, masonry equilibrium, edge-matching, surface mosaicing).

License: GPL-3.0 (see LICENSE). The plugin links `Frahan.Kintsugi.Port` (a GPL-3.0 port), so the whole
distribution is GPL-3.0.

## Quick start (users)
1. Rhino 8 (Windows). Build `src/Frahan.StonePack.GH` (net48) -> `Frahan.StonePack.gha`.
2. Close Rhino. File-copy the `.gha` + `Frahan.StonePack.Core.dll` (and the E57 worker py + native libs)
   into `%APPDATA%/Grasshopper/Libraries/`. One `.gha` only.
3. Open Rhino + Grasshopper. The `Frahan` ribbon tab holds the component families.
4. Open an `examples/` definition to see a full workflow on bundled sample data (`data/`).

## Quick start (developers)
- See `docs/INSTALL.md` for the toolchain (dotnet, RhinoCommon HintPath, the headless `tools/` harness).
- Build: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release`.
- Test: `tests/Frahan.StonePack.Tests` (xUnit-style runner; ~983 tests). Headless packer benches:
  `tools/Frahan.StonePack.Harness --packbench` and `--pack2dstudy`.
- Read `AGENTS.md` (orchestration rules) + `handoffs/` before contributing. Read `CONTRIBUTING.md`.

## Repository layout
- `src/` — the 5 modules: Core, GH (.gha), Rhino (.rhp), EdgeMatching.Core, Kintsugi.Port (GPL-3.0).
- `tools/` — headless harness + GPR bench. `tests/` — the test suite.
- `examples/` — master-spine workflows (`.gh` + `.3dm` + README, referencing `data/`).
- `data/` — sample datasets per workflow (see `data/ATTRIBUTION.md`; LFS at the public step).
- `wiki/` — curated research: `research/` (SLM/PRISMA/ROSES studies, algorithm cards), `algorithms/`,
  `specs/`, `papers/`. `research/` — long-form math derivations + research-level coding context.
- `handoffs/` — human + agent onboarding, `HANDOFF_LATEST.md`, `KNOWN_BUGS.md`.
- `docs/` — install, build, deploy, architecture.

## What makes it research-grade
Every kept algorithm has a measured benchmark, a math derivation (SLM tier), a statistics review (PRISMA),
and an interdisciplinary synthesis (ROSES). Example: the 2D nester crosses the 80% stock-utilization bar at
0 overlap with holes; the studies are in `wiki/research/`.
