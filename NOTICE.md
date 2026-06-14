# NOTICE — Frahan StonePack

Frahan StonePack
Copyright (C) 2026 Libish Murugesan and contributors.

This is an **early alpha (v0.1.0-alpha), experimental / research prototype**: an
**independent open-source research implementation** for stone / geometry-processing
workflows in Grasshopper, under active development. It is **not** an official product of
any university or company. Released for **public testing, feedback, and citation of the
initial implementation** (educational and research use). It is licensed under the
**GNU General Public License v3.0** (see `LICENSE`). Every source file's copyright is
held by its authors; the combined work is distributed under GPLv3.

## Important third-party term (read before any non-research use)

This distribution bundles `Frahan.Kintsugi.Port` and its model weights
(`install/weights/kintsugi.bin`), which are a port of **PuzzleFusion++**
(Wang, Chen, Furukawa 2025). The PuzzleFusion++ authors release their code, data,
and weights for **research purposes only, under GPLv3, and NOT for commercial use**.
That upstream restriction travels with the Kintsugi module and its weights here.

Consequently:
- Use this software for **educational and research purposes**. Do not use it
  commercially.
- A purely GPL-3.0, commercial-capable subset can be obtained by **excluding the
  `Frahan.Kintsugi.Port` module + `kintsugi.bin`** from a build; the rest of the
  plugin is the project's own code plus permissive/GPL third-party libraries
  (see `THIRD_PARTY_NOTICES.md`).

## Attribution

- Learned 6-DoF reassembly: **PuzzleFusion++**, Wang, Chen, Furukawa, ICLR/arXiv:2406.00259.
  Non-commercial research-only. See `src/Frahan.Kintsugi.Port/LICENSE.txt`.
- Geometry kernels (GPL), flattening, mesh/IO libraries, and bundled datasets:
  see `THIRD_PARTY_NOTICES.md` and `data/ATTRIBUTION.md` for full per-item license + DOI.

## How to cite

See `CITATION.cff`. A Zenodo DOI is minted for the v0.1.0-alpha release.
