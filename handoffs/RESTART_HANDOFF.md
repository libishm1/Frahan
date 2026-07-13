# RESTART HANDOFF (2026-07-13)

On restart: `git pull` in D:\frahan-stonepack, then resume from OPEN ITEMS
below. Everything described here is committed + pushed to
github.com/libishm1/Frahan `main` unless marked WIP/uncommitted. Style:
short sentences, no em dashes.

## Where we are (one screen)

- Canonical repo: `D:\frahan-stonepack` (github libishm1/Frahan). Personal
  build/test clone: `D:\code_ws`. Re-discover against Frahan first.
- Branch `main`, HEAD `f51b8de`, pushed to origin. Builds clean.
- Deployed plugin (fresh, this session) at
  `%APPDATA%\Grasshopper\Libraries\`: `Frahan.StonePack.gha`,
  `Frahan.StonePack.dll`, `Frahan.StonePack.Core.dll` all carry the
  attention-mask fix + the GPR TryResolve fix. `.bak-pre-gpr` copies sit
  beside them (rename-aside deploy; safe to delete).
- `install/plugin/` bundle was refreshed with the mask fix (commit ca65b0e).
  It does NOT yet carry the GPR TryResolve fix. Refresh it before the next
  release (copy the 3 DLLs above into install/plugin/).
- Live Rhino: slot `aardvark` (port 10500) may still be up with Grasshopper
  loaded. If not, spawn a slot, then `_-Grasshopper` to load GH before any
  headless audit.

## What this session did (2026-07-13)

1. KINTSUGI LEARNED PORT - root-caused + FIXED. The PuzzleFusion++ denoiser
   attention mask was applied SOFT (bool mask added as +1 bias); diffusers
   0.21.4 on torch 2.x uses SDPA where a bool mask is HARD (False -> -inf).
   Fixed to hard mask in `MultiHeadAttention.cs` + `TorchSharpDenoiserPath.cs`
   (commit 39f3040 / a1e2b88). Transformer became bit-exact vs the Python
   oracle. Port now reassembles in-distribution (1887 samples 0.9deg, 1132
   2.7deg; was 126/78). Also switched GEGLU tanh-approx GELU to exact erf
   (real but negligible). Validated live in Rhino. Deterministic reassembler
   stays the PRIMARY path for synthetic/quarry cuts; learned Port is
   research-only, in-distribution.
2. TORCHSHARP VERSION CONFLICT - guarded. LunchBox ships TorchSharp 0.101.5
   (3-arg Tensor.to); Frahan needs 0.105.0 (4-arg). Rhino 8 CoreCLR default
   ALC is first-loader-wins by simple name, so it is load-order-dependent.
   The exact TorchSharp denoiser path works when Frahan's 0.105.0 loads
   first; else a version guard throws a clear message and the (now-correct)
   manual denoiser runs. See memory: project_hybrid_det_learned_verdict.
3. MERGED tested work + docs to main; pushed (a1e2b88 -> e4ba00a). Committed
   build-necessary uncommitted sources that main already referenced
   (MasonrySolverRegistry OSQP, Nbo LapjvNative/StoneSlotMatcher, OSQP plugin
   registration - a6c31fa/a193bcc). Audited all pre-existing uncommitted
   changes (install DLLs = repairs, kept; example .gh = archived to
   wip/example-canvas-2026-07-13; personal/native = left).
4. DEEP EXAMPLE AUDIT - 76 example .gh through a headless open+solve+scan.
   65 PASS (0 errors cold), 0 open-failures, 11 flagged. Wrote
   HANDOFF_2026-07-13_deep_example_audit_HITL.md + example_audit_2026-07-13.txt.
   Validated + committed examples 01/02/14 (1b4b287).
5. P1 GPR PRESET BUG - FIXED (commit f51b8de, this turn). GPR Fracture
   Extract only took named preset keys; a Construct GPR Preset wired as text
   arrives as "custom - 600 MHz (constructed) (v=0.1 m/ns, 600 MHz,
   eps_r=9)" -> "Unknown preset" on 5 examples. Added `GprPresets.TryResolve`
   (named-key first, else regex-parse v/freq/eps_r, derive missing via
   v=c/sqrt(eps_r)) and pointed the component at it. All 5 re-audited live:
   1 err -> 0. Named keys + garbage-rejection preserved.

## OPEN ITEMS (prioritized - the HITL public-presentable pass)

Master plan: HANDOFF_2026-07-13_deep_example_audit_HITL.md. Workflow: Libish
tunes each algorithm/canvas, assistant validates LIVE in Rhino (truth
criterion (c) visual), captures artifacts, documents before moving on.

- P1 GPR preset: DONE (f51b8de). No action.
- P2 ASYNC / DATA-GATED (cold-safe by design; red on cold open):
  `04_scan_to_bench.gh`, `11_granite_scan_to_bench.gh` (9 downstream
  need-data errors), `05 stone_carving_simulation_LIGHT.gh`. Fix: ship a
  small baked fixture + "set Run=true" canvas note, OR pre-bake the result so
  cold-open is clean. `11` has an in-progress canvas edit archived on
  `wip/example-canvas-2026-07-13` (UNVALIDATED, 28 KB) - validate then merge
  or discard.
- P3 DATA-DEPENDENT UPSTREAM: `32_scan_to_blocks.gh` (Joint Sets to DFN: no
  spacing>0 cold), `vault_generation/three_prong_staggered_cra_v002.gh`
  (shell mesh missing cold). Same remedy: bake a fixture or gate + document.
- P4 BY DESIGN (annotate, not a bug): `27_08_negative_cases.gh` - the red IS
  the intended output (validator rejects a bad chain). Label the canvas or
  route to a "Rejected (expected)" panel.
- VISUAL POLISH pass over the 65 that solve clean: colour-by-metric +
  captions where missing (feedback: examples-visual-and-transform-complete,
  gh-cards-self-presenting).
- STALE HERO IMAGES: 31 BAD + 29 BORDERLINE flagged for recapture. Recipe in
  HANDOFF_2026-07-07_stale_image_recapture.md (fresh slot, open_doc, scale
  x1000 for mm import, Arctic/Rendered, CaptureToBitmap 1280x860 -> jpg).
- KINTSUGI TAIL (research, not release-blocking): larger 4-8 frag samples
  flip = reference noise not a bug; to close, match reference noise +
  add P4 auto-agglomeration. Experiment branch
  `experiment/interlock-phase3` (places FB 00003 at 5%) is unmerged.

## Reusable audit harness (regression check after any plugin rebuild)

Headless, no MCP-GH needed. Spawn a slot, `_-Grasshopper` to load GH, then
`run_csharp` reflecting: find the `Grasshopper` assembly ->
`GH_DocumentIO.Open(path)` -> `Document` -> set `Enabled=true` ->
`NewSolution(true)` -> iterate `Objects`, call
`RuntimeMessages(GH_RuntimeMessageLevel.Error/Warning)`, tally per file.
GprPreset FIELDS are fields not properties (GetField, not GetProperty).
run_csharp `RunScript` returns void: WriteLine output, never `return <expr>`.
Last full run: example_audit_2026-07-13.txt.

## Deploy recipe (redeploy a rebuilt plugin to a locked Libraries folder)

Build Release: `dotnet build src/Frahan.StonePack.GH/Frahan.StonePack.GH.csproj -c Release`.
For each of Frahan.StonePack.Core.dll / .gha / .dll: rename the deployed
copy aside (`mv f f.bak`), then copy the fresh one from
`src/Frahan.StonePack.GH/bin/Release/net48/` into
`%APPDATA%\Grasshopper\Libraries\`. Rhino must be restarted (or a fresh slot
spawned) to pick up a changed .gha/.dll - the ALC caches the first load.

## Gotchas (do not relearn these)

- TorchSharp load-order (above). Never assume 0.105.0 won if LunchBox is
  installed.
- GH autosave + multi-million-vertex internalized mesh = stall/crash on any
  canvas edit. NEVER internalize a big scan; decimate (voxel ~100-200k) or
  reference externally. KNOWN_BUGS.md KB-1.
- mm-import 1000x scale trap: File3dm defaults mm; example imports shrink
  1000x. Scale x1000 for captures; watch fixtures.
- 1-step diffusion is degenerate: scale-conditioning vanishes at
  numInferenceSteps=1. Test the learned Port at >=3 steps.
- Never kill the user's own/adopted Rhino. HITL for pushes and >5-file
  commits. Ship NO Breaking Bad / FB data (GPL-3.0-derived, unlicensed).
  Ask before ~3M-token deep-research fan-outs. Save incremental .3dm builds,
  never overwrite.

## Key paths

- Canonical: D:\frahan-stonepack | Personal: D:\code_ws
- HITL master plan: handoffs/HANDOFF_2026-07-13_deep_example_audit_HITL.md
- Uncommitted audit: handoffs/HANDOFF_2026-07-13_uncommitted_audit.md
- Kintsugi parity note: wiki/research/kintsugi_port_parity.md
- Kintsugi benchmark writeup:
  D:\code_ws\outputs\2026-07-12\kintsugi_n3plus\research\BENCHMARK_learned_port.md
- GPR fix source: src/Frahan.StonePack.Core/Masonry/Quarry/Processing/GprPresets.cs
  (TryResolve) + src/Frahan.StonePack.GH/Quarry/GprFractureExtractComponent.cs
