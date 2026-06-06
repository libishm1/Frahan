# HILT master-spine regeneration recipe

The exact, resumable procedure to refresh each master-spine `.gh` on a live Rhino + Grasshopper slot, so
every example is data-linked, grouped/scribbled, and capture-validated. Style: short sentences, no em dashes.

## Prerequisite (the current block)
The Grasshopper MCP load/save listener must be UP. At packaging time it was down (WinError 10061): the
in-Rhino g1 bridge can BUILD canvases (place/wire/solve) but cannot WRITE `.gh` files; `.gh` load/save needs
the grasshopper MCP (`mcp__grasshopper__load_document` / `save_document`). To unblock: in the live Rhino,
start the Grasshopper MCP listener (the GH-side server component), then confirm
`mcp__grasshopper__get_document_info` returns a document. The rhino g1 bridge separately needs a component
on the canvas to anchor the document (place a Panel first if `g1_get_canvas_graph` says "Could not get GH
document").

## Per-workflow loop (gated heavy nodes, one deliberate solve)
For each `examples/<workflow>/<name>.gh`:
1. LOAD: `mcp__grasshopper__load_document(path=<name>.gh)`.
2. REPATH: set each external data-input node to the `data/<set>/` link from `README.md`. Make a COPY first
   if editing would touch a path other examples share.
3. GROUPS + SCRIBBLES: ensure one coloured group box per stage (0 COVER / 1 INGEST / 2 BENCH / 3 FRACTURE /
   4 PACK-CUT / 5 REPORT) with a scribble title, per `GRASSHOPPER_BEST_PRACTICES.md`. Add the COVER panel
   (site / scan file / date / CRS / units / version).
4. PARAMS: set sane defaults, scale-clamped ranges; default-FALSE `Run` toggles on heavy nodes.
5. SOLVE ONCE: press the stage Run toggles in order; do a SINGLE deliberate solve per stage (avoids the
   300 s MCP-cap trap, KB-2). Confirm no errors; every null output should warn with the missing input.
6. CAPTURE: bake the result, save the `.3dm`, and capture a shaded viewport PNG into the workflow folder.
7. SAVE: `mcp__grasshopper__save_document(path=<name>.gh)`.
8. CHECKPOINT: one line per workflow in `../outputs/.../CHECKPOINT_MIGRATION.jsonl`; commit + push.

## Order
engineer (`04_scan_to_bench_engineer`) -> artist (`05_artist_pointing_machine`) ->
geologist (`03_gpr_fracture_granite`) -> masonry (`01_quarry_to_wall`, `02_masonry_assembly`) ->
fracture/quarry (`03_quarry_to_slabs`) -> ingest (`07_scan_ingest_full`).

## Acceptance (the contributor bar)
A workflow is done when: it opens on a fresh machine with the `.gha` deployed, its data input resolves to
`data/<set>/`, pressing Run in order produces a visible result with 0 errors, and the captured `.3dm` + PNG
match the benchmarked output. Truth criterion (c): seen in Rhino, not just built.
