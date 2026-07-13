# Handoff: uncommitted-changes audit + kintsugi mask fix + main merge (2026-07-13)

Style: short sentences, no em dashes. Audits the pre-session uncommitted working
tree, records what was committed vs archived vs left, and the merge to main.

## Headline

The learned Kintsugi Port (PuzzleFusion++) was ROOT-CAUSED and FIXED this
session. The denoiser attention applied the block-diagonal self_mask as a SOFT
+1 bias, but torch 2.x diffusers uses SDPA whose bool mask is HARD (-inf). Fixed
in both denoiser paths (commit 39f3040). Transformer is now bit-exact to the
reference (residual 0.0000, was 6.13). End-to-end the Port reassembles
in-distribution Breaking Bad fragments: 1887 (2-frag) 0.9 deg, 1132 (3-frag)
2.7 deg (were 126/78). Larger 4-8 frag samples still flip on some seeds
(noise/convergence, not a bug; the reference itself scores <1.0 single-pass on
hard samples). Full detail: D:\code_ws\outputs\2026-07-12\kintsugi_n3plus\
research\BENCHMARK_learned_port.md.

## Merge to main (done, pushed)

main advanced from 9249280 to ca65b0e on origin (github libishm1/Frahan):
- feat/resolution-escalation (62 commits: parity ladder + escalation + mask fix)
  fast-forwarded into main. This ALSO brought the 3 branches already contained
  in it: fix/cloud-icp-async, fix/examples-arch-coherence,
  fix/kintsugi-fracture-generator (no-ops, already merged).
- docs/meeting-link (Calendly link) + docs/mayan-vault-mortar (handoffs + risk
  register + example-52 spec) merged (docs-only, clean).
- NOT MERGED: experiment/interlock-phase3. Its own commit says "NOT SHIPPABLE
  YET - regresses 00002", and its interlock work is superseded by the later
  FacetMatch code already in main. Left on its branch for reference.

## Uncommitted-changes audit + disposition

COMMITTED to main (repairs / build-necessary):
- MasonrySolverRegistry.cs (+44): adds UseOsqpIfAvailable() + SolverDescription().
  The committed callers (BuildOrderStabilityStreamComponent,
  MasonryStabilityRbeComponent, Cra.Worker Program.cs) already referenced these,
  so the committed tree did NOT build - it only compiled with this local edit
  present. This is a real build repair. (commit a6c31fa)
- StoneSlotMatcher.cs (276L) + LapjvNative.cs (79L): untracked but referenced by
  the committed StoneSlotMatchComponent (GH); a clean clone failed to build
  without them. (commit a193bcc)
- StonePackPlugin.OnLoad: EnsureDefaultSolver -> UseOsqpIfAvailable (registers
  the OSQP-first ladder at load). (commit a193bcc)
- install/plugin/ managed DLLs refreshed from the fixed Release build so the
  deploy bundle carries the mask fix: Frahan.Kintsugi.Port.dll,
  Frahan.StonePack.{Core.dll,dll,gha}, Frahan.EdgeMatching.Core.dll. Native DLLs
  unchanged. (commit ca65b0e)

ARCHIVED off main (unvalidated / pending approval), branch
wip/example-canvas-2026-07-13 (b3c48a4):
- examples 01, 02, 04 (04_scan_to_bench + 11_granite_scan_to_bench), and
  14_kintsugi_synthetic .gh canvas edits. Reasons: examples 01/02/04 canvas
  states await user approval; .gh are binary and could not be opened/validated
  in Rhino this session (slot infra was down - a modal dialog blocked startup).
  Do NOT merge until each is validated live (open cold -> solve -> capture) and
  approved.

LEFT uncommitted / flagged (out of the stated scope or personal/native):
- .vscode/launch.json: contains a personal machine path
  (C:\Program Files\Rhino 8\...). Personal dev config; not committed.
- native/osqp_shim/ + native/lapjv_shim/ + native/tna_solver/ (untracked C++
  source + CMake): the NATIVE side of the OSQP/LAPJV/TNA solvers. The committed
  managed code P/Invokes their built DLLs (frahan_osqp.dll etc., already in
  install/plugin/). RECOMMEND committing the native SOURCE for reproducibility
  AFTER a C++ build validation (could not validate the native toolchain here).
  The native /build/ subdirs are build output - gitignore them.
- CLAUDE.md, .claude/, .vscode/extensions.json, lean/lake-manifest.json,
  src/graphify-out/, tests/.../last_run.log: local config / generated artifacts.
  Recommend .gitignore for graphify-out + last_run.log + native build dirs.

## Known caveats

- Rhino slot infra was DOWN all session (a modal dialog blocks rh-mcp bind).
  So install/ was refreshed from validated SOURCE, but a full deploy.ps1 + a
  live in-Rhino load check is still recommended before trusting the deploy.
- TorchSharp exact denoiser is BLOCKED in Rhino by a version conflict: LunchBox
  (net7 ML pkg 2025.5.5.0) loads TorchSharp 0.101.5 first, shadowing Frahan's
  0.105.0 in CoreCLR's default ALC (first-loader-wins). The manual C# denoiser
  (now mask-fixed, faithful) is the in-Rhino path until that is resolved
  (disable LunchBox ML, or ALC-isolate, or align versions).

## Remaining tasks (next session)

1. Lift the larger-sample tail: feed the reference's noise, and add the P4
   auto-agglomeration multi-pass (the reference's own path for hard cases).
2. Resolve the LunchBox TorchSharp conflict for in-Rhino exact-path use.
3. Validate + approve the archived example canvases (wip/example-canvas-...),
   then merge.
4. Commit the native solver source (osqp_shim/lapjv_shim/tna_solver) after a
   C++ build check; add .gitignore entries for build output.
5. Deploy + in-Rhino load check of the refreshed install/ bundle.
