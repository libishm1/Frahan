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
with a truncated top (capstone course) and two flat gable ends — structurally
NOT a true arch: cantilevered staggered courses, stability = COM-over-support
/ overturning, the exact regime the RBE/COM checkers validate. Composes from
EXISTING components, no new code:

1. Solid: trapezoidal-section prism (base width B, height H, top width T,
   length L; e.g. B=3.0, T=0.6, H=2.4, L=4.0 m) — two inclined faces at the
   classic ~steep corbel angle, two flat ends. Native GH box/loft or a small
   internalized Brep->Mesh.
2. `Staggered Block Decompose` (Form=the solid mesh, Course Height ~0.25,
   Block Length ~0.6, Stagger 0.5, Up=Z) -> running-bond cells; corbelling
   emerges automatically because the section narrows with height.
3. Cell meshes -> assembly -> `Masonry Stability (RBE)` (K=8 inscribed, ground
   course fixed) -> verdict + per-interface utilization; expect stable at
   modest corbel angle, UNSTABLE when the user steepens the overhang — the
   didactic knob.
4. Colour by course index (= build order output), bake, capture.
5. README: corbel-vs-arch mechanics note (Maya/tholos precedent), the
   overhang-ratio rule of thumb, numeric sanity row (max tension <= gate,
   utilization < 1, per-course cantilever ratio).

Numeric-sanity acceptance: RBE verdict present + stable at the reference
geometry; flipping corbel angle past the documented threshold flips the
verdict (shows the checker actually computes).


## After validation

1. Build an examples gallery page (thumbnails → per-example) for the docs site.
2. THEN the Rhino-free / kernel phase-out (local plan) with the validated set
   as the regression baseline — re-run this validation after each kernel swap.
