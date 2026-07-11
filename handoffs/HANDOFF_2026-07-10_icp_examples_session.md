# Handoff — ICP root-cause fix + examples 01-10 coherence (2026-07-10)

Fresh-session onboarding. Read `AGENTS.md` first (truth criterion (c) visual
validation in Rhino; HITL gates mandatory; hard limits in Part II). Style:
short sentences, no em dashes.

## State snapshot

- **Branches (all pushed to `libishm1/Frahan`, none merged):**
  - `fix/cloud-icp-async` — the ICP fix chain (base).
  - `fix/examples-arch-coherence` @ `14e9bcf` — stacks on ICP branch; examples
    01-10 work. **This is the current working branch.**
  - `docs/mayan-vault-mortar` (6 commits, **local-only, NOT pushed**) — Mayan
    example 52 spec, risks M6/M7/M8, image-quality audit, stale-image recapture
    handoff.
  - `docs/meeting-link` (pushed) — Calendly link in README + wiki. PR pending.
- **Deployed `.gha`:** build of `3143341` (byte-verified into
  `%APPDATA%/Grasshopper/Libraries`). Deploy flow: ALL Rhino closed (incl. MCP
  slots) → copy fresh `Frahan.StonePack.gha` + `Core.dll` from
  `src/Frahan.StonePack.GH/bin/Release/net48/` into `install/plugin/` → run
  `pwsh -File install/deploy.ps1` → `cmp` byte-verify.
- **Private repo** (`libishm1/Agent-orchestration-main`): branches
  `docs/structural-stone-reference`, `docs/cam-readiness-private`,
  `docs/memory-dbms-formalization` pushed. `planning/` stays gitignored
  (master plan + Rhino map + CAM handoff local; CAM handoff also force-pushed
  to private repo). NEVER push these to the public Frahan repo.

## What happened (compressed)

### 1. Cloud ICP crash — root cause found and fixed (fix/cloud-icp-async)
The crash chain, each layer verified live in Rhino MCP slots:
1. Sync solve froze UI → converted to `AsyncScanComponent` (Run gate).
2. Million-point GH goo lists froze/OOM'd → `Sg`/`Tg` geometry inputs (Mesh /
   PointCloud / points pooled, LIST access) + `TryReadRunOnly` light pass (no
   input recapture on the completion pass).
3. **ROOT CAUSE: native `frahan_geogram_kdtree_query` ACCESS-VIOLATES on a
   healthy 1000-pt/100-query call** (reproduced isolated; native AV kills
   Rhino, uncatchable). Replaced with pure-managed `GridNN` hash grid. The
   native kd entry must NOT be called until the shim is rebuilt/debugged.
4. Units: metre-tuned absolute defaults (voxel scales `{0.5,0.1,0.02}`,
   tolerance `1e-4`) broke mm models → AUTO scales (extent/60,/200,/600) +
   AUTO tolerance (extent*1e-6) + 400k point budget/scale + hoisted buffers.
5. Data diagnostics: extent-ratio unit-mismatch warning ("rigid ICP cannot fix
   scale"), centroid pre-alignment when X0 unwired, non-convergence warning
   now reports RMS + remedies.
- MCP-validated: 5k/50k/200k/1M pts, m + mm, all converge, 1M quarry pair
  8.6 s. Headless harness: scratchpad `IcpDiagTest.cs` (csc net48 pattern —
  note: Framework csc is C#5 only, no string interpolation;
  `Transform.Rotation` factory needs native rhcommon, build matrices by hand).
- **Voxel Downsample** got the same treatment: `G` geometry input, `P`
  optional, native `Cloud` output `C`, empty-input warns orange, `Read()`
  override re-asserting Optional.

### 2. Examples 01-10 architectural coherence (fix/examples-arch-coherence)
- **02_masonry_assembly COMPLETED** (was mute: no Ids, no Blocks source):
  Ids → Masonry Block → assembly, lowest block fixed, RBE (Run-gated) +
  Verdict/Report panels, build-order Gradient+Preview. Viewport-verified.
- **01 + 03_slabs REBUILT** from scratch; **GPR five** (03_gpr x2, 08 x3):
  stale `Preset` param (3ede854e…) replaced with `Construct GPR Preset`
  (A7E0B0F6…) — this removed the **~6-minute first-open freeze** (silent Yak
  package lookup for unknown GUIDs; measured 348 s).
- **07**: lag-free Cloud chain (loaders' `C` → ICP `Sg`/`Tg`), normals →
  reconstruct added, toggles false.
- **Presentation kit on all ten**: README run-instruction panels, sliders,
  Custom Preview + colouring (gradient on 02; swatches elsewhere).
- **04**: PC → VoxelDown.G rewire; normals-carrying chain (VoxelDown.C →
  EstNormals.C → Reconstruct.C) for Poisson; missing Run Normals toggle added.

### 3. Risk register (branch docs/mayan-vault-mortar, unpushed)
- M6 no-tension-only masonry (Mayan mortar finding; Path A example / Path B
  cohesion extension in private dev-map #7).
- M7 async stale-result race (no input fingerprint in AsyncScanComponent).
- M8 exception laundering (FrahanComponentBase blanket catch).
- Plus: full 125-image audit (65 GOOD / 31 BAD / 29 BORDERLINE) + standalone
  recapture handoff `HANDOFF_2026-07-07_stale_image_recapture.md`.

## Field-defect patterns (CHECK THESE on every remaining example, 11-50)
1. **Saved-TRUE Run toggles** — auto-start heavy compute on open. Ship false.
2. **Stale/unknown GUID chunks** — ~6-min silent Yak-lookup freeze per GUID on
   first open. Offline detector: scratchpad `GhAudit.exe` (GH_IO chunk dump;
   compare GUIDs vs docs/components/components.json + resolve via
   ComponentServer.EmitObjectProxy in a slot).
3. **Cloud-typed sources wired into Point-list inputs** — GH cannot convert
   PointCloud→Point. Remap: VoxelDown P→G, EstNormals P→C, ICP S/T→Sg/Tg,
   Reconstruct P→C.
4. **Serialized-required params override new Optional registration** —
   "Input parameter X failed to collect data" AND silent no-solve. Code fix =
   `Read()` override re-asserting Optional (CloudIcp + VoxelDown have it; any
   component whose params change needs it).
5. **Async components with unwired Run** — idle forever, downstream starves
   with misleading errors (04's Poisson case).
6. **Starving required inputs** — check `SourceCount==0 && VolatileDataCount==0
   && !Optional` per Frahan component (how 02's gaps were found).

## MCP slot lessons (operational)
- `GH_DocumentIO.Save()` leaves a blocking prompt headless — **use
  `SaveQuiet(path)` only**.
- run_csharp HTTP timeout is 300 s but the slot keeps executing; use
  write-through logs to scratchpad and read them via Bash.
- One heavy document call at a time; never solve docs with unknown toggle
  states (set `doc.Enabled=false` for audits).
- User-started Rhinos don't always advertise to the router; work in a spawned
  slot on the saved file and have Libish reopen (never save over from a stale
  canvas).

## Resume points (in order)
1. **Examples HITL review with Libish** — one by one per
   `HANDOFF_2026-07-07_examples_validation.md` (tracking table = resume
   point). Examples 01-10 are now solid; capture images per the recapture
   standard (zoom-extents, 3/4, shaded, colour-by-metric); Libish approves
   each. Recapture the 31 BAD images per
   `HANDOFF_2026-07-07_stale_image_recapture.md` during each turn.
2. **Sweep examples 11-50** for the six field-defect patterns above (offline
   GhAudit first, slot fixes after).
3. **PR chain** when Libish says go: `fix/cloud-icp-async` →
   `fix/examples-arch-coherence` → main; push + PR `docs/mayan-vault-mortar`.
4. **Follow-ups:** native geogram kdtree shim rebuild/debug (AV — add a risk
   row when PRing); ICP perf (comparator sort → keyed/quickselect, parallel
   grid queries; downsample 1.4 s @1M); M7 fingerprint fix + M8 exception
   policy (code); example 52 Mayan (Path A spec in validation handoff);
   compressive-strength check (structural-stone gap, private
   reference/HANDOFF_2026-07-09_structural_stone.md in Agent-orchestration);
   Track-1 geology; CAM calibration (PRIVATE planning/ docs).

## Hard rules (unchanged)
- Examples validation is HITL one-by-one; Libish reviews every example+image.
- Save incremental; never overwrite working artifacts without approval.
- planning/ + CAM + structural-stone strategy stay off the public repo.
- Tags/DOIs immutable. Ask before deep-research fan-outs.
