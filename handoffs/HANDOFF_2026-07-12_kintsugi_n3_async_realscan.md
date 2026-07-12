# Handoff — Kintsugi N-fragment reassembly SOLVED + async + real-scan lane opened (2026-07-12)

Fresh-session onboarding: read `AGENTS.md` first (truth criterion (c)
visual validation; HITL gates mandatory). Style: short sentences, no em
dashes. Previous handoff: `HANDOFF_2026-07-11_fracture_generator.md`.

Branch: `fix/kintsugi-fracture-generator`, pushed through `18762a1`
(stacks on fix/examples-arch-coherence -> fix/cloud-icp-async PR chain).
Evidence + research: `D:\code_ws\outputs\2026-07-12\kintsugi_n3plus\`
(committed to code_ws @ 305bd383 on Libish's go).

## HEADLINE

The deterministic synthetic Kintsugi loop WORKS at every tested N and
ships as a Grasshopper example. Sweep N=2/3/5/8: ALL fragments CORRECT
at 0.0% pose error (anchor-aligned RMS, threshold 3% of diag), ~1 s per
solve, async, cold-verified from the saved canvases.

## What was done (commit order)

1. `950d75c` — N>=3 FIX. Root cause was NOT salt quantisation:
   fragment-local capping can NEVER mate-correspond, because sequential
   Voronoi cell clipping tessellates the shared rim curve differently
   per fragment (measured: only ~half the rim points coincide at N>=3;
   three per-fragment rewrites all failed, mates 10-27 rms apart).
   Fracture Roughen v4 = PAIR-BASED interface surfaces: each interface
   is built ONCE per fragment pair from the lower-index fragment's rim
   sampling (exclusive nearest-rim ownership -> arcs -> nearest-endpoint
   polygon closure -> centroid fan -> conforming refinement -> pair-
   salted displacement, boundary PINNED), then the SAME mesh is stitched
   into both fragments: owner welds by rim index, partner gets the full
   copy plus a near-degenerate rail-stitch zip onto its own rim, then
   CombineIdentical + CullDegenerateFaces + pinned fan-fill of leftover
   junction slits. Pieces go to BOTH branches of the Fracture Surfaces
   tree and are oriented OUTWARD per fragment (IsPointInside vote) so
   mates carry OPPOSING normals — identical normals had silently killed
   every true pair at Facet Match's opposition gate. Also in this
   commit: Scramble Fragments gained an optional Regions tree input (5)
   and Scrambled Regions output (2); Facet Match SKIPS Soft ICP in
   regions mode (the contact-only polish dragged CORRECT poses 13-27%
   of diag away; it stays available for the scan path).
2. `36763ce` — Facet Match is ASYNC (AsyncScanComponent run-gate base,
   owned deep-copied snapshot, progress via Message, Run=false cancels).
   Same poses as the sync run; first solve returns in under a second.
3. `dab3b5d` — bench canvas `14_kintsugi_rims_facets_bench.gh`: regions
   wire folded in (Roughen.Fs -> Scramble.Rg; Scramble.SRg ->
   FacetMatch.Fr), cold-verified 5/5 CORRECT.
4. `5a4bb5a` — previous handoff closed out.
5. `286060c` — KB-12 filed (see below).
6. `18762a1` — NEW EXAMPLE `14_kintsugi_facet_reassembly.gh`: the
   verified loop as a self-presenting canvas (3 coloured groups,
   sliders, dusty-red scrambled vs gold assembled previews, Report
   panel, toggles ship FALSE). Cold-verified from the saved file:
   5/5 CORRECT. PNG + result .3dm baked, README updated.

## KB-12 (OPEN): Facet Match scan path crashes Rhino on real scan shells

Real granite shards (random construction DEBRIS per Libish — an even
cleaner negative control, no true mates exist) crash the component ~20 s
into background segmentation: native access violation, 4x reproduced,
process death. Mesh.Reduce also silently refuses these shells. The
async harness behaved correctly up to the native crash. FIX PLAN in
KNOWN_BUGS.md KB-12: input sanitation pre-pass (CullDegenerateFaces,
CombineIdentical, Compact, non-manifold reject-with-warning, ~30k face
cap with decimate-first warning), then bisect the exact native call
with per-stage file checkpoints on shard 0. Extracted shards (floor
RANSAC + split, mirrors scan_shard_stats.py): outputs/2026-07-12/
kintsugi_n3plus/granite_shards_extracted.3dm. Negative-control status:
BLOCKED-BY-BUG, no false positives emitted.

## Research pack (one-sided scans of broken shards)

`RESEARCH_BRIEF_kintsugi_deterministic.md` + `research/*.md` (three
sonnet-agent surveys + license check), all in the outputs folder:
- No published method solves one-sided deterministic fracture matching;
  GARF (ICCV 2025) still fails on stone. Genuinely open problem.
- Proposed composite gate for open meshes: scan-ray free-space reject
  (space carving) + trimmed-overlap contact band + spectral contact-band
  correlation (Thompson 2024, 38/38 on real fractures). All classical,
  C#-friendly, deterministic.
- Do NOT scan-complete before matching: completion hallucinates
  fracture relief (DeepMend/Jigsaw++ evidence); every learned system
  runs completion AFTER pose estimation.
- Cheapest global-assembly upgrade as N grows: deterministic
  cycle-consistency check over committed pairwise poses.
- Heritage lesson (fresco/pottery): combine cheap independent cues
  (break-curve, thickness, colour), do not rely on the fracture face
  alone. Pottery = thin-sheet regime (break STRIPE), different from
  stone; fine as first real positive, not the final granite verdict.
- Datasets: Fractura/GARF and Fantastic Breaks have NO data license
  (code GPLv3 only). Written author permission needed before
  commercial-context evaluation. Self-owned data is the primary
  testbed.

## Evaluation plan (agreed with Libish)

1. KB-12 sanitation fix -> rerun granite DEBRIS negative control
   (expect: 0 placed beyond anchor).
2. Libish scans his BROKEN POTTERY: 30-60 photos per sherd, turntable,
   diffuse light (cross-polarize or spray if glazed) -> Meshroom
   (installed, D:\workspace\*.mg; Depth Anything v3 is NOT on disk and
   single-photo monocular depth hallucinates at fracture-relief scale
   — use only as a labeled one-sided ablation source). Two-sided
   reconstruction first, verify the scan path, THEN hemisphere-cull for
   the one-sided ablation.
3. Fractura lithics / RePAIR only after license permission.

## Resume points

1. KB-12 sanitation pre-pass in FacetMatchComponent, rerun negative
   control on granite_shards_extracted.3dm.
2. Pottery photos from Libish -> Meshroom pipeline -> scan path
   calibration (dihedral segmentation on real fracture surfaces) with
   gate-log-driven iteration.
3. Group B (rim path) recall on the bench canvas is still open.
4. Examples 01/02/04 still await Libish's canvas approval (uncommitted).
5. Slot quirks learned: GH_DocumentIO.Open can stall >300 s transiently
   (retry works; archive-level GH_Archive read is the stall-free way to
   inspect); PLY import needs the scripted dash form (`_-Import`) or it
   pops a modal that hangs headless slots.
