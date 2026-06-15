# Frahan StonePack - reviewer summary (v0.1.0-alpha)

For a collaborator reviewing the project before public release. One page. Style: short
sentences, no em dashes.

## What it is
A Rhino 8 / Grasshopper plugin for stone-fabrication readiness: the pre-CAM bridge from
design intent to machine-ready geometry for dimension stone, monuments, and dry-stone masonry.
Pipeline: GPR / scan -> discontinuity / fracture -> reconstruction -> DFN -> block packing +
cutting -> masonry equilibrium -> fabrication export. 187 Grasshopper components, Rhino-free
algorithm Core, research-grade benchmarks.

Status: experimental research prototype, v0.1.0-alpha. Independent open-source, not a
university or company product. Author: Libish Murugesan (ORCID 0009-0004-3238-4202).

## 60-second orientation (where to look)
- `README.md` - what it is, quick start, license.
- `docs/` static site (the design-system bundle): `index.html` (overview), `wiki.html` (all
  187 components with I/O), `architecture.html` (pipeline + connection graph), `research.html`
  (benchmarks). Open `docs/index.html` locally, or it deploys as GitHub Pages from `/docs`.
- `docs/components/` - the machine-readable contract: `ARCHITECTURE.md`, `RESEARCH.md`
  (every benchmark number is source-cited), `COMPONENTS.md` + `components.json` (per-component
  I/O), `DATA_STRUCTURE_AUDIT.md` (the port type contract).
- `docs/thesis/` - the long-form math + originality register (`90_originality.md`).
- `examples/` - 32 worked workflows (`.gh` + README + result image).

## What is verified
- Test battery: 1034 PASS / 0 FAIL / 147 SKIP (headless clean clone; skips need live Rhino).
  Reproduce: `dotnet run` in `tests/Frahan.StonePack.Tests`.
- Build: clean Release, 0 errors. Every kept algorithm has a measured benchmark + math
  derivation + named citation (`docs/benchmarks/`, `wiki/research/`).
- Component resilience: all 173 synchronous components route through a base that converts any
  uncaught exception to an orange warning, so bad/empty/missing input never red-crashes.
- Data-structure audit: 2028 ports, 0 custom types, one type defect found and fixed.
- Deep pre-release review (6 dimensions, adversarially verified; `docs/DEEP_REVIEW_2026-06-15.md`):
  0 critical, 0 high. The code is sound - build green, battery reproduces 1034/0/147 from a clean
  clone, no secrets/keys/private paths in any tracked file, corrected benchmark framings reproduce
  live. The remaining release work is documentation reconciliation, not code.

## Licensing posture (please sanity-check this)
- GPL-3.0, released for educational and research use (`LICENSE`, `NOTICE.md`).
- It bundles `Frahan.Kintsugi.Port` + `kintsugi.bin` (a port of PuzzleFusion++), which its
  authors license non-commercial research-only. This is consistent with a research-only
  release (their terms are "GPLv3 for research"). The README states this; full attribution is
  in `THIRD_PARTY_NOTICES.md`. The geometry path uses CGAL (GPL) + GMP (LGPL) native shims,
  optional at runtime with managed fallbacks.
- This is the one area where a second legal read is most valuable.

## Known limitations / where to be skeptical (we flag these ourselves)
- Masonry CRA: the ADMM QP cold-start convergence degrades past ~50 contact interfaces
  (54-interface wall 5.4 s, 147-interface 86 s). Roadmap high priority.
- RecoveryCascade is Core-validated but has no Grasshopper consumer; the shipped
  FractureBlockPack runs a separate recovery engine (silent-disagreement risk).
- Discontinuity worker speedup is ~1.4x vs Open3D KD-tree at matched k=24 (NOT the retracted
  "215x vs CloudCompare", which compared different neighbourhoods - see `RESEARCH.md`).
- Kintsugi: the pure-C# diffusion denoiser drifts ~3-5% from libtorch kernels; it is the only
  direct-port and is quarantined out of the default install.
- A few example timings lack machine/date provenance (minor, noted for cleanup).
- The hole-packer rect fast-path multiplier in the benchmark doc/thesis (146x native, ~22,000x
  Sparrow, 0.148 ms) reflects an earlier run; the shipped bench test reproduces ~62x / ~9,400x
  (0.347 ms) on the test machine. The qualitative result (fastest valid hole packer, valid where
  Sparrow is invalid) is unchanged. This figure is on the documentation punch-list.
- A documentation punch-list remains from the deep review (`docs/DEEP_REVIEW_2026-06-15.md`): some
  thesis/benchmark citations point at dev-side `outputs/` study folders not bundled here, and the
  SuiteSparse/OpenBLAS/libgfortran notices owed by the statically-linked BFF exe are pending. The
  clearest items are already fixed: stale licensing BLOCKER rows in the originality register (LICENSE
  + THIRD_PARTY are resolved), an out-of-repo example link, a CC-BY-NC-ND marble-GPR mislabel (it is
  CC BY 4.0), 20 broken thesis TOC links, and a stale "SKELETON" label on two now-working components.

## How to build and run
- Build: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release` (net48).
- Install: `git lfs pull` then `install/deploy.ps1` with Rhino closed; open an `examples/` `.gh`.
- Benchmarks: `tools/Frahan.StonePack.Harness --packbench` / `--pack2dstudy`.

## Most useful feedback
1. The licensing posture (GPLv3 + Kintsugi non-commercial bundling) - is it stated correctly?
2. Any benchmark claim that reads as overstated (we tried to source-cite every number).
3. Whether the originality classification in `docs/thesis/90_originality.md` holds up.
4. Anything that would embarrass the project once public.
