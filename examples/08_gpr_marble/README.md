# Example 08 - GPR fracture survey (Botticino marble, 600 MHz grid)

Read a 600 MHz GPR grid survey of Botticino marble, migrate, and extract fracture reflectors for the
marble-quarry block-layout decision. Companion to the granite GPR spine (example 03). Units: meters.
Style: short sentences, no em dashes.

![Marble GPR radargram (AGC-gained)](08_gpr_radargram_marble.png)

## Data (colocated, works out of the box)
The GPR files are bundled next to the cards in `gpr_data/` so the workflow runs without external
downloads:

- `gpr_data/LA010001.DT` + `.HDR_DT` (185 traces x 512 samples), `LA010002.DT` + `.HDR_DT` - IDS/GSSI
  `.DT` radargrams of Botticino marble. Verified readable via `GprFileReader.Load`.

Full provenance: `gpr_data/_SOURCE.md` and `../../data/DATA_ACCESS.md` (Drive master folder
https://drive.google.com/drive/folders/1mDj1Z20BB70SrkjQKnU6O3kDbfuA-mcS).

## Cards
- `marble_600_grid1.gh`, `marble_600_grid2.gh`, `marble_600_grid3.gh` - the three grid-line workflows
  (ingest -> migrate -> extract -> 3D fracture surface). Set the GPR file input to a `gpr_data/*.DT`.

## Run
1. Open Rhino 8 + Grasshopper with the Frahan `.gha` deployed.
2. Open a `marble_600_grid*.gh`. Point the GPR file input at `gpr_data/LA010001.DT`.
3. Flip the per-stage Run toggles (INGEST -> MIGRATE -> EXTRACT). The radargram + picks appear.

## Tested
`GprFileReader.Load` reads `LA010001.DT` (185 x 512). The AGC-gained radargram above is rendered
directly from that file. Component: `GPR Fracture Extract` / `GPR Fracture Surfaces 3D` (Frahan > Quarry).
