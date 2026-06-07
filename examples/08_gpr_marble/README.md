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

## Block-layout study (cost vs volume vs balanced, on the REAL fracture grid)
The extracted beds drive a full dimension-block layout and cost/volume optimisation. The two profiles
yield 280 picks clustered into **3 real dipping beds** (0.72 m / 6.1 deg, 2.10 m / 0.9 deg, 3.70 m /
6.1 deg; sub-cm plane-fit RMS). The bed spacing (0.72, 1.38, 1.59, 0.30 m) caps block height. Blocks
are packed under `net + W*volume` swept from cost to volume.

![Extracted 3-bed grid](08b_bench_beds.png)

Oblique (bed-following) guillotine, the higher-yield plan:

| Objective | Blocks | Volume | NET value |
|---|---|---|---|
| Max cost | 13 | 32.16 m3 | $28,741 |
| Balanced | 15 | 36.32 m3 | $27,263 |
| Max volume | 20 | 38.85 m3 | $25,010 |

![Max cost](08c_maxcost.png) ![Balanced](08d_balanced.png) ![Max volume](08e_maxvolume.png)

Flat (orthogonal) guillotine is fabricable on any gangsaw today but the dip wedges are waste, so it
recovers far less (max cost 20.26 m3 / $17,454). The **georeferencing prize** is the gap: oblique cuts
recover +11.9 m3 (+59%) and +$11,287 per bench, the business case for the scanning + georeferenced
marking last mile.

![Flat guillotine baseline](08f_flat_guillotine.png)

Full method, statistics, and limitations: `STATISTICAL_REPORT.md`. Numbers: `08_marble_cost_volume_metrics.json`.
Geometry: `08_marble_block_layout.3dm`. Companion synthetic study: `../25_marble_gangsaw_cost/`.
