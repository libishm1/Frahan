# Kintsugi training data + weights attribution

Frahan.Kintsugi.Port is a GPL-3.0 port of PuzzleFusion++ (fractured-object reassembly). The training
data, weights, and parity fixtures live with the module at `../../src/Frahan.Kintsugi.Port/`:
- `Weights/puzzlefusion-plusplus/` — the PuzzleFusion++ upstream (training code, configs, data-prep). GPL-3.0.
- `Weights/parity_fixtures.bin` + `export_parity_fixtures.py` — port-parity fixtures.
- `Weights/extract_breaking_bad_*.py` — extractors for the Breaking Bad dataset (the upstream training set).
- `Models/`, `Primitives/`, `Outer/` — module runtime data.

Upstream training dataset: Breaking Bad (Sellan et al.), https://breaking-bad-dataset.github.io/ — fetch via
the extractor scripts; large, not bundled (LFS / external at the public step). PuzzleFusion++:
https://github.com/eric-zqwang/puzzlefusion-plusplus (GPL-3.0). Honor the upstream licenses.
