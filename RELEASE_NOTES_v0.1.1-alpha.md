# Frahan StonePack v0.1.1-alpha — research preview (2026-07-07)

Status: **Experimental / research prototype**. License: **GPLv3**. Independent
open-source, not an official university or company product.

A patch release over
[v0.1.0-alpha](https://doi.org/10.5281/zenodo.21209690). The plugin itself
gains one small additive Core API; the headline work is the surrounding
ecosystem — a browser demo, a verified mathematics layer, and a public risk
register — plus the first Rhino-free steps toward headless deployment. No
breaking changes; component GUIDs are unchanged.

## Highlights

### Browser nest demo (no install, no backend)
A client-side nesting demo now lives on the docs site:
<https://libishm1.github.io/Frahan/nest/>. Import a DXF or SVG, nest with the
**actual `ContactNfpHoleNester` engine compiled to WebAssembly**, export the
packed layout — all in the browser. The CAD file never leaves your machine.
It is hole-aware: mark defects red in your CAD (or toggle the sample defects)
and parts route around them. This is the same benchmarked engine that beats
OpenNest 2.89 on valid-layout utilization, not a re-implementation.

### Verified mathematics + risk register
The [mathematics section](https://libishm1.github.io/Frahan/wiki/research/math/INDEX/)
documents what each subsystem computes, derived from the shipping code with
22+ code-vs-literature deviations flagged, a four-layer verification ladder,
four **Z3 machine-proved** theorem instances (NFP, IFP, BLF-vertex, inscribed
friction cone), and a Lean 4 + Mathlib formalization plan. A consolidated
[risk register](https://libishm1.github.io/Frahan/wiki/research/RISK_REGISTER/)
triages readiness for deployment.

## Plugin changes (in the .gha / yak)

- **Honest signed-tetra mesh volume** (`MeshPackItem.MeshVolume`,
  `MeshPackResult.FillRatioMeshVolume`): true closed-mesh volume
  `V = (1/6)|sum a·(b×c)|` for the honest 3D packing density numerator,
  computed in Core without RhinoCommon (the bbox `VolumeEstimate` remains for
  the fast over-reporting path).
- **`KinematicAnalysis` is now Rhino-free** (risk H2): the geology
  wedge/planar/toppling feasibility component no longer needs RhinoCommon
  (78 → 77 Rhino-bound Core files), a first step toward headless/service use.
- **Corrections**: the `Kriging.Predict` variance header comment corrected to
  match the code (latent variance); the multi-probe LSH construction in
  `SegmentHashIndex` now carries its Lv et al. 2007 citation.

## Measurements

- First **headless C# 3D density measurement** on the real ETH1100 subset:
  16/16 stones packed, honest signed-tetra density 0.073 vs bbox 0.195 — a
  **2.68× bbox over-report** (the container-independent, measured confirmation
  that bbox density over-states true fill).

## Install

Rhino 8 (Windows) → `_PackageManager` → search **FrahanStonePack**. Or the
offline zip. See <https://libishm1.github.io/Frahan/docs/INSTALL/>.

## Cite

Murugesan, L. (2026). *Frahan StonePack (0.1.1-alpha)*. Zenodo. (Version DOI
minted on release; concept DOI resolves to the latest version.)
