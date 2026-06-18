# Handoff — 2026-06-18 — quarry kriging fix, cladding examples, Panel Tile Surface, Slab Trim research

Session on Frahan StonePack (repo `libishm1/Frahan`, branch `main`). Everything below is **pushed to
main** unless flagged WIP.

## Shipped + pushed this session

**GPR kriging fix** (`GprFractureSurface3DComponent.cs`) — the deep bed no longer encroaches the centre:
- k-PLANES bed separation (depth k-means was mixing dipping beds = "kriging between layers").
- Regression kriging: least-squares dip plane + smoothed residual (nugget 0.15) -> dip exact, sheet smooth.
- 2-sigma outlier clip per bed.
- HULL-MASK: draw each bed only inside the convex hull of its own picks (stops extrapolation; verified
  0 inter-bed overlaps, middle-bed roughness 0.56 -> 0.15 m).

**New components**
- **Construct GPR Preset** (`Quarry/GprPresetComponents.cs`, GUID A7E0B0F6) + `GprPresetGoo` + a **Custom
  Preset** input on GPR Survey Grid: define velocity/freq/eps_r/continuity for any stone. (Botticino =
  limestone; use `marble_600`, v=0.10, empirically tuned on this survey.)
- **Panel Tile Surface** (`Quarry/PanelTileSurfaceComponent.cs`, GUID A7E0B0F7, tab Surface Packing):
  discretize a freeform facade into PLANAR cladding panels; outputs Panels (3D), Cut Tiles (flat, for
  nesting), Planarity (corner deviation = facet warning), Area. Validated 84 panels / 37 mm planarity.
- GPR Survey Grid: bidirectional LA/TA grid layout (auto axis from filename).

**Examples** (all self-presenting .gh + hero + README)
- **35 gpr_quarry_full_workflow** — ingest -> beds -> slabs -> 511 staged-guillotine blocks (replaced the
  old surfaces-only 35). Validated on canvas.
- **36 fractured_block_to_slabs** — block + bedding fracture -> 2 fracture-bounded slabs.
- **37 block_to_cladding_facade** — block -> gangsaw slabs -> Sheet Nest cladding panels -> curved facade,
  with EUR costing (~EUR 69/m2). .gh does the slab->panel-nest step; headless twin does the full chain.
- **38 surface_discretize_tiles** — Panel Tile Surface on a doubly-curved facade -> cut tiles -> Sheet
  Nest. Default facade is a placeholder patch; user will internalize their own surface in the `Facade` param.

**Preprint embedded** — Research Square DOI **10.21203/rs.3.rs-10035624/v1** (Murugesan 2026, "A managed,
uncertainty-aware pipeline from GPR to dimension-stone block yield in fractured quarries") in the main
README (cite + hero caption) and example READMEs 08/33/34/35.

Key commits: 4c74e2e (GPR + Construct Preset + ex35), c4005fb (preprint + ex36), d5f4ab1 (ex37),
0e898ad + d2b9122 (Panel Tile Surface + ex38 + gha).

## WIP — Slab Trim (Greedy Convex Hull) — NOT yet built/tested

Practical request from the Krishnagiri (Tamil Nadu) granite factory: trim an irregular scanned slab to a
usable convex blank with the FEWEST straight tangential wire-cuts (least time). Anchored on ASME
**IDETC2026-193983** "Trimming Stone Using Greedy Algorithm and a Convex Hull of a Discrete Geometry".

- **Research dossier DONE:** `research/slab_trim_greedy_convex_hull.md` (deep-research, 104 agents, verified).
  Key facts: (1) yield vs value are distinct objectives (expose both); (2) exact target = potato-peeling /
  convex-skull (largest inscribed convex polygon) = OPPOSITE of the circumscribing hull, O(n^7) exact
  (impractical), (1-eps) near-linear approx exists (Cabello 2017); (3) "fewest cuts within tolerance" =
  Imai-Iri, solvable OPTIMALLY via shortcut-DAG shortest path (greedy is just the fast heuristic);
  (4) min cut-LENGTH = guillotine "cutting out polygons" (target must be convex); (5) implementable
  Rhino-free (MIConvexHull MIT, or native hull, + Clipper2 / Sutherland-Hodgman).
- **Core WRITTEN (C#, Rhino-free), UNTESTED:** `tools/Frahan.StonePack.Harness/SlabTrimProfile.cs` — a
  `--slabtrim` harness mode with: a synthetic granite-slab blob generator, monotone-chain convex hull,
  greedy convex trim via half-plane (Sutherland-Hodgman) clips at the deepest reflex vertex, kerf, and
  metrics (cut count, recovered-area %, total cut length). Dumps blob/hull/cuts/trimmed CSVs for a
  matplotlib render.
- **TODO next:** (a) add the `--slabtrim` dispatch to `Harness/Program.cs` Main (mirror `--gpr`); build +
  run + matplotlib-validate the greedy trim; iterate the cut rule if it does not converge to a clean
  convex subset. (b) Then wrap the validated algorithm in a `Slab Trim (Greedy Convex Hull)` GH component
  (inputs: boundary curve, target mode, max cuts/tolerance, kerf; outputs: cut lines, trimmed polygon,
  recovered-area %, cut length, cut count) + a small example. Skip the heavy MCP/Grasshopper validation
  to save tokens; rely on the headless harness + matplotlib (the proven reliable path this session).

## Update (end of session) — Slab Trim modes + concave nesting DONE

- **Slab Trim core: BOTH target modes** implemented + validated headless (`--slabtrim` runs both):
  convex blank (5 cuts, 90.3% yield) + **concave kerf-follow** (Imai-Iri min-# shortcut-DAG, 11 cuts,
  96.5% yield, eps=0.06). `research/slab_trim_modes.jpg`. Commit f16a0bf.
- **Example 39 (concave_nest)**: concave-in-concave nesting on **Sheet Nest (Hole-Aware)**, validated on
  canvas: **6/6 placed, density 0.523, Valid, ~2.3 s** + headless raster twin (52% fill). Commit f780131.
- The **trim trio** is complete and pushed: convex blank | concave kerf-follow | concave-in-concave nesting.
- **Math derivations of all session algorithms in LaTeX** (for later Lean formalization):
  `D:/code_ws/proofs/frahan_algorithm_derivations.tex` (definitions + theorems + proof-sketches:
  regression kriging, k-planes, hull mask, best-fit-plane planarity, Sutherland-Hodgman convex trim,
  Imai-Iri min-#, NFP/raster nesting).
- STILL TODO (deferred, tokens): wrap the validated Slab Trim core into a `Slab Trim (Greedy Convex Hull)`
  GH component with the target-mode toggle {convex blank | concave kerf-follow}. Mechanical port.

## Audit context (from the quarry/slab tab audit)
- GREEN (validated, has example): the GPR->blocks spine (Survey Grid, Surfaces 3D, Fracture Bounded Slabs,
  Fracture Block Pack, Bed Block Layout, Construct Preset, Panel Tile Surface, planning chain).
- RED (compile, no canvas example): BlockCutOpt Solve/Omni/AMRR/Pareto, Decompose backends, fracture-plane
  generators, Slab tab CGAL utils, GeoFractNet/Photo Detect (need backends).
- 5 baked-only examples still need a real .gh: 03, 08, 23, 24, 25.

## Environment notes (hard-won)
- MCP run_csharp WEDGES/crashes when GH-compute + bake + file-write + viewport-capture are combined in one
  call, or on the first heavy call in a cold slot (native-DLL load > 300 s). The algorithm is sub-second;
  it is the canvas-over-MCP bridge + 300 s timeout. RELIABLE PATTERN: headless harness (Core) + matplotlib;
  build .gh via GH_DocumentIO.SaveQuiet (no solve) which never crashed; do bake/capture in a SEPARATE call
  or skip it. Reading the LIVE ActiveCanvas doc triggers a re-solve -> use a DETACHED GH_Document.
- gha deploy: `cp src/.../bin/Release/net48/Frahan.StonePack.gha "$APPDATA/Grasshopper/Libraries/"` — fails
  if Rhino is open (file lock). Remember to also refresh `install/plugin/Frahan.StonePack.gha` (LFS) before
  committing, not just the Libraries copy.

## Related work to cite
ReWeave (MRAC IAAC): reclaimed-material -> representation -> packing taxonomy; stone sits at polygon->ML
and voxels->heuristic. 1.0 custom nester (bbox + longest-straight-edge orientation) after OpenNest/DeepNest
limits; 2.0 scan->nest->robotic geopolymer bonding; 3.0 robotic textile upcycling. blog.iaac.net/reweave/.
