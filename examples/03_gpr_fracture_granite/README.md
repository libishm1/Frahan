# Example 03 — GPR fracture extraction (granite, geologist)

The benchmarked granite-domain GPR fracture workflow. Reads a MALA GPR radargram of a granite tunnel,
migrates + extracts fracture reflectors, and reports the picks. This is the geologist master-spine.

## Data - download from Google Drive first
Download the GPR radargram from the shared Drive master folder and place it under
`Data/gpr/grimsel/`:
- MASTER FOLDER: https://drive.google.com/drive/folders/1mDj1Z20BB70SrkjQKnU6O3kDbfuA-mcS?usp=sharing
- File to download: `gpr/grimsel/GPR_AU_N-to-S.rd3` (AU tunnel; `GPR_VE_S-to-N.rd3` is the VE tunnel).

- `../../data/gpr/grimsel/` — Grimsel ISC GPR (MALA GX160, AU + VE tunnels, granite). DOI
  10.3929/ethz-b-000420930, CC-BY-4.0. See `../../data/ATTRIBUTION.md`.
- Validated end-to-end (granite_160 preset): AU 1472 / VE 1485 fracture picks; dt = 0.4464 ns, dx = 0.0498 m.

## Run
1. Open Rhino 8 + Grasshopper with the Frahan `.gha` deployed (see `../../docs/INSTALL.md`).
2. Open `granite_160_AU.gh`. Set the GPR file input to `../../data/gpr/grimsel/<AU file>.rd3`.
3. Press the per-stage `Run` toggles in order (INGEST -> MIGRATE -> EXTRACT -> REPORT).
4. The fracture picks + 3D surfaces appear; the report node emits the geologist brief.

## Status
Baseline copied from the last working/tested HILT card (`outputs/2026-06-04/gpr_extraction/hitl_gh/cards/`).
Pending live regeneration: repath the GPR input to the `data/gpr/grimsel/` link, add coloured stage groups
+ scribbles per `../GRASSHOPPER_BEST_PRACTICES.md`, tune params, and capture a shaded viewport PNG. The
Frahan plugin + components are verified loading on the live slot.

## Best practices applied
See `../GRASSHOPPER_BEST_PRACTICES.md`: COVER panel with site/CRS/units, default-false Run gates on the
heavy migration node, data referenced from `data/` (never internalized), Warnings on null inputs.
