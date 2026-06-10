# Examples audit punch-list (2026-06-07)

From the read-only audit sweep of all 26 examples (54-agent workflow, adversarially verified) plus the
migration TODO 27-30. Style: short sentences, no em dashes. Status as of local commit `f407127` (NOT yet
pushed; awaiting HITL validation of the example-27 PNGs).

## DONE in commit f407127 (local, awaiting push)
- **Migration 27 polygonal masonry**: 7 of 8 cards live-validated in Rhino 8 (01-05 spanning-chain walls
  3/7/10/8/20 stones, 07 3D Voronoi 50 cells, 08 negative cases). Result PNGs captured, README, cards/.
- **Rule-8 robustness fix** (`Wall.cs`): mutual above/below ambiguity now resolved by centroid height
  (acyclic) + graceful Kahn, instead of throwing. The bug that aborted card 06. Cards 01-05/07/08 unchanged.
- **Audit cleanups**: 10_pack2d README stale `.gh` filename; 24 orphan duplicate PNGs + stale `24_cuts.json`
  removed; 03_quarry_to_slabs noted SUPERSEDED by 23.
- **KB-8** logged (2D Voronoi arrangement under-extraction).

## REMAINING migrations (prep written in outputs/2026-06-07/migration_prep/)
- [ ] **28 monument packing** -> examples/28_monument_packing. Components live (Frahan Monument Inventory
  f7a16001 / Bench Monument Pack f7a16002 / Pack Monuments In Cell f7a16003). 3 cards, synthetic boxes
  internalised. Live: solve 3 canvases, hand-wire the inventory -> pack chain for 28b/28c, capture 3 PNGs.
- [ ] **29 edge matching v2** -> examples/29_edge_matching. EdgeMatch Solve d5f10001 live. 3 cards. CAVEAT:
  set MinSegmentLength=0.3 on the no-match card (default 8.0 mm filters all mm-scale segments). Solve + capture.
- [ ] **30 blockcutopt A4** -> examples/30_blockcutopt_validation. BlockCutOpt Solve f2d0bc02 live. A4 PASS
  (N=163). BLOCKER: live `.gitattributes` does NOT LFS-track `*.3dm`; the 19.4 MB + 44.7 MB result .3dm
  would commit as raw blobs. Decide LFS rule (HITL) before committing item 30.

## REMAINING audit fixes (by severity)

### HIGH (stub / not reproducible - need geometry regen, live Rhino)
- [ ] **01_quarry_to_wall**: `.3dm` is a cm-scale placeholder (2 objects, 16/18 result layers empty); no
  README/PNG/metrics. Regenerate the real quarry->fracture->ashlar-pack->wall result; add README+PNG+metrics.
- [ ] **03_gpr_fracture_granite**: no `.3dm`; README admits "pending live regeneration"; 3 conflicting data
  paths. Repath GPR input to colocated `gpr_data/`, run extraction, ship result `.3dm`+PNG+metrics.
- [ ] **07_scan_ingest_full**: not self-reproducible (inputs point at D:/code_ws/Data + a pending Drive
  upload); shipped mesh is raw cap-artifact. Colocate a tiny sample + ship the CLEANED `.3dm`+metrics, OR
  demote the "Scan to mesh: WORKS" claim to "raw reconstruction; cleaning not captured".
- [ ] **23_quarry_to_slab**: no `.3dm` AND no `.gh` (only README+metrics+3 PNGs). Generate geometry + an
  authoring `.gh` so it is openable/reproducible (match 08's completeness contract).
- [ ] **24_guillotine_cut_sequence**: no `.3dm`/`.gh` (orphans already removed). Generate the cut-sequence
  geometry + `.gh` (sibling 25 ships a `.3dm`).
- [ ] **02_masonry_assembly**: bare folder (only `.gh`+`.3dm`); no README/PNG/metrics + visual-check the blocks.

### MEDIUM (drift / missing canvas - mostly live re-solve)
- [ ] **12_trencadis**: `.3dm` has 100 shards on one "Default" layer but README says 28 + named layers.
  Re-solve the `.gh` live, recapture, rewrite README to the real count + grout (5 mm vs 0.40 vs 0.02 drift).
- [ ] **18_pack_settle_bullet**: README height 1.95 m vs metrics 3.06 m; PNG shows a precarious tower with no
  visible container. Open live; re-run the settle or honestly document the artifact + fix the number.
- [ ] **21 / 22 (rubble arch / vault)**: no `.gh` canvas. Add an authoring `.gh` (the new Arch Voussoirs /
  Pendentive Vault Voussoirs components feed the cells).
- [ ] **26_loviisa**: `.gh` persists a stale ABSOLUTE path (`KB11_2022.shp`) not the colocated
  `shp_data/KB11_tulkinta.shp`; remove the stray far-away `card_bench` mesh. Re-point + resave the `.gh`.
- [ ] **06 (2D Voronoi, KB-8)**: fix `Pslg.FromSegments` face extraction for ridge networks (intersection
  split + endpoint snap + half-edge traversal) OR add a 2D-cells input mirroring the 3D component; then
  ship card 06 in example 27. Until then 3D Voronoi (07) is the Voronoi demo.

### LOW
- [ ] **13_surface_mapping**: the `.gh` is segmentation-only; the headline "176-shard trencadis mosaic" is
  baked-only (deferred to example 12). Note this in the README; add metrics.json.
- [ ] **10_pack2d**: filename fixed; the README body still describes V506 while the banner says FreeNestX.
  Inspect the `.gh` and reconcile the packer identity.

## CLEAN (no action) - 13 examples
04, 05, 08, 09, 11, 14, 15, 16, 17, 19, 20, 22, 25.

## Notes
- Every "regen" item needs the live Rhino slot (truth criterion c); the headless harness cannot init
  `rhcommon_c` here. Deploy is file-copy with Rhino CLOSED.
- Commits to Frahan main need explicit push authorization (per task). Batch per logical unit; keep main clean.

## Self-presentation audit (2026-06-10) — canvas-native rule
New standing rule (memory `feedback_gh_cards_self_presenting`): a card's FINAL form must come from canvas
components (Custom Preview / Gradient-by-metric / Panels), not external bake scripts — acceptance: reopening
the .gh cold reproduces the capture.

Headless audit of all 36 example .gh files: only the masonry-27 set now complies.
- **DONE (2026-06-10):** 27_01..27_05, 27_06 (+ CRA verdict panels, exact-joint Assembly wire, mortar joints),
  27_07_stone_match_lambda (Move/Swatch/Traffic-Gradient layer), 27_07_voronoi_3d — Custom Preview + beige
  swatch wired to the Stones output, raw previews hidden. 27_08 intentionally panel-only (negative cases).
- **TODO (rest of library, fold into regen passes):** every other example has prev=0 (geometry presentation
  relies on default red GH previews; many have report Panels only): 01, 02 (0 panels too), 03_gpr (x2),
  03_quarry_to_slabs, 04 (x2), 05 (x2), 07, 08_gpr (x3), 09, 10, 11, 12, 13, 14 (0 panels), 15C (0), 16 (0),
  17 (0), 18 (0), 19 (0), 20 (0), 26. Retrofit recipe = the headless batch in this session's transcript
  (CreateInstance Custom Preview + Colour Swatch, AddSource to the main geometry output, hide raw preview,
  SaveQuiet).
