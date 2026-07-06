# Frahan StonePack — documentation folder

Software repository: <https://github.com/libishm1/Frahan>
Live documentation site: <https://libishm1.github.io/Frahan/>

The site is built with MkDocs Material by `.github/workflows/pages.yml` on
every push to `main` (config: `mkdocs.yml` at the repo root). It assembles the
repo's markdown + images with the directory structure preserved, so relative
links work both on GitHub and on the site.

## What lives here

| Path | What |
|---|---|
| `components/COMPONENTS.md` | The generated component reference — 270+ components with GUIDs, algorithm citations, inputs/outputs, related-component edges. Regenerate with `components/extract_components.py` after component changes (also refreshes `components.json`, the connection maps, and `ICON_LIBRARY.md`). |
| `results/RESULTS.md` | Results & benchmarks at a glance — utilization/validity studies, the OpenNest head-to-head, masonry + 3D packing numbers, test health. Every number is measured; the protocol is described in place. |
| `INSTALL.md` | Install (users: Package Manager; developers: build from source). |
| `STONEPACK_THESIS.md` | The long-form applied thesis in support of the software. |
| `SUPERSESSION_MAP.md` | Which legacy components were superseded by which current ones, with benchmarks. |
| `PERSONA_MAP.md` | Component groups mapped to user personas (quarry engineer, mason, artist, …). |
| `REVIEWER_SUMMARY.md` | Orientation guide for reviewers/collaborators. |
| `DEEP_REVIEW_2026-06-15.md` | Dated point-in-time repo review (historical record). |
| `benchmarks/` | Benchmark math + parity studies referenced by RESULTS.md. |
| `validation/` | Validation cards and datasets referenced by the wiki. |

The research record (studies, specs, engineering log) lives in [`../wiki/`](../wiki/index.md).

## Cite

Murugesan, L. (2026). *Frahan StonePack (0.1.0-alpha)*. Zenodo.
<https://doi.org/10.5281/zenodo.21209690>

---
Frahan StonePack · v0.1.0-alpha · GPL-3.0 · Libish Murugesan
