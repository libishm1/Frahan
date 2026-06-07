# Handoff 2026-06-07 - Marble cost/volume/balanced + flat-vs-oblique guillotine + georeferencing

Read AGENTS.md first (every section applies). Truth criterion (c) visual validation, HITL gates in
section 6 are mandatory stops. This handoff covers the cost/volume/balanced block-cutting work and the
georeferenced-marking last mile. Style: short sentences, no em dashes.

## What shipped this session (all committed; see push status at end)

### Example 25 - synthetic marble gangsaw cost study (commit f9db12c)
`examples/25_marble_gangsaw_cost/`. A 6x3x3 m bench, 3 oblique fractures, blocks packed under
`net + W*volume` swept cost -> balanced -> volume.
- max cost 30.5 m3 / $25,650 ; balanced 35.5 / $24,650 ; max volume 38.0 / $16,700.
- Plus the balanced guillotine cut SEQUENCE as mesh saw planes: 2 perp-Y (cyan) + 2 perp-Z (yellow) +
  25 perp-X rips (magenta) = 29 passes, staged 25d..25g over the placed blocks.
- The earlier identical-results bug is fixed: a clean bench tiles perfectly with big blocks, so volume
  and cost converge. Oblique fractures create awkward runs (1.0 m and 2.5 m) where they diverge.

### Example 08 - REAL Botticino marble GPR cost/volume study (commit 6f8cb72)
`examples/08_gpr_marble/`. Drives the whole study from the real GPR (`gpr_data/LA010001.DT`,
`LA010002.DT`, CC-BY-NC-ND, research only).
- Pipeline: `GprFileReader.Load` -> `RadargramProcessor.ToGrid/Run` (v=0.10 m/ns, eps_r 9) ->
  `FractureExtractor.Extract` -> 280 picks -> clustered into 3 dipping beds:
  0.72 m / 6.1 deg, 2.10 m / 0.9 deg, 3.70 m / 6.1 deg (plane-fit RMS 5-15 mm).
- Bed spacing (0.72, 1.38, 1.59, 0.30 m) caps block height. Catalogue A 3.0x1.5, B 2.0x1.5,
  C 1.5x1.0, D 1.0x1.0; price/m3 falls and cut/m2 rises with size; cut $200/m2.
- TWO guillotine modes:
  - OBLIQUE (bed-following, blocks are bed-bounded hexahedra): max cost 32.2 m3 / $28,741,
    balanced 36.3 / $27,263, max volume 38.9 / $25,010.
  - FLAT (orthogonal dip-safe, fabricable today): max cost 20.3 / $17,454.
  - GEOREFERENCING PRIZE: oblique recovers +11.9 m3 (+59%) and +$11,287 per ~50 m3 bench.
- Slab stage (20 mm + 3 mm kerf) reported per objective.
- Deliverables: `STATISTICAL_REPORT.md`, `08_marble_cost_volume_metrics.json`,
  `08b_bench_beds.png`, `08c_maxcost.png`, `08d_balanced.png`, `08e_maxvolume.png`,
  `08f_flat_guillotine.png`, `08_marble_block_layout.3dm`.

### Research paper (Agent-orchestration repo, commit 0f841be)
`outputs/2026-06-04/gpr_extraction/deep_fracture_review/MASTER_PAPER.tex`: new Results 4.7
(Table tab:costvol) + research-gap (v) closed, open gap (vi) refined. This was named future work.

## The bug you caught (fixed)
Flat axis-aligned blocks placed at each bed's MEAN depth crossed the dipping beds (a 6 deg bed moves
~0.5 m over the 4.8 m span). Fix: blocks are now bed-bounded hexahedra whose top/bottom faces lie on the
real dipping bed planes (offset by the 50 mm keep-out), so no block crosses a fracture. Verified in a
front-elevation capture. The flat (orthogonal) version keeps axis-aligned boxes but shrinks each layer
to the dip-safe envelope (top = deepest point of upper bed, bottom = shallowest of lower bed).

## Terminology locked with the user (2026-06-07)
- GUILLOTINE = full-span straight cut, edge to edge. Can be flat (axis-aligned) OR oblique (tilted).
- OBLIQUE GUILLOTINE = the bed-following cuts (3 tilted bed-parallel passes + vertical rips). Higher
  yield. Lead with this (it is built and correct).
- Sequence the user wants: GUILLOTINE FIRST (flat, fabricable today), OBLIQUE LATER (needs the marking
  chain). Localised adjustment of each mark is left to the stonemason; the system supplies the plan.

## OPEN - the last mile (top priority next)
Cutting planes -> georeferenced physical marking so fabricators/masons can actually cut:
1. Take the GPR-fitted bed planes (UTM/world georeferenced) + the per-block cut planes.
2. Scan the extracted block (LiDAR/photogrammetry; Read LAS Cloud / E57 / photogrammetry ingest exist).
3. Register the scan to the GPR bed model (ICP / control points; marker positioning is in scope per
    project_photogrammetry_scope_decision).
4. Project the (oblique) saw lines onto the real block surface as marking instructions.
This is what makes oblique cuts executable and unlocks the +59% / +$11,287 prize. See MASTER_PAPER gaps
(vi) wire-sag tilted-plane model + (ix) marking, and project memory
`project_marble_cost_volume_georeferencing`.

Secondary: promote the evolved voussoir-from-rubble logic into a real Frahan.RubblePack component
(logic proven ex21/22); vault form-finding via compas-RV.

## Example 26 - Loviisa surface fracture map via shapefile reader (commit 91556ce)
`examples/26_loviisa_surface_fractures/`. Reads a real ESRI Shapefile of mapped surface fracture traces
through `ShapefileFractureReader.Load` (Frahan > Quarry > Ingestion, NetTopologySuite.IO.Esri) and
renders the strike-coloured fracture map. KB11: 708 traces / 6483 verts, EUREF_FIN_TM35FIN, two conjugate
sets (~15 deg, ~105-120 deg). Colocated KB11 .shp set + area boundary + GH card (Vector Fractures Loader
F2D00BEC) + .3dm. The vector-input counterpart to the GPR depth surveys (ex 03/08).
- Dataset `data/loviisa/`: full 8-site manual traces + areas (2.1 MB, the small in-git exception; LFS +
  scoped .gitignore negation `!/data/loviisa/**/*.shp` etc). `DATASET.md` + DATA_ACCESS row.
- install/plugin now ships NetTopologySuite(.Features/.IO.Esri.Shapefile/.IO.GeoJSON) + the net48 System
  polyfills (Memory/Buffers/Numerics.Vectors/CompilerServices.Unsafe) so the GH loader resolves at
  runtime (was missing; same "ship all deps" rule as the BFF runtime).

## Migration state (TWO repos until migration completes)
User confirmed 2026-06-07: a month of work lives in code_ws (Agent-orchestration-main); it is being
migrated into Frahan. Until the user says migration is done, BOTH repos are live. Frahan = the
collaborators repo (push to main, keep it CLEAN, no stray branches). Agent-orchestration = the dev
repo (push to docs/frahan-autonomous-nightshift). The migration-gap TODO (validated GH workflows +
datasets in code_ws not yet in Frahan) is in `MIGRATION_GAP_TODO.md` (this dir) and appended below.

## How to reproduce (slot armadillo, run_python = CPython3)
- Load Core: `os.add_dll_directory(install/plugin); clr.AddReference("Frahan.StonePack.Core.dll")`.
- GPR: `GprFileReader.Load(path)`; `B,dt,dx = RadargramProcessor.ToGrid(g)` (out params returned by
  pythonnet); `RadargramProcessor().Run(B,dt,dx,0.10)`; `FractureExtractor().Extract(e,dt,dx,0.10)`.
  IReadOnlyList has `.Count`, not len(); index with `rp[i]`.
- Capture: hide/show object ids, `view.CaptureToBitmap(Size(w,h)).Save(path)`; fixed framing via
  `vp.ZoomBoundingBox(bbox)`. NOTE doc.Objects.Count includes deleted-undo entries; the .3dm written by
  WriteFile only carries the live (non-deleted) objects (verify by file size).
- The packing/economics scripts are reproducible (DP over 0.1 m cells; net + W*volume).

## Migration-gap TODO (audited 2026-06-07) - full list in MIGRATION_GAP_TODO.md
Coverage ~70-75% by topic. Top remaining (validated in code_ws, no Frahan example):
- HIGH: 27 polygonal masonry incl Voronoi 2D/3D; 28 monument packing; 29 edge-matching v2; 30 BlockCutOpt A4.
- MEDIUM: carving-stages v2 sweep; ship ETH1100 / Stanford-bunny / Tongjiang data (16/17 + scan-ingest not self-reproducible without it).
- LOW: tu1208 multi-format GPR; GeoCrack (reference-only, Drive); CI (net-new).
See MIGRATION_GAP_TODO.md for sources, targets, and the per-item recipe.
