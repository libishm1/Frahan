# Handoff - Live Grasshopper example building (MCP-driven)

Last updated: 2026-06-06. Author: autonomous session on slot `aardvark` (official Rhino MCP, Rhino 8).
Scope: how the master-spine example `.gh` files are built, solved, captured, and saved LIVE on a running
Rhino canvas via the MCP bridge, plus the hard-won bridge quirks. Read this before building more examples.
Style: short sentences, no em dashes.

## TL;DR
We can now build a Grasshopper definition end to end without a human touching the canvas: place
components, wire them, set values, solve, bake the result to Rhino layers, capture a shaded PNG, and save
a clean `.gh` to disk. The MCP `save_document` is disabled, but `GH_DocumentIO.SaveQuiet` via `run_python`
works and is the save path we use.

## The bridge (slot aardvark)
- Official Rhino MCP. The `g1_*` tools drive the active GH1 canvas. `run_python` runs CPython against the
  live doc (`__rhino_doc__` is the RhinoDoc; `Grasshopper.Instances.ActiveCanvas.Document` is the GH doc).
- A `Grasshopper MCP` component + its `Boolean Toggle` (Enabled) live on the canvas; that is the bridge.
  Do NOT remove them from the live doc or you lose control. We strip them only from the SAVED copy.

## CRITICAL bridge quirks (these cost real time)
1. ARRAY-PARAM TOOLS ARE BROKEN on this router build. `g1_apply_graph` and `g1_connect_many` both fail
   with `JsonException: ... could not be converted to ...SliderSpec[] / WireSpec[]. Path: $`. The array
   argument reaches the C# side as a string and will not deserialize. WORKAROUND: use the scalar tools
   only: `g1_place_slider`, `g1_place_component`, `g1_connect` (one wire per call). These take scalar
   params and work reliably. Batch many `g1_connect` calls in one assistant message (they serialize on
   the GH UI thread).
2. MCP `save_document` (jingcheng grasshopper MCP) is DISABLED ("API compatibility"). The `g1` bridge has
   no file I/O. SAVE PATH = `run_python` + `GH_DocumentIO(doc).SaveQuiet(path)` -> returns True, writes a
   real `.gh`. Confirmed working.
3. Wiring INTO a `Panel` via `g1_connect` is rejected ("destination '' is a value source, not an input").
   Wire panels via `run_python`: `panel.AddSource(sourceParam); panel.ExpireSolution(True)`.
4. `Boolean Toggle` is an ambiguous name (two GUIDs). Place via GUID `2e78987b-9dfb-42a2-8b76-3923ac8bd91a`.
5. There is no g1 tool to set a slider/toggle VALUE. Use `run_python`:
   - toggle: `obj.Value = True; obj.ExpireSolution(False)` for `GH_BooleanToggle`.
   - slider: `slider.SetSliderValue(System.Decimal(float(v)))` (pass a python float for float sliders,
     int for int sliders; `Decimal(str(v))` picks the wrong overload and throws Currency error).
6. `get_viewport_image` returns a large inline blob. Prefer capturing to a file with
   `view.CaptureToBitmap(Size).Save(path)` then `Read` the PNG file to validate (truth criterion c).

## The live-build recipe (per example)
1. Confirm slot: `list_slots` -> aardvark. `g1_get_canvas_graph` to see current state.
2. `g1_describe_component` every Frahan component first to get exact input/output names.
3. Place sliders (`g1_place_slider`, solve:false) - capture returned GUIDs.
4. Place components (`g1_place_component`, solve:false) - capture GUIDs. Use the component GUID as the
   selector for Frahan components (names can be ambiguous).
5. Wire with `g1_connect` (scalar), solve:false, batched. Slider/toggle source selector = "" (pure param).
6. Set toggle/slider values via `run_python`. `g1_solve_graph`.
7. Validate: `g1_get_canvas_graph` (include_data:true) -> check the component Report/Info output and
   RuntimeMessages (no errors/warnings). Read the actual numbers.
8. Bake the result output to a NEW Rhino layer (read `param.VolatileData.AllData(True)`,
   `it.ScriptVariable()` gives the Rhino geometry). Hide all other layers, hide GH preview
   (`o.Hidden=True` on every object), set a perspective Shaded camera framed on the bbox, capture PNG.
   `Read` the PNG to confirm it looks right. Re-tune sliders if the visual is poor.
9. Save the result `.3dm` with `File3dm()` + type-specific adds (`f.Objects.AddBrep`, `AddCurve`,
   `AddMesh`); generic `f.Objects.Add(geo, attr)` throws "an integer is required" on this build.
10. Re-enable GH preview (`o.Hidden=False`). Add a Group (`GH_Group`, `CreateAttributes`, `AddObject(guid)`,
    `Colour`, `NickName`). (Scribble: `GH_Scribble` exists but has no `ExpireCaches` here; Group is enough.)
11. CLEAN SAVE: `newdoc = GH_Document.DuplicateDocument(srcDoc)` (STATIC, takes the source);
    remove the `Grasshopper MCP` component + its Enabled-source toggle from `newdoc`
    (`newdoc.RemoveObject(o, False)`); `GH_DocumentIO(newdoc).SaveQuiet(path)`. This yields a pristine
    `.gh` with no missing-component placeholder for collaborators.
12. Write the example `README.md` (problem, component + citation, inputs, data, run steps, best practices).

## Example status (examples/)
- `11_pack3d` DONE. Block Pack (Tree) (Kim 2025 guillotine). 12/12 packed, all-packed=True, score 65.11.
  Files: `11_pack3d_block_pack.gh`, `11_pack3d_result.3dm`, `11_pack3d_result.png`, `README.md`.
  NOTE: we chose Block Pack (Tree) over the legacy `Pack3D Irregular Container` (b3e8a42f) from
  `3dpacking-test7.ghx`. The legacy heightmap packer is the weak baseline the research replaced: with
  identical box footprints it stacks an escaping tower and ignores the box container. For the irregular
  mesh pile, feed varied real meshes (data/eth1100/closed) into the legacy component instead.
- `10_pack2d` TODO - IrregularSheetFillNfpBlf (evolved exact NFP-BLF), 82-89% util_stock at 0 overlap.
- `12_trencadis` TODO - Trencadis Pipeline / Catalog Pack (CVD-Lloyd + GVF), ~55% cov.
- `13_surface_mapping` TODO - surface chart (BFF) -> pack in chart -> lift via barycentric mapper.
- `14_kintsugi` TODO - Kintsugi rim edge-match / Soft ICP 3D on fragment meshes.
See `examples/NEW_EXAMPLES_BUILD_SPEC.md` for the per-example graph + data links.

## Don't repeat these
- Don't call `g1_apply_graph` / `g1_connect_many` (array bug). Scalar tools only.
- Don't `g1_clear_canvas` (it removes the MCP bridge too). Remove non-MCP objects with `run_python`
  `RemoveObjects` if you need a reset, keeping the `Grasshopper MCP` component + toggle.
- Don't internalize multi-million-vertex meshes in a saved `.gh` (KB-1). Reference externally.
