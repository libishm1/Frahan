# HANDOFF_LATEST — Autonomous Nightshift (live, kept current)

Date: 2026-06-04. Branch: `docs/frahan-autonomous-nightshift`. Agent: claude_cloud.
Overwritten continuously so a crash never loses more than the current big task.

## NEWEST STATE — read "2026-06-05h" first

### 2026-06-05h (KNOWN BUGS registry; autosave/large-mesh rule made concrete; paper Future Work + PDF)
- **NEW canonical KNOWN_BUGS.md** in this nightshift folder. KB-1 (RESOLVED-by-rule): saving a .gh that
  internalises a multi-million-vertex mesh stalls/crashes the canvas on any edit (e.g. inserting a List
  Item) because GH autosave rewrites the whole 52 MB file on every change, ONLY once the file is named
  (unnamed file works); it collides with the async Remesh/Close-Holes tasks = the "two parallel
  processes." Confirmed by David Rutten (McNeel forum). NOT a component-code bug. KB-2: Mesh.Reduce on
  multi-million meshes blows the 300 s MCP cap (use voxel-cluster). KB-3: masonry RBE sign-buggy Build
  (OPEN, W4).
- **STANDING RULE (concrete):** NEVER internalise a multi-million-vertex scan in a .gh. Decimate
  (voxel-cluster ~100-200k verts) or reference externally (.3dm / Data Output->Input). Disable GH
  "backup on new connection". Fix delivered: `stone_carving_simulation_LIGHT.gh` (4.06 MB, scan 2.2M ->
  146,437 verts) opens 0.2 s vs 12 s. Carving Stages must stay synchronous + cached; never reorder its inputs.
- **Paper Future Work (ix) + PDF/md** (origin 1ab8d8b): small-scale stone-mason quarrying as the
  principal application (block >1 m all axes & >=3 m length ~3 m^3; slab >=3 m x 1 m at 1/1.5/2 inch;
  bespoke monuments; manual extraction sequence: overburden->clean outcrop->expose two faces->mark
  bore-holes->extract primary block->gang-saw; drive marking from scans to avoid crack zones; deepen to
  ~20 m second layer; co-locate scan positions with ground team + masons). Field thumb-rules =
  PRISMA_BLOCK_DIMENSIONS.md §10. PDF recompiled (tectonic 0.16.9 @ %TEMP%\tectonic_dl\tectonic.exe).
- See CHECKPOINT_10.md. NEXT unchanged: packing benchmark W2 (--packbench; PACKER_API_INVENTORY.md ready).

### 2026-06-05g (Carving Stages regression FIXED + restored; packing-benchmark enabler ready)
- **Carving Stages restored** (origin 469b612 chain: 2180513 debug+HILT, deddcc3 restore, 469b612 report).
  Libish flagged it reverted to a crashing version. Forensics: **098d041 (05-29) was the GOOD version**
  (synchronous + input-hash CACHE + Run gate -> no recompute on unrelated edits; all 9 inputs; RTree
  block-MESH clamp; input order Target/Stages/MaxOffset/Finish/FeatureBoost/Mode/FrontDir/Block/Run).
  **dfff18d "v2" (05-30) was the REGRESSION** (dropped cache+Run gate -> every edit recomputes ~11s on a
  2.2M scan = frozen; replaced block clamp with AABB; REORDERED inputs -> broke files saved under 098d041,
  incl. the 05 pointing-machine .gh). RESTORED 098d041 design + kept v2's good bits ([DesignApplication]/
  [Algorithm] attrs + Radial fold-fix = smoothed normals + offset caps). Rebuilt + redeployed
  (Grasshopper/Libraries gha 19:18). VERIFIED live (slot aardvark): describe shows 9 inputs ending in Run,
  "CACHED + Run-gated"; sphere smoke test = clean compute (Count=4), full re-solve with unchanged inputs
  hits cache in **13.2 ms** (no recompute). Original 05.gh now re-wires correctly (input order matches).
  RTree clamp is O(verts) so first compute on the raw 2.2M is slow -> decimate-first still applies.
  See [[feedback_gh_carving_stages_reorder_bug]] (corrected). HILT_REPORT + _forensics/ committed.
- **MCP lesson:** Mesh.Reduce on a 2.2M mesh exceeds the 300s MCP cap even ONCE and blocks the slot
  (forced 2 Rhino kills/restarts). Never decimate multi-million-vert meshes inline; transforms + bakes are
  fine, only Reduce is pathological. Rhino restart recipe (kill -> Start-Process /runscript=_-MCPStart ->
  background PS AppActivate+SendKeys ENTER -> list_slots adopts) works; standalone grasshopper MCP listener
  refused, use the g1_* in-Rhino bridge (g1_connect src "0" for a Param; g1_connect_many JSON rejected,
  use single g1_connect).
- **Packing benchmark (W2/W3) ENABLER READY:** harness verified booting Rhino.Inside headless (pack3d
  30/30 @ 23.7% fill matches Python golden). Full packer API inventory written to
  `outputs/2026-06-05/keep_or_cut/PACKER_API_INVENTORY.md` (every engine's entry signature + result fields
  + the Validator helpers to reuse). NEXT: add a `--packbench` mode (one method per family) emitting the
  uniform metrics row, run it, write PACKING_BENCHMARK.md, then W3 Fracture-Block-Pack decision.
- Backlog tasks #26-#33 (keep-or-cut W1-W7) + #35 (build engineer/artist/geologist example .gh myself,
  Libish has no photogrammetry .gh; artist seed = the fixed pointing-machine file).

### 2026-06-05f (master-spine audit shipped; keep-or-cut evaluation planned; CHECKPOINT_9)
- **Audit shipped + pushed** (origin f32a175): audit/AUDIT_00..07 + _INDEX under
  `outputs/2026-06-04/gpr_extraction/deep_fracture_review/audit/`. 8-family SLM+ROSES math+code,
  8-layer architecture map (math -> Core engine -> GH component), global redundancy/drop list. Verified
  in source: (1) FractureBlockPack mode 5 did NOT supersede Core RecoveryCascade (two recovery engines,
  never call each other, RecoveryCascade is test-only); (2) MasonryStabilityRbeComponent calls
  sign-buggy RbeQpFormulation.Build; (3) recon Auto should be Advancing-Front-first. Packing 17->~6.
- **Photogrammetry recon resolved** (origin 3e9b7db): merged dense Tongjiang tiles -> merged Poisson
  near-watertight, merged AF one component; sparse panorama was the problem. PHOTOGRAMMETRY_WORKFLOW.md
  is ingest-only reuse; still waits on Libish's own photogrammetry .gh.
- **NEW DIRECTIVE (Libish 2026-06-05) -> `outputs/2026-06-05/keep_or_cut/EXECUTION_PLAN.md`** (tasks
  #26-#33): HIDE not delete; MEASURE before deciding. Reports parity (W1); packing-sprawl benchmark of
  EVERY packer + keep/hide (W2); Fracture Block Pack keep-on-canvas + head-to-head decision (W3);
  masonry RBE sign fix APPROVED but test-if-deliberate-first + HILT (W4); move misfiled GH/Masonry
  components (W5); build out unbuilt components or hide-with-reason (W6); hide measured losers +
  KEEP_OR_CUT_SUMMARY (W7). Every kept algo needs HILT (3dm+gh+png) + numbers report.
- **VEHICLE**: `tools/Frahan.StonePack.Harness` (Rhino.Inside out-of-process; --pack3d/--nfp/--rubble),
  `tools/Frahan.GprBench`, `tests/Frahan.StonePack.Tests` (968 PASS at CP8). dotnet 9.0.313. Phase 1
  confirms baseline + whether Rhino.Inside boots headless here (else use the live Rhino MCP slot).
- **PAPER**: MASTER_PAPER.pdf current (2.03 MB, Table A1 + manufacturability section). No LaTeX
  toolchain installed locally (only matters if the paper is edited again).
- NEXT: show Libish the PDF, then Phase 1 build/smoke, then W2/W3 benchmark.

### 2026-06-05e (Phase 2 block-packing evolution + Phase 3 master paper; committed, origin 3c93552)
- **Kim 2025 (block-packing TREE) equations derived** (Eq 1-9 + the arg-min/arg-max Eq.9 erratum;
  TreePackForest implements Eq.8 + arg max); Mosch 2010 Palmstrom block-size context extracted.
  All in BLOCK_PACKING_EVOLUTION.md. Papers in HILT/ (Kim, Mosch full text; gitignored).
- **Fracture Block Pack evolved + beats baseline** (live grid3, 4 watertight slabs, 0 errors):
  mode 0 naive grid 24.0% -> mode 1 best-of(orient x phase) 35.3% (+47%) -> mode 2 COMBINED
  multi-size 49.3% (default). Uncertainty-safe clearance curve: 49.3% geo -> 34.6/28.8/24.0/16.4%
  at Cl 0.05/0.10/0.15/0.20 (monotone). HITL: evolved_/combined_blockpack_{result.3dm,capture.png}.
- **MASTER_PAPER.tex/.pdf** (deep_fracture_review/): results-heavy engineering paper, full pipeline
  (GPR->fracture->3D->packing), LaTeX equations, images in Methods+Results (own figures), strong
  PRISMA/SLM/ROSES lit review + research-gaps. **4-agent review** (mean 75.3; geophys 83/rock-mech 90
  accept, stats 69, adversarial 59) -> revised per all CRITICAL/MAJOR (REVIEW_RESPONSE.md): novelty
  reframed, single-site Limitations, sigma-component table, effective conf ~10%, clearance curve,
  time-zero/lambda4 disclosure. Compiled (tectonic, 6 figs).
- **QUEUED NEXT (reminder due to Libish):** quarry->monument packing + LiDAR/photogrammetry. The
  working scan->mesh-bench GH file is `Template-General/outputs/2026-05-27/hitl_cards/scan_ingest_cloud/
  cards/11_granite_scan_to_bench1.gh` (Read LAS Cloud -> Estimate Normals[CGAL] -> Scan Reconstruct
  [Poisson/Geogram] -> Bench From Mesh; Granite Dells TLS ~5M pts; ASYNC Run toggles default false).
  Graph extracted read-only to `outputs/2026-06-04/next_phase_lidar_monument/11_granite_scan_to_bench1.graph.xml`.
  RUN via MCP with toggle gates + ~1 min timeouts per stage (never solve all at once); natives must be
  deployed; suggest better workflows; verify sibling cards 01-11. Confirm w/ Libish if a separate
  photogrammetry GH script exists. See [[project_queued_monument_lidar]].



### 2026-06-05d (detection calibration per stone + QUEUED next task)
- Per-stone GPR detection calibration (verified-only research workflow): eta0 granite 0.80 [measured],
  limestone 0.90, sandstone 0.80, marble 0.75, travertine 0.75, andesite 0.50, tuff 0.38 [extrapolated].
  Encoded in `GprDetectionCalibration.cs` (provenanced); table + sources in
  `deep_fracture_review/DETECTION_CALIBRATION.md`. Corrected: sealed factor 0.52 -> 0.10-0.20;
  A_min now DEPTH-AWARE (Fresnel fraction (lambda/4)*depth/2, reproduces Molron 80% for 1-10 m^2).
  Rebuilt + redeployed + HITL re-verified live (slot aardvark): C# bit-matches prototype (granite
  open sub-horizontal 9 m -> 74-79% across 160-750 MHz; sealed 7.4%; sub-vertical 5.6%); marble
  headless solve eff conf 10%. equations.pdf/docx recompiled.
- **QUEUED NEXT TASK (Libish, 2026-06-05):** quarry -> monument packing + the LiDAR/photogrammetry
  workflow. Libish made a GRASSHOPPER SCRIPT to refer to for the photogrammetry workflow -- ASK him
  for it before starting. See [[project_next_session_priority]]. Also: the master paper must include a
  RESEARCH-GAPS section (literature gaps + gaps this workflow closed).
- IN FLIGHT (this mega-task, remaining): Phase 2 block-packing evolution (Mosch 3D-BlockExpert vs
  Frahan TreePackForest + GreedyMeshHeightmapPacker -> evolve combined mesh-bench packer, beat
  baseline, HITL captures); Phase 3 master paper (GPR->fracture->3D->block-packing, LaTeX eqs, full
  PRISMA/SLM/ROSES in ROSES format + research gaps, multi-agent review).

## NEWEST STATE — read "2026-06-05c" first, then b, then "2026-06-05 SESSION DONE"

### 2026-06-05c SESSION DONE (deep extraction + MATH EVOLUTION + live HITL; committed)
- 7 GPR/quarry papers full-text in `HILT/` (gitignored, copyright): Xie2021, Yarahmadi2018,
  Ulker2009, Dorn2012, Elkarmoty2017, Porsani2006, Mosch2010 + Molron2020. Extraction tooling
  (_extract_text.py / _extract_figures.py) + derived layer `deep_fracture_review/extractions/
  DEEP_EXTRACTION.md`. CORRECTION: Elkarmoty 2017 DOES propose a generic "some cm" cut offset +
  kriging (was mis-reported as none); refined novelty = Frahan scales clearance to MEASURED sigma.
- **MATH EVOLVED** (CHECKPOINT_GPR_MATH_CODE.md -> EVOLVED_MATH.md): FractureUncertainty.cs +
  time-zero term (Xie), + DETECTION rung (Molron/Dorn: P_det by dip/openness/area) + EffectiveConf;
  GprFractureSurface3D +3 inputs/+2 outputs. STONE x FREQ PRESETS PRESERVED (GprPresets untouched;
  eta0 the only stone knob, exposed). Built + deployed + LIVE HITL on slot armadillo (run_csharp
  bit-matches prototype; headless solve 0 errors; EFFECTIVE conf 10.1%); evolved_hitl_result.3dm +
  evolved_hitl_capture.png.
- Tools: tectonic 0.16.9 + pandoc 3.10 in gitignored `_tools/` -> equations.pdf + equations.docx.
- Corpus now 38 (Molron added); PRISMA figures regenerated.



### 2026-06-05b SESSION DONE + PUSHED (origin through commit 3d066df)
- **Uncertainty-safe TOGGLE** on `Fracture Block Pack` (6493a94): bool `Uncertainty Safe` (US,
  appended at input index 7 so saved .gh cards stay valid). FALSE=geometric (24%), TRUE=clearance
  = fracture sigma (6.5%). Built + deployed to the ONE Frahan .gha (live next Rhino load).
- **HITL artifacts** (6493a94, LFS): hitl_gh/cards/uncertainty_safe_yield_{result.3dm,3d.png,wire.png}.
- **NEW Research Framework V4** (`reference/RESEARCH_FRAMEWORK_V4_prisma_slm_roses.md`, 3d066df):
  three-tier PRISMA(statistics)+SLM(algorithm math+code)+ROSES(interdisciplinary). Evolves V3,
  reinstates PRISMA in the statistics role (fixes V3 flaw F7). Per Libish's directive.
- **Deep-fracture review** (`outputs/2026-06-04/gpr_extraction/deep_fracture_review/`, 3d066df):
  first V4 application. 37 Crossref-verified sources, 4 strands; PRISMA flow + stats figures
  (make_prisma_stats.py); 6 SLM cards binding the tolerance-ladder math to real Frahan code; ROSES
  synthesis across geophysics/geostatistics/rock-mech/economics/decision. Finding: clearance=measured-
  sigma is NOVEL (no corpus optimizer does it); 24%->6.5% is principled; velocity sounding = the VoI lever.
- QUEUED next from the review: strand E (detection uncertainty, missed clay/sub-vertical fractures);
  exhaustive registered re-run of strand C (economics cluster is thinnest, 5/37); marble synthetic-
  recovery study; quantitative VoI (one CMP sounding vs recovered yield).

## NEWEST STATE — read the "2026-06-05 SESSION DONE" block below first
origin `docs/frahan-autonomous-nightshift` through commit **6305b57** (10+ commits pushed:
live HITL, 5-method 3D surfaces, C# kriging, tolerance ladder, GPR Fracture Surfaces 3D +
Fracture Block Pack components, watertight slabs, end-to-end uncertainty-safe yield). Deploy
collapsed to ONE Frahan .gha; PachydermGH disabled. Older base below (commit 6d71117, the
original GPR mapping ship; full suite 968 PASS).

### What shipped (GPR fracture mapping, end to end)
- **Core** (`frahan_stonepack/src/Frahan.StonePack.Core/Masonry/Quarry/Processing/`):
  `RadargramProcessor`, `FractureExtractor`, `Fft` (radix-2 + **Bluestein** arbitrary-length),
  `GprPresets`, `FractureTracer`, `FractureSurface`. Plus `Earthworks/`: `TinPeelFilter`,
  `TinMerge`, `BedrockSurface`. All Rhino-free, dependency-light.
- **GH** (`Frahan.StonePack.GH/Quarry/`): `GprFractureExtractComponent` (preset + velocity +
  migrate + depth-equalize + quantile + continuity + **dip-gate** + **trace-mode** toggles;
  outputs Picks / Depths / Confidence / Energy Mesh / Bedrock Depth / **Fracture Id** /
  **Fracture Lines** / Report), `CleanScanMeshComponent`, `GprBedrockSurfaceComponent`.
- **Bench**: `tools/Frahan.GprBench` (deterministic timing + energy checksum).
- **Verified facts**: C# == validated Python (granite AU 1472, marble 168/118/179 picks);
  dt 0.4464 ns; 2.34x faster (25.7 -> 11 s), bit-identical; dip-aware recovery 3/3 sub-45,
  steep-60 rejected, 0.65 m RMS.

### Resume commands
```
cd Template-General/outputs/2026-05-01/frahan_stonepack
dotnet build src/Frahan.StonePack.Core/Frahan.StonePack.Core.csproj -c Release   # + GH + tests + tools/Frahan.GprBench
dotnet run  -c Release --project tools/Frahan.GprBench                            # granite bench
# tests: FRAHAN_SKIP_NATIVE=1 dotnet run -c Release --project tests/Frahan.StonePack.Tests
#   (slow Kintsugi BB-inference test is near the end; GPR/earthworks/tracer/surface tests are LAST)
# python figures: cd ../../../../outputs/2026-06-04/gpr_extraction && python gpr_hitl_validation.py
```

### LIVE HITL DONE + PUSHED (2026-06-04, slot armadillo) — report: hitl_gh/HITL_LIVE_VALIDATION.md
- Reinstalled v0.7.0 .gha (running Rhino had stale v0.5.5, no GPR comp); after restart GPR
  GUID registered (206->210 comps). 5 .gh cards EMITTED (fixed builder: _card_lib path +
  marble file glob; old hardcoded g2_LA020004 did not exist).
- NUMERIC HITL PASS, live GH == documented, 0 errors: g1 168, g2 118, g3 179, AU 1472
  (dt 0.4464 ns), VE 1485. Verified 3 ways (json/card-wiring/fixture-consistency).
- Energy meshes now VERTEX-COLOURED (jet+99.5pct == figure panel c); re-baked 5 .3dm;
  verified on-screen by pixel read-back. StonePackAssemblyInfo.Version now assembly-derived.
- Commits b871519 + a00e801 PUSHED to origin (LFS).

### 2026-06-05 SESSION DONE + PUSHED (origin through commit 6305b57; verified live, slot aardvark)
A. energy vertex colours -> DONE. B. true 3D marble fracture surfaces -> DONE (5 methods:
   TPS/loft/CGAL-advancing-front/median/KRIGING; adaptive depth-clustering = 100% picks; TPS
   clamp fix; smoothed 3D curves; make_marble_3d_surfaces.py). C. dimension-block bin-packing
   -> DONE (Fracture Block Pack: each fracture-bounded slab=bin; tree coarse + irregular fit).
- **C# Kriging.cs** (Core; Cholesky + variogram + MLE; managed sigma_interp, NO Python/MathNet).
- **FractureUncertainty.cs** tolerance ladder (sigma_recon+interp+mesh; confidence=erf(T/sigma*sqrt2)).
- **3 GH components live**: GPR Fracture Extract (+Depth Sigma / Confidence within T outputs);
  GPR Fracture Surfaces 3D (A7E0B0F2-...03); Fracture Block Pack (A7E0B0F3-...04).
- **END-TO-END uncertainty-safe yield**: sigma 0.065/0.132/0.278m; clearing fractures by sigma
  drops grid3 yield 24%->6.5%. Doc END_TO_END_PIPELINE.md; canvas hitl_gh/cards/uncertainty_safe_yield.gh.
- Watertight slab fix (make_fracture_blocks.slab_mesh closed manifold -> reliable yield).
- DEPLOY: collapsed Frahan to ONE .gha (removed MeshHeightmap dup -> fixed "DLL conflict in
  Frahan"); disabled PachydermGH.gha (missing Pachyderm_Acoustic 2.5.0.0; was the only loading
  error, NOT Frahan). Automated-restart recipe in [[feedback_mcp_rhino_grasshopper]]; NEVER force
  ComponentServer.LoadExternalFiles on a loaded GH (caused 3149 ID conflicts -> "lost all plugins").
- Native frahan_cgal/geogram: in-process call CRASHES Rhino (FP-trap); use the worker only.

### STILL QUEUED (lower priority)
manual fracture intervention GH inputs; marble synthetic-recovery test; AU/VE registration;
true geogram screened-Poisson needs the native shim deployed beside the .gha (worker path only for now).

### DEFERRED (queued, implementation-ready) — gpr_extraction/TODOS_NEXT.md
- **MANUAL fracture intervention** (Libish asked): Core `FractureTracer.FractureLineFromSection`
  DONE + compiles. TODO = add GH inputs `Manual Lines` (Curve list drawn on the Energy Mesh;
  x=pt.X, depth=-pt.Z, sample via Curve.DivideByCount(span/dx)) + `Manual Only` (bool) to
  `GprFractureExtractComponent`; merge into Fracture Lines / Picks / Fracture Id; report "N auto + M manual".
- **Marble synthetic recovery test** (quantify FP of the 0.65 m continuity calibration; no marble
  ground truth otherwise). Mirror `gpr_granite_accuracy.py` (marble eps_r~9, 600 MHz, shorter fractures).
- **AU/VE registration** via `grimsel_gpr/2d_seismic_coords.csv` for a true per-reflector cross-tunnel
  check (current r=0.069 = different rock volumes, un-registered — honest negative).
- **True 3D marble surfaces**: `FractureSurface.LoftAcrossLines` over the LA/TA grid lines.

### Decisions on record
- Geometry backend = **geogram first** (then CGAL, then RhinoCommon). DAPComputationalGeometry
  evaluated, NOT adopted (duplicates geogram/CGAL + RhinoCommon; package gate, no win).
- Marble continuity calibrated by SPAN only (27 traces ~0.65 m); the 0.985 energy bar was NOT
  lowered (span = principled to marble's short fractures; lowering the quantile = unverifiable clutter).
- `.gsf` (Romania travertine/andesite) blocked (proprietary Geoscanners + embedded GPS); convert
  via GPRSoft -> SEG-Y -> `GprSegYReader`. Presets lit-default until then.
- Raw GPR datasets gitignored (large/licensed); download DOIs in `wiki/index/data_assets_inventory.md`
  (Grimsel 10.3929/ethz-b-000420930 CC-BY-4.0; Bondua 10.17632/w26n6nftxs.3 CC-BY-NC-ND; +B1/B4/B5).

## Resume recipe (fresh agent after a crash)
1. `cd D:\code_ws`; read `AGENTS.md` in full (mandatory).
2. Read `CHECKPOINT_8.md` (newest) + this file's top block.
3. For GPR: continue from `gpr_extraction/TODOS_NEXT.md`. For prior nightshift: PLAN.md + CHECKPOINT_1..7.
4. Honor HITL gates (no push w/o ask — Libish authorized THIS push; <=5-file commits otherwise).

## PRIOR nightshift (carried in; detail in CHECKPOINT_1..7 + cutfill_excavation/PLAN.md)
- W16 OverburdenVolume + `Overburden To Rock Face` GH; alpha-shape CGAL fix (REGULARIZED +
  REGULAR-only) deployed; ScanReconstruct recenter+cleanup; validation-pack A1-A5; SLM spines +
  ROSES; quarry->monument staged demo (`validation_pack/stage_c_quarry_to_monument.py`, awaits Rhino).
- V3 SLM+ROSES program (`algo_review_v3/`); architecture locked
  (`wiki/specs/architectural_decisions_2026-05-31.md`: 5-stage pipeline, 31 primitives + 19
  monoliths, 4 interdisciplinary compositions); ~95+ GH components across 13 families.
- Still-open pre-GPR HITL items: visual-validate the alpha-shape fix + Overburden To Rock Face;
  run the quarry->monument demo; wire GeometryNumerics into the 7 sites (RECENTER_LAYER_DESIGN.md).

## Last updated
2026-06-05 — uncertainty + dimension-block-yield session complete + pushed (origin through 6305b57);
3 GH components live (GPR Fracture Extract uncertainty / GPR Fracture Surfaces 3D / Fracture Block
Pack); end-to-end uncertainty-safe yield demonstrated; deploy deduped + Pachyderm disabled.
