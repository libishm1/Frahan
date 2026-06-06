# Example 11 - 3D block packing (saw-cuttable guillotine)

> **Scale, units, position (corrected 2026-06-06):** METERS. Container = one quarry dimension block 3.0 x 1.5 x 1.5 m; elements 0.2-1.0 m. Block base sits on the z=0 bed plane (not straddling). Tolerance 1 mm, wire-saw Kerf 8 mm (50 mm for primary-quarry yield accounting). Packer = Block Pack (Tree) / DLBF (no cell-size grid; levers are Forests + Kerf). See `../../wiki/research/tolerances_dimensions_slm_roses.md`.


Pack final-piece cuboids into a stone-block container with axis-aligned, saw-cuttable guillotine cuts.
This is the digital-fabrication entrypoint for quarry block subdivision and monument nesting. Style:
short sentences, no em dashes.

![3D block packing result](11_pack3d_result.png)

## What it shows
12 element boxes packed into one container block. Every placement is reachable by straight saw cuts
(guillotine), so the plan is directly fabricable on a wire or bridge saw. The packer picks the cheapest
container subset that fits all elements, and falls back to highest packed value when a full pack is
infeasible.

Measured live result (this definition, as shipped):
`Packed 12/12 elements into 1 of 1 containers; score=65.11; all-packed=True; forests=400; seed=0; kerf=0`.

## Files
- `11_pack3d_block_pack.gh` - the canvas (built and solved live in Rhino 8, MCP bridge stripped).
- `11_pack3d_result.3dm` - baked result: placed blocks (`11_Pack3D_BlockPack`) inside the container
  wireframe (`11_Pack3D_container`).
- `11_pack3d_result.png` - shaded viewport capture of the baked result.

## Component
`Block Pack (Tree)` (Frahan > Masonry). Frahan port of Kim 2025 (Computation 13:211, CC BY 4.0).
Three Frahan extensions beyond the paper: deterministic seed, saw kerf width, and per-container
forbidden boxes (for fracture-aware containers, closing Kim 8.2).

Why this packer and not the legacy `Pack3D Irregular Container` (the original `3dpacking-test7.ghx`
reference): the legacy heightmap packer is the weak baseline the research replaced (it stacks identical
footprints into a tower and does not enforce a box container). `Block Pack (Tree)` is correct by
construction: placed boxes are computed to nest inside the container with saw-cuttable cuts. For the
irregular-mesh pile workflow, feed varied real meshes (e.g. `data/eth1100/closed/`) into the legacy
component instead of synthetic identical boxes.

## Inputs (sliders, left to right top group)
- Element generation: `ElemCount` (12), `ElemX0/ElemXstep` and `ElemY0/ElemYstep` (Series-driven varied
  footprints), `ElemZ` (height half-extent). Center Box uses half-extents, so full size = 2x the slider.
- Container: `ContX/ContY/ContZ` (half-extents -> full 40x40x16 here).
- Solver: `RotMode` (2 = ThreeAxis), `Forests` (400 randomised forests), `Seed` (0 deterministic),
  `Kerf` (saw kerf width, model units), `ContPrice`, `Zero` (feeds Cut Surface Weight / Max Parallelism /
  Memory Budget = 0, the Kim-2025 defaults).

## Data
Synthetic varied boxes (no external file, so it solves on first open). For a real job, replace the
element source with your final-piece bounding boxes and the container with your quarry block AABB
(or wire a `QuarryBlock` from `Scan to Block Inventory` into the `Block` input).

## Run
1. Deploy `Frahan.StonePack.gha` (Rhino closed). Open the `.gh`.
2. It solves on open (no Run gate; `Block Pack (Tree)` is fast). Read the `Report` panel.
3. Tune `Forests` up for harder instances (score plateaus by f ~ 50-1000 on small jobs).

## Best practices
Per `../GRASSHOPPER_BEST_PRACTICES.md`: one coloured Group per stage, Number Sliders for params,
deterministic seed. Results are pre-baked into the `.3dm` and `.png` so reviewers see the outcome without
running Grasshopper. Headless-validated via `tools/Frahan.StonePack.Harness --packbench`.
