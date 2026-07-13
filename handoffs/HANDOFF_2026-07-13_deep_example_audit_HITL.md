# Handoff: deep example audit + HITL tuning for public release (2026-07-13)

Style: short sentences, no em dashes. Goal: take the 76 example canvases from
"opens + resolves" to "public-presentable" through a human-in-the-loop pass where
Libish tunes each algorithm and the assistant validates it live in Rhino.

## Baseline audit (2026-07-13, automated)

Ran every example through a headless open + solve + runtime-message scan (Rhino 8,
current mask-fixed plugin deployed). Harness is reusable (see bottom). Result over
all 76 `.gh`:

- PASS (0 errors on cold open + solve): 65 / 76
- WITH ERRORS: 11
- FAILED TO OPEN / EXCEPTION: 0

Zero open-failures means every component resolves against the deployed plugin
(no missing/renamed components). The full per-example log is at
scratchpad/example_audit.txt (regenerate with the harness below).

## The 11 with errors, categorized + prioritized

P1 - FIXED 2026-07-13 (commit f51b8de, pushed to main). Chose option (a):
added GprPresets.TryResolve (named-key first, else parse the constructed
string) and pointed the component at it. All 5 examples re-audited live in
Rhino 8: 1 error -> 0. Named keys + garbage-rejection preserved. Original
report below for provenance.

P1 - REAL BUG: GPR preset mismatch (5 examples, one fix)
- 03_gpr_fracture_granite/granite_160_AU.gh, .../granite_160_VE.gh
- 08_gpr_marble/marble_600_grid1.gh, grid2.gh, grid3.gh
- Error: `GPR Fracture Extract: Unknown preset 'custom - 600 MHz (constructed)
  (v=0.1 m/ns, 600 MHz, eps_r=9)'. Have: marble_600, granite_160, ...`.
- Diagnosis: the saved canvases pass a CUSTOM preset string that the component's
  preset lookup does not recognize; it only accepts the named presets. The
  physical parameters ARE embedded in the string (v=0.1 m/ns, 600 MHz, eps_r=9).
- Fix options (Libish to choose):
  (a) TUNE THE COMPONENT (recommended): make `GPR Fracture Extract` parse a
      `custom - ... (v=..., ... MHz, eps_r=...)` string into an ad-hoc preset, so
      any user's custom preset works. One code change unblocks all 5 + future.
  (b) RE-AUTHOR the 5 canvases to a named preset (08 -> `marble_600`,
      03 -> `granite_160`). Faster but leaves custom presets broken for users.

P2 - ASYNC / DATA-GATED (cold-safe by design, need Run + fixture to fully solve)
- 04_scan_to_bench_engineer/04_scan_to_bench.gh (Bench From Mesh: needs mesh)
- 04_scan_to_bench_engineer/11_granite_scan_to_bench.gh (9 downstream errors, all
  "need points/mesh/cloud" - the async scan loader is Run-gated)
- 05_artist_pointing_machine/stone_carving_simulation_LIGHT.gh (Carving Stages /
  Enlarge / Fit In Block: needs the target sculpture mesh)
- These are correct cold behavior (heavy loaders do not auto-run). To validate:
  wire/confirm the fixture, toggle Run=true, poll the async task, then re-scan
  for errors. Not a defect - but a public user opening them cold sees red, so
  either (i) ship a tiny baked fixture + a "set Run=true" note on the canvas, or
  (ii) pre-bake the result so cold-open is clean.

P3 - DATA-DEPENDENT UPSTREAM (needs the upstream to produce data)
- 32_scan_to_blocks/32_scan_to_blocks.gh (Joint Sets to DFN: no usable joint sets
  spacing>0 - upstream discontinuity extraction produced nothing cold)
- vault_generation/three_prong_staggered_cra_v002.gh (Vault Shell CRA: shell mesh
  missing/too small - upstream form-find not run)
- Same remedy as P2: bake a fixture or gate + document.

P4 - BY DESIGN (not a bug, verify + annotate)
- 27_polygonal_masonry/27_08_negative_cases.gh (Polygonal Masonry Sequence: chain
  0 not monotone). This is the NEGATIVE-CASE example: the error IS the intended
  output (the validator correctly rejects a bad chain). Annotate the canvas so a
  reader knows the red is expected, or route it to a "Rejected (expected)" panel.

## Acceptance criteria for "public-presentable" (per example)

An example ships when ALL hold (cold, from a clean clone):
1. Opens + solves with 0 unexpected errors. Negative-case red must be labelled.
2. Cold-open reproduces the intended result without manual wiring. Heavy/async
   examples ship a small baked fixture OR a pre-baked result + a one-line
   "toggle Run" instruction on the canvas.
3. Self-presenting canvas: layout, colour, and captions come from canvas
   components (Custom Preview + gradient by metric, Scribble titles, grouped
   stages), NOT from a bake script. Acceptance = reopen cold reproduces the look.
   See feedback: examples-visual-and-transform-complete, gh-cards-self-presenting.
4. The algorithm is TUNED for a sensible default: real fixture data, parameters
   that produce a clean result, units correct (File3dm mm; watch the 1000x
   fixture-scale trap), tolerances per the tolerance budget.
5. Ships the artifact set: metric PNG(s) + a live-Rhino capture + the .3dm + the
   .gh + a short README. See feedback: optimal-workflow-artifacts.
6. Hero image is current (see the stale-image work below).

## HITL workflow (per example, one at a time)

1. Libish tunes the algorithm / canvas for the example (parameters, fixture,
   layout, colour-by-metric, captions).
2. Assistant validates LIVE in Rhino (spawn slot, deploy current plugin, open +
   solve, scan runtime messages, ViewCapture -> Read the JPG). Truth criterion is
   (c) visual validation. Never trust input-relative % - judge the seat/result
   visually.
3. Assistant captures the artifact set (metric PNG + Rhino capture + .3dm) and
   drafts/updates the example README.
4. Libish approves the canvas state (examples 01/02/04 explicitly gated on
   approval; 01/02 approved + committed 2026-07-13, 04 still pending async).
5. Commit the example + artifacts. Document before moving to the next
   (document-before-next rule).

Batching suggestion: fix P1 (one component change, unblocks 5) first; then walk
the async/data set P2/P3 (bake fixtures); then a visual-polish pass over the 65
that already solve clean (colour-by-metric + captions where missing); annotate
P4. Do the GPR family (03/08/33/34/35) together - they share the preset stack.

## Stale hero images (already registered, part of "presentable")

A prior sweep flagged 31 BAD + 29 BORDERLINE example captures for recapture (see
handoffs/HANDOFF_2026-07-07_stale_image_recapture.md and the image-quality audit
in that branch). Recapturing them is part of the public-presentable bar. Use the
headless render recipe: separate fresh slot, open_doc, scale x1000 (mm import
shrinks), Arctic/Rendered mode, CaptureToBitmap(1280x860) -> jpg -> Read.

## Reusable audit harness

Headless, no MCP-GH needed. In a spawned Rhino slot via `run_csharp`:
load Grasshopper (`RhinoApp.RunScript("_-Grasshopper _Load _Enter", false)`), then
reflect `GH_DocumentIO.Open(path)` -> `doc.Enabled=true` ->
`doc.NewSolution(true)` -> iterate `doc.Objects`, call
`RuntimeMessages(GH_RuntimeMessageLevel.Error/Warning)`, tally. Full script is in
the 2026-07-13 session transcript; it wrote scratchpad/example_audit.txt. Re-run
it after each plugin rebuild to catch regressions across all 76 in ~1 pass.

## State of the plugin (context)

- Kintsugi learned Port: FIXED this session (attention-mask soft->hard, commit
  39f3040; transformer bit-exact; reassembles in-distribution 1887 0.9deg /
  1132 2.7deg). Validated live in Rhino. TorchSharp exact path works when
  Frahan's 0.105.0 loads before LunchBox's 0.101.5; else clean fallback to the
  (now-correct) manual denoiser via a version guard (a1e2b88).
- main @ a1e2b88 on origin, builds clean. Deterministic reassembler remains the
  primary path for synthetic/quarry cuts.
