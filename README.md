# Frahan StonePack

> **v0.1.0-alpha - experimental / research prototype.** An independent open-source research
> implementation for stone / geometry-processing workflows in Grasshopper, under active development.
> This release is for **public testing, feedback, and citation of the initial implementation**.
> **Not** an official university or company product. Licensed GPLv3; bundles a non-commercial
> research-only component (Kintsugi / PuzzleFusion++) - see the License note below and `NOTICE.md`.
> Cite via `CITATION.cff`.

![Fracture modelling to block packing](docs/results/hero_fracture_block_packing.png)

*Fracture modelling -> wire-saw block packing: intact, saw-separable stock (blue/green) recovered from a
fractured bench around the mapped fracture planes (green). The full GPR -> block-yield method is described
in the paper preprint ([doi.org/10.21203/rs.3.rs-10035624/v1](https://doi.org/10.21203/rs.3.rs-10035624/v1)).
See [docs/results/RESULTS.md](docs/results/RESULTS.md) for all benchmarks + process results, no Grasshopper required.*

A Rhino / Grasshopper plugin for stone-fabrication readiness: the bridge layer between design intent and
machine-ready fabrication for dimension stone, monuments, and dry-stone masonry. It covers the pipeline
GPR / scan -> fracture mapping + point-cloud discontinuity / joint-set extraction -> 3D reconstruction ->
discrete fracture network (DFN) -> block packing + cutting -> masonry assembly -> fabrication export, plus
a research-grade algorithm library (2D/3D packing, hole-aware no-fit-polygon nesting, block-cut
optimization, masonry equilibrium, edge-matching, surface mosaicing, joint-set + Baecher DFN).

License: **GPL-3.0** (see `LICENSE`), released as a **research preview for educational and research use**.
The plugin bundles `Frahan.Kintsugi.Port` + `kintsugi.bin`, a port of **PuzzleFusion++** whose authors permit
research use only (GPLv3 for research, **not for commercial use**) - so this distribution is for research /
education, not commercial use. A commercial-capable GPL-3.0 subset is obtainable by excluding the Kintsugi
module. Full attribution + per-component licenses: `NOTICE.md`, `THIRD_PARTY_NOTICES.md`, `data/ATTRIBUTION.md`.
How to cite: the **software** via `CITATION.cff` (a Zenodo DOI is minted for v0.1.0-alpha); the **method**
via the paper - Murugesan, L. (2026), *A managed, uncertainty-aware pipeline from ground-penetrating radar
to dimension-stone block yield in fractured quarries*, **Research Square** preprint,
[https://doi.org/10.21203/rs.3.rs-10035624/v1](https://doi.org/10.21203/rs.3.rs-10035624/v1).

## Quick start (users)
1. Rhino 8 (Windows). Build `src/Frahan.StonePack.GH` (net48) -> `Frahan.StonePack.gha`.
2. Close Rhino. File-copy the `.gha` + `Frahan.StonePack.Core.dll` (and the E57 worker py + native libs)
   into `%APPDATA%/Grasshopper/Libraries/`. One `.gha` only.
3. Open Rhino + Grasshopper. The `Frahan` ribbon tab holds the component families.
4. Open an `examples/` definition to see a full workflow on bundled sample data (`data/`).

## Quick start (developers)
- See `docs/INSTALL.md` for the toolchain (dotnet, RhinoCommon HintPath, the headless `tools/` harness).
- Build: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release`.
- Test: `tests/Frahan.StonePack.Tests` (xUnit-style runner; 1034 PASS / 0 FAIL / 147 SKIP as of
  2026-06-14; skips = Rhino-runtime + optional-dataset gates). Headless packer benches:
  `tools/Frahan.StonePack.Harness --packbench` and `--pack2dstudy`.
- Read `AGENTS.md` (orchestration rules) + `handoffs/` before contributing. Read `CONTRIBUTING.md`.

## Repository layout
- `src/` — the 5 modules: Core, GH (.gha), Rhino (.rhp), EdgeMatching.Core, Kintsugi.Port (PuzzleFusion++
  port, non-commercial research-only; see `NOTICE.md`).
- `tools/` — headless harness + GPR bench. `tests/` — the test suite.
- `examples/` — master-spine workflows (`.gh` + `.3dm` + README, referencing `data/`).
- `data/` — sample datasets per workflow (see `data/ATTRIBUTION.md`; LFS at the public step).
- `wiki/` — curated research: `research/` (SLM/PRISMA/ROSES studies, algorithm cards), `algorithms/`,
  `specs/`, `papers/`. `research/` — long-form math derivations + research-level coding context.
- `handoffs/` — human + agent onboarding, `HANDOFF_LATEST.md`, `KNOWN_BUGS.md`.
- `install/` — ready-to-deploy binaries: the `.gha` + native libs (`plugin/`), Kintsugi Port weights
  (`weights/kintsugi.bin`, LFS), Breaking Bad parity samples (`data/`), BFF (`tools/`), and `deploy.ps1` /
  `deploy.sh`. Run `git lfs pull` then the deploy script (Rhino closed). See `install/INSTALL.md`.
- `docs/` — install, build, deploy, architecture.

## Results at a glance (no Grasshopper required)
See [docs/results/RESULTS.md](docs/results/RESULTS.md) for the measured benchmark + process figures: the 2D
stock-utilization study (evolved NFP-BLF crosses the 80% bar at 0 overlap with holes), the hole-aware
nester (HoleNest / ContactNfpHoleNester) head-to-head vs the OpenNest reference physics nester, the 3D
volumetric ratios (Dlbf best-of-orientation 70.4%), the fracture recovery / block-packing captures, and the
masonry/quarry decision. Test battery (2026-06-14): 1034 PASS / 0 FAIL / 147 SKIP from a clean clone
(skips = Rhino-runtime + optional-dataset gates).

## What makes it research-grade
Every kept algorithm has a measured benchmark, a math derivation (SLM tier), a statistics review (PRISMA),
and an interdisciplinary synthesis (ROSES). Example: the 2D nester crosses the 80% stock-utilization bar at
0 overlap with holes; the studies are in `wiki/research/`.
