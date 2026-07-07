# Handoff — stale / bad example-image recapture (2026-07-07)

## Why this exists

A full sweep of all **125** example images (8 parallel subagents + main-loop
verification of a cross-tag sample of 8 — every verdict held) found:

- **65 GOOD · 31 BAD · 29 BORDERLINE**

The code is healthy. The reconstruction native stack was verified this session:
`frahan_cgal.dll` + `frahan_geogram.dll` are bundled, every Phase H/I entry point
(alpha-shape, advancing-front, Poisson, estimate-normals, voxel, kd-tree) is
exported in the shipped DLLs, and `ReconstructionCleanup` is wired at
`ScanReconstructComponent.cs:315`. So the liability is **framing and staleness of
the captures, not broken output.** This handoff is the recapture worklist.

This is a subset of `HANDOFF_2026-07-07_examples_validation.md` — the recaptures
happen during each example's HITL turn there. This file is the standalone
image-only view so the recapture work can be picked up cold.

## Ground rules (binding — read before touching anything)

- Read `AGENTS.md` first. Truth criterion (c): an example is validated by VISUAL
  correctness in Rhino. HITL §6 gates are mandatory stops.
- **HITL, one at a time.** For each recapture: produce the new image, `Read` it so
  it renders, present it to Libish with the fix applied, and only replace the old
  image on Libish's OK. Do not run ahead or batch-replace.
- **Do NOT autonomously prune orphans or edit READMEs to resolve mismatches.**
  Those are flagged below as decisions for Libish — surface them, don't act.
- Save incremental: capture the new image alongside the old (e.g.
  `<name>_v011.jpg`); swap the referenced name only after approval. Never
  overwrite the committed image before Libish signs off.

## The recapture recipe (per image)

1. Deploy the current **v0.1.1 `.gha`** with ALL Rhino closed incl. MCP slots
   (they file-lock the `.gha`): `pwsh -File install/deploy.ps1`. Skip if the
   deployed build is already v0.1.1.
2. Spawn a Rhino MCP slot (v8); force the GH plugin to load; open the example
   `.gh`; `NewSolution(true)`; bake the result layers.
3. **Frame to the capture standard** (below): set a perspective 3/4 camera, Zoom
   Extents (`ZEA` / zoom-to-selected), display mode **Shaded** (not Wireframe),
   colour geometry by its metric.
4. `ViewCaptureToFile` → downscaled JPG into the example folder.
5. `Read` the JPG to confirm it clears the bar (truth criterion), then present to
   Libish.

## Capture standard (the bar every recapture must clear)

- **Zoom-extents** — subject fills the frame, nothing clipped at an edge.
- **3/4 perspective**, never edge-on, so 3D structure reads.
- **Shaded**, not wireframe; no noisy fan-triangulation overlay.
- **Colour-by-metric** (install order / yield / stability / bin), per the house rule.
- **No large dead gray/white areas**; no stray geometry dumped at the world origin.

## Six systemic root causes (fixing the setup fixes many at once)

1. Edge-on / low camera hiding 3D structure — 04, 17, 34, 49, guell barrel.
2. Deadspace (tiny subject in a void) — 42, 48, 30/32 clouds, 15 detail, 27_06/09.
3. Wireframe where the README says shaded (fan-triangulation slivers) — 16, 18,
   21_arch, 22, 15_exploded.
4. Thin diagonal-band demonstrator layout — 15A/B/C, 19, 20.
5. Loose / gappy result underselling the feature — 10 (flagship), 43, 27_10.
6. Pillowy / inflated block material — 27_10_portal, 27_10_castle_keep.

## Special handling — NOT just a reframe

- **STALE (code already fixed, just re-run):** `07_scan_to_mesh.png` — pre-cleanup
  slivers; re-running example 07 today yields a clean mesh.
- **DEPRECATED viz (drop or re-render current):** `15B_rubble_match_scaled.png` —
  old AABB proxy with red spikes.
- **CONTRADICTS README — fix the image OR the text (ask Libish which):**
  `09_uncertainty_3d` (README "rendered 3D result", is a wireframe tangle) ·
  `12_trencadis_result` (~90 shards vs README 28/28) · `27_07_voronoi_3d_pyref`
  (title 100-stone vs 50 cells) · `39_concave_nest_hero` (5 vs README 6/6; input
  panel shows 3) · `44_nbo_to_robot` (README promises TCP triads + approach
  vectors + magenta base; image shows a bare pile).
- **ORPHANED — not referenced by any README; prune or wire in (ask Libish):** the
  six `27_06` / `27_07_lambda` / `27_09` / `27_10_portal` experimental cards ·
  `32_scan_to_blocks.png` (folder namesake; README uses `32_dfn_bench.png`) ·
  `vault_generation/figures/guell_barrel_452_stable.jpg` (README uses
  `whole_shell_cra_barrel.jpg`) · `03_gpr_radargram_AU.png`.

## BAD recapture worklist (31) — grouped by example

Order suggestion: flagships first (10, 49), then the worst folder (15), then by
number. Mark each row done only after Libish approves the replacement.

| ex | image | issue | fix / action |
|----|-------|-------|--------------|
| 04 | 04_lidar_las_cloud.png | camera/deadspace | 3/4 orbit, zoom-fit, colour points by height |
| 04 | 04_packable_volume.png | camera | orbit off edge-on; Clean Scan Mesh to peel cap slivers |
| 07 | 07_scan_to_mesh.png | **STALE** | re-run (cleanup already wired), zoom-fit 3/4 shaded |
| 09 | uncertainty_safe_yield_3d.png | **MISMATCH** | re-render shaded blocks by per-zone yield, or fix README caption |
| 10 | 10_pack2d_result.png | loose (**FLAGSHIP**) | right-size the sheet so parts seat; top-view, no floaters |
| 13 | 13_surface_trencadis.png | artifacts | shade (wireframe off), retriangulate/hide fan cap, drop stray 2D panel |
| 15 | 15_step2_blocks_exploded.png | artifacts (hero) | shaded solids, clean caps, tighter 3/4; interior-blue/boundary-red clear |
| 15 | 15_step2_block_detail.png | deadspace | zoom block to ~65% of frame, shade, label real face vs cut |
| 15 | 15A_evolvedfit.png | deadspace | crop to a few representative pairs, 3/4, fill frame |
| 15 | 15B_multibin_pack.png | deadspace | zoom to a couple bins, 3/4 |
| 15 | 15B_rubble_match_scaled.png | **DEPRECATED** | drop, or re-render with the current matcher |
| 15 | 15C_multibin_pack.png | deadspace | zoom to representative bins, 3/4 |
| 17 | 17_ashlar_wall.png | deadspace | zoom to fill, front-3/4, remove the stray stone at the origin |
| 21 | 21_stone_to_voussoir.png | artifacts | re-trim to one clean closed voussoir; centre before/after |
| 27 | 27_06_wall_generator_stability.png | deadspace/**ORPHAN** | fix (zoom, colour by order) or drop — card 06 held back per README |
| 27 | 27_06_doublecurved_stability.png | loose/**ORPHAN** | opaque shade, zoom; or drop |
| 27 | 27_06_canvas_native.png | loose/**ORPHAN** | close wall gaps, fill frame; or drop |
| 27 | 27_07_stone_match_lambda.png | deadspace/**ORPHAN** | zoom the 3 panels, front-on; or drop |
| 27 | 27_09_ifc_export.png | deadspace/**ORPHAN** | centre + zoom, colour by metric; or drop |
| 27 | 27_10_portal_closeup.png | loose/**ORPHAN** | tighten joints, opaque stone material; or drop |
| 30 | 30_discontinuity_sets.png | deadspace | centre the cloud to fill; inset the matplotlib stereonet, not a wireframe one |
| 30 | segmentation_tongjiang_AB.png | deadspace | zoom the dense cluster, drop stray outlier specks |
| 30 | segmentation_tongjiang_XB.png | deadspace | zoom/centre the cluster, crop the empty top |
| 32 | 32_scan_to_blocks.png | deadspace/**ORPHAN** | recompose to one scene; or drop (not in README) |
| 34 | marble_oblique_hero.jpg | deadspace | opaque size-coloured material, zoom, angle to expose the bed tilt |
| 42 | hero.png | deadspace | zoom to fill, dark fills on light bg, close the input↔result gap |
| 43 | hero.jpg | loose | settle (Bullet) first so courses seat; frame both straight + curved walls |
| 44 | hero.jpg | **MISMATCH** | zoom on annotated stones with TCP triads + approach vectors + base scaled legible |
| 48 | hero.png | deadspace | bring input pair + reassembled result together, zoom to fill |
| 49 | hero.jpg | cutoff (**FLAGSHIP**) | 3/4, all 8 blocks + order labels in frame, nothing clipped |
| vault | figures/guell_barrel_452_stable.jpg | camera/**ORPHAN** | 3/4 fill + colour by stability + cert label; or drop (README uses whole_shell_cra_barrel) |

## BORDERLINE (29) — improve only if the pass has budget

03_gpr_radargram_AU · 07_photogrammetry_ingest · 08_gpr_radargram_marble ·
08f_flat_guillotine · 11_pack3d_result · 12_trencadis_result (mismatch) ·
14_kintsugi_result · 16_rubble_wall (+stray stone) · 18_settle_bullet ·
19_evolved_fit · 20_multibin · 21_rubble_arch · 22_pendentive_vault ·
24_stage3_crossZ_allcuts · 25_0_bench_fractures · 29_classify_irregular ·
31_discontinuity_ingest · 27_04_wall_with_holes · 27_07_voronoi_3d_pyref (mismatch) ·
27_10_castle_keep · 27_10_castle_canvas · 30_segmentation_granite_dells ·
30_segmentation_rockalign · 35_quarry_fab_tail_hero · 35_quarry_fab_facemap ·
37_cladding_facade_hero (labels overlap) · 39_concave_nest_hero (mismatch) ·
40_gprsoft_validation · 40_andesite_pipeline_canvas.

## Cleanest folders — leave alone

25, 40, 41, 23, 35 (figures), 08 (packs) — mostly matplotlib figures / well-framed
3/4 packs, all GOOD.

## Acceptance per image

New capture clears the capture standard **AND** Libish approves it **AND**, where
flagged, the README mismatch is reconciled or the prune decision is made. Only
then swap the referenced image name and mark the row done. Restart-safe: this
table is the resume point — start at the first unchecked row.
