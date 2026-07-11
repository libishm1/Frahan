# Handoff — Kintsugi fracture generator fixed to Breaking Bad distribution (2026-07-11)

Fresh-session onboarding: read `AGENTS.md` first (truth criterion (c) visual
validation; HITL gates mandatory). Style: short sentences, no em dashes.

## What this session did

Task (Libish): "fix the fracture generator components so they are similar to
the original datasets, for the learned path in kintsugi"
(github.com/sgsellan/fracture-modes, github.com/Wuziyi616/multi_part_assembly,
https://breaking-bad-dataset.github.io/). Also: compare against his real
scanned objects.

Branch: `fix/kintsugi-fracture-generator` (stacked on
`fix/examples-arch-coherence`; NOT pushed, unmerged).
Full numbers: `D:\code_ws\outputs\2026-07-11\kintsugi_fracture_generator\REPORT.md`.

## Measured ground truth (new, reusable)

- **Breaking Bad everyday/val, 300 fractures** (from the local
  `pc_data.zip`, exact PuzzleFusion++ tensors): largest-piece volume share
  p25-50-75 = 0.46/0.69/0.92; extent ratio 2.5/4.0/11.5; neighbour contact
  fraction 0.042/0.108/0.273; contact deviation-from-plane/extent
  0.031/0.044/0.061; piece count mode 2, median 5, cap 20.
  -> `bb_target_stats.{py,json}`.
- **Real granite shards** (`D:\granite_shards.ply`, 10 shards): facet
  deviation/extent median 0.0045 (real granite is ~10x flatter than BB);
  extent ratio 6.5. -> `scan_shard_stats.{py,json}`.
- Benchmark conventions confirmed: 1000 pts/piece area-weighted
  (`trimesh.sample_surface`), 2-20 pieces, per-piece recenter, scalar-first
  quats. The C# port sampler matches.

## Defects fixed (both components, GUIDs unchanged, inputs APPENDED)

`FragmentShatterComponent`:
1. Jittered-grid Voronoi = equal cells (largest share 0.13 vs BB 0.69).
   NEW inputs `Impact Bias` (Ib, default 0.9; 0 = bit-identical legacy) +
   `Impact Point` (Ip, optional; auto surface point from Seed). Half-normal
   seed clustering, sigma = diag*(0.07+0.28*(1-bias)).
2. Report prints volume shares + BB target row; warns above 20 fragments.

`FractureRoughenComponent` (Cap Cuts path):
3. **FillHoles declines the large multi-plane rim loops** -> fragments were
   NEVER closed; Mode=Port sampled zero fracture-surface points. Fix:
   centroid-fan cap of remaining holes (`FanCapRemainingHoles`).
4. **FillHoles DUPLICATES rim vertices** (cap only position-welded).
   Any per-vertex treatment splits the seam into two naked loops. Fix:
   `CombineIdentical` weld after capping, before refinement.
5. **Ear-clip caps have no interior vertices** -> displacing "the cap" moved
   only the rim; fracture surfaces stayed dead flat (the exact
   out-of-distribution defect). Fix: conforming midpoint subdivision of
   interior cap edges to resolve the finest noise octave (rim edges shared
   with skin never split; 20k vertex budget). NEW input `Cut Resolution`
   (Cr, 0=auto, negative=legacy).
6. **Skin distortion**: NEW input `Rim Taper` (Rt, default 0.04): rim
   displacement projected onto the skin tangent plane (crack line wiggles,
   silhouette preserved), blending to full 3D in the cap interior.
7. Amplitude default 0.02 -> 0.05 (toward BB band; granite look = 0.006,
   documented in hover).

## Live validation (deployed .gha, headless GH solve in MCP slot)

- All fragments now CLOSED (5/5); legacy settings also close (8/8).
- largest share 0.49 (BB band 0.46-0.92) vs old 0.155.
- contact fraction median 0.083-0.099 (BB 0.042-0.273) vs ~0 before (open cuts).
- planarity 0.020-0.024 vs BB 0.031-0.061: residual gap = BB's curved
  thin-wall vessels; solid block cuts are honestly flatter (real granite
  0.0045). Frequency/amplitude sweeps barely move it; do not chase.
- Visual: `fragments_new_assembled_exploded.jpg` + `fragments_new_v001.3dm`.
- Verifier scores (PF++ port, kintsugi.bin, 20 steps): see
  `port_scores.log` (bb_00008 baseline vs new vs old generator).

## Operational lessons (new)

- **ILGPU.dll was MISSING from install/plugin and Libraries** -> Mode=Port
  crashes with FileNotFoundException (GpuMatmul static fields reference
  ILGPU types). Copied from GH bin Release to BOTH. deploy.ps1 copies
  install/plugin/* so the bundle is now correct. Add to deploy checklist.
- Headless GH component solve pattern: `new GH_Document { Enabled = true }`
  (default Enabled=false solves nothing), `AddObject`, set inputs via
  `PersistentData.Clear()` THEN `Append` (Append alone duplicates the
  registered default -> component solves twice per item), `NewSolution(true)`.
- run_csharp scripts that load Frahan.Kintsugi.Port outside GH need an
  `AppDomain.AssemblyResolve` hook to the Libraries folder.
- Spawned-slot Rhino can survive `close_slot` (stuck process holds the .gha
  file lock); kill before deploy.

## Resume points

1. Read `port_scores.log`; if the new generator's true-neighbour pairs score
   below the BB baseline meaningfully, iterate amplitude/bias with the sweep
   scripts (all knobs are component INPUTS; no rebuild needed).
2. Final deploy of the tuned-defaults build (built, NOT yet deployed:
   deploy needs all Rhino closed; slot may still be running inference).
3. HITL: Libish reviews `fragments_new_assembled_exploded.jpg` + the .3dm +
   example 14 with the new components (canvas revalidation per
   HANDOFF_2026-07-07_examples_validation.md).
4. Commit: 2 component files + this handoff on
   `fix/kintsugi-fracture-generator` (<=5 files, no push without HITL).
   `D:\code_ws\outputs\2026-07-11\` artifacts uncommitted (HITL, >5 files).
5. Consider: regenerate docs/components entries for the two components
   (descriptions changed); example 14 README note about Impact Bias.
