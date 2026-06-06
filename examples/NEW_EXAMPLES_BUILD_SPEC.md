# New examples build spec (2D/3D packing, Trencadís, surface, Kintsugi)

The digital-fabrication entrypoint workflows to add, with their component graph + connections + data links.
Grounded in the reference `3dpacking-test7.ghx` (parsed: Pack3D Irregular Container x4 + Sheet Pack +
Number Sliders + Boolean Toggle Run + Series/Sequence + Move/Yaw 90 + Custom Preview + Group + Scribble).
Build instructions (the MCP `.gh` save is disabled, so these are the recipes to build + Save As). Each is
HEADLESS-TESTED already via `tools/Frahan.StonePack.Harness` (`--packbench` / `--pack2dstudy`); the logic is
resilient, this wires it on a canvas. Style: short sentences, no em dashes. Follow `GRASSHOPPER_BEST_PRACTICES.md`.

## Shared conventions (from the reference .ghx)
- One coloured Group + Scribble per stage. Number Sliders for params (Spacing, Tolerance, Sort Mode,
  Rotations, Max Candidates, Seed). A Boolean Toggle (default false) on Run. Custom Preview for coloured
  output. Relay nodes to keep wires clean.

## 1. examples/10_pack2d/  (2D irregular nesting)
- Parts: a part source -> Frahan Sheet Pack (Unified) [V=0 V506, or use the exact NFP path]. Inputs:
  Parts (closed curves), Sheet Outlines (a Rectangle), Spacing 0.1, Rotations {0,90,180,270}, Sort Mode 1,
  Tolerance 0.01, Run (toggle), Max Candidates 300, Variant 0. Outputs: Packed Curves -> Custom Preview.
- Data: synthetic parts (Populate2D -> Voronoi cells) or `data/eth1100/` footprints. Headless result:
  82-89% util_stock at 0 overlap (see RESULTS).

## 2. examples/11_pack3d/  (3D irregular-container packing) -- the .ghx reference workflow
- Stones (meshes from `data/eth1100/closed/` via Read Ply Mesh / Stone Descriptor) -> Pack3D Irregular
  Container. Inputs: stones, container Box, Spacing, Run (toggle). Outputs: Placed Meshes, Pack Result ->
  Frahan Packing Report. Add Move + Yaw 90 (Series/Sequence-driven) to arrange the input pile, Custom
  Preview to colour. Mirror the reference's 4-up Pack3D layout if comparing settings.
- Data: `data/eth1100/closed/` (ETH dry-stone meshes). Headless result: Dlbf best-of-orientation 70.4%
  vol-fill; TreePackForest 37.2% guillotine-separable.

## 3. examples/12_trencadis/  (mosaic, overlap-accept)
- Parts -> Frahan Trencadís Pipeline (or Catalog Pack). NOT the plain "Trencadís Pack" (skeleton). Inputs:
  Parts, Sheet, grout offset, Run. Output: mosaic tiles -> Custom Preview. CVD-Lloyd + GVF physics.
- Data: synthetic irregular tiles. Headless result: 55.1% cov (physics on) vs 52.7% greedy.

## 4. examples/13_surface_mapping/  (pack on a surface)
- A surface (or mesh) -> Frahan surface-chart / Pack On Surface: flatten chart (BFF) -> pack 2D parts in
  chart space -> lift back to 3D via the barycentric mapper. Inputs: Surface, Parts, chart params, Run.
  Output: surface-mapped parts + Chart Flatness Report.
- Data: a Rhino surface or `data/stanford_scans/` mesh. Use Chart Flatness Report to gate distortion.

## 5. examples/14_kintsugi/  (fractured-mesh reassembly)
- Fragment meshes -> Frahan Kintsugi (rim edge-matching) or Soft ICP 3D. Inputs: fragment meshes, match
  tolerance, Run. Output: reassembled poses (transforms) -> Custom Preview. The training data +
  weights are in `src/Frahan.Kintsugi.Port/` (see `data/kintsugi/ATTRIBUTION.md`); the Breaking Bad dataset
  fetches via the extractor scripts.
- Data: fragment meshes (shatter a `data/stanford_scans/` mesh with Fragment Shatter, or Breaking Bad).

## Build + test loop (per example)
1. On the live slot, place the components (g1 bridge) per the graph above, wire, set Run=true.
2. Solve (g1_solve_graph); confirm 0 errors + a result.
3. Bake the result; capture a shaded viewport (open the baked `.3dm`, hide the previous layer, work in
   layers). Save the PNG into the example folder.
4. Save As the `.gh` (manual -- MCP save is disabled) into the example folder; add a README from this spec.
The packer LOGIC is already headless-validated (resilient); this step is the canvas wiring + capture.
