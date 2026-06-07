# Migration-gap TODO: validated code_ws work not yet in Frahan

Two repos are live until the user confirms migration is complete: `code_ws`
(Agent-orchestration-main, dev) and `Frahan` (frahan-stonepack, collaborators). This is the audited list
of validated GH workflows and datasets in code_ws (Template-General) that are NOT yet represented in
Frahan. Source: background audit 2026-06-07 (3 parallel inventory agents + diff). Coverage is ~70-75%
complete by topic; the binary/plugin side and examples 01-26 are migrated. Style: short sentences, no em
dashes. Each item: copy the validated artifacts, build/verify the example through GH, capture PNGs,
colocate data, write the README, commit to Frahan main.

## HIGH - validated GH demonstrators with NO Frahan example by topic

- [ ] **27 Polygonal masonry (incl. Voronoi 2D/3D)** -> `examples/27_polygonal_masonry`
  Source: `Template-General/outputs/2026-05-21/polygonal_masonry_hitl_cards/cards/`. 8 cards, each
  .gh + .3dm + .md, 6 png-backed (01 three-band wall, 02 twelve-angled, 03 chains wall, 04 wall with
  holes, 05 wavy perlin, 06 voronoi 2d, 07 voronoi 3d) + 08 negative cases. Strongest-validated batch.
  Voronoi 3D is distinct from the existing 3D packing examples. Skip .3dmbak files.

- [ ] **28 Monument packing** -> `examples/28_monument_packing`
  Source: `Template-General/outputs/2026-05-22/hitl_cards/monument_packing/cards/`. 3 cards
  (inventory / pack-in-block / pack-on-bench), each .gh + .3dm + .md. This is the QUEUED monument
  workflow (memory project_queued_monument_lidar); the Tamil Nadu monument application. Distinct from
  the generic pack3d example 11.

- [ ] **29 Edge matching v2** -> `examples/29_edge_matching`
  Source: `Template-General/outputs/2026-05-30/hitl_cards/edge_matching_v2/cards/`. 3 cards (clean /
  two-pieces-no-match / 3-piece chain), each .gh + .3dm + .md (latest iterated build). Migrate the v2
  batch ONLY; the 2026-05-22 and 2026-05-25 edge cards are superseded. Frahan.EdgeMatching.Core has no
  user-facing example today.

- [ ] **30 BlockCutOpt validation (A4)** -> `examples/30_blockcutopt_validation`
  Source: `Template-General/outputs/2026-06-04/hitl_cards/validation_pack/cards/A4_blockcutopt.{gh,md}`
  + `a4_bench.3dm` + `a4_blocks_in_fractures.3dm` (~19 MB, LFS) + `tn_heavy_result.3dm`. A4 is the only
  validation-pack card recorded PASS (memory project_validation_pack_units_a4; N=163, deterministic).
  A1/A2/A3/A5 had the 1000x unit bug, migrate A4 only. BlockCutOpt exists only as Core source in Frahan.

## MEDIUM

- [ ] **Carving stages v2 variant sweep** -> `examples/05_artist_pointing_machine/carving_stages_v2`
  Source: `Template-General/outputs/2026-05-30/hitl_cards/carving_stages_v2/cards/`. 5 cards (flat-top,
  radial-no-fold, radial-with-block, push-in, feature-boost). Base carving stage already in example 05;
  this adds the validated variant sweep. Avoid the older 2026-05-29 05/05b carving cards (superseded).

- [ ] **ETH1100 dry-stone sample subset** -> `data/eth1100` (+ `_SOURCE.md`)
  Source: `D:\code_ws\Data\eth1100` (Zenodo 10038881, CC-BY-4.0). Examples 16_rubble and 17_ashlar were
  BUILT from it but no eth1100 data ships, so they are not self-reproducible. Ship only the meshes 16/17
  consume + Stone_Shape_Properties.csv (not the 3 GB corpus), per the large-data policy.

- [ ] **Stanford scans (bunny min)** -> `data/stanford_scans` (+ `_SOURCE.md`)
  Source: `D:\code_ws\Data\stanford_scans` (Stanford 3DScanRep). Backs validated scan-ingest cards
  05/06/07. Ship stanford_bunny (~4.7 MB tar) at minimum; dragon/buddha/armadillo/drill optional.

- [ ] **Tongjiang quarry clouds** -> `data/tongjiang` (downsampled PLY + `_SOURCE.md`)
  Source: `D:\code_ws\Data\tongjiang` (563 MB, Zenodo 10.5281/zenodo.15614501, CC-BY-4.0). Drives the
  validated scan_ingest_cloud card 01. Ship one downsampled PLY (LFS) like the 04 las_data pattern; pair
  with a scan-ingest-cloud example if one is migrated.

## LOW

- [ ] **Multi-format GPR (tu1208, MALA .rd3 + GSSI .dzt)** -> `data/gpr_formats`
  Source: `D:\code_ws\Data\gpr\tu1208` (Zenodo 1211173, CC-BY-4.0). Format-coverage only; Frahan already
  ships grimsel (03) + bondua marble (08). Ship a 2-3 file subset only if a multi-format GPR example is
  added.

- [ ] **GeoCrack corpus (reference-only)** -> `data/DATA_ACCESS.md` row + Drive link
  Source: `D:\code_ws\Data\geocrack` (4.9 GB, MIT). Do NOT commit; reference by DOI + Drive per the
  3-2-1 large-data policy. No current GH card consumes it.

- [ ] **CI (net-new, not a migration)** -> `.github/workflows/build.yml`
  Neither repo has CI. Fresh authoring: dotnet build of src/ + headless harness smoke test. Out of
  strict migration scope; listed so it is not dropped.

## How to migrate one item (recipe)
1. Copy the validated `.gh` + `.3dm` + `.md` into the target `examples/NN_*/` (skip `.3dmbak`).
2. Colocate the data the card needs (small -> in folder; large -> subset + `_SOURCE.md` + DATA_ACCESS).
3. Open on slot armadillo, set the file inputs to the colocated paths, solve through GH, capture PNGs.
4. Write the example README (design problem + named precedent + numeric tolerance + dataset + wiki
   cross-ref, per feedback_hitl_cards_design_grounded).
5. Commit to Frahan `main` (keep it clean), LFS for binaries.
