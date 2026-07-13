# Handoff — examples validation plan (2026-07-07)

## Strategy: validate before replacing the kernel

Establish what actually works on the shipped **v0.1.1 `.gha`** *before* any
Rhino-free / kernel-swap work. This gives a known-good baseline (AGENTS.md
truth criterion c: visual validation in Rhino), so later headless/kernel
changes can be regression-checked against a validated set. Kernel work is
gated behind this.

## Inventory (51 example folders)

46 have a `.gh`; 44 have ≥1 image; 47 have a README. Gaps that need the most
work:

- **No README + no image + has `.gh`** (build README, validate, capture):
  `01_quarry_to_wall`, `02_masonry_assembly`, `03_quarry_to_slabs`, `28_hole_nest`.
- **README but no image** (validate + capture): `05_artist_pointing_machine`,
  `46_kinematic_intensity_screen`, `47_fabrication_handoff`.
- **No `.gh` (static / HITL-card examples)** — verify images + README current,
  no solve: `21_stereotomy_rubble_arch`, `22_pendentive_vault_rubble`,
  `23_quarry_to_slab`, `24_guillotine_cut_sequence`, `25_marble_gangsaw_cost`.
- **Everything else** (~39): re-validate the `.gh` on the v0.1.1 `.gha` +
  refresh/confirm the image.

## Per-example validation procedure

1. Deploy the current v0.1.1 `.gha` with ALL Rhino closed (incl. MCP slots —
   they file-lock the `.gha`): `pwsh -File install/deploy.ps1`. Skip if the
   deployed build is already v0.1.1 (check `Frahan.StonePack.gha` FileVersion).
2. Spawn a Rhino MCP slot (v8); force the GH plugin to load.
3. Open the `.gh` (grasshopper MCP `load_document`, or `GH_DocumentIO` in
   run_csharp). Solve (`NewSolution(true)`).
4. **Record the regression signals**: does it solve? any component in error
   (red)? any **unresolved** component (a GUID missing on the current `.gha` —
   the key breakage after the legibility rename + packer consolidation)? The
   hidden FreeNestX / Unified still LOAD (hidden ≠ removed), but confirm.
5. Bake outputs; set Top view; `ViewCapture` → JPG saved into the example
   folder as `<name>_v011.jpg` (per the show-images-via-Read-remote rule:
   downscaled JPG, colour by metric).
6. `Read` the JPG to confirm it is representative (truth criterion).
7. Confirm the README matches the shipped component names/GUIDs (post-rename)
   and the data it references.

## Working style — ONE at a time, human-in-the-loop (not autonomous batches)

This is a review loop, not a batch job. Per example:

1. Agent validates the `.gh` on the v0.1.1 `.gha` (deploy if needed) and
   captures the image.
2. Agent **presents it to Libish**: `Read` the JPG so it renders, plus the
   solve result (solves? errors? unresolved components?).
3. **Libish checks** the example and the image (is it correct, representative,
   the right view?).
4. Only on Libish's OK: record the status in the tracking table, keep/rename
   the image, then move to the NEXT example. Do not run ahead.

Libish sets the pace and reviews every example + image. Restart-safe: the
tracking table below is the resume point — start at the first row not marked
done.

## Review order (one by one)

- **First — gaps**: 01, 02, 03_quarry_to_slabs, 28, 05, 46, 47 (build the 4
  missing READMEs, validate, capture the 7 missing images).
- **Then — flagships (must work)**: 10_pack2d, 28_hole_nest, 11_pack3d,
  13_surface_mapping, 27_polygonal_masonry, 35_gpr_quarry_full_workflow,
  49_extraction_order_plan, 50_castle_keep_ifc.
- **Then**: the remaining `.gh` examples by number, one at a time.
- **Static (no `.gh`)**: 21-25 — Libish confirms images + READMEs current.

## Acceptance per example

- Solves with 0 errors on the v0.1.1 `.gha`, OR a documented reason it needs
  external data/plugins (see risks).
- ≥1 current, representative image in the folder.
- README accurate to the shipped components (names + GUIDs) and data.

## Known risks / data dependencies

- **External data not in the repo** (gitignored / LFS): the GPR + geology
  examples (03_gpr, 08, 26, 30, 33, 34, 40) and scans (04, 07, 32) may need
  data under `Data/` that is not committed. Validate structure + note the data
  dependency; use the bundled `gpr_csv` / fixtures where present.
- **Plugin deps**: robot examples (43_nbo_dry_stone_wall — wait, robot =
  44_nbo_to_robot) need visose/Robots for a full solve; 50_castle_keep_ifc
  needs Xbim (bundled). NBO settle (18, 43) uses BulletSharp (bundled).
- **Consolidation**: examples that referenced FreeNestX / Sheet Pack (Unified)
  still load (hidden components load), but the recommended path is now
  `Sheet Nest (Hole-Aware)` / `Sheet Nest (Live)` — update READMEs if they
  name the hidden ones.
- **Deploy file-lock**: every deploy needs ALL Rhino processes closed,
  including MCP slots. Kill `frahan_*worker` first.

## Tracking

**Image recapture flags (2026-07-07 full sweep — see Image-quality audit below):**
examples with ≥1 BAD image needing recapture during their HITL turn: **04, 07, 09,
10 (flagship), 13, 15, 17, 21, 27, 30, 32, 34, 42, 43, 44, 48, 49 (flagship),
vault**. Cleanest (leave alone): 25, 40, 41, 23, 35-figures, 08-packs.

| # | example | .gh | img | README | status |
|---|---|---|---|---|---|
| 01 | quarry_to_wall | 1 | 0 | no | needs README+img+validate |
| 02 | masonry_assembly | 1 | 0 | no | needs README+img+validate |
| 03 | gpr_fracture_granite | 2 | 1 | yes | revalidate (GPR data) |
| 03 | quarry_to_slabs | 1 | 0 | no | needs README+img+validate |
| 04 | scan_to_bench_engineer | 2 | 3 | yes | revalidate (scan data) |
| 05 | artist_pointing_machine | 2 | 0 | yes | needs img+validate |
| 07 | scan_ingest_full | 1 | 2 | yes | revalidate (scan data) |
| 08 | gpr_marble | 3 | 9 | yes | revalidate (GPR data) |
| 09 | uncertainty_safe_yield | 1 | 2 | yes | revalidate |
| 10 | pack2d | 1 | 2 | yes | flagship — revalidate |
| 11 | pack3d | 1 | 1 | yes | flagship — revalidate |
| 12 | trencadis | 1 | 1 | yes | revalidate |
| 13 | surface_mapping | 1 | 2 | yes | flagship — revalidate |
| 14 | kintsugi | 1 | 1 | yes | revalidate |
| 15 | statue_to_blocks | 1 | 9 | yes | revalidate |
| 16 | rubble_masonry | 1 | 1 | yes | revalidate |
| 17 | ashlar_masonry | 1 | 1 | yes | revalidate |
| 18 | pack_settle_bullet | 1 | 1 | yes | revalidate (Bullet) |
| 19 | rubble_evolved_fit_demo | 1 | 1 | yes | revalidate |
| 20 | rubble_multibin_demo | 1 | 1 | yes | revalidate |
| 21 | stereotomy_rubble_arch | 0 | 2 | yes | static — confirm |
| 22 | pendentive_vault_rubble | 0 | 1 | yes | static — confirm |
| 23 | quarry_to_slab | 0 | 3 | yes | static — confirm |
| 24 | guillotine_cut_sequence | 0 | 4 | yes | static — confirm |
| 25 | marble_gangsaw_cost | 0 | 8 | yes | static — confirm |
| 26 | loviisa_surface_fractures | 1 | 1 | yes | revalidate (data) |
| 27 | polygonal_masonry | 11 | 19 | yes | flagship — revalidate |
| 28 | hole_nest | 1 | 0 | no | flagship — needs README+img+validate |
| 29 | liveedge_floor | 1 | 3 | yes | revalidate |
| 30 | discontinuity_sets | 1 | 7 | yes | revalidate (cloud data) |
| 31 | discontinuity_ingest | 1 | 1 | yes | revalidate |
| 32 | scan_to_blocks | 1 | 2 | yes | revalidate (scan data) |
| 33 | gpr_marble_guillotine | 1 | 1 | yes | revalidate (GPR data) |
| 34 | gpr_marble_oblique | 1 | 1 | yes | revalidate (GPR data) |
| 35 | gpr_quarry_full_workflow | 2 | 5 | yes | flagship — revalidate |
| 36 | fractured_block_to_slabs | 1 | 1 | yes | revalidate |
| 37 | block_to_cladding_facade | 1 | 1 | yes | revalidate |
| 38 | surface_discretize_tiles | 1 | 1 | yes | revalidate |
| 39 | concave_nest | 1 | 1 | yes | revalidate |
| 40 | travertine_crosslithology | 3 | 11 | yes | revalidate (GPR data) |
| 41 | floor_tiling | 1 | 5 | yes | revalidate |
| 42 | wholeside_reassembly | 1 | 1 | yes | revalidate |
| 43 | nbo_dry_stone_wall | 1 | 1 | yes | revalidate (Bullet) |
| 44 | nbo_to_robot | 1 | 1 | yes | revalidate (Robots plugin) |
| 45 | cut_and_fill_excavation | 1 | 1 | yes | revalidate |
| 46 | kinematic_intensity_screen | 1 | 0 | yes | needs img+validate |
| 47 | fabrication_handoff | 1 | 0 | yes | needs img+validate |
| 48 | block_matching_3d | 1 | 1 | yes | revalidate |
| 49 | extraction_order_plan | 1 | 1 | yes | flagship — revalidate |
| 50 | castle_keep_ifc | 1 | 1 | yes | flagship — revalidate (Xbim) |
| — | vault_generation | 8 | 2 | yes | revalidate (multi-.gh) |
| 52 | mayan_corbel_vault (NEW, proposed) | 0 | 0 | no | build: see spec below |

### Proposed new example — 52_mayan_corbel_vault (block-mode vault, corbelled)

Libish's proposal 2026-07-07. A Mayan (corbelled) vault: triangular section
with a truncated top (capstone course) and two flat gable ends. Structurally it
is NOT a true arch — cantilevered courses stepping inward.

**MODELLING CORRECTION (Libish, 2026-07-07): Maya vaults are MORTARED, not dry-
stacked.** They are a battered facing of dressed stones over a thick core of
lime-mortar-and-rubble (a near-monolithic cementitious hearting). Stability in
reality comes from BOTH the massive wall/overburden mass counterweighting each
cantilever AND the mortar's tensile/shear bond. Frahan's stability model is
strictly **no-tension frictional** (Heyman/CRA): friction cone anchored at
`f_n >= 0`, tension penalized to zero, and there is NO cohesion / tensile-bond
parameter and NO surcharge term (verified in code 2026-07-07:
`FrictionConeBuilder`, `RbeQpFormulation` lowerBounds). So the checker as
shipped models the DRY-STONE idealisation only. Three honest ways to represent
the mortared vault:

- **Path A — dry-stone conservative bound (works today, no code).** Model the
  full massive walls + overburden and run the existing RBE. A STABLE verdict is
  a true lower bound: it stands from geometry + self-weight alone, mortar only
  adds margin. A steep/thin corbel that historically stood ONLY because of
  mortar will read UNSTABLE — honest ("would not stand un-mortared"), but not
  the historical mechanism. Frame the example as "stable without relying on
  mortar."
- **Path B — add interface cohesion (Mohr-Coulomb), the true mortared model.**
  Extend the cone to `|f_t| <= c*A + mu*f_n` with a tension cutoff
  `f_n >= -sigma_t*A` (bond tensile strength). This is the correct
  cementitious/lime-mortar model and unlocks a whole CLASS (mortared rubble
  walls, Roman concrete), not just this vault. It changes the validated RBE
  solver -> HITL-gated + re-validation + parameter calibration (lime-mortar
  bond ~0.1-0.5 MPa). Opus-tier. When it lands, THIS example gains a "with
  mortar" toggle: steeper corbels that fail dry-stone become feasible — a
  before/after teaching moment.
- **Path C — monolithic-core approximation (works today, geometry trick).**
  Represent the mortared core as a few large blocks so the interfaces sit in
  compression and the no-tension model does not spuriously fail. Approximates
  the mortar's binding via geometry.

Recommendation: **build on Path A now** (honest, ships without touching the
solver), and log Path B (cohesion) as a real feature. Do NOT present the
example as dry-stone corbelling if the README shows lime mortar — state the
model is a conservative no-tension bound and mortar adds margin.

Composition (Path A):
1. Solid: trapezoidal-section prism (base B, height H, top T, length L; e.g.
   B=3.0, T=0.6, H=2.4, L=4.0 m) with the FULL wall thickness (mass = the
   counterweight), two inclined faces, two flat gable ends.
2. `Staggered Block Decompose` (Course Height ~0.25, Block Length ~0.6,
   Stagger 0.5, Up=Z) -> running-bond cells; corbelling emerges as the section
   narrows with height.
3. Cells -> assembly -> `Masonry Stability (RBE)` (K=8 inscribed, ground course
   fixed) -> verdict + per-interface utilization.
4. Colour by course index (= build order), bake, capture.
5. README: corbel-vs-arch note (Maya precedent, and that it is MORTARED),
   the no-tension-conservative caveat, overhang-ratio rule of thumb, numeric
   sanity row.

Numeric-sanity acceptance: RBE verdict present + stable at the reference
geometry with full wall mass; steepening the corbel past the documented
threshold flips the verdict (shows the checker computes). README explicitly
states mortar is not modelled (conservative).


## Image-quality audit (2026-07-07) — FULL SWEEP, recapture register

Swept ALL 125 example images (8 parallel subagents against a fixed rubric +
main-loop verification of a cross-tag sample of 8 BADs — every agent verdict
held). No blank / 0-byte / low-res files (all >=720px). The problem is framing,
not code. **Totals: 65 GOOD, 31 BAD, 29 BORDERLINE.** Recapture the 31 BAD (and,
budget permitting, the 29 BORDERLINE) during the HITL pass. Backend note: the
reconstruction native stack is HEALTHY (DLLs bundled, all Phase H/I entry points
exported, `ReconstructionCleanup` wired at `ScanReconstructComponent.cs:315`) —
the bad reconstruction images are STALE captures, not broken output.

### Systemic root causes (fix once, applies broadly)
1. **Edge-on / low camera** hiding 3D structure — 04, 17, 34, 49, guell barrel.
2. **Deadspace**: tiny subject in a huge gray/white void — 42, 48, 30/32 clouds,
   15 detail, 27_06/09.
3. **Wireframe instead of shaded** (README says "shaded"), noisy fan-triangulation
   slivers — 16, 18, 21_arch, 22, 15_exploded.
4. **Thin diagonal-band demonstrator layout** — 15A/B/C, 19, 20.
5. **Loose/gappy result** underselling the feature — 10 (flagship), 43, 27_10,
   27_06_canvas.
6. **Pillowy/inflated block material** — 27_10_portal, 27_10_castle_keep.
Capture standard for every recapture: zoom-extents (subject fills frame, nothing
clipped), 3/4 (not edge-on) view, SHADED not wireframe, colour-by-metric, no
large dead areas.

### Special-handling flags (not just a reframe)
- **STALE (code already fixed):** `07_scan_to_mesh.png` — pre-cleanup slivers;
  re-run produces a clean mesh.
- **DEPRECATED viz:** `15B_rubble_match_scaled.png` — old AABB proxy with red
  spikes; drop it or re-render the current matcher.
- **CONTRADICTS README (fix image or text):** `09_uncertainty_3d` (README says
  "rendered 3D result", is a wireframe tangle) · `12_trencadis_result` (~90 shards
  vs README 28/28) · `27_07_voronoi_3d_pyref` (title 100-stone vs 50 cells) ·
  `39_concave_nest_hero` (5 vs README 6/6; input panel shows 3) · `44_nbo_to_robot`
  (README promises TCP triads + approach vectors + magenta base; image shows a bare
  pile).
- **ORPHANED (not referenced by any README — prune or wire in):** the six 27_06 /
  27_07_lambda / 27_09 / 27_10 experimental cards · `32_scan_to_blocks.png`
  (folder namesake, README uses 32_dfn_bench) · `guell_barrel_452_stable.jpg`
  (vault, README uses whole_shell_cra_barrel) · `03_gpr_radargram_AU.png`.

### BAD (31) — recapture required
| example | image | issue | fix |
|---|---|---|---|
| 04 | 04_lidar_las_cloud.png | camera/deadspace | 3/4 orbit, zoom-fit, colour by height |
| 04 | 04_packable_volume.png | camera | orbit off edge-on, Clean Scan Mesh to peel cap slivers |
| 07 | 07_scan_to_mesh.png | stale/artifacts | re-run (cleanup fixed), zoom-fit 3/4 |
| 09 | uncertainty_safe_yield_3d.png | mismatch | re-render shaded blocks by per-zone yield |
| 10 | 10_pack2d_result.png | loose (FLAGSHIP) | right-size the sheet, top-view, no floaters |
| 13 | 13_surface_trencadis.png | artifacts | shade, retriangulate fan cap, drop stray panel |
| 15 | 15_step2_blocks_exploded.png | artifacts (hero) | shaded solids, clean caps, tighter 3/4 |
| 15 | 15_step2_block_detail.png | deadspace | zoom block to ~65% frame, shade, label face vs cut |
| 15 | 15A_evolvedfit.png | deadspace | crop to a few pairs, 3/4, fill frame |
| 15 | 15B_multibin_pack.png | deadspace | zoom to a couple bins, 3/4 |
| 15 | 15B_rubble_match_scaled.png | deprecated | drop or re-render current matcher |
| 15 | 15C_multibin_pack.png | deadspace | zoom to representative bins, 3/4 |
| 17 | 17_ashlar_wall.png | deadspace | zoom to fill, front-3/4, remove stray origin stone |
| 21 | 21_stone_to_voussoir.png | artifacts | re-trim to one clean voussoir, centre before/after |
| 27 | 27_06_wall_generator_stability.png | deadspace/orphan | fix or drop (README holds card 06) |
| 27 | 27_06_doublecurved_stability.png | loose/orphan | opaque shade, zoom; or drop |
| 27 | 27_06_canvas_native.png | loose/orphan | close wall gaps, fill frame; or drop |
| 27 | 27_07_stone_match_lambda.png | deadspace/orphan | zoom the 3 panels, front-on; or drop |
| 27 | 27_09_ifc_export.png | deadspace/orphan | centre+zoom, colour by metric; or drop |
| 27 | 27_10_portal_closeup.png | loose/orphan | tighten joints, opaque stone; or drop |
| 30 | 30_discontinuity_sets.png | deadspace | centre cloud, use matplotlib stereonet inset |
| 30 | segmentation_tongjiang_AB.png | deadspace | zoom cluster, drop outlier specks |
| 30 | segmentation_tongjiang_XB.png | deadspace | zoom/centre, crop empty top |
| 32 | 32_scan_to_blocks.png | deadspace/orphan | recompose one scene; or drop (not in README) |
| 34 | marble_oblique_hero.jpg | deadspace | opaque size-colour, zoom, expose bed tilt |
| 42 | hero.png | deadspace | zoom to fill, dark fills, close input↔result gap |
| 43 | hero.jpg | loose | settle first, frame both straight+curved walls |
| 44 | hero.jpg | mismatch | zoom on triads + approach vectors + base, scale up |
| 48 | hero.png | deadspace | bring input+result together, zoom to fill |
| 49 | hero.jpg | cutoff (FLAGSHIP) | 3/4, all 8 blocks + order labels, nothing clipped |
| vault | figures/guell_barrel_452_stable.jpg | camera/orphan | 3/4 fill, colour by stability + cert; or drop |

### BORDERLINE (29) — improve if the HITL pass has budget
03_gpr_radargram_AU · 07_photogrammetry_ingest · 08_gpr_radargram_marble ·
08f_flat_guillotine · 11_pack3d_result · 12_trencadis_result(mismatch) ·
14_kintsugi_result · 16_rubble_wall(+stray stone) · 18_settle_bullet ·
19_evolved_fit · 20_multibin · 21_rubble_arch · 22_pendentive_vault ·
24_stage3_crossZ_allcuts · 25_0_bench_fractures · 29_classify_irregular ·
31_discontinuity_ingest · 27_04_wall_with_holes · 27_07_voronoi_3d_pyref(mismatch) ·
27_10_castle_keep · 27_10_castle_canvas · 30_segmentation_granite_dells ·
30_segmentation_rockalign · 35_quarry_fab_tail_hero · 35_quarry_fab_facemap ·
37_cladding_facade_hero(labels overlap) · 39_concave_nest_hero(mismatch) ·
40_gprsoft_validation · 40_andesite_pipeline_canvas.

### Cleanest folders (leave alone): 25, 40, 41, 23, 35(figures), 08(packs).

## After validation

1. Build an examples gallery page (thumbnails → per-example) for the docs site.
2. THEN the Rhino-free / kernel phase-out (local plan) with the validated set
   as the regression baseline — re-run this validation after each kernel swap.
